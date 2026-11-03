using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace HandheldLauncher.App.Views;

public partial class ArtworkDownloadPopupWindow : Window
{
    public event EventHandler? CancelRequested;
    public event EventHandler? PauseToggled;

    private bool _allowProgrammaticClose;
    private bool _isPaused;
    private readonly DispatcherTimer _spinnerTimer;

    public ArtworkDownloadPopupWindow()
    {
        InitializeComponent();

        // Spinner circular animado: rotação contínua do anel
        SpinnerRing.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        SpinnerRing.RenderTransform = new RotateTransform();
        _spinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _spinnerTimer.Tick += (_, _) =>
        {
            if (SpinnerRing.RenderTransform is RotateTransform rt)
                rt.Angle = (rt.Angle + 6) % 360;
        };
        _spinnerTimer.Start();

        PauseButton.Click += (_, _) => TogglePause();
        CancelButton.Click += (_, _) => RequestCancel();
    }

    private void TogglePause()
    {
        _isPaused = !_isPaused;
        PauseButton.Content = _isPaused ? "▶" : "⏸";
        if (_isPaused) _spinnerTimer.Stop();
        else _spinnerTimer.Start();
        PauseToggled?.Invoke(this, EventArgs.Empty);
    }

    public void Configure(int total)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ProgressText.Text = $"0 / {total}";
            CurrentGameText.Text = "";
        });
    }

    public void UpdateProgress(int current, int total, string? currentTitle)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ProgressText.Text = $"{current} / {total}";
            CurrentGameText.Text = currentTitle ?? "";
        });
    }

    public void MarkCancelling()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ProgressText.Text = "...";
            CurrentGameText.Text = "Cancelando";
            PauseButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            _spinnerTimer.Stop();
        });
    }

    public void CloseForReal()
    {
        _allowProgrammaticClose = true;
        _spinnerTimer.Stop();
        Close();
    }

    private void RequestCancel()
    {
        MarkCancelling();
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_allowProgrammaticClose)
        {
            RequestCancel();
            e.Cancel = true;
        }
        base.OnClosing(e);
    }
}
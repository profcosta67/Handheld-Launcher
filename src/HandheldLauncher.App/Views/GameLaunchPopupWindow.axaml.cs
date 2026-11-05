using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace HandheldLauncher.App.Views;

public partial class GameLaunchPopupWindow : Window
{
    private readonly DispatcherTimer _spinnerTimer;
    private readonly string[] _spinnerFrames = { "◐", "◓", "◑", "◒" };
    private int _spinnerFrame;
    public GameLaunchPopupWindow() : this("", null) { }

    public void UpdateIcon(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath) || !System.IO.File.Exists(iconPath)) return;
        try
        {
            GameIcon.Source = new Bitmap(iconPath);
            GameIcon.IsVisible = true;
            TxtIconFallback.IsVisible = false;
        }
        catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
    }

    public GameLaunchPopupWindow(string title, string? iconPath)
    {
        InitializeComponent();

        _spinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        _spinnerTimer.Tick += (_, _) =>
        {
            _spinnerFrame = (_spinnerFrame + 1) % _spinnerFrames.Length;
            TxtLoadingSpinner.Text = _spinnerFrames[_spinnerFrame];
        };
        _spinnerTimer.Start();
        Closed += (_, _) => _spinnerTimer.Stop();

        TxtGameTitle.Text = string.IsNullOrEmpty(title) ? "Abrindo..." : title;

        bool iconLoaded = false;
        if (!string.IsNullOrEmpty(iconPath) && System.IO.File.Exists(iconPath))
        {
            try { GameIcon.Source = new Bitmap(iconPath); iconLoaded = true; }
            catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
        }

        // Fallback: mostra a inicial do jogo se o ícone falhar.
        // Usa FindControl pra não quebrar caso o XAML ainda não tenha TxtIconFallback.
        if (!iconLoaded && !string.IsNullOrWhiteSpace(title))
        {
            var fallback = this.FindControl<TextBlock>("TxtIconFallback");
            if (fallback != null)
            {
                var trimmed = title.TrimStart();
                if (trimmed.Length > 0)
                {
                    fallback.Text = trimmed[0].ToString().ToUpperInvariant();
                    fallback.IsVisible = true;
                    GameIcon.IsVisible = false;
                }
            }
        }
    }
}
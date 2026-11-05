using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace HandheldLauncher.App.Views;

public partial class HealthAlertPopupWindow : Window
{
    private readonly DispatcherTimer _autoClose = new() { Interval = TimeSpan.FromSeconds(7) };

    public HealthAlertPopupWindow() : this("", "") { }

    public HealthAlertPopupWindow(string title, string message)
    {
        InitializeComponent();
        TxtTitle.Text = "⚠ " + title;
        TxtMessage.Text = message;
        _autoClose.Tick += (_, _) => { _autoClose.Stop(); Close(); };
        Opened += (_, _) => _autoClose.Start();
        Closed += (_, _) => _autoClose.Stop();
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e) => Close();
}

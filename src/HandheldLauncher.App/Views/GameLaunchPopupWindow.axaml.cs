using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace HandheldLauncher.App.Views;

public partial class GameLaunchPopupWindow : Window
{
    public GameLaunchPopupWindow() : this("", null) { }

    public GameLaunchPopupWindow(string title, string? iconPath)
    {
        InitializeComponent();

        TxtGameTitle.Text = string.IsNullOrEmpty(title) ? "Abrindo..." : title;

        if (!string.IsNullOrEmpty(iconPath) && System.IO.File.Exists(iconPath))
        {
            try { GameIcon.Source = new Bitmap(iconPath); }
            catch { }
        }
    }
}
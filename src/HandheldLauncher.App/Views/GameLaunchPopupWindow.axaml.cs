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

        bool iconLoaded = false;
        if (!string.IsNullOrEmpty(iconPath) && System.IO.File.Exists(iconPath))
        {
            try { GameIcon.Source = new Bitmap(iconPath); iconLoaded = true; }
            catch { }
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
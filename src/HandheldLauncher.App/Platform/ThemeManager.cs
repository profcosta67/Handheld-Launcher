using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Temas pré-prontos (v0.6.0, pedido do usuário: "modo dark, white, blue,
/// red"). Em vez de quatro arquivos de estilo, troca a COR dos pincéis que o
/// Theme.axaml já define (BackgroundBrush, AccentBrush...). Todo o XAML usa
/// esses pincéis via StaticResource — que devolve a MESMA instância — então
/// mudar a cor do pincel repinta a tela inteira na hora, sem reiniciar.
/// </summary>
public static class ThemeManager
{
    public static readonly string[] ThemeNames = ["Escuro", "Claro", "Azul", "Vermelho"];

    private sealed record Palette(
        string Background, string Surface, string SurfaceRaised,
        string Accent, string AccentSoft, string TextPrimary, string TextSecondary,
        bool IsLight);

    private static readonly Palette[] Palettes =
    [
        // Escuro (o original, violeta)
        new("#0C0D12", "#181A22", "#222531", "#7C5CFC", "#5A41C4", "#F5F5F7", "#9A9CAB", false),
        // Claro
        new("#EEF0F5", "#FFFFFF", "#E2E5EE", "#5B3FE0", "#B9AEF5", "#16171D", "#5E6272", true),
        // Azul
        new("#07101E", "#0E1B2F", "#152741", "#2F8CFF", "#1D5FB8", "#EEF4FF", "#8EA3C2", false),
        // Vermelho
        new("#130A0C", "#201114", "#2C171B", "#E5334B", "#A3243A", "#FFF1F2", "#B39497", false),
    ];

    public static void Apply(int themeIndex)
    {
        var app = Application.Current;
        if (app is null) return;

        var palette = Palettes[Math.Clamp(themeIndex, 0, Palettes.Length - 1)];

        // Variante do Fluent (TextBox/ComboBox/CheckBox das Configurações).
        app.RequestedThemeVariant = palette.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;

        SetBrush(app, "BackgroundBrush", palette.Background);
        SetBrush(app, "SurfaceBrush", palette.Surface);
        SetBrush(app, "SurfaceRaisedBrush", palette.SurfaceRaised);
        SetBrush(app, "AccentBrush", palette.Accent);
        SetBrush(app, "AccentSoftBrush", palette.AccentSoft);
        SetBrush(app, "TextPrimaryBrush", palette.TextPrimary);
        SetBrush(app, "TextSecondaryBrush", palette.TextSecondary);

        // Semitransparentes: mesma cor da superfície/fundo do tema com ~80% de opacidade.
        SetBrush(app, "BarBrush", "#CC" + palette.Surface.TrimStart('#'));
        SetBrush(app, "BackdropDimBrush", "#CC" + palette.Background.TrimStart('#'));
    }

    private static void SetBrush(Application app, string key, string hex)
    {
        try
        {
            if (app.TryGetResource(key, null, out var value) && value is SolidColorBrush brush)
            {
                brush.Color = Color.Parse(hex);
            }
        }
        catch
        {
            // Melhor esforço — tema parcial é melhor que launcher que não abre.
        }
    }
}

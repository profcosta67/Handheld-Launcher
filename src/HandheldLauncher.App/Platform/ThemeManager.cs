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
    public static readonly string[] ThemeNames =
        ["Escuro", "Claro", "Azul", "Vermelho", "Verde", "Laranja", "Rosa", "Ciano", "Preto (OLED)", "Dourado"];

    /// <summary>
    /// Fontes da interface — todas já vêm instaladas no Windows 10/11 (nada
    /// pra empacotar). Índice salvo em AppSettings.UiFont.
    /// </summary>
    public static readonly (string Name, string? Family)[] Fonts =
    [
        ("Moderna (Segoe UI)", null), // null = a padrão do Theme.axaml
        ("Divertida (Comic Sans)", "Comic Sans MS"),
        ("Formal (Georgia)", "Georgia"),
        ("Gamer (Bahnschrift)", "Bahnschrift"),
        ("Terminal (Consolas)", "Consolas"),
        ("Simples (Arial)", "Arial"),
        ("Clássica (Times New Roman)", "Times New Roman"),
        ("Legível (Verdana)", "Verdana"),
        ("Elegante (Trebuchet)", "Trebuchet MS"),
    ];

    public static readonly string[] FontSizeNames = ["Pequeno", "Médio", "Grande"];

    /// <summary>
    /// Tamanho do texto (v0.7.1): reescala os tamanhos-base do Theme.axaml.
    /// Os estilos usam DynamicResource, e Application.Resources tem
    /// prioridade sobre os recursos do Theme.axaml — então gravar aqui muda
    /// a tela inteira na hora.
    /// </summary>
    public static void ApplyFontSize(int fontSizeIndex)
    {
        var app = Application.Current;
        if (app is null) return;

        var scale = fontSizeIndex switch { 0 => 0.88, 2 => 1.18, _ => 1.0 };
        try
        {
            app.Resources["FontSizeBase"] = Math.Round(14 * scale);
            app.Resources["FontSizeBody"] = Math.Round(15 * scale);
            app.Resources["FontSizeTitle"] = Math.Round(30 * scale);
            app.Resources["FontSizeSubtle"] = Math.Round(14 * scale);
            app.Resources["FontSizeCaption"] = Math.Round(11 * scale);
            app.Resources["FontSizeMenuHeader"] = Math.Round(22 * scale);
        }
        catch
        {
            // Melhor esforço.
        }
    }

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
        // Verde
        new("#08120C", "#0F1D15", "#17291E", "#2FCB6B", "#1F8A4A", "#EEFFF4", "#8FB09C", false),
        // Laranja
        new("#140D07", "#21160D", "#2E1F13", "#FF8A2B", "#B35F1C", "#FFF5EC", "#B9A08B", false),
        // Rosa
        new("#140A12", "#21101D", "#2D1728", "#FF4FA3", "#B33574", "#FFF0F8", "#B596A9", false),
        // Ciano
        new("#061214", "#0C1E21", "#13292D", "#22D3EE", "#1590A3", "#ECFDFF", "#8DADB2", false),
        // Preto (OLED) — fundo preto de verdade (economiza bateria em tela OLED)
        new("#000000", "#0A0A0A", "#161616", "#FFFFFF", "#4A4A4A", "#FFFFFF", "#8C8C8C", false),
        // Dourado
        new("#0E0C08", "#1A160E", "#262015", "#E5B33E", "#9C7A27", "#FFF8E6", "#B3A584", false),
    ];

    /// <summary>
    /// Fonte da interface: 0 = Moderna (Segoe UI, a do próprio Windows — vem
    /// do Theme.axaml), 1 = Divertida (Comic Sans, a fonte original do
    /// projeto). Aplicada como valor local na janela, que vence o estilo.
    /// </summary>
    public static void ApplyFont(Avalonia.Controls.Window window, int uiFont)
    {
        try
        {
            var family = uiFont > 0 && uiFont < Fonts.Length ? Fonts[uiFont].Family : null;
            if (family is not null)
            {
                window.FontFamily = new FontFamily(family);
            }
            else
            {
                window.ClearValue(Avalonia.Controls.Primitives.TemplatedControl.FontFamilyProperty);
            }
        }
        catch
        {
            // Melhor esforço.
        }
    }

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
        SetBrush(app, "AccentTranslucentBrush", "#40" + palette.Accent.TrimStart('#'));
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

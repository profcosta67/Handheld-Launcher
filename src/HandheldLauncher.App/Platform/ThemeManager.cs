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
    /// <summary>
    /// Só PALETA DE COR (ver Palettes abaixo) — o LAYOUT da tela é uma
    /// opção separada desde a v0.9.19 (AppSettings.LayoutTheme, ver
    /// LayoutThemeNames). As duas são independentes de propósito: dá pra
    /// usar o layout Switch com qualquer paleta.
    /// </summary>
    public static readonly string[] ThemeNames =
        [
            "Escuro", "Claro", "Azul", "Vermelho", "Verde", "Laranja", "Rosa", "Ciano", "Preto (OLED)", "Dourado", "Grafite",
            // v0.17 (aprovadas pelo usuário a partir do comparativo de paletas)
            "Meia-noite", "Crepúsculo", "Musgo", "Névoa", "Alto Contraste",
        ];

    /// <summary>
    /// Temas de LAYOUT (v0.9.19) — o esqueleto da tela, não a cor. Índice
    /// salvo em AppSettings.LayoutTheme.
    /// </summary>
    public static readonly string[] LayoutThemeNames = ["Clássico", "Clean", "BigScreen"];

    /// <summary>
    /// Fontes da interface — todas já vêm instaladas no Windows 10/11.
    /// </summary>
    public static readonly (string Name, string? Family)[] Fonts =
    [
        ("Moderna (Segoe UI)", null),
        ("Divertida (Comic Sans)", "Comic Sans MS"),
        ("Formal (Georgia)", "Georgia"),
        ("Gamer (Bahnschrift)", "Bahnschrift"),
        ("Terminal (Consolas)", "Consolas"),
        ("Simples (Arial)", "Arial"),
        ("Clássica (Times New Roman)", "Times New Roman"),
        ("Legível (Verdana)", "Verdana"),
        ("Elegante (Trebuchet)", "Trebuchet MS"),
        // v0.16.4 (sempre acrescentar no FIM: o índice fica salvo nas configurações)
        ("Limpa (Inter)", "fonts:Inter#Inter"),
        ("Manuscrita (Ink Free)", "Ink Free"),
        // v0.17 (aprovadas pelo usuário a partir do comparativo de fontes) —
        // arquivos embutidos em Assets/Fonts/, OFL (ver FONTES-LICENCA.txt).
        ("Amigável (Manrope)", "avares://HandheldLauncher/Assets/Fonts#Manrope"),
        ("Moderninha (Outfit)", "avares://HandheldLauncher/Assets/Fonts#Outfit"),
        ("Técnica (Space Grotesk)", "avares://HandheldLauncher/Assets/Fonts#Space Grotesk"),
        ("Neutra (DM Sans)", "avares://HandheldLauncher/Assets/Fonts#DM Sans"),
        ("Equilibrada (Plus Jakarta Sans)", "avares://HandheldLauncher/Assets/Fonts#Plus Jakarta Sans"),
        ("Gamer 2 (Chakra Petch)", "avares://HandheldLauncher/Assets/Fonts#Chakra Petch"),
        ("Angulosa (Rajdhani)", "avares://HandheldLauncher/Assets/Fonts#Rajdhani"),
        ("Arredondada (Nunito)", "avares://HandheldLauncher/Assets/Fonts#Nunito"),
        ("Bolha (Fredoka)", "avares://HandheldLauncher/Assets/Fonts#Fredoka"),
    ];

    public const int FontModerna = 0, FontDivertida = 1, FontGamer = 3, FontLimpa = 9;

    // v0.17 — índices das fontes novas, usados nas sugestões de layout abaixo.
    public const int FontManrope = 11, FontOutfit = 12, FontSpaceGrotesk = 13, FontDmSans = 14,
        FontPlusJakarta = 15, FontChakraPetch2 = 16, FontRajdhani = 17, FontNunito = 18, FontFredoka2 = 19;

    /// <summary>
    /// v0.16.4 (pedido do usuário): cada layout tem um som e uma fonte que
    /// combinam. Aplicado só quando o layout é TROCADO (Configurações → Layout
    /// da tela); depois o usuário muda o que quiser em Preferências.
    /// v0.17 (aprovado pelo usuário): trocadas pelas sugestões do comparativo —
    /// Plus Jakarta Sans/Manrope/Chakra Petch combinam melhor com cada layout
    /// que as fontes de sistema usadas antes.
    /// </summary>
    public static (int Font, int Sound) LayoutDefaults(int layoutTheme) => layoutTheme switch
    {
        0 => (FontPlusJakarta, 1),   // Clássico → Plus Jakarta Sans + Toque
        1 => (FontManrope, 3),       // Clean     → Manrope + Mecânico
        2 => (FontChakraPetch2, 0),  // Moderno (BigScreen) → Chakra Petch + Console
        _ => (FontModerna, 0),
    };

    /// <summary>
    /// Reservado pro futuro tema Cartoon. v0.17: trocado de Comic Sans pra
    /// Fredoka (aprovado — a sugestão do comparativo pro layout Cartoon).
    /// </summary>
    public static readonly (int Font, int Sound) CartoonDefaults = (FontFredoka2, 2);

    public static readonly string[] FontSizeNames = ["Pequeno", "Médio", "Grande"];

    /// <summary>
    /// Tamanho do texto (v0.7.1): reescala os tamanhos-base do Theme.axaml.
    /// v0.14: também sobrescreve os recursos do tema Fluent
    /// (ControlContentThemeFontSize, BodyFontSize) para afetar ComboBox,
    /// TextBox, CheckBox e Buttons dos diálogos de Configurações — antes só
    /// o XAML do launcher era afetado, deixando o texto dos dropdowns
    /// minúsculo mesmo em "Grande".
    /// </summary>
    /// 
    
    public static void ApplyFont(string fontFamily)
{
    var app = Application.Current;
    if (app is null) return;

    try
    {
        // v0.19: Recurso global que o Quick Settings (e qualquer outra janela)
        // usa para herdar a fonte dinamicamente.
        app.Resources["GlobalFontFamily"] = new FontFamily(fontFamily);

        // Fonte padrão do tema Fluent (afeta TextBox, ComboBox, etc).
        app.Resources["ContentControlThemeFontFamily"] = new FontFamily(fontFamily);
    }
    catch
    {
        // Melhor esforço.
    }
}

public static void ApplyFontSize(int fontSizeIndex)
{
    var app = Application.Current;
    if (app is null) return;

    var scale = fontSizeIndex switch { 0 => 0.85, 2 => 1.30, _ => 1.0 };
    try
    {
        var baseSize = Math.Round(16 * scale);
        var bodySize = Math.Round(17 * scale);
        var titleSize = Math.Round(32 * scale);
        var subtleSize = Math.Round(15 * scale);
        var captionSize = Math.Round(12 * scale);
        var menuHeaderSize = Math.Round(24 * scale);

        app.Resources["FontSizeBase"] = baseSize;
        app.Resources["FontSizeBody"] = bodySize;
        app.Resources["FontSizeTitle"] = titleSize;
        app.Resources["FontSizeSubtle"] = subtleSize;
        app.Resources["FontSizeCaption"] = captionSize;
        app.Resources["FontSizeMenuHeader"] = menuHeaderSize;

        app.Resources["ControlContentThemeFontSize"] = bodySize;
        app.Resources["BodyFontSize"] = bodySize;
        app.Resources["FontSizeNormal"] = baseSize;

        app.Resources["TypeScale1"] = Math.Round(11 * scale);
        app.Resources["TypeScale2"] = Math.Round(12 * scale);
        app.Resources["TypeScale3"] = Math.Round(14 * scale);
        app.Resources["TypeScale4"] = Math.Round(16 * scale);
        app.Resources["TypeScale5"] = Math.Round(18 * scale);
        app.Resources["TypeScale6"] = Math.Round(22 * scale);
        app.Resources["TypeScale7"] = Math.Round(30 * scale);
        app.Resources["TypeScale8"] = Math.Round(36 * scale);
    }
    catch
    {
        // Melhor esforço.
    }
}

    /// <summary>
    /// TileRadius/BarRadius (v0.9.13).
    /// </summary>
    private sealed record Palette(
        string Background, string Surface, string SurfaceRaised,
        string Accent, string AccentSoft, string TextPrimary, string TextSecondary,
        bool IsLight, double TileRadius = 16, double BarRadius = 18);

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
        // Preto (OLED)
        new("#000000", "#0A0A0A", "#161616", "#FFFFFF", "#4A4A4A", "#FFFFFF", "#8C8C8C", false),
        // Dourado
        new("#0E0C08", "#1A160E", "#262015", "#E5B33E", "#9C7A27", "#FFF8E6", "#B3A584", false),
        // Grafite
        new("#1A1A1A", "#242424", "#2E2E2E", "#E4423F", "#A32E2C", "#FFFFFF", "#ACACAC", false, TileRadius: 10, BarRadius: 10),
        // v0.17 (aprovadas pelo usuário a partir do comparativo de paletas)
        // Meia-noite
        new("#0A0E1A", "#111830", "#1A2347", "#6C8CFF", "#4A63C9", "#EDF1FF", "#9AA6CF", false),
        // Crepúsculo
        new("#120D1C", "#1B1428", "#271D3A", "#FF7A59", "#C25A3F", "#FFF3EE", "#B7A5C4", false),
        // Musgo
        new("#0E130F", "#151C16", "#1E281F", "#A3E635", "#6FA01F", "#F3F8EC", "#9DAA93", false),
        // Névoa
        new("#E9EDF2", "#FFFFFF", "#DDE3EB", "#0F6CBD", "#9CC4E8", "#101820", "#4F5B6B", true),
        // Alto Contraste
        new("#000000", "#0D0D0D", "#1A1A1A", "#FFD400", "#8F7700", "#FFFFFF", "#C8C8C8", false, TileRadius: 6, BarRadius: 6),
    ];

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

        try
        {
            app.Resources["TileCornerRadius"] = new CornerRadius(palette.TileRadius);
            app.Resources["BarCornerRadius"] = new CornerRadius(palette.BarRadius);
        }
        catch
        {
            // Melhor esforço.
        }

        var isOled = palette.Background.Equals("#000000", StringComparison.OrdinalIgnoreCase);
        var alpha = isOled ? "#FF" : "#CC";
        SetBrush(app, "BarBrush", alpha + palette.Surface.TrimStart('#'));
        SetBrush(app, "AccentTranslucentBrush", "#40" + palette.Accent.TrimStart('#'));
        SetBrush(app, "BackdropDimBrush", alpha + palette.Background.TrimStart('#'));
        // v0.18.4 (bug "tema Clean com fundo preto em todos os jogos"): no Clean
        // a ÚNICA arte da tela é o fundo desfocado, e ele fica embaixo do
        // BackdropDimBrush — que nas paletas pretas (Preto/OLED, Alto
        // Contraste) é 100% opaco, então a arte sumia. O véu da arte de fundo
        // agora tem brush próprio, nunca 100%: as paletas pretas usam o mesmo
        // 80% das outras. As barras e o resto do OLED continuam preto de verdade.
        SetBrush(app, "ArtBackdropDimBrush", "#CC" + palette.Background.TrimStart('#'));

        SetBrush(app, "OnAccentBrush", TextOnAccent(palette.Accent));
    }

    /// <summary>
    /// Cor do texto sobre o destaque (aba ativa, botão principal). v0.17: antes
    /// escolhia pela luminosidade da cor (limite 0.6) e deixava branco sobre
    /// Verde (2,1:1), Rosa (3,0:1) e Azul (3,3:1) — ilegível. Agora fica com
    /// branco enquanto ele tem contraste de pelo menos 4:1 com o destaque
    /// (Escuro, Vermelho e Grafite seguem iguais) e troca pro quase-preto quando
    /// não tem.
    /// </summary>
    internal static string TextOnAccent(string accentHex)
    {
        var accent = RelativeLuminance(Color.Parse(accentHex));
        var whiteContrast = 1.05 / (accent + 0.05);
        return whiteContrast >= 4.0 ? "#FFFFFF" : "#111111";
    }

    private static double RelativeLuminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
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
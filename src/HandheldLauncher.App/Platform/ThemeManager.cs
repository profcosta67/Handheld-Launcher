using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace HandheldLauncher.App.Platform;

public static class ThemeManager
{
    public static readonly string[] ThemeNames =
    [
        "Escuro", "Claro", "Azul", "Vermelho", "Verde", "Laranja", "Rosa", "Ciano", "Preto (OLED)", "Dourado", "Grafite",
        "Meia-noite", "Crepúsculo", "Musgo", "Névoa", "Alto Contraste",
        // Cartoon tem duas versões — escura (16) e clara (17). São as únicas
        // oferecidas ao usuário; o resto da paleta continua bloqueado.
        "Cartoon", "Cartoon Claro"
    ];

    public static readonly string[] LayoutThemeNames = ["Clássico", "Clean", "BigScreen", "Cartoon"];


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
        ("Limpa (Inter)", "fonts:Inter#Inter"),
        ("Manuscrita (Ink Free)", "Ink Free"),
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
    public const int FontManrope = 11, FontOutfit = 12, FontSpaceGrotesk = 13, FontDmSans = 14,
        FontPlusJakarta = 15, FontChakraPetch2 = 16, FontRajdhani = 17, FontNunito = 18, FontFredoka2 = 19;

    // v0.25.2: apelidos do tema Cartoon. O layout 3 é uma identidade fechada —
    // MainWindow.ApplyAppearance e SettingsViewModel.SaveTo forçam estes três
    // valores sempre que ele está ativo (ver LayoutDefaults abaixo).
    public const int ThemeCartoon = 16;
    public const int ThemeCartoonLight = 17;
    public const int FontFredoka = FontFredoka2;
    public const int SoundBolha = 2;

    public static (int Font, int Sound) LayoutDefaults(int layoutTheme) => layoutTheme switch
{
    0 => (FontPlusJakarta, 1),   // Clássico
    1 => (FontManrope, 3),       // Clean
    2 => (FontChakraPetch2, 0),  // Moderno (BigScreen)
    3 => (FontFredoka2, 2),      // Cartoon: Fredoka + Bolha
    _ => (FontModerna, 0),
};

    // 🟢 Fonte padrão para o Tema Cartoon
    public static readonly (int Font, int Sound) CartoonDefaults = (FontFredoka2, 2); 

    public static readonly string[] FontSizeNames = ["Pequeno", "Médio", "Grande"];

    public static void ApplyFont(string fontFamily)
    {
        var app = Application.Current;
        if (app is null) return;
        try
        {
            app.Resources["GlobalFontFamily"] = new FontFamily(fontFamily);
            app.Resources["ContentControlThemeFontFamily"] = new FontFamily(fontFamily);
        }
        catch { }
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
        catch { }
    }

    private sealed record Palette(
        string Background, string Surface, string SurfaceRaised,
        string Accent, string AccentSoft, string TextPrimary, string TextSecondary,
        bool IsLight, double TileRadius = 16, double BarRadius = 18);

    private static readonly Palette[] Palettes =
    [
        new("#0C0D12", "#181A22", "#222531", "#7C5CFC", "#5A41C4", "#F5F5F7", "#9A9CAB", false),
        new("#EEF0F5", "#FFFFFF", "#E2E5EE", "#5B3FE0", "#B9AEF5", "#16171D", "#5E6272", true),
        new("#07101E", "#0E1B2F", "#152741", "#2F8CFF", "#1D5FB8", "#EEF4FF", "#8EA3C2", false),
        new("#130A0C", "#201114", "#2C171B", "#E5334B", "#A3243A", "#FFF1F2", "#B39497", false),
        new("#08120C", "#0F1D15", "#17291E", "#2FCB6B", "#1F8A4A", "#EEFFF4", "#8FB09C", false),
        new("#140D07", "#21160D", "#2E1F13", "#FF8A2B", "#B35F1C", "#FFF5EC", "#B9A08B", false),
        new("#140A12", "#21101D", "#2D1728", "#FF4FA3", "#B33574", "#FFF0F8", "#B596A9", false),
        new("#061214", "#0C1E21", "#13292D", "#22D3EE", "#1590A3", "#ECFDFF", "#8DADB2", false),
        new("#000000", "#0A0A0A", "#161616", "#FFFFFF", "#4A4A4A", "#FFFFFF", "#8C8C8C", false),
        new("#0E0C08", "#1A160E", "#262015", "#E5B33E", "#9C7A27", "#FFF8E6", "#B3A584", false),
        new("#1A1A1A", "#242424", "#2E2E2E", "#E4423F", "#A32E2C", "#FFFFFF", "#ACACAC", false, TileRadius: 10, BarRadius: 10),
        new("#0A0E1A", "#111830", "#1A2347", "#6C8CFF", "#4A63C9", "#EDF1FF", "#9AA6CF", false),
        new("#120D1C", "#1B1428", "#271D3A", "#FF7A59", "#C25A3F", "#FFF3EE", "#B7A5C4", false),
        new("#0E130F", "#151C16", "#1E281F", "#A3E635", "#6FA01F", "#F3F8EC", "#9DAA93", false),
        new("#E9EDF2", "#FFFFFF", "#DDE3EB", "#0F6CBD", "#9CC4E8", "#101820", "#4F5B6B", true),
        new("#000000", "#0D0D0D", "#1A1A1A", "#FFD400", "#8F7700", "#FFFFFF", "#C8C8C8", false, TileRadius: 6, BarRadius: 6),
        
        // 🟢 PALETA CARTOON ESCURA (Roxo Nickelodeon + Laranja + Bordas Arredondadas)
        new("#1A0B2E", "#2D1B4E", "#42286D", "#FF9800", "#E65100", "#FFFFFF", "#FFEB3B", false, TileRadius: 24, BarRadius: 24),

        // v0.25.2: CARTOON CLARO — "papel de desenho": creme, tinta roxa e o mesmo
        // laranja do estúdio. A tinta preta dos contornos continua a funcionar
        // porque o fundo é claro (ver CartoonLibraryLayout/Styles).
        new("#F6EFE0", "#FFFDF6", "#EFE4CF", "#E65100", "#FFB74D", "#241A2E", "#6F5F77", true, TileRadius: 24, BarRadius: 24)
    ];

    public static void ApplyFont(Avalonia.Controls.Window window, int uiFont)
    {
        try
        {
            var family = uiFont > 0 && uiFont < Fonts.Length ? Fonts[uiFont].Family : null;
            if (family is not null) window.FontFamily = new FontFamily(family);
            else window.ClearValue(Avalonia.Controls.Primitives.TemplatedControl.FontFamilyProperty);
        }
        catch { }
    }

     public static void Apply(int themeIndex)
    {
        var app = Application.Current;
        if (app is null) return;

        var palette = Palettes[Math.Clamp(themeIndex, 0, Palettes.Length - 1)];
        app.RequestedThemeVariant = palette.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;

        // 🟢 CORREÇÃO: Aplica/remove a classe "cartoon" em todas as janelas abertas
        try
        {
            var lifetime = app.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            if (lifetime != null)
            {
                foreach (var window in lifetime.Windows)
                {
                    if (themeIndex is ThemeCartoon or ThemeCartoonLight)
                    {
                        if (!window.Classes.Contains("cartoon")) window.Classes.Add("cartoon");
                    }
                    else
                    {
                        window.Classes.Remove("cartoon");
                    }
                }
            }
        }
        catch { /* melhor esforço */ }

        var isOled = palette.Background.Equals("#000000", StringComparison.OrdinalIgnoreCase);
        var alpha = isOled ? "#FF" : "#CC";
        SetBrush(app, "BarBrush", alpha + palette.Surface.TrimStart('#'));
        SetBrush(app, "AccentTranslucentBrush", "#40" + palette.Accent.TrimStart('#'));
        SetBrush(app, "BackdropDimBrush", alpha + palette.Background.TrimStart('#'));
        SetBrush(app, "ArtBackdropDimBrush", "#CC" + palette.Background.TrimStart('#'));
        SetBrush(app, "OnAccentBrush", TextOnAccent(palette.Accent));
        SetBrush(app, "BackgroundBrush", palette.Background);

        // Até aqui só o fundo/barra acompanhavam o tema: as superfícies, o
        // destaque e o texto ficavam com as cores do tema escuro por omissão
        // (um tema "Claro" era só o fundo a mudar). Os brushes são a MESMA
        // instância usada por {StaticResource ...} em toda a app, por isso
        // mudar a cor aqui propaga-se a todas as janelas abertas.
        SetBrush(app, "SurfaceBrush", palette.Surface);
        SetBrush(app, "SurfaceRaisedBrush", palette.SurfaceRaised);
        SetBrush(app, "AccentBrush", palette.Accent);
        SetBrush(app, "AccentSoftBrush", palette.AccentSoft);
        SetBrush(app, "TextPrimaryBrush", palette.TextPrimary);
        SetBrush(app, "TextSecondaryBrush", palette.TextSecondary);

        // Brushes da Central de Controle derivam destes dois.
        SetBrush(app, "CardBackgroundBrush", palette.Surface);
        SetBrush(app, "ButtonBackgroundBrush", palette.SurfaceRaised);

        // Véu do Quick Settings: 70% opaco / 30% translúcido sobre o tema.
        SetBrush(app, "PanelTranslucentBrush", "#B3" + palette.Background.TrimStart('#'));

        // v0.25.2: véus do layout Cartoon (fundo da página e do ecrã de lançamento).
        // Derivam da cor de fundo da paleta, por isso o Cartoon Claro fica certo.
        SetBrush(app, "ToonVeilBrush", "#B3" + palette.Background.TrimStart('#'));
        SetBrush(app, "ToonVeilStrongBrush", "#E6" + palette.Background.TrimStart('#'));
    }

    /// <summary>
    /// Indica se o tema (índice) é claro. Usado pelo tile "Estilo Escuro" da
    /// Central de Controle para saber em que estado está.
    /// </summary>
    public static bool IsLight(int themeIndex)
        => themeIndex >= 0 && themeIndex < Palettes.Length && Palettes[themeIndex].IsLight;

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
                brush.Color = Color.Parse(hex);
        }
        catch { }
    }
}
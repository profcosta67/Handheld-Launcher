using System.Text.RegularExpressions;

namespace HandheldLauncher.Core.Services.Hud;

/// <summary>Um trecho do HUD com a sua cor (RRGGBB) — null = cor padrão.</summary>
public readonly record struct HudRun(string Text, string? Color);

/// <summary>
/// v0.28: o texto do HUD é montado com as marcas de cor do RTSS (<c>&lt;C=RRGGBB&gt;texto&lt;C&gt;</c>).
/// Para o HUD desenhado pelo próprio launcher, o mesmo texto é quebrado em linhas e trechos
/// coloridos — assim os dois caminhos (RTSS e launcher) mostram exatamente a mesma coisa.
/// </summary>
public static class HudMarkup
{
    private static readonly Regex Tag = new(@"<C=([0-9A-Fa-f]{6})>(.*?)<C>", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Linhas (separadas por \n) com os trechos de cada uma.</summary>
    public static List<List<HudRun>> Parse(string? text)
    {
        var lines = new List<List<HudRun>>();
        if (string.IsNullOrEmpty(text)) return lines;
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            var runs = new List<HudRun>();
            var pos = 0;
            foreach (Match m in Tag.Matches(line))
            {
                if (m.Index > pos) AddPlain(runs, line[pos..m.Index]);
                if (m.Groups[2].Length > 0) runs.Add(new HudRun(m.Groups[2].Value, m.Groups[1].Value.ToUpperInvariant()));
                pos = m.Index + m.Length;
            }
            if (pos < line.Length) AddPlain(runs, line[pos..]);
            lines.Add(runs);
        }
        return lines;
    }

    private static void AddPlain(List<HudRun> runs, string text)
    {
        // marca solta (sem par) não aparece como texto
        text = text.Replace("<C>", "");
        text = Regex.Replace(text, "<C=[0-9A-Fa-f]{6}>", "");
        if (text.Length > 0) runs.Add(new HudRun(text, null));
    }
}

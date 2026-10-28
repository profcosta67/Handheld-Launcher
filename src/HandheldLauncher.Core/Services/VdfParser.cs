using System.Text;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// A node in a parsed VDF (Valve KeyValues) document. Each node is either a
/// leaf with a string value, or a container with child nodes keyed by name.
/// Steam uses this format for libraryfolders.vdf, appmanifest_*.acf, etc.
/// Hand-rolled on purpose — no NuGet package needed for something this small.
/// </summary>
public sealed class VdfNode
{
    public string? Value { get; set; }
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsLeaf => Children.Count == 0;

    public VdfNode? this[string key] => Children.TryGetValue(key, out var child) ? child : null;

    public string? GetString(string key) => this[key]?.Value;
}

public static class VdfParser
{
    public static VdfNode ParseFile(string path)
    {
        var text = File.ReadAllText(path);
        return Parse(text);
    }

    public static VdfNode Parse(string text)
    {
        var pos = 0;
        var root = new VdfNode();
        ParseObjectBody(text, ref pos, root);
        return root;
    }

    private static void ParseObjectBody(string text, ref int pos, VdfNode parent)
    {
        while (true)
        {
            SkipWhitespaceAndComments(text, ref pos);
            if (pos >= text.Length) return;

            if (text[pos] == '}')
            {
                pos++; // consumed by caller's opening '{'
                return;
            }

            // Expect a quoted key.
            var key = ReadQuotedString(text, ref pos);
            if (key is null) return;

            SkipWhitespaceAndComments(text, ref pos);
            if (pos >= text.Length) return;

            var node = new VdfNode();

            if (text[pos] == '{')
            {
                pos++; // consume '{'
                ParseObjectBody(text, ref pos, node);
            }
            else
            {
                node.Value = ReadQuotedString(text, ref pos) ?? string.Empty;
            }

            parent.Children[key] = node;
        }
    }

    private static string? ReadQuotedString(string text, ref int pos)
    {
        SkipWhitespaceAndComments(text, ref pos);
        if (pos >= text.Length || text[pos] != '"') return null;

        pos++; // opening quote
        var sb = new StringBuilder();
        while (pos < text.Length && text[pos] != '"')
        {
            if (text[pos] == '\\' && pos + 1 < text.Length)
            {
                // VDF escapes: \\ and \" are the only ones that matter here.
                pos++;
                sb.Append(text[pos]);
            }
            else
            {
                sb.Append(text[pos]);
            }
            pos++;
        }
        pos++; // closing quote
        return sb.ToString();
    }

    private static void SkipWhitespaceAndComments(string text, ref int pos)
    {
        while (pos < text.Length)
        {
            if (char.IsWhiteSpace(text[pos]))
            {
                pos++;
            }
            else if (text[pos] == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
            {
                while (pos < text.Length && text[pos] != '\n') pos++;
            }
            else
            {
                break;
            }
        }
    }
}

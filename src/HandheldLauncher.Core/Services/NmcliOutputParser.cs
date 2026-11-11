using System.Text;

namespace HandheldLauncher.Core.Services;

/// <summary>Parses NetworkManager's terse output, where colons and backslashes are escaped.</summary>
public static class NmcliOutputParser
{
    public static IReadOnlyList<string> ParseTerseFields(string? line)
    {
        if (string.IsNullOrEmpty(line)) return Array.Empty<string>();

        var fields = new List<string>();
        var field = new StringBuilder();
        for (var i = 0; i < line.Length; i++)
        {
            var current = line[i];
            if (current == '\\' && i + 1 < line.Length)
            {
                field.Append(line[++i]);
            }
            else if (current == ':')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(current);
            }
        }

        fields.Add(field.ToString().TrimEnd('\r'));
        return fields;
    }

    public static string? ParseCurrentSsid(string? output)
    {
        if (string.IsNullOrEmpty(output)) return null;

        foreach (var line in output.Split('\n'))
        {
            var fields = ParseTerseFields(line);
            if (fields.Count >= 2 && fields[0] == "*" && !string.IsNullOrEmpty(fields[1]))
                return fields[1];
        }

        return null;
    }
}

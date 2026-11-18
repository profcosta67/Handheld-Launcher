using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// 18/11: regras puras do "Adicionar jogo" manual — decide se o que o usuário escolheu abre
/// direto (executável) ou pelo Windows (atalho ou endereço), monta os argumentos com o arquivo
/// do jogo de um emulador e reconhece emuladores de Switch para o filtro "Emuladores".
/// Sem acesso a disco: testável.
/// </summary>
public static class ManualLaunchTarget
{
    private static readonly string[] SwitchEmulatorExecutables =
    {
        "ryujinx", "ryujinx.ava", "eden", "yuzu", "suyu", "sudachi", "citron", "torzu",
    };

    /// <summary>
    /// true = abrir pelo Windows (ShellExecute): endereço com esquema (steam://, https://...)
    /// ou arquivo de atalho que não foi convertido em executável (.lnk, .url).
    /// </summary>
    public static bool OpensThroughWindows(string target)
    {
        var value = Clean(target);
        if (value.Length == 0) return false;
        if (value.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) return true;
        return LooksLikeUri(value);
    }

    /// <summary>Esquema de URI (steam://, ms-xbl-...:, https://). Caminho com letra de unidade não conta.</summary>
    public static bool LooksLikeUri(string value)
    {
        var colon = value.IndexOf(':');
        if (colon < 2) return false;                 // "C:\..." tem o ':' na posição 1
        for (var i = 0; i < colon; i++)
        {
            var c = value[i];
            if (!(char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.')) return false;
        }
        return char.IsAsciiLetter(value[0]);
    }

    /// <summary>O executável é um emulador de Switch conhecido (pelo nome do arquivo).</summary>
    public static bool IsSwitchEmulator(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;
        // Separa pelo último \ ou / à mão: o caminho é do Windows mesmo quando os testes rodam no Linux.
        var path = Clean(executablePath);
        var name = path[(path.LastIndexOfAny(new[] { '\\', '/' }) + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return SwitchEmulatorExecutables.Any(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Junta os argumentos digitados com o arquivo do jogo (entre aspas) no fim.</summary>
    public static string? CombineArguments(string? arguments, string? gameFile)
    {
        var args = arguments?.Trim() ?? string.Empty;
        var file = Clean(gameFile ?? string.Empty);
        if (file.Length > 0) args = args.Length > 0 ? $"{args} \"{file}\"" : $"\"{file}\"";
        return args.Length > 0 ? args : null;
    }

    /// <summary>
    /// Monta o GameEntry manual. Executável → abre direto (o launcher acompanha o processo).
    /// Atalho não convertido ou endereço → abre pelo Windows (sem argumentos próprios).
    /// </summary>
    public static GameEntry BuildEntry(string title, string target, string? arguments, string? workingDirectory,
        string? gameFile, bool isApplication, bool isXbox)
    {
        var cleanTarget = Clean(target);
        var throughWindows = OpensThroughWindows(cleanTarget);
        var args = CombineArguments(arguments, gameFile);

        var source = isXbox ? GameSource.XboxApp
            : !isApplication && !throughWindows && IsSwitchEmulator(cleanTarget) ? GameSource.Emulator
            : GameSource.Manual;

        return new GameEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title.Trim(),
            IsApplication = isApplication,
            Source = source,
            ExecutablePath = throughWindows ? null : cleanTarget,
            LaunchUri = throughWindows ? cleanTarget : null,
            LaunchArguments = throughWindows ? null : args,
            WorkingDirectory = throughWindows || string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory.Trim(),
        };
    }

    private static string Clean(string value) => value.Trim().Trim('"').Trim();
}

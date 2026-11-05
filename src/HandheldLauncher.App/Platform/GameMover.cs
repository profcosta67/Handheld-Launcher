using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.App.Platform;

/// <summary>Como um jogo pode ser movido de disco.</summary>
internal enum MoveRoute
{
    /// <summary>Jogo adicionado à mão: o próprio launcher copia, confere, atualiza o atalho e só então apaga a pasta antiga.</summary>
    SelfMove,
    /// <summary>Steam: abre Propriedades → Arquivos instalados → Mover (a Steam atualiza os manifestos dela).</summary>
    SteamClient,
    /// <summary>Xbox/Game Pass: Aplicativos instalados do Windows (ou app Xbox → Gerenciar → Arquivos).</summary>
    WindowsApps,
    /// <summary>Não dá pra mover por aqui com segurança (loja com registro próprio, emulador, pasta compartilhada...).</summary>
    NotMovable,
}

internal sealed record MovePlan(MoveRoute Route, string ShortLabel, string Explanation, string? SourceFolder);

internal sealed record CopyResult(bool Success, string Message, string? DestinationFolder, long Bytes);

/// <summary>
/// v0.18 "Gerenciador de armazenamento" — mover jogo entre SSD e microSD.
///
/// Por que tão restrito: mover a pasta de um jogo por fora da loja que o
/// instalou quebra o registro da loja (Steam/Epic/Xbox apontariam pro lugar
/// antigo) e, se for interrompido no meio, pode deixar o jogo pela metade.
/// Por isso:
/// <list type="bullet">
/// <item>Steam e Xbox vão pro "mover" oficial deles (o launcher só abre a tela certa).</item>
/// <item>O launcher só move sozinho jogos ADICIONADOS À MÃO, e só se a pasta
/// não for raiz de disco/pasta do sistema e não tiver outro jogo/programa da
/// biblioteca dentro (nem estiver dentro da pasta de outro jogo).</item>
/// <item>Ordem que nunca perde dado: copia tudo → confere tamanho de cada
/// arquivo → atualiza o atalho → só então apaga a original. Falhou ou
/// cancelou antes de atualizar o atalho = apaga só a cópia nova; a original
/// nunca foi tocada.</item>
/// </list>
/// </summary>
internal static class GameMover
{
    private const long FreeSpaceMarginBytes = 512L * 1024 * 1024;

    public static MovePlan Plan(GameEntry game, IReadOnlyList<GameEntry> library)
    {
        switch (game.Source)
        {
            case GameSource.Steam:
                return new MovePlan(MoveRoute.SteamClient, "Mover pela Steam",
                    "Jogos da Steam são movidos pela própria Steam, que atualiza os registros dela. Vou abrir as Propriedades do jogo na Steam: vá em \"Arquivos instalados\" → \"Mover pasta de instalação\" e escolha o outro disco.",
                    game.InstallDirectory);

            case GameSource.XboxApp:
                return new MovePlan(MoveRoute.WindowsApps, "Mover pelo Windows/Xbox",
                    "Jogos do Xbox/Game Pass são movidos pelo Windows. Vou abrir \"Aplicativos instalados\": procure o jogo → ⋯ → Mover. Se ele não aparecer lá, use o app Xbox: jogo → Gerenciar → Arquivos → Mover.",
                    game.InstallDirectory);

            case GameSource.EpicGames:
            case GameSource.Gog:
            case GameSource.EaApp:
            case GameSource.Ubisoft:
                return new MovePlan(MoveRoute.NotMovable, "Mover pela loja",
                    "Este jogo é registrado pela loja que o instalou (Epic, GOG, EA ou Ubisoft). Mover a pasta por fora quebraria esse registro — use a opção de mover/reinstalar da própria loja.",
                    game.InstallDirectory);

            case GameSource.Emulator:
                return new MovePlan(MoveRoute.NotMovable, "Não move por aqui",
                    "Jogos de emulador ficam dentro das pastas do emulador/ROMs, junto de outros jogos. Mover por aqui quebraria os outros.",
                    null);
        }

        // ---- Jogo adicionado à mão --------------------------------------------------
        if (game.IsApplication)
            return NotMovable("Isto é um programa, não um jogo.");
        if (!string.IsNullOrWhiteSpace(game.LaunchUri))
            return NotMovable("Este atalho abre por um link de loja — mova pela loja.");
        if (string.IsNullOrWhiteSpace(game.ExecutablePath) || !File.Exists(game.ExecutablePath))
            return NotMovable("O executável deste atalho não foi encontrado.");

        var folder = ResolveManualGameFolder(game);
        if (folder is null)
            return NotMovable("Não consegui descobrir a pasta do jogo.");

        if (UnsafeFolderReason(folder) is { } unsafeReason)
            return new MovePlan(MoveRoute.NotMovable, "Não move por aqui", unsafeReason, folder);

        foreach (var other in library)
        {
            if (other.Id == game.Id) continue;
            foreach (var p in new[] { other.ExecutablePath, other.InstallDirectory, other.WorkingDirectory })
            {
                if (!string.IsNullOrWhiteSpace(p) && IsSameOrInside(p!, folder))
                    return new MovePlan(MoveRoute.NotMovable, "Pasta compartilhada",
                        $"A pasta \"{folder}\" também tem \"{other.Title}\". Mover levaria o outro junto e quebraria o atalho dele.", folder);
            }
            if (!string.IsNullOrWhiteSpace(other.InstallDirectory) && IsSameOrInside(folder, other.InstallDirectory!))
                return new MovePlan(MoveRoute.NotMovable, "Dentro de outro jogo",
                    $"Esta pasta fica dentro da instalação de \"{other.Title}\" — mova aquele jogo pela loja dele.", folder);
        }

        return new MovePlan(MoveRoute.SelfMove, "Mover pelo launcher",
            $"O launcher copia a pasta \"{folder}\" pro disco escolhido, confere cada arquivo, atualiza o atalho e só então apaga a original. Se algo falhar, a original fica intacta.",
            folder);

        static MovePlan NotMovable(string why) => new(MoveRoute.NotMovable, "Não move por aqui", why, null);
    }

    /// <summary>Pasta de trabalho, se o .exe estiver dentro dela; senão a pasta do .exe.</summary>
    public static string? ResolveManualGameFolder(GameEntry game)
    {
        try
        {
            var exe = Path.GetFullPath(game.ExecutablePath!);
            var exeDir = Path.GetDirectoryName(exe);
            if (!string.IsNullOrWhiteSpace(game.WorkingDirectory))
            {
                var wd = Path.GetFullPath(game.WorkingDirectory!);
                if (IsSameOrInside(exe, wd)) return Normalize(wd);
            }
            return exeDir is null ? null : Normalize(exeDir);
        }
        catch (Exception ex)
        {
            DebugLog.Swallowed(ex);
            return null;
        }
    }

    private static string? UnsafeFolderReason(string folder)
    {
        var root = Path.GetPathRoot(folder);
        if (root is null || string.Equals(Normalize(root), folder, StringComparison.OrdinalIgnoreCase))
            return "O jogo está direto na raiz do disco — mover levaria o disco inteiro.";

        var exact = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };
        foreach (var p in exact)
        {
            if (!string.IsNullOrWhiteSpace(p) && string.Equals(Normalize(p), folder, StringComparison.OrdinalIgnoreCase))
                return $"O executável está direto em \"{folder}\" (pasta do sistema/usuário) — não dá pra mover essa pasta inteira.";
        }

        var inside = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        };
        foreach (var p in inside)
        {
            if (!string.IsNullOrWhiteSpace(p) && IsSameOrInside(folder, p))
                return $"A pasta fica dentro de \"{p}\" (pasta do sistema/apps) — mover por aqui não é seguro.";
        }
        return null;
    }

    public static bool IsSameOrInside(string path, string folder)
    {
        try
        {
            var p = Normalize(Path.GetFullPath(path));
            var f = Normalize(Path.GetFullPath(folder));
            return string.Equals(p, f, StringComparison.OrdinalIgnoreCase)
                || p.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string Normalize(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>Discos prontos (fixos ou removíveis) diferentes do de origem, com espaço pra cópia.</summary>
    public static IReadOnlyList<(string Root, string Label, long FreeBytes)> DestinationDrives(string sourceFolder, long neededBytes)
    {
        var list = new List<(string, string, long)>();
        var sourceRoot = Path.GetPathRoot(sourceFolder) ?? "";
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (!d.IsReady) continue;
                if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                if (string.Equals(d.RootDirectory.FullName, sourceRoot, StringComparison.OrdinalIgnoreCase)) continue;
                if (d.AvailableFreeSpace < neededBytes + FreeSpaceMarginBytes) continue;
                var kind = d.DriveType == DriveType.Removable ? "microSD/externo" : "interno";
                list.Add((d.RootDirectory.FullName, $"{d.Name} ({kind}) — {DiskUsage.FormatSize(d.AvailableFreeSpace)} livres", d.AvailableFreeSpace));
            }
        }
        catch (Exception ex) { DebugLog.Write($"[MOVER] Falha ao listar discos: {ex.Message}"); }
        return list;
    }

    /// <summary>
    /// true se dá pra abrir o .exe com acesso exclusivo — se não der, o jogo
    /// (ou algo dele) está aberto e mover agora corromperia a cópia.
    /// </summary>
    public static bool IsExecutableFree(string exePath)
    {
        try
        {
            using var _ = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return true; } // sem permissão de leitura exclusiva não quer dizer "em uso"
        catch (Exception ex) { DebugLog.Swallowed(ex); return true; }
    }

    /// <summary>
    /// Passo 1 e 2: copia e confere. Roda em segundo plano. Nunca toca na
    /// pasta original. Em falha/cancelamento apaga a cópia parcial.
    /// </summary>
    public static CopyResult CopyAndVerify(string sourceFolder, string destinationRoot, IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        string? destFolder = null;
        try
        {
            // Lista tudo antes de copiar. Junção/link simbólico dentro da pasta =
            // recusa (copiar seguiria o link pra outro lugar; apagar depois
            // poderia apagar o destino do link).
            var files = new List<(string Rel, long Size)>();
            var dirs = new List<string>();
            if (!Enumerate(sourceFolder, sourceFolder, files, dirs, out var linkFound))
                return new CopyResult(false, $"A pasta tem atalhos de pasta (junção/link): {linkFound}. Mover assim não é seguro — nada foi alterado.", null, 0);

            var total = files.Sum(f => f.Size);
            var drive = new DriveInfo(destinationRoot);
            if (drive.AvailableFreeSpace < total + FreeSpaceMarginBytes)
                return new CopyResult(false, $"Espaço insuficiente em {destinationRoot} (precisa de {DiskUsage.FormatSize(total)} + folga).", null, total);

            var baseDest = Path.Combine(destinationRoot, "Games", Path.GetFileName(Normalize(sourceFolder)));
            destFolder = baseDest;
            for (var n = 2; Directory.Exists(destFolder) || File.Exists(destFolder); n++) destFolder = $"{baseDest} ({n})";

            Directory.CreateDirectory(destFolder);
            foreach (var rel in dirs) Directory.CreateDirectory(Path.Combine(destFolder, rel));

            long done = 0;
            long lastReported = 0;
            const long ReportEveryBytes = 32L * 1024 * 1024; // não inunda a UI com 1 aviso por MB
            var buffer = new byte[1024 * 1024];
            foreach (var (rel, size) in files)
            {
                ct.ThrowIfCancellationRequested();
                var src = Path.Combine(sourceFolder, rel);
                var dst = Path.Combine(destFolder, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                using (var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, FileOptions.SequentialScan))
                using (var output = new FileStream(dst, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length))
                {
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, read);
                        done += read;
                        if (done - lastReported >= ReportEveryBytes) { lastReported = done; progress?.Report((done, total)); }
                    }
                }
                try { File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(src)); } catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }
            }

            progress?.Report((done, total));

            // Conferência: cada arquivo existe na cópia com o mesmo tamanho.
            foreach (var (rel, size) in files)
            {
                ct.ThrowIfCancellationRequested();
                var dst = new FileInfo(Path.Combine(destFolder, rel));
                if (!dst.Exists || dst.Length != size)
                {
                    TryDelete(destFolder);
                    return new CopyResult(false, $"A conferência falhou em \"{rel}\" — a cópia foi descartada e a original continua intacta.", null, total);
                }
            }

            DebugLog.Write($"[MOVER] Cópia conferida: {files.Count} arquivos, {DiskUsage.FormatSize(total)} → {destFolder}");
            return new CopyResult(true, "", destFolder, total);
        }
        catch (OperationCanceledException)
        {
            if (destFolder is not null) TryDelete(destFolder);
            return new CopyResult(false, "Cancelado. A cópia parcial foi apagada e a original continua intacta.", null, 0);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[MOVER] Falha na cópia: {ex}");
            if (destFolder is not null) TryDelete(destFolder);
            return new CopyResult(false, $"A cópia falhou ({ex.Message}). A cópia parcial foi apagada e a original continua intacta.", null, 0);
        }
    }

    private static bool Enumerate(string root, string current, List<(string, long)> files, List<string> dirs, out string? linkFound)
    {
        linkFound = null;
        foreach (var dir in Directory.EnumerateDirectories(current))
        {
            var info = new DirectoryInfo(dir);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) { linkFound = dir; return false; }
            dirs.Add(Path.GetRelativePath(root, dir));
            if (!Enumerate(root, dir, files, dirs, out linkFound)) return false;
        }
        foreach (var file in Directory.EnumerateFiles(current))
        {
            var info = new FileInfo(file);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) { linkFound = file; return false; }
            files.Add((Path.GetRelativePath(root, file), info.Length));
        }
        return true;
    }

    /// <summary>Troca o começo do caminho (pasta antiga → nova). Caminhos fora da pasta ficam como estão.</summary>
    public static string? Rebase(string? path, string oldFolder, string newFolder)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        if (!IsSameOrInside(path, oldFolder)) return path;
        var rel = Path.GetRelativePath(oldFolder, Path.GetFullPath(path));
        return rel == "." ? newFolder : Path.Combine(newFolder, rel);
    }

    public static string? RebaseArguments(string? args, string oldFolder, string newFolder)
    {
        if (string.IsNullOrWhiteSpace(args)) return args;
        var from = Normalize(oldFolder);
        var to = Normalize(newFolder);
        // Só troca ocorrências seguidas de separador/aspas/fim — evita mexer em
        // "D:\Jogo2" quando a pasta é "D:\Jogo".
        return System.Text.RegularExpressions.Regex.Replace(args,
            System.Text.RegularExpressions.Regex.Escape(from) + @"(?=[\\/""\s]|$)",
            to.Replace("$", "$$"),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>Passo 4 (só depois do atalho atualizado): apaga a original. Falha aqui não perde dado — a cópia nova já é a usada.</summary>
    public static (bool Ok, string Message) DeleteOriginal(string sourceFolder)
    {
        try
        {
            Directory.Delete(sourceFolder, recursive: true);
            return (true, "");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[MOVER] Original não apagada por completo: {ex.Message}");
            return (false, $"O jogo já está no disco novo, mas parte da pasta antiga não pôde ser apagada ({ex.Message}). Pode apagar \"{sourceFolder}\" depois, à mão.");
        }
    }

    /// <summary>Descarta uma cópia já conferida quando o atalho não pôde ser atualizado (a original não foi tocada).</summary>
    public static void DiscardCopy(string folder) => TryDelete(folder);

    private static void TryDelete(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch (Exception ex) { DebugLog.Write($"[MOVER] Não consegui apagar a cópia parcial {folder}: {ex.Message}"); }
    }
}

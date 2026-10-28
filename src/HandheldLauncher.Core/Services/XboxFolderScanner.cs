using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// "Editor de onde procurar" pedido pelo usuário pra jogos Xbox/Game Pass,
/// depois que a detecção automática não achou nada: não existe um scanner
/// de UWP/Xbox de verdade aqui (precisaria referenciar pacotes de
/// Windows.Management.Deployment/APIs de UWP que este projeto não usa),
/// então isto é uma heurística simples de pasta — configurada à mão em
/// AppSettings.XboxScanFolders (settings.json, hand-edit só, sem UI ainda,
/// mesmo padrão do SteamGridDbApiKey).
///
/// Pra cada pasta raiz configurada, toda SUBPASTA IMEDIATA dentro dela é
/// tratada como um jogo — nome da subpasta = título, e o executável
/// escolhido é o MAIOR .exe encontrado dentro dela (recursivo, já que
/// instalações Xbox/Game Pass costumam enterrar o .exe real uma ou duas
/// pastas mais pra dentro): o jogo de verdade quase sempre é muito maior
/// que qualquer launcher/suporte .exe que vem junto. Heurística simples,
/// pode escolher errado em algum caso — revisitar com algo mais
/// inteligente se isso acontecer numa biblioteca real.
///
/// Id é derivado do caminho completo da subpasta (determinístico — NUNCA
/// usar string.GetHashCode() aqui, que é randomizado por processo em
/// .NET, mudaria a cada execução e perderia favoritos/arte salvos contra
/// esse Id). Deliberadamente separado de ISteamLibraryScanner (formato
/// bem diferente — este lê AppSettings, o da Steam não), mas plugado em
/// LibraryService.Refresh() do mesmo jeito que o scanner da Steam (ver
/// comentário "Future scanners... plug in here" lá).
/// </summary>
public sealed class XboxFolderScanner : IXboxFolderScanner
{
    private readonly IReadOnlyList<string> _scanFolders;

    public XboxFolderScanner(IReadOnlyList<string> scanFolders)
    {
        _scanFolders = scanFolders;
    }

    public IReadOnlyList<GameEntry> ScanInstalledGames()
    {
        var results = new List<GameEntry>();

        foreach (var root in _scanFolders)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;

            IEnumerable<string> subfolders;
            try
            {
                if (!Directory.Exists(root)) continue;
                subfolders = Directory.EnumerateDirectories(root);
            }
            catch (IOException)
            {
                // Pasta configurada mas inacessível agora (drive externo
                // desconectado, etc.) — pula ela, não derruba o scan das
                // outras pastas configuradas.
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var gameDir in subfolders)
            {
                var exePath = FindLikelyExecutable(gameDir);
                if (exePath is null) continue;

                results.Add(new GameEntry
                {
                    Id = $"xboxfolder_{SanitizeForId(gameDir)}",
                    Title = Path.GetFileName(gameDir),
                    Source = GameSource.XboxApp,
                    ExecutablePath = exePath,
                    InstallDirectory = gameDir,
                });
            }
        }

        return results;
    }

    private static string? FindLikelyExecutable(string gameDir)
    {
        try
        {
            return Directory.EnumerateFiles(gameDir, "*.exe", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(f => f.Length)
                .FirstOrDefault()
                ?.FullName;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Transforma o caminho completo num Id estável e seguro pra usar como
    /// chave de override (favoritos, arte, playtime) — minúsculas, só
    /// letras/números sobrevivem, todo o resto (barras, dois-pontos,
    /// espaços) vira "_". Mesmo caminho sempre gera o mesmo Id, em
    /// qualquer execução/máquina.
    /// </summary>
    private static string SanitizeForId(string path)
    {
        var chars = path.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars);
    }
}

namespace HandheldLauncher.App.Diagnostics;

/// <summary>
/// Dirt-simple file logger added specifically to chase the "Escolher capa
/// does nothing" report blind — there's no debugger attached to the user's
/// machine from here, and the sandbox this was written in can't build/run
/// the project either. Appends timestamped lines to
/// %AppData%\HandheldLauncher\debug.log. Every call is wrapped in try/catch
/// so a logging failure (locked file, no disk space, whatever) can never
/// be the thing that breaks the feature it's trying to diagnose.
///
/// This is temporary scaffolding, not a permanent feature — fine to rip
/// out (or leave quietly running, it's cheap) once the real bug is found.
/// </summary>
public static class DebugLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HandheldLauncher", "debug.log");

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> SwallowedSites = new();

    /// <summary>
    /// v0.16.4 (plano da v0.16: "trocar os catch vazios por registro no log"):
    /// registra uma exceção que o código decidiu ignorar — UMA vez por ponto do
    /// código (arquivo:linha) por sessão, pra laços que rodam toda hora não
    /// encherem o log. O comportamento não muda: a exceção continua ignorada.
    /// </summary>
    public static void Swallowed(Exception ex,
        [System.Runtime.CompilerServices.CallerMemberName] string member = "",
        [System.Runtime.CompilerServices.CallerFilePath] string file = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        try
        {
            var site = $"{Path.GetFileName(file)}:{line}";
            if (!SwallowedSites.TryAdd(site, 0)) return;
            Write($"[IGNORADO] {site} ({member}): {ex.GetType().Name}: {ex.Message} (próximas vezes neste ponto não são registradas)");
        }
        catch
        {
            // O registro nunca pode quebrar quem chamou.
        }
    }

    public static void Write(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // v0.9.0: o log não cresce pra sempre — passou de 1 MB, vira
            // debug.old.log (o anterior é descartado). Ele nunca contém
            // chaves/senhas, mas não há motivo pra guardar meses de histórico.
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > 1024 * 1024)
            {
                File.Move(LogPath, Path.Combine(dir ?? string.Empty, "debug.old.log"), overwrite: true);
            }

            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}\n");
        }
        catch
        {
            // Logging must never be the thing that breaks the app.
        }
    }
}

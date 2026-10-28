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

    public static void Write(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch
        {
            // Logging must never be the thing that breaks the app.
        }
    }
}

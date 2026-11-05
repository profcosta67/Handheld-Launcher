using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// v0.18: medição de pasta compartilhada entre Gerenciador de Armazenamento,
/// página de Detalhes do jogo e limpeza de cache de shader. Mesma lógica que
/// já estava dentro do StorageManagerWindow (tolerante a erro: subpasta sem
/// permissão fica de fora, nunca derruba a conta inteira).
/// </summary>
internal static class DiskUsage
{
    public static long GetDirectorySizeBytes(string path)
    {
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path))
            {
                try { total += new FileInfo(file).Length; }
                catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }
            }
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                // Pasta de junção/link simbólico: não segue (pode apontar pra
                // outro disco ou criar laço infinito).
                try
                {
                    if ((new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); continue; }
                total += GetDirectorySizeBytes(dir);
            }
        }
        catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }
        return total;
    }

    public static string FormatSize(long bytes)
    {
        var gb = bytes / 1024.0 / 1024.0 / 1024.0;
        if (gb >= 1.0) return $"{gb:F1} GB";
        var mb = bytes / 1024.0 / 1024.0;
        return mb >= 1.0 ? $"{mb:F0} MB" : $"{bytes / 1024.0:F0} KB";
    }
}

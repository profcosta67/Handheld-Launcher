using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// v0.18 "Gerenciador de armazenamento" — limpar cache de shader.
///
/// Só pastas que são cache de verdade e se refazem sozinhas: o cache de
/// DirectX do Windows (o mesmo que a Limpeza de Disco apaga), os caches do
/// driver AMD/NVIDIA/Intel (o mesmo que "Redefinir cache de shader" do
/// Adrenalin faz) e o steamapps\shadercache de cada biblioteca da Steam
/// (a Steam baixa/gera de novo). Nada de save, configuração ou arquivo de
/// jogo. Arquivo em uso (driver segurando) é pulado, nunca forçado. As
/// pastas-raiz ficam; só o conteúdo sai. Efeito colateral esperado: o
/// primeiro carregamento de cada jogo depois disso demora mais (recompila).
/// </summary>
internal static class ShaderCacheCleaner
{
    public static IReadOnlyList<string> ExistingFolders()
    {
        var result = new List<string>();
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(local))
            {
                foreach (var rel in new[]
                {
                    "D3DSCache",
                    Path.Combine("AMD", "DxCache"),
                    Path.Combine("AMD", "DxcCache"),
                    Path.Combine("AMD", "VkCache"),
                    Path.Combine("AMD", "GLCache"),
                    Path.Combine("AMD", "OglCache"),
                    Path.Combine("NVIDIA", "DXCache"),
                    Path.Combine("NVIDIA", "GLCache"),
                    Path.Combine("Intel", "ShaderCache"),
                })
                {
                    var p = Path.Combine(local, rel);
                    if (Directory.Exists(p)) result.Add(p);
                }
            }

            foreach (var lib in SteamLibraryScanner.LocateLibraryFolders())
            {
                var p = Path.Combine(lib, "steamapps", "shadercache");
                if (Directory.Exists(p) && !result.Contains(p, StringComparer.OrdinalIgnoreCase)) result.Add(p);
            }
        }
        catch (Exception ex) { DebugLog.Write($"[SHADER] Falha ao listar pastas: {ex.Message}"); }
        return result;
    }

    public static long MeasureBytes(IEnumerable<string> folders) => folders.Sum(DiskUsage.GetDirectorySizeBytes);

    /// <summary>Apaga o conteúdo (não as pastas-raiz). Devolve bytes liberados e quantos arquivos ficaram (em uso).</summary>
    public static (long FreedBytes, int SkippedFiles) Clear(IEnumerable<string> folders, CancellationToken ct = default)
    {
        long freed = 0;
        var skipped = 0;
        foreach (var root in folders)
        {
            if (!Directory.Exists(root)) continue;
            ClearContents(root, ref freed, ref skipped, ct);
        }
        DebugLog.Write($"[SHADER] Limpeza: {DiskUsage.FormatSize(freed)} liberados, {skipped} arquivo(s) em uso pulados.");
        return (freed, skipped);
    }

    private static void ClearContents(string folder, ref long freed, ref int skipped, CancellationToken ct)
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(folder).ToList(); }
        catch (Exception ex) { DebugLog.Swallowed(ex); return; }

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                var info = new FileInfo(file);
                var len = info.Length;
                if ((info.Attributes & FileAttributes.ReadOnly) != 0) info.Attributes &= ~FileAttributes.ReadOnly;
                info.Delete();
                freed += len;
            }
            catch (Exception) { skipped++; }
        }

        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(folder).ToList(); }
        catch (Exception ex) { DebugLog.Swallowed(ex); return; }

        foreach (var dir in dirs)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                // Junção/link: não entra (poderia apontar pra fora do cache).
                if ((new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0) continue;
            }
            catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); continue; }

            ClearContents(dir, ref freed, ref skipped, ct);
            try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
            catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }
        }
    }
}

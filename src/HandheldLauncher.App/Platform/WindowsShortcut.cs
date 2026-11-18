using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// 18/11: lê atalhos do Windows escolhidos no "Adicionar jogo". Um .lnk (por exemplo o que o
/// Ryujinx cria: Ryujinx.exe + "caminho do jogo") vira executável + argumentos + pasta, para
/// o launcher abrir e acompanhar o processo como qualquer jogo. Um .url devolve o endereço
/// (steam://, https://...). Nunca lança exceção: devolve null se não conseguir ler.
/// </summary>
internal static class WindowsShortcut
{
    public sealed record Contents(string? TargetPath, string? Arguments, string? WorkingDirectory, string? Url);

    public static Contents? TryRead(string path)
    {
        try
        {
            if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in File.ReadLines(path))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("URL=", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 4)
                        return new Contents(null, null, null, trimmed[4..].Trim());
                }
                return null;
            }

            if (!OperatingSystem.IsWindows() || !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return null;

            var link = (IShellLinkW)new ShellLinkCoClass();
            try
            {
                ((IPersistFile)link).Load(path, 0);   // STGM_READ

                var target = new StringBuilder(1024);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                var arguments = new StringBuilder(4096);
                link.GetArguments(arguments, arguments.Capacity);
                var directory = new StringBuilder(1024);
                link.GetWorkingDirectory(directory, directory.Capacity);

                return new Contents(
                    NullIfEmpty(Environment.ExpandEnvironmentVariables(target.ToString())),
                    NullIfEmpty(arguments.ToString()),
                    NullIfEmpty(Environment.ExpandEnvironmentVariables(directory.ToString())),
                    null);
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }
        catch (Exception ex)
        {
            HandheldLauncher.App.Diagnostics.DebugLog.Write($"[ATALHO] Não consegui ler {Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass { }

    // Ordem da vtable de IShellLinkW (shobjidl_core.h) — não reordenar.
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxChars, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxChars);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxChars);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxChars);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxChars, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}

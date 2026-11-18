using System.Runtime.InteropServices;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// 18/11: seletor de arquivo do Windows que devolve o próprio atalho (.lnk) quando o usuário
/// escolhe um atalho. O seletor do Avalonia usa o padrão do Windows, que troca o atalho pelo
/// destino dele — e aí os argumentos do atalho (ex.: o jogo que o Ryujinx abre) se perdem.
/// Usa GetOpenFileNameW com OFN_NODEREFERENCELINKS; o Windows mostra o seletor moderno.
/// Devolve null se o usuário cancelar ou se der erro.
/// </summary>
internal static class NativeFilePicker
{
    private const int MaxPath = 4096;

    private const int OfnHideReadOnly = 0x00000004;
    private const int OfnNoChangeDir = 0x00000008;
    private const int OfnPathMustExist = 0x00000800;
    private const int OfnFileMustExist = 0x00001000;
    private const int OfnExplorer = 0x00080000;
    private const int OfnNoDereferenceLinks = 0x00100000;
    private const int OfnDontAddToRecent = 0x02000000;

    /// <param name="owner">Janela dona (o seletor fica na frente dela).</param>
    /// <param name="title">Título do seletor.</param>
    /// <param name="filter">Pares "descrição|padrões" (ex.: "Executável ou atalho|*.exe;*.lnk").</param>
    public static string? PickFile(IntPtr owner, string title, params string[] filter)
    {
        if (!OperatingSystem.IsWindows()) return null;

        // Formato do Windows: "descrição\0padrões\0...\0\0".
        var filterText = string.Join("\0", filter.SelectMany(f => f.Split('|', 2))) + "\0\0";
        var filterPtr = Marshal.StringToHGlobalUni(filterText);
        var titlePtr = Marshal.StringToHGlobalUni(title);
        var filePtr = Marshal.AllocHGlobal(MaxPath * 2);
        try
        {
            Marshal.Copy(new byte[MaxPath * 2], 0, filePtr, MaxPath * 2);
            var ofn = new OpenFileName
            {
                StructSize = Marshal.SizeOf<OpenFileName>(),
                Owner = owner,
                Filter = filterPtr,
                FilterIndex = 1,
                File = filePtr,
                MaxFile = MaxPath,
                Title = titlePtr,
                Flags = OfnExplorer | OfnFileMustExist | OfnPathMustExist | OfnNoDereferenceLinks
                        | OfnNoChangeDir | OfnHideReadOnly | OfnDontAddToRecent,
            };
            if (!GetOpenFileNameW(ref ofn)) return null;
            var path = Marshal.PtrToStringUni(filePtr);
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (Exception ex)
        {
            HandheldLauncher.App.Diagnostics.DebugLog.Write($"[SELETOR] GetOpenFileName falhou: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(filterPtr);
            Marshal.FreeHGlobal(titlePtr);
            Marshal.FreeHGlobal(filePtr);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int StructSize;
        public IntPtr Owner;
        public IntPtr Instance;
        public IntPtr Filter;
        public IntPtr CustomFilter;
        public int MaxCustomFilter;
        public int FilterIndex;
        public IntPtr File;
        public int MaxFile;
        public IntPtr FileTitle;
        public int MaxFileTitle;
        public IntPtr InitialDir;
        public IntPtr Title;
        public int Flags;
        public short FileOffset;
        public short FileExtension;
        public IntPtr DefExt;
        public IntPtr CustData;
        public IntPtr Hook;
        public IntPtr TemplateName;
        public IntPtr Reserved;
        public int ReservedInt;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OpenFileName ofn);
}

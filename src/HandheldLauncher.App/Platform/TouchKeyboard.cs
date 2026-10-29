using System.Diagnostics;
using System.Runtime.InteropServices;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Abre o teclado virtual do Windows (o de toque, "TabTip") — bug reportado:
/// "quando clica na caixa de texto não abre o teclado". O Windows só abre
/// esse teclado sozinho pra apps que se anunciam de um jeito que o Avalonia
/// não faz, então o launcher pede direto, pelo mesmo canal COM que o botão
/// de teclado da barra de tarefas usa (ITipInvocation.Toggle). Se isso
/// falhar, cai no teclado clássico (osk.exe), que sempre existe.
/// </summary>
internal static class TouchKeyboard
{
    [ComImport]
    [Guid("37c994e7-432b-4834-a2f7-dce1f13b834b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITipInvocation
    {
        void Toggle(IntPtr hwnd);
    }

    private static readonly Guid UIHostNoLaunch = new("4ce576fa-83dc-4f88-951c-9d0782b4e376");

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    private static DateTime _lastToggle = DateTime.MinValue;

    /// <summary>Mostra o teclado (não faz nada se ele já está visível ou foi pedido há menos de 1,5 s).</summary>
    public static void Show()
    {
        if (IsVisible()) return;
        if (DateTime.UtcNow - _lastToggle < TimeSpan.FromSeconds(1.5)) return;
        Toggle();
    }

    /// <summary>Abre/fecha (botão "⌨ Teclado").</summary>
    public static void Toggle()
    {
        _lastToggle = DateTime.UtcNow;
        try
        {
            EnsureTabTipRunning();
            var type = Type.GetTypeFromCLSID(UIHostNoLaunch);
            if (type is null) throw new InvalidOperationException("UIHostNoLaunch indisponível");
            var tip = (ITipInvocation)Activator.CreateInstance(type)!;
            tip.Toggle(GetDesktopWindow());
            Marshal.ReleaseComObject(tip);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"TouchKeyboard: TabTip falhou ({ex.Message}), abrindo osk.exe");
            try
            {
                Process.Start(new ProcessStartInfo("osk.exe") { UseShellExecute = true });
            }
            catch
            {
                // Melhor esforço.
            }
        }
    }

    private static void EnsureTabTipRunning()
    {
        if (Process.GetProcessesByName("TabTip").Length > 0) return;

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            "microsoft shared", "ink", "TabTip.exe");
        if (!File.Exists(path)) return;

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        Thread.Sleep(400);
    }

    /// <summary>
    /// Windows 10: janela "IPTip_Main_Window" visível e habilitada = teclado
    /// aberto. No Windows 11 essa janela nem sempre existe — aí não dá pra
    /// saber e assume fechado (a trava de 1,5 s em Show evita abrir/fechar em
    /// sequência).
    /// </summary>
    private static bool IsVisible()
    {
        try
        {
            var handle = FindWindow("IPTip_Main_Window", null);
            if (handle == IntPtr.Zero) return false;
            const int GwlStyle = -16;
            const int WsDisabled = 0x08000000;
            return IsWindowVisible(handle) && (GetWindowLong(handle, GwlStyle) & WsDisabled) == 0;
        }
        catch
        {
            return false;
        }
    }
}

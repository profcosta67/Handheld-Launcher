using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Fix for the reported "toda vez que ele abre sempre tá abrindo uma aba
/// nova do launcher" bug: a named Mutex, checked in Program.Main BEFORE
/// Avalonia/BuildAvaloniaApp ever runs, so a second launch (double-clicking
/// the shortcut again, the watchdog scheduled task racing with a manual
/// launch, Modo Xbox re-invoking the "home" app, anything) never gets as
/// far as constructing a second MainWindow at all — it just asks the
/// instance that's already running to come to the front, then exits
/// immediately.
/// 
/// v1.0.0 (macOS/Linux prep): Adicionado suporte cross-platform. O Mutex
/// funciona nativamente no .NET. As chamadas de user32.dll (PostMessage)
/// só são executadas no Windows.
/// </summary>
internal static class SingleInstance
{
    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static readonly IntPtr HwndBroadcast = new(0xffff);

    private const string MutexName = "HandheldLauncher_SingleInstance_7f3a9c2e";
    private const string RestoreMessageName = "HandheldLauncher_RestoreRequest_7f3a9c2e";

    /// <summary>
    /// Registered once at static-init time (same string, every process) so
    /// both the instance that owns the Mutex (listening, via MainWindow's
    /// WndProc hook) and a later instance that lost the Mutex (sending)
    /// agree on the exact same numeric Id without either one guessing it.
    /// Retorna 0 se não for Windows, para evitar carregar user32.dll.
    /// </summary>
    public static readonly uint RestoreMessageId = OperatingSystem.IsWindows()
        ? RegisterWindowMessage(RestoreMessageName)
        : 0;

    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, out var isNewInstance);
        return isNewInstance;
    }

    /// <summary>
    /// Broadcasts the restore message system-wide. Cheap/best-effort: if
    /// nothing is listening (shouldn't happen — TryAcquire just told us
    /// someone owns the Mutex) this is simply a no-op, never a crash.
    /// </summary>
    public static void NotifyExistingInstance()
    {
        if (!OperatingSystem.IsWindows()) return; // Só existe no Windows

        try
        {
            PostMessage(HwndBroadcast, RestoreMessageId, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // Best-effort only.
        }
    }
}
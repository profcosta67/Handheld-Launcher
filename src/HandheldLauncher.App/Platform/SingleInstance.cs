using System.Runtime.InteropServices;

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
/// Deliberately NOT relying on Task Scheduler's own MultipleInstances=
/// IgnoreNew policy (set in Install-Launcher.ps1's watchdog task) to be the
/// only thing preventing this — that only ever sees launches it itself
/// started, so a manual double-click (or anything else that starts the exe
/// outside the scheduled task) would slip right past it. A Mutex inside the
/// process itself catches every path at once.
///
/// "Restore" signaling uses RegisterWindowMessage + a broadcast PostMessage
/// instead of, say, a named pipe or FindWindow+PostMessage by title —
/// RegisterWindowMessage guarantees the same numeric message Id across
/// every process that registers the same string, which is exactly the
/// "both sides agree on one Id without either one hardcoding a guess" this
/// needs, and broadcasting removes any dependency on window titles/classes
/// matching exactly. MainWindow's existing native WndProc hook (see
/// RegisterRestoreHotkey/OnNativeWndProcHook) is where the first instance
/// actually listens for it.
/// </summary>
internal static class SingleInstance
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static readonly IntPtr HwndBroadcast = new(0xffff);

    // Suffixed with a random-looking tag so this never collides with some
    // unrelated app's own "HandheldLauncher"-ish mutex/message name on the
    // same machine.
    private const string MutexName = "HandheldLauncher_SingleInstance_7f3a9c2e";
    private const string RestoreMessageName = "HandheldLauncher_RestoreRequest_7f3a9c2e";

    /// <summary>
    /// Registered once at static-init time (same string, every process) so
    /// both the instance that owns the Mutex (listening, via MainWindow's
    /// WndProc hook) and a later instance that lost the Mutex (sending)
    /// agree on the exact same numeric Id without either one guessing it.
    /// </summary>
    public static readonly uint RestoreMessageId = RegisterWindowMessage(RestoreMessageName);

    // Kept alive for the whole process lifetime in this field — a local
    // variable would be eligible for GC (and therefore for the Mutex being
    // released) the moment TryAcquire returns, which would defeat the
    // entire point.
    private static Mutex? _mutex;

    /// <summary>
    /// True if THIS process is the first/only instance (and now owns the
    /// Mutex for as long as it keeps running) — false if another instance
    /// already holds it, in which case this process should call
    /// NotifyExistingInstance() and exit without doing anything else.
    /// </summary>
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

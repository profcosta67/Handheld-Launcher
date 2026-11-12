using System.Runtime.InteropServices;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Linux (X11/XWayland): pede ao gestor de janelas para ativar e pôr "acima" uma
/// janela nossa, como um pager faria (_NET_ACTIVE_WINDOW com origem 2). Sem isto o
/// GNOME recusa trazer o Quick Settings para a frente de um jogo (mostra só a
/// notificação "está pronto"). Falha em silêncio (log) se não houver X11.
/// </summary>
internal static class LinuxWindowActivator
{
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr d);
    [DllImport("libX11.so.6")] private static extern IntPtr XDefaultRootWindow(IntPtr d);
    [DllImport("libX11.so.6")] private static extern IntPtr XInternAtom(IntPtr d, string name, bool onlyIfExists);
    [DllImport("libX11.so.6")] private static extern int XSendEvent(IntPtr d, IntPtr w, bool propagate, long mask, IntPtr ev);
    [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr d);
    [DllImport("libX11.so.6")] private static extern int XGetWindowProperty(IntPtr d, IntPtr w, IntPtr prop, long offset, long length, bool delete,
        IntPtr reqType, out IntPtr actualType, out int actualFormat, out IntPtr nItems, out IntPtr bytesAfter, out IntPtr data);
    [DllImport("libX11.so.6")] private static extern int XFree(IntPtr p);
    [DllImport("libX11.so.6")] private static extern int XSetTransientForHint(IntPtr d, IntPtr w, IntPtr prop);
    [DllImport("libX11.so.6")] private static extern int XDefaultScreen(IntPtr d);
    [DllImport("libX11.so.6")] private static extern int XDisplayWidth(IntPtr d, int screen);
    [DllImport("libX11.so.6")] private static extern int XDisplayHeight(IntPtr d, int screen);
    [DllImport("libX11.so.6")] private static extern IntPtr XGetAtomName(IntPtr d, IntPtr atom);

    private const int ClientMessage = 33;
    private const long SubstructureNotifyMask = 0x80000;
    private const long SubstructureRedirectMask = 0x100000;

    public static void Raise(IntPtr xid, bool above = true)
    {
        if (!OperatingSystem.IsLinux() || xid == IntPtr.Zero) return;
        IntPtr d = IntPtr.Zero;
        try
        {
            d = XOpenDisplay(IntPtr.Zero);
            if (d == IntPtr.Zero) { DebugLog.Write("[X11] Sem display X11 (Wayland puro?) — ativação ignorada."); return; }
            var root = XDefaultRootWindow(d);
            if (above) Send(d, root, xid, "_NET_WM_STATE", 1, XInternAtom(d, "_NET_WM_STATE_ABOVE", false), IntPtr.Zero);
            Send(d, root, xid, "_NET_ACTIVE_WINDOW", 2, IntPtr.Zero, IntPtr.Zero);
            XFlush(d);
            DebugLog.Write($"[X11] Ativação pedida para a janela 0x{xid.ToInt64():x}.");
        }
        catch (Exception ex) { DebugLog.Write($"[X11] Falha ao ativar janela: {ex.GetType().Name}: {ex.Message}"); }
        finally { if (d != IntPtr.Zero) { try { XCloseDisplay(d); } catch { } } }
    }

    private static int _generation;

    /// <summary>Cancela os Raise com retentativa que ainda estão agendados.</summary>
    public static void CancelPending() => System.Threading.Interlocked.Increment(ref _generation);

    /// <summary>Marca <paramref name="child"/> como janela transitória de <paramref name="parent"/> (fica por cima dela, mesmo em tela cheia, sem precisar de foco).</summary>
    public static void SetTransientFor(IntPtr child, IntPtr parent)
    {
        if (!OperatingSystem.IsLinux() || child == IntPtr.Zero || parent == IntPtr.Zero || child == parent) return;
        IntPtr d = IntPtr.Zero;
        try
        {
            d = XOpenDisplay(IntPtr.Zero);
            if (d == IntPtr.Zero) return;
            XSetTransientForHint(d, child, parent);
            XFlush(d);
            DebugLog.Write($"[X11] QS 0x{child.ToInt64():x} marcado como filho de 0x{parent.ToInt64():x} (sem tirar o foco).");
        }
        catch (Exception ex) { DebugLog.Write($"[X11] Falha em transient_for: {ex.GetType().Name}: {ex.Message}"); }
        finally { if (d != IntPtr.Zero) { try { XCloseDisplay(d); } catch { } } }
    }

    /// <summary>Tamanho REAL da tela agora (o Avalonia guarda o antigo depois de um xrandr).</summary>
    public static (int W, int H)? GetScreenSize()
    {
        if (!OperatingSystem.IsLinux()) return null;
        IntPtr d = IntPtr.Zero;
        try
        {
            d = XOpenDisplay(IntPtr.Zero);
            if (d == IntPtr.Zero) return null;
            var sc = XDefaultScreen(d);
            return (XDisplayWidth(d, sc), XDisplayHeight(d, sc));
        }
        catch { return null; }
        finally { if (d != IntPtr.Zero) { try { XCloseDisplay(d); } catch { } } }
    }

    /// <summary>Diagnóstico: estados _NET_WM_STATE da janela (HIDDEN = minimizada, FULLSCREEN…).</summary>
    public static string DescribeState(IntPtr xid)
    {
        if (!OperatingSystem.IsLinux() || xid == IntPtr.Zero) return "n/d";
        IntPtr d = IntPtr.Zero;
        try
        {
            d = XOpenDisplay(IntPtr.Zero);
            if (d == IntPtr.Zero) return "sem X11";
            var prop = XInternAtom(d, "_NET_WM_STATE", false);
            var rc = XGetWindowProperty(d, xid, prop, 0, 32, false, (IntPtr)4 /* XA_ATOM */,
                out _, out var fmt, out var n, out _, out var data);
            if (rc != 0 || data == IntPtr.Zero) return "(nenhum)";
            try
            {
                var names = new List<string>();
                for (var i = 0; i < (int)n.ToInt64() && fmt == 32; i++)
                {
                    var atom = Marshal.ReadIntPtr(data, i * IntPtr.Size);
                    var namePtr = XGetAtomName(d, atom);
                    if (namePtr != IntPtr.Zero) { names.Add((Marshal.PtrToStringAnsi(namePtr) ?? "?").Replace("_NET_WM_STATE_", "")); XFree(namePtr); }
                }
                return names.Count == 0 ? "(nenhum)" : string.Join(",", names);
            }
            finally { XFree(data); }
        }
        catch (Exception ex) { return $"erro {ex.GetType().Name}"; }
        finally { if (d != IntPtr.Zero) { try { XCloseDisplay(d); } catch { } } }
    }

    /// <summary>Janela ativa agora (_NET_ACTIVE_WINDOW da raiz), ou 0 se não der para saber.</summary>
    public static IntPtr GetActiveWindow()
    {
        if (!OperatingSystem.IsLinux()) return IntPtr.Zero;
        IntPtr d = IntPtr.Zero;
        try
        {
            d = XOpenDisplay(IntPtr.Zero);
            if (d == IntPtr.Zero) return IntPtr.Zero;
            var root = XDefaultRootWindow(d);
            var atom = XInternAtom(d, "_NET_ACTIVE_WINDOW", false);
            var rc = XGetWindowProperty(d, root, atom, 0, 1, false, (IntPtr)33 /* XA_WINDOW */,
                out _, out var fmt, out var n, out _, out var data);
            if (rc != 0 || data == IntPtr.Zero) return IntPtr.Zero;
            try { return fmt == 32 && n.ToInt64() >= 1 ? Marshal.ReadIntPtr(data) : IntPtr.Zero; }
            finally { XFree(data); }
        }
        catch (Exception ex) { DebugLog.Write($"[X11] Falha ao ler a janela ativa: {ex.GetType().Name}: {ex.Message}"); return IntPtr.Zero; }
        finally { if (d != IntPtr.Zero) { try { XCloseDisplay(d); } catch { } } }
    }

    /// <summary>
    /// Raise com verificação: tenta a 0/150/400/900 ms, mas só enquanto a janela
    /// alvo ainda não for a ativa — pedidos a mais bagunçam jogos Wine/SDL que
    /// acabaram de voltar de minimizados (tela preta com o jogo rodando).
    /// </summary>
    public static void RaiseWithRetry(Func<IntPtr> xid, Func<bool> stillWanted, bool above = true, int[]? delaysMs = null, bool logState = false)
    {
        var gen = System.Threading.Interlocked.Increment(ref _generation);
        foreach (var ms in delaysMs ?? new[] { 1, 150, 400, 900 })
        {
            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                if (gen != _generation || !stillWanted()) return; // um pedido mais novo (ou cancelamento) invalida este
                var target = xid();
                if (target == IntPtr.Zero) return;
                var active = GetActiveWindow();
                if (logState) DebugLog.Write($"[X11] Estado da janela 0x{target.ToInt64():x} ({ms} ms): {DescribeState(target)}; ativa=0x{active.ToInt64():x}");
                if (active == target) return;
                Raise(target, above);
            }, TimeSpan.FromMilliseconds(ms));
        }
    }

    private static void Send(IntPtr d, IntPtr root, IntPtr win, string msg, long l0, IntPtr l1, IntPtr l2)
    {
        var ev = Marshal.AllocHGlobal(192);
        try
        {
            for (var i = 0; i < 192; i += 8) Marshal.WriteInt64(ev, i, 0);
            Marshal.WriteInt32(ev, 0, ClientMessage);          // type
            Marshal.WriteIntPtr(ev, 24, d);                    // display
            Marshal.WriteIntPtr(ev, 32, win);                  // window
            Marshal.WriteIntPtr(ev, 40, XInternAtom(d, msg, false)); // message_type
            Marshal.WriteInt32(ev, 48, 32);                    // format
            Marshal.WriteInt64(ev, 56, l0);                    // data.l[0]
            Marshal.WriteIntPtr(ev, 64, l1);                   // data.l[1]
            Marshal.WriteIntPtr(ev, 72, l2);                   // data.l[2]
            XSendEvent(d, root, false, SubstructureRedirectMask | SubstructureNotifyMask, ev);
        }
        finally { Marshal.FreeHGlobal(ev); }
    }
}

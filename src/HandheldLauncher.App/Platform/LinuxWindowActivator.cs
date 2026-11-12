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
    public static void RaiseWithRetry(Func<IntPtr> xid, Func<bool> stillWanted, bool above = true)
    {
        foreach (var ms in new[] { 1, 150, 400, 900 })
        {
            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                if (!stillWanted()) return;
                var target = xid();
                if (target == IntPtr.Zero || GetActiveWindow() == target) return;
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

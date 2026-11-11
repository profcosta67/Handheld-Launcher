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

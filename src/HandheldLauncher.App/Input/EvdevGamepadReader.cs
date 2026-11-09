using System.Runtime.InteropServices;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Input;

/// <summary>
/// Lê o estado do gamepad no Linux via evdev (/dev/input/event*). É o
/// substituto do XInput no Linux (Fase 3). A leitura é não-bloqueante
/// (O_NONBLOCK) para o timer de 50 ms do GamepadPoller nunca ficar preso.
/// </summary>
internal sealed class EvdevGamepadReader : IDisposable
{
    // Códigos do kernel (linux/input-event-codes.h)
    public const ushort BtnSouth = 0x130;   // A (Xbox) / Cross (PS)
    public const ushort BtnEast = 0x131;    // B / Circle
    public const ushort BtnNorth = 0x133;   // Y / Triangle
    public const ushort BtnWest = 0x134;    // X / Square
    public const ushort BtnSelect = 0x13a;  // View / Share
    public const ushort BtnStart = 0x13b;   // Menu / Options
    public const ushort BtnTl = 0x136;      // LB / L1
    public const ushort BtnTr = 0x137;      // RB / R1
    public const ushort BtnThumbl = 0x13d;  // L3
    public const ushort BtnThumbr = 0x13e;  // R3
    public const ushort BtnDpadUp = 0x220;
    public const ushort BtnDpadDown = 0x221;
    public const ushort BtnDpadLeft = 0x222;
    public const ushort BtnDpadRight = 0x223;

    private const ushort EvKey = 0x01;
    private const ushort EvAbs = 0x03;
    private const ushort AbsHat0X = 0x10;
    private const ushort AbsHat0Y = 0x11;

    [StructLayout(LayoutKind.Sequential)]
    private struct InputEvent
    {
        public long Sec;
        public long USec;
        public ushort Type;
        public ushort Code;
        public int Value;
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "open")]
    private static extern int Open(string pathname, int flags);

    [DllImport("libc", SetLastError = true, EntryPoint = "read")]
    private static extern long Read(int fd, IntPtr buffer, ulong count);

    [DllImport("libc", SetLastError = true, EntryPoint = "close")]
    private static extern int Close(int fd);

    private const int O_RDONLY = 0;
    private const int O_NONBLOCK = 0x0800; // Linux

    private readonly List<int> _fds = new();
    private readonly HashSet<ushort> _down = new();
    private readonly byte[] _raw = new byte[24];

    public EvdevGamepadReader()
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles("/dev/input", "event*"))
            {
                var fd = Open(path, O_RDONLY | O_NONBLOCK);
                if (fd >= 0) _fds.Add(fd);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[EVDEV] Falha ao enumerar /dev/input: {ex.Message}");
        }

        if (_fds.Count == 0)
            DebugLog.Write("[EVDEV] Nenhum /dev/input/event* disponível.");
    }

    public IReadOnlyCollection<ushort> DownButtons => _down;
    public int HatX { get; private set; }
    public int HatY { get; private set; }

    public void Poll()
    {
        foreach (var fd in _fds)
        {
            while (true)
            {
                long n;
                unsafe
                {
                    fixed (byte* p = _raw)
                    {
                        n = Read(fd, (IntPtr)p, 24);
                    }
                }

                if (n <= 0) break; // EAGAIN (sem dados) ou erro

                var type = BitConverter.ToUInt16(_raw, 16);
                var code = BitConverter.ToUInt16(_raw, 18);
                var value = BitConverter.ToInt32(_raw, 20);

                if (type == EvKey)
                {
                    if (value == 1) _down.Add(code);
                    else _down.Remove(code);
                }
                else if (type == EvAbs)
                {
                    if (code == AbsHat0X) HatX = value;
                    else if (code == AbsHat0Y) HatY = value;
                }
            }
        }
    }

    public void Dispose()
    {
        foreach (var fd in _fds)
        {
            try { Close(fd); } catch { /* já fechado */ }
        }
        _fds.Clear();
    }
}
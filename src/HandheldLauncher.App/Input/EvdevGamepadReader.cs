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
    private const ushort AbsX = 0x00;
    private const ushort AbsY = 0x01;

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

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static extern int Ioctl(int fd, ulong request, byte[] data);

    private const int O_RDONLY = 0;
    private const int O_NONBLOCK = 0x0800; // Linux

    private readonly Dictionary<string, int> _fdByPath = new();
    private readonly Dictionary<int, (int Min, int Max)> _rangeX = new();
    private readonly Dictionary<int, (int Min, int Max)> _rangeY = new();
    private readonly HashSet<string> _deniedLogged = new();
    private readonly HashSet<string> _notGamepad = new();
    private int _buttonLogs;
    private DateTime _lastScan = DateTime.MinValue;
    private readonly Dictionary<int, int> _rawX = new();
    private readonly Dictionary<int, int> _rawY = new();
    private readonly HashSet<ushort> _down = new();
    private readonly byte[] _raw = new byte[24];

    public EvdevGamepadReader() => Scan();

    /// <summary>Há pelo menos um dispositivo de entrada aberto com sinal de gamepad.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>Eixo analógico esquerdo normalizado (-1..1).</summary>
    public double StickX { get; private set; }
    public double StickY { get; private set; }

    // Reabre /dev/input/event* de tempos a tempos: o controle pode ligar depois
    // do launcher (login automático) e as permissões podem aparecer mais tarde.
    private void Scan()
    {
        _lastScan = DateTime.UtcNow;
        try
        {
            foreach (var path in Directory.EnumerateFiles("/dev/input", "event*"))
            {
                if (_fdByPath.ContainsKey(path) || _notGamepad.Contains(path)) continue;
                var fd = Open(path, O_RDONLY | O_NONBLOCK);
                if (fd < 0)
                {
                    if (_deniedLogged.Add(path))
                        DebugLog.Write($"[EVDEV] Sem acesso a {path} (errno={Marshal.GetLastWin32Error()}). Se o controle não responder, adicione o utilizador ao grupo 'input'.");
                    continue;
                }
                // Só gamepads (BTN_SOUTH): descarta teclado, mouse, touchscreen, IMU.
                if (!HasGamepadButtons(fd)) { try { Close(fd); } catch { } _notGamepad.Add(path); continue; }
                _fdByPath[path] = fd;
                _rangeX[fd] = ReadRange(fd, AbsX);
                _rangeY[fd] = ReadRange(fd, AbsY);
                _rawX[fd] = 0; _rawY[fd] = 0;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[EVDEV] Falha ao enumerar /dev/input: {ex.Message}");
        }
    }

    private static bool HasGamepadButtons(int fd)
    {
        try
        {
            var bits = new byte[96]; // KEY_MAX = 0x2ff → 96 bytes
            // EVIOCGBIT(EV_KEY, 96) = _IOC(_IOC_READ, 'E', 0x20 + EV_KEY, 96)
            if (Ioctl(fd, 0x80000000UL | (96UL << 16) | (0x45UL << 8) | 0x21UL, bits) < 0) return false;
            return (bits[BtnSouth / 8] & (1 << (BtnSouth % 8))) != 0;
        }
        catch { return false; }
    }

    private static (int Min, int Max) ReadRange(int fd, ushort axis)
    {
        try
        {
            var buf = new byte[24]; // struct input_absinfo: value,min,max,fuzz,flat,res
            // EVIOCGABS(abs) = _IOR('E', 0x40+abs, struct input_absinfo)
            if (Ioctl(fd, 0x80184540UL + axis, buf) < 0) return (0, 0);
            return (BitConverter.ToInt32(buf, 4), BitConverter.ToInt32(buf, 8));
        }
        catch { return (0, 0); }
    }

    private static double Normalize(int v, (int Min, int Max) r)
    {
        if (r.Max <= r.Min) return 0;
        var mid = (r.Min + r.Max) / 2.0;
        var half = (r.Max - r.Min) / 2.0;
        return Math.Clamp((v - mid) / half, -1, 1);
    }

    public IReadOnlyCollection<ushort> DownButtons => _down;
    public int HatX { get; private set; }
    public int HatY { get; private set; }

    public void Poll()
    {
        if (DateTime.UtcNow - _lastScan > TimeSpan.FromSeconds(3)) Scan();

        List<string>? dead = null;
        double bestX = 0, bestY = 0;
        foreach (var (path, fd) in _fdByPath)
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

                if (n <= 0)
                {
                    // EAGAIN = sem dados; qualquer outro erro (ENODEV) = dispositivo removido.
                    if (n < 0 && Marshal.GetLastWin32Error() is not (11 or 4)) (dead ??= new()).Add(path);
                    break;
                }

                var type = BitConverter.ToUInt16(_raw, 16);
                var code = BitConverter.ToUInt16(_raw, 18);
                var value = BitConverter.ToInt32(_raw, 20);

                if (type == EvKey)
                {
                    if (value == 1 && code is >= 0x130 and <= 0x13e && _buttonLogs < 60)
                    {
                        _buttonLogs++;
                        DebugLog.Write($"[EVDEV] botão 0x{code:x} pressionado ({path}).");
                    }
                    if (value == 1) _down.Add(code);
                    else if (value == 0) _down.Remove(code);
                }
                else if (type == EvAbs)
                {
                    if (code == AbsHat0X) HatX = value;
                    else if (code == AbsHat0Y) HatY = value;
                    else if (code == AbsX) _rawX[fd] = value;
                    else if (code == AbsY) _rawY[fd] = value;
                }
            }

            var rx = _rangeX.GetValueOrDefault(fd);
            var ry = _rangeY.GetValueOrDefault(fd);
            if (rx.Max > rx.Min && ry.Max > ry.Min)
            {
                var nx = Normalize(_rawX[fd], rx); var ny = Normalize(_rawY[fd], ry);
                if (Math.Abs(nx) > Math.Abs(bestX)) bestX = nx;
                if (Math.Abs(ny) > Math.Abs(bestY)) bestY = ny;
            }
        }

        if (dead != null)
            foreach (var path in dead)
            {
                var fd = _fdByPath[path];
                try { Close(fd); } catch { }
                _fdByPath.Remove(path); _rangeX.Remove(fd); _rangeY.Remove(fd); _rawX.Remove(fd); _rawY.Remove(fd);
                _down.Clear(); HatX = 0; HatY = 0;
            }

        StickX = bestX; StickY = bestY;
        IsConnected = _fdByPath.Count > 0;
    }


    public void Dispose()
    {
        foreach (var fd in _fdByPath.Values)
        {
            try { Close(fd); } catch { /* já fechado */ }
        }
        _fdByPath.Clear();
    }
}
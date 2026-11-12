using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Input;

/// <summary>
/// v0.16.3: terceiro leitor de L3+R3, direto do HID do controle (Raw Input).
///
/// Por quê: o log do beta.9 (04/11) mostrou que, com o JOGO em primeiro
/// plano, nem o GameInput (mesmo com entrada em segundo plano ligada) nem o
/// XInput do launcher recebem botão algum — o Windows entrega o controle
/// "parado" pra quem não está em foco. Days Gone, Eden e Far Cry 6 falharam
/// assim; o Far Cry 6 só funcionou quando o launcher ainda estava em foco.
///
/// Raw Input com RIDEV_INPUTSINK é o caminho que Steam/Handheld Companion
/// usam: o Windows entrega os relatórios HID do controle a uma janela nossa
/// mesmo com outra janela em foco. Roda numa thread própria do PROCESSO
/// AJUDANTE (nunca no launcher): se algo der errado aqui, só o ajudante cai.
///
/// Mapeamento: a coleção HID "compatível com XInput" (nome do dispositivo
/// com "IG_" — Legion Go 2, ROG Ally, controles Xbox) expõe os botões
/// 1..10 = A, B, X, Y, LB, RB, Back, Start, L3, R3. Outros controles HID
/// só são registrados no log (diagnóstico), sem disparar nada.
/// </summary>
internal static unsafe class HidComboReader
{
    private const ushort UsagePageGenericDesktop = 0x01;
    private const ushort UsageJoystick = 0x04;
    private const ushort UsageGamepad = 0x05;
    private const ushort UsagePageButton = 0x09;
    private const ushort XInputHidL3 = 9;
    private const ushort XInputHidR3 = 10;

    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIDEV_DEVNOTIFY = 0x00002000;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIM_TYPEHID = 2;
    private const uint RIDI_PREPARSEDDATA = 0x20000005;
    private const uint RIDI_DEVICENAME = 0x20000007;
    private const uint WM_INPUT = 0x00FF;
    private const uint WM_INPUT_DEVICE_CHANGE = 0x00FE;
    private const int HIDP_STATUS_SUCCESS = 0x00110000;
    private const int HidP_Input = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICELIST
    {
        public IntPtr Device;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint Size;
        public uint Style;
        public IntPtr WndProc;
        public int ClsExtra;
        public int WndExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr IconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    // CharSet.Unicode → GetRawInputDeviceInfoW. Sem isso o .NET chamava a
    // versão A (ANSI) e o nome do dispositivo vinha "embaralhado" ao ser lido
    // como UTF-16 (beta.10): o "IG_" não era achado e o HID não disparava.
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetRawInputDeviceInfoW")]
    private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList(IntPtr list, ref uint count, uint size);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG msg);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    // v0.25.9: hat switch (D-pad) para a ponte do Quick Settings.
    [DllImport("hid.dll")]
    private static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection,
        ushort usage, out uint usageValue, IntPtr preparsedData, byte* report, uint reportLength);

    private const ushort UsageHatSwitch = 0x39;
    private const ushort UsageAxisX = 0x30;
    private const ushort UsageAxisY = 0x31;

    // HIDP_VALUE_CAPS (72 bytes em x64/x86): só os campos que usamos.
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    private struct HidpValueCaps
    {
        [FieldOffset(18)] public ushort BitSize;
        [FieldOffset(40)] public int LogicalMin;
        [FieldOffset(44)] public int LogicalMax;
    }

    [DllImport("hid.dll")]
    private static extern int HidP_GetSpecificValueCaps(int reportType, ushort usagePage, ushort linkCollection,
        ushort usage, HidpValueCaps* valueCaps, ref ushort valueCapsLength, IntPtr preparsedData);

    private struct AxisCaps
    {
        public bool Ok;
        public int Min;
        public int Max;
        public int Bits;
    }

    private static AxisCaps ReadAxisCaps(IntPtr preparsed, ushort usage)
    {
        try
        {
            HidpValueCaps* buf = stackalloc HidpValueCaps[4];
            ushort count = 4;
            var status = HidP_GetSpecificValueCaps(HidP_Input, UsagePageGenericDesktop, 0, usage, buf, ref count, preparsed);
            if (status != HIDP_STATUS_SUCCESS || count < 1) return default;
            var caps = buf[0];
            var max = caps.LogicalMax;
            // Eixo sem sinal de 16 bits declarado como 0..0xFFFF pode vir com max = -1.
            if (caps.LogicalMin >= 0 && max < 0 && caps.BitSize is > 0 and < 32) max = (1 << caps.BitSize) - 1;
            if (max <= caps.LogicalMin) return default;
            return new AxisCaps { Ok = true, Min = caps.LogicalMin, Max = max, Bits = caps.BitSize };
        }
        catch
        {
            return default;
        }
    }

    /// <summary>Valor bruto do eixo → -1..1 usando a faixa REAL do descritor (centro = meio da faixa).</summary>
    private static double NormalizeAxis(uint raw, AxisCaps caps)
    {
        var value = (long)raw;
        if (caps.Min < 0)
        {
            if (caps.Bits >= 32) value = (int)raw;
            else if (caps.Bits > 0)
            {
                var shift = 32 - caps.Bits;
                value = (int)(raw << shift) >> shift;   // extensão de sinal
            }
        }
        var mid = (caps.Min + (double)caps.Max) / 2.0;
        var half = (caps.Max - (double)caps.Min) / 2.0;
        return Math.Clamp((value - mid) / half, -1.0, 1.0);
    }

    private static int _stickChangesLogged;
    private static int _hatChangesLogged;
    private static uint _lastHat = uint.MaxValue;

    [DllImport("hid.dll")]
    private static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection,
        ushort* usageList, ref uint usageLength, IntPtr preparsedData, byte* report, uint reportLength);

    private sealed class DeviceInfo
    {
        public required string ShortName;
        public required bool IsXInputCompatible;
        public IntPtr PreparsedData;   // memória nossa (Marshal.AllocHGlobal), vive enquanto o ajudante vive
        public string LastButtons = "";
        public bool WasDown;
        public int PadButtons;      // v0.25.9: estado deste controle para a ponte do QS
        public int PadDirections;   // D-pad (hat) ou, se solto, analógico
        public int PadHatDirections; // só o D-pad
        public int StickDirection;  // só o analógico (para a histerese)
        public AxisCaps AxisX;
        public AxisCaps AxisY;
    }

    private static readonly Dictionary<IntPtr, DeviceInfo> Devices = new();
    private static WndProcDelegate? _wndProc;   // referência viva: o Windows chama de volta
    private static Action? _onCombo;
    private static int _buttonChangesLogged;

    /// <summary>Total de relatórios HID recebidos desde o último heartbeat (lido pelo ajudante).</summary>
    public static int ReportsSinceHeartbeat;

    public static void Start(Action onCombo)
    {
        _onCombo = onCombo;
        var thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "HidComboReader",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void Run()
    {
        try
        {
            LogDevices();

            _wndProc = WndProc;
            var instance = GetModuleHandle(null);
            var wc = new WNDCLASSEX
            {
                Size = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                WndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                Instance = instance,
                ClassName = "HandheldLauncher_HidComboReader",
            };
            if (RegisterClassEx(ref wc) == 0)
            {
                DebugLog.Write($"HidComboReader: RegisterClassEx falhou (erro={Marshal.GetLastWin32Error()}).");
                return;
            }

            // Janela comum que nunca é mostrada (sem WS_VISIBLE). Não usa
            // HWND_MESSAGE: entrega em segundo plano (INPUTSINK) precisa de
            // uma janela de nível de topo.
            var hwnd = CreateWindowEx(0x00000080 /* WS_EX_TOOLWINDOW */, wc.ClassName, "HandheldLauncher HID",
                0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                DebugLog.Write($"HidComboReader: CreateWindowEx falhou (erro={Marshal.GetLastWin32Error()}).");
                return;
            }

            var devices = new[]
            {
                new RAWINPUTDEVICE { UsagePage = UsagePageGenericDesktop, Usage = UsageGamepad, Flags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, Target = hwnd },
                new RAWINPUTDEVICE { UsagePage = UsagePageGenericDesktop, Usage = UsageJoystick, Flags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, Target = hwnd },
            };
            if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            {
                DebugLog.Write($"HidComboReader: RegisterRawInputDevices falhou (erro={Marshal.GetLastWin32Error()}).");
                return;
            }

            DebugLog.Write("HidComboReader: ouvindo o HID do controle em segundo plano (Raw Input).");

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"HidComboReader: parou ({ex.Message}).");
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == WM_INPUT) HandleInput(lParam);
            else if (msg == WM_INPUT_DEVICE_CHANGE)
            {
                // wParam 1 = chegou, 2 = saiu. Saiu: esquece o cache (o handle muda).
                if ((long)wParam == 2 && Devices.Remove(lParam, out var gone))
                {
                    if (gone.PreparsedData != IntPtr.Zero) Marshal.FreeHGlobal(gone.PreparsedData);
                    DebugLog.Write($"HidComboReader: dispositivo saiu ({gone.ShortName}).");
                    PublishPadState(); // o controle que saiu não deixa botão "preso" na ponte
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"HidComboReader: erro lendo relatório ({ex.Message}).");
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Publica a UNIÃO dos controles compatíveis com XInput: dois controles
    /// (ou um físico + um virtual espelhado) intercalando relatórios não
    /// contam o mesmo aperto duas vezes.
    /// </summary>
    private static void PublishPadState()
    {
        var allButtons = 0;
        var allDirections = 0;
        foreach (var d in Devices.Values)
        {
            if (!d.IsXInputCompatible) continue;
            allButtons |= d.PadButtons;
            allDirections |= d.PadDirections;
        }
        HidPadBridge.Publish(allButtons, allDirections);
    }

    private static void HandleInput(IntPtr rawInputHandle)
    {
        var headerSize = (uint)sizeof(RAWINPUTHEADER);
        uint size = 0;
        GetRawInputData(rawInputHandle, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0 || size > 64 * 1024) return;

        var buffer = stackalloc byte[(int)size];
        if (GetRawInputData(rawInputHandle, RID_INPUT, (IntPtr)buffer, ref size, headerSize) != size) return;

        var header = (RAWINPUTHEADER*)buffer;
        if (header->Type != RIM_TYPEHID) return;

        // RAWHID logo depois do cabeçalho: dwSizeHid, dwCount, bRawData[].
        var hid = buffer + headerSize;
        var reportSize = *(uint*)hid;
        var reportCount = *(uint*)(hid + 4);
        var reports = hid + 8;
        if (reportSize == 0 || reportCount == 0 || 8 + (long)reportSize * reportCount > size - headerSize) return;

        Interlocked.Increment(ref ReportsSinceHeartbeat);

        var device = GetDevice(header->Device);
        if (device is null || device.PreparsedData == IntPtr.Zero) return;

        var usages = stackalloc ushort[64];
        for (uint i = 0; i < reportCount; i++)
        {
            var report = reports + i * reportSize;
            uint count = 64;
            var status = HidP_GetUsages(HidP_Input, UsagePageButton, 0, usages, ref count,
                device.PreparsedData, report, reportSize);
            if (status != HIDP_STATUS_SUCCESS) continue;

            var hasL3 = false;
            var hasR3 = false;
            var sb = new StringBuilder();
            for (var u = 0; u < count; u++)
            {
                if (usages[u] == XInputHidL3) hasL3 = true;
                if (usages[u] == XInputHidR3) hasR3 = true;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(usages[u]);
            }

            var buttons = sb.ToString();
            if (buttons != device.LastButtons)
            {
                device.LastButtons = buttons;
                if (_buttonChangesLogged < 60)
                {
                    _buttonChangesLogged++;
                    DebugLog.Write($"HidComboReader: [{device.ShortName}] botões=[{buttons}] · em foco: {ForegroundDescription()}");
                }
            }

            if (!device.IsXInputCompatible) continue;

            // v0.25.9: publica botões + D-pad para o Quick Settings (HidPadBridge).
            var buttonMask = 0;
            for (var u = 0; u < count; u++)
                if (usages[u] is >= 1 and <= 10) buttonMask |= 1 << (usages[u] - 1);
            var hatDirections = device.PadHatDirections; // relatório sem hat: mantém o último
            if (HidP_GetUsageValue(HidP_Input, UsagePageGenericDesktop, 0, UsageHatSwitch, out var hat,
                    device.PreparsedData, report, reportSize) == HIDP_STATUS_SUCCESS)
            {
                hatDirections = HidPadBridge.DirectionsFromHat(hat);
                if (hat != _lastHat)
                {
                    _lastHat = hat;
                    if (_hatChangesLogged < 30)
                    {
                        _hatChangesLogged++;
                        DebugLog.Write($"HidComboReader: [{device.ShortName}] D-pad hat={hat} → direções={hatDirections}");
                    }
                }
            }
            device.PadHatDirections = hatDirections;

            // Analógico esquerdo → uma direção (eixo dominante, com histerese).
            // O D-pad tem prioridade, como no GamepadPoller.
            var stickDirection = device.StickDirection;
            if (device.AxisX.Ok && device.AxisY.Ok
                && HidP_GetUsageValue(HidP_Input, UsagePageGenericDesktop, 0, UsageAxisX, out var rawX,
                       device.PreparsedData, report, reportSize) == HIDP_STATUS_SUCCESS
                && HidP_GetUsageValue(HidP_Input, UsagePageGenericDesktop, 0, UsageAxisY, out var rawY,
                       device.PreparsedData, report, reportSize) == HIDP_STATUS_SUCCESS)
            {
                var nx = NormalizeAxis(rawX, device.AxisX);
                var ny = NormalizeAxis(rawY, device.AxisY);
                stickDirection = HidPadBridge.DirectionFromStick(nx, ny, device.StickDirection);
                if (stickDirection != device.StickDirection && _stickChangesLogged < 30)
                {
                    _stickChangesLogged++;
                    DebugLog.Write($"HidComboReader: [{device.ShortName}] analógico x={nx:F2} y={ny:F2} (y+ = baixo) → direção={stickDirection}");
                }
            }
            device.StickDirection = stickDirection;
            device.PadDirections = hatDirections != 0 ? hatDirections : stickDirection;
            device.PadButtons = buttonMask;

            PublishPadState();

            var isDown = hasL3 && hasR3;
            if (isDown && !device.WasDown)
            {
                DebugLog.Write($"HidComboReader: L3+R3 (via HID, {device.ShortName}) · em foco: {ForegroundDescription()}");
                _onCombo?.Invoke();
            }
            device.WasDown = isDown;
        }
    }

    private static DeviceInfo? GetDevice(IntPtr handle)
    {
        if (Devices.TryGetValue(handle, out var known)) return known;

        var name = GetDeviceName(handle) ?? "?";
        var info = new DeviceInfo
        {
            ShortName = ShortenName(name),
            IsXInputCompatible = name.Contains("IG_", StringComparison.OrdinalIgnoreCase),
        };

        uint size = 0;
        GetRawInputDeviceInfo(handle, RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
        if (size > 0 && size < 1024 * 1024)
        {
            var data = Marshal.AllocHGlobal((int)size);
            if (GetRawInputDeviceInfo(handle, RIDI_PREPARSEDDATA, data, ref size) == size)
                info.PreparsedData = data;
            else
                Marshal.FreeHGlobal(data);
        }

        if (info.IsXInputCompatible && info.PreparsedData != IntPtr.Zero)
        {
            info.AxisX = ReadAxisCaps(info.PreparsedData, UsageAxisX);
            info.AxisY = ReadAxisCaps(info.PreparsedData, UsageAxisY);
            DebugLog.Write(info.AxisX.Ok && info.AxisY.Ok
                ? $"HidComboReader: [{info.ShortName}] analógico esquerdo: X {info.AxisX.Min}..{info.AxisX.Max} ({info.AxisX.Bits} bits), Y {info.AxisY.Min}..{info.AxisY.Max} ({info.AxisY.Bits} bits)."
                : $"HidComboReader: [{info.ShortName}] eixos X/Y não encontrados — o analógico não navega o QS no jogo.");
        }

        Devices[handle] = info;
        DebugLog.Write($"HidComboReader: dispositivo novo {info.ShortName} (compatível XInput={info.IsXInputCompatible}, " +
                       $"descritor={(info.PreparsedData != IntPtr.Zero ? "ok" : "falhou")}).");
        return info;
    }

    private static string? GetDeviceName(IntPtr handle)
    {
        uint chars = 0;
        GetRawInputDeviceInfo(handle, RIDI_DEVICENAME, IntPtr.Zero, ref chars);
        if (chars == 0 || chars > 4096) return null;
        var buffer = Marshal.AllocHGlobal((int)chars * 2);
        try
        {
            return GetRawInputDeviceInfo(handle, RIDI_DEVICENAME, buffer, ref chars) == uint.MaxValue
                ? null
                : Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>"\\?\HID#VID_17EF&amp;PID_61EB&amp;IG_00#9&amp;..." → "VID_17EF&amp;PID_61EB&amp;IG_00".</summary>
    private static string ShortenName(string name)
    {
        var parts = name.Split('#');
        return parts.Length > 1 ? parts[1] : name;
    }

    /// <summary>Registra no log os controles HID que o Windows conhece (só VID/PID, sem número de série).</summary>
    private static void LogDevices()
    {
        uint count = 0;
        var itemSize = (uint)sizeof(RAWINPUTDEVICELIST);
        if (GetRawInputDeviceList(IntPtr.Zero, ref count, itemSize) != 0 || count == 0 || count > 512) return;

        var list = stackalloc RAWINPUTDEVICELIST[(int)count];
        var got = GetRawInputDeviceList((IntPtr)list, ref count, itemSize);
        if (got == uint.MaxValue) return;

        var names = new List<string>();
        for (var i = 0; i < got; i++)
        {
            if (list[i].Type != RIM_TYPEHID) continue;
            var name = GetDeviceName(list[i].Device);
            if (name is not null && name.Contains("IG_", StringComparison.OrdinalIgnoreCase))
                names.Add(ShortenName(name));
        }
        DebugLog.Write(names.Count == 0
            ? "HidComboReader: nenhum controle compatível com XInput na lista HID."
            : $"HidComboReader: controles HID compatíveis com XInput: {string.Join(" | ", names)}");
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);

    /// <summary>Janela em primeiro plano agora: "'Days Gone' (DaysGone.exe)". Usado no log pra confirmar a hipótese do foco.</summary>
    public static string ForegroundDescription()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "nenhuma janela";
            var title = new StringBuilder(128);
            GetWindowText(hwnd, title, title.Capacity);
            GetWindowThreadProcessId(hwnd, out var pid);
            string process;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                process = p.ProcessName;
            }
            catch
            {
                process = $"pid {pid}";
            }
            return $"'{title}' ({process})";
        }
        catch
        {
            return "?";
        }
    }
}

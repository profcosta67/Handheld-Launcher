using System.Runtime.InteropServices;
using HandheldLauncher.App.Diagnostics;
using System.Text.Json;

namespace HandheldLauncher.App.Platform;

internal static class DisplayColorManager
{
    public static readonly string[] ModeNames =
        ["Padrão", "Vívido", "OLED (contraste alto)", "Noturno (luz quente)", "Confortável"];

    [StructLayout(LayoutKind.Sequential)]
    private struct Ramp
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Red;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Green;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Blue;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool GetDeviceGammaRamp(IntPtr hdc, ref Ramp lpRamp);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool SetDeviceGammaRamp(IntPtr hdc, ref Ramp lpRamp);

    private readonly record struct ChannelCurve(double Gamma, double Contrast, double Scale);
    private readonly record struct ModePreset(ChannelCurve R, ChannelCurve G, ChannelCurve B);

    private static readonly ModePreset[] Presets =
    [
        default,
        new(new(1.0, 1.15, 1.0), new(1.0, 1.15, 1.0), new(1.0, 1.15, 1.0)),
        new(new(1.08, 1.35, 1.0), new(1.08, 1.35, 1.0), new(1.08, 1.35, 1.0)),
        new(new(1.0, 1.0, 1.0), new(1.0, 1.0, 0.90), new(1.05, 1.0, 0.60)),
        new(new(0.95, 0.90, 0.92), new(0.95, 0.90, 0.92), new(0.95, 0.90, 0.88)),
    ];

    private static readonly object Sync = new();
    private static Ramp? _original;
    private static bool _captured;
    private static int _currentMode = -1;
    private static bool _hooksRegistered;

    private static readonly string BackupPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HandheldLauncher", "gamma_backup.bin");

    private static void EnsureExitHooksRegistered()
    {
        if (_hooksRegistered) return;
        _hooksRegistered = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreOriginal();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => RestoreOriginal();
    }

    public static void CaptureOriginal()
    {
        lock (Sync) { CaptureOriginalLocked(); }
    }

    private static void CaptureOriginalLocked()
    {
        if (_captured) return;
        EnsureExitHooksRegistered();

        try
        {
            // 🟢 Tenta carregar do backup primeiro (evita acumular filtros em caso de crash)
            if (File.Exists(BackupPath))
            {
                var json = File.ReadAllText(BackupPath);
                var backup = JsonSerializer.Deserialize<RampBackup>(json);
                if (backup != null && backup.Red.Length == 256 && backup.Green.Length == 256 && backup.Blue.Length == 256)
                {
                    _original = new Ramp { Red = backup.Red, Green = backup.Green, Blue = backup.Blue };
                    _captured = true;
                    _currentMode = 0;
                    DebugLog.Write("[DisplayColor] Curva original carregada do backup em disco.");
                    return;
                }
            }

            IntPtr hdc = IntPtr.Zero;
            try
            {
                hdc = GetDC(IntPtr.Zero);
                if (hdc == IntPtr.Zero) return;

                var ramp = NewRamp();
                if (!GetDeviceGammaRamp(hdc, ref ramp))
                {
                    DebugLog.Write($"[DisplayColor] GetDeviceGammaRamp falhou (Win32={Marshal.GetLastWin32Error()}).");
                    return;
                }

                _original = CloneRamp(ramp);
                _captured = true;
                _currentMode = 0;

                // 🟢 Salva o backup em disco
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
                    
                    // 🟢 CORREÇÃO: extrai o valor nulo-verificado uma única vez
                    var original = _original!.Value;
                    var backup = new RampBackup
                    {
                        Red = original.Red,
                        Green = original.Green,
                        Blue = original.Blue
                    };
                    File.WriteAllText(BackupPath, JsonSerializer.Serialize(backup));
                }
                catch { /* Melhor esforço */ }

                DebugLog.Write("[DisplayColor] Curva original do Windows capturada e salva.");
            }
            finally
            {
                if (hdc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdc);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[DisplayColor] CaptureOriginal EXCEPTION {ex}");
        }
    }

    public static void Apply(int modeIndex)
    {
        lock (Sync)
        {
            try
            {
                modeIndex = Math.Clamp(modeIndex, 0, Presets.Length - 1);
                CaptureOriginalLocked();
                if (!_captured || _original is not { } original) return;

                if (modeIndex == _currentMode) return;

                if (modeIndex == 0)
                {
                    if (SetRamp(CloneRamp(original)))
                    {
                        _currentMode = 0;
                        DebugLog.Write("[DisplayColor] Modo Padrão: curva original restaurada.");
                    }
                    return;
                }

                var preset = Presets[modeIndex];
                var ramp = new Ramp
                {
                    Red = BuildChannelCurve(preset.R, original.Red),
                    Green = BuildChannelCurve(preset.G, original.Green),
                    Blue = BuildChannelCurve(preset.B, original.Blue),
                };

                if (SetRamp(ramp))
                {
                    _currentMode = modeIndex;
                    DebugLog.Write($"[DisplayColor] Modo {ModeNames[modeIndex]} aplicado sobre a curva original.");
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"DisplayColorManager.Apply EXCEPTION {ex}");
            }
        }
    }

    public static void RestoreOriginal()
    {
        lock (Sync)
        {
            try
            {
                if (_original is { } ramp && SetRamp(CloneRamp(ramp)))
                {
                    _currentMode = 0;
                    DebugLog.Write("[DisplayColor] Curva original restaurada.");
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"DisplayColorManager.RestoreOriginal EXCEPTION {ex}");
            }
        }
    }

    private static bool SetRamp(Ramp ramp)
    {
        IntPtr hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return false;
        try { return SetDeviceGammaRamp(hdc, ref ramp); }
        finally { ReleaseDC(IntPtr.Zero, hdc); }
    }

    private static Ramp NewRamp() => new() { Red = new ushort[256], Green = new ushort[256], Blue = new ushort[256] };
    private static Ramp CloneRamp(Ramp source) => new()
    {
        Red = (ushort[])source.Red.Clone(),
        Green = (ushort[])source.Green.Clone(),
        Blue = (ushort[])source.Blue.Clone(),
    };

    private static ushort[] BuildChannelCurve(ChannelCurve curve, ushort[] originalChannel)
    {
        var values = new ushort[256];
        for (var i = 0; i < 256; i++)
        {
            var x = originalChannel[i] / 65535.0;
            var contrasted = Math.Clamp(0.5 + (x - 0.5) * curve.Contrast, 0.0, 1.0);
            var gamma = Math.Pow(contrasted, curve.Gamma);
            var final = Math.Clamp(gamma * curve.Scale, 0.0, 1.0);
            values[i] = (ushort)Math.Round(final * 65535.0);
        }
        return values;
    }

    private sealed class RampBackup
    {
        public ushort[] Red { get; set; } = new ushort[256];
        public ushort[] Green { get; set; } = new ushort[256];
        public ushort[] Blue { get; set; } = new ushort[256];
    }
}
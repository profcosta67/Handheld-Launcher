using System.Runtime.InteropServices;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Modos de otimização de imagem/cor. Cada preset é calculado sempre sobre
/// a curva original capturada do Windows, nunca sobre o último preset aplicado.
/// Isso evita acumular OLED + Noturno + outros efeitos ao trocar de modo.
/// </summary>
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

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool GetDeviceGammaRamp(IntPtr hdc, ref Ramp lpRamp);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool SetDeviceGammaRamp(IntPtr hdc, ref Ramp lpRamp);

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

    private static void EnsureExitHooksRegistered()
    {
        if (_hooksRegistered) return;
        _hooksRegistered = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreOriginal();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => RestoreOriginal();
    }

    /// <summary>
    /// Captura a curva original uma única vez, antes de aplicar qualquer preset.
    /// Só marca a captura como concluída se GetDeviceGammaRamp realmente funcionar.
    /// </summary>
    public static void CaptureOriginal()
    {
        lock (Sync)
        {
            if (_captured) return;
            EnsureExitHooksRegistered();

            IntPtr hdc = IntPtr.Zero;
            try
            {
                hdc = GetDC(IntPtr.Zero);
                if (hdc == IntPtr.Zero)
                {
                    DebugLog.Write("[DisplayColor] CaptureOriginal: GetDC falhou; tentará novamente na próxima aplicação.");
                    return;
                }

                var ramp = NewRamp();
                if (!GetDeviceGammaRamp(hdc, ref ramp))
                {
                    DebugLog.Write($"[DisplayColor] CaptureOriginal: GetDeviceGammaRamp falhou (Win32={Marshal.GetLastWin32Error()}); tentará novamente.");
                    return;
                }

                _original = CloneRamp(ramp);
                _captured = true;
                _currentMode = 0;
                DebugLog.Write("[DisplayColor] Curva original do Windows capturada com sucesso.");
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[DisplayColor] CaptureOriginal EXCEPTION {ex}");
            }
            finally
            {
                if (hdc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdc);
            }
        }
    }

    /// <summary>
    /// Aplica um preset transformando a curva original capturada. Nunca lê a
    /// curva atualmente modificada para gerar o próximo preset.
    /// </summary>
    public static void Apply(int modeIndex)
    {
        lock (Sync)
        {
            try
            {
                modeIndex = Math.Clamp(modeIndex, 0, Presets.Length - 1);
                CaptureOriginalLocked();
                if (!_captured || _original is not { } original)
                {
                    DebugLog.Write("[DisplayColor] Preset não aplicado: curva original indisponível.");
                    return;
                }

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
                    DebugLog.Write($"[DisplayColor] Modo {ModeNames[modeIndex]} aplicado sobre a curva original (sem acumular presets).");
                }
                else
                {
                    DebugLog.Write($"[DisplayColor] SetDeviceGammaRamp falhou ao aplicar {ModeNames[modeIndex]} (Win32={Marshal.GetLastWin32Error()}).");
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"DisplayColorManager.Apply EXCEPTION {ex}");
            }
        }
    }

    // Caller already holds Sync. Kept separate so Apply can retry a failed capture.
    private static void CaptureOriginalLocked()
    {
        if (_captured) return;
        EnsureExitHooksRegistered();

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
            DebugLog.Write("[DisplayColor] Curva original do Windows capturada com sucesso.");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[DisplayColor] CaptureOriginal EXCEPTION {ex}");
        }
        finally
        {
            if (hdc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdc);
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
        try
        {
            return SetDeviceGammaRamp(hdc, ref ramp);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    private static Ramp NewRamp() => new()
    {
        Red = new ushort[256],
        Green = new ushort[256],
        Blue = new ushort[256],
    };

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
            // Usa a amostra original capturada, não o valor deixado pelo modo anterior.
            var x = originalChannel[i] / 65535.0;
            var contrasted = Math.Clamp(0.5 + (x - 0.5) * curve.Contrast, 0.0, 1.0);
            var gamma = Math.Pow(contrasted, curve.Gamma);
            var final = Math.Clamp(gamma * curve.Scale, 0.0, 1.0);
            values[i] = (ushort)Math.Round(final * 65535.0);
        }
        return values;
    }
}

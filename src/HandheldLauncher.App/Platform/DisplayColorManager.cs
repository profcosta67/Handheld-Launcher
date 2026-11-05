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
                    var fromDisk = new Ramp { Red = backup.Red, Green = backup.Green, Blue = backup.Blue };
                    if (LooksUnmodified(fromDisk, out var diskReason))
                    {
                        _original = fromDisk;
                        _captured = true;
                        _currentMode = 0;
                        DebugLog.Write("[DisplayColor] Curva original carregada do backup em disco.");
                        return;
                    }

                    // v0.16.2: backup salvo com um efeito já aplicado (ex.: o
                    // launcher capturou a tela enquanto um modo de cor estava
                    // ativo). Usá-lo fazia o "Padrão" continuar com efeito e os
                    // modos se somarem em cima dele. Descarta e segue pra
                    // captura validada abaixo.
                    DebugLog.Write($"[DisplayColor] Backup em disco parece ter um efeito de cor aplicado ({diskReason}) — descartado.");
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

                if (LooksUnmodified(ramp, out var screenReason))
                {
                    _original = CloneRamp(ramp);
                }
                else
                {
                    // A tela já está com um efeito (sessão anterior fechada sem
                    // restaurar, ou outro programa). Usa a curva neutra como base.
                    DebugLog.Write($"[DisplayColor] Curva atual da tela parece ter um efeito aplicado ({screenReason}) — usando a curva neutra como original.");
                    _original = IdentityRamp();
                }
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

    /// <summary>
    /// Aplica o modo sobre a curva original. <paramref name="force"/> reaplica
    /// mesmo se for o modo atual (o Quick Settings usa isso: o Windows pode ter
    /// resetado a curva por baixo, ex. ao trocar resolução/Hz).
    /// v0.16.2: única porta de entrada de cor do launcher — o Quick Settings
    /// passou a usar este mesmo método (antes tinha um sistema separado, com
    /// outra base, e os efeitos se somavam).
    /// </summary>
    public static bool Apply(int modeIndex, bool force = false)
    {
        lock (Sync)
        {
            try
            {
                modeIndex = Math.Clamp(modeIndex, 0, Presets.Length - 1);
                CaptureOriginalLocked();
                if (!_captured || _original is not { } original) return false;

                if (modeIndex == _currentMode && !force) return true;

                if (modeIndex == 0)
                {
                    if (SetRamp(CloneRamp(original)))
                    {
                        _currentMode = 0;
                        DebugLog.Write("[DisplayColor] Modo Padrão: curva original restaurada.");
                        return true;
                    }
                    return false;
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
                    return true;
                }
                DebugLog.Write($"[DisplayColor] O driver de vídeo recusou o modo {ModeNames[modeIndex]}.");
                return false;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"DisplayColorManager.Apply EXCEPTION {ex}");
                return false;
            }
        }
    }

    /// <summary>
    /// v0.16.3: reaplica o modo atual. O Windows pode voltar a curva de gama
    /// ao padrão quando o modo de vídeo muda (resolução/Hz) — sem isso, o modo
    /// escolhido sumia da tela em silêncio, mas o launcher continuava achando
    /// que estava aplicado.
    /// </summary>
    public static bool ReapplyCurrent()
    {
        int mode;
        lock (Sync)
        {
            if (!_captured || _currentMode < 0) return false;
            mode = _currentMode;
        }
        DebugLog.Write($"[DisplayColor] Reaplicando o modo {ModeNames[mode]} (mudança de modo de vídeo).");
        return Apply(mode, force: true);
    }

    /// <summary>Nomes curtos usados no Quick Settings e no HUD → índice do modo.</summary>
    public static int IndexFromName(string? name) => name switch
    {
        "Vívido" => 1,
        "OLED" => 2,
        "Noturno" => 3,
        "Confortável" => 4,
        _ => 0,
    };

    /// <summary>
    /// v0.16.2: a curva "original" só é confiável se parecer a de fábrica.
    /// Todos os modos do launcher deixam marcas detectáveis: contraste maior
    /// que 1 achata as pontas da curva (vários valores em 0 no começo / no
    /// máximo no fim), e os modos Noturno/Confortável baixam o branco de algum
    /// canal. Calibrações reais do Windows (perfil de cor) mexem pouco e não
    /// fazem nenhuma das duas coisas.
    /// </summary>
    private static bool LooksUnmodified(Ramp ramp, out string reason)
    {
        foreach (var (name, channel) in new[] { ("R", ramp.Red), ("G", ramp.Green), ("B", ramp.Blue) })
        {
            if (channel is null || channel.Length != 256) { reason = $"canal {name} inválido"; return false; }

            var leadingZeros = 0;
            while (leadingZeros < 256 && channel[leadingZeros] == 0) leadingZeros++;
            if (leadingZeros > 2) { reason = $"canal {name} achatado no preto ({leadingZeros} pontos em 0)"; return false; }

            var trailingMax = 0;
            while (trailingMax < 256 && channel[255 - trailingMax] >= 65534) trailingMax++;
            if (trailingMax > 2) { reason = $"canal {name} achatado no branco ({trailingMax} pontos no máximo)"; return false; }

            if (channel[255] < 0.97 * 65535) { reason = $"branco do canal {name} reduzido ({channel[255] * 100.0 / 65535:0}%)"; return false; }
        }

        reason = "";
        return true;
    }

    private static Ramp IdentityRamp()
    {
        var ramp = NewRamp();
        for (var i = 0; i < 256; i++)
        {
            var value = (ushort)(i * 257);
            ramp.Red[i] = ramp.Green[i] = ramp.Blue[i] = value;
        }
        return ramp;
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
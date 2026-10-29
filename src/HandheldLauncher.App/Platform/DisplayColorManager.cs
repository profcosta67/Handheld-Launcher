using System.Runtime.InteropServices;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Modos de otimização de imagem/cor (v0.9.13, item do roteiro: "Vívido,
/// OLED, Noturno, Confortável etc."). Usa <c>SetDeviceGammaRamp</c>
/// (gdi32.dll) — ajusta a curva de cor da tela INTEIRA (fora do launcher
/// também, enquanto ele estiver aberto), sem depender do fabricante da GPU;
/// é o mesmo caminho de apps como GammaBoost/Dimmer pra efeito parecido.
///
/// <para>
/// <b>Importante sobre segurança/robustez</b> (não dá pra testar num
/// aparelho real daqui): essa API muda um estado GLOBAL do Windows que
/// sobrevive ao launcher fechar, se a gente não desfizer. Em vez de "voltar
/// pra uma curva linear neutra" (chute que poderia não bater com um perfil
/// de cor/calibração que já existisse no PC), capturamos a curva ORIGINAL
/// de verdade via <c>GetDeviceGammaRamp</c> ANTES de aplicar qualquer
/// preset (<see cref="CaptureOriginal"/>, chamada no início do app — ver
/// App.axaml.cs) e devolvemos exatamente ela no modo "Padrão" e ao
/// fechar/sair (<see cref="RestoreOriginal"/>, ligada em desktop.Exit +
/// ProcessExit/UnhandledException como rede de segurança — ver
/// App.axaml.cs). Mesmo assim, é experimental: se o processo for encerrado
/// de um jeito que nenhum desses hooks pega (ex: Finalizar tarefa no Gerenciador
/// de Tarefas), a tela pode ficar com a curva do último preset até reabrir
/// o launcher (que reaplica "Padrão"/o preset salvo) ou reiniciar o
/// aparelho — avisado também na tela de Configurações.
/// </para>
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

    /// <summary>
    /// Aproximação feita só com o que SetDeviceGammaRamp permite (uma curva
    /// por canal R/G/B, sem controle de saturação/matiz de verdade):
    /// contraste = curva em S em torno do meio; gama = realça/abaixa sombras;
    /// escala = brilho máximo do canal. Índice 0 (Padrão) não usa isto —
    /// ver Apply.
    /// </summary>
    private static readonly ModePreset[] Presets =
    [
        default, // 0 Padrão — Apply trata separado (restaura o original)
        new(new(1.0, 1.15, 1.0), new(1.0, 1.15, 1.0), new(1.0, 1.15, 1.0)), // 1 Vívido: mais contraste, sem mexer na cor
        new(new(1.08, 1.35, 1.0), new(1.08, 1.35, 1.0), new(1.08, 1.35, 1.0)), // 2 OLED: contraste bem mais alto, pretos mais fechados
        new(new(1.0, 1.0, 1.0), new(1.0, 1.0, 0.90), new(1.05, 1.0, 0.60)), // 3 Noturno: corta azul bastante, verde um pouco
        new(new(0.95, 0.90, 0.92), new(0.95, 0.90, 0.92), new(0.95, 0.90, 0.88)), // 4 Confortável: mais suave e um pouco mais escuro/quente
    ];

    private static Ramp? _original;
    private static bool _captured;
    private static int _currentMode = -1;

    /// <summary>
    /// Rede de segurança além do desktop.Exit em App.axaml.cs (que cobre o
    /// fechamento normal): ProcessExit/UnhandledException pegam um
    /// encerramento mais abrupto (exceção não tratada, Ctrl+C...) — não
    /// cobre um "Finalizar tarefa" no Gerenciador de Tarefas (esse não dá
    /// chance de nenhum código rodar), caso residual avisado na tela de
    /// Configurações. Roda uma vez, no primeiro toque na classe (chamado
    /// de CaptureOriginal, sempre o primeiro método usado).
    /// </summary>
    private static void EnsureExitHooksRegistered()
    {
        if (_hooksRegistered) return;
        _hooksRegistered = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreOriginal();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => RestoreOriginal();
    }

    private static bool _hooksRegistered;

    /// <summary>
    /// Snapshot da curva de gama ATUAL do Windows — chamar uma vez, bem no
    /// início do app, ANTES de qualquer <see cref="Apply"/> (ver
    /// App.axaml.cs). Idempotente (só captura na primeira chamada).
    /// </summary>
    public static void CaptureOriginal()
    {
        if (_captured) return;
        _captured = true; // marca ANTES de tentar — nunca tenta de novo nem se falhar
        EnsureExitHooksRegistered();
        try
        {
            var hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return;
            try
            {
                var ramp = NewRamp();
                if (GetDeviceGammaRamp(hdc, ref ramp))
                {
                    _original = ramp;
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"DisplayColorManager.CaptureOriginal EXCEPTION {ex}");
        }
    }

    /// <summary>Índice em <see cref="ModeNames"/>. 0 (Padrão) restaura a curva original.</summary>
    public static void Apply(int modeIndex)
    {
        try
        {
            CaptureOriginal();

            if (modeIndex == _currentMode) return;
            _currentMode = modeIndex;

            if (modeIndex <= 0 || modeIndex >= Presets.Length)
            {
                RestoreOriginal();
                return;
            }

            var preset = Presets[modeIndex];
            var ramp = new Ramp
            {
                Red = BuildChannelCurve(preset.R),
                Green = BuildChannelCurve(preset.G),
                Blue = BuildChannelCurve(preset.B),
            };
            SetRamp(ramp);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"DisplayColorManager.Apply EXCEPTION {ex}");
        }
    }

    /// <summary>Devolve a curva original capturada por <see cref="CaptureOriginal"/> — chamado ao fechar/sair (ver App.axaml.cs).</summary>
    public static void RestoreOriginal()
    {
        try
        {
            if (_original is { } ramp)
            {
                SetRamp(ramp);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"DisplayColorManager.RestoreOriginal EXCEPTION {ex}");
        }
    }

    private static void SetRamp(Ramp ramp)
    {
        var hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return;
        try
        {
            SetDeviceGammaRamp(hdc, ref ramp);
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

    private static ushort[] BuildChannelCurve(ChannelCurve curve)
    {
        var values = new ushort[256];
        for (var i = 0; i < 256; i++)
        {
            var x = i / 255.0;
            var contrasted = Math.Clamp(0.5 + (x - 0.5) * curve.Contrast, 0.0, 1.0);
            var gamma = Math.Pow(contrasted, curve.Gamma);
            var final = Math.Clamp(gamma * curve.Scale, 0.0, 1.0);
            values[i] = (ushort)Math.Round(final * 65535.0);
        }
        return values;
    }
}

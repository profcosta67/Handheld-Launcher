using System.Runtime.InteropServices;
using Avalonia.Platform;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Input;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Som de interação do launcher (v0.9.10, pedido do usuário: "esquecemos
/// de uma coisa importante, som de interação do launcher"). Três tons
/// curtos e sintéticos (gerados do zero por script, sem nenhum banco de
/// efeitos de terceiros — ver Assets/sfx-move.wav, sfx-confirm.wav,
/// sfx-back.wav), tocados via `winmm.dll PlaySound` com SND_MEMORY: toca
/// direto de um buffer já carregado em memória (uma vez, no início, a
/// partir dos recursos embutidos do Avalonia), sem escrever nada em disco
/// nem precisar de pacote extra — `System.Media.SoundPlayer` existiria,
/// mas exigiria referenciar `System.Windows.Extensions`, que este projeto
/// não usa pra mais nada (preferimos P/Invoke direto, mesmo padrão já
/// usado em Platform/BatteryStatus, Platform/PowerThrottling etc.).
///
/// SND_ASYNC (não bloqueia a thread que chamou) + SND_NODEFAULT (nunca
/// cai pro "ding" padrão do Windows se o buffer vier vazio/corrompido) +
/// SND_MEMORY. Chamar de novo interrompe o som anterior e começa o novo —
/// só uma instância tocando por vez (é uma API global do processo), o que
/// é exatamente o comportamento certo pra feedback de navegação (nunca
/// acumula eco se o usuário repetir rápido um direcional, por exemplo).
/// </summary>
internal static class UiSounds
{
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndMemory = 0x0004;

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(byte[]? pszSound, IntPtr hmod, uint fdwSound);

    private static readonly byte[]? MoveBytes = LoadWav("sfx-move.wav");
    private static readonly byte[]? ConfirmBytes = LoadWav("sfx-confirm.wav");
    private static readonly byte[]? BackBytes = LoadWav("sfx-back.wav");

    /// <summary>
    /// Ligado por padrão; "Sons de interação" em Configurações → Aparência
    /// deixa desligar (mesmo padrão da opção "Animações" já existente).
    /// </summary>
    public static bool Enabled { get; set; } = true;

    public static void PlayMove() => Play(MoveBytes);

    public static void PlayConfirm() => Play(ConfirmBytes);

    public static void PlayBack() => Play(BackBytes);

    /// <summary>
    /// Ponto único usado por MainWindow.OnGamepadTick pra cobrir biblioteca,
    /// todos os menus (contexto/global/energia), o seletor de capa e o
    /// painel de Downloads de uma vez só — sem precisar de uma chamada
    /// espalhada em cada switch interno daqueles estados (eles todos
    /// passam pelo mesmo `foreach` antes de se separar; ver comentário em
    /// OnGamepadTick). CycleOverlay (L3+R3) e os botões de filtro (LB/RB)
    /// usam o tick de Move — são ações rápidas/discretas, não confirmam
    /// nem voltam nada.
    /// </summary>
    public static void PlayForAction(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.MoveUp:
            case GamepadAction.MoveDown:
            case GamepadAction.MoveLeft:
            case GamepadAction.MoveRight:
            case GamepadAction.PreviousFilter:
            case GamepadAction.NextFilter:
            case GamepadAction.CycleOverlay:
                PlayMove();
                break;

            case GamepadAction.Confirm:
            case GamepadAction.Menu:
            case GamepadAction.GlobalMenu:
            case GamepadAction.Downloads:
                PlayConfirm();
                break;

            case GamepadAction.Back:
                PlayBack();
                break;
        }
    }

    private static void Play(byte[]? bytes)
    {
        if (!Enabled || bytes is null) return;

        try
        {
            PlaySound(bytes, IntPtr.Zero, SndAsync | SndMemory | SndNoDefault);
        }
        catch
        {
            // Melhor esforço — nunca travar a navegação por causa de som.
        }
    }

    private static byte[]? LoadWav(string fileName)
    {
        try
        {
            var uri = new Uri($"avares://HandheldLauncher/Assets/{fileName}");
            using var stream = AssetLoader.Open(uri);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"UiSounds: falha ao carregar {fileName} ({ex.Message})");
            return null;
        }
    }
}

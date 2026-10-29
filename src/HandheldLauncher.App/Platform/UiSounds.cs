using System.Runtime.InteropServices;
using Avalonia.Platform;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Input;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Som de interação do launcher (v0.9.10, pedido do usuário: "esquecemos
/// de uma coisa importante, som de interação do launcher"). Tons curtos e
/// sintéticos (gerados do zero por script, sem nenhum banco de efeitos de
/// terceiros — ver Assets/sfx-&lt;pack&gt;-move/confirm/back.wav), tocados via
/// `winmm.dll PlaySound` com SND_MEMORY: toca direto de um buffer já em
/// memória, sem escrever nada em disco nem precisar de pacote extra —
/// `System.Media.SoundPlayer` existiria, mas exigiria referenciar
/// `System.Windows.Extensions`, que este projeto não usa pra mais nada
/// (preferimos P/Invoke direto, mesmo padrão já usado em
/// Platform/BatteryStatus, Platform/PowerThrottling etc.).
///
/// <para>
/// <b>Bug corrigido na v0.9.11</b> (reportado pelo usuário: "quando segura
/// ou passa muito rápido o som fica bugado bem estranho"): a v0.9.10
/// passava um <c>byte[]</c> gerenciado direto pra <c>PlaySound(...,
/// SND_ASYNC | SND_MEMORY, ...)</c>. O P/Invoke só fixa (pin) esse array
/// na memória DURANTE a chamada — como SND_ASYNC retorna na hora e o
/// Windows continua lendo esse buffer em segundo plano enquanto o som
/// toca, o garbage collector tinha liberdade pra mover/coletar aquele
/// array a qualquer momento DEPOIS da chamada retornar, corrompendo o
/// áudio que o Windows ainda estava lendo — exatamente o tipo de artefato
/// "bugado/estranho" relatado, e mais provável de acontecer quanto mais
/// rápido o som é disparado de novo (segurar um direcional = repetição
/// rápida = mais chance de pegar o array no meio de uma coleta/mudança).
/// Corrigido copiando cada .wav, uma única vez, pra memória NÃO
/// gerenciada (<see cref="Marshal.AllocHGlobal"/>) — o garbage collector
/// nunca toca nisso, então o buffer fica sempre válido pra qualquer
/// replay assíncrono, não importa a frequência.
/// </para>
///
/// SND_ASYNC (não bloqueia a thread que chamou) + SND_NODEFAULT (nunca
/// cai pro "ding" padrão do Windows se o buffer vier vazio/corrompido) +
/// SND_MEMORY. Chamar de novo interrompe o som anterior e começa o novo —
/// só uma instância tocando por vez (é uma API global do processo), o que
/// é o comportamento certo pra feedback de navegação (nunca acumula eco).
/// </summary>
internal static class UiSounds
{
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndMemory = 0x0004;

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(IntPtr pszSound, IntPtr hmod, uint fdwSound);

    /// <summary>
    /// v0.9.11, pedido do usuário ("podemos por opções de sons também no
    /// tema, alguns diferentes, nada muito exagerado") — mesmo prefixo de
    /// arquivo usado em Assets/sfx-&lt;pack&gt;-move.wav etc. Índice 0
    /// ("Clássico") é o som original já aprovado; os outros são variações
    /// mais discretas, nunca algo chamativo.
    /// </summary>
    public static readonly string[] PackNames = ["Clássico", "Suave", "Retro", "Future"];

    private static readonly SoundSet[] Packs = PackNames
        .Select(name => LoadPack(FileSlug(name)))
        .ToArray();

    private static string FileSlug(string packName) => packName switch
    {
        "Clássico" => "classic",
        "Suave" => "soft",
        "Retro" => "retro",
        "Future" => "future",
        _ => "classic",
    };

    private static int _pack;

    /// <summary>Índice em <see cref="PackNames"/>. Fora da faixa cai pro 0.</summary>
    public static int Pack
    {
        get => _pack;
        set => _pack = value >= 0 && value < Packs.Length ? value : 0;
    }

    /// <summary>
    /// Ligado por padrão; "Sons de interação" em Configurações → Aparência
    /// deixa desligar (mesmo padrão da opção "Animações" já existente).
    /// </summary>
    public static bool Enabled { get; set; } = true;

    public static void PlayMove() => Play(Packs[_pack].Move);

    public static void PlayConfirm() => Play(Packs[_pack].Confirm);

    public static void PlayBack() => Play(Packs[_pack].Back);

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
            case GamepadAction.GameDetails:
                PlayConfirm();
                break;

            case GamepadAction.Back:
                PlayBack();
                break;
        }
    }

    private static void Play(IntPtr soundPtr)
    {
        if (!Enabled || soundPtr == IntPtr.Zero) return;

        try
        {
            PlaySound(soundPtr, IntPtr.Zero, SndAsync | SndMemory | SndNoDefault);
        }
        catch
        {
            // Melhor esforço — nunca travar a navegação por causa de som.
        }
    }

    private readonly record struct SoundSet(IntPtr Move, IntPtr Confirm, IntPtr Back);

    private static SoundSet LoadPack(string slug) => new(
        LoadWav($"sfx-{slug}-move.wav"),
        LoadWav($"sfx-{slug}-confirm.wav"),
        LoadWav($"sfx-{slug}-back.wav"));

    /// <summary>
    /// Lê o .wav embutido (recurso do Avalonia) e copia pra memória NÃO
    /// gerenciada — ver o porquê no comentário da classe. Nunca liberado
    /// de propósito: são uns KB por som, por um processo que já roda a
    /// vida toda do app; o Windows recupera tudo no fechamento mesmo.
    /// </summary>
    private static IntPtr LoadWav(string fileName)
    {
        try
        {
            var uri = new Uri($"avares://HandheldLauncher/Assets/{fileName}");
            using var stream = AssetLoader.Open(uri);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();

            var unmanaged = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, unmanaged, bytes.Length);
            return unmanaged;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"UiSounds: falha ao carregar {fileName} ({ex.Message})");
            return IntPtr.Zero;
        }
    }
}

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

    // IMPORTANTE: _volumeFraction precisa ser inicializado ANTES de Packs
    // abaixo — os inicializadores de campo estático executam em ORDEM
    // TEXTUAL dentro do construtor estático implícito, e o load de Packs já
    // chama LoadClip/RescaleToUnmanaged usando _volumeFraction na hora. Se
    // esse campo viesse depois no arquivo (como numa versão anterior desta
    // leva), Packs carregaria com volume 0.0 (silêncio) e só se corrigiria
    // se Volume:set algum dia recebesse um valor DIFERENTE do padrão 70 —
    // com o padrão batendo direto (AppSettings.SoundVolume também é 70), o
    // early-return do setter nunca chamaria RescaleAllPacks, e o som
    // ficaria mudo pra sempre em qualquer instalação nova. Bug encontrado e
    // corrigido ainda nesta leva, antes de qualquer teste real.
    private static double _volumeFraction = 0.70;
    private static int _volumePercent = 70;

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

    /// <summary>
    /// 0-100 (v0.9.13, pedido do usuário: "os sons ficaram um pouco alto,
    /// dá aquela estourada no volume máximo"). <c>PlaySound</c> (winmm) não
    /// tem parâmetro de volume — a escala é aplicada direto nas amostras
    /// PCM de cada som (ver <see cref="RescaleToUnmanaged"/>), ANTES de
    /// copiar pra memória não gerenciada. Padrão 70 (não 100): já começa
    /// mais baixo que o volume "cheio" original pra evitar o estouro
    /// reportado sem precisar regerar os .wav. Trocar em runtime reconstrói
    /// os 12 buffers (3 sons × 4 packs) na hora — são poucos KB cada,
    /// instantâneo.
    /// </summary>
    public static int Volume
    {
        get => _volumePercent;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (clamped == _volumePercent) return;
            _volumePercent = clamped;
            _volumeFraction = clamped / 100.0;
            RescaleAllPacks();
        }
    }

    private static void RescaleAllPacks()
    {
        foreach (var pack in Packs)
        {
            Rescale(pack.Move);
            Rescale(pack.Confirm);
            Rescale(pack.Back);
        }
    }

    private static void Rescale(SoundClip clip)
    {
        if (clip.Raw.Length == 0) return;
        var old = clip.Ptr;
        clip.Ptr = RescaleToUnmanaged(clip.Raw, _volumeFraction);
        if (old != IntPtr.Zero)
        {
            try { Marshal.FreeHGlobal(old); } catch { /* melhor esforço */ }
        }
    }

    public static void PlayMove() => Play(Packs[_pack].Move.Ptr);

    public static void PlayConfirm() => Play(Packs[_pack].Confirm.Ptr);

    public static void PlayBack() => Play(Packs[_pack].Back.Ptr);

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
            case GamepadAction.ToggleQuickSettings:
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

    private sealed class SoundSet
    {
        public required SoundClip Move { get; init; }
        public required SoundClip Confirm { get; init; }
        public required SoundClip Back { get; init; }
    }

    /// <summary>
    /// v0.9.13: antes, LoadWav só devolvia o IntPtr já escalado (fixo pra
    /// sempre). Agora guarda também os bytes ORIGINAIS do .wav (Raw), pra
    /// <see cref="Rescale"/> poder recalcular o buffer não-gerenciado toda
    /// vez que o usuário troca o volume em Configurações, sem precisar
    /// reabrir o arquivo embutido de novo.
    /// </summary>
    private sealed class SoundClip
    {
        public byte[] Raw = [];
        public IntPtr Ptr;
    }

    private static SoundSet LoadPack(string slug) => new()
    {
        Move = LoadClip($"sfx-{slug}-move.wav"),
        Confirm = LoadClip($"sfx-{slug}-confirm.wav"),
        Back = LoadClip($"sfx-{slug}-back.wav"),
    };

    /// <summary>
    /// Lê o .wav embutido (recurso do Avalonia), guarda os bytes originais
    /// e copia a versão já escalada pelo volume atual (<see
    /// cref="_volumeFraction"/>) pra memória NÃO gerenciada — ver o porquê
    /// da memória não gerenciada no comentário da classe. Buffer escalado
    /// nunca liberado no load inicial (só quando um <see cref="Rescale"/>
    /// posterior troca ele por outro) — são uns KB por som, por um
    /// processo que já roda a vida toda do app.
    /// </summary>
    private static SoundClip LoadClip(string fileName)
    {
        var clip = new SoundClip();
        try
        {
            var uri = new Uri($"avares://HandheldLauncher/Assets/{fileName}");
            using var stream = AssetLoader.Open(uri);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            clip.Raw = memory.ToArray();
            clip.Ptr = RescaleToUnmanaged(clip.Raw, _volumeFraction);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"UiSounds: falha ao carregar {fileName} ({ex.Message})");
        }
        return clip;
    }

    /// <summary>
    /// Escala as amostras PCM 16-bit do .wav pelo volume (0.0-1.0) e copia
    /// o resultado pra memória não gerenciada. Nosso próprio gerador
    /// (scratchpad/gen_sfx_*.py) sempre escreve o cabeçalho WAV canônico de
    /// 44 bytes (RIFF/WAVEfmt.../data, mono 16-bit) — confere essa
    /// assinatura antes de tocar nos bytes; se algo não bater (formato
    /// diferente no futuro), toca o áudio ORIGINAL sem escalar em vez de
    /// arriscar corromper o som.
    /// </summary>
    private static IntPtr RescaleToUnmanaged(byte[] wav, double volumeFraction)
    {
        try
        {
            if (wav.Length > 44
                && wav[0] == (byte)'R' && wav[1] == (byte)'I' && wav[2] == (byte)'F' && wav[3] == (byte)'F'
                && wav[36] == (byte)'d' && wav[37] == (byte)'a' && wav[38] == (byte)'t' && wav[39] == (byte)'a')
            {
                var scaled = (byte[])wav.Clone();
                for (var i = 44; i + 1 < scaled.Length; i += 2)
                {
                    var sample = (short)(scaled[i] | (scaled[i + 1] << 8));
                    var value = Math.Clamp((int)Math.Round(sample * volumeFraction), short.MinValue, short.MaxValue);
                    scaled[i] = (byte)(value & 0xFF);
                    scaled[i + 1] = (byte)((value >> 8) & 0xFF);
                }
                return CopyToUnmanaged(scaled);
            }
        }
        catch
        {
            // Melhor esforço — cai pro áudio original sem escalar abaixo.
        }
        return CopyToUnmanaged(wav);
    }

    private static IntPtr CopyToUnmanaged(byte[] bytes)
    {
        var unmanaged = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, unmanaged, bytes.Length);
        return unmanaged;
    }
}

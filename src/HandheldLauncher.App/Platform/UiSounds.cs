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
    // v0.16.4: os bipes antigos (Clássico, Suave, Retro, Future) foram trocados
    // por 7 estilos novos escolhidos pelo usuário (Wyzowl SFX Kit e Kenney,
    // CC0 — ver Assets/SONS-LICENCA.txt). A ORDEM importa: o índice salvo em
    // AppSettings.SoundTheme aponta pra cá, e ThemeManager.LayoutDefaults usa
    // esses índices.
    public static readonly string[] PackNames = ["Console", "Toque", "Bolha", "Mecânico", "Marimba", "Botão", "Digital"];

    // IMPORTANTE: _volumeFraction precisa ser inicializado ANTES de Packs
    // abaixo — os inicializadores de campo estático executam em ORDEM
    // TEXTUAL dentro do construtor estático implícito, e o load de Packs já
    // chama LoadClip/BuildBuffer usando _volumeFraction na hora. Se
    // esse campo viesse depois no arquivo (como numa versão anterior desta
    // leva), Packs carregaria com volume 0.0 (silêncio) e só se corrigiria
    // se Volume:set algum dia recebesse um valor DIFERENTE do padrão 70 —
    // com o padrão batendo direto (AppSettings.SoundVolume também é 70), o
    // early-return do setter nunca chamaria RescaleAllPacks, e o som
    // ficaria mudo pra sempre em qualquer instalação nova. Bug encontrado e
    // corrigido ainda nesta leva, antes de qualquer teste real.
    private static double _volumeFraction = 0.70;
    private static int _volumePercent = 70;

    // v0.16.4 (beta.16): ganho de reforço aplicado POR CIMA do volume (0-100%).
    // Os .wav têm pico entre -10 e -4 dBFS (o "mover" é o mais baixo) e, com o
    // volume em 70%, ficavam ainda mais fracos — o teste de 05/11 mostrou
    // "tem hora que não sai som". 1.5 (≈ +3,5 dB) deixa o 70% padrão no nível
    // que o usuário já ouvia e dá folga real pro slider subir; o pico mais alto
    // (-4 dB) vira -0,5 dB — sem estourar.
    private const double MakeupGain = 1.5;

    // v0.16.5 (beta.17 — relato do usuário: "o volume dos sons do botão A/B
    // Start estão mais altos que do joystick para passar para o lado").
    // v0.17 (beta.18 — usuário ainda ouvia "quase inaudível" mesmo com o
    // 1.8 abaixo): medi os .wav de verdade (sfx-console-move/confirm/back)
    // em vez de confiar só no pico. O pico já tinha ficado quase igualado
    // (move -10,0 dBFS vs confirm -4,0/back -5,0 dBFS — 1.8≈+5,1dB cobria
    // essa parte), mas o RMS (o que o ouvido percebe como "volume", não o
    // pico instantâneo) contava outra história: move -27,0 dBFS de RMS
    // contra -19,2/-19,8 dBFS do confirm/back — quase 8dB de diferença REAL
    // de energia, e o clipe de mover também é bem mais curto (12ms contra
    // 31-48ms), o que o ouvido percebe como ainda mais fraco pra uma mesma
    // energia. 2.0 (+6dB, soma +9,5dB com o MakeupGain) é o máximo que dá
    // pra subir sem estourar o pico em 0dBFS — fecha a maior parte da
    // diferença de RMS sem distorcer. Pra igualar 100% ainda faltaria
    // regravar o clipe de mover com mais sustentação (fora do escopo de um
    // ajuste de ganho).
    //
    // v0.18 (05/11 — usuário ainda relata "som ainda está com o volume dos
    // direcionais baixo" mesmo com o ajuste acima): medi os 21 arquivos de
    // TODOS os 7 estilos (não só "console") com um script (peak dB + RMS
    // dB). Resultado: o PICO do clipe de mover é -10,0 dBFS em TODOS os 7
    // estilos, sem exceção — ou seja, 2.0 (+6dB) já leva o pico de mover
    // pra -4,0 dBFS em TODOS eles, exatamente empatado com o pico do
    // confirm. Subir esse ganho global além disso estouraria o pico (sample
    // clipping) em QUALQUER estilo — não há mais folga nenhuma por esse
    // caminho, confirmado com números reais, não só pelo comentário antigo.
    // O que SOBRA de diferença não é ganho, é DURAÇÃO: o clipe de mover
    // mede entre ~11ms (bolha) e ~97ms dependendo do estilo, contra 30-300ms
    // do confirm/back no mesmo estilo — um clique mais curto que ~150-200ms
    // é percebido pelo ouvido como mais fraco mesmo com a MESMA energia por
    // amostra, porque o ouvido integra energia ao longo de uma janela de
    // tempo. Ver <see cref="AppendMoveSustainTail"/> pra a correção disso
    // (sem precisar regravar nenhum arquivo).
    private const double MoveRoleGain = 2.0;

    // v0.18: cauda sintética SÓ pro clipe de mover (ver comentário de
    // MoveRoleGain acima) — 2 repetições decrescentes do próprio clipe,
    // coladas direto depois do fim dele (sem silêncio entre), simulando um
    // decaimento natural de um clique percussivo e esticando a duração
    // ativa em até ~3x sem tocar no pico (cada eco entra bem abaixo do
    // teto: -5,2dB e -11,0dB relativos ao original, então a soma nunca
    // passa do que o clipe original já atinge).
    private const double MoveEchoGain1 = 0.55;
    private const double MoveEchoGain2 = 0.28;

    // Respiro de silêncio antes do som quando o aparelho de áudio pode estar
    // "dormindo" (sem som há > ColdAfterMs): o driver acorda durante o silêncio
    // em vez de engolir o começo do clique. Só é usado nesse caso — no uso
    // contínuo (navegando) o som sai na hora.
    private const int ColdLeadInMs = 70;
    private const int ColdAfterMs = 2500;
    // Silêncio no fim: o winmm corta o último bloco de buffer de clipes muito
    // curtos (11-14 ms); cauda + duração mínima garantem que o clique inteiro toque.
    private const int TailMs = 30;
    private const int MinTotalMs = 80;

    private static long _lastPlayTick;
    private static int _playFailuresLogged;
    private static readonly HashSet<int> _packLogged = [];

    private static readonly SoundSet[] Packs = PackNames
        .Select(name => LoadPack(FileSlug(name)))
        .ToArray();

    private static string FileSlug(string packName) => packName switch
    {
        "Console" => "console",
        "Toque" => "toque",
        "Bolha" => "bolha",
        "Mecânico" => "mecanico",
        "Marimba" => "marimba",
        "Botão" => "botao",
        "Digital" => "digital",
        _ => "console",
    };

    private static int _pack;

    /// <summary>Índice em <see cref="PackNames"/>. Fora da faixa cai pro 0.</summary>
    public static int Pack
    {
        get => _pack;
        set => _pack = value >= 0 && value < Packs.Length ? value : 0;
    }

    private static bool _enabled = true;

    /// <summary>
    /// Ligado por padrão; "Sons de interação" em Configurações → Aparência
    /// deixa desligar (mesmo padrão da opção "Animações" já existente).
    ///
    /// v0.17 (05/11): usuário relatou "o som parou de reproduzir" no meio
    /// de uma sessão, sem nenhuma exceção nem falha do PlaySound no
    /// debug.log — ou seja, os sons pararam de TOCAR, não de FALHAR. Um
    /// jeito disso acontecer sem deixar rastro é ApplyAppearance (chamado
    /// a cada recarga de configurações) reaplicar
    /// <c>settings.SoundEffects</c> como false por engano (ex.: leitura
    /// concorrente do appsettings.json devolvendo um objeto zerado).
    /// Registra só a TRANSIÇÃO (liga→desliga e desliga→liga), não cada
    /// leitura, pra não logar o resto da sessão à toa — mas é o suficiente
    /// pra confirmar ou descartar essa hipótese no próximo debug.log.
    /// </summary>
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled != value)
            {
                DebugLog.Write($"[SOM] Enabled mudou de {_enabled} para {value}.");
            }
            _enabled = value;
        }
    }

    /// <summary>
    /// 0-100 (v0.9.13, pedido do usuário: "os sons ficaram um pouco alto,
    /// dá aquela estourada no volume máximo"). <c>PlaySound</c> (winmm) não
    /// tem parâmetro de volume — a escala é aplicada direto nas amostras
    /// PCM de cada som (ver <see cref="BuildBuffer"/>), ANTES de
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
        if (!clip.Loaded) return;
        var gain = _volumeFraction * MakeupGain * clip.RoleGain;
        var newHot = BuildBuffer(clip, gain, 0);
        var newCold = BuildBuffer(clip, gain, ColdLeadInMs);
        var oldHot = clip.Ptr;
        var oldCold = clip.ColdPtr;
        // Troca primeiro, libera depois: um som pode estar tocando AGORA desse
        // buffer (SND_ASYNC lê da memória em segundo plano). Antes da beta.16
        // o buffer antigo era liberado na hora — leitura de memória já liberada.
        clip.Ptr = newHot;
        clip.ColdPtr = newCold;
        ScheduleFree(oldHot);
        ScheduleFree(oldCold);
    }

    private static void ScheduleFree(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return;
        _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ =>
        {
            try { Marshal.FreeHGlobal(ptr); } catch (Exception ex) { DebugLog.Swallowed(ex); }
        }, TaskScheduler.Default);
    }

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
            case GamepadAction.ToggleQuickSettings:
                PlayMove();
                break;

            case GamepadAction.Confirm:
            case GamepadAction.Menu:
            case GamepadAction.GlobalMenu:
            case GamepadAction.Downloads:
            case GamepadAction.QuickAccessMenu:
            case GamepadAction.GameDetails:
                PlayConfirm();
                break;

            case GamepadAction.Back:
                PlayBack();
                break;
        }
    }

    private static void Play(SoundClip clip)
    {
        if (!Enabled || !clip.Loaded) return;

        try
        {
            var now = Environment.TickCount64;
            var cold = now - Interlocked.Exchange(ref _lastPlayTick, now) > ColdAfterMs;
            var ptr = cold && clip.ColdPtr != IntPtr.Zero ? clip.ColdPtr : clip.Ptr;
            if (ptr == IntPtr.Zero) return;

            LogFirstUse();
            PlayPointer(ptr);
        }
        catch (Exception ex)
        {
            // Melhor esforço — nunca travar a navegação por causa de som.
            DebugLog.Swallowed(ex);
        }
    }

    private static void PlayPointer(IntPtr ptr)
    {
        if (!PlaySound(ptr, IntPtr.Zero, SndAsync | SndMemory | SndNoDefault)
            && Interlocked.Increment(ref _playFailuresLogged) <= 5)
        {
            // Sem isso, "não saiu som" não deixava rastro nenhum no debug.log.
            DebugLog.Write($"[SOM] PlaySound falhou (erro Win32 {Marshal.GetLastWin32Error()}).");
        }
    }

    private static void LogFirstUse()
    {
        lock (_packLogged)
        {
            if (!_packLogged.Add(_pack)) return;
        }
        DebugLog.Write($"[SOM] Primeiro som do estilo '{PackNames[_pack]}' — volume {_volumePercent}%, ligado={Enabled}.");
    }

    // ---- Prévia nas Configurações -------------------------------------------------

    private static Timer? _previewTimer;
    private static IntPtr _previewPtr;
    private static readonly object PreviewLock = new();

    /// <summary>
    /// Toca um exemplo (o som de "confirmar") do estilo/volume que o usuário
    /// está escolhendo nas Configurações — sem mexer no estilo/volume ativos
    /// (esses só mudam ao salvar). Espera 180 ms depois da última mudança, pra
    /// arrastar o slider não metralhar. Antes da beta.16 trocar volume/estilo
    /// não dava retorno nenhum: parecia que "não responde".
    /// </summary>
    public static void Preview(int pack, int volumePercent, bool enabled)
    {
        if (!enabled) return;
        try
        {
            lock (PreviewLock)
            {
                _previewTimer?.Dispose();
                _previewTimer = new Timer(_ => PlayPreview(pack, volumePercent), null, 180, Timeout.Infinite);
            }
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
    }

    private static void PlayPreview(int pack, int volumePercent)
    {
        try
        {
            if (pack < 0 || pack >= Packs.Length) pack = 0;
            var clip = Packs[pack].Confirm;
            if (!clip.Loaded) return;
            var gain = Math.Clamp(volumePercent, 0, 100) / 100.0 * MakeupGain;
            var ptr = BuildBuffer(clip, gain, ColdLeadInMs);
            if (ptr == IntPtr.Zero) return;

            IntPtr old;
            lock (PreviewLock)
            {
                old = _previewPtr;
                _previewPtr = ptr;
            }
            Interlocked.Exchange(ref _lastPlayTick, Environment.TickCount64);
            PlayPointer(ptr);
            ScheduleFree(old);
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
    }

    // ---- Carregamento / montagem dos buffers -------------------------------------

    private sealed class SoundSet
    {
        public required SoundClip Move { get; init; }
        public required SoundClip Confirm { get; init; }
        public required SoundClip Back { get; init; }
    }

    /// <summary>
    /// Um som já decodificado: só as amostras PCM de 16 bits (sem cabeçalho),
    /// mais os dois buffers prontos pra tocar na memória NÃO gerenciada — o
    /// "quente" (sai na hora) e o "frio" (com respiro de silêncio antes).
    /// Quando o arquivo não é PCM 16 bits, <see cref="RawFile"/> guarda os bytes
    /// originais e toca sem escala (melhor tocar do que ficar mudo).
    /// </summary>
    private sealed class SoundClip
    {
        public byte[] Pcm = [];
        public byte[] RawFile = [];
        public short Channels = 1;
        public int SampleRate = 44100;
        public IntPtr Ptr;
        public IntPtr ColdPtr;
        public double RoleGain = 1.0;
        public bool IsMoveRole;
        public bool Loaded => Ptr != IntPtr.Zero;
    }

    private static SoundSet LoadPack(string slug) => new()
    {
        Move = LoadClip($"sfx-{slug}-move.wav", MoveRoleGain, isMoveRole: true),
        Confirm = LoadClip($"sfx-{slug}-confirm.wav"),
        Back = LoadClip($"sfx-{slug}-back.wav"),
    };

    /// <summary>
    /// Lê o .wav embutido (recurso do Avalonia), decodifica e monta os buffers
    /// no volume atual. Ver o porquê da memória não gerenciada no comentário
    /// da classe.
    /// </summary>
    private static SoundClip LoadClip(string fileName, double roleGain = 1.0, bool isMoveRole = false)
    {
        var clip = new SoundClip { RoleGain = roleGain, IsMoveRole = isMoveRole };
        try
        {
            var uri = new Uri($"avares://HandheldLauncher/Assets/{fileName}");
            using var stream = AssetLoader.Open(uri);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var file = memory.ToArray();

            if (TryParseWav(file, out var channels, out var rate, out var pcm))
            {
                clip.Channels = channels;
                clip.SampleRate = rate;
                clip.Pcm = pcm;
            }
            else
            {
                DebugLog.Write($"[SOM] {fileName}: formato não é PCM 16 bits — toca sem escala de volume.");
                clip.RawFile = file;
            }

            var gain = _volumeFraction * MakeupGain * clip.RoleGain;
            clip.Ptr = BuildBuffer(clip, gain, 0);
            clip.ColdPtr = BuildBuffer(clip, gain, ColdLeadInMs);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"UiSounds: falha ao carregar {fileName} ({ex.Message})");
        }
        return clip;
    }

    /// <summary>
    /// Percorre os "chunks" do .wav até achar "fmt " e "data". Os arquivos
    /// gerados pelo ffmpeg trazem um chunk "LIST" (metadados) ANTES do "data",
    /// então o "data" NÃO começa no byte 44. A versão anterior assumia o
    /// cabeçalho canônico de 44 bytes, não reconhecia nenhum dos 21 arquivos e
    /// tocava tudo SEM escala — o controle de volume não fazia efeito algum.
    /// </summary>
    private static bool TryParseWav(byte[] wav, out short channels, out int sampleRate, out byte[] pcm)
    {
        channels = 0;
        sampleRate = 0;
        pcm = [];
        if (wav.Length < 12
            || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F'
            || wav[8] != 'W' || wav[9] != 'A' || wav[10] != 'V' || wav[11] != 'E')
            return false;

        short format = 0, bits = 0;
        var haveFormat = false;
        var pos = 12;
        while (pos + 8 <= wav.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
            var size = BitConverter.ToInt32(wav, pos + 4);
            var body = pos + 8;

            if (id == "fmt " && body + 16 <= wav.Length)
            {
                format = BitConverter.ToInt16(wav, body);
                channels = BitConverter.ToInt16(wav, body + 2);
                sampleRate = BitConverter.ToInt32(wav, body + 4);
                bits = BitConverter.ToInt16(wav, body + 14);
                haveFormat = true;
            }
            else if (id == "data")
            {
                var length = size < 0 || body + size > wav.Length ? wav.Length - body : size;
                if (!haveFormat || format != 1 || bits != 16 || channels < 1 || sampleRate < 8000) return false;
                var frame = channels * 2;
                length -= length % frame;
                if (length <= 0) return false;
                pcm = new byte[length];
                Buffer.BlockCopy(wav, body, pcm, 0, length);
                return true;
            }

            if (size < 0) break;
            pos = body + size + (size & 1);
        }
        return false;
    }

    /// <summary>
    /// Monta um .wav canônico (cabeçalho de 44 bytes) na memória NÃO gerenciada:
    /// [respiro de silêncio] + amostras × ganho + cauda de silêncio (mínimo
    /// <see cref="MinTotalMs"/> no total). Ganho é limitado ao intervalo de 16
    /// bits. Se o arquivo não for PCM 16 bits, copia o original sem escala.
    /// </summary>
    private static IntPtr BuildBuffer(SoundClip clip, double gain, int leadInMs)
    {
        try
        {
            if (clip.Pcm.Length == 0)
                return clip.RawFile.Length == 0 ? IntPtr.Zero : CopyToUnmanaged(clip.RawFile);

            var frame = clip.Channels * 2;
            var leadBytes = clip.SampleRate * leadInMs / 1000 * frame;
            var dataBytes = clip.Pcm.Length;
            var minTotal = clip.SampleRate * MinTotalMs / 1000 * frame;
            var tailBytes = Math.Max(clip.SampleRate * TailMs / 1000 * frame, minTotal - leadBytes - dataBytes);
            var total = leadBytes + dataBytes + tailBytes;

            var wav = new byte[44 + total];
            WriteAscii(wav, 0, "RIFF");
            WriteInt32(wav, 4, 36 + total);
            WriteAscii(wav, 8, "WAVE");
            WriteAscii(wav, 12, "fmt ");
            WriteInt32(wav, 16, 16);
            WriteInt16(wav, 20, 1);
            WriteInt16(wav, 22, clip.Channels);
            WriteInt32(wav, 24, clip.SampleRate);
            WriteInt32(wav, 28, clip.SampleRate * frame);
            WriteInt16(wav, 32, (short)frame);
            WriteInt16(wav, 34, 16);
            WriteAscii(wav, 36, "data");
            WriteInt32(wav, 40, total);

            var offset = 44 + leadBytes;
            for (var i = 0; i + 1 < clip.Pcm.Length; i += 2)
            {
                var sample = (short)(clip.Pcm[i] | (clip.Pcm[i + 1] << 8));
                var value = Math.Clamp((int)Math.Round(sample * gain), short.MinValue, short.MaxValue);
                wav[offset + i] = (byte)(value & 0xFF);
                wav[offset + i + 1] = (byte)((value >> 8) & 0xFF);
            }

            // v0.18 — ver comentário de MoveEchoGain1/2: só o clipe de mover
            // ganha a cauda sintética, escrita dentro do espaço de silêncio
            // que já existia (tailBytes já cobre pelo menos MinTotalMs no
            // total, então normalmente há espaço de sobra pro clipe curto
            // de mover). Cada eco é truncado se não couber inteiro — nunca
            // escreve fora do buffer alocado.
            if (clip.IsMoveRole)
            {
                var afterOriginal = offset + dataBytes;
                var tailAvailable = 44 + total - afterOriginal;
                AppendMoveEcho(wav, afterOriginal, tailAvailable, clip.Pcm, gain * MoveEchoGain1);
                AppendMoveEcho(wav, afterOriginal + dataBytes, tailAvailable - dataBytes, clip.Pcm, gain * MoveEchoGain2);
            }

            return CopyToUnmanaged(wav);
        }
        catch (Exception ex)
        {
            DebugLog.Swallowed(ex);
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Soma (nunca sobrescreve) uma cópia decaída das amostras do clipe
    /// original dentro da região de cauda — ver comentário de
    /// MoveEchoGain1/2. A região de destino já começa zerada (silêncio),
    /// então "somar" aqui é equivalente a "escrever", mas soma é o que
    /// deixa a 2ª chamada (eco 2, que começa um clipe de distância da 1ª)
    /// segura mesmo se algum dia as regiões se sobrepuserem. Satura no
    /// limite de 16 bits igual o resto do BuildBuffer — nunca estoura o
    /// buffer, nunca estoura a amostra.
    /// </summary>
    private static void AppendMoveEcho(byte[] wav, int startByteOffset, int availableBytes, byte[] pcm, double echoGain)
    {
        if (availableBytes <= 0) return;
        var writable = Math.Min(availableBytes, pcm.Length);
        writable -= writable % 2;
        for (var i = 0; i + 1 < writable; i += 2)
        {
            var sample = (short)(pcm[i] | (pcm[i + 1] << 8));
            var existing = (short)(wav[startByteOffset + i] | (wav[startByteOffset + i + 1] << 8));
            var value = Math.Clamp(existing + (int)Math.Round(sample * echoGain), short.MinValue, short.MaxValue);
            wav[startByteOffset + i] = (byte)(value & 0xFF);
            wav[startByteOffset + i + 1] = (byte)((value >> 8) & 0xFF);
        }
    }

    private static void WriteAscii(byte[] target, int offset, string text)
    {
        for (var i = 0; i < text.Length; i++) target[offset + i] = (byte)text[i];
    }

    private static void WriteInt32(byte[] target, int offset, int value) =>
        BitConverter.TryWriteBytes(target.AsSpan(offset, 4), value);

    private static void WriteInt16(byte[] target, int offset, short value) =>
        BitConverter.TryWriteBytes(target.AsSpan(offset, 2), value);

    private static IntPtr CopyToUnmanaged(byte[] bytes)
    {
        var unmanaged = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, unmanaged, bytes.Length);
        return unmanaged;
    }
}

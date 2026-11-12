using System.IO.MemoryMappedFiles;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Input;

/// <summary>
/// v0.25.9 — ponte do controle para o Quick Settings com o JOGO em foco.
///
/// Por quê (log de 11/11, Days Gone): o QS abre sem roubar o foco do jogo
/// (WS_EX_NOACTIVATE, desde 09/11), e o Windows não entrega XInput a um
/// processo em segundo plano. Resultado: das 16 aberturas do QS dentro de
/// jogo no log, nenhuma recebeu um botão; das 17 na biblioteca, todas. O
/// leitor HID do ajudante (Raw Input com INPUTSINK) recebe os botões mesmo
/// assim — ele viu o B (botão 2) às 12:44:03 com o QS aberto.
///
/// Então o AJUDANTE publica o estado do controle numa memória compartilhada
/// pequena e o launcher lê dali enquanto o QS está aberto num jogo. A leitura
/// bruta de HID continua só no processo ajudante (se algo der errado lá, só
/// ele cai — a regra original do HidComboReader).
///
/// Layout (int32 alinhados): 0 magic · 4 versão · 8 sequência · 12 botões
/// (bit u-1 = usage HID u) · 16 direções (1 cima, 2 baixo, 4 esquerda,
/// 8 direita) · 20.. contadores de aperto [14] (0–9 = botões 1–10, 10 cima,
/// 11 baixo, 12 esquerda, 13 direita). Contadores, e não só o estado, para
/// um toque mais curto que o tique de 50 ms do launcher nunca se perder.
/// </summary>
internal static class HidPadBridge
{
    public const int Magic = 0x44504848; // "HHPD"
    public const int Version = 1;
    public const int Capacity = 128;

    public const int OffMagic = 0;
    public const int OffVersion = 4;
    public const int OffSequence = 8;
    public const int OffButtons = 12;
    public const int OffDirections = 16;
    public const int OffCounters = 20;
    public const int CounterCount = 14;

    public const int DirUp = 1, DirDown = 2, DirLeft = 4, DirRight = 8;

    public static string MapName(int launcherPid) => $"Local\\HandheldLauncher_HidPad_{launcherPid}";

    // ------------------------------------------------------------ lado do ajudante

    private static MemoryMappedFile? _writerFile;
    private static MemoryMappedViewAccessor? _writer;
    private static int _lastButtons = -1;
    private static int _rawDirections = -1;        // último estado bruto recebido
    private static int _committedDirections = -1;  // estado já contado/publicado
    private static System.Threading.Timer? _diagonalTimer;
    private static int _sequence;
    private static readonly object WriterLock = new();

    /// <summary>
    /// Quanto esperar antes de aceitar uma diagonal. Um D-pad de handheld
    /// raramente passa de "solto" direto para uma direção pura: costuma
    /// reportar a diagonal por 1–2 relatórios. Se a diagonal virar uma
    /// direção pura dentro desta janela, só a pura conta; se continuar,
    /// o jogador quis mesmo a diagonal e as duas direções contam.
    /// </summary>
    public const int DiagonalSettleMs = 45;

    private static bool IsDiagonal(int d) =>
        (d & (DirUp | DirDown)) != 0 && (d & (DirLeft | DirRight)) != 0;

    /// <summary>Ajudante: cria (ou reabre) a memória compartilhada do launcher pai.</summary>
    public static void InitWriter(int launcherPid)
    {
        if (!OperatingSystem.IsWindows() || launcherPid <= 0) return;
        try
        {
            lock (WriterLock)
            {
                _writerFile = MemoryMappedFile.CreateOrOpen(MapName(launcherPid), Capacity);
                _writer = _writerFile.CreateViewAccessor(0, Capacity);
                _writer.Write(OffVersion, Version);
                _writer.Write(OffMagic, Magic);
                _sequence = _writer.ReadInt32(OffSequence);
            }
            DebugLog.Write($"HidPadBridge: publicando o controle para o Quick Settings ({MapName(launcherPid)}).");
        }
        catch (Exception ex)
        {
            _writer = null;
            DebugLog.Write($"HidPadBridge: não consegui criar a ponte ({ex.GetType().Name}: {ex.Message}) — QS no jogo segue só com XInput.");
        }
    }

    /// <summary>
    /// Ajudante: novo estado vindo do HID (máscara de botões e direções, D-pad
    /// e analógico já combinados). Conta os apertos novos e publica. Chamado
    /// pela thread do HidComboReader.
    /// </summary>
    public static void Publish(int buttons, int directions)
    {
        var writer = _writer;
        if (writer is null) return;
        try
        {
            lock (WriterLock)
            {
                if (buttons == _lastButtons && directions == _rawDirections) return;

                var prevButtons = _lastButtons < 0 ? buttons : _lastButtons;
                var newButtons = buttons & ~prevButtons;
                for (var i = 0; i < 10; i++)
                    if ((newButtons & (1 << i)) != 0) Bump(writer, i);
                writer.Write(OffButtons, buttons);
                _lastButtons = buttons;

                var prevRaw = _rawDirections;
                _rawDirections = directions;
                if (_committedDirections < 0)
                {
                    // Primeiro relatório: o que já está segurado não conta como aperto.
                    _committedDirections = directions;
                    writer.Write(OffDirections, directions);
                }
                else if (IsDiagonal(directions) && directions != _committedDirections)
                {
                    // Espera a diagonal "assentar" antes de contar.
                    _diagonalTimer ??= new System.Threading.Timer(OnDiagonalSettled, _timerGeneration, DiagonalSettleMs, System.Threading.Timeout.Infinite);
                }
                else
                {
                    // Toque curto numa diagonal ainda pendente: conta antes de soltar.
                    // Só a partir do neutro — soltar uma direção já contada
                    // passando de raspão pela diagonal não pode contar a vizinha
                    // (revisão 11/11).
                    if (_diagonalTimer != null && directions == 0 && IsDiagonal(prevRaw)
                        && _committedDirections == 0)
                        CommitDirections(writer, prevRaw);
                    CancelDiagonalTimer();
                    CommitDirections(writer, directions);
                }

                writer.Write(OffSequence, ++_sequence);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"HidPadBridge: falha ao publicar ({ex.GetType().Name}) — ponte desligada.");
            _writer = null;
        }
    }

    private static int _timerGeneration;

    private static void CancelDiagonalTimer()
    {
        _timerGeneration++;
        _diagonalTimer?.Dispose();
        _diagonalTimer = null;
    }

    private static void OnDiagonalSettled(object? state)
    {
        try
        {
            lock (WriterLock)
            {
                // Callback de um timer já cancelado/substituído: ignora.
                if (state is not int gen || gen != _timerGeneration) return;
                CancelDiagonalTimer();
                var writer = _writer;
                if (writer is null || _rawDirections < 0) return;
                CommitDirections(writer, _rawDirections);
                writer.Write(OffSequence, ++_sequence);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"HidPadBridge: falha ao confirmar diagonal ({ex.GetType().Name}).");
        }
    }

    /// <summary>Conta as direções novas em relação à última confirmada e publica.</summary>
    private static void CommitDirections(MemoryMappedViewAccessor writer, int directions)
    {
        var added = directions & ~_committedDirections;
        if ((added & DirUp) != 0) Bump(writer, 10);
        if ((added & DirDown) != 0) Bump(writer, 11);
        if ((added & DirLeft) != 0) Bump(writer, 12);
        if ((added & DirRight) != 0) Bump(writer, 13);
        _committedDirections = directions;
        writer.Write(OffDirections, directions);
    }

    private static void Bump(MemoryMappedViewAccessor writer, int index)
    {
        var offset = OffCounters + index * 4;
        writer.Write(offset, unchecked(writer.ReadInt32(offset) + 1));
    }

    /// <summary>
    /// Hat switch do HID (coleção "IG_" do XInput: 1 = cima, sentido horário
    /// até 8 = cima-esquerda; 0 ou fora da faixa = solto) → máscara de direções.
    /// </summary>
    public static int DirectionsFromHat(uint hat) => hat switch
    {
        1 => DirUp,
        2 => DirUp | DirRight,
        3 => DirRight,
        4 => DirDown | DirRight,
        5 => DirDown,
        6 => DirDown | DirLeft,
        7 => DirLeft,
        8 => DirUp | DirLeft,
        _ => 0,
    };

    // Zona morta do analógico, mesma proporção do GamepadPoller (18000/32768
    // para começar, 8000/32768 para soltar — histerese contra ruído).
    private const double StickPress = 0.55;
    private const double StickRelease = 0.25;

    /// <summary>
    /// Analógico (x, y em -1..1, y POSITIVO = para baixo, como no HID) →
    /// UMA direção, a do eixo dominante: navegar menus não deve gerar
    /// diagonais. <paramref name="previous"/> é a direção anterior (0 = solto)
    /// e liga a histerese.
    /// </summary>
    public static int DirectionFromStick(double x, double y, int previous)
    {
        var ax = Math.Abs(x);
        var ay = Math.Abs(y);
        var magnitude = Math.Max(ax, ay);
        if (magnitude < (previous != 0 ? StickRelease : StickPress)) return 0;
        var candidate = ax >= ay
            ? (x > 0 ? DirRight : DirLeft)
            : (y > 0 ? DirDown : DirUp);
        // Trocar de direção exige passar do limiar de aperto: soltar o analógico
        // passando pelo meio não pode gerar um movimento espúrio.
        if (previous != 0 && candidate != previous && magnitude < StickPress) return previous;
        return candidate;
    }
}

/// <summary>
/// v0.25.9 — lado do launcher da <see cref="HidPadBridge"/>: transforma os
/// contadores publicados pelo ajudante em <see cref="GamepadAction"/>.
/// Só é consultado com o QS aberto e um jogo rodando.
/// </summary>
internal sealed class HidPadReader : IDisposable
{
    // Mesmos tempos do GamepadPoller (v0.25.9): 330 ms inicial, 110 ms depois, 70 ms após 1,5 s.
    private const int RepeatInitialDelayMs = 330;
    private const int RepeatIntervalMs = 110;
    private const int RepeatFastAfterMs = 1500;
    private const int RepeatFastIntervalMs = 70;
    private static readonly TimeSpan OpenRetry = TimeSpan.FromSeconds(1);

    private MemoryMappedFile? _file;
    private MemoryMappedViewAccessor? _view;
    private DateTime _lastOpenAttempt = DateTime.MinValue;
    private readonly int[] _lastCounters = new int[HidPadBridge.CounterCount];
    private bool _baselined;
    private readonly DateTime[] _heldSince = new DateTime[4];
    private readonly DateTime[] _lastRepeat = new DateTime[4];

    /// <summary>A ponte existe e o ajudante já publicou pelo menos um estado.</summary>
    public bool HasData => _view is not null && SafeRead(HidPadBridge.OffSequence) > 0;

    /// <summary>
    /// Zera a referência: apertos feitos ANTES (jogando, com o QS fechado) não
    /// viram ações no painel. Chamar na abertura do QS.
    /// </summary>
    public void Resync()
    {
        _baselined = false;
        Array.Clear(_heldSince);
        Array.Clear(_lastRepeat);
    }

    public IReadOnlyList<GamepadAction> Poll()
    {
        var actions = new List<GamepadAction>();
        if (!OperatingSystem.IsWindows() || !EnsureOpen()) return actions;

        try
        {
            if (_view!.ReadInt32(HidPadBridge.OffMagic) != HidPadBridge.Magic) return actions;

            var counters = new int[HidPadBridge.CounterCount];
            for (var i = 0; i < counters.Length; i++)
                counters[i] = _view.ReadInt32(HidPadBridge.OffCounters + i * 4);

            if (!_baselined)
            {
                Array.Copy(counters, _lastCounters, counters.Length);
                _baselined = true;
                return actions;
            }

            for (var i = 0; i < counters.Length; i++)
            {
                var delta = unchecked(counters[i] - _lastCounters[i]);
                _lastCounters[i] = counters[i];
                if (delta <= 0) continue;          // sem aperto novo (ou ponte recriada)
                delta = Math.Min(delta, 3);         // nunca despejar uma rajada no painel
                if (MapCounter(i) is { } action)
                    for (var n = 0; n < delta; n++) actions.Add(action);
            }

            // Repetição de direção segurada (mesmos tempos do GamepadPoller).
            var directions = _view.ReadInt32(HidPadBridge.OffDirections);
            var now = DateTime.UtcNow;
            AddRepeat(actions, directions, HidPadBridge.DirUp, 0, GamepadAction.MoveUp, now);
            AddRepeat(actions, directions, HidPadBridge.DirDown, 1, GamepadAction.MoveDown, now);
            AddRepeat(actions, directions, HidPadBridge.DirLeft, 2, GamepadAction.MoveLeft, now);
            AddRepeat(actions, directions, HidPadBridge.DirRight, 3, GamepadAction.MoveRight, now);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[QS-INPUT] Ponte HID: leitura falhou ({ex.GetType().Name}) — reabrindo.");
            Close();
        }

        if (actions.Count > 0)
            DebugLog.Write($"[QS-INPUT] Ponte HID → {string.Join(", ", actions)}");
        return actions;
    }

    private void AddRepeat(List<GamepadAction> actions, int directions, int bit, int index, GamepadAction action, DateTime now)
    {
        if ((directions & bit) == 0)
        {
            _heldSince[index] = default;
            return;
        }

        if (_heldSince[index] == default)
        {
            // Começou a segurar agora — o aperto em si já veio pelo contador.
            _heldSince[index] = now;
            _lastRepeat[index] = now;
            return;
        }

        if ((now - _heldSince[index]).TotalMilliseconds < RepeatInitialDelayMs) return;
        var heldMs = (now - _heldSince[index]).TotalMilliseconds;
        if ((now - _lastRepeat[index]).TotalMilliseconds < (heldMs >= RepeatFastAfterMs ? RepeatFastIntervalMs : RepeatIntervalMs)) return;
        _lastRepeat[index] = now;
        actions.Add(action);
    }

    /// <summary>Mesmo mapeamento do GamepadPoller (XInput); L3/R3 ficam com o ajudante.</summary>
    private static GamepadAction? MapCounter(int index) => index switch
    {
        0 => GamepadAction.Confirm,         // A
        1 => GamepadAction.Back,            // B
        2 => GamepadAction.GameDetails,     // X
        3 => GamepadAction.Connectivity,    // Y
        4 => GamepadAction.PreviousFilter,  // LB
        5 => GamepadAction.NextFilter,      // RB
        6 => GamepadAction.Menu,            // Back/View
        7 => GamepadAction.GlobalMenu,      // Start/Menu
        10 => GamepadAction.MoveUp,
        11 => GamepadAction.MoveDown,
        12 => GamepadAction.MoveLeft,
        13 => GamepadAction.MoveRight,
        _ => null,                          // 8, 9 = L3, R3
    };

    private bool EnsureOpen()
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (_view is not null) return true;
        if (DateTime.UtcNow - _lastOpenAttempt < OpenRetry) return false;
        _lastOpenAttempt = DateTime.UtcNow;
        try
        {
            _file = MemoryMappedFile.OpenExisting(HidPadBridge.MapName(Environment.ProcessId), MemoryMappedFileRights.Read);
            _view = _file.CreateViewAccessor(0, HidPadBridge.Capacity, MemoryMappedFileAccess.Read);
            _baselined = false;
            DebugLog.Write("[QS-INPUT] Ponte HID do ajudante conectada.");
            return true;
        }
        catch (FileNotFoundException)
        {
            return false; // ajudante ainda não criou (ou não está rodando)
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[QS-INPUT] Ponte HID indisponível ({ex.GetType().Name}: {ex.Message}).");
            Close();
            return false;
        }
    }

    private int SafeRead(int offset)
    {
        try { return _view?.ReadInt32(offset) ?? 0; }
        catch { return 0; }
    }

    private void Close()
    {
        try { _view?.Dispose(); } catch { /* melhor esforço */ }
        try { _file?.Dispose(); } catch { /* melhor esforço */ }
        _view = null;
        _file = null;
    }

    public void Dispose() => Close();
}

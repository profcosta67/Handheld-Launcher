using System.Diagnostics;
using System.Runtime.InteropServices;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// 22/11 — medição de FPS própria, sem o RTSS. O Windows registra (ETW) cada vez que um jogo
/// entrega um quadro: o evento "Present" do DXGI (Direct3D 10, 11 e 12) e do Direct3D 9. É a
/// mesma fonte que o PresentMon da Intel usa (MIT). Aqui só contamos esses eventos por processo:
/// FPS = quadros nos últimos 2 s / 2.
///
/// Limites desta primeira versão: jogos em Vulkan ou OpenGL que não passam pelo DXGI não aparecem
/// (para eles o RTSS continua sendo a fonte). Precisa de administrador — o launcher já roda assim.
///
/// Só fica ligado enquanto o TDP inteligente tem uma sessão de jogo (Start/Stop): fora disso,
/// nenhum evento é pedido ao Windows.
///
/// Tudo em melhor esforço: qualquer falha vira uma linha [FPS-PROPRIO] no debug.log e o launcher
/// segue com o RTSS.
/// </summary>
internal sealed class EtwFrameCounter : IDisposable
{
    public static EtwFrameCounter Shared { get; } = new();

    private const string SessionName = "HandheldLauncher-FPS";

    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private const ushort DxgiPresentStart = 42;
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private const ushort D3D9PresentStart = 1;

    private static readonly long WindowTicks = Stopwatch.Frequency * 2;      // janela de 2 s
    private static readonly long KeepTicks = Stopwatch.Frequency * 3;

    private readonly object _gate = new();          // ligar/desligar
    private readonly object _framesGate = new();    // contagem (a thread do ETW só pega este)
    private readonly Dictionary<int, Queue<long>> _frames = new();
    private ulong _sessionHandle;
    private ulong _traceHandle = InvalidTraceHandle;
    private IntPtr _properties;
    private IntPtr _logFile;
    private IntPtr _loggerName;
    private Thread? _thread;
    private Thread? _orphan;   // thread do ProcessTrace que não terminou no Stop: não religa enquanto viver

    // O Windows chama este callback de código nativo: um delegate estático nunca é coletado, mesmo
    // se uma thread antiga do ProcessTrace ainda estiver entregando eventos depois do Stop.
    private static readonly EventRecordCallback Callback = record => Shared.OnEvent(record);
    private volatile bool _running;
    private bool _failedOnce;

    public bool IsRunning => _running;

    /// <summary>Liga a contagem (idempotente). false = não deu (o motivo vai para o log).</summary>
    public bool Start()
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (IntPtr.Size != 8) return false;   // as posições das estruturas abaixo são de 64 bits
        lock (_gate)
        {
            if (_running) return true;
            if (_failedOnce) return false;   // não insiste a cada segundo
            if (_orphan is { IsAlive: true }) return false;
            _orphan = null;
            try
            {
                if (!StartSession())
                {
                    _failedOnce = true;
                    StopSessionUnlocked();   // não deixa sessão do Windows ligada à toa
                    return false;
                }
                _running = true;
                DebugLog.Write("[FPS-PROPRIO] Medição própria de FPS ligada (eventos Present do DXGI e do Direct3D 9).");
                return true;
            }
            catch (Exception ex)
            {
                _failedOnce = true;
                DebugLog.Write($"[FPS-PROPRIO] Não deu para ligar: {ex.GetType().Name}: {ex.Message}");
                StopSessionUnlocked();
                return false;
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return;
            _running = false;
            StopSessionUnlocked();
            lock (_framesGate) _frames.Clear();
            DebugLog.Write("[FPS-PROPRIO] Medição própria de FPS desligada.");
        }
    }

    /// <summary>
    /// FPS do processo, ou null se ele não entregou quadros há mais de 3 s. Usa o horário de cada
    /// evento (QPC, gravado pelo Windows na hora do quadro), não o da entrega: o Windows entrega os
    /// eventos em lotes (~1 por segundo) e contar pela entrega daria 30 FPS num jogo a 60.
    /// FPS = (quadros − 1) / (último − primeiro) nos 2 s que terminam no quadro mais recente.
    /// </summary>
    public double? TryGetFps(int pid)
    {
        if (!IsRunning) return null;
        lock (_framesGate)
        {
            if (!_frames.TryGetValue(pid, out var queue) || queue.Count < 2) return null;
            // Maior e menor horário (os eventos de CPUs diferentes podem chegar fora de ordem).
            var last = long.MinValue;
            foreach (var t in queue) if (t > last) last = t;
            if (Stopwatch.GetTimestamp() - last > KeepTicks) return null;   // parou de entregar
            var first = long.MaxValue;
            var n = 0;
            foreach (var t in queue)
            {
                if (last - t > WindowTicks) continue;
                if (t < first) first = t;
                n++;
            }
            if (n < 2 || last <= first) return null;
            return (n - 1) * (double)Stopwatch.Frequency / (last - first);
        }
    }

    public void Dispose() => Stop();

    // ===================================================================== sessão ETW

    private bool StartSession()
    {
        const int propsSize = 120;               // EVENT_TRACE_PROPERTIES (x64)
        const int nameBytes = 1024;
        var total = propsSize + nameBytes;
        _properties = Marshal.AllocHGlobal(total);
        Zero(_properties, total);
        WriteProperties(_properties, total, propsSize);

        var status = StartTraceW(out _sessionHandle, SessionName, _properties);
        if (status == ErrorAlreadyExists)
        {
            // Sessão de uma execução anterior que não foi fechada (launcher caiu): fecha e refaz.
            ControlTraceW(0, SessionName, _properties, EventTraceControlStop);
            Zero(_properties, total);
            WriteProperties(_properties, total, propsSize);
            status = StartTraceW(out _sessionHandle, SessionName, _properties);
        }
        if (status != 0)
        {
            DebugLog.Write($"[FPS-PROPRIO] StartTrace falhou (erro {status}{(status == 5 ? ": sem administrador" : "")}).");
            return false;
        }

        var dxgi = DxgiProvider;
        var d3d9 = D3D9Provider;
        var e1 = EnableTraceEx2(_sessionHandle, ref dxgi, EventControlCodeEnableProvider, TraceLevelInformation, ulong.MaxValue, 0, 0, IntPtr.Zero);
        var e2 = EnableTraceEx2(_sessionHandle, ref d3d9, EventControlCodeEnableProvider, TraceLevelInformation, ulong.MaxValue, 0, 0, IntPtr.Zero);
        if (e1 != 0 && e2 != 0)
        {
            DebugLog.Write($"[FPS-PROPRIO] Não consegui assinar os eventos (DXGI erro {e1}, D3D9 erro {e2}).");
            return false;
        }

        // EVENT_TRACE_LOGFILEW (x64, 448 bytes): LoggerName @8, ProcessTraceMode @28,
        // EventRecordCallback @424.
        const int logFileSize = 448;
        _logFile = Marshal.AllocHGlobal(logFileSize);
        Zero(_logFile, logFileSize);
        _loggerName = Marshal.StringToHGlobalUni(SessionName);
        Marshal.WriteIntPtr(_logFile, 8, _loggerName);
        Marshal.WriteInt32(_logFile, 28, ProcessTraceModeRealTime | ProcessTraceModeEventRecord);
        Marshal.WriteIntPtr(_logFile, 424, Marshal.GetFunctionPointerForDelegate(Callback));

        _traceHandle = OpenTraceW(_logFile);
        if (_traceHandle == InvalidTraceHandle)
        {
            DebugLog.Write($"[FPS-PROPRIO] OpenTrace falhou (erro {Marshal.GetLastWin32Error()}).");
            return false;
        }

        var handle = _traceHandle;
        _thread = new Thread(() =>
        {
            try
            {
                var handles = new[] { handle };
                var result = ProcessTrace(handles, 1, IntPtr.Zero, IntPtr.Zero);
                if (result != 0 && result != ErrorCancelled)
                    DebugLog.Write($"[FPS-PROPRIO] ProcessTrace terminou com erro {result}.");
            }
            catch (Exception ex) { DebugLog.Write($"[FPS-PROPRIO] Leitura parou: {ex.Message}"); }
        })
        { IsBackground = true, Name = "EtwFrameCounter", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
        return true;
    }

    private static void WriteProperties(IntPtr p, int total, int propsSize)
    {
        Marshal.WriteInt32(p, 0, total);                       // Wnode.BufferSize
        Marshal.WriteInt32(p, 40, 1);                          // Wnode.ClientContext = QPC
        Marshal.WriteInt32(p, 44, WnodeFlagTracedGuid);        // Wnode.Flags
        Marshal.WriteInt32(p, 48, 64);                         // BufferSize (KB)
        Marshal.WriteInt32(p, 52, 4);                          // MinimumBuffers
        Marshal.WriteInt32(p, 56, 16);                         // MaximumBuffers
        Marshal.WriteInt32(p, 64, EventTraceRealTimeMode);     // LogFileMode
        Marshal.WriteInt32(p, 68, 1);                          // FlushTimer (s)
        Marshal.WriteInt32(p, 116, propsSize);                 // LoggerNameOffset
    }

    private void StopSessionUnlocked()
    {
        try
        {
            if (_properties != IntPtr.Zero)
                ControlTraceW(0, SessionName, _properties, EventTraceControlStop);
            if (_traceHandle != InvalidTraceHandle)
                CloseTrace(_traceHandle);
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }

        _traceHandle = InvalidTraceHandle;
        _sessionHandle = 0;
        // A thread do ProcessTrace termina sozinha depois do CloseTrace; espera um pouco antes
        // de soltar a memória que o Windows ainda pode estar lendo.
        try { _thread?.Join(2000); } catch (Exception ex) { DebugLog.Swallowed(ex); }
        var threadDone = _thread is null || !_thread.IsAlive;
        if (!threadDone)
        {
            // O Windows ainda pode estar usando esta memória: fica (vazamento pequeno de propósito)
            // e a medição não religa enquanto essa thread existir.
            _orphan = _thread;
            _logFile = IntPtr.Zero;
            _loggerName = IntPtr.Zero;
            DebugLog.Write("[FPS-PROPRIO] A leitura do Windows não terminou em 2 s; a medição só religa quando ela terminar.");
        }
        _thread = null;
        if (threadDone)
        {
            if (_logFile != IntPtr.Zero) { Marshal.FreeHGlobal(_logFile); _logFile = IntPtr.Zero; }
            if (_loggerName != IntPtr.Zero) { Marshal.FreeHGlobal(_loggerName); _loggerName = IntPtr.Zero; }
        }
        if (_properties != IntPtr.Zero) { Marshal.FreeHGlobal(_properties); _properties = IntPtr.Zero; }
    }

    /// <summary>Chamado pelo Windows a cada evento (thread do ProcessTrace). Precisa ser rápido.</summary>
    private void OnEvent(IntPtr record)
    {
        try
        {
            // EVENT_RECORD.EventHeader: ProcessId @12, ProviderId @24, EventDescriptor.Id @40.
            var id = (ushort)Marshal.ReadInt16(record, 40);
            if (id != DxgiPresentStart && id != D3D9PresentStart) return;
            var provider = ReadGuid(record, 24);
            if (!(id == DxgiPresentStart && provider == DxgiProvider)
                && !(id == D3D9PresentStart && provider == D3D9Provider)) return;

            if (!_running) return;
            var pid = Marshal.ReadInt32(record, 12);
            var stamp = Marshal.ReadInt64(record, 16);   // EVENT_HEADER.TimeStamp (QPC: ClientContext = 1)
            lock (_framesGate)
            {
                if (!_frames.TryGetValue(pid, out var queue))
                {
                    if (_frames.Count >= 64)
                    {
                        // nunca cresce sem limite: tira os processos que pararam de entregar quadros
                        foreach (var key in _frames.Where(kv => kv.Value.Count == 0 || stamp - kv.Value.Last() > KeepTicks)
                                     .Select(kv => kv.Key).ToList())
                            _frames.Remove(key);
                        if (_frames.Count >= 64) return;
                    }
                    queue = new Queue<long>();
                    _frames[pid] = queue;
                }
                queue.Enqueue(stamp);
                while (queue.Count > 0 && stamp - queue.Peek() > KeepTicks) queue.Dequeue();
            }
        }
        catch
        {
            // Nunca deixar exceção escapar para o código nativo.
        }
    }

    private static Guid ReadGuid(IntPtr p, int offset)
    {
        var bytes = new byte[16];
        Marshal.Copy(p + offset, bytes, 0, 16);
        return new Guid(bytes);
    }

    private static void Zero(IntPtr p, int size)
    {
        for (var i = 0; i < size; i += 8) Marshal.WriteInt64(p, i, 0);
    }

    // ===================================================================== Win32

    private const int ErrorAlreadyExists = 183;
    private const int ErrorCancelled = 1223;
    private const int WnodeFlagTracedGuid = 0x00020000;
    private const int EventTraceRealTimeMode = 0x00000100;
    private const int EventTraceControlStop = 1;
    private const uint EventControlCodeEnableProvider = 1;
    private const byte TraceLevelInformation = 4;
    private const int ProcessTraceModeRealTime = 0x00000100;
    private const int ProcessTraceModeEventRecord = 0x10000000;
    private const ulong InvalidTraceHandle = ulong.MaxValue;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void EventRecordCallback(IntPtr eventRecord);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int StartTraceW(out ulong sessionHandle, string sessionName, IntPtr properties);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int ControlTraceW(ulong sessionHandle, string sessionName, IntPtr properties, int controlCode);

    [DllImport("advapi32.dll")]
    private static extern int EnableTraceEx2(ulong traceHandle, ref Guid providerId, uint controlCode, byte level,
        ulong matchAnyKeyword, ulong matchAllKeyword, uint timeout, IntPtr enableParameters);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern ulong OpenTraceW(IntPtr logfile);

    [DllImport("advapi32.dll")]
    private static extern int ProcessTrace([In] ulong[] handleArray, uint handleCount, IntPtr startTime, IntPtr endTime);

    [DllImport("advapi32.dll")]
    private static extern int CloseTrace(ulong traceHandle);
}

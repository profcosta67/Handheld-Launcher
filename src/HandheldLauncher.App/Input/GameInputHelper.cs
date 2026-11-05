using System.Diagnostics;
using System.Runtime.InteropServices;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Input;

/// <summary>
/// L3+R3 com o JOGO na frente (bug da v0.6.2: "quando tá com o launcher
/// aberto ele detecta e muda, quando tá na tela do jogo não detecta").
///
/// Causa: nas versões recentes do Windows a leitura de controle passa pelo
/// GameInput, que por padrão NÃO entrega botões pra programas em segundo
/// plano (só um estado "parado") — o XInput do launcher via o controle
/// solto enquanto o jogo tinha o foco. O GameInput tem uma opção explícita
/// pra isso: SetFocusPolicy(GameInputEnableBackgroundInput).
///
/// Como a API do GameInput é nativa (tabela de métodos COM) e não dá pra
/// testar aqui, ela roda num PROCESSO AJUDANTE separado — o próprio
/// HandheldLauncher.exe aberto com "--gameinput-helper &lt;pid&gt;". Se algo der
/// errado lá (GameInput ausente, versão diferente, até um crash), só o
/// ajudante cai; o launcher continua normal com o XInput de antes. Quando vê
/// L3+R3, o ajudante sinaliza um evento com nome do Windows que o launcher
/// está esperando.
///
/// v0.16: o ajudante só fica ligado enquanto um JOGO está rodando — é só
/// nesse momento que o launcher perde o foco e precisa da entrega em
/// segundo plano. Com o launcher na biblioteca (foco normal), quem detecta
/// L3+R3 é o <see cref="OverlayHotkeyWatcher"/>, via XInput comum — os dois
/// nunca ficam ligados ao mesmo tempo (evita os dois dispararem o mesmo
/// toggle do overlay em corrida, e reduz o tempo em que algo força entrega
/// de input em segundo plano, o que foi identificado como um provável
/// bloqueador de suspensão/Modern Standby no aparelho). Ver
/// MainWindow.SetGamepadComboDetectionForGame.
/// </summary>
internal static unsafe class GameInputHelper
{
    public const string Argument = "--gameinput-helper";
    public const string EventName = "HandheldLauncher_HudCycle_7f3a9c2e";

    private const uint GameInputKindGamepad = 0x00040000;
    private const uint GameInputEnableBackgroundInput = 0x40;
    private const uint LeftThumbstick = 0x1000;
    private const uint RightThumbstick = 0x2000;

    // Índices na tabela de métodos (GameInput.h v0), contando os 3 do IUnknown.
    private const int SlotRelease = 2;
    private const int SlotGetCurrentReading = 4;
    private const int SlotSetFocusPolicy = 21;
    private const int SlotGetGamepadState = 22;

    [StructLayout(LayoutKind.Sequential)]
    private struct GamepadState
    {
        public uint Buttons;
        public float LeftTrigger;
        public float RightTrigger;
        public float LeftThumbstickX;
        public float LeftThumbstickY;
        public float RightThumbstickX;
        public float RightThumbstickY;
    }

    [DllImport("GameInput.dll")]
    private static extern int GameInputCreate(out IntPtr gameInput);

    private static Process? _helper;

    /// <summary>v0.16.2: estado do ajudante pra Central de Ajuda.</summary>
    public static bool IsHelperRunning
    {
        get
        {
            try { return _helper is { HasExited: false }; }
            catch { return false; }
        }
    }

    public static int? LastExitCode { get; private set; }
    public static DateTime? LastExitAtLocal { get; private set; }

    /// <summary>
    /// Lado do launcher: abre o processo ajudante (melhor esforço). Idempotente —
    /// chamar com o ajudante já rodando não abre uma segunda cópia (v0.16:
    /// agora é ligado/desligado conforme um jogo está rodando ou não, então
    /// precisa ser seguro contra chamadas repetidas).
    /// </summary>
    public static void StartHelperProcess()
    {
        try
        {
            if (_helper is { HasExited: false }) return;

            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            _helper = Process.Start(new ProcessStartInfo(exe, $"{Argument} {Environment.ProcessId}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            DebugLog.Write($"GameInputHelper: ajudante iniciado (pid={_helper?.Id.ToString() ?? "null"}).");

            // v0.16.2 (diagnóstico): o ajudante loga "rodando" e some sem mais
            // nenhuma linha. O código de saída diz como ele morreu:
            // 0 = saiu sozinho; 0xC0000005 = crash nativo (access violation);
            // 0xE0434352 = exceção .NET não tratada; 0xFFFFFFFF = morto via
            // Kill() (o próprio StopHelperProcess ao voltar pra biblioteca).
            if (_helper is not null)
            {
                var helper = _helper;
                helper.EnableRaisingEvents = true;
                helper.Exited += (_, _) =>
                {
                    try
                    {
                        LastExitCode = helper.ExitCode;
                        LastExitAtLocal = DateTime.Now;
                        DebugLog.Write($"GameInputHelper: ajudante saiu (pid={helper.Id}, código=0x{helper.ExitCode:X8}).");
                    }
                    catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
                };
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"GameInputHelper: não consegui abrir o ajudante ({ex.Message})");
        }
    }

    /// <summary>
    /// Encerra o ajudante na hora (antes de aplicar uma atualização — ele roda
    /// do mesmo exe e seguraria os arquivos — ao fechar o launcher, ou ao
    /// voltar pra biblioteca depois de um jogo — v0.16). Idempotente.
    /// </summary>
    public static void StopHelperProcess()
    {
        try
        {
            if (_helper is { HasExited: false }) _helper.Kill();
        }
        catch
        {
            // Melhor esforço — ele também se encerra sozinho em até 2 s.
        }
        finally
        {
            _helper = null;
        }
    }

    /// <summary>Lado do launcher: thread que espera o sinal do ajudante.</summary>
    public static void ListenInBackground(Action onCombo)
    {
        var thread = new Thread(() =>
        {
            try
            {
                using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
                while (true)
                {
                    signal.WaitOne();
                    DebugLog.Write("GameInputHelper: sinal L3+R3 recebido do ajudante.");
                    onCombo();
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"GameInputHelper: listener parou ({ex.Message})");
            }
        })
        {
            IsBackground = true,
            Name = "GameInputHelperListener",
        };
        thread.Start();
    }

    /// <summary>Lado do ajudante: chamado no Program.Main quando o exe abre com --gameinput-helper.</summary>
    public static void RunHelper(string[] args)
    {
        var parentPid = args.Length > 1 && int.TryParse(args[1], out var pid) ? pid : 0;

        // v0.16.2 (diagnóstico): deixar rastro se o ajudante sair por
        // exceção .NET ou encerramento normal (crash nativo não passa aqui —
        // nesse caso o launcher registra o código de saída).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            DebugLog.Write($"GameInputHelper: exceção não tratada no ajudante: {e.ExceptionObject}");
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            DebugLog.Write("GameInputHelper: ajudante encerrando (ProcessExit).");

        using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);

        // v0.16.3: dois leitores no ajudante (GameInput + HID). Se os dois
        // virem o mesmo aperto, o overlay abriria e fecharia na hora — então
        // um disparo só a cada 600 ms, venha de onde vier.
        var fireLock = new object();
        var lastFire = DateTime.MinValue;
        void Fire(string source)
        {
            lock (fireLock)
            {
                if (DateTime.UtcNow - lastFire < TimeSpan.FromMilliseconds(600))
                {
                    DebugLog.Write($"GameInputHelper: L3+R3 repetido via {source} ignorado (já disparado).");
                    return;
                }
                lastFire = DateTime.UtcNow;
            }
            signal.Set();
        }

        // v0.16.3: leitor HID (Raw Input em segundo plano) — o log do beta.9
        // mostrou que, com o jogo em foco, o GameInput não entrega botão
        // nenhum. Ver HidComboReader.
        try { HidComboReader.Start(() => Fire("HID")); }
        catch (Exception ex) { DebugLog.Write($"GameInputHelper: leitor HID não iniciou ({ex.Message})"); }

        IntPtr gameInput;
        try
        {
            var hr = GameInputCreate(out gameInput);
            if (hr < 0 || gameInput == IntPtr.Zero)
            {
                DebugLog.Write($"GameInputHelper: GameInputCreate falhou (hr=0x{hr:X8}) — seguindo só com o leitor HID.");
                WaitForParentOnly(parentPid);
                return;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"GameInputHelper: GameInput indisponível ({ex.Message}) — seguindo só com o leitor HID.");
            WaitForParentOnly(parentPid);
            return;
        }

        var vtable = *(IntPtr**)gameInput;
        var setFocusPolicy = (delegate* unmanaged[Stdcall]<IntPtr, uint, void>)vtable[SlotSetFocusPolicy];
        var getCurrentReading = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr*, int>)vtable[SlotGetCurrentReading];

        setFocusPolicy(gameInput, GameInputEnableBackgroundInput);
        DebugLog.Write("GameInputHelper: rodando (entrada em segundo plano ligada)");
        LogLoadedGameInputModule();

        var wasDown = false;
        var lastParentCheck = DateTime.UtcNow;

        // v0.16.2 (diagnóstico): o ajudante sobe e liga a entrada em segundo
        // plano, mas não detecta L3+R3 com o jogo na frente. Estes contadores
        // mostram no debug.log se ele está lendo o controle (e o quê), ou se
        // GetCurrentReading está falhando — sem isso não dá pra saber.
        var okReads = 0;
        var failReads = 0;
        var lastHr = 0;
        uint lastButtons = 0;
        var buttonChangesLogged = 0;
        var lastHeartbeat = DateTime.UtcNow;

        DebugLog.Write($"GameInputHelper: entrando no loop de leitura (pai pid={parentPid}).");
        while (true)
        {
            if (DateTime.UtcNow - lastParentCheck > TimeSpan.FromSeconds(2))
            {
                lastParentCheck = DateTime.UtcNow;
                if (!IsAlive(parentPid))
                {
                    DebugLog.Write($"GameInputHelper: launcher (pid={parentPid}) não está mais vivo — ajudante saindo.");
                    return;
                }
            }

            IntPtr reading = IntPtr.Zero;
            var hr = getCurrentReading(gameInput, GameInputKindGamepad, IntPtr.Zero, &reading);
            if (hr >= 0 && reading != IntPtr.Zero)
            {
                okReads++;
                var readingTable = *(IntPtr**)reading;
                var getGamepadState = (delegate* unmanaged[Stdcall]<IntPtr, GamepadState*, byte>)readingTable[SlotGetGamepadState];
                var release = (delegate* unmanaged[Stdcall]<IntPtr, uint>)readingTable[SlotRelease];

                GamepadState state;
                var gotState = getGamepadState(reading, &state) != 0;
                release(reading);

                if (gotState && state.Buttons != lastButtons)
                {
                    // Limite pra não inundar o log se alguém ficar apertando tudo.
                    if (buttonChangesLogged < 60)
                    {
                        DebugLog.Write($"GameInputHelper: botões=0x{state.Buttons:X4}");
                        buttonChangesLogged++;
                    }
                    lastButtons = state.Buttons;
                }

                var isDown = gotState
                             && (state.Buttons & LeftThumbstick) != 0
                             && (state.Buttons & RightThumbstick) != 0;

                if (isDown && !wasDown)
                {
                    DebugLog.Write($"GameInputHelper: L3+R3 (via GameInput) · em foco: {HidComboReader.ForegroundDescription()}");
                    Fire("GameInput");
                }
                wasDown = isDown;
            }
            else
            {
                failReads++;
                lastHr = hr;
            }

            if (DateTime.UtcNow - lastHeartbeat > TimeSpan.FromSeconds(60))
            {
                var hidReports = Interlocked.Exchange(ref HidComboReader.ReportsSinceHeartbeat, 0);
                DebugLog.Write($"GameInputHelper: vivo (último minuto) — leituras ok={okReads}, falhas={failReads}, último hr=0x{lastHr:X8}; " +
                               $"relatórios HID={hidReports}; em foco: {HidComboReader.ForegroundDescription()}");
                okReads = 0;
                failReads = 0;
                lastHeartbeat = DateTime.UtcNow;
            }

            Thread.Sleep(30);
        }
    }

    /// <summary>
    /// v0.16.2 (diagnóstico): qual GameInput.dll foi carregado e a versão
    /// dele — o runtime do GameInput é atualizado pelo Windows separado do
    /// launcher, e a API v0 usada aqui pode se comportar diferente.
    /// </summary>
    /// <summary>
    /// v0.16.3: sem GameInput, o ajudante continua vivo só pelo leitor HID,
    /// e sai quando o launcher morre (mesma regra do loop principal).
    /// </summary>
    private static void WaitForParentOnly(int parentPid)
    {
        var lastHeartbeat = DateTime.UtcNow;
        while (IsAlive(parentPid))
        {
            Thread.Sleep(2000);
            if (DateTime.UtcNow - lastHeartbeat > TimeSpan.FromSeconds(60))
            {
                var hidReports = Interlocked.Exchange(ref HidComboReader.ReportsSinceHeartbeat, 0);
                DebugLog.Write($"GameInputHelper: vivo (só HID) — relatórios HID={hidReports}; em foco: {HidComboReader.ForegroundDescription()}");
                lastHeartbeat = DateTime.UtcNow;
            }
        }
        DebugLog.Write($"GameInputHelper: launcher (pid={parentPid}) não está mais vivo — ajudante saindo.");
    }

    private static void LogLoadedGameInputModule()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            foreach (ProcessModule module in self.Modules)
            {
                if (!string.Equals(module.ModuleName, "GameInput.dll", StringComparison.OrdinalIgnoreCase)) continue;
                DebugLog.Write($"GameInputHelper: GameInput.dll = '{module.FileName}' v{module.FileVersionInfo.FileVersion}");
                return;
            }
            DebugLog.Write("GameInputHelper: GameInput.dll não encontrado entre os módulos carregados.");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"GameInputHelper: não consegui ler a versão do GameInput.dll ({ex.Message})");
        }
    }

    private static bool IsAlive(int pid)
    {
        if (pid <= 0) return true;
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}

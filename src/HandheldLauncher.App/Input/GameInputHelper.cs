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
/// está esperando. O ajudante se encerra sozinho quando o launcher fecha.
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

    /// <summary>Lado do launcher: abre o processo ajudante (melhor esforço).</summary>
    public static void StartHelperProcess()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            _helper = Process.Start(new ProcessStartInfo(exe, $"{Argument} {Environment.ProcessId}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex)
        {
            DebugLog.Write($"GameInputHelper: não consegui abrir o ajudante ({ex.Message})");
        }
    }

    /// <summary>
    /// Encerra o ajudante na hora (antes de aplicar uma atualização — ele roda
    /// do mesmo exe e seguraria os arquivos — ou ao fechar o launcher).
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

        IntPtr gameInput;
        try
        {
            var hr = GameInputCreate(out gameInput);
            if (hr < 0 || gameInput == IntPtr.Zero)
            {
                DebugLog.Write($"GameInputHelper: GameInputCreate falhou (hr=0x{hr:X8})");
                return;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"GameInputHelper: GameInput indisponível ({ex.Message})");
            return;
        }

        var vtable = *(IntPtr**)gameInput;
        var setFocusPolicy = (delegate* unmanaged[Stdcall]<IntPtr, uint, void>)vtable[SlotSetFocusPolicy];
        var getCurrentReading = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr*, int>)vtable[SlotGetCurrentReading];

        setFocusPolicy(gameInput, GameInputEnableBackgroundInput);
        DebugLog.Write("GameInputHelper: rodando (entrada em segundo plano ligada)");

        using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        var wasDown = false;
        var lastParentCheck = DateTime.UtcNow;

        while (true)
        {
            if (DateTime.UtcNow - lastParentCheck > TimeSpan.FromSeconds(2))
            {
                lastParentCheck = DateTime.UtcNow;
                if (!IsAlive(parentPid)) return;
            }

            IntPtr reading = IntPtr.Zero;
            if (getCurrentReading(gameInput, GameInputKindGamepad, IntPtr.Zero, &reading) >= 0 && reading != IntPtr.Zero)
            {
                var readingTable = *(IntPtr**)reading;
                var getGamepadState = (delegate* unmanaged[Stdcall]<IntPtr, GamepadState*, byte>)readingTable[SlotGetGamepadState];
                var release = (delegate* unmanaged[Stdcall]<IntPtr, uint>)readingTable[SlotRelease];

                GamepadState state;
                var isDown = getGamepadState(reading, &state) != 0
                             && (state.Buttons & LeftThumbstick) != 0
                             && (state.Buttons & RightThumbstick) != 0;
                release(reading);

                if (isDown && !wasDown)
                {
                    DebugLog.Write("GameInputHelper: L3+R3");
                    signal.Set();
                }
                wasDown = isDown;
            }

            Thread.Sleep(30);
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

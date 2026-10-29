using System.Runtime.InteropServices;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Pede ao Windows 11 pra NÃO colocar o launcher em "modo de eficiência"
/// (EcoQoS) quando ele fica em segundo plano — nesse modo os timers do app
/// ficam lentos, e o atalho L3+R3 do HUD e o próprio HUD (atualização a cada
/// 1 s) perdiam toques/atrasavam com o jogo na frente. Custo de bateria
/// desprezível: o launcher quase não faz nada em segundo plano.
/// Melhor esforço: em Windows antigo a chamada só falha e nada muda.
/// </summary>
internal static class PowerThrottling
{
    private const int ProcessPowerThrottling = 4;
    private const uint ExecutionSpeed = 0x1;
    private const uint IgnoreTimerResolution = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessPowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr hProcess, int processInformationClass, ref ProcessPowerThrottlingState info, uint size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    public static void DisableForThisProcess()
    {
        try
        {
            var state = new ProcessPowerThrottlingState
            {
                Version = 1,
                ControlMask = ExecutionSpeed | IgnoreTimerResolution,
                StateMask = 0, // 0 = desligado pros bits do ControlMask
            };
            SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf(state));
        }
        catch
        {
            // Melhor esforço.
        }
    }
}

namespace HandheldLauncher.App.Platform;

/// <summary>v0.27: o que o vigia do jogo sabe e outros serviços precisam ler (fora da UI thread).</summary>
internal static class GameSessionInfo
{
    private static volatile int _detectedGamePid;

    /// <summary>PID do jogo com janela reconhecida; 0 enquanto o jogo ainda não apareceu.</summary>
    public static int DetectedGamePid
    {
        get => _detectedGamePid;
        set => _detectedGamePid = value;
    }

    private static volatile int _smartTdpWatts;

    /// <summary>Watts que o TDP inteligente está aplicando agora; 0 quando não está no comando.</summary>
    public static int SmartTdpWatts
    {
        get => _smartTdpWatts;
        set => _smartTdpWatts = value;
    }
}

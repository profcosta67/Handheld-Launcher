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
}

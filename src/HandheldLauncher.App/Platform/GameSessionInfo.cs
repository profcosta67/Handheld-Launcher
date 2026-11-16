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

    private static int _fanTouches;

    /// <summary>Conta cada vez que a ventoinha é aplicada pelo caminho do usuário (botões, perfil de TDP).</summary>
    public static int FanTouches => Volatile.Read(ref _fanTouches);
    public static void NoteFanTouch() => Interlocked.Increment(ref _fanTouches);

    private static long _userTdpRequestTicks;

    /// <summary>Marca um pedido de TDP feito pelo usuário (perfil ou watts no Quick Settings).</summary>
    public static void NoteUserTdpRequest() => Interlocked.Exchange(ref _userTdpRequestTicks, Environment.TickCount64);

    /// <summary>Instante (TickCount64) do último pedido de TDP do usuário; 0 se nunca pediu.</summary>
    public static long UserTdpRequestTicks => Interlocked.Read(ref _userTdpRequestTicks);

    /// <summary>Há quantos segundos o usuário pediu um TDP à mão (muito grande se nunca pediu).</summary>
    public static double SecondsSinceUserTdpRequest
    {
        get
        {
            var at = Interlocked.Read(ref _userTdpRequestTicks);
            return at == 0 ? double.MaxValue : (Environment.TickCount64 - at) / 1000.0;
        }
    }

    private static int _learningResets;

    /// <summary>Conta cada "resetar aprendizado" pedido no Quick Settings.</summary>
    public static int LearningResets => Volatile.Read(ref _learningResets);
    public static void NoteLearningReset(string? gameId)
    {
        _learningResetGameId = gameId;
        Interlocked.Increment(ref _learningResets);
    }

    private static volatile string? _learningResetGameId;

    /// <summary>Jogo do último reset (null = todos os jogos).</summary>
    public static string? LearningResetGameId => _learningResetGameId;

    private static volatile int _smartTdpPossibleFps;

    /// <summary>Meta fora de alcance: FPS que o jogo consegue e que o modo inteligente está segurando; 0 se a meta vale.</summary>
    public static int SmartTdpPossibleFps
    {
        get => _smartTdpPossibleFps;
        set => _smartTdpPossibleFps = value;
    }

    private static volatile int _smartTdpWatts;

    /// <summary>Watts que o TDP inteligente está aplicando agora; 0 quando não está no comando.</summary>
    public static int SmartTdpWatts
    {
        get => _smartTdpWatts;
        set => _smartTdpWatts = value;
    }
}

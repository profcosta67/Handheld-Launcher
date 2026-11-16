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

    /// <summary>Há quantos segundos o usuário pediu um TDP à mão (muito grande se nunca pediu).</summary>
    public static double SecondsSinceUserTdpRequest
    {
        get
        {
            var at = Interlocked.Read(ref _userTdpRequestTicks);
            return at == 0 ? double.MaxValue : (Environment.TickCount64 - at) / 1000.0;
        }
    }

    private static volatile int _smartTdpWatts;

    /// <summary>Watts que o TDP inteligente está aplicando agora; 0 quando não está no comando.</summary>
    public static int SmartTdpWatts
    {
        get => _smartTdpWatts;
        set => _smartTdpWatts = value;
    }
}

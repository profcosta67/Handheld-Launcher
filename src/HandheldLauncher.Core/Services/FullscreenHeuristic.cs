namespace HandheldLauncher.Core.Services;

/// <summary>Como a janela do jogo está no ecrã.</summary>
public enum GameWindowMode
{
    /// <summary>Janela, ou maximizada com barra de título — o painel pode aparecer por cima sem problema.</summary>
    Windowed,

    /// <summary>Sem bordas a ocupar o ecrã inteiro (flip-model): por cima disto o painel funciona.</summary>
    BorderlessFullscreen,

    /// <summary>Ecrã inteiro "a sério" (WS_POPUP sem legenda): aqui a imagem costuma ficar preta.</summary>
    FullscreenLikely,
}

/// <summary>
/// Decide se a janela do jogo está em ecrã inteiro — o caso em que o Quick
/// Settings pode deixar a imagem preta (o jogo perde a superfície exclusiva).
///
/// <para>Regras puras (sem P/Invoke) para poderem ser testadas: o
/// <c>Platform/FullscreenDetector</c> só lê o rect, os estilos e o monitor e
/// chama isto. É aviso, não bloqueio: nunca mexemos na janela do jogo.</para>
/// </summary>
public static class FullscreenHeuristic
{
    /// <summary>Tolerância (px) para "cobre o monitor" — evita falhar por 1 px de borda.</summary>
    private const int CoverTolerance = 2;

    public static GameWindowMode Classify(
        int windowWidth,
        int windowHeight,
        int monitorWidth,
        int monitorHeight,
        bool hasCaption,
        bool hasThickFrame,
        bool isPopup)
    {
        if (windowWidth <= 0 || windowHeight <= 0 || monitorWidth <= 0 || monitorHeight <= 0)
            return GameWindowMode.Windowed;

        var coversMonitor =
            windowWidth >= monitorWidth - CoverTolerance &&
            windowHeight >= monitorHeight - CoverTolerance;

        if (!coversMonitor) return GameWindowMode.Windowed;

        // Com barra de título ou bordas redimensionáveis é uma janela maximizada.
        if (hasCaption || hasThickFrame) return GameWindowMode.Windowed;

        return isPopup ? GameWindowMode.FullscreenLikely : GameWindowMode.BorderlessFullscreen;
    }

    /// <summary>
    /// `GameDVR_FSEBehaviorMode`: 2 = "otimizações de ecrã inteiro" desligadas,
    /// que é a causa mais comum de o jogo ficar em exclusivo verdadeiro.
    /// </summary>
    public static string DescribeFullscreenOptimizations(int? fseBehaviorMode) => fseBehaviorMode switch
    {
        2 => "otimizações de ecrã inteiro DESLIGADAS (FSEBehaviorMode=2) — provável causa",
        0 => "otimizações de ecrã inteiro ligadas",
        null => "definição de otimizações de ecrã inteiro desconhecida",
        var other => $"FSEBehaviorMode={other}",
    };

    /// <summary>Texto do aviso mostrado no painel.</summary>
    public static string? WarningFor(GameWindowMode mode) => mode == GameWindowMode.FullscreenLikely
        ? "Jogo em ecrã inteiro: o painel pode ficar por cima e a imagem ficar preta. Usa \"Janela sem bordas\" no jogo."
        : null;
}

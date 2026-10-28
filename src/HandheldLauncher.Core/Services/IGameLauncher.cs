using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

public interface IGameLauncher
{
    /// <summary>Raised when a launched game's process exits, with how long it ran.</summary>
    event EventHandler<GameExitedEventArgs>? GameExited;

    bool IsAnyGameRunning { get; }

    void Launch(GameEntry game);

    /// <summary>
    /// Forces a running game closed — the explicit "fechar jogo" action from
    /// the UI/controller, for when a game doesn't exit cleanly on its own
    /// (or, for Steam titles, since we don't yet detect their exit
    /// automatically — see GameLauncher's class comment).
    /// Safe to call even if the game isn't actually running.
    /// </summary>
    void CloseGame(GameEntry game);
}

public sealed class GameExitedEventArgs : EventArgs
{
    public required GameEntry Game { get; init; }
    public required TimeSpan Duration { get; init; }
}

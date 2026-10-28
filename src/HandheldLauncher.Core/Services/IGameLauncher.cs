using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

public interface IGameLauncher
{
    /// <summary>Raised when a launched game's process exits, with how long it ran.</summary>
    event EventHandler<GameExitedEventArgs>? GameExited;

    bool IsAnyGameRunning { get; }

    void Launch(GameEntry game);
}

public sealed class GameExitedEventArgs : EventArgs
{
    public required GameEntry Game { get; init; }
    public required TimeSpan Duration { get; init; }
}

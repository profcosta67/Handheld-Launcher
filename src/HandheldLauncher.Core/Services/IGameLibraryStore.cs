using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// Persists everything the launcher adds on top of what the scanners find:
/// manually-added (non-Steam) games, and per-game overrides like custom
/// artwork, collection assignment and locally-tracked playtime.
/// </summary>
public interface IGameLibraryStore
{
    IReadOnlyList<GameEntry> LoadManualGames();
    void AddManualGame(GameEntry game);
    void RemoveManualGame(string id);

    /// <summary>
    /// v0.18 (Gerenciador de armazenamento → mover jogo): troca só os
    /// caminhos de um jogo adicionado à mão, relendo o arquivo do disco
    /// (nunca regrava a cópia em memória, que já tem nome/tempo de jogo
    /// mesclados dos overrides). false = não é um jogo manual salvo.
    /// </summary>
    bool UpdateManualGamePaths(string id, string executablePath, string? workingDirectory, string? launchArguments);

    IReadOnlyDictionary<string, GameMetadataOverride> LoadOverrides();
    void SaveOverride(string gameId, GameMetadataOverride data);
}

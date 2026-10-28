namespace HandheldLauncher.Core.Models;

/// <summary>
/// Um jogo que a conta Steam do usuário TEM (comprado/resgatado), vindo da
/// Steam Web API (GetOwnedGames) — diferente de GameEntry, que é só pra
/// jogo JÁ instalado localmente. Usado pelo painel de Downloads pra
/// mostrar "na sua conta, não instalado" (ver ILibraryService.
/// GetOwnedButNotInstalledSteamGamesAsync) com um botão "Instalar" que
/// abre `steam://install/{appid}` — a Steam mesma baixa, este launcher
/// nunca baixa nada diretamente.
/// </summary>
public sealed class OwnedSteamGame
{
    public required uint AppId { get; set; }

    public required string Title { get; set; }

    public long PlaytimeMinutes { get; set; }
}

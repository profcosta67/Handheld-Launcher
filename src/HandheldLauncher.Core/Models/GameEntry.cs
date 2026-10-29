namespace HandheldLauncher.Core.Models;

/// <summary>
/// A single game/app the launcher knows about, regardless of where it came from.
/// This is the one shape the UI binds to — Steam scanner, manual entries and
/// future scanners (Xbox app, Epic, emulators) all normalize into this.
/// </summary>
public class GameEntry
{
    /// <summary>
    /// Stable identity for this entry. For Steam games this is "steam_{appid}".
    /// For manual entries it's a generated GUID. Used as the SteamGridDB lookup
    /// key cache and to detect duplicates across re-scans.
    /// </summary>
    public required string Id { get; set; }

    public required string Title { get; set; }

    public GameSource Source { get; set; } = GameSource.Manual;

    /// <summary>Steam AppId when Source == Steam, otherwise null.</summary>
    public uint? SteamAppId { get; set; }

    /// <summary>Full path to the executable to launch (manual/emulator entries).</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Command-line arguments passed to ExecutablePath, if any.</summary>
    public string? LaunchArguments { get; set; }

    /// <summary>
    /// Link oficial da loja pra abrir o jogo (ex: com.epicgames.launcher://...).
    /// Quando existe, GameLauncher usa ele em vez do .exe (o app da loja cuida
    /// de conta/DRM). Nunca persistido como override.
    /// </summary>
    public string? LaunchUri { get; set; }

    /// <summary>Working directory to launch from. Defaults to the exe's folder.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Install directory, when known (used to help locate save data, icons, etc).</summary>
    public string? InstallDirectory { get; set; }

    // --- Artwork, filled in by the SteamGridDB integration later ---
    public string? GridImagePath { get; set; }   // vertical/box cover (the tile art)
    public string? HeroImagePath { get; set; }   // wide banner for the "now selected" hero area
    public string? LogoImagePath { get; set; }   // transparent logo overlay
    public string? IconImagePath { get; set; }   // small icon (taskbar/list fallback)

    /// <summary>
    /// Capa LANDSCAPE independente pra WIDE CAPSULE ("último jogo
    /// aberto") — pedido explícito do usuário depois de reportar que a
    /// WIDE CAPSULE estava "puxando" (esticando) a capa vertical normal
    /// (GridImagePath, portrait 600x900) dentro de uma caixa larga. Vem do
    /// mesmo endpoint "/grids/" do SteamGridDB que GridImagePath (ainda é
    /// uma CAPA, não um banner), só filtrado por dimensões landscape
    /// (460x215/920x430 — "Steam Big Picture"/capsule horizontal) — regra
    /// do usuário: "tem que vir de capas mas com formato estilo banner,
    /// mas não pode ser a mesma do banner, elas têm que ser
    /// independentes". Nunca compartilha arquivo com GridImagePath nem
    /// com HeroImagePath.
    /// </summary>
    public string? WideGridImagePath { get; set; }

    /// <summary>Local playtime tracked by the launcher itself, in minutes.</summary>
    public long PlaytimeMinutes { get; set; }

    public DateTimeOffset? LastPlayedUtc { get; set; }

    /// <summary>User-defined grouping, e.g. "Mario Games", "Indies" (matches the folder concept in the reference UI).</summary>
    public string? Collection { get; set; }

    /// <summary>True while this game's process is currently running.</summary>
    public bool IsRunning { get; set; }

    /// <summary>User-toggled favorite — drives the "Favoritos" filter tab.</summary>
    public bool IsFavorite { get; set; }

    /// <summary>
    /// True só pras entradas SINTÉTICAS criadas pela aba "Não instalados"
    /// (ver ILibraryService.BuildEntryForOwnedGame) — um jogo que a conta
    /// Steam do usuário tem mas que ainda não foi instalado nesta máquina.
    /// Nunca true pra uma entrada de verdade vinda de um scanner ou de
    /// "Adicionar jogo"; ExecutablePath sempre null quando isto é true
    /// (não tem nada instalado pra apontar ainda). Drives o badge "Instalar"
    /// na tile (GameTileViewModel.IsOwnedNotInstalled) e troca o que
    /// confirmar/"Abrir jogo" faz pra essa tile (ver MainViewModel.
    /// LaunchSelected/BuildContextMenu) — instala em vez de lançar.
    /// </summary>
    public bool IsOwnedNotInstalled { get; set; }
}

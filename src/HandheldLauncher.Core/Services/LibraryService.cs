using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

public sealed class LibraryService : ILibraryService
{
    private readonly ISteamLibraryScanner _steamScanner;
    private readonly IXboxFolderScanner? _xboxFolderScanner;
    private readonly IReadOnlyList<IStoreLibraryScanner> _storeScanners;
    private readonly ISteamOwnedGamesClient? _steamOwnedGamesClient;
    private readonly IGameLibraryStore _store;
    private List<GameEntry> _games = new();

    /// <summary>
    /// xboxFolderScanner e steamOwnedGamesClient são opcionais (null =
    /// não configurado em settings.json, comportamento idêntico a antes
    /// de cada um existir) — ver XboxFolderScanner/ISteamOwnedGamesClient
    /// pra o que cada um faz.
    /// </summary>
    public LibraryService(
        ISteamLibraryScanner steamScanner,
        IGameLibraryStore store,
        IXboxFolderScanner? xboxFolderScanner = null,
        ISteamOwnedGamesClient? steamOwnedGamesClient = null,
        IReadOnlyList<IStoreLibraryScanner>? storeScanners = null)
    {
        _steamScanner = steamScanner;
        _store = store;
        _xboxFolderScanner = xboxFolderScanner;
        _storeScanners = storeScanners ?? Array.Empty<IStoreLibraryScanner>();
        _steamOwnedGamesClient = steamOwnedGamesClient;
        Refresh();
    }

    public IReadOnlyList<GameEntry> Games => _games;

    public void Refresh()
    {
        var overrides = _store.LoadOverrides();
        var merged = new List<GameEntry>();

        foreach (var game in _steamScanner.ScanInstalledGames())
        {
            ApplyOverride(game, overrides);
            merged.Add(game);
        }

        foreach (var game in _store.LoadManualGames())
        {
            ApplyOverride(game, overrides);
            merged.Add(game);
        }

        // XboxFolderScanner — pastas extras configuradas em
        // AppSettings.XboxScanFolders (nova numa leva anterior, ver essa
        // classe). Null quando nenhuma pasta está configurada, então nada
        // muda pra quem não configurou isso.
        if (_xboxFolderScanner is not null)
        {
            foreach (var game in _xboxFolderScanner.ScanInstalledGames())
            {
                // DEDUP contra jogo manual (bug reportado nesta leva: "ele
                // aparece como não steam e como xbox no launcher") —
                // acontece quando um jogo foi adicionado à mão (via
                // "Adicionar jogo", ANTES de XboxScanFolders existir ou
                // antes da pasta certa ser configurada) e agora a MESMA
                // instalação também é encontrada pelo scanner automático:
                // sem isso, os dois ficam como tiles separadas (Ids
                // diferentes) — uma em "Não Steam", outra em "Xbox".
                // Compara pelo EXECUTÁVEL (caminho completo, sem
                // diferenciar maiúsculas) em vez do Id (que são formatos
                // diferentes e nunca vão bater): se já existe QUALQUER
                // entrada em `merged` apontando pro mesmo .exe, pula o
                // duplicado do scanner. De propósito prioriza o que já
                // está em `merged` (o manual, que entrou no loop acima)
                // em vez do recém-escaneado, pra não perder o Id/overrides
                // (favorito, capas já escolhidas) de um jogo que o usuário
                // talvez já tenha customizado à mão. Efeito colateral:
                // ele continua classificado como "Não Steam"/Manual, não
                // "Xbox" — pra corrigir a categoria, é preciso apagar o
                // atalho manual (Excluir atalho) e deixar o scanner
                // recriá-lo do zero (perde favorito/capas já escolhidas
                // nesse caso).
                if (merged.Any(existing => ExecutablePathsMatch(existing.ExecutablePath, game.ExecutablePath)))
                {
                    continue;
                }

                ApplyOverride(game, overrides);
                merged.Add(game);
            }
        }

        // Epic Games Store (v0.9.1) — só instalados, lidos dos manifestos
        // locais (ver EpicLibraryScanner). Mesmo dedup do Xbox: se o
        // usuário já tinha adicionado o jogo à mão (mesmo .exe), mantém o
        // manual (favorito/capas escolhidas) e pula o da Epic.
        // v0.9.3: o mesmo laço vale pra todas as lojas (Epic, GOG, EA,
        // Ubisoft). Cada scanner é isolado: se um falhar, os outros seguem.
        foreach (var scanner in _storeScanners)
        {
            IReadOnlyList<GameEntry> scanned;
            try
            {
                scanned = scanner.ScanInstalledGames();
            }
            catch
            {
                continue;
            }

            foreach (var game in scanned)
            {
                if (merged.Any(existing => SameLaunchTarget(existing, game) || SameInstallFolder(existing, game)))
                {
                    continue;
                }

                ApplyOverride(game, overrides);
                merged.Add(game);
            }
        }

        // Future scanners (emulator folders, GOG...) plug in here the same
        // way: scan -> ApplyOverride -> merged.Add.

        merged.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
        _games = merged;
    }

    /// <summary>
    /// Compara dois caminhos de executável de forma tolerante (resolve
    /// "..", separadores diferentes etc. via Path.GetFullPath; Windows não
    /// diferencia maiúsculas em caminho de arquivo) — null/vazio nunca
    /// "bate" com nada, pra não tratar dois jogos sem ExecutablePath como
    /// duplicados um do outro.
    /// </summary>
    /// <summary>
    /// v0.9.17, BUG REPORTADO pelo usuário: "ele detectou apenas uma room
    /// que na vdd eh uma dlc nsp do digimon time strange. as outras rooms
    /// ele não detectou" — 18 arquivos .nsp na pasta, 1 jogo na
    /// biblioteca.
    ///
    /// <para>
    /// Causa: o dedup deste laço foi escrito pensando em LOJAS, onde vale
    /// "um executável = um jogo" e "uma pasta = um jogo". Pra EMULADOR as
    /// duas premissas são falsas: todos os jogos do Eden compartilham o
    /// MESMO executável (o `eden.exe`) e normalmente a MESMA pasta (a das
    /// ROMs). Então o 1º jogo entrava e todos os outros eram descartados
    /// como duplicata dele — sobrava exatamente um, o primeiro em ordem
    /// alfabética (no caso do usuário, uma DLC do Digimon).
    /// </para>
    ///
    /// <para>
    /// Correção: o que identifica o jogo não é só o executável, é o
    /// executável MAIS os argumentos (pro Eden, o argumento é o caminho da
    /// ROM — ver EdenLibraryScanner). Pra entradas de loja, que não têm
    /// argumento nenhum, os dois lados ficam vazios e o comportamento é
    /// idêntico ao de antes.
    /// </para>
    /// </summary>
    private static bool SameLaunchTarget(GameEntry a, GameEntry b)
    {
        if (!ExecutablePathsMatch(a.ExecutablePath, b.ExecutablePath)) return false;

        return string.Equals(
            a.LaunchArguments?.Trim() ?? string.Empty,
            b.LaunchArguments?.Trim() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Mesma causa do bug acima, outra regra: pra emulador, a "pasta de
    /// instalação" é a pasta das ROMs — compartilhada por todos os jogos
    /// dali — então comparar por pasta jogaria fora todos menos o
    /// primeiro. Nunca trata entrada de emulador como duplicata por pasta;
    /// pra loja, segue valendo igual antes (ver SameFolder).
    /// </summary>
    private static bool SameInstallFolder(GameEntry a, GameEntry b)
    {
        if (a.Source == GameSource.Emulator || b.Source == GameSource.Emulator) return false;
        return SameFolder(a.InstallDirectory, b.InstallDirectory);
    }

    /// <summary>
    /// Mesma pasta de instalação = mesmo jogo (v0.9.3) — evita, por exemplo,
    /// um jogo da EA comprado na Steam aparecer duas vezes (Steam + EA).
    /// </summary>
    private static bool SameFolder(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool ExecutablePathsMatch(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;

        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            // Caminho malformado — trata como "não bate" em vez de deixar
            // Refresh() inteiro explodir por causa de um caminho estranho.
            return false;
        }
    }

    public void AddManualGame(GameEntry game)
    {
        // Não força mais Source = Manual aqui: a tela "Adicionar jogo" agora
        // deixa marcar "É um jogo do Xbox/Game Pass", e sobrescrever sempre
        // pra Manual jogaria essa escolha fora. O default do próprio
        // GameEntry (Manual) já cobre o caso comum.
        _store.AddManualGame(game);
        Refresh();
    }

    public void RemoveManualGame(string id)
    {
        _store.RemoveManualGame(id);
        Refresh();
    }

    public void SetCollection(string gameId, string? collection)
    {
        var existing = _games.FirstOrDefault(g => g.Id == gameId);
        var overrideData = ToOverride(existing) ?? new GameMetadataOverride();
        overrideData.Collection = collection;
        _store.SaveOverride(gameId, overrideData);
        Refresh();
    }

    public void SetFavorite(string gameId, bool isFavorite)
    {
        var existing = _games.FirstOrDefault(g => g.Id == gameId);
        var overrideData = ToOverride(existing) ?? new GameMetadataOverride();
        overrideData.IsFavorite = isFavorite;
        _store.SaveOverride(gameId, overrideData);

        // Mesma ideia do MarkLaunched: muta a instância em memória direto em
        // vez de dar Refresh() completo, pra não perder SelectedTile/etc e
        // pro toggle refletir na hora (a aba "Favoritos" depende disso).
        if (existing is not null) existing.IsFavorite = isFavorite;
    }

    public void RecordPlaySession(string gameId, TimeSpan duration)
    {
        var existing = _games.FirstOrDefault(g => g.Id == gameId);
        var overrideData = ToOverride(existing) ?? new GameMetadataOverride();
        overrideData.PlaytimeMinutes += (long)duration.TotalMinutes;
        overrideData.LastPlayedUtc = DateTimeOffset.UtcNow;
        _store.SaveOverride(gameId, overrideData);
        Refresh();
    }

    public void MarkLaunched(string gameId)
    {
        var existing = _games.FirstOrDefault(g => g.Id == gameId);
        var overrideData = ToOverride(existing) ?? new GameMetadataOverride();
        overrideData.LastPlayedUtc = DateTimeOffset.UtcNow;
        _store.SaveOverride(gameId, overrideData);

        // No Refresh() here on purpose — a full re-scan would replace every
        // GameEntry instance, orphaning whatever the UI currently holds
        // (SelectedTile, RunningGame...) for no benefit. Mutate the live
        // instance directly so the "último jogo aberto" change is visible
        // immediately.
        if (existing is not null) existing.LastPlayedUtc = overrideData.LastPlayedUtc;
    }

    public IReadOnlyList<DownloadProgressInfo> GetActiveDownloads() => _steamScanner.ScanActiveDownloads();

    public bool HasSteamWebApiCredentials => _steamOwnedGamesClient?.HasCredentials ?? false;

    public async Task<IReadOnlyList<OwnedSteamGame>> GetOwnedButNotInstalledSteamGamesAsync()
    {
        if (_steamOwnedGamesClient is null || !_steamOwnedGamesClient.HasCredentials)
        {
            return Array.Empty<OwnedSteamGame>();
        }

        var owned = await _steamOwnedGamesClient.GetOwnedGamesAsync();

        // _games (não Games da MainViewModel, que é filtrada pela aba
        // ativa) — comparar contra a biblioteca FILTRADA esconderia jogo
        // já instalado sempre que uma aba diferente de "Todos" estivesse
        // selecionada no momento, mostrando ele como "não instalado"
        // erradamente.
        var installedAppIds = _games
            .Where(g => g.Source == GameSource.Steam && g.SteamAppId is not null)
            .Select(g => g.SteamAppId!.Value)
            .ToHashSet();

        return owned.Where(g => !installedAppIds.Contains(g.AppId)).ToList();
    }

    public GameEntry BuildEntryForOwnedGame(OwnedSteamGame owned)
    {
        var game = new GameEntry
        {
            Id = $"steam_{owned.AppId}",
            Title = owned.Title,
            Source = GameSource.Steam,
            SteamAppId = owned.AppId,
            IsOwnedNotInstalled = true,
        };

        ApplyOverride(game, _store.LoadOverrides());

        // Nunca "último jogo aberto" (WIDE CAPSULE) pra um jogo que nem
        // está instalado — mesmo que o override tenha uma data de uma
        // instalação anterior (jogo já desinstalado e reinstalado antes).
        game.LastPlayedUtc = null;

        return game;
    }

    private static void ApplyOverride(GameEntry game, IReadOnlyDictionary<string, GameMetadataOverride> overrides)
    {
        if (!overrides.TryGetValue(game.Id, out var o)) return;

        // v0.9.19: nome editado pelo usuário vence o do scanner. Só
        // sobrescreve se tiver algo de verdade — override vazio/em branco
        // deixaria a tile sem nome nenhum.
        if (!string.IsNullOrWhiteSpace(o.TitleOverride)) game.Title = o.TitleOverride;
        game.TitleOverride = o.TitleOverride;
        game.IsApplication = o.IsApplication;

        game.Collection = o.Collection;
        game.GridImagePath = o.GridImagePath;
        game.HeroImagePath = o.HeroImagePath;
        game.LogoImagePath = o.LogoImagePath;
        game.IconImagePath = o.IconImagePath;
        game.WideGridImagePath = o.WideGridImagePath;
        game.PlaytimeMinutes = o.PlaytimeMinutes;
        game.LastPlayedUtc = o.LastPlayedUtc;
        game.IsFavorite = o.IsFavorite;
    }

    /// <summary>
    /// v0.9.19: salva nome editado + "é um programa" (ver
    /// GameMetadataOverride.TitleOverride/IsApplication). Nome vazio
    /// LIMPA o override, voltando pro nome original do scanner — é o jeito
    /// de desfazer um renomear sem ter que lembrar qual era o nome.
    /// </summary>
    public void SaveEntryEdits(string gameId, string? title, bool isApplication)
    {
        var existing = _games.FirstOrDefault(g => g.Id == gameId);
        var overrideData = ToOverride(existing) ?? new GameMetadataOverride();
        overrideData.TitleOverride = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        overrideData.IsApplication = isApplication;
        _store.SaveOverride(gameId, overrideData);
        Refresh();
    }

    private static GameMetadataOverride? ToOverride(GameEntry? game)
    {
        if (game is null) return null;
        return new GameMetadataOverride
        {
            Collection = game.Collection,
            GridImagePath = game.GridImagePath,
            HeroImagePath = game.HeroImagePath,
            LogoImagePath = game.LogoImagePath,
            IconImagePath = game.IconImagePath,
            WideGridImagePath = game.WideGridImagePath,
            PlaytimeMinutes = game.PlaytimeMinutes,
            LastPlayedUtc = game.LastPlayedUtc,
            IsFavorite = game.IsFavorite,
            // v0.9.19: os dois PRECISAM viajar aqui — sem isso, favoritar
            // (ou mudar coleção, ou registrar tempo de jogo) num jogo
            // renomeado salvaria um override sem eles e apagaria o nome
            // escolhido e a marca de "é um programa". Ver
            // GameEntry.TitleOverride.
            TitleOverride = game.TitleOverride,
            IsApplication = game.IsApplication,
        };
    }
}

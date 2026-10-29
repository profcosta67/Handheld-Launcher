using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services.Artwork;

/// <summary>
/// Sits between ISteamGridDbClient (talks to the API) and IGameLibraryStore
/// (persists the result): resolves the right lookup per game source, downloads
/// the chosen image to a local cache folder, and saves the path as an override
/// so a later re-scan doesn't lose it.
/// </summary>
public sealed class ArtworkService : IArtworkService
{
    private readonly ISteamGridDbClient _client;
    private readonly IGameLibraryStore _store;
    private readonly HttpClient _downloadHttp;
    private readonly string _cacheDir;

    public ArtworkService(ISteamGridDbClient client, IGameLibraryStore store, string? cacheDirOverride = null)
    {
        _client = client;
        _store = store;
        _downloadHttp = new HttpClient();
        _cacheDir = cacheDirOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HandheldLauncher", "artwork");
    }

    public bool HasApiKey => _client.HasApiKey;

    public async Task<bool> FetchGridAsync(GameEntry game, CancellationToken ct = default)
    {
        if (!HasApiKey) return false;

        var image = game.Source == GameSource.Steam && game.SteamAppId is { } appId
            ? await _client.GetBestGridForSteamAppAsync(appId, ct)
            : await _client.GetBestGridForTitleAsync(game.Title, ct);

        if (image is null) return false;

        var localPath = await DownloadToCache(image.Url, _cacheDir, $"{game.Id}_grid", ct);
        if (localPath is null) return false;

        var data = GetOrCreateOverride(game.Id);
        data.GridImagePath = localPath;
        _store.SaveOverride(game.Id, data);

        game.GridImagePath = localPath;
        return true;
    }

    public async Task<bool> FetchHeroAsync(GameEntry game, CancellationToken ct = default)
    {
        if (!HasApiKey) return false;

        var image = game.Source == GameSource.Steam && game.SteamAppId is { } appId
            ? await _client.GetBestHeroForSteamAppAsync(appId, ct)
            : await _client.GetBestHeroForTitleAsync(game.Title, ct);

        if (image is null) return false;

        var localPath = await DownloadToCache(image.Url, _cacheDir, $"{game.Id}_hero", ct);
        if (localPath is null) return false;

        var data = GetOrCreateOverride(game.Id);
        data.HeroImagePath = localPath;
        _store.SaveOverride(game.Id, data);

        game.HeroImagePath = localPath;
        return true;
    }

    /// <summary>Same shape as FetchHeroAsync, but for the transparent "logo" wordmark (game.LogoImagePath) — the "CURRENT LOGO" overlay on the row's wide tile.</summary>
    public async Task<bool> FetchLogoAsync(GameEntry game, CancellationToken ct = default)
    {
        if (!HasApiKey) return false;

        var image = game.Source == GameSource.Steam && game.SteamAppId is { } appId
            ? await _client.GetBestLogoForSteamAppAsync(appId, ct)
            : await _client.GetBestLogoForTitleAsync(game.Title, ct);

        if (image is null) return false;

        var localPath = await DownloadToCache(image.Url, _cacheDir, $"{game.Id}_logo", ct);
        if (localPath is null) return false;

        TrimTransparentPadding(localPath);

        var data = GetOrCreateOverride(game.Id);
        data.LogoImagePath = localPath;
        _store.SaveOverride(game.Id, data);

        game.LogoImagePath = localPath;
        return true;
    }

    /// <summary>Ícone quadrado (game.IconImagePath) pra tela "Iniciando..." — mesmo formato de FetchLogoAsync.</summary>
    public async Task<bool> FetchIconAsync(GameEntry game, CancellationToken ct = default)
    {
        if (!HasApiKey) return false;

        var image = game.Source == GameSource.Steam && game.SteamAppId is { } appId
            ? await _client.GetBestIconForSteamAppAsync(appId, ct)
            : await _client.GetBestIconForTitleAsync(game.Title, ct);

        if (image is null) return false;

        var localPath = await DownloadToCache(image.Url, _cacheDir, $"{game.Id}_icon", ct);
        if (localPath is null) return false;

        var data = GetOrCreateOverride(game.Id);
        data.IconImagePath = localPath;
        _store.SaveOverride(game.Id, data);

        game.IconImagePath = localPath;
        return true;
    }

    /// <summary>
    /// Same shape as FetchGridAsync, mas pra uma capa LANDSCAPE
    /// (game.WideGridImagePath) — a WIDE CAPSULE ("último jogo aberto")
    /// usa isso em vez de GridImagePath, pedido do usuário depois de
    /// reportar a capa portrait sendo esticada dentro da caixa larga.
    /// </summary>
    public async Task<bool> FetchWideGridAsync(GameEntry game, CancellationToken ct = default)
    {
        if (!HasApiKey) return false;

        var image = game.Source == GameSource.Steam && game.SteamAppId is { } appId
            ? await _client.GetBestWideGridForSteamAppAsync(appId, ct)
            : await _client.GetBestWideGridForTitleAsync(game.Title, ct);

        if (image is null) return false;

        var localPath = await DownloadToCache(image.Url, _cacheDir, $"{game.Id}_widegrid", ct);
        if (localPath is null) return false;

        var data = GetOrCreateOverride(game.Id);
        data.WideGridImagePath = localPath;
        _store.SaveOverride(game.Id, data);

        game.WideGridImagePath = localPath;
        return true;
    }

    public async Task<IReadOnlyList<ArtworkCandidate>> GetGridCandidatesAsync(GameEntry game, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<ArtworkCandidate>();

        var images = game.Source == GameSource.Steam && game.SteamAppId is { } appId
            ? await _client.GetGridCandidatesForSteamAppAsync(appId, ct)
            : await _client.GetGridCandidatesForTitleAsync(game.Title, ct);

        return await DownloadCandidatesAsync(images, $"{game.Id}_grid_candidate", ct);
    }

    public async Task<IReadOnlyList<ArtworkCandidate>> GetHeroCandidatesAsync(GameEntry game, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<ArtworkCandidate>();

        var images = game.Source == GameSource.Steam && game.SteamAppId is { } appId
            ? await _client.GetHeroCandidatesForSteamAppAsync(appId, ct)
            : await _client.GetHeroCandidatesForTitleAsync(game.Title, ct);

        return await DownloadCandidatesAsync(images, $"{game.Id}_hero_candidate", ct);
    }

    public async Task<IReadOnlyList<ArtworkCandidate>> GetLogoCandidatesAsync(GameEntry game, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<ArtworkCandidate>();

        var images = game.Source == GameSource.Steam && game.SteamAppId is { } appId
            ? await _client.GetLogoCandidatesForSteamAppAsync(appId, ct)
            : await _client.GetLogoCandidatesForTitleAsync(game.Title, ct);

        return await DownloadCandidatesAsync(images, $"{game.Id}_logo_candidate", ct);
    }

    public async Task<IReadOnlyList<ArtworkCandidate>> GetIconCandidatesAsync(GameEntry game, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<ArtworkCandidate>();

        var images = game.Source == GameSource.Steam && game.SteamAppId is { } appId
            ? await _client.GetIconCandidatesForSteamAppAsync(appId, ct)
            : await _client.GetIconCandidatesForTitleAsync(game.Title, ct);

        return await DownloadCandidatesAsync(images, $"{game.Id}_icon_candidate", ct);
    }

    public async Task<IReadOnlyList<ArtworkCandidate>> GetWideGridCandidatesAsync(GameEntry game, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<ArtworkCandidate>();

        var images = game.Source == GameSource.Steam && game.SteamAppId is { } appId
            ? await _client.GetWideGridCandidatesForSteamAppAsync(appId, ct)
            : await _client.GetWideGridCandidatesForTitleAsync(game.Title, ct);

        return await DownloadCandidatesAsync(images, $"{game.Id}_widegrid_candidate", ct);
    }

    public Task<bool> ApplyGridCandidateAsync(GameEntry game, ArtworkCandidate candidate, CancellationToken ct = default)
        => ApplyCandidateAsync(game, candidate, ArtworkSlot.Grid);

    public Task<bool> ApplyHeroCandidateAsync(GameEntry game, ArtworkCandidate candidate, CancellationToken ct = default)
        => ApplyCandidateAsync(game, candidate, ArtworkSlot.Hero);

    public Task<bool> ApplyLogoCandidateAsync(GameEntry game, ArtworkCandidate candidate, CancellationToken ct = default)
        => ApplyCandidateAsync(game, candidate, ArtworkSlot.Logo);

    public Task<bool> ApplyIconCandidateAsync(GameEntry game, ArtworkCandidate candidate, CancellationToken ct = default)
        => ApplyCandidateAsync(game, candidate, ArtworkSlot.Icon);

    public Task<bool> ApplyWideGridCandidateAsync(GameEntry game, ArtworkCandidate candidate, CancellationToken ct = default)
        => ApplyCandidateAsync(game, candidate, ArtworkSlot.WideGrid);

    /// <summary>Which GameEntry/GameMetadataOverride field ApplyCandidateAsync below writes to — grid/hero/logo/widegrid, matching the 4 picker types.</summary>
    private enum ArtworkSlot { Grid, Hero, Logo, WideGrid, Icon }

    /// <summary>
    /// Downloads every candidate image to disk (once — a file that's
    /// already cached from a previous picker open, keyed by SteamGridDB's
    /// own image Id, is left alone) so the picker UI can bind straight to
    /// local paths. One bad candidate (network hiccup, bad URL) just gets
    /// skipped instead of failing the whole picker.
    /// </summary>
    private async Task<IReadOnlyList<ArtworkCandidate>> DownloadCandidatesAsync(
        IReadOnlyList<SteamGridDbImage> images, string filenamePrefix, CancellationToken ct)
    {
        var candidatesDir = Path.Combine(_cacheDir, "candidates");
        var results = new List<ArtworkCandidate>();

        foreach (var image in images)
        {
            var localPath = await DownloadToCache(image.Url, candidatesDir, $"{filenamePrefix}_{image.Id}", ct, skipIfExists: true);
            if (localPath is null) continue;

            results.Add(new ArtworkCandidate
            {
                Id = image.Id,
                RemoteUrl = image.Url,
                LocalThumbnailPath = localPath,
                Width = image.Width,
                Height = image.Height,
            });
        }

        return results;
    }

    /// <summary>
    /// Candidate's local file (downloaded when the picker opened) already
    /// IS the real image at full resolution — SteamGridDB doesn't offer a
    /// separate thumbnail endpoint, and resizing ourselves would need an
    /// imaging library we don't have. So "applying" a pick is just copying
    /// that cached file into the game's grid/hero/logo path; no second
    /// download needed.
    ///
    /// Filename includes the candidate's own SteamGridDB image Id (not
    /// just "{game.Id}_grid") specifically so this NEVER overwrites the
    /// file that's currently on screen — overwriting it in place risked a
    /// Windows file-lock conflict with whatever still had that exact file
    /// open for display, which is the likely cause of a user-reported "the
    /// first apply silently does nothing, the second one works" bug: a
    /// failed overwrite was caught and swallowed as a quiet "false", with
    /// nothing visibly different, until the lock cleared on a later try.
    /// Old grid/hero/logo files from previous picks are left behind as
    /// harmless orphans in the same cache folder the README already tells
    /// the user they can clear anytime.
    /// </summary>
    private Task<bool> ApplyCandidateAsync(GameEntry game, ArtworkCandidate candidate, ArtworkSlot slot)
    {
        Directory.CreateDirectory(_cacheDir);
        var extension = Path.GetExtension(candidate.LocalThumbnailPath);
        var suffix = slot switch { ArtworkSlot.Hero => "hero", ArtworkSlot.Logo => "logo", ArtworkSlot.WideGrid => "widegrid", ArtworkSlot.Icon => "icon", _ => "grid" };
        var localPath = Path.Combine(_cacheDir, $"{game.Id}_{suffix}_{candidate.Id}{extension}");

        try
        {
            File.Copy(candidate.LocalThumbnailPath, localPath, overwrite: true);
        }
        catch (IOException)
        {
            return Task.FromResult(false);
        }

        if (slot == ArtworkSlot.Logo)
        {
            // Mesmo corte de margem transparente do fluxo automático
            // (FetchLogoAsync) — sem isso, escolher o LOGO manualmente no
            // picker (ApplyLogoCandidateAsync) continuaria herdando o
            // tamanho inconsistente do PNG original da SteamGridDB.
            TrimTransparentPadding(localPath);
        }

        var data = GetOrCreateOverride(game.Id);
        switch (slot)
        {
            case ArtworkSlot.Hero:
                data.HeroImagePath = localPath;
                game.HeroImagePath = localPath;
                break;
            case ArtworkSlot.Logo:
                data.LogoImagePath = localPath;
                game.LogoImagePath = localPath;
                break;
            case ArtworkSlot.WideGrid:
                data.WideGridImagePath = localPath;
                game.WideGridImagePath = localPath;
                break;
            case ArtworkSlot.Icon:
                data.IconImagePath = localPath;
                game.IconImagePath = localPath;
                break;
            default:
                data.GridImagePath = localPath;
                game.GridImagePath = localPath;
                break;
        }
        _store.SaveOverride(game.Id, data);

        return Task.FromResult(true);
    }

    private async Task<string?> DownloadToCache(string url, string directory, string filenameStem, CancellationToken ct, bool skipIfExists = false)
    {
        Directory.CreateDirectory(directory);

        var extension = Path.GetExtension(new Uri(url).LocalPath);
        if (string.IsNullOrEmpty(extension)) extension = ".png";
        var localPath = Path.Combine(directory, $"{filenameStem}{extension}");

        if (skipIfExists && File.Exists(localPath)) return localPath;

        try
        {
            var bytes = await _downloadHttp.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(localPath, bytes, ct);
            return localPath;
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            // Network hiccup / bad URL — skip this one image, don't take
            // down the whole fetch (or picker) pass.
            return null;
        }
    }

    /// <summary>
    /// v0.9.6: pedido do usuário ("ajustar a logo dos jogos, uns ficam
    /// pequenas em cima, outros em tamanho bom"). Causa: o LOGO é um PNG
    /// transparente da SteamGridDB (wordmark/título do jogo) e cada fonte
    /// exporta com uma margem transparente diferente em volta do desenho
    /// real — um logo "gordo" de borda a borda e outro minúsculo cercado
    /// de ar invisível. GameTileViewModel.CaptionLogoHeight escala todo
    /// PNG pra uma altura FIXA, então a margem invisível conta como
    /// "altura do logo" e empurra o desenho de verdade pra um tamanho bem
    /// menor do que os logos sem margem.
    ///
    /// Corrige cortando a imagem pro menor retângulo que ainda contém
    /// algum pixel não-transparente (bounding box do canal alpha), uma
    /// vez, no momento do download/aplicação — não em toda renderização.
    /// Só roda em PNG (o único formato com canal alpha que a SteamGridDB
    /// usa pra LOGO); best-effort, qualquer erro deixa o arquivo original
    /// intocado.
    /// </summary>
    private static void TrimTransparentPadding(string path)
    {
        const byte AlphaThreshold = 8; // ignora halo quase-transparente de anti-aliasing nas bordas

        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            using var original = new System.Drawing.Bitmap(path);

            var minX = original.Width;
            var minY = original.Height;
            var maxX = -1;
            var maxY = -1;

            for (var y = 0; y < original.Height; y++)
            {
                for (var x = 0; x < original.Width; x++)
                {
                    if (original.GetPixel(x, y).A <= AlphaThreshold) continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            // Imagem toda transparente (não devia acontecer, mas não é
            // motivo pra travar) ou já sem nenhuma margem pra cortar.
            if (maxX < minX || maxY < minY) return;
            if (minX == 0 && minY == 0 && maxX == original.Width - 1 && maxY == original.Height - 1) return;

            using var cropped = original.Clone(
                new System.Drawing.Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1),
                original.PixelFormat);
            cropped.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (Exception)
        {
            // PNG corrompido, sem suporte a GDI+ no ambiente, etc. — deixa
            // a imagem original (com a margem) em vez de travar o fetch.
        }
    }

    /// <summary>
    /// v0.9.23: era uma cópia CAMPO A CAMPO, e isso já apagou dados do
    /// usuário três vezes — todo campo novo que alguém esquecesse de
    /// acrescentar aqui sumia do jogo assim que qualquer busca de capa
    /// rodasse. Primeiro WideGridImagePath, depois IsFavorite, e por fim
    /// TitleOverride/IsApplication, este reportado pelo usuário ("eu edito
    /// o nome, mas mudo a imagem e ele volta pro nome original").
    ///
    /// <para>
    /// Agora é Clone() (MemberwiseClone), que copia tudo — inclusive campo
    /// que ainda nem existe. A lista de campos sumiu junto com a chance de
    /// esquecer algum. Ver GameMetadataOverride.Clone.
    /// </para>
    /// </summary>
    private GameMetadataOverride GetOrCreateOverride(string gameId)
    {
        var overrides = _store.LoadOverrides();
        return overrides.TryGetValue(gameId, out var existing)
            ? existing.Clone()
            : new GameMetadataOverride();
    }
}

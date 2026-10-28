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

    public Task<bool> ApplyGridCandidateAsync(GameEntry game, ArtworkCandidate candidate, CancellationToken ct = default)
        => ApplyCandidateAsync(game, candidate, isHero: false);

    public Task<bool> ApplyHeroCandidateAsync(GameEntry game, ArtworkCandidate candidate, CancellationToken ct = default)
        => ApplyCandidateAsync(game, candidate, isHero: true);

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
    /// that cached file into the game's grid/hero path; no second download
    /// needed.
    ///
    /// Filename includes the candidate's own SteamGridDB image Id (not
    /// just "{game.Id}_grid") specifically so this NEVER overwrites the
    /// file that's currently on screen — overwriting it in place risked a
    /// Windows file-lock conflict with whatever still had that exact file
    /// open for display, which is the likely cause of a user-reported "the
    /// first apply silently does nothing, the second one works" bug: a
    /// failed overwrite was caught and swallowed as a quiet "false", with
    /// nothing visibly different, until the lock cleared on a later try.
    /// Old grid/hero files from previous picks are left behind as harmless
    /// orphans in the same cache folder the README already tells the user
    /// they can clear anytime.
    /// </summary>
    private Task<bool> ApplyCandidateAsync(GameEntry game, ArtworkCandidate candidate, bool isHero)
    {
        Directory.CreateDirectory(_cacheDir);
        var extension = Path.GetExtension(candidate.LocalThumbnailPath);
        var suffix = isHero ? "hero" : "grid";
        var localPath = Path.Combine(_cacheDir, $"{game.Id}_{suffix}_{candidate.Id}{extension}");

        try
        {
            File.Copy(candidate.LocalThumbnailPath, localPath, overwrite: true);
        }
        catch (IOException)
        {
            return Task.FromResult(false);
        }

        var data = GetOrCreateOverride(game.Id);
        if (isHero) data.HeroImagePath = localPath; else data.GridImagePath = localPath;
        _store.SaveOverride(game.Id, data);

        if (isHero) game.HeroImagePath = localPath; else game.GridImagePath = localPath;
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

    private GameMetadataOverride GetOrCreateOverride(string gameId)
    {
        var overrides = _store.LoadOverrides();
        if (overrides.TryGetValue(gameId, out var existing))
        {
            return new GameMetadataOverride
            {
                Collection = existing.Collection,
                GridImagePath = existing.GridImagePath,
                HeroImagePath = existing.HeroImagePath,
                LogoImagePath = existing.LogoImagePath,
                IconImagePath = existing.IconImagePath,
                PlaytimeMinutes = existing.PlaytimeMinutes,
                LastPlayedUtc = existing.LastPlayedUtc,
            };
        }

        return new GameMetadataOverride();
    }
}

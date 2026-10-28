using System;
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

        Directory.CreateDirectory(_cacheDir);

        var extension = Path.GetExtension(new Uri(image.Url).LocalPath);
        if (string.IsNullOrEmpty(extension)) extension = ".png";
        var localPath = Path.Combine(_cacheDir, $"{game.Id}_grid{extension}");

        try
        {
            var bytes = await _downloadHttp.GetByteArrayAsync(image.Url, ct);
            await File.WriteAllBytesAsync(localPath, bytes, ct);
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            // Network hiccup / bad URL — skip this one game, don't take down
            // the whole fetch-all pass.
            return false;
        }

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

        Directory.CreateDirectory(_cacheDir);

        var extension = Path.GetExtension(new Uri(image.Url).LocalPath);
        if (string.IsNullOrEmpty(extension)) extension = ".png";
        var localPath = Path.Combine(_cacheDir, $"{game.Id}_hero{extension}");

        try
        {
            var bytes = await _downloadHttp.GetByteArrayAsync(image.Url, ct);
            await File.WriteAllBytesAsync(localPath, bytes, ct);
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            return false;
        }

        var data = GetOrCreateOverride(game.Id);
        data.HeroImagePath = localPath;
        _store.SaveOverride(game.Id, data);

        game.HeroImagePath = localPath;
        return true;
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

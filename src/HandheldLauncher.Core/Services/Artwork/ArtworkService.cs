using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HandheldLauncher.Core.Models;
using SkiaSharp;

namespace HandheldLauncher.Core.Services.Artwork;

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

    public async Task<IReadOnlyList<ArtworkCandidate>> GetGridCandidatesAsync(GameEntry game, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<ArtworkCandidate>();

        var images = game.Source == GameSource.Steam && game.SteamAppId is { } appId
            ? await _client.GetGridCandidatesForSteamAppAsync(appId, animatedOnly, ct)
            : await _client.GetGridCandidatesForTitleAsync(game.Title, animatedOnly, ct);

        return await DownloadCandidatesAsync(images, $"{game.Id}_grid_candidate", ct);
    }

    public async Task<IReadOnlyList<ArtworkCandidate>> GetHeroCandidatesAsync(GameEntry game, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<ArtworkCandidate>();

        var images = game.Source == GameSource.Steam && game.SteamAppId is { } appId
            ? await _client.GetHeroCandidatesForSteamAppAsync(appId, animatedOnly, ct)
            : await _client.GetHeroCandidatesForTitleAsync(game.Title, animatedOnly, ct);

        return await DownloadCandidatesAsync(images, $"{game.Id}_hero_candidate", ct);
    }

    public async Task<IReadOnlyList<ArtworkCandidate>> GetLogoCandidatesAsync(GameEntry game, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<ArtworkCandidate>();

        var images = game.Source == GameSource.Steam && game.SteamAppId is { } appId
            ? await _client.GetLogoCandidatesForSteamAppAsync(appId, animatedOnly, ct)
            : await _client.GetLogoCandidatesForTitleAsync(game.Title, animatedOnly, ct);

        return await DownloadCandidatesAsync(images, $"{game.Id}_logo_candidate", ct);
    }

    public async Task<IReadOnlyList<ArtworkCandidate>> GetIconCandidatesAsync(GameEntry game, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<ArtworkCandidate>();

        var images = game.Source == GameSource.Steam && game.SteamAppId is { } appId
            ? await _client.GetIconCandidatesForSteamAppAsync(appId, animatedOnly, ct)
            : await _client.GetIconCandidatesForTitleAsync(game.Title, animatedOnly, ct);

        return await DownloadCandidatesAsync(images, $"{game.Id}_icon_candidate", ct);
    }

    public async Task<IReadOnlyList<ArtworkCandidate>> GetWideGridCandidatesAsync(GameEntry game, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<ArtworkCandidate>();

        var images = game.Source == GameSource.Steam && game.SteamAppId is { } appId
            ? await _client.GetWideGridCandidatesForSteamAppAsync(appId, animatedOnly, ct)
            : await _client.GetWideGridCandidatesForTitleAsync(game.Title, animatedOnly, ct);

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

    private enum ArtworkSlot { Grid, Hero, Logo, WideGrid, Icon }

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
            return null;
        }
    }

    private static void TrimTransparentPadding(string path)
    {
        const byte AlphaThreshold = 8;

        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            var bytes = File.ReadAllBytes(path);
            using var original = SKBitmap.Decode(bytes);
            if (original is null) return;

            var minX = original.Width;
            var minY = original.Height;
            var maxX = -1;
            var maxY = -1;

            for (var y = 0; y < original.Height; y++)
            {
                for (var x = 0; x < original.Width; x++)
                {
                    if (original.GetPixel(x, y).Alpha <= AlphaThreshold) continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY) return;
            if (minX == 0 && minY == 0 && maxX == original.Width - 1 && maxY == original.Height - 1) return;

            var cropRect = new SKRectI(minX, minY, maxX + 1, maxY + 1);
            using var cropped = new SKBitmap(cropRect.Width, cropRect.Height);
            
            using (var canvas = new SKCanvas(cropped))
            {
                canvas.DrawBitmap(original, cropRect, new SKRect(0, 0, cropRect.Width, cropRect.Height));
            }

            using var image = SKImage.FromBitmap(cropped);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(path);
            data.SaveTo(stream);
        }
        catch (Exception)
        {
        }
    }

    private GameMetadataOverride GetOrCreateOverride(string gameId)
    {
        var overrides = _store.LoadOverrides();
        return overrides.TryGetValue(gameId, out var existing)
            ? existing.Clone()
            : new GameMetadataOverride();
    }
}
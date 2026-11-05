using System;
using System.Collections.Concurrent;
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
    private bool _selfHealRan;

    public ArtworkService(ISteamGridDbClient client, IGameLibraryStore store, string? cacheDirOverride = null)
    {
        _client = client;
        _store = store;
        _downloadHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _downloadHttp.DefaultRequestHeaders.UserAgent.ParseAdd(AppUserAgent.Value);
        _cacheDir = cacheDirOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HandheldLauncher", "artwork");
        Directory.CreateDirectory(_cacheDir);

        // 🟢 OTIMIZAÇÃO: Roda em background para não travar a abertura do app!
        _ = Task.Run(() => 
        {
            if (_selfHealRan) return;
            _selfHealRan = true;
            
            SelfHealCorruptedArtwork();
            SanitizeOverrides();
        });
    }

    public bool HasApiKey => _client.HasApiKey;

    public void ReloadSettings()
    {
        var settings = AppSettingsStore.Load();
        _client.UpdateApiKey(settings.SteamGridDbApiKey);
    }

    private void SelfHealCorruptedArtwork()
    {
        try
        {
            if (!Directory.Exists(_cacheDir)) return;

            var deletedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int scanned = 0, deleted = 0;

            foreach (var file in Directory.EnumerateFiles(_cacheDir, "*.*", SearchOption.AllDirectories))
            {
                scanned++;
                try
                {
                    var fi = new FileInfo(file);
                    if (fi.Length < 100) { File.Delete(file); deletedPaths.Add(file); deleted++; continue; }
                    
                    if (!IsValidImageMagic(file))
                    {
                        File.Delete(file);
                        deletedPaths.Add(file);
                        deleted++;
                    }
                }
                catch { }
            }

            var overridesCleaned = 0;
            if (deletedPaths.Count > 0)
            {
                var overrides = _store.LoadOverrides();
                foreach (var kv in overrides)
                {
                    var ov = kv.Value;
                    bool changed = false;
                    if (!string.IsNullOrEmpty(ov.GridImagePath) && deletedPaths.Contains(ov.GridImagePath)) { ov.GridImagePath = null; changed = true; }
                    if (!string.IsNullOrEmpty(ov.HeroImagePath) && deletedPaths.Contains(ov.HeroImagePath)) { ov.HeroImagePath = null; changed = true; }
                    if (!string.IsNullOrEmpty(ov.LogoImagePath) && deletedPaths.Contains(ov.LogoImagePath)) { ov.LogoImagePath = null; changed = true; }
                    if (!string.IsNullOrEmpty(ov.IconImagePath) && deletedPaths.Contains(ov.IconImagePath)) { ov.IconImagePath = null; changed = true; }
                    if (!string.IsNullOrEmpty(ov.WideGridImagePath) && deletedPaths.Contains(ov.WideGridImagePath)) { ov.WideGridImagePath = null; changed = true; }
                    if (changed) { _store.SaveOverride(kv.Key, ov); overridesCleaned++; }
                }
            }

            if (deleted > 0 || overridesCleaned > 0)
                System.Diagnostics.Debug.WriteLine($"[SGDB] SelfHeal Background: {deleted}/{scanned} arquivos removidos, {overridesCleaned} overrides limpos.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SGDB] SelfHeal EXCEPTION: {ex.Message}");
        }
    }

    private void SanitizeOverrides()
    {
        try
        {
            var overrides = _store.LoadOverrides();
            var cleaned = 0;

            foreach (var kv in overrides)
            {
                var ov = kv.Value;
                bool changed = false;

                if (!string.IsNullOrEmpty(ov.GridImagePath) && !File.Exists(ov.GridImagePath)) { ov.GridImagePath = null; changed = true; }
                if (!string.IsNullOrEmpty(ov.HeroImagePath) && !File.Exists(ov.HeroImagePath)) { ov.HeroImagePath = null; changed = true; }
                if (!string.IsNullOrEmpty(ov.LogoImagePath) && !File.Exists(ov.LogoImagePath)) { ov.LogoImagePath = null; changed = true; }
                if (!string.IsNullOrEmpty(ov.WideGridImagePath) && !File.Exists(ov.WideGridImagePath)) { ov.WideGridImagePath = null; changed = true; }
                if (!string.IsNullOrEmpty(ov.IconImagePath) && !File.Exists(ov.IconImagePath)) { ov.IconImagePath = null; changed = true; }

                if (changed) { _store.SaveOverride(kv.Key, ov); cleaned++; }
            }
            if (cleaned > 0)
                System.Diagnostics.Debug.WriteLine($"[SGDB] SanitizeOverrides Background: {cleaned} override(s) limpos.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SGDB] SanitizeOverrides EXCEPTION: {ex.Message}");
        }
    }

    private static bool IsValidImageMagic(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            if (fs.Length < 4) return false;
            var b = new byte[12];
            var read = fs.Read(b, 0, 12);
            if (read < 4) return false;
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return true; // PNG
            if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return true; // JPEG
            if (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46) return true; // GIF
            if (read >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return true; // WebP
            return false;
        }
        catch { return false; }
    }

    private static bool IsValidImageFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        try
        {
            if (new FileInfo(path).Length < 100) return false;
            return IsValidImageMagic(path);
        }
        catch { return false; }
    }

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
        if (!IsValidImageFile(localPath)) return false;
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

    public Task<IReadOnlyList<ArtworkCandidate>> GetGridCandidatesAsync(GameEntry game, bool animatedOnly = false, CancellationToken ct = default)
        => FetchCandidatesCoreAsync(game, animatedOnly, ct, async () =>
            game.Source == GameSource.Steam && game.SteamAppId is { } appId
                ? await _client.GetGridCandidatesForSteamAppAsync(appId, animatedOnly, ct)
                : await _client.GetGridCandidatesForTitleAsync(game.Title, animatedOnly, ct),
            $"{game.Id}_grid_candidate");

    public Task<IReadOnlyList<ArtworkCandidate>> GetHeroCandidatesAsync(GameEntry game, bool animatedOnly = false, CancellationToken ct = default)
        => FetchCandidatesCoreAsync(game, animatedOnly, ct, async () =>
            game.Source == GameSource.Steam && game.SteamAppId is { } appId
                ? await _client.GetHeroCandidatesForSteamAppAsync(appId, animatedOnly, ct)
                : await _client.GetHeroCandidatesForTitleAsync(game.Title, animatedOnly, ct),
            $"{game.Id}_hero_candidate");

    public Task<IReadOnlyList<ArtworkCandidate>> GetLogoCandidatesAsync(GameEntry game, bool animatedOnly = false, CancellationToken ct = default)
        => FetchCandidatesCoreAsync(game, animatedOnly, ct, async () =>
            game.Source == GameSource.Steam && game.SteamAppId is { } appId
                ? await _client.GetLogoCandidatesForSteamAppAsync(appId, animatedOnly, ct)
                : await _client.GetLogoCandidatesForTitleAsync(game.Title, animatedOnly, ct),
            $"{game.Id}_logo_candidate");

    public Task<IReadOnlyList<ArtworkCandidate>> GetIconCandidatesAsync(GameEntry game, bool animatedOnly = false, CancellationToken ct = default)
        => FetchCandidatesCoreAsync(game, animatedOnly, ct, async () =>
            game.Source == GameSource.Steam && game.SteamAppId is { } appId
                ? await _client.GetIconCandidatesForSteamAppAsync(appId, animatedOnly, ct)
                : await _client.GetIconCandidatesForTitleAsync(game.Title, animatedOnly, ct),
            $"{game.Id}_icon_candidate");

    public Task<IReadOnlyList<ArtworkCandidate>> GetWideGridCandidatesAsync(GameEntry game, bool animatedOnly = false, CancellationToken ct = default)
        => FetchCandidatesCoreAsync(game, animatedOnly, ct, async () =>
            game.Source == GameSource.Steam && game.SteamAppId is { } appId
                ? await _client.GetWideGridCandidatesForSteamAppAsync(appId, animatedOnly, ct)
                : await _client.GetWideGridCandidatesForTitleAsync(game.Title, animatedOnly, ct),
            $"{game.Id}_widegrid_candidate");

    public Task<IReadOnlyList<ArtworkCandidate>> GetWideArtworkCandidatesAsync(GameEntry game, bool animatedOnly = false, CancellationToken ct = default)
        => FetchCandidatesCoreAsync(game, animatedOnly, ct, async () =>
            game.Source == GameSource.Steam && game.SteamAppId is { } appId
                ? await _client.GetWideGridCandidatesForSteamAppAsync(appId, animatedOnly, ct)
                : await _client.GetWideGridCandidatesForTitleAsync(game.Title, animatedOnly, ct),
            $"{game.Id}_widegrid_candidate");

    private async Task<IReadOnlyList<ArtworkCandidate>> FetchCandidatesCoreAsync(
        GameEntry game, bool animatedOnly, CancellationToken ct,
        Func<Task<IReadOnlyList<SteamGridDbImage>>> fetchImages, string filenamePrefix)
    {
        if (!HasApiKey) return Array.Empty<ArtworkCandidate>();
        try
        {
            var images = await fetchImages();
            return await DownloadCandidatesAsync(images, filenamePrefix, ct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SGDB] Candidates '{game.Title}' EXCEPTION: {ex.Message}");
            return Array.Empty<ArtworkCandidate>();
        }
    }

    public Task<bool> ApplyGridCandidateAsync(GameEntry game, ArtworkCandidate candidate, CancellationToken ct = default)
        => ApplyCandidateAsync(game, candidate, ArtworkSlot.Grid, ct);
    public Task<bool> ApplyHeroCandidateAsync(GameEntry game, ArtworkCandidate candidate, CancellationToken ct = default)
        => ApplyCandidateAsync(game, candidate, ArtworkSlot.Hero, ct);
    public Task<bool> ApplyLogoCandidateAsync(GameEntry game, ArtworkCandidate candidate, CancellationToken ct = default)
        => ApplyCandidateAsync(game, candidate, ArtworkSlot.Logo, ct);
    public Task<bool> ApplyIconCandidateAsync(GameEntry game, ArtworkCandidate candidate, CancellationToken ct = default)
        => ApplyCandidateAsync(game, candidate, ArtworkSlot.Icon, ct);
    public Task<bool> ApplyWideGridCandidateAsync(GameEntry game, ArtworkCandidate candidate, CancellationToken ct = default)
        => ApplyCandidateAsync(game, candidate, ArtworkSlot.WideGrid, ct);

    private enum ArtworkSlot { Grid, Hero, Logo, WideGrid, Icon }

    private async Task<IReadOnlyList<ArtworkCandidate>> DownloadCandidatesAsync(
        IReadOnlyList<SteamGridDbImage> images, string filenamePrefix, CancellationToken ct)
    {
        var candidatesDir = Path.Combine(_cacheDir, "candidates");
        var results = new ConcurrentBag<ArtworkCandidate>();
        using var sem = new SemaphoreSlim(4);
        var tasks = new List<Task>();
        foreach (var image in images)
        {
            tasks.Add(Task.Run(async () =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    var localPath = await DownloadToCache(image.Url, candidatesDir, $"{filenamePrefix}_{image.Id}", ct, skipIfExists: true);
                    if (localPath is null) return;
                    results.Add(new ArtworkCandidate { Id = image.Id, RemoteUrl = image.Url, LocalThumbnailPath = localPath, Width = image.Width, Height = image.Height });
                }
                finally { sem.Release(); }
            }, ct));
        }
        await Task.WhenAll(tasks);
        return results.ToArray();
    }

    private Task<bool> ApplyCandidateAsync(GameEntry game, ArtworkCandidate candidate, ArtworkSlot slot, CancellationToken ct)
    {
        Directory.CreateDirectory(_cacheDir);
        var extension = Path.GetExtension(candidate.LocalThumbnailPath);
        var suffix = slot switch { ArtworkSlot.Hero => "hero", ArtworkSlot.Logo => "logo", ArtworkSlot.WideGrid => "widegrid", ArtworkSlot.Icon => "icon", _ => "grid" };
        var localPath = Path.Combine(_cacheDir, $"{game.Id}_{suffix}_{candidate.Id}{extension}");
        try { File.Copy(candidate.LocalThumbnailPath, localPath, overwrite: true); }
        catch (IOException) { return Task.FromResult(false); }

        var data = GetOrCreateOverride(game.Id);
        switch (slot)
        {
            case ArtworkSlot.Hero: data.HeroImagePath = localPath; game.HeroImagePath = localPath; break;
            case ArtworkSlot.Logo: data.LogoImagePath = localPath; game.LogoImagePath = localPath; break;
            case ArtworkSlot.WideGrid: data.WideGridImagePath = localPath; game.WideGridImagePath = localPath; break;
            case ArtworkSlot.Icon: data.IconImagePath = localPath; game.IconImagePath = localPath; break;
            default: data.GridImagePath = localPath; game.GridImagePath = localPath; break;
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

        if (skipIfExists && IsValidImageFile(localPath)) return localPath;

        try
        {
            using var response = await _downloadHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[SGDB] HTTP {(int)response.StatusCode} em {Path.GetFileName(url)}");
                return null;
            }

            var tempPath = localPath + ".tmp";
            try
            {
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    await response.Content.CopyToAsync(fs, ct);

                if (!IsValidImageMagic(tempPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[SGDB] Download inválido (não é imagem): {Path.GetFileName(url)}");
                    try { File.Delete(tempPath); } catch { }
                    return null;
                }

                if (File.Exists(localPath)) File.Delete(localPath);
                File.Move(tempPath, localPath);
                return localPath;
            }
            catch
            {
                if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { }
                throw;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SGDB] Download falhou {Path.GetFileName(url)}: {ex.GetType().Name} - {ex.Message}");
            return null;
        }
    }

    private GameMetadataOverride GetOrCreateOverride(string gameId)
    {
        var overrides = _store.LoadOverrides();
        return overrides.TryGetValue(gameId, out var existing) ? existing.Clone() : new GameMetadataOverride();
    }
}
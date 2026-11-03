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

    public ArtworkService(ISteamGridDbClient client, IGameLibraryStore store, string? cacheDirOverride = null)
    {
        _client = client;
        _store = store;
        _downloadHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _downloadHttp.DefaultRequestHeaders.UserAgent.ParseAdd("HandheldLauncher/15.6");
        _cacheDir = cacheDirOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HandheldLauncher", "artwork");
        Directory.CreateDirectory(_cacheDir);

        SelfHealCorruptedArtwork();
        SanitizeOverrides();
    }

    public bool HasApiKey => _client.HasApiKey;

    public void ReloadSettings()
    {
        var settings = AppSettingsStore.Load();
        _client.UpdateApiKey(settings.SteamGridDbApiKey);
    }

    /// <summary>
    /// Valida cada arquivo do cache com SKBitmap.Decode (full decode, não só
    /// magic bytes) — a forma que funciona no SkiaSharp atual: byte[] em vez
    /// de string path. Se o decode falhar, deleta o arquivo e limpa o
    /// override → HasLogo=false → nome do jogo volta a aparecer.
    /// </summary>
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

                    var bytes = File.ReadAllBytes(file);
                    using var bmp = SKBitmap.Decode(bytes);
                    if (bmp is null)
                    {
                        File.Delete(file);
                        deletedPaths.Add(file);
                        deleted++;
                    }
                }
                catch
                {
                    try { File.Delete(file); deletedPaths.Add(file); deleted++; } catch { }
                }
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
                System.Diagnostics.Debug.WriteLine($"[SGDB] SelfHeal: {deleted}/{scanned} arquivos removidos, {overridesCleaned} overrides limpos.");
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

                if (!string.IsNullOrEmpty(ov.GridImagePath) && !IsValidImageFile(ov.GridImagePath)) { ov.GridImagePath = null; changed = true; }
                if (!string.IsNullOrEmpty(ov.HeroImagePath) && !IsValidImageFile(ov.HeroImagePath)) { ov.HeroImagePath = null; changed = true; }
                if (!string.IsNullOrEmpty(ov.LogoImagePath) && !IsValidImageFile(ov.LogoImagePath)) { ov.LogoImagePath = null; changed = true; }
                if (!string.IsNullOrEmpty(ov.WideGridImagePath) && !IsValidImageFile(ov.WideGridImagePath)) { ov.WideGridImagePath = null; changed = true; }
                if (!string.IsNullOrEmpty(ov.IconImagePath) && !IsValidImageFile(ov.IconImagePath)) { ov.IconImagePath = null; changed = true; }

                if (changed) { _store.SaveOverride(kv.Key, ov); cleaned++; }
            }
            if (cleaned > 0)
                System.Diagnostics.Debug.WriteLine($"[SGDB] SanitizeOverrides: {cleaned} override(s) limpos.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SGDB] SanitizeOverrides EXCEPTION: {ex.Message}");
        }
    }

    /// <summary>
    /// Valida com SKBitmap.Decode(bytes) — full decode. Se o PNG tem header
    /// válido mas body corrompido (bug do trim antigo), retorna false aqui
    /// e o nome do jogo aparece em vez do logo quebrado.
    /// </summary>
    private static bool IsValidImageFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        try
        {
            if (new FileInfo(path).Length < 100) return false;
            var bytes = File.ReadAllBytes(path);
            using var bmp = SKBitmap.Decode(bytes);
            return bmp is not null;
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

    /// <summary>
    /// v15.6-R3: SEM TRIM. TrimTransparentPadding antigo corrompia PNGs com
    /// SKImage.Encode em alguns casos (header válido, body quebrado) →
    /// Avalonia new Bitmap falhava ao renderizar → tile vazia + HasLogo
    /// escondia o nome. Projeto antigo NÃO fazia trim e funcionava — voltamos
    /// ao mesmo fluxo.
    /// </summary>
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

    public async Task<IReadOnlyList<ArtworkCandidate>> GetWideArtworkCandidatesAsync(GameEntry game, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<ArtworkCandidate>();
        try
        {
            var gridsTask = game.Source == GameSource.Steam && game.SteamAppId is { } appId
                ? _client.GetWideGridCandidatesForSteamAppAsync(appId, animatedOnly, ct)
                : _client.GetWideGridCandidatesForTitleAsync(game.Title, animatedOnly, ct);
            var heroesTask = game.Source == GameSource.Steam && game.SteamAppId is { } steamAppId
                ? _client.GetHeroCandidatesForSteamAppAsync(steamAppId, animatedOnly, ct)
                : _client.GetHeroCandidatesForTitleAsync(game.Title, animatedOnly, ct);

            await Task.WhenAll(gridsTask, heroesTask);
            var grids = await DownloadCandidatesAsync(await gridsTask, $"{game.Id}_widegrid_candidate", ct);
            var heroes = await DownloadCandidatesAsync(await heroesTask, $"{game.Id}_hero_candidate", ct, isHero: true);
            return grids.Concat(heroes).ToArray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SGDB] Wide artwork '{game.Title}' EXCEPTION: {ex.Message}");
            return Array.Empty<ArtworkCandidate>();
        }
    }

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
        => ApplyCandidateAsync(game, candidate, candidate.IsHero ? ArtworkSlot.Hero : ArtworkSlot.WideGrid, ct);

    private enum ArtworkSlot { Grid, Hero, Logo, WideGrid, Icon }

    private async Task<IReadOnlyList<ArtworkCandidate>> DownloadCandidatesAsync(
        IReadOnlyList<SteamGridDbImage> images, string filenamePrefix, CancellationToken ct, bool isHero = false)
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
                    results.Add(new ArtworkCandidate { Id = image.Id, RemoteUrl = image.Url, LocalThumbnailPath = localPath, Width = image.Width, Height = image.Height, IsHero = isHero });
                }
                finally { sem.Release(); }
            }, ct));
        }
        await Task.WhenAll(tasks);
        return results.ToArray();
    }

    /// <summary>
    /// Não precisa de async: só faz File.Copy e SaveOverride (síncronos).
    /// Retorna Task.FromResult pra casar com a interface async.
    /// </summary>
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
            if (!response.IsSuccessStatusCode) return null;

            var tempPath = localPath + ".tmp";
            try
            {
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    await response.Content.CopyToAsync(fs, ct);

                if (!IsValidImageFile(tempPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[SGDB] Download inválido: {Path.GetFileName(url)}");
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
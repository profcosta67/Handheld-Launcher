using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.Services;
using HandheldLauncher.App.Views;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;
using HandheldLauncher.Core.Services.Artwork;
using Avalonia.Threading;

namespace HandheldLauncher.App.ViewModels;

/// <summary>v0.28: downloads, jogos da conta não instalados, capas e seletor de capa. Separado do MainViewModel.cs.</summary>
public sealed partial class MainViewModel
{
    private bool _isDownloadsOpen;
    public bool IsDownloadsOpen { get => _isDownloadsOpen; private set => SetField(ref _isDownloadsOpen, value); }

    public ObservableCollection<DownloadProgressInfo> ActiveDownloads { get; } = new();
    public bool HasActiveDownloads => ActiveDownloads.Count > 0;
    public bool NoActiveDownloads => !HasActiveDownloads;

    private DownloadProgressInfo? PrimaryActiveDownload => ActiveDownloads.FirstOrDefault(d => d.BytesDownloaded > 0 && d.BytesDownloaded < d.BytesToDownload) ?? ActiveDownloads.FirstOrDefault();

    public double TopBarDownloadPercent => PrimaryActiveDownload?.PercentComplete ?? 0;
    public string TopBarDownloadPercentText => PrimaryActiveDownload is { } d ? $"{d.PercentComplete:0}%" : string.Empty;

    public string TopBarDownloadToolTip
    {
        get
        {
            var tip = "Downloads (Y no controle)";
            if (PrimaryActiveDownload is not { } d) return tip;
            tip += $"\nBaixando: {d.Title} — {d.PercentComplete:0}%";
            var queued = ActiveDownloads.Count - 1; if (queued > 0) tip += $" (+{queued} na fila)";
            return tip;
        }
    }

    public ObservableCollection<OwnedSteamGame> OwnedNotInstalledGames { get; } = new();
    public bool HasSteamWebApiCredentials => _library.HasSteamWebApiCredentials;
    public bool NoSteamWebApiCredentials => !HasSteamWebApiCredentials;

    private bool _isLoadingOwnedGames;
    public bool IsLoadingOwnedGames { get => _isLoadingOwnedGames; private set => SetField(ref _isLoadingOwnedGames, value); }

    private string? _ownedGamesErrorMessage;
    public string? OwnedGamesErrorMessage { get => _ownedGamesErrorMessage; private set { if (SetField(ref _ownedGamesErrorMessage, value)) OnPropertyChanged(nameof(HasOwnedGamesError)); } }
    public bool HasOwnedGamesError => !string.IsNullOrEmpty(OwnedGamesErrorMessage);

    private string? _notInstalledTabMessage;
    public string? NotInstalledTabMessage { get => _notInstalledTabMessage; private set { if (SetField(ref _notInstalledTabMessage, value)) OnPropertyChanged(nameof(HasNotInstalledTabMessage)); } }
    public bool HasNotInstalledTabMessage => !string.IsNullOrEmpty(NotInstalledTabMessage);

    private void UpdateNotInstalledTabMessage()
    {
        if (_activeFilter == LibraryFilter.Cloud)
        {
            CloudFilterEmptyText = HasCloudFilter && Games.Count == 0 && _cloudGames.Count > 0
                ? "Nenhum jogo da nuvem com esse filtro. B limpa a busca e a letra."
                : "";
            if (HasCloudFilter) { NotInstalledTabMessage = null; return; }
            // Só o bloco fixo na tela = catálogo ainda não chegou.
            NotInstalledTabMessage = Games.Count > 1 ? null
                : _isLoadingCloudCatalog ? "Carregando o catálogo do Xbox Cloud Gaming..."
                : _cloudCatalogFailed ? "Não consegui baixar o catálogo de nuvem agora (sem internet?). O bloco \"Xbox Cloud Gaming\" abre o site normalmente."
                : null;
            return;
        }
        if (_activeFilter != LibraryFilter.NotInstalled) { NotInstalledTabMessage = null; return; }
        if (!HasSteamWebApiCredentials) NotInstalledTabMessage = "Configure a Steam Web API key e o SteamID64 na tela de Configurações pra ver aqui os jogos da sua conta que ainda não estão instalados.";
        else if (IsLoadingOwnedGames) NotInstalledTabMessage = "Carregando jogos da sua conta...";
        else if (HasOwnedGamesError) NotInstalledTabMessage = OwnedGamesErrorMessage;
        else if (Games.Count == 0) NotInstalledTabMessage = "Nenhum jogo da sua conta Steam está faltando instalar aqui.";
        else NotInstalledTabMessage = null;
    }

    public void ToggleDownloads() { if (IsDownloadsOpen) { CloseDownloads(); return; } RefreshActiveDownloads(); IsDownloadsOpen = true; }

    public void OpenSteamDownloads()
    {
        try
        {
            if (OperatingSystem.IsWindows()) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "steam://open/downloads", UseShellExecute = true });
            else if (OperatingSystem.IsLinux()) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "xdg-open", Arguments = "steam://open/downloads", UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
    }

    public void CloseDownloads() => IsDownloadsOpen = false;
    private readonly Dictionary<string, (long Bytes, DateTime At, double? Speed)> _downloadSamples = new();

    // v0.18.3 (pente fino de desempenho): a varredura lê TODOS os manifestos da
    // Steam (um arquivo por jogo instalado) e rodava na thread da tela a cada 6 s
    // com o launcher parado — outro engasgo periódico. Agora lê em segundo plano
    // e só aplica o resultado na tela; nunca duas varreduras ao mesmo tempo.
    public void RefreshActiveDownloads()
    {
        if (_downloadsScanInFlight) return;
        _downloadsScanInFlight = true;
        _ = RefreshActiveDownloadsCoreAsync();
    }

    private async Task RefreshActiveDownloadsCoreAsync()
    {
        IReadOnlyList<DownloadProgressInfo> downloads;
        try
        {
            downloads = await Task.Run(() =>
            {
                using var __meter = HandheldLauncher.Core.Services.Power.ActivityMeter.Measure("downloads");
                return _library.GetActiveDownloads();
            });
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); _downloadsScanInFlight = false; return; }
        _downloadsScanInFlight = false;

        var now = DateTime.UtcNow; ActiveDownloads.Clear();
        foreach (var download in downloads)
        {
            if (_downloadSamples.TryGetValue(download.Title, out var previous))
            {
                var seconds = (now - previous.At).TotalSeconds;
                var delta = download.BytesDownloaded - previous.Bytes;
                if (delta > 0 && seconds > 0.5)
                {
                    var speed = delta / seconds;
                    download.BytesPerSecond = speed;
                    _downloadSamples[download.Title] = (download.BytesDownloaded, now, speed);
                }
                else download.BytesPerSecond = (now - previous.At).TotalSeconds < 30 ? previous.Speed : null;
            }
            else _downloadSamples[download.Title] = (download.BytesDownloaded, now, null);
            ActiveDownloads.Add(download);
        }
        OnPropertyChanged(nameof(HasActiveDownloads)); OnPropertyChanged(nameof(NoActiveDownloads));
        OnPropertyChanged(nameof(TopBarDownloadPercent)); OnPropertyChanged(nameof(TopBarDownloadPercentText));
        OnPropertyChanged(nameof(TopBarDownloadToolTip));
    }

    public async Task RefreshOwnedGames()
    {
        if (!HasSteamWebApiCredentials) return;
        IsLoadingOwnedGames = true; OwnedGamesErrorMessage = null; UpdateNotInstalledTabMessage();
        try
        {
            var owned = await _library.GetOwnedButNotInstalledSteamGamesAsync();
            if (!IsDownloadsOpen && _activeFilter != LibraryFilter.NotInstalled) return;
            OwnedNotInstalledGames.Clear(); foreach (var game in owned) OwnedNotInstalledGames.Add(game);
            if (_activeFilter == LibraryFilter.NotInstalled) { RebuildGamesFromLibrary(); _ = FetchMissingArtworkForOwnedGames(); }
        }
        catch (Exception ex) { OwnedGamesErrorMessage = $"Erro ao buscar jogos da conta: {ex.Message}"; }
        finally { IsLoadingOwnedGames = false; UpdateNotInstalledTabMessage(); }
    }

    private void FetchMissingArtwork() => _ = FetchMissingArtworkCore(userInitiated: true);
    private void FetchMissingArtworkBackground() => _ = FetchMissingArtworkCore(userInitiated: false);

    private async Task FetchMissingArtworkCore(bool userInitiated)
    {
        if (!_artwork.HasApiKey)
        {
            DebugLog.Write("[SGDB] ABORT: sem API key → abrindo Settings");
            if (userInitiated) SettingsRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (userInitiated)
        {
            try { _ownedArtworkCts?.Cancel(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
            DebugLog.Write("[SGDB] Download manual aguardando a fila atual (até 60s).");
            if (!await _artworkSemaphore.WaitAsync(TimeSpan.FromSeconds(60)))
            {
                DebugLog.Write("[SGDB] TIMEOUT aguardando fila de artwork após 60s — comando manual não iniciado.");
                return;
            }
        }
        else
        {
            if (!await _artworkSemaphore.WaitAsync(TimeSpan.FromSeconds(60)))
            {
                DebugLog.Write("[SGDB] TIMEOUT background aguardando outro download — abortando com segurança");
                return;
            }
        }

        _ownedArtworkCts = new CancellationTokenSource();
        var ct = _ownedArtworkCts.Token;
        _artworkPauseGate.Set();
        IsFetchingArtwork = true;
        
        _failedAssets.Clear();

        ArtworkDownloadPopupWindow? modal = null;
        Task? dialogTask = null;
        if (userInitiated)
        {
            try
            {
                modal = new ArtworkDownloadPopupWindow();
                modal.Configure(0);
                modal.CancelRequested += (_, _) =>
                {
                    DebugLog.Write("[SGDB] Cancel solicitado pelo usuário");
                    try { _ownedArtworkCts?.Cancel(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
                    _artworkPauseGate.Set();
                };
                modal.PauseToggled += (_, _) =>
                {
                    if (_artworkPauseGate.IsSet) { _artworkPauseGate.Reset(); DebugLog.Write("[SGDB] Download PAUSADO."); }
                    else { _artworkPauseGate.Set(); DebugLog.Write("[SGDB] Download RETOMADO."); }
                };
                var owner = GetMainWindow();
                if (owner is not null) dialogTask = modal.ShowDialog(owner);
                else { modal.Show(); dialogTask = Task.CompletedTask; }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[SGDB] Falha ao abrir modal: {ex.Message}");
                modal = null; dialogTask = null;
            }
        }

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int ok = 0, skipped = 0;

            _library.Refresh();
            // v0.18.2 (pedido do usuário): a Nuvem NUNCA entra na busca automática do
            // SteamGridDB — as capas dela vêm do catálogo da Microsoft, em pasta
            // própria. Hoje as entradas da Nuvem nem estão em _library.Games; o filtro
            // é trava extra pra isso não mudar sem querer no futuro.
            var installed = _library.Games.Where(g => !IsCloudEntry(g)).ToList();

            List<GameEntry> ownedEntries = new();
            if (userInitiated && HasSteamWebApiCredentials)
            {
                try
                {
                    var owned = await _library.GetOwnedButNotInstalledSteamGamesAsync(ct);
                    OwnedNotInstalledGames.Clear();
                    foreach (var og in owned) OwnedNotInstalledGames.Add(og);
                    ownedEntries = owned.Select(_library.BuildEntryForOwnedGame).ToList();
                    DebugLog.Write($"[SGDB] Owned games incluídos na checagem: {ownedEntries.Count}");
                }
                catch (OperationCanceledException swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
                catch (Exception ex) { DebugLog.Write($"[SGDB] Falha ao carregar owned games: {ex.Message}"); }
            }

            static bool HasPendingWork(GameEntry g) =>
                NeedsDownload(g.GridImagePath) || NeedsDownload(g.HeroImagePath) ||
                NeedsDownload(g.LogoImagePath) || NeedsDownload(g.WideGridImagePath) ||
                NeedsDownload(g.IconImagePath);

            var installedPending = installed.Where(HasPendingWork).ToList();
            var ownedPending = ownedEntries.Where(HasPendingWork).ToList();

            var total = installedPending.Count + ownedPending.Count;
            if (total == 0)
            {
                DebugLog.Write("[SGDB] Nada a baixar — todas as capas já estão no cache.");
                modal?.UpdateProgress(0, 0, "Todas as capas já estão atualizadas");
                modal?.CloseForReal();
                if (dialogTask is not null) { try { await dialogTask; } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); } }
                IsFetchingArtwork = false;
                _artworkPauseGate.Set();
                _ownedArtworkCts?.Dispose();
                _ownedArtworkCts = null;
                try { _artworkSemaphore.Release(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
                DebugLog.Write("[SGDB] Lock de artwork liberado (nenhuma capa pendente).");
                return;
            }

            DebugLog.Write($"[SGDB] Diagnóstico: {installedPending.Count} instalados pendentes, {ownedPending.Count} owned pendentes.");
            foreach (var g in installedPending)
            {
                DebugLog.Write($"[SGDB] Pendente: {g.Title} | Grid:{NeedsDownload(g.GridImagePath)} Hero:{NeedsDownload(g.HeroImagePath)} Logo:{NeedsDownload(g.LogoImagePath)} Wide:{NeedsDownload(g.WideGridImagePath)} Icon:{NeedsDownload(g.IconImagePath)}");
            }

            if (modal != null) modal.Configure(total);
            DebugLog.Write($"[SGDB] === Atualização iniciada ({installedPending.Count} instalados + {ownedPending.Count} owned = {total} pendentes) === userInitiated={userInitiated}");

            int processed = 0;
            bool cancelledInPhase1 = false;

            foreach (var game in installedPending)
            {
                if (ct.IsCancellationRequested) { DebugLog.Write("[SGDB] Cancelado (fase 1)."); cancelledInPhase1 = true; break; }
                try { await Task.Run(() => _artworkPauseGate.Wait(ct), ct); }
                catch (OperationCanceledException) { cancelledInPhase1 = true; break; }

                processed++;
                if (modal != null) modal.UpdateProgress(processed, total, game.Title);

                bool anyWork = false;
                try
                {
                    if (NeedsDownload(game.GridImagePath) && !_failedAssets.Contains($"{game.Id}_grid")) { anyWork = true; if (await _artwork.FetchGridAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_grid"); }
                    if (ct.IsCancellationRequested) break;
                    if (NeedsDownload(game.HeroImagePath) && !_failedAssets.Contains($"{game.Id}_hero")) { anyWork = true; if (await _artwork.FetchHeroAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_hero"); }
                    if (ct.IsCancellationRequested) break;
                    if (NeedsDownload(game.LogoImagePath) && !_failedAssets.Contains($"{game.Id}_logo")) { anyWork = true; if (await _artwork.FetchLogoAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_logo"); }
                    if (ct.IsCancellationRequested) break;
                    if (NeedsDownload(game.WideGridImagePath) && !_failedAssets.Contains($"{game.Id}_widegrid")) { anyWork = true; if (await _artwork.FetchWideGridAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_widegrid"); }
                    if (ct.IsCancellationRequested) break;
                    if (NeedsDownload(game.IconImagePath) && !_failedAssets.Contains($"{game.Id}_icon")) { anyWork = true; if (await _artwork.FetchIconAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_icon"); }
                }
                catch (OperationCanceledException) { DebugLog.Write("[SGDB] Cancelado (exceção fase 1)."); cancelledInPhase1 = true; break; }
                catch (Exception ex) { DebugLog.Write($"[SGDB] '{game.Title}' EXCEPTION: {ex.Message}"); }

                if (!anyWork) { skipped++; continue; }
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { cancelledInPhase1 = true; break; }
            }

            if (!cancelledInPhase1 && !ct.IsCancellationRequested)
            {
                foreach (var game in ownedPending)
                {
                    if (ct.IsCancellationRequested) { DebugLog.Write("[SGDB] Cancelado (fase 2)."); break; }
                    try { await Task.Run(() => _artworkPauseGate.Wait(ct), ct); }
                    catch (OperationCanceledException) { break; }

                    processed++;
                    if (modal != null) modal.UpdateProgress(processed, total, game.Title);

                    bool anyWork = false;
                    try
                    {
                        if (NeedsDownload(game.GridImagePath) && !_failedAssets.Contains($"{game.Id}_grid")) { anyWork = true; if (await _artwork.FetchGridAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_grid"); }
                        if (ct.IsCancellationRequested) break;
                        if (NeedsDownload(game.HeroImagePath) && !_failedAssets.Contains($"{game.Id}_hero")) { anyWork = true; if (await _artwork.FetchHeroAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_hero"); }
                        if (ct.IsCancellationRequested) break;
                        if (NeedsDownload(game.LogoImagePath) && !_failedAssets.Contains($"{game.Id}_logo")) { anyWork = true; if (await _artwork.FetchLogoAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_logo"); }
                        if (ct.IsCancellationRequested) break;
                        if (NeedsDownload(game.WideGridImagePath) && !_failedAssets.Contains($"{game.Id}_widegrid")) { anyWork = true; if (await _artwork.FetchWideGridAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_widegrid"); }
                        if (ct.IsCancellationRequested) break;
                        if (NeedsDownload(game.IconImagePath) && !_failedAssets.Contains($"{game.Id}_icon")) { anyWork = true; if (await _artwork.FetchIconAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_icon"); }
                    }
                    catch (OperationCanceledException) { DebugLog.Write("[SGDB] Cancelado (exceção fase 2)."); break; }
                    catch (Exception ex) { DebugLog.Write($"[SGDB] '{game.Title}' owned EXCEPTION: {ex.Message}"); }

                    if (!anyWork) { skipped++; continue; }
                    try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
                }
            }

            DebugLog.Write($"[SGDB] === Concluído em {sw.Elapsed.TotalSeconds:F1}s === Processados={processed}/{total}, Sucessos={ok}, Skipped={skipped}");
            if (ok > 0) Refresh();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[SGDB] EXCEPTION FATAL: {ex.GetType().Name} - {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            IsFetchingArtwork = false;
            _artworkPauseGate.Set();
            _ownedArtworkCts?.Dispose();
            _ownedArtworkCts = null;
            try { _artworkSemaphore.Release(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
            DebugLog.Write("[SGDB] Lock de artwork liberado.");
            modal?.CloseForReal();
            if (dialogTask is not null) { try { await dialogTask; } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); } }
        }
    }

    private async Task FetchMissingArtworkForOwnedGames()
    {
        if (!_artwork.HasApiKey) return;
        if (_isFetchingOwnedArtwork) return;

        if (!await _artworkSemaphore.WaitAsync(0))
        {
            DebugLog.Write("[SGDB] ABORT owned: lock ocupado");
            return;
        }

        _isFetchingOwnedArtwork = true;
        _ownedArtworkCts = new CancellationTokenSource();
        var ct = _ownedArtworkCts.Token;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int downloaded = 0, total = 0;
        DebugLog.Write($"[SGDB] === Owned-not-installed iniciado ({OwnedNotInstalledGames.Count} jogos) ===");

        try
        {
            foreach (var owned in OwnedNotInstalledGames.ToList())
            {
                if (ct.IsCancellationRequested) break;
                if (_activeFilter != LibraryFilter.NotInstalled) return;

                total++;
                var entry = _library.BuildEntryForOwnedGame(owned);
                bool anyWork = false;
                try
                {
                    if (NeedsDownload(entry.GridImagePath)) { anyWork = true; if (await _artwork.FetchGridAsync(entry, ct)) downloaded++; }
                    if (ct.IsCancellationRequested) break;
                    if (NeedsDownload(entry.HeroImagePath)) { anyWork = true; if (await _artwork.FetchHeroAsync(entry, ct)) downloaded++; }
                    if (ct.IsCancellationRequested) break;
                    if (NeedsDownload(entry.LogoImagePath)) { anyWork = true; if (await _artwork.FetchLogoAsync(entry, ct)) downloaded++; }
                    if (ct.IsCancellationRequested) break;
                    if (NeedsDownload(entry.WideGridImagePath)) { anyWork = true; if (await _artwork.FetchWideGridAsync(entry, ct)) downloaded++; }
                    if (ct.IsCancellationRequested) break;
                    if (NeedsDownload(entry.IconImagePath)) { anyWork = true; if (await _artwork.FetchIconAsync(entry, ct)) downloaded++; }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { DebugLog.Write($"[SGDB] '{entry.Title}' owned EXCEPTION: {ex.Message}"); }

                if (anyWork) { try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; } }
            }

            DebugLog.Write($"[SGDB] === Owned concluído em {sw.Elapsed.TotalSeconds:F1}s (total={total}, baixados={downloaded}) ===");
            if (_activeFilter == LibraryFilter.NotInstalled) RebuildGamesFromLibrary();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[SGDB] Owned FATAL: {ex.GetType().Name} - {ex.Message}");
        }
        finally
        {
            _isFetchingOwnedArtwork = false;
            _ownedArtworkCts?.Dispose();
            _ownedArtworkCts = null;
            try { _artworkSemaphore.Release(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
            DebugLog.Write("[SGDB] Owned lock liberado.");
        }
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
            return d.MainWindow;
        return null;
    }

    public void InstallOwnedGame(OwnedSteamGame game) => _launcher.OpenSteamInstaller(game.AppId);

    private async Task OpenCoverPicker(GameEntry game, ArtworkPickerKind kind)
    {
        if (IsCloudEntry(game)) return; // v0.18.2: Nuvem fora do SteamGridDB
        CoverPickerTitle = kind switch { ArtworkPickerKind.WideGrid => $"Capas largas e banners · {game.Title}", ArtworkPickerKind.Hero => $"Banner · {game.Title}", ArtworkPickerKind.Logo => $"Logo · {game.Title}", ArtworkPickerKind.Icon => $"Ícone · {game.Title}", _ => $"Capa · {game.Title}", };
        _coverPickerKind = kind; _coverPickerGame = game; _coverPickerIndex = 0; CoverPickerErrorMessage = null; CoverPickerCandidates.Clear(); NotifyCoverPickerResultFlags(); IsCoverPickerLoading = true; IsCoverPickerOpen = true;
        try
        {
            var candidates = new List<ArtworkCandidate>();
            
            if (kind == ArtworkPickerKind.WideGrid)
            {
                var wides = await _artwork.GetWideGridCandidatesAsync(game, FilterAnimatedOnly);
                var heroes = await _artwork.GetHeroCandidatesAsync(game, FilterAnimatedOnly);
                candidates.AddRange(wides);
                candidates.AddRange(heroes);
            }
            else
            {
                candidates = (kind switch
                {
                    ArtworkPickerKind.Hero => await _artwork.GetHeroCandidatesAsync(game, FilterAnimatedOnly),
                    ArtworkPickerKind.Logo => await _artwork.GetLogoCandidatesAsync(game, FilterAnimatedOnly),
                    ArtworkPickerKind.Icon => await _artwork.GetIconCandidatesAsync(game, FilterAnimatedOnly),
                    _ => await _artwork.GetGridCandidatesAsync(game, FilterAnimatedOnly),
                }).ToList();
            }

            if (!IsCoverPickerOpen || _coverPickerGame != game) return;
            foreach (var candidate in candidates) CoverPickerCandidates.Add(new ArtworkCandidateViewModel(candidate));
            UpdateCoverPickerSelection();
        }
        catch (Exception ex) { CoverPickerErrorMessage = $"Erro ao buscar no SteamGridDB: {ex.Message}"; }
        finally { IsCoverPickerLoading = false; NotifyCoverPickerResultFlags(); }
    }

    public void CloseCoverPicker() { IsCoverPickerOpen = false; _coverPickerGame = null; CoverPickerCandidates.Clear(); CoverPickerErrorMessage = null; NotifyCoverPickerResultFlags(); }
    private void NotifyCoverPickerResultFlags() { OnPropertyChanged(nameof(CoverPickerHasResults)); OnPropertyChanged(nameof(CoverPickerIsEmpty)); }
    public const int CoverPickerColumns = 5;

    public void MoveCoverPickerSelection(int delta) { if (!IsCoverPickerOpen || CoverPickerCandidates.Count == 0) return; _coverPickerIndex = ((_coverPickerIndex + delta) % CoverPickerCandidates.Count + CoverPickerCandidates.Count) % CoverPickerCandidates.Count; UpdateCoverPickerSelection(); }
    public void MoveCoverPickerSelectionVertical(int rowDelta) { if (!IsCoverPickerOpen || CoverPickerCandidates.Count == 0) return; var newIndex = _coverPickerIndex + rowDelta * CoverPickerColumns; if (newIndex < 0 || newIndex >= CoverPickerCandidates.Count) return; _coverPickerIndex = newIndex; UpdateCoverPickerSelection(); }

    public async Task ConfirmCoverPickerSelection()
    {
        if (!IsCoverPickerOpen || _coverPickerGame is null || CoverPickerCandidates.Count == 0) { CloseCoverPicker(); return; }
        var chosen = CoverPickerCandidates[_coverPickerIndex].Candidate; var game = _coverPickerGame; var kind = _coverPickerKind; CloseCoverPicker();
        try { var applied = kind switch { ArtworkPickerKind.WideGrid => await _artwork.ApplyWideGridCandidateAsync(game, chosen), ArtworkPickerKind.Hero => await _artwork.ApplyHeroCandidateAsync(game, chosen), ArtworkPickerKind.Logo => await _artwork.ApplyLogoCandidateAsync(game, chosen), ArtworkPickerKind.Icon => await _artwork.ApplyIconCandidateAsync(game, chosen), _ => await _artwork.ApplyGridCandidateAsync(game, chosen), }; }
        catch (Exception ex) { DebugLog.Write($"[SGDB] Falha ao aplicar capa escolhida para '{game.Title}': {ex.Message}"); }
        Refresh();
    }

    private void UpdateCoverPickerSelection() { for (var i = 0; i < CoverPickerCandidates.Count; i++) CoverPickerCandidates[i].IsSelected = i == _coverPickerIndex; }
}

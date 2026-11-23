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

/// <summary>v0.28: aba Nuvem (Xbox Cloud Gaming) — catálogo, busca e letras. Separado do MainViewModel.cs.</summary>
public sealed partial class MainViewModel
{
    // ---- v0.18 aba "Nuvem" (Xbox Cloud Gaming) -------------------------------------
    // Entradas SINTÉTICAS (como as de "Não instalados"): nunca entram na
    // biblioteca nem no library.json — existem só enquanto a aba está aberta.
    private const string CloudIdPrefix = "xcloud_";
    private const string CloudHomeId = "xcloud_home";
    public static bool IsCloudEntry(GameEntry game) => game.Id.StartsWith(CloudIdPrefix, StringComparison.Ordinal);

    private IReadOnlyList<XCloudGame> _cloudGames = Array.Empty<XCloudGame>();
    private bool _cloudCatalogLoaded;
    private bool _isLoadingCloudCatalog;
    private bool _cloudCatalogFailed;
    private DateTimeOffset _cloudFetchedUtc; // v0.18.3 (revisão): fica em memória — não relê o catalog.json a cada entrada na aba

    private IEnumerable<GameEntry> BuildCloudEntries()
    {
        if (!_cloudCatalogLoaded)
        {
            _cloudGames = XCloudCatalog.LoadCached(out _cloudFetchedUtc);
            _cloudCatalogLoaded = true;
        }

        var query = NormalizeForSearch(_cloudSearchText);
        var filtering = query.Length > 0 || _cloudLetter is not null;

        // O bloco fixo só aparece sem filtro (com filtro, a lista é só de jogos).
        if (!filtering)
        {
            yield return new GameEntry
            {
                Id = CloudHomeId,
                Title = "Xbox Cloud Gaming",
                Source = GameSource.XboxApp,
                LaunchUri = XCloudCatalog.HomeUrl,
            };
        }
        foreach (var g in _cloudGames)
        {
            if (filtering)
            {
                var title = NormalizeForSearch(g.Title);
                if (query.Length > 0 && !title.Contains(query, StringComparison.Ordinal)) continue;
                if (_cloudLetter is not null && LetterOf(title) != _cloudLetter) continue;
            }
            yield return new GameEntry
            {
                Id = CloudIdPrefix + g.ProductId,
                Title = g.Title,
                Source = GameSource.XboxApp,
                LaunchUri = XCloudCatalog.GamePageUrl(g),
                GridImagePath = g.PosterPath,
            };
        }
    }

    // ---- v0.18.3: busca e filtro por letra (SÓ na aba Nuvem) ---------------------
    // Pedido do usuário: "o Xbox é muito jogo" — ~560 títulos. Busca por nome
    // sem acento/maiúscula ("pokemon" acha "Pokémon") + faixa # A–Z que FILTRA
    // (mostra só os jogos daquela letra). Os dois juntos valem ao mesmo tempo.
    private string _cloudSearchText = "";
    public string CloudSearchText
    {
        get => _cloudSearchText;
        set
        {
            if (!SetField(ref _cloudSearchText, value ?? "")) return;
            OnPropertyChanged(nameof(HasCloudFilter));
            // Espera parar de digitar (250 ms) antes de remontar a grade.
            _cloudSearchDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _cloudSearchDebounce.Stop();
            _cloudSearchDebounce.Tick -= OnCloudSearchDebounce;
            _cloudSearchDebounce.Tick += OnCloudSearchDebounce;
            _cloudSearchDebounce.Start();
        }
    }
    private DispatcherTimer? _cloudSearchDebounce;
    private void OnCloudSearchDebounce(object? sender, EventArgs e)
    {
        _cloudSearchDebounce?.Stop();
        if (_activeFilter == LibraryFilter.Cloud) RebuildGamesFromLibrary();
    }

    private string? _cloudLetter;
    public string CloudLetterText => _cloudLetter is null ? "" : $"Letra: {_cloudLetter}";
    public bool HasCloudFilter => _cloudSearchText.Trim().Length > 0 || _cloudLetter is not null;

    public ObservableCollection<CloudLetterViewModel> CloudLetters { get; } = BuildCloudLetters();
    private static ObservableCollection<CloudLetterViewModel> BuildCloudLetters()
    {
        var list = new ObservableCollection<CloudLetterViewModel> { new("Todos", null), new("#", "#") };
        for (var c = 'A'; c <= 'Z'; c++) list.Add(new CloudLetterViewModel(c.ToString(), c.ToString()));
        list[0].IsActive = true;
        return list;
    }

    private bool _isCloudLetterBarFocused;
    public bool IsCloudLetterBarFocused { get => _isCloudLetterBarFocused; private set => SetField(ref _isCloudLetterBarFocused, value); }
    private int _cloudLetterCursor;

    public void SetCloudLetter(string? letter)
    {
        _cloudLetter = letter;
        foreach (var l in CloudLetters) l.IsActive = l.Value == letter;
        OnPropertyChanged(nameof(HasCloudFilter));
        OnPropertyChanged(nameof(CloudLetterText));
        if (_activeFilter == LibraryFilter.Cloud) RebuildGamesFromLibrary();
    }

    public void ClearCloudFilters()
    {
        _cloudSearchDebounce?.Stop();
        SetField(ref _cloudSearchText, "", nameof(CloudSearchText));
        LeaveCloudLetterBar();
        SetCloudLetter(null);
    }

    /// <summary>X na aba Nuvem: entra/sai da faixa de letras.</summary>
    public void ToggleCloudLetterBar()
    {
        if (IsCloudLetterBarFocused) { LeaveCloudLetterBar(); return; }
        var active = CloudLetters.ToList().FindIndex(l => l.IsActive);
        _cloudLetterCursor = active < 0 ? 0 : active;
        UpdateCloudLetterCursor();
        IsCloudLetterBarFocused = true;
    }

    public void MoveCloudLetterCursor(int delta)
    {
        _cloudLetterCursor = Math.Clamp(_cloudLetterCursor + delta, 0, CloudLetters.Count - 1);
        UpdateCloudLetterCursor();
    }

    public void ConfirmCloudLetterCursor()
    {
        var chosen = CloudLetters[_cloudLetterCursor].Value;
        LeaveCloudLetterBar();
        SetCloudLetter(chosen);
    }

    public void LeaveCloudLetterBar()
    {
        IsCloudLetterBarFocused = false;
        foreach (var l in CloudLetters) l.IsCursor = false;
    }

    private void UpdateCloudLetterCursor()
    {
        for (var i = 0; i < CloudLetters.Count; i++) CloudLetters[i].IsCursor = i == _cloudLetterCursor;
    }

    private string _cloudFilterEmptyText = "";
    public string CloudFilterEmptyText { get => _cloudFilterEmptyText; private set { if (SetField(ref _cloudFilterEmptyText, value)) OnPropertyChanged(nameof(HasCloudFilterEmptyText)); } }
    public bool HasCloudFilterEmptyText => _cloudFilterEmptyText.Length > 0;

    /// <summary>Minúsculas, sem acento, espaços colapsados — "Pokémon  Legends" → "pokemon legends".</summary>
    private static string NormalizeForSearch(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var decomposed = text.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length);
        var lastSpace = false;
        foreach (var ch in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(ch)) { if (!lastSpace) sb.Append(' '); lastSpace = true; continue; }
            sb.Append(ch);
            lastSpace = false;
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    /// <summary>"A"…"Z" pela primeira letra/número do nome (pula aspas, colchetes etc.); número = "#".</summary>
    private static string? LetterOf(string normalizedTitle)
    {
        foreach (var ch in normalizedTitle)
        {
            if (char.IsDigit(ch)) return "#";
            if (ch is >= 'a' and <= 'z') return char.ToUpperInvariant(ch).ToString();
            if (char.IsLetter(ch)) return "#"; // letra fora de A–Z (ex.: japonês) fica junto do "#"
        }
        return null;
    }

    private DateTime _lastCloudOpenUtc = DateTime.MinValue;

    private void OpenCloudEntry(GameEntry game)
    {
        // v0.18.3 (revisão): A duas vezes enquanto o Edge ainda abre = duas janelas.
        if (DateTime.UtcNow - _lastCloudOpenUtc < TimeSpan.FromSeconds(4)) return;
        _lastCloudOpenUtc = DateTime.UtcNow;
        var url = string.IsNullOrWhiteSpace(game.LaunchUri) ? XCloudCatalog.HomeUrl : game.LaunchUri!;
        DebugLog.Write($"[XCLOUD] Abrindo '{game.Title}' → {url}");
        XCloudRequested?.Invoke(this, url);
    }

    private async Task RefreshCloudCatalogAsync()
    {
        if (_isLoadingCloudCatalog) return;
        if (_cloudGames.Count > 0 && !XCloudCatalog.IsStale(_cloudFetchedUtc))
        {
            await DownloadCloudPostersAsync();
            return;
        }

        _isLoadingCloudCatalog = true;
        _cloudCatalogFailed = false;
        UpdateNotInstalledTabMessage();
        try
        {
            var fresh = await Task.Run(() => XCloudCatalog.RefreshAsync());
            if (fresh is not null)
            {
                _cloudGames = fresh;
                _cloudCatalogLoaded = true;
                _cloudFetchedUtc = DateTimeOffset.UtcNow;
            }
            else
            {
                _cloudCatalogFailed = true;
            }
        }
        finally
        {
            _isLoadingCloudCatalog = false;
        }
        if (_activeFilter == LibraryFilter.Cloud) RebuildGamesFromLibrary();
        else UpdateNotInstalledTabMessage();
        await DownloadCloudPostersAsync();
    }

    private async Task DownloadCloudPostersAsync()
    {
        var games = _cloudGames;
        if (!games.Any(g => g.PosterPath is null && g.PosterUrl is not null)) return;
        var got = await Task.Run(() => XCloudCatalog.DownloadMissingPostersAsync(games));
        if (got > 0 && _activeFilter == LibraryFilter.Cloud && ReferenceEquals(games, _cloudGames)) RebuildGamesFromLibrary();
    }
}

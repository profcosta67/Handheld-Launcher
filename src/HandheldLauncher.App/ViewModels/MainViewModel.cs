using System.Collections.ObjectModel;
using System.Windows.Input;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;
using HandheldLauncher.Core.Services.Artwork;

namespace HandheldLauncher.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly ILibraryService _library;
    private readonly IGameLauncher _launcher;
    private readonly IArtworkService _artwork;

    public MainViewModel(ILibraryService library, IGameLauncher launcher, IArtworkService artwork)
    {
        _library = library;
        _launcher = launcher;
        _artwork = artwork;
        _launcher.GameExited += OnGameExited;

        Games = new ObservableCollection<GameTileViewModel>(
            _library.Games.Select(g => new GameTileViewModel(g)));
        ReorderLastPlayedFirst();

        MoveNextCommand = new RelayCommand(MoveNext, () => Games.Count > 1);
        MovePreviousCommand = new RelayCommand(MovePrevious, () => Games.Count > 1);
        LaunchSelectedCommand = new RelayCommand(LaunchSelected, () => SelectedTile is not null);
        CloseRunningGameCommand = new RelayCommand(CloseRunningGame, () => RunningGame is not null);
        RefreshCommand = new RelayCommand(Refresh);
        FetchArtworkCommand = new RelayCommand(FetchMissingArtwork, () => CanFetchArtwork);

        SelectedIndex = Games.Count > 0 ? 0 : -1;
    }

    public ObservableCollection<GameTileViewModel> Games { get; }

    private int _selectedIndex = -1;
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (Games.Count == 0)
            {
                SetField(ref _selectedIndex, -1);
                SelectedTile = null;
                return;
            }

            var clamped = Math.Clamp(value, 0, Games.Count - 1);
            if (SetField(ref _selectedIndex, clamped))
            {
                UpdateFocus();
                SelectedTile = Games[clamped];
            }
        }
    }

    private GameTileViewModel? _selectedTile;
    public GameTileViewModel? SelectedTile
    {
        get => _selectedTile;
        private set => SetField(ref _selectedTile, value);
    }

    /// <summary>
    /// The game the UI currently considers "running" — set when we launch,
    /// cleared on an explicit close or a detected exit. For Steam titles we
    /// have no automatic exit detection yet (see GameLauncher), so this can
    /// stay set even after the player has already quit the game; the
    /// "Fechar jogo" button/B-button is the explicit way back from that.
    /// </summary>
    private GameEntry? _runningGame;
    public GameEntry? RunningGame
    {
        get => _runningGame;
        private set => SetField(ref _runningGame, value);
    }

    private bool _isFetchingArtwork;
    public bool IsFetchingArtwork
    {
        get => _isFetchingArtwork;
        private set
        {
            if (SetField(ref _isFetchingArtwork, value))
            {
                OnPropertyChanged(nameof(CanFetchArtwork));
            }
        }
    }

    /// <summary>False with no explanation in the UI yet if there's no API key — the
    /// button's ToolTip in MainWindow.axaml is where that's spelled out.</summary>
    public bool CanFetchArtwork => _artwork.HasApiKey && !IsFetchingArtwork;

    private bool _isContextMenuOpen;

    /// <summary>
    /// Per-tile menu opened with the Start button. While this is true,
    /// MainWindow routes gamepad/keyboard up/down/confirm/back into the
    /// menu instead of the normal tile navigation — see OnGamepadTick.
    /// </summary>
    public bool IsContextMenuOpen
    {
        get => _isContextMenuOpen;
        private set => SetField(ref _isContextMenuOpen, value);
    }

    private GameTileViewModel? _contextMenuTarget;
    public GameTileViewModel? ContextMenuTarget
    {
        get => _contextMenuTarget;
        private set => SetField(ref _contextMenuTarget, value);
    }

    private int _contextMenuIndex;

    /// <summary>
    /// v1's fixed action list, in order (index matches
    /// ConfirmContextMenuSelection's switch). Small and fixed on purpose —
    /// grows into something data-driven if/when there are more than a
    /// couple of actions.
    /// </summary>
    public ObservableCollection<ContextMenuItemViewModel> ContextMenuItems { get; } = new()
    {
        new ContextMenuItemViewModel("Buscar capa no SteamGridDB"),
        new ContextMenuItemViewModel("Abrir jogo"),
    };

    private bool _isGlobalMenuOpen;

    /// <summary>
    /// The global actions menu (View/Back button) — this is what makes the
    /// bottom bar's buttons (Buscar capas, Adicionar jogo, Atualizar, Sair,
    /// Fechar jogo) reachable without a mouse. Independent of
    /// IsContextMenuOpen; MainWindow only lets one of the two be entered
    /// at a time (see OnGamepadTick).
    /// </summary>
    public bool IsGlobalMenuOpen
    {
        get => _isGlobalMenuOpen;
        private set => SetField(ref _isGlobalMenuOpen, value);
    }

    public ObservableCollection<ContextMenuItemViewModel> GlobalMenuItems { get; } = new();

    private readonly List<Action> _globalMenuActions = new();
    private int _globalMenuIndex;

    /// <summary>Raised when "Adicionar jogo" is picked from the global menu — MainWindow owns the dialog, not the ViewModel.</summary>
    public event EventHandler? AddGameRequested;

    /// <summary>Raised when "Sair" is picked from the global menu — MainWindow owns Close(), not the ViewModel.</summary>
    public event EventHandler? QuitRequested;

    public ICommand MoveNextCommand { get; }
    public ICommand MovePreviousCommand { get; }
    public ICommand LaunchSelectedCommand { get; }
    public ICommand CloseRunningGameCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand FetchArtworkCommand { get; }

    public void MoveNext() => SelectedIndex++;

    public void MovePrevious() => SelectedIndex--;

    public void LaunchSelected()
    {
        if (SelectedTile is null) return;
        var game = SelectedTile.Game;

        _launcher.Launch(game);
        RunningGame = game;

        // "Último jogo aberto" updates the instant we launch — not only
        // once the game exits (that's RecordPlaySession, in OnGameExited).
        // MarkLaunched mutates `game` in place (same instance), so the
        // tiles below pick it up as soon as we reorder/re-flag them.
        _library.MarkLaunched(game.Id);
        ReorderLastPlayedFirst();

        // Keep the cursor + top banner on the game that was just launched —
        // it's now first in the row and rendered wide, so this keeps focus
        // visually consistent with what the player just did.
        SelectedIndex = 0;
    }

    public void CloseRunningGame()
    {
        if (RunningGame is null) return;
        _launcher.CloseGame(RunningGame);
        RunningGame = null;
    }

    /// <summary>Start button: opens the menu for SelectedTile, or closes it if already open (toggle).</summary>
    public void ToggleContextMenu()
    {
        if (IsContextMenuOpen)
        {
            CloseContextMenu();
            return;
        }

        if (SelectedTile is null) return;

        ContextMenuTarget = SelectedTile;
        _contextMenuIndex = 0;
        UpdateContextMenuSelection();
        IsContextMenuOpen = true;
    }

    public void CloseContextMenu()
    {
        IsContextMenuOpen = false;
        ContextMenuTarget = null;
    }

    /// <summary>+1/-1 while the menu is open — wraps around, there are only ever a couple of items.</summary>
    public void MoveContextMenuSelection(int delta)
    {
        if (!IsContextMenuOpen || ContextMenuItems.Count == 0) return;
        _contextMenuIndex = ((_contextMenuIndex + delta) % ContextMenuItems.Count + ContextMenuItems.Count) % ContextMenuItems.Count;
        UpdateContextMenuSelection();
    }

    public void ConfirmContextMenuSelection()
    {
        if (!IsContextMenuOpen || ContextMenuTarget is null)
        {
            CloseContextMenu();
            return;
        }

        var game = ContextMenuTarget.Game;
        var selectedAction = _contextMenuIndex;
        CloseContextMenu();

        switch (selectedAction)
        {
            case 0: // Buscar capa no SteamGridDB
                _ = RefetchArtworkAsync(game);
                break;

            case 1: // Abrir jogo
                var tile = Games.FirstOrDefault(t => t.Game.Id == game.Id);
                if (tile is not null)
                {
                    SelectedIndex = Games.IndexOf(tile);
                    LaunchSelected();
                }
                break;
        }
    }

    private void UpdateContextMenuSelection()
    {
        for (var i = 0; i < ContextMenuItems.Count; i++)
        {
            ContextMenuItems[i].IsSelected = i == _contextMenuIndex;
        }
    }

    /// <summary>View/Back button: opens the global menu, or closes it if already open (toggle).</summary>
    public void ToggleGlobalMenu()
    {
        if (IsGlobalMenuOpen)
        {
            CloseGlobalMenu();
            return;
        }

        BuildGlobalMenu();
        if (GlobalMenuItems.Count == 0) return;

        _globalMenuIndex = 0;
        UpdateGlobalMenuSelection();
        IsGlobalMenuOpen = true;
    }

    public void CloseGlobalMenu()
    {
        IsGlobalMenuOpen = false;
    }

    public void MoveGlobalMenuSelection(int delta)
    {
        if (!IsGlobalMenuOpen || GlobalMenuItems.Count == 0) return;
        _globalMenuIndex = ((_globalMenuIndex + delta) % GlobalMenuItems.Count + GlobalMenuItems.Count) % GlobalMenuItems.Count;
        UpdateGlobalMenuSelection();
    }

    public void ConfirmGlobalMenuSelection()
    {
        if (!IsGlobalMenuOpen) return;

        var index = _globalMenuIndex;
        CloseGlobalMenu();

        if (index >= 0 && index < _globalMenuActions.Count)
        {
            _globalMenuActions[index]();
        }
    }

    private void UpdateGlobalMenuSelection()
    {
        for (var i = 0; i < GlobalMenuItems.Count; i++)
        {
            GlobalMenuItems[i].IsSelected = i == _globalMenuIndex;
        }
    }

    /// <summary>
    /// Rebuilt fresh every time the menu opens (not kept live while it's
    /// closed) — the set of available actions depends on state (is a game
    /// running? do we have an API key?) that's cheap enough to just
    /// re-check at open time rather than track reactively.
    /// </summary>
    private void BuildGlobalMenu()
    {
        GlobalMenuItems.Clear();
        _globalMenuActions.Clear();

        void Add(string label, Action action)
        {
            GlobalMenuItems.Add(new ContextMenuItemViewModel(label));
            _globalMenuActions.Add(action);
        }

        if (RunningGame is not null)
        {
            Add($"Fechar: {RunningGame.Title}", CloseRunningGame);
        }

        if (CanFetchArtwork)
        {
            Add("Buscar capas", FetchMissingArtwork);
        }

        Add("Adicionar jogo", () => AddGameRequested?.Invoke(this, EventArgs.Empty));
        Add("Atualizar", Refresh);
        Add("Sair", () => QuitRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// Forces a fresh grid+hero lookup for one game, overwriting whatever
    /// it already has. Note for whoever's reading this because a user
    /// asked "can I pick a different cover?": today this still grabs
    /// SteamGridDB's single top-scored match for both grid and hero, same
    /// as FetchMissingArtwork — it's a re-roll, not a browse-and-pick
    /// across candidates. A real picker needs ISteamGridDbClient to return
    /// a list of candidates instead of just the best one, plus UI to
    /// browse them; that's future work, not implemented here.
    /// </summary>
    private async Task RefetchArtworkAsync(GameEntry game)
    {
        if (!_artwork.HasApiKey) return;

        IsFetchingArtwork = true;
        try
        {
            await _artwork.FetchGridAsync(game);
            await _artwork.FetchHeroAsync(game);
            Refresh();
        }
        finally
        {
            IsFetchingArtwork = false;
        }
    }

    /// <summary>Called from MainWindow's "Adicionar jogo" dialog once the user fills it in and confirms.</summary>
    public void AddManualGame(GameEntry game)
    {
        _library.AddManualGame(game);
        Refresh();

        var tile = Games.FirstOrDefault(t => t.Game.Id == game.Id);
        if (tile is not null) SelectedIndex = Games.IndexOf(tile);
    }

    public void Refresh()
    {
        _library.Refresh();

        var previouslySelectedId = SelectedTile?.Game.Id;

        Games.Clear();
        foreach (var game in _library.Games)
        {
            Games.Add(new GameTileViewModel(game));
        }
        ReorderLastPlayedFirst();

        var restoredIndex = previouslySelectedId is null
            ? 0
            : Math.Max(0, Games.ToList().FindIndex(t => t.Game.Id == previouslySelectedId));

        SelectedIndex = Games.Count > 0 ? restoredIndex : -1;
    }

    /// <summary>
    /// Fetches covers AND hero banners for every game missing either, one
    /// game/image at a time (not parallel — gentler on the API and simpler
    /// to reason about without a real test run to tune concurrency
    /// against). Can take a while on a big library; the button disables
    /// itself meanwhile via CanFetchArtwork.
    /// </summary>
    private async void FetchMissingArtwork()
    {
        if (!CanFetchArtwork) return;
        IsFetchingArtwork = true;

        try
        {
            foreach (var tile in Games.ToList())
            {
                if (string.IsNullOrEmpty(tile.Game.GridImagePath))
                {
                    await _artwork.FetchGridAsync(tile.Game);
                }

                if (string.IsNullOrEmpty(tile.Game.HeroImagePath))
                {
                    await _artwork.FetchHeroAsync(tile.Game);
                }
            }

            Refresh();
        }
        finally
        {
            IsFetchingArtwork = false;
        }
    }

    private void UpdateFocus()
    {
        for (var i = 0; i < Games.Count; i++)
        {
            Games[i].IsFocused = i == _selectedIndex;
        }
    }

    /// <summary>
    /// Puts whichever game has the newest LastPlayedUtc first in the row
    /// and flags it IsLastPlayed (wide landscape tile); every other tile
    /// gets IsLastPlayed = false. Pure navigation (MoveNext/MovePrevious)
    /// never calls this — only a launch or a library reload does, so
    /// scrolling through the row never resizes anything.
    /// </summary>
    private void ReorderLastPlayedFirst()
    {
        if (Games.Count == 0) return;

        GameTileViewModel? lastPlayedTile = null;
        DateTimeOffset? latest = null;

        foreach (var tile in Games)
        {
            tile.IsLastPlayed = false;
            if (tile.Game.LastPlayedUtc is { } played && (latest is null || played > latest))
            {
                latest = played;
                lastPlayedTile = tile;
            }
        }

        if (lastPlayedTile is null) return;

        lastPlayedTile.IsLastPlayed = true;

        var index = Games.IndexOf(lastPlayedTile);
        if (index > 0)
        {
            Games.Move(index, 0);
        }
    }

    private void OnGameExited(object? sender, GameExitedEventArgs e)
    {
        _library.RecordPlaySession(e.Game.Id, e.Duration);
        if (RunningGame?.Id == e.Game.Id) RunningGame = null;
        Refresh();
    }
}

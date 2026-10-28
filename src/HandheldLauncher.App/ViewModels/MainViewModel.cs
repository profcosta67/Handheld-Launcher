using System.Collections.ObjectModel;
using System.Windows.Input;
using HandheldLauncher.App.Diagnostics;
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

        Games = new ObservableCollection<GameTileViewModel>();

        MoveNextCommand = new RelayCommand(MoveNext, () => Games.Count > 1);
        MovePreviousCommand = new RelayCommand(MovePrevious, () => Games.Count > 1);
        LaunchSelectedCommand = new RelayCommand(LaunchSelected, () => SelectedTile is not null);
        CloseRunningGameCommand = new RelayCommand(CloseRunningGame, () => RunningGame is not null);
        RefreshCommand = new RelayCommand(Refresh);
        FetchArtworkCommand = new RelayCommand(FetchMissingArtwork, () => CanFetchArtwork);

        // Abas de filtro — "Todos, favoritos, steam, não steam, xbox (por
        // enquanto)", pedido do usuário. Cada uma só troca o filtro ativo e
        // reconstrói Games (sem re-escanear a biblioteca) — ver SetFilter.
        SetFilterAllCommand = new RelayCommand(() => SetFilter(LibraryFilter.All));
        SetFilterFavoritesCommand = new RelayCommand(() => SetFilter(LibraryFilter.Favorites));
        SetFilterSteamCommand = new RelayCommand(() => SetFilter(LibraryFilter.Steam));
        SetFilterNonSteamCommand = new RelayCommand(() => SetFilter(LibraryFilter.NonSteam));
        SetFilterXboxCommand = new RelayCommand(() => SetFilter(LibraryFilter.Xbox));

        RebuildGamesFromLibrary();
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

    private string _batteryStatusText = string.Empty;

    /// <summary>
    /// Top bar's real battery readout ("⚡86%"/"🔌 86%"), pushed in from the
    /// outside by MainWindow.axaml.cs on a timer (Platform.BatteryStatus,
    /// a Win32 GetSystemPowerStatus call) — this ViewModel has no Windows
    /// API access of its own on purpose (see other Views-push-into-VM
    /// patterns like MinimizeRequested). Replaces the old hardcoded
    /// "⚡100%" placeholder text that used to just sit in the XAML — bug
    /// reported by the user: "a bateria não está acompanhando o real, ela
    /// está cravada a 100%". Empty string (nothing shown) on a desktop PC
    /// with no battery at all, or if Windows can't report it right now.
    /// </summary>
    public string BatteryStatusText
    {
        get => _batteryStatusText;
        set => SetField(ref _batteryStatusText, value);
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

    private bool _isLaunching;

    /// <summary>
    /// Feedback visual pedido pelo usuário ("feedback visual pra já kkk") —
    /// cobre o intervalo entre apertar confirmar e o jogo de fato aparecer
    /// na tela, que sem isso parecia "não fez nada" por alguns segundos.
    /// Não tem como MainViewModel saber com certeza quando o processo
    /// externo terminou de carregar (GameLauncher não expõe isso e cada
    /// jogo demora um tempo diferente), então o overlay só fica visível
    /// por um tempo fixo (ver LaunchFeedbackMilliseconds) e desaparece
    /// solo — não é "o jogo carregou", é "demos tempo suficiente pro
    /// player ver que o toque funcionou".
    /// </summary>
    public bool IsLaunching
    {
        get => _isLaunching;
        private set => SetField(ref _isLaunching, value);
    }

    private string _launchingGameTitle = string.Empty;
    public string LaunchingGameTitle
    {
        get => _launchingGameTitle;
        private set => SetField(ref _launchingGameTitle, value);
    }

    private const int LaunchFeedbackMilliseconds = 2500;

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
        new ContextMenuItemViewModel("Escolher capa"),
        new ContextMenuItemViewModel("Escolher capa larga"), // WideGridImagePath — a capa só da WIDE CAPSULE, independente da capa normal e do banner
        new ContextMenuItemViewModel("Escolher banner"),
        new ContextMenuItemViewModel("Escolher logo"),
        new ContextMenuItemViewModel("Favoritar"), // texto trocado em ToggleContextMenu dependendo do jogo em foco
        new ContextMenuItemViewModel("Abrir jogo"),
    };

    /// <summary>Qual dos quatro tipos de arte o seletor (IsCoverPickerOpen) está escolhendo no momento — ver OpenCoverPicker/ConfirmCoverPickerSelection.</summary>
    private enum ArtworkPickerKind { Grid, WideGrid, Hero, Logo }

    /// <summary>Abas de filtro pedidas pelo usuário: "Todos, favoritos, steam, não steam, xbox (por enquanto)".</summary>
    private enum LibraryFilter { All, Favorites, Steam, NonSteam, Xbox }

    /// <summary>
    /// Ordem VISUAL das abas (pedido do usuário: "fav, todos/steam/xbox/
    /// nao steam") — separada da ordem de declaração do enum acima de
    /// propósito, porque LB/RB (CyclePreviousFilter/CycleNextFilter) têm
    /// que andar na mesma ordem em que as abas aparecem na tela, não na
    /// ordem em que o enum foi escrito.
    /// </summary>
    private static readonly LibraryFilter[] FilterOrder =
    {
        LibraryFilter.Favorites,
        LibraryFilter.All,
        LibraryFilter.Steam,
        LibraryFilter.Xbox,
        LibraryFilter.NonSteam,
    };

    private LibraryFilter _activeFilter = LibraryFilter.All;

    // Um bool por aba em vez de expor o enum pro XAML — fica direto pra
    // bindar Classes.active="{Binding IsFilterXActive}" em cada botão da
    // fileira de abas (ver MainWindow.axaml).
    public bool IsFilterAllActive => _activeFilter == LibraryFilter.All;
    public bool IsFilterFavoritesActive => _activeFilter == LibraryFilter.Favorites;
    public bool IsFilterSteamActive => _activeFilter == LibraryFilter.Steam;
    public bool IsFilterNonSteamActive => _activeFilter == LibraryFilter.NonSteam;
    public bool IsFilterXboxActive => _activeFilter == LibraryFilter.Xbox;

    private bool _isGlobalMenuOpen;

    /// <summary>
    /// The global actions menu (View/Back button, or the gear icon in the
    /// top bar for mouse/touch — same menu either way, see
    /// MainWindow.OnGearMenuClick): Atualizar Biblioteca, Atualizar Capas
    /// Automático, Adicionar jogo, Minimizar, Power, and "Fechar:
    /// &lt;jogo&gt;" when one is running. "Minimizar"/"Power" are ALSO
    /// separate bottom-bar buttons (mouse/touch shortcuts) — they're
    /// repeated here specifically so the controller can reach them too:
    /// View/Back already reaches this menu from anywhere, so there's no
    /// need for a separate "navigate up/down to the on-screen icons"
    /// input model. "Sair" moved out of here into the Power menu (see
    /// BuildPowerMenu). Independent of IsContextMenuOpen; MainWindow only
    /// lets one of the three menus be entered at a time (see
    /// OnGamepadTick).
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

    /// <summary>Raised when "Sair" is picked (now from the Power menu, not the global menu — see BuildPowerMenu) — MainWindow owns Close(), not the ViewModel.</summary>
    public event EventHandler? QuitRequested;

    /// <summary>Raised when "Minimizar" is picked from the global menu — MainWindow owns Hide(), not the ViewModel. Same action as the bottom-bar "Minimizar" button (OnMinimizeClick); this just gives the controller a path to it too.</summary>
    public event EventHandler? MinimizeRequested;

    private bool _isPowerMenuOpen;

    /// <summary>
    /// The "Power" button's menu (Desligar tela / Reiniciar / Desligar o
    /// Legion Go / Sair) — same visual/overlay pattern as the global menu,
    /// just a separate independent flag so opening one never interferes
    /// with the other. MainWindow is the one place that checks which
    /// overlay (if any) owns gamepad/keyboard input right now.
    /// </summary>
    public bool IsPowerMenuOpen
    {
        get => _isPowerMenuOpen;
        private set => SetField(ref _isPowerMenuOpen, value);
    }

    public ObservableCollection<ContextMenuItemViewModel> PowerMenuItems { get; } = new();

    private readonly List<Action> _powerMenuActions = new();
    private int _powerMenuIndex;

    /// <summary>Raised when "Desligar tela" is picked — MainWindow owns the actual Win32 call, not the ViewModel.</summary>
    public event EventHandler? TurnOffScreenRequested;

    /// <summary>Raised when "Reiniciar o Legion Go" is picked — MainWindow owns the actual `shutdown /r` call, not the ViewModel.</summary>
    public event EventHandler? RestartDeviceRequested;

    /// <summary>Raised when "Desligar o Legion Go" is picked — MainWindow owns the actual `shutdown /s` call, not the ViewModel.</summary>
    public event EventHandler? ShutdownDeviceRequested;

    private bool _isCoverPickerOpen;

    /// <summary>
    /// Capa/banner/logo picker opened from the per-tile menu's "Escolher
    /// capa"/"Escolher banner"/"Escolher logo". Same "only one overlay
    /// routes input at a time" rule as the other two menus — see
    /// MainWindow.OnGamepadTick.
    /// </summary>
    public bool IsCoverPickerOpen
    {
        get => _isCoverPickerOpen;
        private set => SetField(ref _isCoverPickerOpen, value);
    }

    private string _coverPickerTitle = string.Empty;
    public string CoverPickerTitle
    {
        get => _coverPickerTitle;
        private set => SetField(ref _coverPickerTitle, value);
    }

    private bool _isCoverPickerLoading;

    /// <summary>True while GetGridCandidatesAsync/GetHeroCandidatesAsync/GetLogoCandidatesAsync is still fetching+downloading candidates from SteamGridDB.</summary>
    public bool IsCoverPickerLoading
    {
        get => _isCoverPickerLoading;
        private set
        {
            if (SetField(ref _isCoverPickerLoading, value))
            {
                OnPropertyChanged(nameof(CoverPickerIsEmpty));
            }
        }
    }

    public ObservableCollection<ArtworkCandidateViewModel> CoverPickerCandidates { get; } = new();

    /// <summary>True once loading has finished and at least one candidate came back — drives the grid's visibility.</summary>
    public bool CoverPickerHasResults => CoverPickerCandidates.Count > 0;

    /// <summary>True once loading has finished with zero candidates — drives the "nenhuma opção" message.</summary>
    public bool CoverPickerIsEmpty => !IsCoverPickerLoading && !CoverPickerHasError && CoverPickerCandidates.Count == 0;

    private string? _coverPickerErrorMessage;

    /// <summary>
    /// Set when GetGridCandidatesAsync/GetHeroCandidatesAsync throws — a
    /// network hiccup, SteamGridDB being down, whatever. Added after a user
    /// report of the picker seeming to do nothing at all when opened: an
    /// unhandled exception in an async void method can otherwise disappear
    /// silently (or, worse, crash the whole app) instead of ever reaching
    /// IsCoverPickerLoading = false. Showing SOMETHING in the overlay beats
    /// a bug that looks like "the button doesn't do anything."
    /// </summary>
    public string? CoverPickerErrorMessage
    {
        get => _coverPickerErrorMessage;
        private set
        {
            if (SetField(ref _coverPickerErrorMessage, value))
            {
                OnPropertyChanged(nameof(CoverPickerHasError));
                OnPropertyChanged(nameof(CoverPickerIsEmpty));
            }
        }
    }

    public bool CoverPickerHasError => !string.IsNullOrEmpty(CoverPickerErrorMessage);

    private int _coverPickerIndex;

    /// <summary>Read by MainWindow's scroll-into-view logic after every move.</summary>
    public int CoverPickerIndex => _coverPickerIndex;

    private ArtworkPickerKind _coverPickerKind;
    private GameEntry? _coverPickerGame;

    public ICommand MoveNextCommand { get; }
    public ICommand MovePreviousCommand { get; }
    public ICommand LaunchSelectedCommand { get; }
    public ICommand CloseRunningGameCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand FetchArtworkCommand { get; }

    public ICommand SetFilterAllCommand { get; }
    public ICommand SetFilterFavoritesCommand { get; }
    public ICommand SetFilterSteamCommand { get; }
    public ICommand SetFilterNonSteamCommand { get; }
    public ICommand SetFilterXboxCommand { get; }

    public void MoveNext() => SelectedIndex++;

    public void MovePrevious() => SelectedIndex--;

    public async void LaunchSelected()
    {
        if (SelectedTile is null) return;
        var game = SelectedTile.Game;

        // Overlay "Iniciando <jogo>..." entra ANTES de chamar _launcher —
        // é exatamente o intervalo "apertei e nada parece acontecer" que o
        // usuário queria coberto.
        LaunchingGameTitle = game.Title;
        IsLaunching = true;

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

        await Task.Delay(LaunchFeedbackMilliseconds);
        IsLaunching = false;
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

        // Índice 4 = "Favoritar"/"Desfavoritar" (ver ContextMenuItems e o
        // switch em ConfirmContextMenuSelection) — texto muda na hora de
        // abrir porque o mesmo item serve pra ligar e desligar.
        ContextMenuItems[4].Label = SelectedTile.Game.IsFavorite ? "Desfavoritar" : "Favoritar";

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

        DebugLog.Write($"ConfirmContextMenuSelection: index={selectedAction} game={game.Title}");

        switch (selectedAction)
        {
            case 0: // Escolher capa
                OpenCoverPicker(game, ArtworkPickerKind.Grid);
                break;

            case 1: // Escolher capa larga
                OpenCoverPicker(game, ArtworkPickerKind.WideGrid);
                break;

            case 2: // Escolher banner
                OpenCoverPicker(game, ArtworkPickerKind.Hero);
                break;

            case 3: // Escolher logo
                OpenCoverPicker(game, ArtworkPickerKind.Logo);
                break;

            case 4: // Favoritar / Desfavoritar
                _library.SetFavorite(game.Id, !game.IsFavorite);
                // RebuildGamesFromLibrary (não Refresh — não precisa
                // re-escanear) pra aba "Favoritos" refletir o toggle na
                // hora, caso esteja ativa.
                RebuildGamesFromLibrary();
                break;

            case 5: // Abrir jogo
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
            Add("Atualizar Capas Automático", FetchMissingArtwork);
        }

        Add("Adicionar jogo", () => AddGameRequested?.Invoke(this, EventArgs.Empty));
        Add("Atualizar Biblioteca", Refresh);

        // "Minimizar" e "Power" entram aqui também (além dos próprios
        // botões da barra de baixo) especificamente pra resolver o
        // controle não alcançar esses ícones apertando o direcional: como
        // esse menu já é 100% navegável por gamepad (View/Back), colocar
        // as duas ações aqui dá acesso total sem precisar inventar um novo
        // modelo de foco pra cima/baixo na tela. "Power" aqui fecha este
        // menu e abre o TogglePowerMenu (mesmo padrão de "overlay fecha o
        // anterior antes de abrir o próximo" já usado em
        // ConfirmGlobalMenuSelection). "Sair" NÃO fica mais aqui — mudou
        // pro menu Power (ver BuildPowerMenu) a pedido do usuário.
        Add("Minimizar", () => MinimizeRequested?.Invoke(this, EventArgs.Empty));
        Add("Power", TogglePowerMenu);
    }

    /// <summary>Botão "Power" da barra de baixo: abre o menu, ou fecha se já estiver aberto (toggle).</summary>
    public void TogglePowerMenu()
    {
        if (IsPowerMenuOpen)
        {
            ClosePowerMenu();
            return;
        }

        BuildPowerMenu();
        if (PowerMenuItems.Count == 0) return;

        _powerMenuIndex = 0;
        UpdatePowerMenuSelection();
        IsPowerMenuOpen = true;
    }

    public void ClosePowerMenu()
    {
        IsPowerMenuOpen = false;
    }

    public void MovePowerMenuSelection(int delta)
    {
        if (!IsPowerMenuOpen || PowerMenuItems.Count == 0) return;
        _powerMenuIndex = ((_powerMenuIndex + delta) % PowerMenuItems.Count + PowerMenuItems.Count) % PowerMenuItems.Count;
        UpdatePowerMenuSelection();
    }

    public void ConfirmPowerMenuSelection()
    {
        if (!IsPowerMenuOpen) return;

        var index = _powerMenuIndex;
        ClosePowerMenu();

        if (index >= 0 && index < _powerMenuActions.Count)
        {
            _powerMenuActions[index]();
        }
    }

    private void UpdatePowerMenuSelection()
    {
        for (var i = 0; i < PowerMenuItems.Count; i++)
        {
            PowerMenuItems[i].IsSelected = i == _powerMenuIndex;
        }
    }

    /// <summary>
    /// Fixed 3-item list — no state to check, unlike BuildGlobalMenu, so
    /// this could be built once in the constructor instead, but rebuilding
    /// on open keeps the same pattern as the other two menus for
    /// consistency.
    /// </summary>
    private void BuildPowerMenu()
    {
        PowerMenuItems.Clear();
        _powerMenuActions.Clear();

        void Add(string label, Action action)
        {
            PowerMenuItems.Add(new ContextMenuItemViewModel(label));
            _powerMenuActions.Add(action);
        }

        Add("Desligar tela", () => TurnOffScreenRequested?.Invoke(this, EventArgs.Empty));
        Add("Reiniciar o Legion Go", () => RestartDeviceRequested?.Invoke(this, EventArgs.Empty));
        Add("Desligar o Legion Go", () => ShutdownDeviceRequested?.Invoke(this, EventArgs.Empty));

        // Movido pra aqui a pedido do usuário (antes vivia no menu
        // geral/engrenagem) — faz mais sentido ao lado de
        // reiniciar/desligar o aparelho do que junto de
        // Atualizar Biblioteca/Adicionar jogo.
        Add("Sair", () => QuitRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// Opens the capa/banner/logo picker for one game: shows a loading
    /// state immediately, then fills in with whatever candidates come
    /// back. Static images only for now (the API call itself filters out
    /// animated GIF/webm, see SteamGridDbClient) — see README's "Seletor
    /// de capas" section for the animated-cover follow-up.
    /// </summary>
    private async void OpenCoverPicker(GameEntry game, ArtworkPickerKind kind)
    {
        DebugLog.Write($"OpenCoverPicker: ENTER game={game.Title} kind={kind}");

        CoverPickerTitle = kind switch
        {
            ArtworkPickerKind.WideGrid => $"Escolher capa larga — {game.Title}",
            ArtworkPickerKind.Hero => $"Escolher banner — {game.Title}",
            ArtworkPickerKind.Logo => $"Escolher logo — {game.Title}",
            _ => $"Escolher capa — {game.Title}",
        };
        _coverPickerKind = kind;
        _coverPickerGame = game;
        _coverPickerIndex = 0;
        CoverPickerErrorMessage = null;

        CoverPickerCandidates.Clear();
        NotifyCoverPickerResultFlags();

        IsCoverPickerLoading = true;
        IsCoverPickerOpen = true;

        DebugLog.Write($"OpenCoverPicker: IsCoverPickerOpen set to {IsCoverPickerOpen}, fetching candidates...");

        try
        {
            var candidates = kind switch
            {
                ArtworkPickerKind.WideGrid => await _artwork.GetWideGridCandidatesAsync(game),
                ArtworkPickerKind.Hero => await _artwork.GetHeroCandidatesAsync(game),
                ArtworkPickerKind.Logo => await _artwork.GetLogoCandidatesAsync(game),
                _ => await _artwork.GetGridCandidatesAsync(game),
            };

            DebugLog.Write($"OpenCoverPicker: fetch returned {candidates.Count} candidates");

            // The picker may have already been closed (player hit B while
            // this was in flight) — don't resurrect it with stale results.
            if (!IsCoverPickerOpen || _coverPickerGame != game)
            {
                DebugLog.Write("OpenCoverPicker: picker was closed/retargeted while fetching, discarding results");
                return;
            }

            foreach (var candidate in candidates)
            {
                CoverPickerCandidates.Add(new ArtworkCandidateViewModel(candidate));
            }

            UpdateCoverPickerSelection();
        }
        catch (Exception ex)
        {
            // Without this, a network hiccup / SteamGridDB being down /
            // anything else thrown here would just vanish — async void
            // swallows it (or, worse, it escapes as an unhandled exception
            // and can take the whole app down) — and from the player's
            // seat that looks exactly like "the button does nothing."
            DebugLog.Write($"OpenCoverPicker: EXCEPTION {ex}");
            CoverPickerErrorMessage = $"Erro ao buscar no SteamGridDB: {ex.Message}";
        }
        finally
        {
            IsCoverPickerLoading = false;
            NotifyCoverPickerResultFlags();
            DebugLog.Write($"OpenCoverPicker: EXIT candidates={CoverPickerCandidates.Count} error={CoverPickerErrorMessage} isOpen={IsCoverPickerOpen}");
        }
    }

    public void CloseCoverPicker()
    {
        DebugLog.Write($"CloseCoverPicker called (was open={IsCoverPickerOpen})");
        IsCoverPickerOpen = false;
        _coverPickerGame = null;
        CoverPickerCandidates.Clear();
        CoverPickerErrorMessage = null;
        NotifyCoverPickerResultFlags();
    }

    private void NotifyCoverPickerResultFlags()
    {
        OnPropertyChanged(nameof(CoverPickerHasResults));
        OnPropertyChanged(nameof(CoverPickerIsEmpty));
    }

    /// <summary>
    /// Must match MainWindow.axaml's UniformGrid Columns="5" on the
    /// picker's ItemsPanel exactly — that's a fixed column count (not a
    /// WrapPanel, whose column count depends on pixel widths at render
    /// time and the ViewModel has no reliable way to mirror), so ↑↓ below
    /// can jump by this many and land in the right cell every time. Public
    /// so MainWindow's manual scroll-into-view math (the ScrollViewer
    /// doesn't auto-follow a plain ItemsControl's "selected" item — there's
    /// no SelectedItem here, just an IsSelected flag per candidate) can use
    /// the same row math instead of a second hardcoded "5".
    /// </summary>
    public const int CoverPickerColumns = 5;

    /// <summary>Left/right: flat ±1, wrapping around the whole list (crossing a row edge lands in the adjacent row — expected).</summary>
    public void MoveCoverPickerSelection(int delta)
    {
        if (!IsCoverPickerOpen || CoverPickerCandidates.Count == 0) return;
        _coverPickerIndex = ((_coverPickerIndex + delta) % CoverPickerCandidates.Count + CoverPickerCandidates.Count) % CoverPickerCandidates.Count;
        UpdateCoverPickerSelection();
    }

    /// <summary>
    /// Up/down: jumps a full row (±CoverPickerColumns) instead of ±1, so it
    /// actually moves to the cell above/below instead of behaving like
    /// left/right (the bug reported: pressing down moved right, pressing
    /// up moved left). No wraparound here — jumping off the top or bottom
    /// edge just does nothing, since wrapping to the opposite edge of a
    /// 2D grid reads as "it teleported", not "it wrapped".
    /// </summary>
    public void MoveCoverPickerSelectionVertical(int rowDelta)
    {
        if (!IsCoverPickerOpen || CoverPickerCandidates.Count == 0) return;

        var newIndex = _coverPickerIndex + rowDelta * CoverPickerColumns;
        if (newIndex < 0 || newIndex >= CoverPickerCandidates.Count) return;

        _coverPickerIndex = newIndex;
        UpdateCoverPickerSelection();
    }

    public async void ConfirmCoverPickerSelection()
    {
        if (!IsCoverPickerOpen || _coverPickerGame is null || CoverPickerCandidates.Count == 0)
        {
            CloseCoverPicker();
            return;
        }

        var chosen = CoverPickerCandidates[_coverPickerIndex].Candidate;
        var game = _coverPickerGame;
        var kind = _coverPickerKind;
        CloseCoverPicker();

        DebugLog.Write($"ConfirmCoverPickerSelection: applying candidate id={chosen.Id} kind={kind} game={game.Title}");

        try
        {
            var applied = kind switch
            {
                ArtworkPickerKind.WideGrid => await _artwork.ApplyWideGridCandidateAsync(game, chosen),
                ArtworkPickerKind.Hero => await _artwork.ApplyHeroCandidateAsync(game, chosen),
                ArtworkPickerKind.Logo => await _artwork.ApplyLogoCandidateAsync(game, chosen),
                _ => await _artwork.ApplyGridCandidateAsync(game, chosen),
            };

            DebugLog.Write($"ConfirmCoverPickerSelection: apply returned {applied}");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"ConfirmCoverPickerSelection: EXCEPTION {ex}");
        }

        Refresh();
    }

    private void UpdateCoverPickerSelection()
    {
        for (var i = 0; i < CoverPickerCandidates.Count; i++)
        {
            CoverPickerCandidates[i].IsSelected = i == _coverPickerIndex;
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
        RebuildGamesFromLibrary();
    }

    /// <summary>
    /// LB/RB no controle (ver GamepadPoller.GamepadAction.PreviousFilter/
    /// NextFilter e MainWindow.OnGamepadTick) — pedido do usuário: "não
    /// consigo acessar os filtros... pelo controle". Anda pela MESMA
    /// ordem visual das abas na tela (FilterOrder), não pela ordem do
    /// enum. Aplica direto, sem cursor/confirmação separada — ciclar já
    /// É a ação, igual clicar numa aba.
    /// </summary>
    public void CycleNextFilter() => CycleFilter(1);

    public void CyclePreviousFilter() => CycleFilter(-1);

    private void CycleFilter(int delta)
    {
        var currentIndex = Array.IndexOf(FilterOrder, _activeFilter);
        if (currentIndex < 0) currentIndex = 0;
        var nextIndex = ((currentIndex + delta) % FilterOrder.Length + FilterOrder.Length) % FilterOrder.Length;
        SetFilter(FilterOrder[nextIndex]);
    }

    /// <summary>
    /// Troca a aba de filtro ativa e reconstrói Games sem re-escanear a
    /// biblioteca (_library.Refresh() não é chamado aqui) — trocar de aba
    /// é instantâneo, não precisa re-ler Steam/arquivos do disco de novo.
    /// </summary>
    private void SetFilter(LibraryFilter filter)
    {
        if (_activeFilter == filter) return;
        _activeFilter = filter;

        OnPropertyChanged(nameof(IsFilterAllActive));
        OnPropertyChanged(nameof(IsFilterFavoritesActive));
        OnPropertyChanged(nameof(IsFilterSteamActive));
        OnPropertyChanged(nameof(IsFilterNonSteamActive));
        OnPropertyChanged(nameof(IsFilterXboxActive));

        RebuildGamesFromLibrary();
    }

    private bool MatchesActiveFilter(GameEntry game) => _activeFilter switch
    {
        LibraryFilter.Favorites => game.IsFavorite,
        LibraryFilter.Steam => game.Source == GameSource.Steam,
        LibraryFilter.NonSteam => game.Source != GameSource.Steam,
        LibraryFilter.Xbox => game.Source == GameSource.XboxApp,
        _ => true,
    };

    /// <summary>
    /// A ÚNICA coisa que reconstrói a coleção Games exibida — tanto
    /// Refresh() (depois de re-escanear) quanto SetFilter() (só troca de
    /// aba) chamam isso. Filtra _library.Games (a lista CRUA, sem filtro)
    /// pelo filtro ativo, então tudo que já depende de índice em Games
    /// (SelectedIndex, MoveNext/MovePrevious, o scroll manual da fileira,
    /// ReorderLastPlayedFirst) continua funcionando sem mudança nenhuma —
    /// eles nunca sabem que existe filtro, só veem uma lista menor.
    /// </summary>
    private void RebuildGamesFromLibrary()
    {
        var previouslySelectedId = SelectedTile?.Game.Id;

        Games.Clear();
        foreach (var game in _library.Games)
        {
            if (MatchesActiveFilter(game))
            {
                Games.Add(new GameTileViewModel(game));
            }
        }
        ReorderLastPlayedFirst();

        var restoredIndex = previouslySelectedId is null
            ? 0
            : Math.Max(0, Games.ToList().FindIndex(t => t.Game.Id == previouslySelectedId));

        SelectedIndex = Games.Count > 0 ? restoredIndex : -1;

        // SelectedIndex's setter only touches SelectedTile when the NUMBER
        // actually changes — but this just replaced every GameTileViewModel
        // instance in Games, so an index that happens to land on the same
        // number as before (very common: the game you just edited art for
        // is usually still index 0, the "último jogo aberto") would
        // otherwise leave SelectedTile pointing at an orphaned pre-refresh
        // object with stale art. Force it to the fresh instance
        // unconditionally. This was the actual cause of a reported bug:
        // applying a new hero banner updated the small "último jogo
        // aberto" tile (always rebuilt fresh regardless) but not the big
        // top banner, which stayed on the old image until the player
        // navigated away and back — the only thing that ever changed
        // SelectedIndex's numeric value and forced the old code to refresh
        // it.
        if (Games.Count > 0) SelectedTile = Games[SelectedIndex];
    }

    /// <summary>
    /// Fetches covers, hero banners AND logos (all three, for EVERY game
    /// missing them — not just the "último jogo aberto" tile anymore, see
    /// the comment inside the loop) one game/image at a time (not parallel
    /// — gentler on the API and simpler to reason about without a real
    /// test run to tune concurrency against). Can take a while on a big
    /// library; the button disables itself meanwhile via CanFetchArtwork.
    /// </summary>
    private async void FetchMissingArtwork()
    {
        if (!CanFetchArtwork) return;
        IsFetchingArtwork = true;

        try
        {
            // _library.Games (lista CRUA) e não Games (a coleção filtrada
            // pela aba ativa) — agora que existem abas de filtro, iterar
            // Games faria "Atualizar Capas Automático" só completar a arte
            // de quem está visível na aba atual (ex: só os jogos Steam, se
            // a aba "Steam" estiver selecionada no momento), pulando o
            // resto da biblioteca silenciosamente. Esse botão sempre foi
            // "a biblioteca inteira" e continua sendo.
            foreach (var game in _library.Games.ToList())
            {
                if (string.IsNullOrEmpty(game.GridImagePath))
                {
                    await _artwork.FetchGridAsync(game);
                }

                if (string.IsNullOrEmpty(game.HeroImagePath))
                {
                    await _artwork.FetchHeroAsync(game);
                }

                // Logo (CURRENT LOGO): corrigido nesta leva — antes só
                // buscava pra tile do "último jogo aberto", porque o logo só
                // aparecia ali. Agora ele aparece ACIMA DE QUALQUER capa
                // (CAPSULE ou WIDE CAPSULE, no lugar do nome — ver
                // GameTileViewModel.ShowCurrentLogo), então busca pra toda a
                // biblioteca igual capa/banner já faziam.
                if (string.IsNullOrEmpty(game.LogoImagePath))
                {
                    await _artwork.FetchLogoAsync(game);
                }

                // Capa landscape (WIDE CAPSULE) — independente da capa
                // normal acima, novo campo desta leva (ver
                // GameEntry.WideGridImagePath). Busca pra biblioteca
                // inteira igual as outras três, não só pro "último jogo
                // aberto" — quem virar o último jogo aberto no futuro já
                // tem a arte pronta sem precisar buscar na hora.
                if (string.IsNullOrEmpty(game.WideGridImagePath))
                {
                    await _artwork.FetchWideGridAsync(game);
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

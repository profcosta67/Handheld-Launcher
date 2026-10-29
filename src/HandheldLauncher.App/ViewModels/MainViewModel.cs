using System.Collections.ObjectModel;
using System.Windows.Input;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Platform;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;
using HandheldLauncher.Core.Services.Artwork;

namespace HandheldLauncher.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly ILibraryService _library;
    private readonly IGameLauncher _launcher;
    private readonly IArtworkService _artwork;
    private readonly IPerformanceOverlayService _performanceOverlay;
    private readonly UpdateService? _updates;
    private readonly PerformanceHud? _hud;

    public MainViewModel(
        ILibraryService library,
        IGameLauncher launcher,
        IArtworkService artwork,
        IPerformanceOverlayService performanceOverlay,
        UpdateService? updates = null,
        PerformanceHud? hud = null)
    {
        _library = library;
        _launcher = launcher;
        _artwork = artwork;
        _performanceOverlay = performanceOverlay;
        _updates = updates;
        _hud = hud;
        _launcher.GameExited += OnGameExited;

        Games = new ObservableCollection<GameTileViewModel>();

        MoveNextCommand = new RelayCommand(MoveNext, () => Games.Count > 1);
        MovePreviousCommand = new RelayCommand(MovePrevious, () => Games.Count > 1);
        LaunchSelectedCommand = new RelayCommand(LaunchSelected, () => SelectedTile is not null);
        CloseRunningGameCommand = new RelayCommand(CloseRunningGame, () => RunningGame is not null);
        RefreshCommand = new RelayCommand(Refresh);
        FetchArtworkCommand = new RelayCommand(FetchMissingArtwork, () => CanFetchArtwork);

        // Abas de filtro — reorganizadas numa leva posterior a pedido do
        // usuário: "Favoritos, Todos, Oficiais, Não Steam, Não instalados"
        // (era Favoritos/Todos/Steam/Xbox/Não Steam). "Oficiais" junta
        // Steam+Xbox na mesma aba (decisão dele); "Não instalados" é novo,
        // puxa OwnedNotInstalledGames em vez de _library.Games — ver
        // RebuildGamesFromLibrary. Cada uma só troca o filtro ativo e
        // reconstrói Games (sem re-escanear a biblioteca) — ver SetFilter.
        SetFilterAllCommand = new RelayCommand(() => SetFilter(LibraryFilter.All));
        SetFilterFavoritesCommand = new RelayCommand(() => SetFilter(LibraryFilter.Favorites));
        SetFilterOfficialCommand = new RelayCommand(() => SetFilter(LibraryFilter.Official));
        SetFilterNonSteamCommand = new RelayCommand(() => SetFilter(LibraryFilter.NonSteam));
        SetFilterNotInstalledCommand = new RelayCommand(() => SetFilter(LibraryFilter.NotInstalled));

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

    // ------------------------------------------------------------------
    // Atualização automática (UpdateService / Velopack). Tudo aqui é
    // no-op quando _updates é null ou o app não foi instalado pelo
    // Setup.exe (ex: `dotnet run`, instalação antiga via .ps1).
    // ------------------------------------------------------------------

    private string _updateStatusText = string.Empty;

    /// <summary>
    /// Texto do aviso de atualização na barra de cima ("Verificando…",
    /// "⬆ v1.2.4 pronta", etc.). Vazio = nada aparece.
    /// </summary>
    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set
        {
            if (SetField(ref _updateStatusText, value))
            {
                OnPropertyChanged(nameof(HasUpdateStatus));
            }
        }
    }

    public bool HasUpdateStatus => !string.IsNullOrEmpty(_updateStatusText);

    /// <summary>Já baixou uma versão nova; só falta reiniciar.</summary>
    public bool IsUpdateReady => _updates?.IsUpdateReady ?? false;

    public string? PendingUpdateVersion => _updates?.PendingVersionText;

    /// <summary>
    /// "Handheld Launcher v0.3.4" no rodapé. Instalado pelo Setup.exe: versão
    /// do Velopack. Fora disso (dotnet run): versão do assembly, sem o
    /// sufixo "+hash do commit" que o .NET 8 anexa.
    /// </summary>
    public string AppVersionText
    {
        get
        {
            string version;
            if (_updates is { IsInstalled: true })
            {
                version = _updates.CurrentVersionText;
            }
            else
            {
                var info = System.Reflection.Assembly.GetExecutingAssembly()
                    .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                    .FirstOrDefault()?.InformationalVersion ?? "dev";
                var plus = info.IndexOf('+');
                version = plus >= 0 ? info[..plus] : info;
            }

            return $"Handheld Launcher v{version} · por profcosta67";
        }
    }

    /// <summary>
    /// Loop de verificação: espera ~20s (não competir com a abertura do
    /// launcher), checa, e depois repete a cada 6h (aparelho que fica dias
    /// ligado em suspensão). Chamado uma vez pelo MainWindow. Silencioso
    /// por padrão — só aparece algo na tela quando existe versão nova.
    /// </summary>
    public async Task RunUpdateLoopAsync()
    {
        if (_updates is not { IsInstalled: true }) return;

        await Task.Delay(TimeSpan.FromSeconds(20));
        while (true)
        {
            await CheckForUpdatesAsync(userInitiated: false);
            await Task.Delay(TimeSpan.FromHours(6));
        }
    }

    /// <summary>
    /// Verifica e baixa. <paramref name="userInitiated"/> = veio do item
    /// de menu "Verificar atualizações": nesse caso mostra o resultado
    /// ("Verificando…" / "Já está atualizado") por alguns segundos; no
    /// caso automático só aparece o aviso final, se houver versão nova.
    /// Nunca baixa enquanto um jogo está rodando (não disputar disco/rede
    /// com o jogo).
    /// </summary>
    public async Task CheckForUpdatesAsync(bool userInitiated)
    {
        if (_updates is not { IsInstalled: true }) return;
        if (IsUpdateReady) return;
        if (!userInitiated && RunningGame is not null) return;

        if (userInitiated) UpdateStatusText = "Verificando atualizações…";

        var version = await _updates.CheckAndDownloadAsync();

        if (version is not null)
        {
            UpdateStatusText = $"⬆ v{version} pronta — toque pra reiniciar";
            OnPropertyChanged(nameof(IsUpdateReady));
            OnPropertyChanged(nameof(PendingUpdateVersion));
        }
        else if (userInitiated)
        {
            UpdateStatusText = "Já está na versão mais recente (ou sem internet).";
            await Task.Delay(TimeSpan.FromSeconds(5));
            if (!IsUpdateReady) UpdateStatusText = string.Empty;
        }
    }

    /// <summary>Usado pela tela de Configurações: verifica e devolve uma frase com o resultado.</summary>
    public async Task<string> CheckForUpdatesWithResultAsync()
    {
        if (_updates is not { IsInstalled: true })
        {
            return "Atualização automática só funciona no launcher instalado pelo Setup.exe.";
        }

        await CheckForUpdatesAsync(userInitiated: true);
        return IsUpdateReady
            ? $"v{PendingUpdateVersion} baixada — feche as Configurações e toque em ⬆ na barra de cima (ou no menu) pra reiniciar."
            : "Você já está na versão mais recente (ou sem internet).";
    }

    public bool UpdatesInstalled => _updates is { IsInstalled: true };

    /// <summary>
    /// Reinicia pra aplicar a versão já baixada. Recusa se há um jogo
    /// rodando — trocar os arquivos do launcher não mata o jogo, mas
    /// reiniciar o launcher com um jogo aberto atrapalha o "Fechar jogo"
    /// e o retorno pro launcher; melhor esperar o jogo fechar.
    /// </summary>
    public void ApplyUpdate()
    {
        if (!IsUpdateReady) return;

        if (RunningGame is not null)
        {
            UpdateStatusText = $"⬆ v{PendingUpdateVersion} pronta — feche o jogo antes de reiniciar";
            return;
        }

        _hud?.ClearNow(); // senão o texto do HUD fica congelado na tela durante a troca
        HandheldLauncher.App.Input.GameInputHelper.StopHelperProcess(); // roda do mesmo exe — liberar os arquivos pra troca
        _updates?.ApplyAndRestart();
    }

    // ------------------------------------------------------------------
    // HUD de desempenho (Platform/PerformanceHud)
    // ------------------------------------------------------------------

    /// <summary>
    /// Desligado → Resumido → Médio → Completo → Desligado. Chamado pelo
    /// L3+R3 (até com o jogo aberto) e pelo item do menu geral. Salva a
    /// escolha no settings.json pra valer na próxima abertura também.
    /// </summary>
    private long _lastOverlayCycleTicks;

    public void CycleOverlayPreset()
    {
        if (_hud is null) return;

        // Pode vir de três lugares ao mesmo tempo (XInput, GameInput, atalho
        // de teclado) e de threads diferentes — um aperto = uma troca.
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastOverlayCycleTicks);
        if (now - last < 500 || Interlocked.CompareExchange(ref _lastOverlayCycleTicks, now, last) != last) return;

        _hud.Preset = (_hud.Preset + 1) % PerformanceHud.PresetNames.Length;

        try
        {
            var settings = AppSettingsStore.Load();
            settings.OverlayPreset = _hud.Preset;
            AppSettingsStore.Save(settings);
        }
        catch
        {
            // Melhor esforço — o preset já mudou na tela de qualquer jeito.
        }
    }

    /// <summary>Página HUD do menu: escolhe um preset direto (e salva).</summary>
    public void SetOverlayPreset(int preset)
    {
        if (_hud is null) return;
        _hud.Preset = preset;
        try
        {
            var settings = AppSettingsStore.Load();
            settings.OverlayPreset = _hud.Preset;
            AppSettingsStore.Save(settings);
        }
        catch
        {
            // Melhor esforço.
        }
    }

    /// <summary>Depois de salvar nas Configurações: aplica o preset na hora, sem reiniciar.</summary>
    public void ReloadOverlayPresetFromSettings()
    {
        if (_hud is null) return;
        try
        {
            _hud.Preset = AppSettingsStore.Load().OverlayPreset;
        }
        catch
        {
            // Melhor esforço.
        }
    }

    /// <summary>Tira o HUD da tela (Sair/fechar), pra não ficar texto congelado no RTSS.</summary>
    public void ClearOverlayNow() => _hud?.ClearNow();

    public string OverlayStatusText => _hud is null ? "indisponível" : $"{_hud.PresetName} ({_hud.Status})";

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

    private string? _launchingGameIconPath;

    /// <summary>
    /// Ícone do SteamGridDB (seção Icons) mostrado em cima do "Iniciando
    /// &lt;jogo&gt;..." — pedido do usuário. Null = sem ícone ainda (some
    /// da tela, fica só o texto).
    /// </summary>
    public string? LaunchingGameIconPath
    {
        get => _launchingGameIconPath;
        private set => SetField(ref _launchingGameIconPath, value);
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
    /// Reconstruído a cada abertura (ver BuildContextMenu) em vez de uma
    /// lista fixa — mesmo padrão de GlobalMenuItems/PowerMenuItems (List
    /// &lt;Action&gt; + ObservableCollection em paralelo, zerados e
    /// reconstruídos do zero toda vez que o menu abre). Virou necessário
    /// nesta leva: "Excluir atalho" só faz sentido pra jogos não-Steam e
    /// "Excluir jogo" só pra Steam, então o conjunto de itens agora
    /// depende de QUAL jogo está em foco — uma lista fixa com índices
    /// não dava mais pra sustentar isso (já tinha precisado reindexar à
    /// mão 3 vezes nesta sessão só pra inserir itens novos).
    /// </summary>
    public ObservableCollection<ContextMenuItemViewModel> ContextMenuItems { get; } = new();

    private readonly List<Action> _contextMenuActions = new();

    /// <summary>Qual dos quatro tipos de arte o seletor (IsCoverPickerOpen) está escolhendo no momento — ver OpenCoverPicker/ConfirmCoverPickerSelection.</summary>
    private enum ArtworkPickerKind { Grid, WideGrid, Hero, Logo, Icon }

    /// <summary>
    /// Abas de filtro. Reorganizado numa leva posterior a pedido do
    /// usuário ("dividir as subabas, fav, ALL, oficiais, não Steam, não
    /// instalados"): Steam e Xbox viraram uma aba só (Official), e
    /// NotInstalled é nova — não filtra _library.Games como as outras,
    /// puxa de OwnedNotInstalledGames (ver RebuildGamesFromLibrary).
    /// </summary>
    private enum LibraryFilter { All, Favorites, Official, NonSteam, NotInstalled }

    /// <summary>
    /// Ordem VISUAL das abas (pedido do usuário: "fav, ALL, oficiais, não
    /// Steam, não instalados") — separada da ordem de declaração do enum
    /// acima de propósito, porque LB/RB (CyclePreviousFilter/
    /// CycleNextFilter) têm que andar na mesma ordem em que as abas
    /// aparecem na tela, não na ordem em que o enum foi escrito.
    /// </summary>
    private static readonly LibraryFilter[] FilterOrder =
    {
        LibraryFilter.Favorites,
        LibraryFilter.All,
        LibraryFilter.Official,
        LibraryFilter.NonSteam,
        LibraryFilter.NotInstalled,
    };

    private LibraryFilter _activeFilter = LibraryFilter.All;

    // Um bool por aba em vez de expor o enum pro XAML — fica direto pra
    // bindar Classes.active="{Binding IsFilterXActive}" em cada botão da
    // fileira de abas (ver MainWindow.axaml).
    public bool IsFilterAllActive => _activeFilter == LibraryFilter.All;
    public bool IsFilterFavoritesActive => _activeFilter == LibraryFilter.Favorites;
    public bool IsFilterOfficialActive => _activeFilter == LibraryFilter.Official;
    public bool IsFilterNonSteamActive => _activeFilter == LibraryFilter.NonSteam;
    public bool IsFilterNotInstalledActive => _activeFilter == LibraryFilter.NotInstalled;

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

    /// <summary>
    /// Raised when "Configurações" é escolhido no menu global — pedido do
    /// usuário depois de se perder tentando achar onde colar a Steam Web
    /// API key ("seria possível criar uma tela de configuração pelo
    /// próprio Launcher?"). Mesmo padrão do AddGameRequested acima:
    /// MainWindow é quem possui o diálogo (SettingsWindow) e o acesso a
    /// AppSettingsStore, não a ViewModel.
    /// </summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised when "Sair" is picked (now from the Power menu, not the global menu — see BuildPowerMenu) — MainWindow owns Close(), not the ViewModel.</summary>
    public event EventHandler? QuitRequested;

    /// <summary>Raised when "Minimizar" is picked from the global menu — MainWindow owns Hide(), not the ViewModel. Same action as the bottom-bar "Minimizar" button (OnMinimizeClick); this just gives the controller a path to it too.</summary>
    public event EventHandler? MinimizeRequested;

    private bool _isPowerMenuOpen;

    /// <summary>
    /// The "Power" button's menu (Desligar tela / Reiniciar o aparelho /
    /// Desligar o aparelho / Sair) — same visual/overlay pattern as the global menu,
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

    /// <summary>Raised when "Reiniciar o aparelho" is picked — MainWindow owns the actual `shutdown /r` call, not the ViewModel.</summary>
    public event EventHandler? RestartDeviceRequested;

    /// <summary>Raised when "Desligar o aparelho" is picked — MainWindow owns the actual `shutdown /s` call, not the ViewModel.</summary>
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
    public ICommand SetFilterOfficialCommand { get; }
    public ICommand SetFilterNonSteamCommand { get; }
    public ICommand SetFilterNotInstalledCommand { get; }

    public void MoveNext() => SelectedIndex++;

    public void MovePrevious() => SelectedIndex--;

    public async void LaunchSelected()
    {
        if (SelectedTile is null) return;
        var game = SelectedTile.Game;

        // Tile da aba "Não instalados" — confirmar/dar duplo clique
        // instala em vez de tentar abrir (não tem ExecutablePath nenhum
        // pra lançar, o jogo nem está instalado ainda). Mesmo destino do
        // item "Instalar" do menu de contexto dessas tiles (ver
        // BuildContextMenu) e do botão "Instalar" do painel de Downloads
        // (InstallOwnedGame) — sempre steam://install/{appid}, nunca
        // baixa nada diretamente.
        if (game.IsOwnedNotInstalled)
        {
            if (game.SteamAppId is { } ownedAppId) _launcher.OpenSteamInstaller(ownedAppId);
            return;
        }

        // Overlay "Iniciando <jogo>..." entra ANTES de chamar _launcher —
        // é exatamente o intervalo "apertei e nada parece acontecer" que o
        // usuário queria coberto.
        LaunchingGameTitle = game.Title;
        LaunchingGameIconPath = game.IconImagePath;
        IsLaunching = true;

        // Sem ícone ainda: busca agora em segundo plano. Se chegar enquanto a
        // tela "Iniciando" ainda está aberta, aparece na hora; senão fica
        // salvo pra próxima vez.
        if (string.IsNullOrEmpty(game.IconImagePath) && _artwork.HasApiKey)
        {
            _ = FetchLaunchIconAsync(game);
        }

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

    private async Task FetchLaunchIconAsync(GameEntry game)
    {
        try
        {
            if (await _artwork.FetchIconAsync(game) && IsLaunching && LaunchingGameTitle == game.Title)
            {
                LaunchingGameIconPath = game.IconImagePath;
            }
        }
        catch
        {
            // Melhor esforço — sem ícone a tela continua só com o texto.
        }
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

        BuildContextMenu(SelectedTile);

        UpdateContextMenuSelection();
        IsContextMenuOpen = true;
    }

    /// <summary>
    /// Reconstrói ContextMenuItems/_contextMenuActions do zero pra tile em
    /// foco — mesma ideia de BuildGlobalMenu/BuildPowerMenu abaixo.
    /// "Favoritar"/"Desfavoritar" sai automaticamente certo (sem precisar
    /// mais do hack de reescrever Label depois) porque isso reconstrói na
    /// hora de abrir, já olhando pro estado atual do jogo.
    /// </summary>
    private void BuildContextMenu(GameTileViewModel target)
    {
        ContextMenuItems.Clear();
        _contextMenuActions.Clear();

        void Add(string label, Action action)
        {
            ContextMenuItems.Add(new ContextMenuItemViewModel(label));
            _contextMenuActions.Add(action);
        }

        var game = target.Game;

        // Tile da aba "Não instalados" — menu de UM item só, nada de
        // Favoritar/Excluir/Personalizar fazem sentido pra um jogo que
        // nem está instalado ainda (ver GameEntry.IsOwnedNotInstalled).
        // "Abrir jogo" aqui mesmo instala (OpenGameFromMenu → LaunchSelected,
        // que já desvia pra OpenSteamInstaller nesse caso).
        if (game.IsOwnedNotInstalled)
        {
            Add("Instalar", () => OpenGameFromMenu(game));
            return;
        }

        // Ordem pedida pelo usuário nesta leva ("muito desorganizado,
        // vamos por partes"): Abrir jogo, Favoritar/Desfavoritar, Excluir
        // atalho/Excluir jogo, Personalizar — as 4 edições de arte que
        // antes eram itens soltos no topo agora vivem dentro do submenu
        // "Personalizar" (ver OpenPersonalizeMenu).
        Add("Abrir jogo", () => OpenGameFromMenu(game));
        Add(game.IsFavorite ? "Desfavoritar" : "Favoritar", () => ToggleFavorite(game));

        // Excluir atalho/jogo. Dois caminhos BEM diferentes:
        //   - Steam: "Excluir jogo" abre o desinstalador da própria Steam
        //     (steam://uninstall/{appid}, ver GameLauncher) — a Steam
        //     cuida de apagar os arquivos de verdade, o launcher nunca
        //     toca nada no disco.
        //   - Não-Steam ADICIONADO À MÃO (Source != Steam, veio de
        //     "Adicionar jogo"/AddManualGame, não de um scanner
        //     automático): "Excluir atalho" chama
        //     ILibraryService.RemoveManualGame — só remove da nossa
        //     lista, nunca apaga os arquivos do jogo em si.
        //   - Não-Steam ESCANEADO AUTOMATICAMENTE (ex: pasta configurada
        //     em AppSettings.XboxScanFolders, ver XboxFolderScanner — Id
        //     sempre começa com "xboxfolder_") NÃO ganha item de exclusão
        //     ainda: RemoveManualGame não teria o que remover (não é um
        //     "jogo manual" guardado por nós, reaparece no próximo scan
        //     de qualquer forma) — pra tirar um desses da lista, por
        //     enquanto é preciso editar XboxScanFolders no settings.json.
        if (game.Source == GameSource.Steam)
        {
            if (game.SteamAppId is { } appId)
            {
                Add("Excluir jogo", () => RequestDeleteSteamGame(game, appId));
            }
        }
        else if (!game.Id.StartsWith("xboxfolder_", StringComparison.Ordinal))
        {
            Add("Excluir atalho", () => RequestDeleteShortcut(game));
        }

        Add("Personalizar", () => OpenPersonalizeMenu(game));
    }

    /// <summary>
    /// Submenu "Personalizar" (novo nesta leva, pedido do usuário pra
    /// organizar o menu de contexto): troca o conteúdo do menu já aberto
    /// pelas 4 edições de arte (antes eram itens soltos no topo do menu
    /// principal) + "Voltar", que reconstrói o menu principal de novo —
    /// mesmo padrão de troca de conteúdo do overlay já usado por
    /// OpenConfirmMenu.
    /// </summary>
    private void OpenPersonalizeMenu(GameEntry game)
    {
        var tile = Games.FirstOrDefault(t => t.Game.Id == game.Id);
        if (tile is null) return;

        ContextMenuTarget = tile;

        ContextMenuItems.Clear();
        _contextMenuActions.Clear();

        void Add(string label, Action action)
        {
            ContextMenuItems.Add(new ContextMenuItemViewModel(label));
            _contextMenuActions.Add(action);
        }

        // v0.7.0: sem o "Escolher" repetido em todo item (pedido do usuário).
        Add("Capa", () => OpenCoverPicker(game, ArtworkPickerKind.Grid));
        Add("Capa larga", () => OpenCoverPicker(game, ArtworkPickerKind.WideGrid)); // WideGridImagePath — a capa só da WIDE CAPSULE, independente da capa normal e do banner
        Add("Banner", () => OpenCoverPicker(game, ArtworkPickerKind.Hero));
        Add("Logo", () => OpenCoverPicker(game, ArtworkPickerKind.Logo));
        Add("Ícone", () => OpenCoverPicker(game, ArtworkPickerKind.Icon)); // IconImagePath — aparece na tela "Iniciando <jogo>..."
        Add("Voltar", () => ReopenContextMenu(tile));

        _contextMenuIndex = 0;
        UpdateContextMenuSelection();
        IsContextMenuOpen = true;
    }

    /// <summary>"Voltar" do submenu "Personalizar" — reabre o menu principal pro mesmo jogo.</summary>
    private void ReopenContextMenu(GameTileViewModel tile)
    {
        ContextMenuTarget = tile;
        _contextMenuIndex = 0;
        BuildContextMenu(tile);
        UpdateContextMenuSelection();
        IsContextMenuOpen = true;
    }

    private void ToggleFavorite(GameEntry game)
    {
        _library.SetFavorite(game.Id, !game.IsFavorite);
        // RebuildGamesFromLibrary (não Refresh — não precisa re-escanear)
        // pra aba "Favoritos" refletir o toggle na hora, caso esteja ativa.
        RebuildGamesFromLibrary();
    }

    private void OpenGameFromMenu(GameEntry game)
    {
        var tile = Games.FirstOrDefault(t => t.Game.Id == game.Id);
        if (tile is null) return;
        SelectedIndex = Games.IndexOf(tile);
        LaunchSelected();
    }

    /// <summary>
    /// Troca o conteúdo do menu já aberto por uma confirmação de duas
    /// opções ("Cancelar" primeiro — cursor nunca começa em cima da ação
    /// destrutiva) em vez de executar a exclusão na hora. Reaproveita o
    /// mesmo overlay/estado (IsContextMenuOpen/ContextMenuItems) em vez de
    /// inventar um quarto menu — ConfirmContextMenuSelection já fechou o
    /// menu (CloseContextMenu) antes de rodar a ação que chamou isto, por
    /// isso reabre IsContextMenuOpen e reaponta ContextMenuTarget aqui.
    /// </summary>
    private void OpenConfirmMenu(GameEntry game, string confirmLabel, Action onConfirm)
    {
        ContextMenuTarget = Games.FirstOrDefault(t => t.Game.Id == game.Id);

        ContextMenuItems.Clear();
        _contextMenuActions.Clear();

        void Add(string label, Action action)
        {
            ContextMenuItems.Add(new ContextMenuItemViewModel(label));
            _contextMenuActions.Add(action);
        }

        Add("Cancelar", () => { });
        Add(confirmLabel, onConfirm);

        _contextMenuIndex = 0; // "Cancelar" pré-selecionado
        UpdateContextMenuSelection();
        IsContextMenuOpen = true;
    }

    private void RequestDeleteShortcut(GameEntry game)
    {
        OpenConfirmMenu(game, "Sim, excluir atalho", () =>
        {
            _library.RemoveManualGame(game.Id);
            Refresh();
        });
    }

    private void RequestDeleteSteamGame(GameEntry game, uint appId)
    {
        OpenConfirmMenu(game, "Sim, abrir desinstalador da Steam", () => _launcher.OpenSteamUninstaller(appId));
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

    /// <summary>
    /// Despacho dinâmico (_contextMenuActions, montado por BuildContextMenu
    /// ou OpenConfirmMenu) em vez do switch fixo por índice de antes — ver
    /// o comentário de ContextMenuItems. Mesmo padrão já usado por
    /// ConfirmGlobalMenuSelection/ConfirmPowerMenuSelection.
    /// </summary>
    public void ConfirmContextMenuSelection()
    {
        if (!IsContextMenuOpen || ContextMenuTarget is null)
        {
            CloseContextMenu();
            return;
        }

        var index = _contextMenuIndex;
        var gameTitle = ContextMenuTarget.Game.Title;
        CloseContextMenu();

        DebugLog.Write($"ConfirmContextMenuSelection: index={index} game={gameTitle}");

        if (index >= 0 && index < _contextMenuActions.Count)
        {
            _contextMenuActions[index]();
        }
    }

    private void UpdateContextMenuSelection()
    {
        for (var i = 0; i < ContextMenuItems.Count; i++)
        {
            ContextMenuItems[i].IsSelected = i == _contextMenuIndex;
        }
    }

    // ------------------------------------------------------------------
    // Menu geral em subpastas (v0.5.0, pedido do usuário: "menu ficou muito
    // poluído... separar e fazer subpastas"). Uma única janela de menu com
    // "páginas": Raiz → Biblioteca / HUD / Configurações / Power. B volta
    // pra Raiz; View fecha de vez. O antigo menu Power separado agora é só
    // a página Power deste mesmo menu (o botão ⏻ abre direto nela).
    // ------------------------------------------------------------------

    private enum GlobalMenuPage { Root, Library, Hud, Settings, Power }

    private GlobalMenuPage _globalMenuPage = GlobalMenuPage.Root;

    // Paralelo a _globalMenuActions: true = o item só troca de página
    // (menu continua aberto); false = ação de verdade (fecha e executa).
    private readonly List<bool> _globalMenuNavigates = new();

    private string _globalMenuTitle = "Menu";
    public string GlobalMenuTitle
    {
        get => _globalMenuTitle;
        private set => SetField(ref _globalMenuTitle, value);
    }

    private string _globalMenuHint = string.Empty;
    public string GlobalMenuHint
    {
        get => _globalMenuHint;
        private set => SetField(ref _globalMenuHint, value);
    }

    public bool HasGlobalMenuHint => !string.IsNullOrEmpty(_globalMenuHint);

    /// <summary>View/Back button: opens the global menu (raiz), or closes it if already open (toggle).</summary>
    public void ToggleGlobalMenu()
    {
        if (IsGlobalMenuOpen)
        {
            CloseGlobalMenu();
            return;
        }

        OpenGlobalMenuAt(GlobalMenuPage.Root);
    }

    private void OpenGlobalMenuAt(GlobalMenuPage page)
    {
        _globalMenuPage = page;
        BuildGlobalMenu();
        if (GlobalMenuItems.Count == 0) return;

        _globalMenuIndex = 0;
        UpdateGlobalMenuSelection();
        IsGlobalMenuOpen = true;
    }

    private void NavigateGlobalMenu(GlobalMenuPage page)
    {
        _globalMenuPage = page;
        BuildGlobalMenu();
        _globalMenuIndex = 0;
        UpdateGlobalMenuSelection();
    }

    public void CloseGlobalMenu()
    {
        IsGlobalMenuOpen = false;
    }

    /// <summary>B/Esc: numa subpasta volta pra raiz; na raiz fecha.</summary>
    public void GlobalMenuBack()
    {
        if (_globalMenuPage != GlobalMenuPage.Root)
        {
            NavigateGlobalMenu(GlobalMenuPage.Root);
            return;
        }

        CloseGlobalMenu();
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
        if (index < 0 || index >= _globalMenuActions.Count) return;

        if (_globalMenuNavigates[index])
        {
            _globalMenuActions[index]();
            return;
        }

        CloseGlobalMenu();
        _globalMenuActions[index]();
    }

    private void UpdateGlobalMenuSelection()
    {
        for (var i = 0; i < GlobalMenuItems.Count; i++)
        {
            GlobalMenuItems[i].IsSelected = i == _globalMenuIndex;
        }
    }

    /// <summary>
    /// Rebuilt fresh every time a page opens — the set of available actions
    /// depends on state (is a game running? update ready? API key?).
    /// </summary>
    private void BuildGlobalMenu()
    {
        GlobalMenuItems.Clear();
        _globalMenuActions.Clear();
        _globalMenuNavigates.Clear();

        void Add(string label, Action action)
        {
            GlobalMenuItems.Add(new ContextMenuItemViewModel(label));
            _globalMenuActions.Add(action);
            _globalMenuNavigates.Add(false);
        }

        void Folder(string label, GlobalMenuPage page)
        {
            GlobalMenuItems.Add(new ContextMenuItemViewModel(label));
            _globalMenuActions.Add(() => NavigateGlobalMenu(page));
            _globalMenuNavigates.Add(true);
        }

        switch (_globalMenuPage)
        {
            case GlobalMenuPage.Library:
                GlobalMenuTitle = "Biblioteca";
                Add("Atualizar Biblioteca", Refresh);
                if (CanFetchArtwork) Add("Atualizar Capas Automático", FetchMissingArtwork);
                Add("Adicionar jogo", () => AddGameRequested?.Invoke(this, EventArgs.Empty));
                Add("Downloads", ToggleDownloads);
                break;

            case GlobalMenuPage.Hud:
                GlobalMenuTitle = "HUD";
                if (_hud is not null)
                {
                    for (var i = 0; i < PerformanceHud.PresetNames.Length; i++)
                    {
                        var preset = i;
                        var marker = _hud.Preset == preset ? "●" : "○";
                        Add($"{marker}  {PerformanceHud.PresetNames[preset]}", () => SetOverlayPreset(preset));
                    }
                }
                if (_performanceOverlay.IsInstalled)
                {
                    Add(
                        _performanceOverlay.IsRunning ? "RTSS: rodando" : "RTSS: iniciar",
                        () =>
                        {
                            if (!_performanceOverlay.IsRunning) _performanceOverlay.EnsureRunning();
                        });
                }
                else
                {
                    Add("RTSS: baixar (necessário pro HUD)", _performanceOverlay.OpenDownloadPage);
                }
                break;

            case GlobalMenuPage.Settings:
                GlobalMenuTitle = "Configurações";
                Add("Preferências", () => SettingsRequested?.Invoke(this, EventArgs.Empty));
                if (_updates is { IsInstalled: true } && !IsUpdateReady)
                {
                    Add($"Verificar atualizações (v{_updates.CurrentVersionText})", () => _ = CheckForUpdatesAsync(userInitiated: true));
                }
                break;

            case GlobalMenuPage.Power:
                GlobalMenuTitle = "Energia";
                Add("Ir para a área de trabalho", () => DesktopRequested?.Invoke(this, EventArgs.Empty));
                Add("Minimizar", () => MinimizeRequested?.Invoke(this, EventArgs.Empty));
                Add("Desligar tela", () => TurnOffScreenRequested?.Invoke(this, EventArgs.Empty));
                Add("Reiniciar o aparelho", () => RestartDeviceRequested?.Invoke(this, EventArgs.Empty));
                Add("Desligar o aparelho", () => ShutdownDeviceRequested?.Invoke(this, EventArgs.Empty));
                Add("Sair", () => QuitRequested?.Invoke(this, EventArgs.Empty));
                break;

            default:
                GlobalMenuTitle = "Menu";

                // Coisas do momento ficam soltas no topo, só quando existem.
                if (IsUpdateReady)
                {
                    Add(
                        RunningGame is null
                            ? $"Reiniciar pra atualizar (v{PendingUpdateVersion})"
                            : $"Atualização v{PendingUpdateVersion} pronta — feche o jogo pra aplicar",
                        ApplyUpdate);
                }

                if (RunningGame is not null)
                {
                    Add($"Fechar: {RunningGame.Title}", CloseRunningGame);
                }

                Folder("Biblioteca", GlobalMenuPage.Library);
                Folder("HUD", GlobalMenuPage.Hud);
                Folder("Configurações", GlobalMenuPage.Settings);
                Folder("Energia", GlobalMenuPage.Power);
                Add("Sobre", OpenAbout);
                break;
        }

        // v0.7.0: sem a linha de "↑↓ navega · A confirma..." em todo menu
        // (pedido do usuário) — só fica uma dica quando ela ensina algo que
        // não dá pra descobrir sozinho (o atalho do HUD dentro do jogo).
        GlobalMenuHint = _globalMenuPage == GlobalMenuPage.Hud
            ? "No jogo: L3+R3 (ou Ctrl+Alt+O) troca o HUD"
            : string.Empty;
        OnPropertyChanged(nameof(HasGlobalMenuHint));
    }

    // ------------------------------------------------------------------
    // "Sobre" (Configurações → Sobre, v0.7.0)
    // ------------------------------------------------------------------

    private bool _isAboutOpen;
    public bool IsAboutOpen
    {
        get => _isAboutOpen;
        private set => SetField(ref _isAboutOpen, value);
    }

    public void OpenAbout() => IsAboutOpen = true;

    public void CloseAbout() => IsAboutOpen = false;

    /// <summary>"Versão 0.7.0" no Sobre.</summary>
    public string AboutVersionText
    {
        get
        {
            var full = AppVersionText;
            var start = full.IndexOf('v');
            var end = full.IndexOf(' ', Math.Max(start, 0));
            return start >= 0 ? "Versão " + (end > start ? full[(start + 1)..end] : full[(start + 1)..]) : full;
        }
    }

    /// <summary>"Ir para a área de trabalho" (página Power) — MainWindow esconde na bandeja e minimiza as outras janelas.</summary>
    public event EventHandler? DesktopRequested;

    /// <summary>
    /// Botão ⏻ da barra de baixo: abre o menu direto na página Power, ou
    /// fecha se já estiver nela (toggle).
    /// </summary>
    public void TogglePowerMenu()
    {
        if (IsGlobalMenuOpen && _globalMenuPage == GlobalMenuPage.Power)
        {
            CloseGlobalMenu();
            return;
        }

        OpenGlobalMenuAt(GlobalMenuPage.Power);
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

        // Rótulos renomeados nesta leva (pedido do usuário) — "Reiniciar
        // o Legion Go"/"Desligar o Legion Go" eram específicos do Legion
        // Go, mas o projeto agora também roda no ROG Ally; "o aparelho"
        // vale pra qualquer um dos dois.
        Add("Desligar tela", () => TurnOffScreenRequested?.Invoke(this, EventArgs.Empty));
        Add("Reiniciar o aparelho", () => RestartDeviceRequested?.Invoke(this, EventArgs.Empty));
        Add("Desligar o aparelho", () => ShutdownDeviceRequested?.Invoke(this, EventArgs.Empty));

        // Movido pra aqui a pedido do usuário (antes vivia no menu
        // geral/engrenagem) — faz mais sentido ao lado de
        // reiniciar/desligar o aparelho do que junto de
        // Atualizar Biblioteca/Adicionar jogo.
        Add("Sair", () => QuitRequested?.Invoke(this, EventArgs.Empty));
    }

    private bool _isDownloadsOpen;

    /// <summary>
    /// Painel de Downloads (item "Downloads" do menu global) — pedido do
    /// usuário como "gerenciador de download". Mostra duas seções:
    /// 1) ActiveDownloads: downloads/atualizações da Steam em andamento
    /// AGORA (lido do manifesto .acf de cada jogo, ver
    /// ISteamLibraryScanner.ScanActiveDownloads) — é a parte que o usuário
    /// pediu primeiro ("mais pra olhar os jogos que estão sendo baixado
    /// nos outros launcher"). 2) OwnedNotInstalledGames: jogos da conta
    /// Steam que ainda não estão instalados aqui, via Steam Web API — a
    /// parte que ele pediu como "se der, seria legal também".
    ///
    /// DECISÕES DE ESCOPO (v1), nenhuma confirmada de volta com o usuário
    /// ainda — sinalizar isso na resposta:
    /// - Só Steam. Epic/Xbox app não têm nenhum arquivo local confiável pra
    ///   ler progresso de download (o truque do .acf é específico da
    ///   Steam) — então "baixando agora" de Epic/Xbox não existe nesta
    ///   versão. "Jogos da conta" também é só Steam: o usuário só marcou
    ///   essa opção pra Steam na pergunta de esclarecimento, e Epic não
    ///   tem API pública pra isso sem OAuth completo.
    /// - "Instalar" é mouse/touch-only (ver OnGamepadTick/OnWindowKeyDown
    ///   em MainWindow.axaml.cs) — quebra do padrão "tudo navegável por
    ///   controle" do resto do app, decisão deliberada pra não ter que
    ///   inventar um modelo de foco novo só pra essa lista.
    /// </summary>
    public bool IsDownloadsOpen
    {
        get => _isDownloadsOpen;
        private set => SetField(ref _isDownloadsOpen, value);
    }

    public ObservableCollection<DownloadProgressInfo> ActiveDownloads { get; } = new();

    public bool HasActiveDownloads => ActiveDownloads.Count > 0;
    public bool NoActiveDownloads => !HasActiveDownloads;

    /// <summary>
    /// O download que o indicador da barra de cima representa (pedido do
    /// usuário: "algo que indica visual o percentual do download"). A
    /// Steam baixa um jogo por vez e deixa o resto na fila — então
    /// prefere o que já está DE FATO em andamento (algum byte baixado, mas
    /// não terminado); sem nenhum assim, cai pro primeiro da lista.
    /// </summary>
    private DownloadProgressInfo? PrimaryActiveDownload =>
        ActiveDownloads.FirstOrDefault(d => d.BytesDownloaded > 0 && d.BytesDownloaded < d.BytesToDownload)
        ?? ActiveDownloads.FirstOrDefault();

    /// <summary>0-100 pra mini barra de progresso do lado do ícone ⇩ (só aparece com HasActiveDownloads).</summary>
    public double TopBarDownloadPercent => PrimaryActiveDownload?.PercentComplete ?? 0;

    /// <summary>
    /// Texto "45%" do lado do ícone ⇩. Montado aqui em C# de propósito, em
    /// vez de StringFormat no XAML — foi exatamente um StringFormat
    /// começando com chave de abertura que quebrou o build numa leva
    /// anterior (AVLN2000).
    /// </summary>
    public string TopBarDownloadPercentText =>
        PrimaryActiveDownload is { } d ? $"{d.PercentComplete:0}%" : string.Empty;

    /// <summary>Tooltip do atalho de Downloads: sempre diz que o Y abre, e quando tem algo baixando diz o quê (e quantos na fila).</summary>
    public string TopBarDownloadToolTip
    {
        get
        {
            var tip = "Downloads (Y no controle)";
            if (PrimaryActiveDownload is not { } d) return tip;

            tip += $"\nBaixando: {d.Title} — {d.PercentComplete:0}%";
            var queued = ActiveDownloads.Count - 1;
            if (queued > 0) tip += $" (+{queued} na fila)";
            return tip;
        }
    }

    public ObservableCollection<OwnedSteamGame> OwnedNotInstalledGames { get; } = new();

    /// <summary>True só quando settings.json tem SteamWebApiKey E SteamId64 preenchidos — ver AppSettings.</summary>
    public bool HasSteamWebApiCredentials => _library.HasSteamWebApiCredentials;
    public bool NoSteamWebApiCredentials => !HasSteamWebApiCredentials;

    private bool _isLoadingOwnedGames;
    public bool IsLoadingOwnedGames
    {
        get => _isLoadingOwnedGames;
        private set => SetField(ref _isLoadingOwnedGames, value);
    }

    private string? _ownedGamesErrorMessage;
    public string? OwnedGamesErrorMessage
    {
        get => _ownedGamesErrorMessage;
        private set
        {
            if (SetField(ref _ownedGamesErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasOwnedGamesError));
            }
        }
    }

    public bool HasOwnedGamesError => !string.IsNullOrEmpty(OwnedGamesErrorMessage);

    private string? _notInstalledTabMessage;

    /// <summary>
    /// Mensagem mostrada NO LUGAR da fileira de jogos quando a aba "Não
    /// instalados" está ativa e ainda não há tile nenhuma pra mostrar —
    /// sem credencial configurada, carregando, erro de rede, ou lista
    /// genuinamente vazia (conta sem nenhum jogo faltando instalar).
    /// Reavaliada em RebuildGamesFromLibrary e nos dois pontos de
    /// RefreshOwnedGames que mudam IsLoadingOwnedGames — sempre null
    /// quando outra aba está ativa.
    /// </summary>
    public string? NotInstalledTabMessage
    {
        get => _notInstalledTabMessage;
        private set
        {
            if (SetField(ref _notInstalledTabMessage, value))
            {
                OnPropertyChanged(nameof(HasNotInstalledTabMessage));
            }
        }
    }

    public bool HasNotInstalledTabMessage => !string.IsNullOrEmpty(NotInstalledTabMessage);

    private void UpdateNotInstalledTabMessage()
    {
        if (_activeFilter != LibraryFilter.NotInstalled)
        {
            NotInstalledTabMessage = null;
            return;
        }

        if (!HasSteamWebApiCredentials)
        {
            NotInstalledTabMessage = "Configure a Steam Web API key e o SteamID64 na tela de Configurações pra ver aqui os jogos da sua conta que ainda não estão instalados.";
        }
        else if (IsLoadingOwnedGames)
        {
            NotInstalledTabMessage = "Carregando jogos da sua conta...";
        }
        else if (HasOwnedGamesError)
        {
            NotInstalledTabMessage = OwnedGamesErrorMessage;
        }
        else if (Games.Count == 0)
        {
            NotInstalledTabMessage = "Nenhum jogo da sua conta Steam está faltando instalar aqui.";
        }
        else
        {
            NotInstalledTabMessage = null;
        }
    }

    /// <summary>Item "Downloads" do menu global: abre o painel, ou fecha se já estiver aberto (toggle).</summary>
    public void ToggleDownloads()
    {
        if (IsDownloadsOpen)
        {
            CloseDownloads();
            return;
        }

        RefreshActiveDownloads();
        IsDownloadsOpen = true;
    }

    /// <summary>
    /// Botão "Abrir downloads na Steam" (A no controle com o painel aberto) —
    /// a Steam não oferece nenhum jeito oficial de outro programa pausar/
    /// retomar um download dela, então o caminho é levar direto pra tela de
    /// downloads da própria Steam, onde dá pra pausar.
    /// </summary>
    public void OpenSteamDownloads()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "steam://open/downloads",
                UseShellExecute = true,
            });
        }
        catch
        {
            // Steam não instalada — melhor esforço.
        }
    }

    public void CloseDownloads()
    {
        IsDownloadsOpen = false;
    }

    /// <summary>
    /// Reconsulta "baixando agora" — chamado ao abrir o painel e, enquanto
    /// ele ficar aberto, por um timer periódico (ver MainWindow.axaml.cs)
    /// já que o progresso real continua mudando com o painel aberto na
    /// tela.
    /// </summary>
    // Última leitura de cada download (por título) pra calcular velocidade.
    private readonly Dictionary<string, (long Bytes, DateTime At, double? Speed)> _downloadSamples = new();

    public void RefreshActiveDownloads()
    {
        var now = DateTime.UtcNow;
        ActiveDownloads.Clear();
        foreach (var download in _library.GetActiveDownloads())
        {
            // Velocidade = bytes novos ÷ tempo desde a leitura anterior. A
            // Steam só regrava o .acf de tempos em tempos, então entre duas
            // gravações a diferença é 0 — nesse caso mantém a última
            // velocidade medida em vez de mostrar "parado" piscando.
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
                else
                {
                    download.BytesPerSecond = (now - previous.At).TotalSeconds < 30 ? previous.Speed : null;
                }
            }
            else
            {
                _downloadSamples[download.Title] = (download.BytesDownloaded, now, null);
            }

            ActiveDownloads.Add(download);
        }
        OnPropertyChanged(nameof(HasActiveDownloads));
        OnPropertyChanged(nameof(NoActiveDownloads));
        OnPropertyChanged(nameof(TopBarDownloadPercent));
        OnPropertyChanged(nameof(TopBarDownloadPercentText));
        OnPropertyChanged(nameof(TopBarDownloadToolTip));
    }

    /// <summary>
    /// Busca "na sua conta, não instalado" — só dispara se
    /// HasSteamWebApiCredentials (senão a tela já mostra a mensagem
    /// explicando o que falta configurar, ver MainWindow.axaml e
    /// NotInstalledTabMessage). Mais lenta que RefreshActiveDownloads
    /// (chamada de rede de verdade pra api.steampowered.com), por isso
    /// IsLoadingOwnedGames separado em vez de reusar IsLoading* de outra
    /// tela. Alimenta DOIS consumidores da MESMA OwnedNotInstalledGames:
    /// o painel de Downloads (se IsDownloadsOpen) e a aba "Não instalados"
    /// da biblioteca principal (se essa for a aba ativa, ver SetFilter) —
    /// chamado por qualquer um dos dois ao abrir/selecionar.
    /// </summary>
    public async void RefreshOwnedGames()
    {
        if (!HasSteamWebApiCredentials) return;

        IsLoadingOwnedGames = true;
        OwnedGamesErrorMessage = null;
        UpdateNotInstalledTabMessage();

        try
        {
            var owned = await _library.GetOwnedButNotInstalledSteamGamesAsync();

            // Nem o painel nem a aba que pediram isso continuam abertos/
            // ativos — mesma ideia do OpenCoverPicker, não ressuscita com
            // resultado velho.
            if (!IsDownloadsOpen && _activeFilter != LibraryFilter.NotInstalled) return;

            OwnedNotInstalledGames.Clear();
            foreach (var game in owned)
            {
                OwnedNotInstalledGames.Add(game);
            }

            if (_activeFilter == LibraryFilter.NotInstalled)
            {
                RebuildGamesFromLibrary();
                FetchMissingArtworkForOwnedGames();
            }
        }
        catch (Exception ex)
        {
            OwnedGamesErrorMessage = $"Erro ao buscar jogos da conta: {ex.Message}";
        }
        finally
        {
            IsLoadingOwnedGames = false;
            UpdateNotInstalledTabMessage();
        }
    }

    /// <summary>
    /// Busca a capa (GridImagePath) pra cada jogo "na sua conta, não
    /// instalado" que ainda não tem uma — mesma ideia de
    /// FetchMissingArtwork, só que pra OwnedNotInstalledGames em vez de
    /// _library.Games (que só tem jogo JÁ instalado). Usa o MESMO esquema
    /// de Id ("steam_{appid}", ver ILibraryService.BuildEntryForOwnedGame)
    /// que SteamLibraryScanner usaria se/quando o jogo for instalado de
    /// verdade — então a capa buscada aqui já aparece pronta depois, sem
    /// precisar buscar de novo. Só a capa (não hero/logo/wide-grid): essas
    /// tiles nunca viram a WIDE CAPSULE "último jogo aberto" (ver
    /// BuildEntryForOwnedGame zerando LastPlayedUtc), então não precisam
    /// do resto.
    /// </summary>
    private async void FetchMissingArtworkForOwnedGames()
    {
        if (!_artwork.HasApiKey) return;

        foreach (var owned in OwnedNotInstalledGames.ToList())
        {
            // Usuário saiu da aba no meio do caminho — para, sem gastar
            // mais chamadas à API por nada que mais ninguém vai ver.
            if (_activeFilter != LibraryFilter.NotInstalled) return;

            var entry = _library.BuildEntryForOwnedGame(owned);
            if (!string.IsNullOrEmpty(entry.GridImagePath)) continue;

            await _artwork.FetchGridAsync(entry);
        }

        if (_activeFilter == LibraryFilter.NotInstalled)
        {
            RebuildGamesFromLibrary();
        }
    }

    /// <summary>
    /// Botão "Instalar" (mouse/touch, ver IsDownloadsOpen) de um item de
    /// OwnedNotInstalledGames — delega pra steam://install/{appid}; a
    /// própria Steam abre e cuida do download de verdade, este launcher
    /// nunca baixa nada diretamente (mesmo padrão já usado pelo
    /// "Excluir jogo" do menu de contexto, ver OpenSteamUninstaller).
    /// </summary>
    public void InstallOwnedGame(OwnedSteamGame game)
    {
        _launcher.OpenSteamInstaller(game.AppId);
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
            ArtworkPickerKind.WideGrid => $"Capa larga · {game.Title}",
            ArtworkPickerKind.Hero => $"Banner · {game.Title}",
            ArtworkPickerKind.Logo => $"Logo · {game.Title}",
            ArtworkPickerKind.Icon => $"Ícone · {game.Title}",
            _ => $"Capa · {game.Title}",
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
                ArtworkPickerKind.Icon => await _artwork.GetIconCandidatesAsync(game),
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
                ArtworkPickerKind.Icon => await _artwork.ApplyIconCandidateAsync(game, chosen),
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
        OnPropertyChanged(nameof(IsFilterOfficialActive));
        OnPropertyChanged(nameof(IsFilterNonSteamActive));
        OnPropertyChanged(nameof(IsFilterNotInstalledActive));

        RebuildGamesFromLibrary();

        // "Não instalados" busca de rede (Steam Web API) — refaz toda vez
        // que a aba é selecionada, mesma cadência já usada pelo painel de
        // Downloads (ToggleDownloads chama isso toda vez que abre). Sem
        // credencial configurada, RefreshOwnedGames já retorna na hora sem
        // fazer nada (a mensagem "configure..." vem de
        // UpdateNotInstalledTabMessage, chamado dentro de
        // RebuildGamesFromLibrary acima).
        if (filter == LibraryFilter.NotInstalled)
        {
            RefreshOwnedGames();
        }
    }

    private bool MatchesActiveFilter(GameEntry game) => _activeFilter switch
    {
        LibraryFilter.Favorites => game.IsFavorite,
        // "Oficiais" = Steam + Xbox juntos (decisão do usuário ao
        // reorganizar as abas) — "Não Steam" vira o complemento exato
        // disso (nem Steam nem Xbox), em vez de "qualquer coisa !=
        // Steam" como era antes (quando isso incluía Xbox também).
        LibraryFilter.Official => game.Source == GameSource.Steam || game.Source == GameSource.XboxApp,
        LibraryFilter.NonSteam => game.Source != GameSource.Steam && game.Source != GameSource.XboxApp,
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
    ///
    /// "Não instalados" é a EXCEÇÃO: não vem de _library.Games (que só
    /// tem jogo JÁ instalado) — vem de OwnedNotInstalledGames, convertido
    /// pra GameEntry sintética via ILibraryService.BuildEntryForOwnedGame
    /// a cada rebuild (ver MainViewModel.RefreshOwnedGames/
    /// FetchMissingArtworkForOwnedGames pra quando essa lista é
    /// (re)carregada).
    /// </summary>
    /// <summary>Recria as tiles (ex: depois de mudar o tamanho das capas nas Configurações).</summary>
    public void ReloadTiles()
    {
        var selected = SelectedIndex;
        RebuildGamesFromLibrary();
        if (selected >= 0 && Games.Count > 0) SelectedIndex = Math.Min(selected, Games.Count - 1);
    }

    private void RebuildGamesFromLibrary()
    {
        var previouslySelectedId = SelectedTile?.Game.Id;

        Games.Clear();

        if (_activeFilter == LibraryFilter.NotInstalled)
        {
            foreach (var owned in OwnedNotInstalledGames)
            {
                Games.Add(new GameTileViewModel(_library.BuildEntryForOwnedGame(owned)));
            }
        }
        else
        {
            foreach (var game in _library.Games)
            {
                if (MatchesActiveFilter(game))
                {
                    Games.Add(new GameTileViewModel(game));
                }
            }
        }
        ReorderLastPlayedFirst();
        UpdateNotInstalledTabMessage();

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

                // Ícone quadrado pra tela "Iniciando <jogo>..." (seção Icons
                // do SteamGridDB).
                if (string.IsNullOrEmpty(game.IconImagePath))
                {
                    await _artwork.FetchIconAsync(game);
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

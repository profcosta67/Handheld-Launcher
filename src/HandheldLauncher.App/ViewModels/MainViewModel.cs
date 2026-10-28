using System.Collections.ObjectModel;
using System.Windows.Input;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly ILibraryService _library;
    private readonly IGameLauncher _launcher;

    public MainViewModel(ILibraryService library, IGameLauncher launcher)
    {
        _library = library;
        _launcher = launcher;
        _launcher.GameExited += OnGameExited;

        Games = new ObservableCollection<GameTileViewModel>(
            _library.Games.Select(g => new GameTileViewModel(g)));

        MoveNextCommand = new RelayCommand(MoveNext, () => Games.Count > 1);
        MovePreviousCommand = new RelayCommand(MovePrevious, () => Games.Count > 1);
        LaunchSelectedCommand = new RelayCommand(LaunchSelected, () => SelectedTile is not null);
        CloseRunningGameCommand = new RelayCommand(CloseRunningGame, () => RunningGame is not null);
        RefreshCommand = new RelayCommand(Refresh);

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

    public ICommand MoveNextCommand { get; }
    public ICommand MovePreviousCommand { get; }
    public ICommand LaunchSelectedCommand { get; }
    public ICommand CloseRunningGameCommand { get; }
    public ICommand RefreshCommand { get; }

    public void MoveNext() => SelectedIndex++;

    public void MovePrevious() => SelectedIndex--;

    public void LaunchSelected()
    {
        if (SelectedTile is null) return;
        _launcher.Launch(SelectedTile.Game);
        RunningGame = SelectedTile.Game;
    }

    public void CloseRunningGame()
    {
        if (RunningGame is null) return;
        _launcher.CloseGame(RunningGame);
        RunningGame = null;
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

        var restoredIndex = previouslySelectedId is null
            ? 0
            : Math.Max(0, Games.ToList().FindIndex(t => t.Game.Id == previouslySelectedId));

        SelectedIndex = Games.Count > 0 ? restoredIndex : -1;
    }

    private void UpdateFocus()
    {
        for (var i = 0; i < Games.Count; i++)
        {
            Games[i].IsFocused = i == _selectedIndex;
        }
    }

    private void OnGameExited(object? sender, GameExitedEventArgs e)
    {
        _library.RecordPlaySession(e.Game.Id, e.Duration);
        if (RunningGame?.Id == e.Game.Id) RunningGame = null;
        Refresh();
    }
}

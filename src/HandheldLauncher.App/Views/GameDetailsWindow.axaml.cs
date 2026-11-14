using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Input;
using HandheldLauncher.App.ViewModels;

namespace HandheldLauncher.App.Views;

/// <summary>
/// Página "Detalhes do jogo". v0.9.11 nasceu como modal de consulta (só um
/// botão Fechar). v0.18 "Detalhes 2.0": página em tela cheia (do tamanho da
/// janela principal), toda pelo controle — ver <see cref="HandleGamepadAction"/>.
///
/// Continua NÃO montando a própria ViewModel: quem abre
/// (MainWindow.OpenGameDetailsDialogAsync) já define DataContext com uma
/// GameDetailsViewModel pronta (só a MainViewModel tem os clients Steam).
/// As ações que mexem na biblioteca (jogar, favoritar, menu do jogo) também
/// são feitas lá fora: esta janela só avisa o que foi pedido
/// (<see cref="Result"/>, <see cref="FavoriteToggleRequested"/>), sem
/// duplicar nenhuma lógica que já existe na MainViewModel.
/// </summary>
public partial class GameDetailsWindow : Window
{
    public enum DetailsResult { None, Play, Options }

    /// <summary>O que o usuário pediu ao sair (Jogar / Mais opções). Lido pelo MainWindow depois do ShowDialog.</summary>
    public DetailsResult Result { get; private set; }

    /// <summary>X / botão Favoritar. O MainWindow alterna na biblioteca e devolve o novo estado.</summary>
    public event Action? FavoriteToggleRequested;

    /// <summary>
    /// Disparado antes de abrir o navegador/Explorer (mesmo padrão da Central
    /// de Ajuda): o launcher e esta página ficam "sempre por cima", então
    /// sem sair da frente a janela externa abriria escondida atrás.
    /// </summary>
    public event Action? ExternalAppOpening;
    public bool OpenedExternalApp { get; private set; }

    private const double GamepadScrollStepPx = 110;

    public GameDetailsWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDownHandler;
        // v0.26: abre com um zoom curto (escala 0,9 → 1 e aparece).
        Opened += (_, _) =>
        {
            PopupCard.Opacity = 1;
            PopupCard.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("scale(1)");
        };
    }

    private GameDetailsViewModel? Vm => DataContext as GameDetailsViewModel;

    /// <summary>Toque/clique fora do cartão fecha, como em qualquer popup.</summary>
    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, Backdrop)) Close();
    }

    /// <summary>
    /// Ocupa a tela inteira quando o launcher está em tela cheia (modo
    /// Handheld) e ~92% da área útil no modo "Uso em PC" — nos dois casos
    /// cabe em 1280×720 (ROG Xbox Ally a 150%), o que a janela fixa de
    /// 900×800 da versão anterior não fazia.
    /// </summary>
    public void FitToOwner(Window owner)
    {
        try
        {
            var screen = owner.Screens.ScreenFromPoint(owner.Position) ?? owner.Screens.Primary;
            if (screen is null) return;
            var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
            var fill = owner.WindowState is WindowState.FullScreen or WindowState.Maximized;
            var area = fill ? screen.Bounds : screen.WorkingArea;

            var w = area.Width / scaling;
            var h = area.Height / scaling;
            if (!fill)
            {
                w = Math.Min(w * 0.92, 1280);
                h = Math.Min(h * 0.92, 820);
            }

            Width = w;
            Height = h;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(
                area.X + (int)((area.Width - w * scaling) / 2),
                area.Y + (int)((area.Height - h * scaling) / 2));
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
    }

    /// <summary>
    /// Chamado pelo MainWindow.OnGamepadTick ANTES do DialogGamepadNavigator
    /// genérico (mesmo padrão de Settings/Central de Ajuda/Armazenamento).
    /// Esta página consome todas as ações de navegação — nenhuma cai no
    /// navigator genérico, que tentaria mover foco entre os botões.
    /// </summary>
    public bool HandleGamepadAction(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.MoveUp: Scroll(-GamepadScrollStepPx); return true;
            case GamepadAction.MoveDown: Scroll(GamepadScrollStepPx); return true;
            case GamepadAction.MoveLeft:
            case GamepadAction.MoveRight:
                return true;
            case GamepadAction.PreviousFilter: ChangeSection(-1); return true;
            case GamepadAction.NextFilter: ChangeSection(1); return true;
            case GamepadAction.Confirm: Play(); return true;
            case GamepadAction.Back: Close(); return true;
            case GamepadAction.GameDetails: FavoriteToggleRequested?.Invoke(); return true;
            case GamepadAction.Downloads: RunSectionAction(); return true;
            case GamepadAction.Menu: Result = DetailsResult.Options; Close(); return true;
            case GamepadAction.GlobalMenu: return true;
            default: return false;
        }
    }

    private void Scroll(double delta)
    {
        var maxOffset = Math.Max(0, ContentScroll.Extent.Height - ContentScroll.Viewport.Height);
        var y = Math.Clamp(ContentScroll.Offset.Y + delta, 0, maxOffset);
        ContentScroll.Offset = new Vector(ContentScroll.Offset.X, y);
    }

    private void ChangeSection(int delta)
    {
        if (Vm is not { } vm) return;
        vm.SectionIndex += delta;
        ContentScroll.Offset = new Vector(0, 0);
    }

    private void Play()
    {
        Result = DetailsResult.Play;
        Close();
    }

    private void RunSectionAction()
    {
        if (Vm is not { } vm || !vm.HasSectionAction) return;
        if (vm.IsMediaSection && vm.StorePageUrl is { } url) OpenExternal(url);
        else if (vm.IsFilesSection && vm.CanOpenGameFolder && vm.GameFolderPath is { } folder) OpenExternal(folder);
    }

    private void OpenExternal(string target)
    {
        try
        {
            OpenedExternalApp = true;
            Topmost = false;
            try { ExternalAppOpening?.Invoke(); } catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            DebugLog.Write($"[GameDetails] Abrindo externo: {target}");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[GameDetails] Não consegui abrir '{target}': {ex.Message}");
        }
    }

    private void OnKeyDownHandler(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }

    private void OnSectionClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not Button { Tag: string tag } || !int.TryParse(tag, out var index)) return;
        vm.SectionIndex = index;
        ContentScroll.Offset = new Vector(0, 0);
    }

    private void OnPlayClick(object? sender, RoutedEventArgs e) => Play();
    private void OnFavoriteClick(object? sender, RoutedEventArgs e) => FavoriteToggleRequested?.Invoke();
    private void OnSectionActionClick(object? sender, RoutedEventArgs e) => RunSectionAction();
    private void OnOptionsClick(object? sender, RoutedEventArgs e) { Result = DetailsResult.Options; Close(); }
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}

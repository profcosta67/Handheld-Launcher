using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using HandheldLauncher.App.ViewModels;

namespace HandheldLauncher.App.Views.Layouts;

/// <summary>
/// Layout "Cartoon" (índice 3) — ver o comentário longo dentro do .axaml.
///
/// <para>Além do <see cref="ILibraryLayout"/> (navegação, filtros, aba Nuvem,
/// adicionar jogo), este layout implementa <see cref="ILaunchScreenHost"/>:
/// escolher um jogo abre o ecrã de lançamento estilo Steam Deck, com a wide
/// capsule como fundo. A MainWindow só usa esse contrato quando ele existe,
/// por isso os outros três layouts ficam exatamente como estavam.</para>
/// </summary>
public partial class CartoonLibraryLayout : UserControl, ILibraryLayout, ILaunchScreenHost
{
    private MainViewModel? _vm;
    private IDisposable? _closeTimer;

    public CartoonLibraryLayout()
    {
        InitializeComponent();

        // Fecha o popup se a seleção mudar por fora (ex.: refresh da biblioteca).
        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                _vm = vm;
                vm.PropertyChanged += OnViewModelPropertyChanged;
            }
        };
        Unloaded += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm = null;
        };
    }

    ItemsControl ILibraryLayout.GamesItemsControl => GamesItemsControl;
    ScrollViewer ILibraryLayout.GameRowScrollViewer => GameRowScrollViewer;
    Image ILibraryLayout.HeroImage => HeroImage;
    Image ILibraryLayout.HeroBackImage => HeroBackImage;

    /// <summary>O tema é sempre grade vertical (blocos) — ver MainWindow.ApplyAppearance.</summary>
    bool ILibraryLayout.SupportsVerticalGrid => true;

    /// <summary>Rolagem por linha (grade), como no modo vertical dos outros layouts.</summary>
    bool ILibraryLayout.CentersSelection => false;

    CloudGridView ILibraryLayout.CloudGrid => CloudGrid;

    public void SetVertical(bool vertical)
    {
        GameOuterPanel.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        GameOuterPanel.Margin = vertical ? new Thickness(0) : new Thickness(0, 0, 28, 0);
        AddProgramButton.Margin = vertical ? new Thickness(0, 28, 0, 0) : new Thickness(28, 0, 0, 0);
        AddProgramButton.HorizontalAlignment = HorizontalAlignment.Left;
    }

    // ===== Ecrã de lançamento (ILaunchScreenHost) =====

    public bool IsLaunchScreenOpen => LaunchScreen.IsVisible;

    private bool AnimationsOn
    {
        get
        {
            var noAnim = (TopLevel.GetTopLevel(this) as MainWindow)?.Classes.Contains("noAnim");
            return noAnim != true;
        }
    }

    public void OpenLaunchScreen()
    {
        if (DataContext is not MainViewModel vm || vm.SelectedTile is null) return;

        // Se um fecho estava a meio, cancela — senão ele esconderia este popup.
        _closeTimer?.Dispose();
        _closeTimer = null;
        LaunchScreen.IsVisible = true;

        if (!AnimationsOn)
        {
            LaunchCard.Opacity = 1;
            LaunchCard.RenderTransform = TransformOperations.Parse("rotate(-0.6deg)");
            LaunchBurst.Opacity = 0.85;
            LaunchFlash.Opacity = 0;
            return;
        }

        // Entrada de desenho animado: flash + estrela de impacto + cartão que
        // "salta" para a frente (squash & stretch, como nos anos 90).
        LaunchCard.Transitions = null;
        LaunchCard.Opacity = 0;
        LaunchCard.RenderTransform = TransformOperations.Parse("scale(0.86) rotate(-2deg)");
        LaunchBurst.Transitions = null;
        LaunchBurst.Opacity = 0;
        LaunchBurst.RenderTransform = TransformOperations.Parse("scale(0.6) rotate(-12deg)");
        LaunchFlash.Transitions = null;
        LaunchFlash.Opacity = 0.85;

        Dispatcher.UIThread.Post(() =>
        {
            LaunchCard.Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(170) },
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(190), Easing = new CubicEaseOut() }
            };
            LaunchCard.Opacity = 1;
            LaunchCard.RenderTransform = TransformOperations.Parse("rotate(-0.6deg)");

            LaunchBurst.Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) },
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(260), Easing = new CubicEaseOut() }
            };
            LaunchBurst.Opacity = 0.85;
            LaunchBurst.RenderTransform = TransformOperations.Parse("scale(1) rotate(0deg)");

            LaunchFlash.Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(260) }
            };
            LaunchFlash.Opacity = 0;
        }, DispatcherPriority.Background);
    }

    public void CloseLaunchScreen()
    {
        if (!LaunchScreen.IsVisible) return;

        if (!AnimationsOn) { LaunchScreen.IsVisible = false; return; }

        LaunchCard.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) },
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(140), Easing = new CubicEaseIn() }
        };
        LaunchCard.Opacity = 0;
        LaunchCard.RenderTransform = TransformOperations.Parse("scale(0.92) rotate(1.4deg)");

        LaunchBurst.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) }
        };
        LaunchBurst.Opacity = 0;

        _closeTimer?.Dispose();
        _closeTimer = DispatcherTimer.RunOnce(() => LaunchScreen.IsVisible = false, TimeSpan.FromMilliseconds(150));
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTile) && LaunchScreen.IsVisible)
            CloseLaunchScreen();
    }

    private void OnPlayClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        CloseLaunchScreen();
        if (DataContext is MainViewModel vm) _ = vm.LaunchSelected();
    }

    /// <summary>Serve o botão INFO da faixa de topo e o do popup.</summary>
    private void OnInfoClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        CloseLaunchScreen();
        (DataContext as MainViewModel)?.OpenGameDetailsForSelected();
    }

    private void OnCloseLaunchClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => CloseLaunchScreen();

    private void OnLaunchScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseLaunchScreen();
        e.Handled = true;
    }

    // ===== Interação da biblioteca =====

    private MainWindow? Owner => TopLevel.GetTopLevel(this) as MainWindow;

    private void OnAddProgramClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Owner?.HandleAddProgramClick();
    private void OnSubFilterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Owner?.HandleSubFilterClick(sender, e);

    /// <summary>
    /// Clique/toque num cartão: seleciona (lógica partilhada da MainWindow) e
    /// abre o ecrã de lançamento. Clique direito continua a abrir o menu de
    /// contexto — nesse caso a MainWindow marca o evento como tratado.
    /// </summary>
    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Owner is not MainWindow owner) return;

        owner.HandleTilePointerPressed(sender, e);
        if (e.Handled) return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            OpenLaunchScreen();
    }

    private void OnGameRowPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (GameRowScrollViewer is null) return;
        var maxOffset = Math.Max(0, GameRowScrollViewer.Extent.Width - GameRowScrollViewer.Viewport.Width);
        if (maxOffset <= 0) return; // grade vertical: deixa o ScrollViewer rolar sozinho
        var wheelDelta = e.Delta.Y != 0 ? e.Delta.Y : -e.Delta.X;
        if (wheelDelta == 0) return;
        var nextOffset = Math.Clamp(GameRowScrollViewer.Offset.X - wheelDelta * 72, 0, maxOffset);
        GameRowScrollViewer.Offset = new Vector(nextOffset, GameRowScrollViewer.Offset.Y);
        e.Handled = true;
    }
}

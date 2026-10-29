using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Views;

/// <summary>
/// Modal "Configurações" dialog. Mesmo padrão do AddGameWindow — caller
/// abre com <c>await new SettingsWindow().ShowDialog(owner)</c> (ver
/// MainWindow.axaml.cs's OpenSettingsDialogAsync). Diferente do
/// AddGameWindow, esta Window é quem lê/grava settings.json direto via
/// AppSettingsStore (a ViewModel não conhece o arquivo nem o caminho) —
/// não devolve nada pro caller fechar, já que salvar aqui grava direto no
/// disco e só tem efeito depois de reiniciar o launcher (ver aviso na
/// própria tela).
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; } = new();

    private readonly AppSettings _settings;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;

        _settings = AppSettingsStore.Load();
        ViewModel.LoadFrom(_settings);
        ThemeManager.ApplyFont(this, _settings.UiFont);

        // Bug: "o scroll da configuração não está funcionando" — a janela
        // tinha altura fixa maior que a tela do handheld, então a parte de
        // baixo (Salvar/Fechar) ficava FORA da tela e a rolagem nunca era
        // necessária. Limita a altura à área útil da tela.
        Opened += (_, _) => FitToScreen();

        // Teclado virtual ao tocar numa caixa de texto (o Windows não abre
        // sozinho pro Avalonia) — só quando o toque veio de dedo/caneta, pra
        // não abrir com mouse num PC.
        AddHandler(PointerPressedEvent, (_, e) => _lastPointerWasTouch = e.Pointer.Type != PointerType.Mouse, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox && e.NavigationMethod == NavigationMethod.Pointer && _lastPointerWasTouch)
            {
                TouchKeyboard.Show();
            }
        }, RoutingStrategies.Bubble);
    }

    private bool _lastPointerWasTouch;

    private void FitToScreen()
    {
        try
        {
            var screen = Screens.Primary;
            if (screen is null) return;
            var available = screen.WorkingArea.Height / screen.Scaling - 40;
            if (available > 300 && Height > available)
            {
                Height = available;
                Position = new Avalonia.PixelPoint(Position.X, screen.WorkingArea.Y + 20);
            }
        }
        catch
        {
            // Melhor esforço.
        }
    }

    // v0.9.5: as chaves/SteamID64 saíram da tela principal (ver .axaml) —
    // só o status aparece aqui. "Gerenciar contas" abre o diálogo de
    // verdade, passando a MESMA ViewModel (edição ali já reflete no status
    // desta tela na hora, via INotifyPropertyChanged — sem precisar
    // reconsultar nada quando o diálogo fechar).
    private async void OnManageAccountsClick(object? sender, RoutedEventArgs e)
    {
        await new AccountsWindow(ViewModel).ShowDialog(this);
    }

    private void OnKeyboardClick(object? sender, RoutedEventArgs e)
    {
        TouchKeyboard.Toggle();
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.SaveTo(_settings);

        // v0.9.7, pedido do usuário ("clicar em salvar e já aparecer uma
        // tela de confirmação e clicar em ok") — foca o OK da telinha que
        // acabou de aparecer (ver SettingsWindow.axaml, Panel ligado a
        // HasSavedMessage) pra quem está no controle já poder confirmar
        // com A na hora, sem precisar navegar até lá.
        SavedOkButton.Focus();
    }

    private void OnSavedOkClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.DismissSavedMessage();
    }

    /// <summary>Preenchido por quem abre (MainWindow.OpenSettingsDialogAsync).</summary>
    public Func<Task<string>>? CheckForUpdates { get; set; }

    private async void OnCheckUpdatesClick(object? sender, RoutedEventArgs e)
    {
        if (CheckForUpdates is null) return;
        ViewModel.UpdateResultText = "Verificando…";
        ViewModel.UpdateResultText = await CheckForUpdates();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

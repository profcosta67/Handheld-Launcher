using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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

    /// <summary>
    /// v0.9.14: abas de categoria (v0.9.16: Aparência/Diretórios/
    /// Emuladores/Modo de uso/Contas) — cada Button.pill.categoryTab
    /// guarda seu índice no Tag (ver .axaml).
    /// DialogGamepadNavigator.CycleCategoryTab também cai aqui (ele só acha
    /// o botão certo e simula o clique de verdade), então LB/RB pelo
    /// controle usam exatamente este mesmo caminho — e por isso a 5ª aba
    /// da v0.9.16 não precisou de nenhuma mudança na navegação por
    /// controle.
    /// </summary>
    private void OnCategoryTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var index))
        {
            ViewModel.CategoryIndex = index;
        }
    }

    /// <summary>
    /// v0.9.16, aba Emuladores: aponta o `eden.exe` na mão (pra quando a
    /// detecção automática não achou — ver EdenInstallationDetector).
    /// Mesmo padrão de StorageProvider do AddGameWindow.OnBrowseClick:
    /// esta Window já É um TopLevel, então dá pra usar StorageProvider
    /// direto.
    /// </summary>
    private async void OnBrowseEdenClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecione o eden.exe",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Executável (*.exe)") { Patterns = new[] { "*.exe" } },
            },
        });

        var file = files.FirstOrDefault();
        if (file is null) return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        ViewModel.EdenExecutablePath = path;
    }

    /// <summary>
    /// v0.9.16, aba Emuladores: acrescenta uma pasta de ROMs na caixa (uma
    /// por linha) em vez de substituir o que já está lá — dá pra ter ROM
    /// em mais de um disco, que é o caso comum em handheld (SSD interno +
    /// cartão). Ignora pasta repetida pra não encher a lista de duplicatas
    /// se o usuário clicar duas vezes na mesma.
    /// </summary>
    private async void OnAddRomFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Selecione a pasta com as ROMs",
            AllowMultiple = false,
        });

        var folder = folders.FirstOrDefault();
        if (folder is null) return;

        var path = folder.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        var existing = ViewModel.EdenRomFoldersText
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (existing.Contains(path, StringComparer.OrdinalIgnoreCase)) return;

        ViewModel.EdenRomFoldersText = existing.Length == 0
            ? path
            : string.Join(Environment.NewLine, existing.Append(path));
    }

    /// <summary>
    /// v0.9.17, "Testar agora" da aba Emuladores: escaneia com o que está
    /// NAS CAIXAS agora (não com o que está salvo em settings.json), pra o
    /// usuário conferir a configuração sem salvar nem reiniciar o
    /// launcher. Nasceu de um diagnóstico às cegas: na v0.9.16 ele
    /// configurou a pasta certa, reiniciou, viu 1 jogo de 18 arquivos e
    /// não tinha nenhuma informação sobre o motivo.
    /// </summary>
    private void OnTestEdenScanClick(object? sender, RoutedEventArgs e)
    {
        var folders = ViewModel.EdenRomFoldersText
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        var exePath = ViewModel.EdenExecutablePath?.Trim();

        try
        {
            var scanner = new EdenLibraryScanner(string.IsNullOrWhiteSpace(exePath) ? null : exePath, folders);
            scanner.ScanInstalledGames(out var report);
            ViewModel.EdenScanResultText = DescribeScan(report);
        }
        catch (Exception ex)
        {
            ViewModel.EdenScanResultText = $"Não deu pra escanear: {ex.Message}";
        }
    }

    private static string DescribeScan(EdenScanReport report)
    {
        if (!report.EdenExecutableFound)
        {
            return "O eden.exe não foi encontrado no caminho acima — sem ele nenhum jogo é listado.";
        }

        var parts = new List<string>
        {
            report.Games == 1 ? "1 jogo encontrado" : $"{report.Games} jogos encontrados",
        };

        if (report.SkippedAddOns > 0)
        {
            parts.Add(report.SkippedAddOns == 1
                ? "1 arquivo ignorado (DLC ou atualização)"
                : $"{report.SkippedAddOns} arquivos ignorados (DLC ou atualização)");
        }

        if (report.FoldersMissing > 0)
        {
            parts.Add(report.FoldersMissing == 1
                ? "1 pasta da lista não existe"
                : $"{report.FoldersMissing} pastas da lista não existem");
        }

        if (report.UsedEdenConfigFallback)
        {
            parts.Add(report.FoldersScanned > 0
                ? "nenhuma pasta informada aqui, então foi usada a configuração do próprio Eden"
                : "nenhuma pasta informada aqui e a configuração do próprio Eden não tem pasta nenhuma");
        }

        return string.Join(" · ", parts) + ".";
    }

    private void OnKeyboardClick(object? sender, RoutedEventArgs e)
    {
        TouchKeyboard.Toggle();
    }

    // 🟢 MÉTODO: Para o botão "Abrir site da Chave" (SteamGridDB)
    private void OnOpenApiKeySiteClick(object? sender, RoutedEventArgs e)
    {
        const string url = "https://www.steamgriddb.com/profile/preferences/api";
        if (Owner is MainWindow mainWindow)
        {
            mainWindow.OpenExternalUrl(url);
        }
        else
        {
            // Fallback caso não ache a MainWindow
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
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
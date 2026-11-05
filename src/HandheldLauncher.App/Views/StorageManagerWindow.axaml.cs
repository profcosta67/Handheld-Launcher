using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Input;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Views;

/// <summary>
/// v0.18 "Gerenciador de armazenamento" (Fase 1).
/// 1ª leva (só consulta): espaço por disco, aviso de pouco espaço, tamanho
/// por jogo calculado em segundo plano.
/// 2ª leva: mover jogo entre discos (ver Platform/GameMover — o launcher só
/// move sozinho jogo adicionado à mão; Steam/Xbox vão pro mover oficial) e
/// limpar cache de shader (Platform/ShaderCacheCleaner).
/// </summary>
public partial class StorageManagerWindow : Window
{
    private readonly MainViewModel? _vm;

    public ObservableCollection<DriveRowViewModel> Drives { get; } = new();
    public ObservableCollection<GameStorageRowViewModel> GameRows { get; } = new();

    /// <summary>Antes de abrir Steam/Configurações do Windows (mesmo padrão da Central de Ajuda).</summary>
    public event Action? ExternalAppOpening;
    public bool OpenedExternalApp { get; private set; }

    private int _selectedIndex = -1;
    private int _loadGeneration;

    // Painel de ação/confirmação/progresso.
    private bool _panelOpen;
    private readonly List<Action> _panelActions = new();
    private int _panelIndex;

    // Operação longa (cópia ou limpeza) em andamento.
    private bool _busy;
    private bool _cancelAllowed;
    private bool _closeRequested;
    private CancellationTokenSource? _cts;

    private IReadOnlyList<string> _shaderFolders = Array.Empty<string>();
    private long _shaderBytes;

    public StorageManagerWindow() : this(null) { }

    public StorageManagerWindow(MainViewModel? vm)
    {
        _vm = vm;
        InitializeComponent();
        DriveList.ItemsSource = Drives;
        GameList.ItemsSource = GameRows;

        try { ThemeManager.ApplyFont(this, AppSettingsStore.Load().UiFont); }
        catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }

        RefreshDrives();
        LoadGameSizesAsync();
        _ = MeasureShaderCacheAsync();
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Nunca fecha no meio de uma cópia/limpeza: pede o cancelamento e fecha
        // sozinha quando a operação terminar de desfazer o que começou.
        if (_busy)
        {
            e.Cancel = true;
            _closeRequested = true;
            if (_cancelAllowed) _cts?.Cancel();
            TxtPanelProgress.Text = _cancelAllowed ? "Cancelando — desfazendo a cópia parcial..." : "Terminando — já está quase pronto...";
        }
        base.OnClosing(e);
    }

    // ---- Controle --------------------------------------------------------------------

    private const double GamepadScrollStepPx = 90;

    /// <summary>
    /// Interceptado em OnGamepadTick ANTES do DialogGamepadNavigator genérico.
    /// Lista: ↑/↓ escolhe jogo, A ações do jogo, Y limpar cache, B (cai no
    /// genérico) fecha. Com o painel aberto: ↑/↓ escolhe opção, A confirma,
    /// B volta (ou cancela a cópia em andamento).
    /// </summary>
    public bool HandleGamepadAction(GamepadAction action)
    {
        if (_panelOpen) return HandlePanelAction(action);

        switch (action)
        {
            case GamepadAction.MoveUp:
                if (GameRows.Count == 0) { Scroll(-GamepadScrollStepPx); return true; }
                if (_selectedIndex <= 0) { SelectRow(-1); ContentScroll.Offset = new Vector(0, 0); return true; }
                SelectRow(_selectedIndex - 1);
                return true;
            case GamepadAction.MoveDown:
                if (GameRows.Count == 0) { Scroll(GamepadScrollStepPx); return true; }
                SelectRow(Math.Min(GameRows.Count - 1, _selectedIndex + 1));
                return true;
            case GamepadAction.MoveLeft:
            case GamepadAction.MoveRight:
                return true;
            case GamepadAction.Confirm:
                if (_selectedIndex >= 0 && _selectedIndex < GameRows.Count) OpenActionsFor(GameRows[_selectedIndex]);
                return true;
            case GamepadAction.Downloads:
                OpenShaderCachePanel();
                return true;
            default:
                return false;
        }
    }

    private bool HandlePanelAction(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.MoveUp:
            case GamepadAction.MoveLeft:
                if (_panelActions.Count > 0) { _panelIndex = Math.Max(0, _panelIndex - 1); UpdatePanelSelection(); }
                return true;
            case GamepadAction.MoveDown:
            case GamepadAction.MoveRight:
                if (_panelActions.Count > 0) { _panelIndex = Math.Min(_panelActions.Count - 1, _panelIndex + 1); UpdatePanelSelection(); }
                return true;
            case GamepadAction.Confirm:
                if (!_busy && _panelIndex >= 0 && _panelIndex < _panelActions.Count) _panelActions[_panelIndex]();
                return true;
            case GamepadAction.Back:
                if (_busy)
                {
                    if (_cancelAllowed) { _cts?.Cancel(); TxtPanelProgress.Text = "Cancelando — desfazendo a cópia parcial..."; }
                }
                else ClosePanel();
                return true;
            default:
                return true; // nada escapa pro navigator genérico com o painel aberto
        }
    }

    private void Scroll(double delta)
    {
        var maxOffset = Math.Max(0, ContentScroll.Extent.Height - ContentScroll.Viewport.Height);
        ContentScroll.Offset = new Vector(ContentScroll.Offset.X, Math.Clamp(ContentScroll.Offset.Y + delta, 0, maxOffset));
    }

    private void SelectRow(int index)
    {
        _selectedIndex = index;
        for (var i = 0; i < GameRows.Count; i++) GameRows[i].IsSelected = i == index;
        if (index >= 0)
        {
            try { GameList.ContainerFromIndex(index)?.BringIntoView(); }
            catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }
        }
    }

    private void OnGameRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_panelOpen || sender is not Border { Tag: GameStorageRowViewModel row }) return;
        SelectRow(GameRows.IndexOf(row));
        OpenActionsFor(row);
    }

    private void OnClearShaderCacheClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_panelOpen) OpenShaderCachePanel();
    }

    // ---- Painel ----------------------------------------------------------------------

    private void ShowPanel(string title, string body, int selected, params (string Label, Action Action)[] options)
    {
        TxtPanelTitle.Text = title;
        TxtPanelBody.Text = body;
        PanelProgress.IsVisible = false;
        PanelOptions.Children.Clear();
        _panelActions.Clear();
        foreach (var (label, action) in options)
        {
            var btn = new Button
            {
                Content = label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Focusable = false,
            };
            btn.Classes.Add("pill");
            btn.Classes.Add("panelOption");
            var run = action;
            btn.Click += (_, _) => { if (!_busy) run(); };
            PanelOptions.Children.Add(btn);
            _panelActions.Add(run);
        }
        _panelIndex = Math.Clamp(selected, 0, Math.Max(0, options.Length - 1));
        UpdatePanelSelection();
        ActionPanel.IsVisible = true;
        _panelOpen = true;
    }

    private void ShowProgress(string title, string body, bool indeterminate)
    {
        TxtPanelTitle.Text = title;
        TxtPanelBody.Text = body;
        PanelOptions.Children.Clear();
        _panelActions.Clear();
        PanelProgress.IsVisible = true;
        PanelProgressBar.IsIndeterminate = indeterminate;
        PanelProgressBar.Value = 0;
        TxtPanelProgress.Text = "";
        ActionPanel.IsVisible = true;
        _panelOpen = true;
    }

    private void UpdatePanelSelection()
    {
        for (var i = 0; i < PanelOptions.Children.Count; i++)
        {
            if (PanelOptions.Children[i] is Button b) b.Classes.Set("selected", i == _panelIndex);
        }
    }

    private void ClosePanel()
    {
        ActionPanel.IsVisible = false;
        _panelOpen = false;
        PanelOptions.Children.Clear();
        _panelActions.Clear();
    }

    // ---- Ações por jogo --------------------------------------------------------------

    private void OpenActionsFor(GameStorageRowViewModel row)
    {
        if (row.Game is not { } game || _vm is null) return;
        var plan = GameMover.Plan(game, _vm.AllLibraryGames);
        var folderLine = row.InstallDirectory is { } dir ? $"\n\nPasta: {dir}" : "";

        switch (plan.Route)
        {
            case MoveRoute.SteamClient when game.SteamAppId is { } appId:
                ShowPanel(row.Title, plan.Explanation + folderLine, 0,
                    ("Abrir propriedades do jogo na Steam", () => { ClosePanel(); OpenExternal($"steam://gameproperties/{appId}"); }),
                    ("Voltar", ClosePanel));
                return;

            case MoveRoute.WindowsApps:
                ShowPanel(row.Title, plan.Explanation + folderLine, 0,
                    ("Abrir Aplicativos instalados do Windows", () => { ClosePanel(); OpenExternal("ms-settings:appsfeatures"); }),
                    ("Voltar", ClosePanel));
                return;

            case MoveRoute.SelfMove when plan.SourceFolder is { } source:
                OpenSelfMove(row, game, source, plan.Explanation);
                return;

            default:
                ShowPanel(row.Title, plan.Explanation + folderLine, 0, ("Voltar", ClosePanel));
                return;
        }
    }

    private void OpenSelfMove(GameStorageRowViewModel row, GameEntry game, string source, string explanation)
    {
        if (_vm?.RunningGame?.Id == game.Id || (game.ExecutablePath is { } exe && !GameMover.IsExecutableFree(exe)))
        {
            ShowPanel(row.Title, "Feche o jogo antes de mover — copiar com ele aberto pode levar arquivos pela metade.", 0, ("Voltar", ClosePanel));
            return;
        }
        if (row.SizeBytes <= 0)
        {
            ShowPanel(row.Title, "Ainda estou medindo o tamanho deste jogo. Tente de novo em alguns segundos.", 0, ("Voltar", ClosePanel));
            return;
        }

        var destinations = GameMover.DestinationDrives(source, row.SizeBytes);
        if (destinations.Count == 0)
        {
            ShowPanel(row.Title,
                $"Nenhum outro disco tem espaço livre suficiente (precisa de {DiskUsage.FormatSize(row.SizeBytes)} + folga de 0,5 GB).", 0,
                ("Voltar", ClosePanel));
            return;
        }

        var options = destinations
            .Select(d => ($"Mover para {d.Label}", (Action)(() => ConfirmMove(row, game, source, d.Root))))
            .Append(("Voltar", (Action)ClosePanel))
            .ToArray();
        ShowPanel(row.Title, $"{explanation}\n\nTamanho: {DiskUsage.FormatSize(row.SizeBytes)}", 0, options);
    }

    private void ConfirmMove(GameStorageRowViewModel row, GameEntry game, string source, string destinationRoot)
    {
        var body =
            $"Mover \"{row.Title}\" ({DiskUsage.FormatSize(row.SizeBytes)})\nde: {source}\npara: {Path.Combine(destinationRoot, "Games")}\n\n" +
            "• Não desligue o aparelho durante a cópia. B cancela a qualquer momento antes do fim.\n" +
            "• A pasta original só é apagada depois que a cópia for conferida e o atalho atualizado.\n" +
            "• Se o jogo tiver desinstalador do Windows, ele continua apontando pro lugar antigo.";
        // "Cancelar" vem primeiro e já selecionado: um A a mais sem querer não começa a cópia.
        ShowPanel("Confirmar movimentação", body, 0,
            ("Cancelar", ClosePanel),
            ("Sim, mover agora", () => _ = RunMoveAsync(row, game, source, destinationRoot)));
    }

    private async Task RunMoveAsync(GameStorageRowViewModel row, GameEntry game, string source, string destinationRoot)
    {
        if (_vm is null) return;
        _busy = true;
        _cancelAllowed = true;
        _cts = new CancellationTokenSource();
        ShowProgress($"Movendo \"{row.Title}\"", "Copiando arquivos... (B cancela)", indeterminate: false);
        DebugLog.Write($"[MOVER] Início: '{game.Title}' {source} → {destinationRoot}");

        var progress = new Progress<(long Done, long Total)>(p =>
        {
            PanelProgressBar.Value = p.Total > 0 ? p.Done * 100.0 / p.Total : 0;
            if (!_closeRequested) TxtPanelProgress.Text = $"{DiskUsage.FormatSize(p.Done)} de {DiskUsage.FormatSize(p.Total)}";
        });

        string title, message;
        try
        {
            var token = _cts.Token;
            var copy = await Task.Run(() => GameMover.CopyAndVerify(source, destinationRoot, progress, token));
            if (!copy.Success || copy.DestinationFolder is not { } dest)
            {
                title = "O jogo não foi movido";
                message = copy.Message;
            }
            else
            {
                // Daqui em diante não dá mais pra cancelar: o próximo passo troca o atalho.
                _cancelAllowed = false;
                TxtPanelBody.Text = "Cópia conferida. Atualizando o atalho...";

                var newExe = GameMover.Rebase(game.ExecutablePath, source, dest)!;
                var newWd = GameMover.Rebase(game.WorkingDirectory, source, dest);
                var newArgs = GameMover.RebaseArguments(game.LaunchArguments, source, dest);

                if (!_vm.ApplyMovedManualGame(game, newExe, newWd, newArgs))
                {
                    await Task.Run(() => GameMover.DiscardCopy(dest));
                    title = "O jogo não foi movido";
                    message = "Não consegui atualizar o atalho na biblioteca — a cópia foi descartada e a original continua intacta.";
                }
                else
                {
                    TxtPanelBody.Text = "Atalho atualizado. Apagando a pasta antiga...";
                    var (ok, deleteMessage) = await Task.Run(() => GameMover.DeleteOriginal(source));
                    title = "Jogo movido";
                    message = ok ? $"\"{row.Title}\" agora está em:\n{dest}" : deleteMessage;
                    DebugLog.Write($"[MOVER] Concluído: '{game.Title}' → {dest} (original apagada={ok})");
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[MOVER] Erro inesperado: {ex}");
            title = "O jogo não foi movido";
            message = $"Erro inesperado: {ex.Message}. Se a cópia chegou a ser criada, ela fica em {Path.Combine(destinationRoot, "Games")}; a original não foi apagada.";
        }
        finally
        {
            _busy = false;
            _cancelAllowed = false;
            _cts?.Dispose();
            _cts = null;
        }

        if (_closeRequested) { Close(); return; }
        ShowPanel(title, message, 0, ("OK", () => { ClosePanel(); RefreshDrives(); LoadGameSizesAsync(); }));
    }

    // ---- Cache de shader -------------------------------------------------------------

    private async Task MeasureShaderCacheAsync()
    {
        TxtShaderCache.Text = "Calculando...";
        try
        {
            var (folders, bytes) = await Task.Run(() =>
            {
                var f = ShaderCacheCleaner.ExistingFolders();
                return (f, ShaderCacheCleaner.MeasureBytes(f));
            });
            _shaderFolders = folders;
            _shaderBytes = bytes;
            TxtShaderCache.Text = folders.Count == 0
                ? "Nenhuma pasta de cache de shader encontrada."
                : $"{DiskUsage.FormatSize(bytes)} em {folders.Count} pasta(s) — DirectX, driver de vídeo e Steam. Refaz sozinho.";
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[SHADER] Falha ao medir: {ex.Message}");
            TxtShaderCache.Text = "Não consegui medir o cache agora.";
        }
    }

    private void OpenShaderCachePanel()
    {
        if (_vm?.RunningGame is not null)
        {
            ShowPanel("Limpar cache de shader", "Feche o jogo antes — o driver de vídeo usa esses arquivos enquanto o jogo está aberto.", 0, ("Voltar", ClosePanel));
            return;
        }
        if (_shaderFolders.Count == 0)
        {
            ShowPanel("Limpar cache de shader", "Nenhuma pasta de cache de shader foi encontrada neste aparelho (ou ainda estou medindo).", 0, ("Voltar", ClosePanel));
            return;
        }

        var body =
            $"Apaga {DiskUsage.FormatSize(_shaderBytes)} de cache de shader:\n" +
            string.Join("\n", _shaderFolders.Select(f => "• " + f)) +
            "\n\nSaves, configurações e arquivos de jogo não são tocados. Ajuda quando um jogo começa a travar/engasgar depois de atualização de driver. " +
            "O primeiro carregamento de cada jogo depois disso demora um pouco mais, enquanto o cache é refeito.";
        ShowPanel("Limpar cache de shader?", body, 0,
            ("Cancelar", ClosePanel),
            ("Sim, limpar", () => _ = RunShaderClearAsync()));
    }

    private async Task RunShaderClearAsync()
    {
        _busy = true;
        _cancelAllowed = true;
        _cts = new CancellationTokenSource();
        ShowProgress("Limpando cache de shader", "Apagando arquivos de cache... (B interrompe)", indeterminate: true);

        string message;
        try
        {
            var folders = _shaderFolders;
            var token = _cts.Token;
            var (freed, skipped) = await Task.Run(() => ShaderCacheCleaner.Clear(folders, token));
            message = $"{DiskUsage.FormatSize(freed)} liberados." +
                      (skipped > 0 ? $" {skipped} arquivo(s) estavam em uso (Steam ou driver) e ficaram — é normal." : "");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[SHADER] Erro na limpeza: {ex}");
            message = $"A limpeza parou com erro: {ex.Message}";
        }
        finally
        {
            _busy = false;
            _cancelAllowed = false;
            _cts?.Dispose();
            _cts = null;
        }

        if (_closeRequested) { Close(); return; }
        ShowPanel("Cache de shader", message, 0, ("OK", () => { ClosePanel(); RefreshDrives(); _ = MeasureShaderCacheAsync(); }));
    }

    // ---- Abrir app externo -----------------------------------------------------------

    private void OpenExternal(string target)
    {
        try
        {
            OpenedExternalApp = true;
            Topmost = false;
            try { ExternalAppOpening?.Invoke(); } catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            DebugLog.Write($"[ARMAZENAMENTO] Abrindo externo: {target}");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[ARMAZENAMENTO] Não consegui abrir '{target}': {ex.Message}");
            ShowPanel("Não consegui abrir", $"Não foi possível abrir \"{target}\" ({ex.Message}).", 0, ("OK", ClosePanel));
        }
    }

    // ---- Discos e lista de jogos (1ª leva) -------------------------------------------

    private void RefreshDrives()
    {
        Drives.Clear();
        var lowSpaceWarnings = new List<string>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable) continue;

                var totalGb = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
                if (totalGb < 0.5) continue;

                var freeGb = drive.TotalFreeSpace / 1024.0 / 1024.0 / 1024.0;
                var usedPercent = totalGb > 0 ? (1.0 - freeGb / totalGb) * 100.0 : 0;

                Drives.Add(new DriveRowViewModel
                {
                    Label = $"{drive.Name} ({(drive.DriveType == DriveType.Removable ? "microSD/externo" : "interno")})",
                    UsageText = $"{freeGb:F0} GB livres de {totalGb:F0} GB",
                    UsedPercent = Math.Clamp(usedPercent, 0, 100),
                });

                // v0.18: aviso quando sobra menos de 10% (e menos de 15GB em
                // valor absoluto, pra não disparar num disco de 2TB com 150GB
                // livres — isso não é "pouco espaço" na prática).
                if (freeGb < 15 && usedPercent > 90)
                    lowSpaceWarnings.Add($"{drive.Name} está com só {freeGb:F0} GB livres.");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[ARMAZENAMENTO] Falha ao listar discos: {ex.Message}");
        }

        LowSpaceWarning.IsVisible = lowSpaceWarnings.Count > 0;
        if (lowSpaceWarnings.Count > 0)
            TxtLowSpaceWarning.Text = "⚠ Pouco espaço: " + string.Join(" ", lowSpaceWarnings);
    }

    private void LoadGameSizesAsync()
    {
        var generation = ++_loadGeneration;
        GameRows.Clear();
        _selectedIndex = -1;
        TxtTotalGamesSize.Text = "";

        // v0.18 (2ª leva): a biblioteca inteira, não só a aba de filtro aberta
        // agora (antes um jogo fora da aba atual simplesmente não aparecia).
        var library = _vm?.AllLibraryGames ?? (IReadOnlyList<GameEntry>)Array.Empty<GameEntry>();

        var rows = new List<GameStorageRowViewModel>();
        foreach (var g in library)
        {
            if (g.IsApplication || g.IsOwnedNotInstalled) continue;

            string? folder = null;
            var isExeFolder = false;
            if (!string.IsNullOrWhiteSpace(g.InstallDirectory) && Directory.Exists(g.InstallDirectory))
            {
                folder = g.InstallDirectory;
            }
            else if (g.Source == GameSource.Manual && !string.IsNullOrWhiteSpace(g.ExecutablePath))
            {
                // Jogo adicionado à mão nunca tem InstallDirectory — usa a pasta
                // do .exe (é ela que o "mover pelo launcher" leva).
                var resolved = GameMover.ResolveManualGameFolder(g);
                if (resolved is not null && Directory.Exists(resolved)) { folder = resolved; isExeFolder = true; }
            }
            if (folder is null) continue;

            var plan = GameMover.Plan(g, library);
            rows.Add(new GameStorageRowViewModel
            {
                Title = g.Title,
                InstallDirectory = folder,
                SourceLabel = g.Source.ToString(),
                Game = g,
                MoveLabel = plan.ShortLabel,
                IsExecutableFolder = isExeFolder,
            });
        }

        if (rows.Count == 0)
        {
            TxtNoGames.IsVisible = true;
            return;
        }
        TxtNoGames.IsVisible = false;
        foreach (var row in rows) GameRows.Add(row);

        _ = Task.Run(() =>
        {
            foreach (var row in rows)
            {
                if (generation != _loadGeneration) return; // lista recarregada (ex: depois de mover)
                long bytes;
                try { bytes = DiskUsage.GetDirectorySizeBytes(row.InstallDirectory!); }
                catch (Exception ex)
                {
                    DebugLog.Write($"[ARMAZENAMENTO] Falha ao medir '{row.Title}': {ex.Message}");
                    bytes = 0;
                }

                row.SizeBytes = bytes;
                var text = DiskUsage.FormatSize(bytes);
                Dispatcher.UIThread.Post(() => row.SizeText = text);
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _loadGeneration) return;
                var selected = _selectedIndex >= 0 && _selectedIndex < GameRows.Count ? GameRows[_selectedIndex] : null;
                var ordered = GameRows.OrderByDescending(r => r.SizeBytes).ToList();
                GameRows.Clear();
                foreach (var row in ordered) GameRows.Add(row);
                if (selected is not null) SelectRow(GameRows.IndexOf(selected));

                var totalGb = ordered.Sum(r => r.SizeBytes) / 1024.0 / 1024.0 / 1024.0;
                TxtTotalGamesSize.Text = $"{totalGb:F1} GB no total";
            });
        });
    }
}

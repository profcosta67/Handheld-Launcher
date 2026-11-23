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

/// <summary>v0.28: menu de contexto do jogo, menu global, energia e "Sobre". Separado do MainViewModel.cs.</summary>
public sealed partial class MainViewModel
{
    public void ToggleContextMenu()
    {
        if (IsContextMenuOpen) { CloseContextMenu(); return; }
        if (SelectedTile is null) return;
        ContextMenuTarget = SelectedTile;
        _contextMenuIndex = 0;
        BuildContextMenu(SelectedTile);
        UpdateContextMenuSelection();
        IsContextMenuOpen = true;
    }

    private void BuildContextMenu(GameTileViewModel target)
    {
        ContextMenuItems.Clear(); _contextMenuActions.Clear();
        void Add(string label, Action action) { ContextMenuItems.Add(new ContextMenuItemViewModel(label)); _contextMenuActions.Add(action); }
        var game = target.Game;
        if (game.IsOwnedNotInstalled) { Add("📥 Instalar", () => OpenGameFromMenu(game)); return; }
        if (IsCloudEntry(game)) { Add("☁ Jogar na nuvem", () => { CloseContextMenu(); OpenCloudEntry(game); }); return; }

        Add("▶ Abrir jogo", () => OpenGameFromMenu(game));
        if (HasGameDetails(game)) Add("ℹ Detalhes do jogo", () => OpenGameDetails(game));
        Add(game.IsFavorite ? "💔 Desfavoritar" : "⭐ Favoritar", () => ToggleFavorite(game));
        Add("✏ Editar", () => EditGameRequested?.Invoke(this, game));

        if (game.Source == GameSource.Steam) { if (game.SteamAppId is { } appId) Add("🗑 Excluir jogo", () => RequestDeleteSteamGame(game, appId)); }
        else if (game.Id.StartsWith("xboxfolder_", StringComparison.Ordinal)) Add("🗑 Excluir jogo", () => RequestDeleteXboxGame(game));
        else Add("🗑 Excluir atalho", () => RequestDeleteShortcut(game));

        Add("🎨 Personalizar", () => OpenPersonalizeMenu(game));
    }

    private void OpenPersonalizeMenu(GameEntry game)
    {
        var tile = Games.FirstOrDefault(t => t.Game.Id == game.Id);
        if (tile is null) return;
        ContextMenuTarget = tile; ContextMenuItems.Clear(); _contextMenuActions.Clear();
        void Add(string label, Action action) { ContextMenuItems.Add(new ContextMenuItemViewModel(label)); _contextMenuActions.Add(action); }

        Add("🖼 Capa", () => _ = OpenCoverPicker(game, ArtworkPickerKind.Grid));
        Add("🖼 Capa larga", () => _ = OpenCoverPicker(game, ArtworkPickerKind.WideGrid));
        Add("🖼 Banner", () => _ = OpenCoverPicker(game, ArtworkPickerKind.Hero));
        Add("🖼 Logo", () => _ = OpenCoverPicker(game, ArtworkPickerKind.Logo));
        Add("🖼 Ícone", () => _ = OpenCoverPicker(game, ArtworkPickerKind.Icon));
        Add("↩ Voltar", () => ReopenContextMenu(tile));
        _contextMenuIndex = 0; UpdateContextMenuSelection(); IsContextMenuOpen = true;
    }

    private void ReopenContextMenu(GameTileViewModel tile) { ContextMenuTarget = tile; _contextMenuIndex = 0; BuildContextMenu(tile); UpdateContextMenuSelection(); IsContextMenuOpen = true; }
    private void ToggleFavorite(GameEntry game) { _library.SetFavorite(game.Id, !game.IsFavorite); RebuildGamesFromLibrary(); }

    private void OpenGameFromMenu(GameEntry game)
    {
        var tile = Games.FirstOrDefault(t => t.Game.Id == game.Id);
        if (tile is null) return;
        SelectedIndex = Games.IndexOf(tile);
        _ = LaunchSelected();
    }

    private void OpenConfirmMenu(GameEntry game, string confirmLabel, Action onConfirm)
    {
        ContextMenuTarget = Games.FirstOrDefault(t => t.Game.Id == game.Id); ContextMenuItems.Clear(); _contextMenuActions.Clear();
        void Add(string label, Action action, bool isDanger = false) { ContextMenuItems.Add(new ContextMenuItemViewModel(label, isDanger)); _contextMenuActions.Add(action); }
        Add("❌ Cancelar", () => { }); Add(confirmLabel, onConfirm, isDanger: true);
        _contextMenuIndex = 0; UpdateContextMenuSelection(); IsContextMenuOpen = true;
    }

    private void RequestDeleteShortcut(GameEntry game) { OpenConfirmMenu(game, "🗑 Sim, excluir atalho", () => { _library.RemoveManualGame(game.Id); Refresh(); }); }
    private void RequestDeleteSteamGame(GameEntry game, uint appId) { OpenConfirmMenu(game, "🗑 Sim, abrir desinstalador da Steam", () => _launcher.OpenSteamUninstaller(appId)); }
    private void RequestDeleteXboxGame(GameEntry game) { if (!OperatingSystem.IsWindows()) return; OpenConfirmMenu(game, "🗑 Sim, abrir desinstalador do Windows", () => _launcher.OpenWindowsAppUninstaller(game.InstallDirectory)); }

    public void CloseContextMenu() { IsContextMenuOpen = false; ContextMenuTarget = null; }

    public void MoveContextMenuSelection(int delta)
    {
        if (!IsContextMenuOpen || ContextMenuItems.Count == 0) return;
        _contextMenuIndex = ((_contextMenuIndex + delta) % ContextMenuItems.Count + ContextMenuItems.Count) % ContextMenuItems.Count;
        UpdateContextMenuSelection();
    }

    public void ConfirmContextMenuSelection()
    {
        if (!IsContextMenuOpen || ContextMenuTarget is null) { CloseContextMenu(); return; }
        var index = _contextMenuIndex;
        CloseContextMenu();
        if (index >= 0 && index < _contextMenuActions.Count) _contextMenuActions[index]();
    }

    public void HighlightContextMenuItem(ContextMenuItemViewModel item) { var index = ContextMenuItems.IndexOf(item); if (index < 0) return; _contextMenuIndex = index; UpdateContextMenuSelection(); }
    public void ActivateContextMenuItem(ContextMenuItemViewModel item) { HighlightContextMenuItem(item); if (ContextMenuItems.IndexOf(item) >= 0) ConfirmContextMenuSelection(); }
    public void HighlightGlobalMenuItem(ContextMenuItemViewModel item) { var index = GlobalMenuItems.IndexOf(item); if (index < 0) return; _globalMenuIndex = index; UpdateGlobalMenuSelection(); }

    // Resolve o item clicado pelo próprio índice em vez de delegar para
    // ConfirmGlobalMenuSelection(), que lê o _globalMenuIndex compartilhado
    // com teclado/gamepad — esse índice pode mudar entre o clique e o
    // despacho por reentrância de hover do ponteiro (branch fix/power-menu-dispatch).
    public void ActivateGlobalMenuItem(ContextMenuItemViewModel item)
    {
        var index = GlobalMenuItems.IndexOf(item);
        if (index < 0 || index >= _globalMenuActions.Count || index >= _globalMenuNavigates.Count)
        {
            DebugLog.Write($"Global menu: ignored stale pointer item '{item.Label}' on page {_globalMenuPage}.");
            return;
        }

        _globalMenuIndex = index;
        UpdateGlobalMenuSelection();
        var action = _globalMenuActions[index];
        var navigates = _globalMenuNavigates[index];
        if (!navigates) CloseGlobalMenu();
        action();
    }

    public void ActivateCoverCandidate(ArtworkCandidateViewModel candidate)
    {
        var index = CoverPickerCandidates.IndexOf(candidate);
        if (index < 0) return;
        _coverPickerIndex = index; UpdateCoverPickerSelection(); _ = ConfirmCoverPickerSelection();
    }

    private void UpdateContextMenuSelection() { for (var i = 0; i < ContextMenuItems.Count; i++) ContextMenuItems[i].IsSelected = i == _contextMenuIndex; }

    private enum GlobalMenuPage { Root, Library, Hud, Updates, Help, Power, ConfirmPower }
    private GlobalMenuPage _globalMenuPage = GlobalMenuPage.Root;
    private string _pendingPowerConfirmLabel = string.Empty;
    private Action? _pendingPowerAction;

    private void RequestPowerConfirm(string lowercaseLabel, Action action)
    {
        _pendingPowerConfirmLabel = lowercaseLabel; _pendingPowerAction = action;
        NavigateGlobalMenu(GlobalMenuPage.ConfirmPower); IsGlobalMenuOpen = true;
    }

    private readonly List<bool> _globalMenuNavigates = new();

    private string _globalMenuTitle = "Menu";
    public string GlobalMenuTitle { get => _globalMenuTitle; private set => SetField(ref _globalMenuTitle, value); }

    private string _globalMenuHint = string.Empty;
    public string GlobalMenuHint { get => _globalMenuHint; private set => SetField(ref _globalMenuHint, value); }
    public bool HasGlobalMenuHint => !string.IsNullOrEmpty(_globalMenuHint);

    public void ToggleGlobalMenu() { if (IsGlobalMenuOpen) { CloseGlobalMenu(); return; } OpenGlobalMenuAt(GlobalMenuPage.Root); }

    private void OpenGlobalMenuAt(GlobalMenuPage page)
    {
        _globalMenuPage = page; BuildGlobalMenu();
        if (GlobalMenuItems.Count == 0) return;
        _globalMenuIndex = 0; UpdateGlobalMenuSelection(); IsGlobalMenuOpen = true;
    }

    private void NavigateGlobalMenu(GlobalMenuPage page) { _globalMenuPage = page; BuildGlobalMenu(); _globalMenuIndex = 0; UpdateGlobalMenuSelection(); }
    public void CloseGlobalMenu() => IsGlobalMenuOpen = false;

    public void GlobalMenuBack()
    {
        if (_globalMenuPage != GlobalMenuPage.Root) { NavigateGlobalMenu(GlobalMenuPage.Root); return; }
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
        if (_globalMenuNavigates[index]) { _globalMenuActions[index](); return; }
        CloseGlobalMenu(); _globalMenuActions[index]();
    }

    private void UpdateGlobalMenuSelection() { for (var i = 0; i < GlobalMenuItems.Count; i++) GlobalMenuItems[i].IsSelected = i == _globalMenuIndex; }

    private void BuildGlobalMenu()
    {
        GlobalMenuItems.Clear(); _globalMenuActions.Clear(); _globalMenuNavigates.Clear();

        void Add(string label, Action action, bool isDanger = false) { GlobalMenuItems.Add(new ContextMenuItemViewModel(label, isDanger)); _globalMenuActions.Add(action); _globalMenuNavigates.Add(false); }
        void Folder(string label, GlobalMenuPage page) { GlobalMenuItems.Add(new ContextMenuItemViewModel(label, isFolder: true)); _globalMenuActions.Add(() => NavigateGlobalMenu(page)); _globalMenuNavigates.Add(true); }

        switch (_globalMenuPage)
        {
            case GlobalMenuPage.Library:
                GlobalMenuTitle = "Biblioteca"; Add("🔄 Recarregar jogos", Refresh);
                if (IsFetchingArtwork) Add("⏳ Baixando capas…", () => { });
                else if (!_artwork.HasApiKey) Add("🖼 Capas (falta a chave)", () => SettingsRequested?.Invoke(this, EventArgs.Empty));
                else Add("🖼 Baixar capas", () =>
                {
                    DebugLog.Write($"[SGDB] MENU: 'Atualizar Capas Automático' clicado. HasApiKey={_artwork.HasApiKey}, IsFetchingArtwork={IsFetchingArtwork}, Games={_library.Games.Count}");
                    FetchMissingArtwork();
                });
                Add("➕ Adicionar jogo", () => AddGameRequested?.Invoke(this, EventArgs.Empty));
                Add("⬇ Downloads", ToggleDownloads); break;

            case GlobalMenuPage.Hud:
                GlobalMenuTitle = "HUD";
                if (_hud is not null && OperatingSystem.IsWindows())
                {
                    for (var i = 0; i < PerformanceHud.PresetNames.Length; i++)
                    {
                        var preset = i; var marker = _hud.Preset == preset ? "●" : "○";
                        Add($"{marker}  {PerformanceHud.PresetNames[preset]}", () => SetOverlayPreset(preset));
                    }
                }
                if (_performanceOverlay.IsInstalled) Add(_performanceOverlay.IsRunning ? "⚙ RTSS: rodando" : "⚙ RTSS: iniciar", () => { if (!_performanceOverlay.IsRunning) _performanceOverlay.EnsureRunning(); });
                else Add("⬇ RTSS: baixar (necessário pro HUD)", _performanceOverlay.OpenDownloadPage);
                break;

            case GlobalMenuPage.Updates:
                GlobalMenuTitle = "Atualizações";
                if (_updates is { IsInstalled: true } && !IsUpdateReady) Add("🔄 Verificar launcher", () => _ = CheckForUpdatesAsync(userInitiated: true));
                Add("🪟 Windows e drivers", () => SystemUpdateRequested?.Invoke(this, EventArgs.Empty));
                Add("🛠 Driver manual (.inf)", () => DriverUpdateRequested?.Invoke(this, EventArgs.Empty));
                break;

            case GlobalMenuPage.Help:
                GlobalMenuTitle = "Ajuda";
                Add("🛟 Central de Ajuda", () => HelpCenterRequested?.Invoke(this, EventArgs.Empty));
                Add("⌨ Atalhos", OpenShortcutsHelp);
                Add("ℹ Sobre", OpenAbout);
                break;

            case GlobalMenuPage.Power:
                GlobalMenuTitle = "Energia"; Add("🌙 Desligar tela", () => TurnOffScreenRequested?.Invoke(this, EventArgs.Empty));
                Add("🔄 Reiniciar", () => RequestPowerConfirm("reiniciar o aparelho", () => RestartDeviceRequested?.Invoke(this, EventArgs.Empty)));
                Add("⏻ Desligar", () => RequestPowerConfirm("desligar o aparelho", () => ShutdownDeviceRequested?.Invoke(this, EventArgs.Empty)));
                Add("🔁 Reiniciar Launcher", () => RestartLauncherRequested?.Invoke(this, EventArgs.Empty));
                Add("🚪 Fechar launcher", () => QuitRequested?.Invoke(this, EventArgs.Empty)); break;

            case GlobalMenuPage.ConfirmPower:
                GlobalMenuTitle = "Confirmar?"; Folder("❌ Cancelar", GlobalMenuPage.Power); Add($"⚠ Sim, {_pendingPowerConfirmLabel}", () => _pendingPowerAction?.Invoke(), isDanger: true); break;

            default:
                GlobalMenuTitle = "Menu"; Add("🎮 Quick Settings", () => OpenOverlayRequested?.Invoke(this, EventArgs.Empty));
                if (IsUpdateReady) Add(RunningGame is null ? "⬆ Atualizar agora" : "⬆ Atualização pronta (feche o jogo)", ApplyUpdate);
                if (RunningGame is not null) Add("⏹ Fechar o jogo", CloseRunningGame);
                Folder("📚 Biblioteca", GlobalMenuPage.Library); Folder("📊 HUD", GlobalMenuPage.Hud);
                // v0.17 (05/11, pedido do usuário): "Layout da tela" virou "TEMAS" e
                // subiu pro menu raiz (antes só aparecia dentro de Configurações); e
                // "Configurações" deixou de ser uma pasta com um único item de
                // verdade (Preferências) — agora é ação direta, economiza um clique.
                // v0.18 (pedido do usuário, 05/11): "TEMAS" (maiúsculo, plural) virou
                // "Tema" — mais curto e consistente com o resto dos itens do menu raiz.
                Add("🎨 Tema", () => LayoutPickerRequested?.Invoke(this, EventArgs.Empty));
                Add("⚙ Configurações", () => SettingsRequested?.Invoke(this, EventArgs.Empty));
                // v0.18 (pedido do usuário, 05/11): Gerenciador de armazenamento e
                // Saúde do aparelho, direto no menu raiz junto de TEMAS/Configurações
                // — são "sobre o aparelho", não pertencem dentro de outra pasta.
                Add("💾 Armazenamento", () => StorageManagerRequested?.Invoke(this, EventArgs.Empty));
                // v0.18: contador de alertas ainda não vistos (zera ao abrir a tela).
                var unseenAlerts = HealthAlerts.UnseenCount;
                Add(unseenAlerts > 0 ? $"🌡 Saúde do Aparelho ({unseenAlerts} {(unseenAlerts == 1 ? "alerta" : "alertas")})" : "🌡 Saúde do Aparelho",
                    () => DeviceHealthRequested?.Invoke(this, EventArgs.Empty));
                // v0.18: o atalho "☁ Xbox Cloud Gaming" saiu daqui (pedido do usuário: "tira
                // ele do menu") e virou a aba "Nuvem" da biblioteca. Aqui só aparece o
                // "Fechar", e só enquanto o xCloud aberto pelo launcher estiver rodando.
                if (XCloudLauncher.IsRunning) Add("⏹ Fechar Xbox Cloud Gaming", () => _ = Task.Run(XCloudLauncher.CloseAll));
                Folder("🔄 Atualizações", GlobalMenuPage.Updates);
                Folder("🛟 Ajuda", GlobalMenuPage.Help); Folder("⚡ Energia", GlobalMenuPage.Power);
                break;
        }
        GlobalMenuHint = _globalMenuPage switch
        {
            GlobalMenuPage.Hud => "No jogo: L3+R3 (ou Ctrl+Alt+O) troca o HUD",
            GlobalMenuPage.Updates when _updates is { IsInstalled: true } => $"Versão atual: v{_updates.CurrentVersionText}",
            GlobalMenuPage.Root when IsUpdateReady => $"Nova versão pronta: v{PendingUpdateVersion}",
            GlobalMenuPage.Root when RunningGame is not null => $"Jogando: {RunningGame.Title}",
            _ => string.Empty,
        };
        OnPropertyChanged(nameof(HasGlobalMenuHint));
    }

    private bool _isAboutOpen;
    public bool IsAboutOpen { get => _isAboutOpen; private set => SetField(ref _isAboutOpen, value); }
    public void OpenAbout() => IsAboutOpen = true;
    public void CloseAbout() => IsAboutOpen = false;

    public string AboutVersionText
    {
        get
        {
            var full = AppVersionText; var start = full.IndexOf('v'); var end = full.IndexOf(' ', Math.Max(start, 0));
            return start >= 0 ? "Versão " + (end > start ? full[(start + 1)..end] : full[(start + 1)..]) : full;
        }
    }

    public void TogglePowerMenu() { if (IsGlobalMenuOpen && _globalMenuPage == GlobalMenuPage.Power) { CloseGlobalMenu(); return; } OpenGlobalMenuAt(GlobalMenuPage.Power); }
}

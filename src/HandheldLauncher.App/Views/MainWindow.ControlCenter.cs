using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Input;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;
using HandheldLauncher.Services;

namespace HandheldLauncher.App.Views;

// Central de Controle (tecla Y / ícone "📶 Rede" da barra de cima): Wi-Fi,
// Bluetooth, Syncthing e Downloads no mesmo painel, navegável por controle,
// teclado, mouse e toque.
public partial class MainWindow : Window
{
    private bool _isControlCenterOpen;
    private readonly RadioService _radioService = new();
    private readonly WifiService _wifiService = new();
    private readonly BluetoothService _btService = new();

    // Volume/Brilho/TDP da Central de Controle: mesma implementação por
    // plataforma que o OverlayWindow usa (Windows nativo, Linux via
    // pactl/brightnessctl, Mock nas restantes).
    //
    // Criado só à primeira utilização: o construtor do WindowsHardwareService
    // corre o DeviceDetector, e o launcher não deve pagar isso no arranque por
    // causa de um painel que pode nunca ser aberto.
    private IHardwareService? _hardwareService;

    private IHardwareService Hardware => _hardwareService ??= CreateHardwareService();

    /// <summary>Sliders em modo "preencher a partir das definições" não devem gravar nada.</summary>
    private bool _suppressControlCenterSliders;

    private bool _nightLightOn;
    private bool _darkModeOn;

    // Gravar as definições a cada tick do slider martelava o disco: aplica-se o
    // valor na hora ao hardware e só se persiste quando o dedo para.
    private DispatcherTimer? _settingsSaveTimer;

    private static IHardwareService CreateHardwareService()
    {
        if (OperatingSystem.IsWindows()) return new WindowsHardwareService();
        if (OperatingSystem.IsLinux()) return new LinuxHardwareService();
        return new MockHardwareService();
    }

    // Alvo de foco com posição na "grade" do painel: linhas 0 e 1 = os seis
    // tiles (3×2), linha 2 = Sync/Ver Fila/Fechar, linha 3+ = linhas das listas
    // (coluna 1 = Wi-Fi, coluna 2 = Bluetooth).
    private readonly record struct ControlCenterTarget(Control Control, int Row, int Column);

    private readonly List<ControlCenterTarget> _controlCenterTargets = new();
    private ControlCenterTarget? _controlCenterCurrent;
    private bool _controlCenterBusy;
    private DispatcherTimer? _controlCenterRefreshTimer;

    public void ToggleControlCenter()
    {
        if (_isControlCenterOpen) HideControlCenter();
        else ShowControlCenter();
    }

    private async void ShowControlCenter()
    {
        if (this.FindControl<Panel>("ControlCenterOverlay") is not { } overlay) return;
        if (this.FindControl<Border>("ControlCenterCard") is not { } card) return;

        overlay.IsVisible = true;
        _isControlCenterOpen = true;
        _controlCenterCurrent = null;

        await Task.Delay(10);
        card.Opacity = 1;
        card.RenderTransform = TransformOperations.Parse("translateY(0px)");
        DebugLog.Write("[CONTROL CENTER] Aberto.");

        LoadControlCenterSliders();
        UpdateExtraTilesVisuals();

        await UpdateRadioVisualsAsync();
        await LoadNetworksAndDevicesAsync(discoverBluetooth: true);

        // Cursor visível desde o início — senão a primeira seta parece "sumir"
        // (com _controlCenterCurrent nulo, isto foca o primeiro tile).
        MoveControlCenterFocus(NavigationDirection.Down);

        // O BringIntoView do foco rolava o painel e cortava o cabeçalho
        // ("Configurações rápidas" aparecia a meio do cartão). Volta ao topo.
        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<ScrollViewer>("ControlCenterScroll") is { } scroll)
                scroll.Offset = default;
        }, DispatcherPriority.Background);

        // O estado muda por fora (o Windows reconecta sozinho, o rádio desliga),
        // então o painel se atualiza enquanto estiver aberto.
        _controlCenterRefreshTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _controlCenterRefreshTimer.Tick -= OnControlCenterRefreshTick;
        _controlCenterRefreshTimer.Tick += OnControlCenterRefreshTick;
        _controlCenterRefreshTimer.Start();
    }

    private async void OnControlCenterRefreshTick(object? sender, EventArgs e)
    {
        if (!_isControlCenterOpen || _controlCenterBusy) return;
        ApplyArtworkTile();
        await UpdateRadioVisualsAsync();
        await LoadNetworksAndDevicesAsync(keepFocus: true);
    }

    /// <summary>X no painel: relê rádios e listas na hora.</summary>
    private async Task RefreshControlCenterAsync()
    {
        if (_controlCenterBusy) return;

        _controlCenterBusy = true;
        try
        {
            SetControlCenterFeedback("Atualizando…");
            await UpdateRadioVisualsAsync();
            await LoadNetworksAndDevicesAsync(keepFocus: true, discoverBluetooth: true);
            SetControlCenterFeedback(string.Empty);
        }
        finally
        {
            _controlCenterBusy = false;
        }
    }

    private async void HideControlCenter()
    {
        if (this.FindControl<Panel>("ControlCenterOverlay") is not { } overlay) return;
        if (this.FindControl<Border>("ControlCenterCard") is not { } card) return;

        _controlCenterRefreshTimer?.Stop();

        // Solta o foco ANTES de esconder: com algo focado dentro do painel, o
        // Avalonia joga o foco para o primeiro controle da janela (o botão
        // "Rede" da barra de cima) e a borda de foco fica pendurada lá.
        TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
        foreach (var target in _controlCenterTargets) target.Control.Classes.Remove("focused");
        _controlCenterTargets.Clear();
        _controlCenterCurrent = null;

        card.Opacity = 0;
        card.RenderTransform = TransformOperations.Parse("translateY(20px)");
        _isControlCenterOpen = false;

        await Task.Delay(200);
        if (!_isControlCenterOpen) overlay.IsVisible = false;
        DebugLog.Write("[CONTROL CENTER] Fechado.");
    }

    // ---------------------------------------------------------------------
    // Navegação (controle/teclado). O OnGamepadTick chama isto e para de
    // mandar ações para a biblioteca enquanto o painel está aberto.
    // ---------------------------------------------------------------------
    public bool HandleControlCenterGamepad(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.Back:
            case GamepadAction.Menu:
            case GamepadAction.Connectivity:
                HideControlCenter();
                return true;

            case GamepadAction.MoveUp:
                MoveControlCenterFocus(NavigationDirection.Up);
                return true;

            case GamepadAction.MoveDown:
                MoveControlCenterFocus(NavigationDirection.Down);
                return true;

            case GamepadAction.MoveLeft:
                MoveControlCenterFocus(NavigationDirection.Left);
                return true;

            case GamepadAction.MoveRight:
                MoveControlCenterFocus(NavigationDirection.Right);
                return true;

            case GamepadAction.Confirm:
                ActivateControlCenterFocus();
                return true;

            case GamepadAction.GameDetails: // X: força atualizar as listas
                _ = RefreshControlCenterAsync();
                return true;
        }

        return true; // painel aberto consome qualquer outra ação
    }

    // ---------- foco / ordem de navegação ----------

    // Navegação ESPACIAL (era uma lista plana: com o foco nos tiles, "direita/
    // esquerda" circulava pelos 4 tiles e o A acabava ligando o Modo Avião).
    private void MoveControlCenterFocus(NavigationDirection direction)
    {
        EnsureControlCenterTargets();
        if (_controlCenterTargets.Count == 0) return;

        if (_controlCenterCurrent is not { } current)
        {
            FocusControlCenterTarget(_controlCenterTargets[0]);
            return;
        }

        // Dos tiles desce-se direto para a lista da MESMA coluna (Wi-Fi → redes,
        // Bluetooth → aparelhos) em vez de cair no "Ver Fila"/"Fechar" — era o
        // "no Bluetooth clico para baixo e ele vai para o X".
        var next = direction switch
        {
            NavigationDirection.Left => FindInSameRow(current, -1),
            NavigationDirection.Right => FindInSameRow(current, +1),
            NavigationDirection.Down when current.Row <= 1
                => FindInColumnFrom(3, current.Column) ?? FindInAdjacentRow(current, +1),
            NavigationDirection.Up when current.Row >= 3
                => FindInColumnFrom(0, current.Column) ?? FindInAdjacentRow(current, -1),
            NavigationDirection.Up => FindInAdjacentRow(current, -1),
            _ => FindInAdjacentRow(current, +1),
        };

        if (next is { } target) FocusControlCenterTarget(target);
    }

    // ←/→ andam dentro da mesma linha (os 4 tiles, ou a própria lista).
    private ControlCenterTarget? FindInSameRow(ControlCenterTarget current, int step)
    {
        var row = _controlCenterTargets
            .Where(t => t.Row == current.Row)
            .OrderBy(t => t.Column)
            .ToList();

        var index = row.FindIndex(t => t.Column == current.Column);
        if (index < 0) return null;

        var next = index + step;
        return next >= 0 && next < row.Count ? row[next] : null;
    }

    // ↑/↓ vão para a linha vizinha que tenha itens, na coluna mais próxima.
    private ControlCenterTarget? FindInAdjacentRow(ControlCenterTarget current, int step)
    {
        var rows = _controlCenterTargets.Select(t => t.Row).Distinct().OrderBy(r => r).ToList();
        var index = rows.IndexOf(current.Row);
        if (index < 0) return null;

        for (var i = index + step; i >= 0 && i < rows.Count; i += step)
        {
            var candidates = _controlCenterTargets.Where(t => t.Row == rows[i]).ToList();
            if (candidates.Count > 0)
                return candidates.OrderBy(t => Math.Abs(t.Column - current.Column)).First();
        }

        return null;
    }

    // Primeiro item da coluna a partir de uma linha mínima (salto tile ↔ lista).
    private ControlCenterTarget? FindInColumnFrom(int minRow, int column)
    {
        foreach (var target in _controlCenterTargets.OrderBy(t => t.Row))
        {
            if (target.Row >= minRow && target.Column == column) return target;
        }

        return null;
    }

    private void FocusControlCenterTarget(ControlCenterTarget target)
    {
        _controlCenterCurrent = target;

        foreach (var item in _controlCenterTargets)
        {
            if (item.Equals(target)) item.Control.Classes.Add("focused");
            else item.Control.Classes.Remove("focused");
        }

        target.Control.Focus(NavigationMethod.Directional);
        target.Control.BringIntoView();
    }

    private void ActivateControlCenterFocus()
    {
        EnsureControlCenterTargets();
        if (_controlCenterCurrent is not { } current) return;

        var control = current.Control;
        switch (control)
        {
            case Button button:
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                break;
            case Border { DataContext: WifiNetwork network } row:
                _ = ToggleWifiConnectionAsync(network, row);
                break;
            case Border { DataContext: BtDevice device } row:
                _ = ToggleBluetoothConnectionAsync(device, row);
                break;
        }
    }

    // Toque/mouse também movem o "cursor" do controle para o item tocado.
    private void SyncControlCenterFocus(Control control)
    {
        EnsureControlCenterTargets();
        var target = _controlCenterTargets.FirstOrDefault(t => ReferenceEquals(t.Control, control));
        if (target.Control is not null) FocusControlCenterTarget(target);
    }

    /// <summary>
    /// Monta a "grade" de navegação. É refeita a cada refresh porque as listas
    /// são recriadas: linhas 0 e 1 = os seis tiles (3×2), linha 2 = Sync/Ver
    /// Fila/Fechar, linha 3+ = linhas das listas (coluna 1 = Wi-Fi, 2 = BT).
    /// </summary>
    private void EnsureControlCenterTargets()
    {
        var wifiRows = FindListRows("WifiList");
        var bluetoothRows = FindListRows("BluetoothList");

        var targets = new List<ControlCenterTarget>();

        // Tiles na mesma disposição do UniformGrid do MainWindow.axaml.
        (string Name, int Row, int Column)[] tiles =
        [
            ("BtnArtwork", 0, 0), ("BtnWifi", 0, 1), ("BtnBluetooth", 0, 2),
            ("BtnNightLight", 1, 0), ("BtnDarkMode", 1, 1), ("BtnAirplane", 1, 2),
        ];

        foreach (var (name, row, column) in tiles)
        {
            if (this.FindControl<Control>(name) is { } tile)
                targets.Add(new ControlCenterTarget(tile, row, column));
        }

        if (this.FindControl<Control>("BtnSync") is { } sync)
            targets.Add(new ControlCenterTarget(sync, 2, 0));

        if (this.FindControl<Control>("BtnShowDownloads") is { } downloads)
            targets.Add(new ControlCenterTarget(downloads, 2, 1));

        if (this.FindControl<Control>("BtnCloseControlCenter") is { } close)
            targets.Add(new ControlCenterTarget(close, 2, 2));

        // Colunas alinhadas com os tiles de cima: Wi-Fi = 1, Bluetooth = 2 —
        // assim "descer" do tile cai na lista certa (ver MoveControlCenterFocus).
        for (var i = 0; i < wifiRows.Count; i++)
            targets.Add(new ControlCenterTarget(wifiRows[i], 3 + i, 1));

        for (var i = 0; i < bluetoothRows.Count; i++)
            targets.Add(new ControlCenterTarget(bluetoothRows[i], 3 + i, 2));

        _controlCenterTargets.Clear();
        _controlCenterTargets.AddRange(targets);

        // As linhas das listas são Borders (só pintura, ver MainWindow.axaml):
        // viram focáveis aqui para o controle/teclado conseguirem percorrê-las.
        foreach (var target in _controlCenterTargets) target.Control.Focusable = true;

        // Cursor: se a posição lógica ainda existe depois de recriar as listas,
        // devolve o destaque para ela; senão começa do zero.
        if (_controlCenterCurrent is not { } current) return;

        var same = _controlCenterTargets.FirstOrDefault(t => t.Row == current.Row && t.Column == current.Column);
        if (same.Control is null)
        {
            _controlCenterCurrent = null;
            return;
        }

        _controlCenterCurrent = same;
        foreach (var target in _controlCenterTargets)
        {
            if (target.Equals(same)) target.Control.Classes.Add("focused");
            else target.Control.Classes.Remove("focused");
        }
    }

    private List<Control> FindListRows(string listName)
    {
        if (this.FindControl<ItemsControl>(listName) is not { } list) return new List<Control>();

        return list.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("listItem"))
            .Cast<Control>()
            .ToList();
    }

    // ---------- carga das listas ----------

    /// <summary>v0.26: lista vazia não ocupa espaço (antes ficava um quadro em branco).</summary>
    private void ShowListCardIfAny(string cardName, System.Collections.IEnumerable? items)
    {
        if (this.FindControl<Border>(cardName) is not { } card) return;
        var any = false;
        if (items is not null) foreach (var _ in items) { any = true; break; }
        card.IsVisible = any;
    }

    private async Task LoadNetworksAndDevicesAsync(bool keepFocus = false, bool discoverBluetooth = false)
    {
        try
        {
            // Sem keepFocus (abertura/refresh do rádio) o cursor volta ao início.
            if (!keepFocus) _controlCenterCurrent = null;

            if (this.FindControl<ItemsControl>("WifiList") is { } wifiList)
            {
                wifiList.ItemsSource = await _wifiService.GetAvailableNetworksAsync();
                ShowListCardIfAny("WifiListCard", wifiList.ItemsSource);
            }

            if (this.FindControl<ItemsControl>("BluetoothList") is { } btList)
            {
                // Descoberta ativa só ao abrir/atualizar: varrer o rádio a cada
                // 8 s deixaria o Bluetooth lento e gastaria bateria.
                var window = discoverBluetooth ? TimeSpan.FromSeconds(6) : TimeSpan.Zero;
                btList.ItemsSource = await _btService.GetDevicesAsync(window);
                ShowListCardIfAny("BluetoothListCard", btList.ItemsSource);
            }

            // Os itens foram recriados: refaz a grade e devolve o cursor.
            Dispatcher.UIThread.Post(EnsureControlCenterTargets, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[CONTROL CENTER] Erro ao carregar listas: {ex.Message}");
        }
    }

    // ---------- eventos ----------

    private void OnControlCenterClick(object? sender, RoutedEventArgs e) => ToggleControlCenter();

    private void OnCloseControlCenter(object? sender, RoutedEventArgs e) => HideControlCenter();

    private async void OnToggleAirplaneMode(object? sender, RoutedEventArgs e)
    {
        DebugLog.Write("[CONTROL CENTER] Alternando Modo Avião...");
        await _radioService.ToggleAirplaneModeAsync();
        await UpdateRadioVisualsAsync();
        await LoadNetworksAndDevicesAsync();
    }

    private async void OnToggleWifi(object? sender, RoutedEventArgs e)
    {
        DebugLog.Write("[CONTROL CENTER] Alternando Wi-Fi...");
        var isOn = await _radioService.IsRadioOnAsync(RadioKind.WiFi);
        await _radioService.SetRadioStateAsync(RadioKind.WiFi, !isOn);
        await UpdateRadioVisualsAsync();
        await LoadNetworksAndDevicesAsync();   // sem rádio não há redes para listar
    }

    private async void OnToggleBluetooth(object? sender, RoutedEventArgs e)
    {
        DebugLog.Write("[CONTROL CENTER] Alternando Bluetooth...");
        var isOn = await _radioService.IsRadioOnAsync(RadioKind.Bluetooth);
        await _radioService.SetRadioStateAsync(RadioKind.Bluetooth, !isOn);
        await UpdateRadioVisualsAsync();
        await LoadNetworksAndDevicesAsync();
    }

    private void OnToggleSyncthing(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SyncthingEnabled = !vm.SyncthingEnabled;
            DebugLog.Write($"[CONTROL CENTER] Syncthing alterado para: {vm.SyncthingEnabled}");
            UpdateExtraTilesVisuals();
        }
    }

    private void OnShowDownloadsClick(object? sender, RoutedEventArgs e)
    {
        HideControlCenter();
        if (DataContext is MainViewModel vm) vm.ToggleDownloads();
    }

    // Wi-Fi: toque/mouse (PointerPressed) ou A com o item focado.
    private async void OnWifiNetworkClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: WifiNetwork network } row)
            await ToggleWifiConnectionAsync(network, row);
    }

    // Bluetooth: conectar/desconectar de um dispositivo pareado.
    private async void OnBluetoothDeviceClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: BtDevice device } row)
            await ToggleBluetoothConnectionAsync(device, row);
    }

    private async Task ToggleWifiConnectionAsync(WifiNetwork network, Control source)
    {
        if (_controlCenterBusy) return;

        SyncControlCenterFocus(source);
        _controlCenterBusy = true;
        try
        {
            if (network.IsConnected)
            {
                DebugLog.Write($"[WIFI] Desconectando de {network.Ssid}...");
                await _wifiService.DisconnectAsync();
                SetControlCenterFeedback($"Desconectado de {network.Ssid}.");
            }
            else
            {
                // 1) Tenta direto: rede aberta, ou já com perfil/senha salvos.
                DebugLog.Write($"[WIFI] Conectando em {network.Ssid}...");
                var ok = await _wifiService.ConnectAsync(network.Ssid);

                // 2) Falhou e a rede é protegida → pede a senha e tenta de novo.
                //    Cobre "sem perfil salvo" (InvalidCredential) e também perfil
                //    antigo com senha errada.
                if (!ok && network.IsSecured)
                {
                    _controlCenterBusy = false; // libera o loop enquanto o modal está aberto
                    string? password;
                    try { password = await PromptWifiPasswordAsync(network.Ssid, failed: true); }
                    finally { _controlCenterBusy = true; }

                    if (password is null)
                    {
                        SetControlCenterFeedback($"Conexão em {network.Ssid} cancelada.");
                        return;
                    }

                    DebugLog.Write($"[WIFI] Tentando {network.Ssid} com a senha digitada...");
                    ok = await _wifiService.ConnectAsync(network.Ssid, password);
                }

                SetControlCenterFeedback(ok
                    ? $"Conectado em {network.Ssid}."
                    : $"Não foi possível conectar em {network.Ssid} (senha incorreta?).");
            }

            await Task.Delay(900);   // dá tempo ao Windows para trocar de rede
            await UpdateRadioVisualsAsync();
            await LoadNetworksAndDevicesAsync(keepFocus: true);
        }
        finally
        {
            _controlCenterBusy = false;
        }
    }

    private async Task ToggleBluetoothConnectionAsync(BtDevice device, Control source)
    {
        if (_controlCenterBusy) return;

        SyncControlCenterFocus(source);
        _controlCenterBusy = true;
        try
        {
            var wasConnected = device.IsConnected;
            var ok = wasConnected
                ? await _btService.DisconnectAsync(device)
                : device.IsPaired
                    ? await _btService.ConnectAsync(device)
                    : await _btService.PairAsync(device); // aparelho novo: emparelha

            var verb = wasConnected ? "Desconectar" : device.IsPaired ? "Conectar" : "Emparelhar";
            SetControlCenterFeedback(ok
                ? $"{verb} '{device.Name}': ok."
                : $"{verb} '{device.Name}' falhou.");

            await Task.Delay(900);
            await UpdateRadioVisualsAsync();
            await LoadNetworksAndDevicesAsync(keepFocus: true);
        }
        finally
        {
            _controlCenterBusy = false;
        }
    }

    // ---------- feedback visual ----------

    private void SetControlCenterFeedback(string message)
    {
        if (this.FindControl<TextBlock>("ControlCenterFeedback") is { } text)
            text.Text = message;
    }

    private async Task<string?> PromptWifiPasswordAsync(string ssid, bool failed = false)
    {
        var dialog = new WifiPasswordWindow(ssid, failed) { Topmost = true };
        _activeDialog = dialog; // o enforcer de topmost/foco do launcher olha este campo
        try
        {
            return await dialog.ShowDialog<string?>(this);
        }
        finally
        {
            _activeDialog = null;
        }
    }

    // ---------- volume ----------

    /// <summary>Preenche o slider com o que está guardado (sem aplicar ao hardware).</summary>
    private void LoadControlCenterSliders()
    {
        var settings = AppSettingsStore.Load();
        var volume = Math.Clamp(settings.Volume, 0, 100);

        _suppressControlCenterSliders = true;
        if (this.FindControl<Slider>("VolumeSlider") is { } volumeSlider)
            volumeSlider.Value = volume;
        _suppressControlCenterSliders = false;

        SetControlCenterText("TxtVolumeValue", volume.ToString());
    }

    private void OnVolumeSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        var value = (int)Math.Round(e.NewValue);
        SetControlCenterText("TxtVolumeValue", value.ToString());
        if (_suppressControlCenterSliders) return;

        Hardware.SetVolume(value);
        ScheduleSettingsSave(settings => settings.Volume = value);
    }

    // ---------- atualizar capas ----------

    /// <summary>
    /// "Atualizar Capas": dispara o mesmo comando do menu Biblioteca
    /// (SteamGridDB). O tile fica "ligado" enquanto a busca corre.
    /// </summary>
    private void OnRefreshArtworkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (!vm.CanFetchArtwork)
        {
            ApplyArtworkTile();
            SetControlCenterFeedback(vm.IsFetchingArtwork
                ? "A buscar capas… espera que a fila termine."
                : "Falta a chave do SteamGridDB — configura-a nas Configurações.");
            return;
        }

        vm.FetchArtworkCommand.Execute(null);
        SetControlCenterFeedback("A buscar capas em falta (SteamGridDB)…");
        ApplyArtworkTile();
    }

    private void OnArtworkDetailsClick(object? sender, RoutedEventArgs e) =>
        SetControlCenterFeedback("Procura capas em falta no SteamGridDB (precisa da chave configurada). Mesmo comando do menu Biblioteca.");

    private void ApplyArtworkTile()
    {
        var busy = DataContext is MainViewModel { IsFetchingArtwork: true };
        var canFetch = DataContext is MainViewModel { CanFetchArtwork: true };

        if (this.FindControl<Border>("TileArtwork") is { } tile) tile.Classes.Set("on", busy);
        SetControlCenterText("TxtArtworkState", busy ? "A buscar…" : canFetch ? "Automático" : "Falta a chave");
    }

    // ---------- luz noturna ----------

    private void OnToggleNightLight(object? sender, RoutedEventArgs e)
    {
        _nightLightOn = !_nightLightOn;
        ApplyNightLightTile();
        SetControlCenterFeedback(_nightLightOn
            ? "Luz Noturna ligada: filtro âmbar sobre o launcher."
            : "Luz Noturna desligada.");
    }

    private void OnNightLightDetailsClick(object? sender, RoutedEventArgs e) =>
        SetControlCenterFeedback("A Luz Noturna é um filtro âmbar aplicado só ao launcher — não altera o gamma do sistema.");

    private void ApplyNightLightTile()
    {
        if (this.FindControl<Border>("NightLightFilter") is { } filter) filter.IsVisible = _nightLightOn;
        if (this.FindControl<Border>("TileNightLight") is { } tile) tile.Classes.Set("on", _nightLightOn);
        SetControlCenterText("TxtNightLightState", _nightLightOn ? "Ligada" : "Desligada");
    }

    // ---------- estilo (tema claro/escuro) ----------

    private void OnToggleDarkMode(object? sender, RoutedEventArgs e)
    {
        _darkModeOn = !_darkModeOn;

        // ThemeManager: 0 = "Escuro", 1 = "Claro". O tema escolhido nas
        // Configurações é substituído, por isso também fica guardado.
        var themeIndex = _darkModeOn ? 0 : 1;
        ThemeManager.Apply(themeIndex);
        ScheduleSettingsSave(settings => settings.Theme = themeIndex);

        ApplyDarkModeTile();
        SetControlCenterFeedback(_darkModeOn ? "Estilo Escuro ligado." : "Estilo Escuro desligado (tema Claro).");
    }

    private void OnDarkModeDetailsClick(object? sender, RoutedEventArgs e) =>
        SetControlCenterFeedback($"Estilo atual: {(_darkModeOn ? "Escuro" : "Claro")}. Os restantes temas estão nas Configurações.");

    private void ApplyDarkModeTile()
    {
        if (this.FindControl<Border>("TileDarkMode") is { } tile) tile.Classes.Set("on", _darkModeOn);
        SetControlCenterText("TxtDarkModeState", _darkModeOn ? "Ligado" : "Desligado");
    }

    /// <summary>Estado dos tiles que não dependem dos rádios (capas, sync, luz, tema).</summary>
    private void UpdateExtraTilesVisuals()
    {
        var settings = AppSettingsStore.Load();

        _darkModeOn = !ThemeManager.IsLight(settings.Theme);
        ApplyDarkModeTile();
        ApplyNightLightTile();
        ApplyArtworkTile();

        var syncthingOn = DataContext is MainViewModel { SyncthingEnabled: true };
        if (this.FindControl<Border>("TileSync") is { } syncTile) syncTile.Classes.Set("on", syncthingOn);
        SetControlCenterText("TxtSyncState", syncthingOn ? "Ligado" : "Desligado");
    }

    // ---------- setas "→" dos tiles de conectividade ----------

    private void OnWifiDetailsClick(object? sender, RoutedEventArgs e)
    {
        SetControlCenterFeedback("Escolhe uma rede na lista abaixo (A ou toque liga/desliga).");
        FocusFirstListRow(column: 1);
    }

    private void OnBluetoothDetailsClick(object? sender, RoutedEventArgs e)
    {
        SetControlCenterFeedback("Escolhe um aparelho na lista abaixo (A ou toque liga/desliga).");
        FocusFirstListRow(column: 2);
    }

    private void OnAirplaneDetailsClick(object? sender, RoutedEventArgs e) =>
        SetControlCenterFeedback("O Modo Avião desliga o Wi-Fi e o Bluetooth de uma vez.");

    private void OnSyncDetailsClick(object? sender, RoutedEventArgs e) =>
        SetControlCenterFeedback("Sync liga/desliga a sincronização de estados de jogo (Syncthing).");

    private void FocusFirstListRow(int column)
    {
        EnsureControlCenterTargets();
        var target = _controlCenterTargets.FirstOrDefault(t => t.Row >= 3 && t.Column == column);
        if (target.Control is not null) FocusControlCenterTarget(target);
    }

    // ---------- persistência (com debounce) ----------

    private Action<AppSettings>? _pendingSettingsChange;

    /// <summary>Guarda as definições quando o utilizador para de mexer (evita I/O por tick).</summary>
    private void ScheduleSettingsSave(Action<AppSettings> change)
    {
        _settingsSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Tick -= OnSettingsSaveTick;
        _settingsSaveTimer.Tick += OnSettingsSaveTick;
        _pendingSettingsChange = change;
        _settingsSaveTimer.Start();
    }

    private void OnSettingsSaveTick(object? sender, EventArgs e)
    {
        _settingsSaveTimer?.Stop();
        if (_pendingSettingsChange is not { } change) return;
        _pendingSettingsChange = null;

        try
        {
            var settings = AppSettingsStore.Load();
            change(settings);
            AppSettingsStore.Save(settings);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[CONTROL CENTER] Falha ao guardar definições: {ex.Message}");
        }
    }

    // ---------- estado dos rádios ----------

    private async Task UpdateRadioVisualsAsync()
    {
        var wifiOn = await _radioService.IsRadioOnAsync(RadioKind.WiFi);
        var bluetoothOn = await _radioService.IsRadioOnAsync(RadioKind.Bluetooth);
        var airplaneOn = !wifiOn && !bluetoothOn;

        if (this.FindControl<Border>("TileWifi") is { } wifiTile) wifiTile.Classes.Set("on", wifiOn);
        if (this.FindControl<Border>("TileBluetooth") is { } bluetoothTile) bluetoothTile.Classes.Set("on", bluetoothOn);
        if (this.FindControl<Border>("TileAirplane") is { } airplaneTile) airplaneTile.Classes.Set("on", airplaneOn);

        var wifi = wifiOn ? (await _wifiService.GetCurrentSsidAsync() ?? "ligado, sem rede") : "Desligado";
        SetControlCenterText("TxtWifiState", wifi);
        SetControlCenterText("TxtBluetoothState", bluetoothOn ? "Ligado" : "Desligado");
        SetControlCenterText("TxtAirplaneState", airplaneOn ? "Ligado" : "Desligado");

        if (this.FindControl<TextBlock>("ControlCenterStatus") is { } status)
            status.Text = $"Wi-Fi: {wifi}   ·   Bluetooth: {(bluetoothOn ? "ligado" : "desligado")}";
    }

    private void SetControlCenterText(string name, string text)
    {
        if (this.FindControl<TextBlock>(name) is { } block) block.Text = text;
    }
}

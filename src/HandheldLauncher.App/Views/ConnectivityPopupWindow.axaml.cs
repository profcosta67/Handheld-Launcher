<<<<<<< HEAD
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Input;
using HandheldLauncher.App.Input;
=======
using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
>>>>>>> 6b50390488994743904ad660c78a3cc0a532c2c8

namespace HandheldLauncher.App.Views;

public partial class ConnectivityPopupWindow : Window
{
<<<<<<< HEAD
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    private readonly GamepadPoller _gamepad = new();
    private readonly DispatcherTimer _gamepadTimer;
    private DispatcherTimer? _waitForSettingsTimer;

    public ConnectivityPopupWindow()
    {
        InitializeComponent();
        
        BtnClose.Click += (_, _) => Close();
        BtnOpenWifi.Click += OnOpenNativeWifiClick;
        BtnOpenBluetooth.Click += OnOpenNativeBluetoothClick;
        BtnRefreshBluetooth.Click += OnRefreshBluetoothClick;

        // 🎮 Timer do controle para o popup ser independente
        _gamepadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _gamepadTimer.Tick += (_, _) =>
        {
            if (!IsActive) return;
            foreach (var action in _gamepad.Poll())
                HandleGamepadAction(action);
        };
        _gamepadTimer.Start();
        
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        Opened += async (_, _) =>
        {
            BtnOpenWifi.Focus();
            await RefreshWifiAsync();
            await RefreshBluetoothAsync();
        };
        
        Closed += (_, _) => _gamepadTimer.Stop();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsActive) return;
        GamepadAction? action = e.Key switch
        {
            Key.Up => GamepadAction.MoveUp,
            Key.Down => GamepadAction.MoveDown,
            Key.Left => GamepadAction.MoveLeft,
            Key.Right => GamepadAction.MoveRight,
            Key.Enter or Key.Space => GamepadAction.Confirm,
            Key.Escape => GamepadAction.Back,
            _ => null
        };
        if (action is { } mapped && HandleGamepadAction(mapped))
            e.Handled = true;
    }

    public bool HandleGamepadAction(GamepadAction action)
    {
        var focusManager = TopLevel.GetTopLevel(this)?.FocusManager;
        var current = focusManager?.GetFocusedElement() as Control;

        if (action == GamepadAction.Back || action == GamepadAction.Menu)
        {
            Close();
            return true;
        }

        if (action == GamepadAction.MoveDown)
        {
            if (current == BtnOpenWifi) BtnOpenBluetooth.Focus();
            else if (current == BtnOpenBluetooth) BtnRefreshBluetooth.Focus();
            else if (current == BtnRefreshBluetooth) BtnClose.Focus();
            else BtnOpenWifi.Focus();
            return true;
        }
        else if (action == GamepadAction.MoveUp)
        {
            if (current == BtnOpenWifi) BtnClose.Focus();
            else if (current == BtnClose) BtnRefreshBluetooth.Focus();
            else if (current == BtnRefreshBluetooth) BtnOpenBluetooth.Focus();
            else BtnOpenWifi.Focus();
            return true;
        }
        else if (action == GamepadAction.Confirm)
        {
            if (current is Button btn) btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return true;
        }
        return false;
    }

    private MainWindow? GetMainWindow()
    {
        if (Owner is MainWindow mw) return mw;
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow as MainWindow;
        return null;
    }

    private async Task<(int ExitCode, string Output)> RunAsync(string file, string arguments)
=======
    public ConnectivityPopupWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            await RefreshWifiAsync();
            await RefreshBluetoothAsync();
        };
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string file, string arguments)
>>>>>>> 6b50390488994743904ad660c78a3cc0a532c2c8
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, (await outputTask) + Environment.NewLine + (await errorTask));
    }

    private async Task RefreshWifiAsync()
    {
        try
        {
<<<<<<< HEAD
            WifiStatusText.Text = "Verificando adaptador Wi-Fi...";
            var adapter = await RunAsync("netsh.exe", "wlan show interfaces");
            var state = Regex.Match(adapter.Output, @"(?im)^\s*Estado\s*:\s*(.+)$");
            var ssid = Regex.Match(adapter.Output, @"(?im)^\s*SSID\s*:\s*(.+)$");
            
            if (state.Success)
                WifiStatusText.Text = $"Adaptador: {state.Groups[1].Value.Trim()}" + (ssid.Success ? $" · Conectado a {ssid.Groups[1].Value.Trim()}" : "");
            else
                WifiStatusText.Text = "Adaptador Wi-Fi indisponível ou não reconhecido.";
        }
        catch
        {
            WifiStatusText.Text = "Não foi possível consultar o Wi-Fi.";
=======
            WifiStatusText.Text = "Procurando redes Wi-Fi...";
            var adapter = await RunAsync("netsh.exe", "wlan show interfaces");
            var state = Regex.Match(adapter.Output, @"(?im)^\s*Estado\s*:\s*(.+)$");
            var ssid = Regex.Match(adapter.Output, @"(?im)^\s*SSID\s*:\s*(.+)$");
            WifiStatusText.Text = state.Success
                ? $"Adaptador: {state.Groups[1].Value.Trim()}" + (ssid.Success ? $" · Conectado a {ssid.Groups[1].Value.Trim()}" : "")
                : "Adaptador Wi-Fi indisponível ou não reconhecido pelo Windows.";
            var scan = await RunAsync("netsh.exe", "wlan show networks mode=bssid");
            var names = Regex.Matches(scan.Output, @"(?im)^\s*SSID\s+\d+\s*:\s*(.+)$")
                .Select(m => m.Groups[1].Value.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();
            WifiNetworksCombo.ItemsSource = names;
            if (names.Count == 0) FeedbackText.Text = "Nenhuma rede encontrada. Verifique se o Wi-Fi está ligado.";
        }
        catch (Exception ex)
        {
            WifiStatusText.Text = "Não foi possível consultar o Wi-Fi.";
            FeedbackText.Text = ex.Message;
>>>>>>> 6b50390488994743904ad660c78a3cc0a532c2c8
        }
    }

    private async Task RefreshBluetoothAsync()
    {
        try
        {
<<<<<<< HEAD
            BluetoothStatusText.Text = "Verificando Bluetooth...";
            BluetoothDevicesText.Text = "Buscando dispositivos reais...";
            
            var script = @"$devices = Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'OK' -and $_.FriendlyName -notmatch 'Service|Serviço|Enumerator|Enumerador|Generic|Genérico|Transport|Transporte|Protocol|Protocolo|Perfil|Profile|Microsoft|Attribute|Atributo|Device \(|Dispositivo \(' }; if ($devices) { $devices | Select-Object -ExpandProperty FriendlyName } else { 'Nenhum' }";
            
            var result = await RunAsync("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"");
            var lines = result.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
            
            var devices = lines.Where(l => !string.IsNullOrWhiteSpace(l) && l != "Nenhum").Distinct().ToList();
            
            if (devices.Count > 0)
            {
                BluetoothStatusText.Text = "Bluetooth ativo";
                BluetoothDevicesText.Text = string.Join(Environment.NewLine, devices);
            }
            else
            {
                BluetoothStatusText.Text = "Bluetooth ativo";
                BluetoothDevicesText.Text = "Nenhum dispositivo pareado ou adaptador detectado.";
            }
        }
        catch (Exception ex)
        {
            BluetoothStatusText.Text = "Erro ao consultar Bluetooth.";
=======
            var result = await RunAsync("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue | Select-Object Status,FriendlyName,InstanceId | ConvertTo-Csv -NoTypeInformation\"");
            var lines = result.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var devices = lines.Skip(1).Where(l => !l.StartsWith("At", StringComparison.OrdinalIgnoreCase) && !l.StartsWith("Status", StringComparison.OrdinalIgnoreCase)).ToList();
            var enabled = lines.Any(l => l.StartsWith("\"OK\"", StringComparison.OrdinalIgnoreCase));
            BluetoothStatusText.Text = devices.Count > 0
                ? (enabled ? "Adaptador/dispositivo Bluetooth ativo." : "Bluetooth detectado, mas pode estar desativado.")
                : "Nenhum adaptador Bluetooth foi encontrado.";
            BluetoothDevicesText.Text = devices.Count > 0
                ? string.Join(Environment.NewLine, devices.Select(l => l.Replace("\"", "").Split(',').ElementAtOrDefault(1)).Where(n => !string.IsNullOrWhiteSpace(n)))
                : "Nenhum dispositivo pareado ou adaptador encontrado.";
        }
        catch (Exception ex)
        {
            BluetoothStatusText.Text = "Não foi possível consultar o Bluetooth.";
>>>>>>> 6b50390488994743904ad660c78a3cc0a532c2c8
            BluetoothDevicesText.Text = ex.Message;
        }
    }

<<<<<<< HEAD
    private async void OnRefreshBluetoothClick(object? sender, RoutedEventArgs e) => await RefreshBluetoothAsync();

    private void OnOpenNativeWifiClick(object? sender, RoutedEventArgs e) => OpenNativeSettings("ms-settings:network-wifi");
    private void OnOpenNativeBluetoothClick(object? sender, RoutedEventArgs e) => OpenNativeSettings("ms-settings:bluetooth");

    private void OpenNativeSettings(string uri)
    {
        try
        {
            var main = GetMainWindow();
            if (main != null) { main.Topmost = false; main.WindowState = WindowState.Minimized; }
            if (Owner != null && Owner != main) Owner.Hide();
            Hide();

            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

            // 🟢 MONITOR DE JANELA: Espera o usuário sair da tela do Windows
            _waitForSettingsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _waitForSettingsTimer.Tick += (_, _) =>
            {
                var title = GetActiveWindowTitle();
                
                // Se o título não for mais a janela de configurações do Windows
                if (string.IsNullOrEmpty(title) || 
                   (!title.Contains("Configurações") && !title.Contains("Settings") && 
                    !title.Contains("Wi-Fi") && !title.Contains("Bluetooth") && 
                    !title.Contains("Rede") && !title.Contains("Network")))
                {
                    _waitForSettingsTimer?.Stop();
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (Owner != null && Owner != main) Owner.Show();
                        if (main != null) { main.WindowState = WindowState.Normal; main.Activate(); main.Topmost = true; }
                        Close();
                    });
                }
            };
            _waitForSettingsTimer.Start();
        }
        catch (Exception ex)
        {
            FeedbackText.Text = $"Erro ao abrir: {ex.Message}";
        }
    }

    private static string GetActiveWindowTitle()
    {
        const int nChars = 256;
        var buff = new StringBuilder(nChars);
        var handle = GetForegroundWindow();
        return GetWindowText(handle, buff, nChars) > 0 ? buff.ToString() : string.Empty;
    }
=======
    private async void OnRefreshWifiClick(object? sender, RoutedEventArgs e) => await RefreshWifiAsync();
    private async void OnRefreshBluetoothClick(object? sender, RoutedEventArgs e) => await RefreshBluetoothAsync();

    private async void OnConnectWifiClick(object? sender, RoutedEventArgs e)
    {
        if (WifiNetworksCombo.SelectedItem is not string ssid)
        {
            FeedbackText.Text = "Selecione uma rede Wi-Fi primeiro.";
            return;
        }

        try
        {
            FeedbackText.Text = $"Solicitando conexão com {ssid}...";
            // Conecta a perfis já salvos no Windows. Se não houver perfil, o netsh informa isso claramente.
            var quotedSsid = "\"" + ssid.Replace("\"", "") + "\"";
            var result = await RunAsync("netsh.exe", "wlan connect name=" + quotedSsid);
            FeedbackText.Text = result.ExitCode == 0 ? $"Solicitação enviada para {ssid}. Se a rede exigir senha, salve o perfil no Windows primeiro." : result.Output.Trim();
            await RefreshWifiAsync();
        }
        catch (Exception ex) { FeedbackText.Text = ex.Message; }
    }

    private async void OnToggleBluetoothClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            FeedbackText.Text = "Alterando estado do adaptador Bluetooth...";
            var script = "$d=Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue | Where-Object {$_.FriendlyName -match 'Radio|Adapter|Bluetooth'} | Select-Object -First 1; if(-not $d){throw 'Adaptador Bluetooth não encontrado'}; if($d.Status -eq 'OK'){Disable-PnpDevice -InstanceId $d.InstanceId -Confirm:$false -ErrorAction Stop}else{Enable-PnpDevice -InstanceId $d.InstanceId -Confirm:$false -ErrorAction Stop}; 'OK'";
            var result = await RunAsync("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "\\\"") + "\"");
            FeedbackText.Text = result.ExitCode == 0 ? "Comando enviado. Atualizando estado..." : "O Windows bloqueou a alteração. Pode ser necessário executar o launcher como administrador. " + result.Output.Trim();
            await RefreshBluetoothAsync();
        }
        catch (Exception ex) { FeedbackText.Text = ex.Message; }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
>>>>>>> 6b50390488994743904ad660c78a3cc0a532c2c8
}
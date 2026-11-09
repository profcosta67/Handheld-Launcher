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

namespace HandheldLauncher.App.Views;

public partial class ConnectivityPopupWindow : Window
{
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
        BtnDownloads.Click += OnDownloadsClick;

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
        }
    }

    private async Task RefreshBluetoothAsync()
    {
        try
        {
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
            BluetoothDevicesText.Text = ex.Message;
        }
    }

    private async void OnRefreshBluetoothClick(object? sender, RoutedEventArgs e) => await RefreshBluetoothAsync();

    private void OnDownloadsClick(object? sender, RoutedEventArgs e)
    {
        if (GetMainWindow()?.DataContext is HandheldLauncher.App.ViewModels.MainViewModel vm)
            vm.ToggleDownloads();
        Close();
    }

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

            _waitForSettingsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _waitForSettingsTimer.Tick += (_, _) =>
            {
                var title = GetActiveWindowTitle();
                
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
}
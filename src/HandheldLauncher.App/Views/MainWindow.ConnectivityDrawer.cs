using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HandheldLauncher.App.Views;

public partial class MainWindow : Window
{
    private void OnConnectivityDrawerClick(object? sender, RoutedEventArgs e)
    {
        ConnectivityDrawer.IsVisible = true;
        _ = RefreshDrawerWifiAsync();
        _ = RefreshDrawerBluetoothAsync();
    }

    private void OnConnectivityDrawerCloseClick(object? sender, RoutedEventArgs e) => ConnectivityDrawer.IsVisible = false;
    private void OnConnectivityDrawerBackdropPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e) => ConnectivityDrawer.IsVisible = false;
    private async void OnDrawerRefreshWifiClick(object? sender, RoutedEventArgs e) => await RefreshDrawerWifiAsync();
    private async void OnDrawerRefreshBluetoothClick(object? sender, RoutedEventArgs e) => await RefreshDrawerBluetoothAsync();

    private static async Task<string> RunConnectivityCommandAsync(string file, string arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(file, arguments) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (await output) + Environment.NewLine + (await error);
    }

    private async Task RefreshDrawerWifiAsync()
    {
        try
        {
            DrawerWifiStatus.Text = "Procurando redes Wi-Fi...";
            DrawerWifiNetworks.Items.Clear();
            var current = await RunConnectivityCommandAsync("netsh.exe", "wlan show interfaces");
            var ssid = Regex.Match(current, @"(?im)^\s*SSID\s*:\s*(.+)$");
            DrawerWifiStatus.Text = ssid.Success && !string.IsNullOrWhiteSpace(ssid.Groups[1].Value) ? "Conectado a " + ssid.Groups[1].Value.Trim() : "Não conectado";
            var networks = await RunConnectivityCommandAsync("netsh.exe", "wlan show networks mode=bssid");
            foreach (Match match in Regex.Matches(networks, @"(?im)^\s*SSID\s+\d+\s*:\s*(.*)$"))
            {
                var name = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(name) && !DrawerWifiNetworks.Items.Contains(name)) DrawerWifiNetworks.Items.Add(name);
            }
            if (DrawerWifiNetworks.Items.Count == 0) DrawerWifiNetworks.Items.Add("Nenhuma rede encontrada");
        }
        catch (Exception ex) { DrawerWifiStatus.Text = "Falha ao consultar Wi-Fi"; DrawerConnectivityFeedback.Text = ex.Message; }
    }

    private async Task RefreshDrawerBluetoothAsync()
    {
        try
        {
            DrawerBluetoothStatus.Text = "Buscando dispositivos pareados...";
            DrawerBluetoothDevices.Items.Clear();
            const string script = "Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'OK' -and $_.FriendlyName -notmatch 'Enumerator|Microsoft|Generic|Service|Attribute' } | Select-Object -ExpandProperty FriendlyName";
            var output = await RunConnectivityCommandAsync("powershell.exe", "-NoProfile -Command \"" + script + "\"");
            foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()) DrawerBluetoothDevices.Items.Add(line);
            DrawerBluetoothStatus.Text = DrawerBluetoothDevices.Items.Count == 0 ? "Nenhum dispositivo pareado encontrado" : DrawerBluetoothDevices.Items.Count + " dispositivo(s) disponível(is)";
        }
        catch (Exception ex) { DrawerBluetoothStatus.Text = "Falha ao consultar Bluetooth"; DrawerConnectivityFeedback.Text = ex.Message; }
    }

    private async void OnDrawerConnectWifiClick(object? sender, RoutedEventArgs e)
    {
        var network = DrawerWifiNetworks.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(network) || network == "Nenhuma rede encontrada") { DrawerConnectivityFeedback.Text = "Selecione uma rede Wi-Fi na lista."; return; }
        try
        {
            DrawerConnectivityFeedback.Text = "Conectando a " + network + "...";
            var password = DrawerWifiPassword.Text ?? string.Empty;
            if (!string.IsNullOrEmpty(password))
            {
                var escapedSsid = SecurityElement.Escape(network);
                var escapedPassword = SecurityElement.Escape(password);
                var xml = $"<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><name>{escapedSsid}</name><SSIDConfig><SSID><name>{escapedSsid}</name></SSID></SSIDConfig><connectionType>ESS</connectionType><connectionMode>auto</connectionMode><MSM><security><authEncryption><authentication>WPA2PSK</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption><sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{escapedPassword}</keyMaterial></sharedKey></security></MSM></WLANProfile>";
                var path = Path.Combine(Path.GetTempPath(), "HandheldLauncher-WiFi-" + Guid.NewGuid().ToString("N") + ".xml");
                await File.WriteAllTextAsync(path, xml);
                try { await RunConnectivityCommandAsync("netsh.exe", $"wlan add profile filename=\"{path}\" user=current"); }
                finally { try { File.Delete(path); } catch { } }
            }
            await RunConnectivityCommandAsync("netsh.exe", $"wlan connect name=\"{network}\" ssid=\"{network}\"");
            DrawerConnectivityFeedback.Text = "Solicitação de conexão enviada.";
            await Task.Delay(1800);
            await RefreshDrawerWifiAsync();
        }
        catch (Exception ex) { DrawerConnectivityFeedback.Text = "Não foi possível conectar: " + ex.Message; }
    }

    private async void OnDrawerDisconnectWifiClick(object? sender, RoutedEventArgs e)
    {
        try { await RunConnectivityCommandAsync("netsh.exe", "wlan disconnect"); DrawerConnectivityFeedback.Text = "Desconexão solicitada."; await Task.Delay(700); await RefreshDrawerWifiAsync(); }
        catch (Exception ex) { DrawerConnectivityFeedback.Text = "Não foi possível desconectar: " + ex.Message; }
    }

    private void OnDrawerManageBluetoothClick(object? sender, RoutedEventArgs e)
    {
        if (DrawerBluetoothDevices.SelectedItem is not string selected) { DrawerConnectivityFeedback.Text = "Selecione um dispositivo Bluetooth."; return; }
        try { Process.Start(new ProcessStartInfo("ms-settings:connecteddevices") { UseShellExecute = true }); DrawerConnectivityFeedback.Text = "O Windows abriu o gerenciamento de dispositivos para concluir a conexão de: " + selected; }
        catch (Exception ex) { DrawerConnectivityFeedback.Text = ex.Message; }
    }
}

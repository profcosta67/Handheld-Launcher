using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
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
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{file} terminou com código {process.ExitCode}: {error.Trim()} {output.Trim()}".Trim());
        return output;
    }

    private static string ReadNetshValue(string output, string key)
    {
        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;
            if (line.Substring(0, separator).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                return line.Substring(separator + 1).Trim();
        }
        return string.Empty;
    }

    private static IReadOnlyList<string> ParseNetshSsids(string output)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("SSID ", StringComparison.OrdinalIgnoreCase)) continue;
            var separator = trimmed.IndexOf(':');
            if (separator < 0) continue;
            var label = trimmed.Substring(0, separator).Trim();
            if (label.Length < 5 || !int.TryParse(label.Substring(5).Trim(), out _)) continue;
            var name = trimmed.Substring(separator + 1).Trim();
            if (name.Length > 0 && seen.Add(name)) result.Add(name);
        }
        return result;
    }

    private async Task RefreshDrawerWifiAsync()
    {
        try
        {
            DrawerWifiStatus.Text = "Procurando redes Wi-Fi...";
            DrawerWifiNetworks.Items.Clear();
            var current = await RunConnectivityCommandAsync("netsh.exe", "wlan show interfaces");
            var connected = ReadNetshValue(current, "SSID");
            DrawerWifiStatus.Text = !string.IsNullOrWhiteSpace(connected) && !connected.Equals("N/A", StringComparison.OrdinalIgnoreCase)
                ? "Conectado a " + connected : "Não conectado";

            // Uma linha por SSID, sem duplicar o mesmo nome por ponto de acesso.
            var networks = await RunConnectivityCommandAsync("netsh.exe", "wlan show networks mode=Bssid");
            foreach (var name in ParseNetshSsids(networks))
                DrawerWifiNetworks.Items.Add(name);
            if (DrawerWifiNetworks.Items.Count == 0)
                DrawerWifiNetworks.Items.Add("Nenhuma rede encontrada");
        }
        catch (Exception ex) { DrawerWifiStatus.Text = "Falha ao consultar Wi-Fi"; DrawerConnectivityFeedback.Text = ex.Message; }
    }

    private async Task RefreshDrawerBluetoothAsync()
    {
        try
        {
            DrawerBluetoothStatus.Text = "Buscando dispositivos pareados...";
            DrawerBluetoothDevices.Items.Clear();
            // Não exigir Status=OK: periféricos pareados podem estar desconectados.
            const string script = "$ErrorActionPreference='Stop'; Get-PnpDevice -Class Bluetooth | Where-Object { $_.FriendlyName -and $_.FriendlyName -notmatch 'Enumerator|Microsoft Bluetooth|Generic Access|Service Discovery' } | Sort-Object FriendlyName -Unique | ForEach-Object { '{0} [{1}]' -f $_.FriendlyName,$_.Status }";
            var output = await RunConnectivityCommandAsync("powershell.exe", "-NoProfile -Command \"" + script + "\"");
            foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)))
                DrawerBluetoothDevices.Items.Add(line);
            DrawerBluetoothStatus.Text = DrawerBluetoothDevices.Items.Count == 0 ? "Nenhum dispositivo Bluetooth encontrado" : DrawerBluetoothDevices.Items.Count + " dispositivo(s) detectado(s)";
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

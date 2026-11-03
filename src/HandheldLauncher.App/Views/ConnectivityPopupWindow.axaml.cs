using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HandheldLauncher.App.Views;

public partial class ConnectivityPopupWindow : Window
{
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
        }
    }

    private async Task RefreshBluetoothAsync()
    {
        try
        {
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
            BluetoothDevicesText.Text = ex.Message;
        }
    }

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
            var result = await RunAsync("netsh.exe", $"wlan connect name={\"\\"\"}{ssid}{\"\\"\"}");
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
}
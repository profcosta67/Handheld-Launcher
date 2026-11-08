using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform
{
    public class WindowsGameModeService
    {
        private readonly string _omniConsolePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "OmniConsole");
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "HandheldLauncher";

        // --- PARTE 1: INSTALAÇÃO DO OMNICONSOLE ---

                public async Task<bool> EnableOmniConsoleAsync()
        {
            try
            {
                // 1. Verifica se a pasta e os arquivos existem antes de qualquer coisa
                if (!Directory.Exists(_omniConsolePath))
                    throw new DirectoryNotFoundException($"A pasta do OmniConsole não existe no build: {_omniConsolePath}");

                string certPath = Path.Combine(_omniConsolePath, "OmniConsole.cer");
                string msixPath = Path.Combine(_omniConsolePath, "OmniConsole.msix");

                if (!File.Exists(certPath))
                    throw new FileNotFoundException($"O arquivo do certificado não foi encontrado em: {certPath}");

                if (!File.Exists(msixPath))
                    throw new FileNotFoundException($"O arquivo de instalação não foi encontrado em: {msixPath}");

                // 2. Executa a instalação
                // ATENÇÃO: Este método precisa que o App rode como Administrador!
                EnableDeveloperMode();
                InstallCertificate(certPath);
                await InstallMsixAsync(msixPath);
                
                string aumid = await GetOmniConsoleAumidAsync();
                if (string.IsNullOrEmpty(aumid)) 
                    throw new Exception("AUMID do OmniConsole não encontrado após a instalação.");

                RegisterAsHomeApp(aumid);
                ConfigureOmniConsole();

                DebugLog.Write("[GAME MODE] OmniConsole instalado e configurado com sucesso.");
                return true;
            }
            catch (Exception ex)
            {
                // Agora o erro vai pro log com detalhes exatos
                DebugLog.Write($"[GAME MODE] Falha crítica: {ex.GetType().Name} - {ex.Message}");
                return false;
            }
        }

        private void EnableDeveloperMode()
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock", true);
            key?.SetValue("AllowDevelopmentWithoutDevLicense", 1, RegistryValueKind.DWord);
        }

        private void InstallCertificate(string certPath)
        {
            var cert = new X509Certificate2(certPath);
            using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);
            store.Add(cert);
        }

                        private async Task InstallMsixAsync(string msixPath)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    // O -ErrorAction Stop força o PowerShell a cuspir o erro real
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Add-AppxPackage -Path '{msixPath}' -ErrorAction Stop\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                }
            };
            
            process.Start();
            
            // Lê a saída e o erro do PowerShell
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();

            // Se o PowerShell retornar erro, nós jogamos a mensagem dele para o C#
            if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(error))
            {
                throw new Exception($"O Windows bloqueou a instalação do MSIX. Erro do PowerShell: {error.Trim()}");
            }
        }

        private async Task<string> GetOmniConsoleAumidAsync()
        {
            // Descobre o ID real do pacote instalado
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"(Get-AppxPackage -Name *OmniConsole*).PackageFamilyName + '!App'\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            string result = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return result.Trim();
        }

        private void RegisterAsHomeApp(string aumid)
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GamingConfiguration", true);
            key?.SetValue("GamingHomeApp", aumid);
        }

        private void ConfigureOmniConsole()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string omniConfigDir = Path.Combine(localAppData, "OmniConsole");
            Directory.CreateDirectory(omniConfigDir);
            
            string configFile = Path.Combine(omniConfigDir, "platforms.json");
            string launcherExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HandheldLauncher.exe");

            var config = new
            {
                DefaultPlatform = "HandheldLauncher",
                Platforms = new[] {
                    new { Name = "HandheldLauncher", Path = launcherExe, Arguments = "" }
                }
            };

            string jsonConfig = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configFile, jsonConfig);
        }

        // --- PARTE 2: INICIAR NO BOOT ---

        public static void SetLauncherOnBoot(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                if (enable)
                {
                    var exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                        key?.SetValue(AppName, $"\"{exePath}\"");
                    
                    // Desativa o Xbox FSE original (opcional se usarmos o Omni)
                    using var gameBarKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameBar", writable: true);
                    gameBarKey?.SetValue("AutoGameModeEnabled", 0, RegistryValueKind.DWord);
                }
                else
                {
                    key?.DeleteValue(AppName, throwOnMissingValue: false);
                }
                DebugLog.Write($"[GAME MODE] Boot configurado: {enable}");
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[GAME MODE] Falha ao configurar boot: {ex.Message}");
            }
        }
    }
}
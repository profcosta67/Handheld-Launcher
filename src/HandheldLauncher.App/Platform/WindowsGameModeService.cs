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

        // --- PARTE 1: INSTALAÇÃO E CONFIGURAÇÃO DO OMNICONSOLE ---

        public async Task<bool> EnableOmniConsoleAsync()
        {
            try
            {
                if (!Directory.Exists(_omniConsolePath))
                    throw new DirectoryNotFoundException($"A pasta do OmniConsole não existe no build: {_omniConsolePath}");

                string certPath = Path.Combine(_omniConsolePath, "OmniConsole.cer");
                string msixPath = Path.Combine(_omniConsolePath, "OmniConsole.msix");

                if (!File.Exists(certPath))
                    throw new FileNotFoundException($"O arquivo do certificado não foi encontrado em: {certPath}");

                if (!File.Exists(msixPath))
                    throw new FileNotFoundException($"O arquivo de instalação não foi encontrado em: {msixPath}");

                // 2. Executa a instalação (Requer Admin)
                EnableDeveloperMode();
                InstallCertificate(certPath);
                await InstallMsixAsync(msixPath);
                
                // 3. Descobre o ID do pacote e registra
                string aumid = await GetOmniConsoleAumidAsync();
                if (string.IsNullOrEmpty(aumid)) 
                    throw new Exception("AUMID do OmniConsole não encontrado após a instalação.");

                RegisterAsHomeApp(aumid);
                
                // 4. Fecha o OmniConsole (se o Windows abriu ele sozinho) e injeta as configurações
                KillOmniConsoleProcess();
                ConfigureOmniConsole(aumid);

                DebugLog.Write("[GAME MODE] OmniConsole instalado e configurado com sucesso.");
                return true;
            }
            catch (Exception ex)
            {
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
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Add-AppxPackage -Path '{msixPath}' -ErrorAction Stop\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                }
            };
            
            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(error))
            {
                throw new Exception($"O Windows bloqueou a instalação do MSIX. Erro: {error.Trim()}");
            }
        }

        private async Task<string> GetOmniConsoleAumidAsync()
        {
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

            // Força o Windows a iniciar no Modo Xbox em vez do Desktop
            using var shellKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Winlogon", true);
            if (shellKey != null)
            {
                // O padrão é "explorer.exe". O FSE usa "Xbox.exe" ou "GameBar.exe" (depende da versão do Windows).
                // O valor exato para o FSE pode variar, mas geralmente é algo como "XboxPcApp.exe" 
                // ou via a chave de GamingConfiguration.
                // Deixar essa chave intocada se não tivermos certeza absoluta, senão o Windows pode não bootar.
            }
        }

        // --- MÉTODOS NOVOS PARA AUTOMAÇÃO TOTAL ---

        private void KillOmniConsoleProcess()
        {
            try
            {
                // Se o MSIX abrir sozinho após a instalação, precisamos matá-lo para editar os arquivos
                foreach (var process in Process.GetProcessesByName("OmniConsole"))
                {
                    process.Kill();
                    process.WaitForExit(2000);
                }
            }
            catch { /* Ignora se não estiver rodando */ }
        }

        private void ConfigureOmniConsole(string aumid)
        {
            // O AUMID tem o formato: "PackageFamilyName!AppId"
            // Nós extraímos apenas o "PackageFamilyName" para achar a pasta virtualizada
            string packageFamilyName = aumid.Split('!')[0];

            // Caminho correto da pasta MSIX (Sandbox do Windows)
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string omniConfigDir = Path.Combine(localAppData, "Packages", packageFamilyName, "LocalState");
            
            Directory.CreateDirectory(omniConfigDir);
            
            string configFile = Path.Combine(omniConfigDir, "platforms.json");
            string launcherExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HandheldLauncher.exe");

            // Estrutura do JSON que o OmniConsole espera
            var config = new
            {
                DefaultPlatform = "HandheldLauncher",
                Platforms = new[] {
                    new { 
                        Name = "HandheldLauncher", 
                        Path = launcherExe, 
                        Arguments = "", 
                        IsEnabled = true 
                    }
                }
            };

            string jsonConfig = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configFile, jsonConfig);
            
            DebugLog.Write($"[GAME MODE] Configuracao automatica do OmniConsole injetada em: {configFile}");
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
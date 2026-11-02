using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.App.Platform;

public class WindowsDriverService
{
    public async Task<string> InstallDriverAsync(string infPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(infPath)) return "Arquivo .inf não encontrado.";

                // Comando oficial do Windows para adicionar e instalar um driver
                // /add-driver : Adiciona o pacote ao repositório de drivers
                // /install    : Instala o driver nos dispositivos correspondentes
                var startInfo = new ProcessStartInfo
                {
                    FileName = "pnputil.exe",
                    Arguments = $"/add-driver \"{infPath}\" /install",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return "Falha ao iniciar o processo do pnputil.";

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    DebugLog.Write($"[DRIVERS] Driver instalado com sucesso: {infPath}");
                    return "Driver instalado com sucesso!";
                }
                else
                {
                    DebugLog.Write($"[DRIVERS] Erro ao instalar driver: {error}");
                    return $"Erro: {error}";
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[DRIVERS] Exceção ao instalar driver: {ex.Message}");
                return $"Erro inesperado: {ex.Message}";
            }
        });
    }

    public async Task<string> ListInstalledDriversAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "pnputil.exe",
                    Arguments = "/enum-drivers",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return "Falha ao listar drivers.";
                return process.StandardOutput.ReadToEnd();
            }
            catch (Exception ex)
            {
                return $"Erro: {ex.Message}";
            }
        });
    }
}
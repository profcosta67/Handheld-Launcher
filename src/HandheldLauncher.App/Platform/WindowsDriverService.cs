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

                // pnputil precisa de privilégios de administrador pra instalar o
                // driver nos dispositivos (não só adicionar ao repositório). Sem
                // elevação, a instalação pode "dar certo" parcialmente e falhar
                // silenciosamente no passo de instalar no dispositivo. Se o launcher
                // já não estiver rodando elevado, pedimos UAC só pra esse processo
                // (pnputil), não pro launcher inteiro.
                bool isElevated = IsRunningAsAdmin();

                // Comando oficial do Windows para adicionar e instalar um driver
                // /add-driver : Adiciona o pacote ao repositório de drivers
                // /install    : Instala o driver nos dispositivos correspondentes
                var startInfo = new ProcessStartInfo
                {
                    FileName = "pnputil.exe",
                    Arguments = $"/add-driver \"{infPath}\" /install",
                    CreateNoWindow = true
                };

                if (isElevated)
                {
                    // Já elevado: mantém o comportamento original, com saída capturada.
                    startInfo.UseShellExecute = false;
                    startInfo.RedirectStandardOutput = true;
                    startInfo.RedirectStandardError = true;
                }
                else
                {
                    // Não elevado: dispara o prompt de UAC só para este processo.
                    // UseShellExecute=true + Verb="runas" não permite redirecionar
                    // saída, então o resultado é inferido pelo ExitCode.
                    startInfo.UseShellExecute = true;
                    startInfo.Verb = "runas";
                }

                using var process = Process.Start(startInfo);
                if (process == null) return "Falha ao iniciar o processo do pnputil.";

                string output = isElevated ? process.StandardOutput.ReadToEnd() : string.Empty;
                string error = isElevated ? process.StandardError.ReadToEnd() : string.Empty;
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    DebugLog.Write($"[DRIVERS] Driver instalado com sucesso: {infPath}");
                    return "Driver instalado com sucesso!";
                }
                else
                {
                    DebugLog.Write($"[DRIVERS] Erro ao instalar driver (exit={process.ExitCode}): {error}");
                    return isElevated
                        ? $"Erro: {error}"
                        : $"Erro: pnputil retornou código {process.ExitCode}.";
                }
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // ERROR_CANCELLED: usuário clicou "Não" no prompt do UAC.
                DebugLog.Write("[DRIVERS] Instalação cancelada pelo usuário no prompt do UAC.");
                return "Instalação cancelada — é preciso aceitar o prompt de permissão de administrador para instalar o driver.";
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[DRIVERS] Exceção ao instalar driver: {ex.Message}");
                return $"Erro inesperado: {ex.Message}";
            }
        });
    }

    private static bool IsRunningAsAdmin()
    {
        if (!OperatingSystem.IsWindows()) return false;

        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
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
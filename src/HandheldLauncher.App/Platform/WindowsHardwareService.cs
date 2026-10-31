using System;
using System.Diagnostics;
using System.IO;
using HandheldLauncher.Core.Services;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

public class WindowsHardwareService : IHardwareService
{
    private readonly string _ryzenAdjPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ryzenadj.exe");

    public void SetTdpMode(string mode)
    {
        if (!OperatingSystem.IsWindows()) return;

        // 🟢 Novos limites de TDP em miliwatts (mW) com margem de segurança
        int tdpLimit = mode switch
        {
            "Eco" => 8000,         // 8W
            "Normal" => 15000,     // 15W
            "Desempenho" => 25000, // 25W
            "Boost" => 30000,      // 30W (Teto seguro para AMD)
            _ => 15000
        };

        // 🟢 ADICIONADO: A trava de segurança para 90 graus!
        string arguments = $"--stapm-limit={tdpLimit} --fast-limit={tdpLimit} --slow-limit={tdpLimit} --tctl-temp=90";

        try
        {
            DebugLog.Write($"[WINDOWS TDP] Executando RyzenAdj: {arguments}");
            
            var psi = new ProcessStartInfo
            {
                FileName = _ryzenAdjPath,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                // Lê as saídas antes do WaitForExit para evitar deadlock
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit(2000); 
                
                DebugLog.Write($"[WINDOWS TDP] Saída: {output.Trim()} | Erro: {error.Trim()}");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[WINDOWS TDP] Erro ao chamar RyzenAdj: {ex.Message}");
        }
    }

    /// <summary>
    /// Cria a Tarefa Agendada no Windows para iniciar o Launcher como Admin sem UAC.
    /// </summary>
    public void RegisterAutoStartTask()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

            string taskName = "HandheldLauncher_Startup";
            
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Create /TN \"{taskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var proc = Process.Start(psi);
            proc?.WaitForExit();
            
            DebugLog.Write($"[AUTOSTART] Tarefa criada com código {proc?.ExitCode}");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[AUTOSTART] Erro: {ex.Message}");
        }
    }

    public void SetVolume(double level)
    {
        DebugLog.Write($"[WINDOWS] Volume não implementado: {level}");
    }

    public void SetBrightness(double level)
    {
        DebugLog.Write($"[WINDOWS] Brilho não implementado: {level}");
    }

    public void SetColorMode(string mode)
    {
        DebugLog.Write($"[WINDOWS] Cor não implementado: {mode}");
    }

    public void SetPerformanceHudEnabled(bool enabled)
    {
        DebugLog.Write($"[WINDOWS] HUD não implementado: {enabled}");
    }
}
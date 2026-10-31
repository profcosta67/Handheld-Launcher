using System;
using System.Diagnostics;
using HandheldLauncher.Core.Services;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

public class WindowsHardwareService : IHardwareService
{
    // Caminho do RyzenAdj (ajustaremos depois se precisar)
    private readonly string _ryzenAdjPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ryzenadj.exe");

    public void SetTdpMode(string mode)
    {
        if (!OperatingSystem.IsWindows()) return;

        // Define os limites de TDP em miliwatts (mW)
        // Eco: 8W (8000), Equilibrado: 15W (15000), Turbo: 25W (25000)
        int tdpLimit = mode switch
        {
            "Eco" => 8000,
            "Turbo" => 25000,
            "Equilibrado" => 15000,
            _ => 15000
        };

        string arguments = $"--stapm-limit={tdpLimit} --fast-limit={tdpLimit} --slow-limit={tdpLimit}";

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
                process.WaitForExit(2000); // Espera até 2 segundos
                string output = process.StandardOutput.ReadToEnd();
                DebugLog.Write($"[WINDOWS TDP] Resultado: {output}");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[WINDOWS TDP] Erro ao chamar RyzenAdj: {ex.Message}");
        }
    }

    public void SetVolume(double level)
    {
        // Implementaremos depois usando o CoreAudio API do Windows
        DebugLog.Write($"[WINDOWS] Volume não implementado ainda: {level}");
    }

    public void SetBrightness(double level)
    {
        // Implementaremos depois usando WMI
        DebugLog.Write($"[WINDOWS] Brilho não implementado ainda: {level}");
    }

    public void SetColorMode(string mode)
    {
        DebugLog.Write($"[WINDOWS] Cor não implementado ainda: {mode}");
    }

    public void SetPerformanceHudEnabled(bool enabled)
    {
        // Lógica para iniciar o RTSS ou similar
        DebugLog.Write($"[WINDOWS] HUD: {enabled}");
    }
        public void RegisterAutoStartTask()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(exePath) || !System.IO.File.Exists(exePath)) return;

            string taskName = "HandheldLauncher_Startup";
            
            // Cria a tarefa para rodar no logon, com privilégios máximos (sem UAC)
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Create /TN \"{taskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit();
            
            DebugLog.Write($"[AUTOSTART] Tarefa criada com código {proc?.ExitCode}");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[AUTOSTART] Erro: {ex.Message}");
        }
    }
    
}

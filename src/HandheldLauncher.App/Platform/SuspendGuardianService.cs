using System;
using System.Diagnostics; 
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// v0.18 "Guardião de suspensão".
/// <para>v0.26: Adicionado controle de energia (AC/DC). Quando o cabo de energia
/// é desconectado, o Windows derruba o TDP e o plano de energia. O Guardião
/// intercepta essa mudança e força o Alto Desempenho novamente.</para>
/// </summary>
public static class SuspendGuardianService
{
    private static bool _started;
    private static Action? _onResume;
    private static DateTime _lastResumeHandledAtUtc = DateTime.MinValue;
    private static Timer? _resumeDelayTimer;

    public static void Start(Action onResume)
    {
        if (_started) return;
        if (!OperatingSystem.IsWindows())
        {
            DebugLog.Write("[SUSPENSAO] Guardião de suspensão não iniciado: recurso é só Windows.");
            return;
        }

        _onResume = onResume;

        try
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            _started = true;
            DebugLog.Write("[SUSPENSAO] Guardião de suspensão iniciado (escutando SystemEvents.PowerModeChanged).");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[SUSPENSAO] Falha ao assinar PowerModeChanged — guardião não vai funcionar: {ex.Message}");
        }
    }

    public static void Stop()
    {
        if (!_started) return;
        try { SystemEvents.PowerModeChanged -= OnPowerModeChanged; }
        catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }
        _started = false;
        _onResume = null;
    }

    private static void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        try
        {
            DebugLog.Write($"[SUSPENSAO] PowerModeChanged recebido: {e.Mode}.");

            // =======================================================================
            // CORREÇÃO DO CABO DE ENERGIA (AC/DC)
            // =======================================================================
            if (e.Mode == PowerModes.StatusChange)
            {
                bool isOnBattery = IsOnBattery();

                if (isOnBattery)
                {
                    DebugLog.Write("[ENERGIA] Cabo desconectado! Forçando Alto Desempenho e TDP Máximo.");

                    // 1. Força o plano de energia do Windows para Alto Desempenho
                    PowerPlanService.SetHighPerformance();

                    // 2. Reaplica o TDP (usando a instância elevada para garantir que não falhe)
                    // ATENÇÃO: Troque "Performance" pelo nome exato do modo que o seu sistema usa (ex: "Performance", "Turbo", "30W")
                    _ = Task.Run(() => ElevationService.RunElevated("tdp-apply", "--mode", "Performance"));
                }
                else
                {
                    DebugLog.Write("[ENERGIA] Cabo conectado (AC). O Windows vai gerenciar o desempenho.");
                }
                
                return; // Sai do método para não confundir com a lógica de suspensão abaixo
            }
            // =======================================================================

            // Lógica de suspensão normal (Wake up)
            if (e.Mode != PowerModes.Resume) return;

            var now = DateTime.UtcNow;
            if ((now - _lastResumeHandledAtUtc).TotalSeconds < 5)
            {
                DebugLog.Write("[SUSPENSAO] Resume duplicado ignorado (< 5s do anterior).");
                return;
            }
            _lastResumeHandledAtUtc = now;

            DebugLog.Write("[SUSPENSAO] Aparelho acordou da suspensão — reaplicando TDP e devolvendo foco em 2s.");

            LogPowerRequests(); 

            _resumeDelayTimer?.Dispose();
            _resumeDelayTimer = new Timer(_ =>
            {
                try { _onResume?.Invoke(); }
                catch (Exception ex) { DebugLog.Write($"[SUSPENSAO] EXCEPTION no callback de resume: {ex.Message}"); }
            }, null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[SUSPENSAO] EXCEPTION em OnPowerModeChanged: {ex.Message}");
        }
    }

    // =======================================================================
    // DIAGNÓSTICO DO POWERCFG
    // =======================================================================
    private static void LogPowerRequests()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = "/requests",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                
                if (string.IsNullOrWhiteSpace(output))
                    DebugLog.Write("[SUSPENSAO] Diagnóstico powercfg: Nenhum bloqueio de energia detectado.");
                else
                    DebugLog.Write("[SUSPENSAO] Diagnóstico powercfg /requests:\n" + output);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write("[SUSPENSAO] Falha ao executar powercfg /requests: " + ex.Message);
        }
    }

    // =======================================================================
    // DETECÇÃO NATIVA DE BATERIA (AC/DC)
    // =======================================================================
    // Usamos P/Invoke do kernel32 para não depender de bibliotecas do WinForms.
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS systemPowerStatus);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus; // 0 = Bateria, 1 = Tomada, 255 = Desconhecido
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    private static bool IsOnBattery()
    {
        try
        {
            if (GetSystemPowerStatus(out var status))
            {
                return status.ACLineStatus == 0; // 0 significa que o cabo está fora
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[ENERGIA] Falha ao ler status da bateria: {ex.Message}");
        }
        return false; // Na dúvida, assume que está na tomada para não forçar nada
    }
}
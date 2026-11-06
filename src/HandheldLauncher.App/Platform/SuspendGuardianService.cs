using System;
using System.Diagnostics; // Necessário para o ProcessStartInfo
using System.Threading;
using Microsoft.Win32;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// v0.18 "Guardião de suspensão" (pedido do usuário, 05/11): ao acordar o
/// aparelho da suspensão do Windows, reaplica o TDP salvo e devolve o foco
/// pro jogo em execução. Handhelds Windows costumam "esquecer" os dois ao
/// voltar de suspender — o TDP volta pro padrão do firmware e alguma outra
/// janela do sistema (Xbox Game Bar, notificação, etc.) fica na frente do
/// jogo.
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
            if (e.Mode != PowerModes.Resume) return;

            var now = DateTime.UtcNow;
            if ((now - _lastResumeHandledAtUtc).TotalSeconds < 5)
            {
                DebugLog.Write("[SUSPENSAO] Resume duplicado ignorado (< 5s do anterior).");
                return;
            }
            _lastResumeHandledAtUtc = now;

            DebugLog.Write("[SUSPENSAO] Aparelho acordou da suspensão — reaplicando TDP e devolvendo foco em 2s.");

            // =======================================================================
            // AQUI ESTÁ A CHAMADA DO DIAGNÓSTICO DO POWERCFG
            // =======================================================================
            LogPowerRequests(); 
            // =======================================================================

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
    } // <--- ESTA CHAVE ESTAVA FALTANDO NO SEU CÓDIGO

    // =======================================================================
    // MÉTODO FORA DO ONPOWERMODECHANGED
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
}
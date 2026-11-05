using System;
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
///
/// Usa Microsoft.Win32.SystemEvents.PowerModeChanged em vez de um hook
/// manual de WM_POWERBROADCAST: é a API padrão do .NET pra isso, já roda
/// sua própria thread/janela de mensagens por dentro, sem precisar criar
/// uma janela nativa só pra isso. Only Windows fires it — em outras
/// plataformas Start() não faz nada.
/// </summary>
public static class SuspendGuardianService
{
    private static bool _started;
    private static Action? _onResume;
    private static DateTime _lastResumeHandledAtUtc = DateTime.MinValue;

    // Precisa de uma referência guardada — um Timer sem referência pode ser
    // coletado pelo GC antes de disparar (armadilha clássica do
    // System.Threading.Timer). Só um resume por vez interessa, então um
    // campo estático é suficiente; uma troca aqui também cancela o anterior
    // (ver Dispose em OnPowerModeChanged).
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

            // Alguns drivers/firmwares disparam Resume mais de uma vez pro
            // mesmo evento real de acordar — ignora um 2º aviso chegando
            // menos de 5s depois do primeiro, pra não reaplicar TDP/foco
            // em duplicidade.
            var now = DateTime.UtcNow;
            if ((now - _lastResumeHandledAtUtc).TotalSeconds < 5)
            {
                DebugLog.Write("[SUSPENSAO] Resume duplicado ignorado (< 5s do anterior).");
                return;
            }
            _lastResumeHandledAtUtc = now;

            DebugLog.Write("[SUSPENSAO] Aparelho acordou da suspensão — reaplicando TDP e devolvendo foco em 2s.");

            // Espera 2s pro hardware/driver (PawnIO/RyzenSMU, janela do jogo)
            // voltar a responder antes de agir — acordar não é instantâneo.
            // Dispose do timer anterior (se ainda não disparou) só por
            // segurança — o debounce de 5s acima já evita chegar duas vezes.
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
}

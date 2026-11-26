using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Platform;

namespace HandheldLauncher.App.Views;

/// <summary>
/// 0.28: tela cinza depois de um jogo fechar (ou travar). O launcher volta, recebe o controle
/// normalmente, mas a janela não desenha nada — o desenho da tela ficou preso na placa de vídeo
/// depois que o jogo caiu. O usuário via a tela parada e só conseguia fechar pelo Gerenciador de
/// Tarefas (e o fechamento ainda travava).
///
/// <para>Depois que um jogo fecha, este vigia pede um quadro à janela. Se em duas tentativas de
/// 5 s nenhum quadro for desenhado, o launcher se reinicia sozinho (a instância nova espera esta
/// sair antes de abrir).</para>
/// </summary>
public partial class MainWindow
{
    private const double RenderCheckSeconds = 5;
    private const int RenderMissesToRestart = 2;
    private DispatcherTimer? _renderCheckTimer;
    private int _renderMisses;
    private long _renderFrameStamp;
    private bool _renderRestarting;

    /// <summary>Confere, por alguns segundos, se a janela voltou a desenhar depois do jogo.</summary>
    private void StartRenderCheckAfterGame()
    {
        if (!OperatingSystem.IsWindows() || _renderRestarting) return;
        _renderCheckTimer?.Stop();
        _renderMisses = 0;
        var checks = 0;
        long asked = 0;

        void Ask()
        {
            asked = Interlocked.Increment(ref _renderFrameStamp);
            var stamp = asked;
            try
            {
                RequestAnimationFrame(_ => Interlocked.CompareExchange(ref _renderFrameStamp, -stamp, stamp));
                InvalidateVisual();
            }
            catch (Exception ex) { DebugLog.Swallowed(ex); }
        }

        Ask();
        _renderCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(RenderCheckSeconds) };
        _renderCheckTimer.Tick += (_, _) =>
        {
            // Só vale com a janela à vista: minimizada ou escondida ela não desenha mesmo.
            if (!IsVisible || WindowState == WindowState.Minimized)
            {
                _renderCheckTimer?.Stop();
                return;
            }
            var drew = Interlocked.Read(ref _renderFrameStamp) == -asked;
            checks++;
            if (drew)
            {
                if (_renderMisses > 0) DebugLog.Write("[RENDER] A janela voltou a desenhar.");
                _renderMisses = 0;
                if (checks >= 3) { _renderCheckTimer?.Stop(); return; } // ~15 s desenhando: ok
            }
            else
            {
                _renderMisses++;
                DebugLog.Write($"[RENDER] Nenhum quadro desenhado em {RenderCheckSeconds:0} s depois do jogo ({_renderMisses}/{RenderMissesToRestart}).");
                if (_renderMisses >= RenderMissesToRestart)
                {
                    _renderCheckTimer?.Stop();
                    RestartAfterFrozenScreen();
                    return;
                }
            }
            Ask();
        };
        _renderCheckTimer.Start();
    }

    private void RestartAfterFrozenScreen()
    {
        if (_renderRestarting) return;
        _renderRestarting = true;
        DebugLog.Write("[RENDER] A tela do launcher parou de desenhar depois do jogo — reiniciando o launcher.");
        if (!LauncherRestart.TryStartReplacement())
        {
            DebugLog.Write("[RENDER] Não deu para abrir a instância nova — o launcher continua como está.");
            _renderRestarting = false;
            return;
        }
        // O vigia do encerramento garante a saída mesmo se o fechamento travar no desenho.
        StartShutdownWatchdog();
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}

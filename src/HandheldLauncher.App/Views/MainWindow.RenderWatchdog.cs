using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.ViewModels;

namespace HandheldLauncher.App.Views;

/// <summary>
/// 0.28: tela cinza depois de um jogo fechar (ou travar). O launcher volta, recebe o controle
/// normalmente, mas a janela não desenha nada — o desenho da tela ficou preso na placa de vídeo
/// depois que o jogo caiu. O usuário via a tela parada e só conseguia fechar pelo Gerenciador de
/// Tarefas (e o fechamento ainda travava).
///
/// <para>Depois que um jogo fecha, este vigia pede um quadro a cada 5 s. Se em três tentativas
/// seguidas nenhum quadro for desenhado, o launcher se reinicia sozinho (a instância nova espera
/// esta sair antes de abrir). No máximo um reinício assim a cada 10 minutos.</para>
///
/// <para>O pedido de quadro do Avalonia é do processo todo e corre mesmo sem nada para redesenhar
/// ou com a janela coberta: ele mede se o desenho da tela está andando.</para>
/// </summary>
public partial class MainWindow
{
    private const double RenderCheckSeconds = 5;
    private const int RenderMissesToRestart = 3;
    private static readonly TimeSpan RenderRestartMinGap = TimeSpan.FromMinutes(10);
    private static readonly string RenderRestartStampPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HandheldLauncher", "render-restart.txt");

    private DispatcherTimer? _renderCheckTimer;
    private int _renderMisses;
    private long _renderAskId;
    private long _renderSeenId;
    private bool _renderRestarting;

    /// <summary>Confere, por alguns segundos, se a janela voltou a desenhar depois do jogo.</summary>
    private void StartRenderCheckAfterGame()
    {
        if (!OperatingSystem.IsWindows() || _renderRestarting) return;
        _renderCheckTimer?.Stop();
        _renderMisses = 0;
        var checks = 0;
        var lastTick = DateTime.UtcNow;

        // Tudo aqui roda na thread da interface (timer e pedido de quadro): campos simples bastam.
        void Ask()
        {
            var id = ++_renderAskId;
            try { RequestAnimationFrame(_ => _renderSeenId = id); }
            catch (Exception ex) { DebugLog.Swallowed(ex); }
        }

        Ask();
        _renderCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(RenderCheckSeconds) };
        _renderCheckTimer.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            var late = (now - lastTick).TotalSeconds > RenderCheckSeconds * 2.5;
            lastTick = now;

            // Só vale com a janela à vista e nada de jogo/QS no caminho.
            if (!IsVisible || WindowState == WindowState.Minimized || GameOrOverlayActive())
            {
                _renderCheckTimer?.Stop();
                return;
            }
            // Tick muito atrasado: o aparelho dormiu ou a tela apagou. Recomeça a contagem.
            if (late)
            {
                _renderMisses = 0;
                Ask();
                return;
            }

            var drew = _renderSeenId == _renderAskId;
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

    private bool GameOrOverlayActive()
        => DataContext is MainViewModel vm && (vm.RunningGame is not null || vm.IsLaunching)
           || _overlayWindow is { IsVisible: true };

    private void RestartAfterFrozenScreen()
    {
        if (_renderRestarting || GameOrOverlayActive()) return;
        try
        {
            if (File.Exists(RenderRestartStampPath)
                && DateTime.UtcNow - File.GetLastWriteTimeUtc(RenderRestartStampPath) < RenderRestartMinGap)
            {
                DebugLog.Write("[RENDER] A tela parou de desenhar de novo, mas já houve um reinício automático há menos de 10 min — não reinicia.");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(RenderRestartStampPath)!);
            File.WriteAllText(RenderRestartStampPath, DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }

        _renderRestarting = true;
        DebugLog.Write("[RENDER] A tela do launcher parou de desenhar depois do jogo — reiniciando o launcher.");
        if (!LauncherRestart.TryStartReplacement())
        {
            DebugLog.Write("[RENDER] Não deu para abrir a instância nova — o launcher continua como está.");
            _renderRestarting = false;
            return;
        }
        // Com o desenho preso, o fechamento da janela pode nunca terminar: o vigia do encerramento
        // (12 s) garante a saída, com o controle desmascarado antes.
        StartShutdownWatchdog();
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}

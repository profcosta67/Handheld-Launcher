using System.Runtime.Versioning;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Views;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// 18/11: o que o launcher faz pelos componentes de fora ao abrir, em thread de fundo:
/// <list type="number">
/// <item>copia para o debug.log o resultado da tarefa que acorda o controle antes do login;</item>
/// <item>registra (ou remove) essa tarefa conforme a opção <c>WakeControllerAtBoot</c>;</item>
/// <item>instala o que falta e vem no pacote — PawnIO (TDP) e HidHide (controle isolado);</item>
/// <item>se o RTSS não estiver instalado, mostra UMA vez por versão a tela de componentes, onde o
/// usuário escolhe instalar (ele não pode ser redistribuído no nosso pacote).</item>
/// </list>
/// Melhor esforço: cada passo vira linha no debug.log e nada aqui derruba o launcher.
/// </summary>
internal static class StartupComponents
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(12);
    private static int _started;

    public static void RunInBackground(MainWindow mainWindow)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (Interlocked.Exchange(ref _started, 1) == 1) return;

        var thread = new Thread(() => Run(mainWindow))
        {
            IsBackground = true,
            Name = "StartupComponents",
            Priority = ThreadPriority.BelowNormal,
        };
        thread.Start();
    }

    [SupportedOSPlatform("windows")]
    private static void Run(MainWindow mainWindow)
    {
        try
        {
            Thread.Sleep(StartupDelay);   // abrir o launcher vem primeiro

            ControllerWakeTask.ReportLastBootResult();

            AppSettings settings;
            try { settings = AppSettingsStore.Load(); }
            catch (Exception ex)
            {
                DebugLog.Write($"[COMPONENTES] Não foi possível ler as configurações: {ex.Message}");
                return;
            }

            ApplyControllerWakeTask(settings);
            var installed = settings.AutoInstallComponents ? SystemComponents.InstallMissingBundled() : null;

            var statuses = SystemComponents.CheckAll();
            foreach (var status in statuses)
                DebugLog.Write($"[COMPONENTES] {status.Name}: {status.State} — {status.Detail}");

            if (installed is not null)
                DebugLog.Write($"[COMPONENTES] Instalado agora: {installed}. O PawnIO e o HidHide podem pedir um reinício do Windows para valer.");

            ShowNoticeIfNeeded(mainWindow, settings, statuses);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[COMPONENTES] Verificação de abertura parou: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyControllerWakeTask(AppSettings settings)
    {
        try
        {
            var exists = ControllerWakeTask.Exists();
            if (exists is null)
            {
                DebugLog.Write("[CONTROLE-BOOT] Não foi possível conferir a tarefa de inicialização.");
                return;
            }
            if (settings.WakeControllerAtBoot == exists.Value) return;

            DebugLog.Write(settings.WakeControllerAtBoot
                ? "[CONTROLE-BOOT] Registrando a tarefa que acorda o controle antes do login."
                : "[CONTROLE-BOOT] Removendo a tarefa que acorda o controle antes do login (opção desligada).");
            ControllerWakeTask.Apply(settings.WakeControllerAtBoot);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[CONTROLE-BOOT] Falha ao ajustar a tarefa: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Aviso de componente em falta — uma vez por versão, e só com o launcher à frente e sem jogo
    /// aberto (o MainWindow decide isso). Hoje o único que precisa de pedido é o RTSS.
    /// </summary>
    private static void ShowNoticeIfNeeded(MainWindow mainWindow, AppSettings settings,
        IReadOnlyList<SystemComponents.Status> statuses)
    {
        try
        {
            var missing = statuses.Where(s => s.State == SystemComponents.State.Missing && s.CanInstallHere).ToList();
            if (missing.Count == 0) return;

            var version = typeof(StartupComponents).Assembly.GetName().Version?.ToString() ?? "dev";
            if (string.Equals(settings.ComponentsNoticeShownFor, version, StringComparison.Ordinal))
            {
                DebugLog.Write($"[COMPONENTES] Faltando: {string.Join(", ", missing.Select(m => m.Name))} — aviso já mostrado nesta versão.");
                return;
            }

            settings.ComponentsNoticeShownFor = version;
            try { AppSettingsStore.Save(settings); }
            catch (Exception ex) { DebugLog.Swallowed(ex); }

            DebugLog.Write($"[COMPONENTES] Faltando: {string.Join(", ", missing.Select(m => m.Name))} — abrindo a tela de componentes.");
            Avalonia.Threading.Dispatcher.UIThread.Post(() => mainWindow.ShowComponentsNotice());
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[COMPONENTES] Falha ao avisar sobre componentes: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

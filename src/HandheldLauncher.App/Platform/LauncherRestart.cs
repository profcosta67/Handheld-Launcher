using System.Diagnostics;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Reinicia o PRÓPRIO launcher (v0.9.21) — pedido do usuário pro seletor
/// de layout: "confirmando já reinicia o Launcher", em vez de ele ter que
/// fechar e abrir na mão.
///
/// <para>
/// <b>Por que não é só Process.Start + Close:</b> o launcher tem trava de
/// instância única por mutex (ver SingleInstance, usado em Program.Main).
/// Abrir o novo processo enquanto o antigo ainda está vivo faz o novo
/// perder a disputa do mutex, chamar NotifyExistingInstance (que só traz a
/// janela antiga pra frente) e sair — ou seja, o "reiniciar" não
/// reiniciaria nada, e falharia em SILÊNCIO.
/// </para>
///
/// <para>
/// Solução: o processo novo nasce com <c>--relaunch-after &lt;pid&gt;</c> e,
/// ANTES de disputar o mutex, espera o processo antigo morrer (ver
/// <see cref="WaitForPreviousInstance"/>, chamado no Program.Main). Sem
/// cmd.exe, sem script intermediário, sem timer chutado.
/// </para>
///
/// <para>
/// De propósito NÃO marca "saída deliberada" (WatchdogGate): aqui a gente
/// QUER o launcher de volta. E como o processo novo é aberto por nós (não
/// pelo Agendador de Tarefas) e sem <c>--watchdog</c>, o WatchdogGate o
/// trata como abertura manual e deixa abrir normalmente.
/// </para>
/// </summary>
internal static class LauncherRestart
{
    private const string WaitArgument = "--relaunch-after";

    /// <summary>
    /// Abre uma instância nova que espera esta morrer, e devolve true se
    /// conseguiu. False = NADA foi aberto, e quem chamou não deve fechar o
    /// launcher (senão o usuário fica sem nada na tela).
    /// </summary>
    public static bool TryStartReplacement()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                DebugLog.Write($"LauncherRestart: ProcessPath inválido ('{exe}') — reinício cancelado.");
                return false;
            }

            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
            };
            psi.ArgumentList.Add(WaitArgument);
            psi.ArgumentList.Add(Environment.ProcessId.ToString());

            var started = Process.Start(psi);
            DebugLog.Write($"LauncherRestart: instância nova pid={started?.Id.ToString() ?? "?"} esperando pid={Environment.ProcessId}.");
            return started is not null;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"LauncherRestart EXCEPTION: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Chamado no começo do Program.Main, ANTES da trava de instância
    /// única. Sem o argumento, retorna na hora. Com ele, espera o processo
    /// anterior sair — com teto de tempo, pra um pid que nunca morre (ou
    /// que já virou outro processo) não travar a abertura pra sempre.
    /// </summary>
    public static void WaitForPreviousInstance(string[] args)
    {
        try
        {
            var index = Array.FindIndex(args, a => string.Equals(a, WaitArgument, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || index + 1 >= args.Length) return;
            if (!int.TryParse(args[index + 1], out var pid)) return;

            Process previous;
            try
            {
                previous = Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                // Já morreu antes da gente chegar aqui — melhor caso.
                return;
            }

            using (previous)
            {
                if (!previous.WaitForExit(10_000))
                {
                    DebugLog.Write($"LauncherRestart: pid={pid} não saiu em 10s; abrindo mesmo assim (a trava de instância única decide).");
                }
            }
        }
        catch (Exception ex)
        {
            // Nunca impedir a abertura por causa da espera.
            DebugLog.Write($"LauncherRestart.WaitForPreviousInstance EXCEPTION: {ex}");
        }
    }
}

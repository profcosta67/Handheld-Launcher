using System.Diagnostics;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// v0.18 — abre o Xbox Cloud Gaming no Edge em modo app (sem barra de
/// endereço/abas) e em tela cheia (pedido do usuário: "o site abre mas não
/// em fullscreen"). O Edge vem com todo Windows 10/11.
///
/// Usa uma pasta de perfil SÓ pro xCloud (%AppData%\HandheldLauncher\xcloud\edge):
/// <list type="bullet">
/// <item>garante um processo novo do Edge, então "--start-fullscreen" vale
/// mesmo com o Edge normal do usuário já aberto (senão o pedido iria pra
/// janela já aberta e a opção seria ignorada);</item>
/// <item>permite fechar só o xCloud depois (<see cref="CloseAll"/>), sem
/// derrubar o Edge normal;</item>
/// <item>custo: o login na conta Microsoft é feito uma vez nesse perfil
/// (depois ele lembra).</item>
/// </list>
/// Se o Edge não abrir por qualquer motivo, cai pro navegador padrão.
/// </summary>
public static class XCloudLauncher
{
    private static readonly string ProfileDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HandheldLauncher", "xcloud", "edge");

    /// <summary>true depois de abrir — usado pra mostrar "Fechar Xbox Cloud Gaming" no menu.</summary>
    public static bool WasOpened { get; private set; }

    /// <summary>
    /// v0.18.1: o xCloud está aberto? Além do que esta sessão abriu
    /// (<see cref="WasOpened"/>), olha o "lockfile" que o Edge mantém na pasta
    /// do perfil enquanto roda — pega também o caso de o launcher ter sido
    /// reaberto com o xCloud ainda aberto. Barato (só um File.Exists), dá pra
    /// chamar ao montar menu.
    /// </summary>
    public static bool IsRunning
    {
        get
        {
            // v0.18.3 (revisão): o "lockfile" é a fonte da verdade (o Edge cria ao
            // abrir e o Windows apaga quando o processo termina). WasOpened sozinho
            // ficava "preso" se o usuário fechasse o Edge por conta própria; agora
            // só cobre os primeiros segundos, antes de o lockfile existir.
            try { if (File.Exists(Path.Combine(ProfileDir, "lockfile"))) return true; }
            catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }
            return WasOpened && DateTime.UtcNow - _openedAtUtc < TimeSpan.FromSeconds(20);
        }
    }

    private static DateTime _openedAtUtc = DateTime.MinValue;

    public static void Open(string url)
    {
        try
        {
            Directory.CreateDirectory(ProfileDir);
            var psi = new ProcessStartInfo
            {
                FileName = "msedge.exe", // resolvido pelo App Paths do Windows
                Arguments = $"--app=\"{url}\" --start-fullscreen --user-data-dir=\"{ProfileDir}\" --no-first-run",
                UseShellExecute = true,
            };
            Process.Start(psi);
            WasOpened = true;
            _openedAtUtc = DateTime.UtcNow;
            DebugLog.Write($"[XCLOUD] Edge (modo app, tela cheia) → {url}");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[XCLOUD] Edge não abriu ({ex.Message}) — usando o navegador padrão.");
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                WasOpened = false;
            }
            catch (Exception ex2) { DebugLog.Write($"[XCLOUD] Navegador padrão também falhou: {ex2.Message}"); }
        }
    }

    /// <summary>
    /// Fecha só as janelas do Edge abertas com o perfil do xCloud (procura
    /// pela pasta do perfil na linha de comando via WMI) — o Edge normal do
    /// usuário não é tocado.
    /// </summary>
    public static int CloseAll()
    {
        var closed = 0;
        if (!OperatingSystem.IsWindows()) return 0;
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'msedge.exe'");
            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    var cmd = obj["CommandLine"] as string;
                    if (cmd is null || cmd.IndexOf(ProfileDir, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    try
                    {
                        using var p = Process.GetProcessById(Convert.ToInt32(obj["ProcessId"]));
                        p.Kill(entireProcessTree: true);
                        closed++;
                    }
                    catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }
                }
            }
        }
        catch (Exception ex) { DebugLog.Write($"[XCLOUD] Falha ao fechar (WMI): {ex.Message}"); }
        WasOpened = false;
        DebugLog.Write(closed > 0
            ? $"[XCLOUD] Fechado ({closed} processo(s) do perfil do xCloud)."
            : $"[XCLOUD] Fechar: nenhum processo do Edge com o perfil {ProfileDir} foi encontrado.");
        return closed;
    }
}

namespace HandheldLauncher.App.Diagnostics;

/// <summary>
/// Rede de segurança para manipuladores de eventos assíncronos (<c>async void</c>). Uma exceção
/// que escapa de um <c>async void</c> sobe para a thread da interface e derruba o launcher inteiro
/// (inclusive com um jogo aberto). Aqui ela vira uma linha no log, e o launcher segue.
/// </summary>
public static class UiSafe
{
    public static async void Run(Func<Task> action, string name)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // cancelamento é esperado (janela fechada, nova busca)
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[UI-ERRO] {name}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{FirstLines(ex.StackTrace, 6)}");
        }
    }

    private static string FirstLines(string? text, int count) =>
        string.IsNullOrEmpty(text) ? "" : string.Join(Environment.NewLine, text.Split('\n').Take(count).Select(l => "    " + l.TrimEnd()));
}

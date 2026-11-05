namespace HandheldLauncher.App.Diagnostics;

/// <summary>
/// Dirt-simple file logger added specifically to chase the "Escolher capa
/// does nothing" report blind — there's no debugger attached to the user's
/// machine from here, and the sandbox this was written in can't build/run
/// the project either. Appends timestamped lines to
/// %AppData%\HandheldLauncher\debug.log. Every call is wrapped in try/catch
/// so a logging failure (locked file, no disk space, whatever) can never
/// be the thing that breaks the feature it's trying to diagnose.
///
/// This is temporary scaffolding, not a permanent feature — fine to rip
/// out (or leave quietly running, it's cheap) once the real bug is found.
/// </summary>
public static class DebugLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HandheldLauncher", "debug.log");

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> SwallowedSites = new();

    /// <summary>
    /// v0.16.4 (plano da v0.16: "trocar os catch vazios por registro no log"):
    /// registra uma exceção que o código decidiu ignorar — UMA vez por ponto do
    /// código (arquivo:linha) por sessão, pra laços que rodam toda hora não
    /// encherem o log. O comportamento não muda: a exceção continua ignorada.
    /// </summary>
    public static void Swallowed(Exception ex,
        [System.Runtime.CompilerServices.CallerMemberName] string member = "",
        [System.Runtime.CompilerServices.CallerFilePath] string file = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        try
        {
            var site = $"{Path.GetFileName(file)}:{line}";
            if (!SwallowedSites.TryAdd(site, 0)) return;
            Write($"[IGNORADO] {site} ({member}): {ex.GetType().Name}: {ex.Message} (próximas vezes neste ponto não são registradas)");
        }
        catch
        {
            // O registro nunca pode quebrar quem chamou.
        }
    }

    // v0.18.3 (pente fino de desempenho): antes, CADA linha abria, gravava e
    // fechava o arquivo na hora, na thread que chamou — quase sempre a da tela.
    // O controle registra cada aperto/solta/repetição, então navegar segurando
    // um direcional fazia várias gravações em disco por segundo na thread da
    // UI (antivírus olhando cada gravação deixa isso ainda mais caro) = os
    // pequenos engasgos na navegação. Agora a linha entra numa fila (com o
    // horário do momento, então a ordem e os horários continuam exatos) e uma
    // thread de fundo grava em lote a cada 200 ms. Flush() grava na hora — usado
    // na saída do app, em crash e antes de empacotar o log no "reportar erro".
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> Pending = new();
    private static readonly object FileLock = new();
    private static int _writerStarted;

    public static void Write(string message)
    {
        try
        {
            Pending.Enqueue($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}\n");
            EnsureWriter();
        }
        catch
        {
            // Logging must never be the thing that breaks the app.
        }
    }

    private static void EnsureWriter()
    {
        if (Interlocked.Exchange(ref _writerStarted, 1) != 0) return;
        try
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
            var thread = new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(200);
                    Flush();
                }
            })
            { IsBackground = true, Name = "DebugLog", Priority = ThreadPriority.BelowNormal };
            thread.Start();
        }
        catch
        {
            // Sem thread de fundo: cada Write ainda chega no disco via Flush abaixo.
            _writerStarted = 0;
            Flush();
        }
    }

    /// <summary>Grava agora tudo que está na fila (saída do app, crash, antes de copiar o log).</summary>
    public static void Flush()
    {
        try
        {
            lock (FileLock)
            {
                if (Pending.IsEmpty) return;
                var sb = new System.Text.StringBuilder();
                while (Pending.TryDequeue(out var line)) sb.Append(line);

                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                // v0.9.0: o log não cresce pra sempre — passou de 1 MB, vira
                // debug.old.log (o anterior é descartado). Ele nunca contém
                // chaves/senhas, mas não há motivo pra guardar meses de histórico.
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > 1024 * 1024)
                {
                    File.Move(LogPath, Path.Combine(dir ?? string.Empty, "debug.old.log"), overwrite: true);
                }

                File.AppendAllText(LogPath, sb.ToString());
            }
        }
        catch
        {
            // Logging must never be the thing that breaks the app.
        }
    }
}

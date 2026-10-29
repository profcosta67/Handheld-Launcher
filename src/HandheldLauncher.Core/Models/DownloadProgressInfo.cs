namespace HandheldLauncher.Core.Models;

/// <summary>
/// Um jogo que a Steam está baixando/atualizando AGORA MESMO — pedido do
/// usuário ("gerenciador de download... pra olhar os jogos que estão
/// sendo baixados nos outros launchers"). Lido direto dos arquivos
/// `appmanifest_*.acf` que a própria Steam já mantém no disco (mesmo
/// mecanismo que ISteamLibraryScanner já usa pra listar jogos instalados
/// — ver SteamLibraryScanner.ScanActiveDownloads), sem precisar de API
/// nem do cliente Steam rodando.
///
/// Só Steam por enquanto — Epic e o Xbox app não escrevem o progresso de
/// download num arquivo simples de ler como a Steam faz; ver README pra
/// essa limitação.
/// </summary>
public sealed class DownloadProgressInfo
{
    public required string Title { get; set; }

    public GameSource Source { get; set; } = GameSource.Steam;

    /// <summary>0-100.</summary>
    public double PercentComplete { get; set; }

    public long BytesDownloaded { get; set; }

    public long BytesToDownload { get; set; }

    /// <summary>
    /// Velocidade medida pelo launcher (diferença de BytesDownloaded entre
    /// duas leituras do .acf ÷ tempo). Null = ainda sem duas leituras, ou
    /// parado.
    /// </summary>
    public double? BytesPerSecond { get; set; }

    /// <summary>"12.4 MB/s · faltam ~8 min" — vazio enquanto não há velocidade medida.</summary>
    public string DetailText
    {
        get
        {
            var percent = $"{PercentComplete:0}%";
            if (BytesPerSecond is not { } speed || speed <= 0) return percent;

            var mbps = speed / (1024 * 1024);
            var text = $"{percent} · {mbps.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} MB/s";
            var left = BytesToDownload - BytesDownloaded;
            if (left > 0)
            {
                var eta = TimeSpan.FromSeconds(left / speed);
                text += eta.TotalHours >= 1
                    ? $" · faltam ~{(int)eta.TotalHours}h{eta.Minutes:00}"
                    : $" · faltam ~{Math.Max(1, (int)Math.Ceiling(eta.TotalMinutes))} min";
            }
            return text;
        }
    }
}

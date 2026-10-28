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
}

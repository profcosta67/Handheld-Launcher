using System.Diagnostics;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// Overlay de FPS/performance via RTSS (RivaTuner Statistics Server) em
/// vez do overlay nativo do Legion Space — pedido explícito do usuário
/// ("sem usar aquele nativo do legion").
///
/// Deliberadamente NÃO construímos um overlay nosso do zero: desenhar por
/// cima de um jogo de verdade exige hookar a chamada Present() de
/// DirectX/Vulkan/OpenGL, o que na prática significa injetar uma DLL no
/// processo do jogo (técnica C++ nível de sistema, fora do que .NET/
/// Avalonia fazem). Isso traz dois problemas sérios: (1) jogos com
/// anti-cheat online costumam banir por injeção de DLL não assinada,
/// mesmo quando a intenção é inofensiva; (2) nada disso dá pra testar
/// neste sandbox (nem localmente, nem contra jogos reais) — um hook
/// quebrado pode no mínimo não funcionar, no pior caso travar o jogo.
///
/// RTSS já faz exatamente isso, há anos, de um jeito maduro e
/// geralmente aceito pela maioria dos anti-cheats (é a mesma base usada
/// por MSI Afterburner). Então o papel deste launcher aqui é só: 1)
/// detectar se está instalado, 2) deixar ele rodando em segundo plano
/// (ele mesmo faz o hook em qualquer jogo que abrir depois — não
/// precisamos avisar qual jogo é qual), 3) se não estiver instalado, dar
/// um jeito fácil de ir buscar. A CONFIGURAÇÃO do overlay em si (quais
/// informações mostrar, posição na tela, etc.) continua sendo feita
/// dentro do próprio RTSS, na primeira vez que o usuário abrir a janela
/// dele — isso não é algo que replicamos aqui.
/// </summary>
public sealed class PerformanceOverlayService : IPerformanceOverlayService
{
    // RTSS é sempre um app 32-bit (até em Windows 64-bit, pra conseguir
    // hookar jogos de ambas arquiteturas) — mesma pasta de instalação
    // desde as primeiras versões, então um caminho fixo já cobre a
    // instalação padrão; "Program Files" (sem "(x86)") cobre o caso raro
    // de alguém ter forçado a instalação lá.
    private static readonly string[] CommonInstallPaths =
    {
        @"C:\Program Files (x86)\RivaTuner Statistics Server\RTSS.exe",
        @"C:\Program Files\RivaTuner Statistics Server\RTSS.exe",
    };

    private const string DownloadPageUrl = "https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/";

    public string? FindInstalledExecutable()
    {
        foreach (var path in CommonInstallPaths)
        {
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public bool IsInstalled => FindInstalledExecutable() is not null;

    public bool IsRunning
    {
        get
        {
            try
            {
                return Process.GetProcessesByName("RTSS").Length > 0;
            }
            catch
            {
                // Melhor esforço — se checar processos falhar por algum
                // motivo nesta máquina, trata como "não sei", não como
                // "travou o app".
                return false;
            }
        }
    }

    public void EnsureRunning()
    {
        if (IsRunning) return;

        var exePath = FindInstalledExecutable();
        if (exePath is null) return; // não instalado - nada a fazer, sem erro

        try
        {
            // RTSS abre minimizado na bandeja por padrão (sem janela
            // própria) — não precisa de nenhuma flag extra pra isso.
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Melhor esforço — nunca deixa isso derrubar a abertura do launcher.
        }
    }

    public void OpenDownloadPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DownloadPageUrl,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Melhor esforço.
        }
    }
}

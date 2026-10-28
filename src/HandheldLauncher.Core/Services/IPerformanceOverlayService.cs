namespace HandheldLauncher.Core.Services;

/// <summary>
/// Integração com o RTSS (RivaTuner Statistics Server) pro overlay de
/// FPS/performance — pedido do usuário como alternativa ao overlay
/// nativo do Legion Space ("será que conseguimos por nativamente aquele
/// mangohud pra ver fps etc? Sem usar aquele nativo do legion"). Ver
/// PerformanceOverlayService pra por que NÃO construímos um overlay
/// nosso do zero.
/// </summary>
public interface IPerformanceOverlayService
{
    bool IsInstalled { get; }

    bool IsRunning { get; }

    /// <summary>Melhor esforço: não faz nada (sem erro) se não estiver instalado.</summary>
    void EnsureRunning();

    /// <summary>Abre a página de download do RTSS no navegador padrão.</summary>
    void OpenDownloadPage();
}

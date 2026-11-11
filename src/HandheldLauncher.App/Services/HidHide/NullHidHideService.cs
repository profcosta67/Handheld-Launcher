namespace HandheldLauncher.Services.HidHide;

/// <summary>
/// Substituto inerte do HidHide para quando o driver não está acessível — por
/// exemplo quando o launcher corre **sem administrador** (v0.24: o manifest é
/// asInvoker). Nada é escondido e nada é restaurado; o resto do launcher
/// funciona igual.
/// </summary>
public sealed class NullHidHideService : IHidHideService
{
    public bool IsAvailable => false;

    /// <summary>Sem driver não há mascaramento — nunca "pronto".</summary>
    public bool IsReady => false;

    /// <inheritdoc />
    public string StatusDetail => "driver do HidHide indisponível";

    public string? ControllerInstanceId => null;

    public void Initialize()
    {
    }

    public void HideControllerForProcess(int processId)
    {
    }

    public void RestoreControllerForProcess(int processId)
    {
    }

    public void ForceRestartController(bool includeXusb) { }

    public void ForceRestartController()
    {
    }
}

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
}

namespace HandheldLauncher.Services.HidHide;

public interface IHidHideService
{
    bool IsAvailable { get; }

    /// <summary>
    /// v0.24.1: o mascaramento está REALMENTE configurado? Falso quando o
    /// driver está ausente, o controle não foi encontrado ou a configuração
    /// não foi aceite (típico sem administrador). Ver <see cref="StatusDetail"/>.
    /// </summary>
    bool IsReady { get; }

    /// <summary>Motivo curto do estado atual — aparece no log e na Saúde do aparelho.</summary>
    string StatusDetail { get; }
    
    /// <summary>
    /// O DeviceInstanceId do controle físico detectado (ex: HID\VID_17EF&PID_61ED...).
    /// Retorna null se nenhum controle for encontrado.
    /// </summary>
    string? ControllerInstanceId { get; }

    void Initialize();
    void HideControllerForProcess(int processId);
    void RestoreControllerForProcess(int processId);
}
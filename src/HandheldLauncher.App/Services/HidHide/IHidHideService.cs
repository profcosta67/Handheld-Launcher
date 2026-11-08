namespace HandheldLauncher.Services.HidHide;

public interface IHidHideService
{
    bool IsAvailable { get; }
    
    /// <summary>
    /// O DeviceInstanceId do controle físico detectado (ex: HID\VID_17EF&PID_61ED...).
    /// Retorna null se nenhum controle for encontrado.
    /// </summary>
    string? ControllerInstanceId { get; }

    void Initialize();
    void HideControllerForProcess(int processId);
    void RestoreControllerForProcess(int processId);
}
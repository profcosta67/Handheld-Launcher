using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using HandheldLauncher.Core.Services;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

public class WindowsHardwareService : IHardwareService
{
    private readonly string _ryzenAdjPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ryzenadj.exe");
    private readonly string _ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "ffmpeg.exe");

    private RyzenSmuService? _smu;
    private byte[]? _smuModule;

    private Process? _ffmpegProcess;
    public bool IsRecording => _ffmpegProcess != null && !_ffmpegProcess.HasExited;

    // ==========================================
    // TDP
    // ==========================================
    private bool InitializePawnIo()
    {
        if (_smu != null) return true;
        try
        {
            var modulePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "RyzenSMU.bin");
            if (!File.Exists(modulePath)) return false;
            _smuModule = File.ReadAllBytes(modulePath);
            _smu = new RyzenSmuService();
            return _smu.Initialize(_smuModule);
        }
        catch (Exception ex) { DebugLog.Write($"[PawnIO] Falha ao inicializar: {ex.Message}"); return false; }
    }

    public void SetTdpMode(string mode)
    {
        if (!OperatingSystem.IsWindows()) return;
        int tdpLimit = mode switch { "Eco" => 8000, "Normal" => 15000, "Desempenho" => 25000, "Boost" => 30000, _ => 15000 };

        if (InitializePawnIo())
        {
            bool stapmOk = _smu!.SendSmuCommand("stapm-limit", (uint)tdpLimit);
            bool fastOk = _smu.SendSmuCommand("fast-limit", (uint)tdpLimit);
            bool slowOk = _smu.SendSmuCommand("slow-limit", (uint)tdpLimit);
            if (stapmOk && fastOk && slowOk) { DebugLog.Write($"[PawnIO] TDP alterado para {tdpLimit}mW."); return; }
        }

        try
        {
            string arguments = $"--stapm-limit={tdpLimit} --fast-limit={tdpLimit} --slow-limit={tdpLimit} --tctl-temp=90";
            DebugLog.Write($"[RyzenAdj] Fallback: {arguments}");
            var psi = new ProcessStartInfo { FileName = _ryzenAdjPath, Arguments = arguments, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using var process = Process.Start(psi);
            process?.WaitForExit(2000);
            DebugLog.Write($"[RyzenAdj] TDP definido para {tdpLimit}mW (fallback)");
        }
        catch (Exception ex) { DebugLog.Write($"[RyzenAdj] Erro: {ex.Message}"); }
    }

    // ==========================================
    // VOLUME, BRILHO, COR
    // ==========================================
    public void SetVolume(double level) { /* código existente */ }
    public void SetBrightness(double level) { /* código existente */ }
    public void SetColorMode(string mode) { /* código existente */ }
    public void SetPerformanceHudEnabled(bool enabled) { /* código existente */ }

    // ==========================================
    // GRAVAÇÃO
    // ==========================================
    public void ToggleScreenRecording()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (IsRecording) StopRecording(); else StartRecording();
    }

    private void StartRecording()
    {
        try
        {
            string videosPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            string outputPath = Path.Combine(videosPath, $"HandheldLauncher_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
            string arguments = $"-y -f gdigrab -framerate 60 -i desktop -c:v libx264 -preset ultrafast -crf 23 \"{outputPath}\"";
            var psi = new ProcessStartInfo { FileName = _ffmpegPath, Arguments = arguments, CreateNoWindow = true, UseShellExecute = false, RedirectStandardInput = true };
            _ffmpegProcess = Process.Start(psi);
            DebugLog.Write($"[GRAVADOR] Iniciado. Salvando em: {outputPath}");
        }
        catch (Exception ex) { DebugLog.Write($"[GRAVADOR] Erro: {ex.Message}"); _ffmpegProcess = null; }
    }

    private void StopRecording()
    {
        try
        {
            if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
            {
                _ffmpegProcess.StandardInput.WriteLine("q");
                _ffmpegProcess.WaitForExit(3000);
                if (!_ffmpegProcess.HasExited) _ffmpegProcess.Kill();
            }
            DebugLog.Write("[GRAVADOR] Parado.");
        }
        catch (Exception ex) { DebugLog.Write($"[GRAVADOR] Erro ao parar: {ex.Message}"); }
        finally { _ffmpegProcess = null; }
    }

    // ==========================================
    // RESOLUÇÃO E HZ
    // ==========================================
    [DllImport("user32.dll")]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll")]
    private static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
    }

    private const int DM_PELSWIDTH = 0x80000;
    private const int DM_PELSHEIGHT = 0x100000;
    private const int DM_DISPLAYFREQUENCY = 0x400000;
    private const int CDS_UPDATEREGISTRY = 0x01;
    private const int CDS_TEST = 0x02;
    private const int DISP_CHANGE_SUCCESSFUL = 0;

    public string GetDisplayInfo()
    {
        if (!OperatingSystem.IsWindows()) return "Mac / Linux";
        try
        {
            var devMode = new DEVMODE { dmDeviceName = new string(new char[32]) };
            devMode.dmSize = (short)Marshal.SizeOf(devMode);
            if (EnumDisplaySettings(null, -1, ref devMode))
                return $"{devMode.dmPelsWidth}x{devMode.dmPelsHeight} {devMode.dmDisplayFrequency}Hz";
        }
        catch { }
        return "--";
    }

    public void SetDisplayMode(int width, int height, int refreshRate)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var devMode = new DEVMODE { dmDeviceName = new string(new char[32]) };
            devMode.dmSize = (short)Marshal.SizeOf(devMode);
            if (!EnumDisplaySettings(null, -1, ref devMode)) return;

            devMode.dmPelsWidth = width;
            devMode.dmPelsHeight = height;
            devMode.dmDisplayFrequency = refreshRate;
            devMode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;

            if (ChangeDisplaySettings(ref devMode, CDS_TEST) != DISP_CHANGE_SUCCESSFUL) { DebugLog.Write($"[TELA] Não suportado."); return; }
            if (ChangeDisplaySettings(ref devMode, CDS_UPDATEREGISTRY) == DISP_CHANGE_SUCCESSFUL) { DebugLog.Write($"[TELA] Alterado para {width}x{height}@{refreshRate}Hz."); }
        }
        catch (Exception ex) { DebugLog.Write($"[TELA] Erro: {ex.Message}"); }
    }
}

// VolumeMixer (o mesmo que você já tem)
internal static class VolumeMixer
{
    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int NotImpl1();
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        // A ordem precisa corresponder exatamente ao vtable COM do Windows.
        [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int GetChannelCount(out uint channelCount);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(Guid eventContext);
        [PreserveSig] int VolumeStepDown(Guid eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }

    public static bool SetMasterVolume(float level)
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.GetDefaultAudioEndpoint(0, 1, out var device);
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 23, IntPtr.Zero, out var obj);
            var volume = (IAudioEndpointVolume)obj;
            volume.SetMasterVolumeLevelScalar(level, Guid.Empty);
            return true;
        }
        catch { return false; }
    }
}
using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Threading;
using System.Runtime.InteropServices;
using HandheldLauncher.Core.Services;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

public enum RecordingState { Stopped, Recording, Error }

public class WindowsHardwareService : IHardwareService
{
    private readonly string _ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "ffmpeg.exe");

    private RyzenSmuService? _smu;
    private byte[]? _smuModule;

    private Process? _ffmpegProcess;
    private string? _lastOutputPath;
    private RecordingState _recordingState = RecordingState.Stopped;

    public bool IsRecording => _recordingState == RecordingState.Recording;
    public RecordingState RecordingState => _recordingState;
    public event Action<RecordingState>? RecordingStateChanged;

    private void SetRecordingState(RecordingState state)
    {
        if (_recordingState == state) return;
        _recordingState = state;
        try { RecordingStateChanged?.Invoke(state); } catch (Exception ex) { DebugLog.Write("[GRAVADOR] Handler de estado falhou: " + ex.Message); }
    }

    // ==========================================
    // TDP
    // ==========================================
    private bool InitializePawnIo()
    {
        if (_smu?.IsInitialized == true) return true;

        _smu?.Dispose();
        _smu = null;

        if (!RyzenSmuService.IsSupportedCpu())
        {
            DebugLog.Write("[PawnIO] Este processador ainda não possui mapeamento SMU validado. Nenhum comando TDP será enviado.");
            return false;
        }

        try
        {
            var modulePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "RyzenSMU.bin");
            if (!File.Exists(modulePath))
            {
                DebugLog.Write("[PawnIO] RyzenSMU.bin não encontrado; provider PawnIO indisponível.");
                return false;
            }

            _smuModule = File.ReadAllBytes(modulePath);
            var service = new RyzenSmuService();
            if (!service.Initialize(_smuModule))
            {
                service.Dispose();
                return false;
            }

            _smu = service;
            return true;
        }
        catch (Exception ex)
        {
            _smu?.Dispose();
            _smu = null;
            DebugLog.Write($"[PawnIO] Falha ao inicializar: {ex.Message}");
            return false;
        }
    }

    public bool SetTdpMode(string mode)
    {
        if (!OperatingSystem.IsWindows()) return false;

        // v0.18.5: teto do aparelho agora (firmware Lenovo / tabela ASUS /
        // 30 W), já considerando tomada ou bateria.
        var limits = DeviceTdpLimits.Current();
        bool isCustom = HandheldLauncher.Core.Services.TdpProfiles.IsCustom(mode);

        // Perfis: os mesmos watts de sempre (modo desconhecido = 20 W), o
        // mesmo valor nos três limites, só baixados se a BATERIA permitir
        // menos que isso (ex.: Boost 30 W num Ally fora da tomada → 25 W).
        // Personalizado: SPL escolhido + picos na proporção do fabricante.
        int asked = HandheldLauncher.Core.Services.TdpProfiles.WattsFor(mode) ?? 20;
        int stapmW, slowW, fastW;
        if (isCustom)
        {
            (stapmW, slowW, fastW) = limits.PeaksFor(asked);
        }
        else
        {
            stapmW = slowW = fastW = Math.Min(asked, limits.SplMax);
        }

        DebugLog.Write(isCustom || stapmW != asked
            ? $"[TDP] Solicitação do perfil {mode}: SPL {stapmW} W / SPPT {slowW} W / FPPT {fastW} W (pedido {asked} W; teto {limits.SplMax}/{limits.SpptMax}/{limits.FpptMax} W na {(limits.OnAc ? "tomada" : "bateria")}, {limits.Source}). Provider permitido: PawnIO."
            : "[TDP] Solicitação do perfil " + mode + ": " + (stapmW * 1000) + "mW. Provider permitido: PawnIO.");
        if (!InitializePawnIo())
        {
            DebugLog.Write("[TDP] PawnIO não inicializado. Perfil não aplicado; nenhum fallback WinRing0/RyzenAdj será executado.");
            return false;
        }

        try
        {
            bool applied = isCustom
                ? _smu!.SetPowerLimits((uint)stapmW * 1000, (uint)slowW * 1000, (uint)fastW * 1000,
                                       (uint)limits.SplMax * 1000, (uint)limits.SpptMax * 1000, (uint)limits.FpptMax * 1000)
                : _smu!.SetPowerLimits((uint)stapmW * 1000); // perfis: caminho e trava (30 W) de sempre
            DebugLog.Write(applied
                ? $"[TDP] PawnIO aceitou os três comandos ({stapmW}/{slowW}/{fastW} W). Confirme os limites efetivos pelos sensores do hardware."
                : $"[TDP] PawnIO não confirmou todos os comandos ({stapmW}/{slowW}/{fastW} W).");
            return applied;
        }
        catch (Exception ex)
        {
            DebugLog.Write("[TDP] Falha ao aplicar perfil " + mode + " via PawnIO: " + ex);
            return false;
        }
    }

    // ==========================================
    // VOLUME, BRILHO E COR
    // ==========================================
    public void SetVolume(double level)
    {
        if (!OperatingSystem.IsWindows()) return;
        float normalized = (float)(Math.Clamp(level, 0, 100) / 100.0);
        DebugLog.Write("[VOLUME] Solicitação do slider: " + normalized.ToString("P0") + ".");
        bool success = VolumeMixer.SetMasterVolume(normalized);
        DebugLog.Write(success
            ? $"[VOLUME] Volume principal ajustado para {normalized:P0}."
            : "[VOLUME] Não foi possível ajustar o dispositivo de áudio padrão.");
    }

    public void SetBrightness(double level)
    {
        if (!OperatingSystem.IsWindows()) return;

        byte brightness = (byte)Math.Clamp(Math.Round(level), 0, 100);
        DebugLog.Write("[BRILHO] Solicitação do slider: " + brightness + "%.");
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT * FROM WmiMonitorBrightnessMethods");

            var methods = searcher.Get();
            bool applied = false;
            foreach (ManagementObject monitor in methods)
            {
                try
                {
                    monitor.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, brightness });
                    applied = true;
                }
                finally
                {
                    monitor.Dispose();
                }
            }

            DebugLog.Write(applied
                ? $"[BRILHO] Brilho solicitado em {brightness}%."
                : "[BRILHO] O monitor não expõe WmiMonitorBrightnessMethods; nenhum ajuste aplicado.");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[BRILHO] Falha ao ajustar brilho: {ex.Message}");
        }
    }

    private static readonly object GammaLock = new();
    private static GammaRamp? _originalGammaRamp;

    [StructLayout(LayoutKind.Sequential)]
    private struct GammaRamp
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Red;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Green;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Blue;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll", EntryPoint = "GetDeviceGammaRamp", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDeviceGammaRamp(IntPtr hdc, ref GammaRamp ramp);

    [DllImport("gdi32.dll", EntryPoint = "SetDeviceGammaRamp", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDeviceGammaRamp(IntPtr hdc, ref GammaRamp ramp);

    public bool SetColorMode(string mode)
    {
        if (!OperatingSystem.IsWindows()) return false;

        // v0.16.2: o Quick Settings passa a usar o MESMO sistema de cor da
        // abertura do launcher e das Configurações (DisplayColorManager).
        // Antes, este método tinha uma base própria (rampa neutra) e efeitos
        // próprios, sem avisar o DisplayColorManager — os dois sistemas se
        // desencontravam: o launcher abria com a cor salva, o Quick Settings
        // mostrava "Padrão", e trocar de modo somava efeitos. force: true
        // porque o Windows pode ter resetado a curva por baixo.
        var index = DisplayColorManager.IndexFromName(mode);
        var applied = DisplayColorManager.Apply(index, force: true);
        DebugLog.Write(applied
            ? $"[COR] Perfil {mode} aplicado (Quick Settings → DisplayColorManager)."
            : $"[COR] Perfil {mode} não pôde ser aplicado; veja as linhas [DisplayColor].");
        return applied;
    }

    // Os helpers abaixo (CreateIdentityGammaRamp/CloneGammaRamp/BuildGammaRamp,
    // _originalGammaRamp e GammaLock) eram do sistema de cor antigo do Quick
    // Settings e não são mais usados desde a v0.16.2. Mantidos até o dono do
    // projeto aprovar a remoção.

    private static GammaRamp CreateIdentityGammaRamp()
    {
        var ramp = new GammaRamp
        {
            Red = new ushort[256],
            Green = new ushort[256],
            Blue = new ushort[256]
        };

        for (int i = 0; i < 256; i++)
        {
            ushort value = (ushort)(i * 257);
            ramp.Red[i] = ramp.Green[i] = ramp.Blue[i] = value;
        }

        return ramp;
    }

    private static GammaRamp CloneGammaRamp(GammaRamp source) => new()
    {
        Red = (ushort[])source.Red.Clone(),
        Green = (ushort[])source.Green.Clone(),
        Blue = (ushort[])source.Blue.Clone()
    };

    private static GammaRamp BuildGammaRamp(GammaRamp source, double contrast, double redGain, double greenGain, double blueGain)
    {
        var result = new GammaRamp
        {
            Red = new ushort[256],
            Green = new ushort[256],
            Blue = new ushort[256]
        };

        for (int i = 0; i < 256; i++)
        {
            result.Red[i] = TransformGammaValue(source.Red[i], contrast, redGain);
            result.Green[i] = TransformGammaValue(source.Green[i], contrast, greenGain);
            result.Blue[i] = TransformGammaValue(source.Blue[i], contrast, blueGain);
        }

        return result;
    }

    private static ushort TransformGammaValue(ushort original, double contrast, double gain)
    {
        double normalized = original / 65535.0;
        double adjusted = Math.Clamp(((normalized - 0.5) * contrast + 0.5) * gain, 0.0, 1.0);
        return (ushort)Math.Clamp(Math.Round(adjusted * 65535.0), 0, 65535);
    }

    public void SetPerformanceHudEnabled(bool enabled)
    {
        DebugLog.Write($"[HUD] Solicitação de estado recebida: {(enabled ? "ativar" : "desativar")}.");
    }

    // ==========================================
    // GRAVAÇÃO  (v0.14.3 — blindada)
    // ==========================================
    public void ToggleScreenRecording()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (_recordingState == RecordingState.Recording) StopRecording();
        else StartRecording();
    }

    private static bool IsFfmpegUsable(string path)
    {
        try
        {
            if (!File.Exists(path)) { DebugLog.Write($"[GRAVADOR] ffmpeg.exe não encontrado em {path}"); return false; }
            var info = new FileInfo(path);
            if (info.Length < 1024) { DebugLog.Write($"[GRAVADOR] ffmpeg.exe suspeito (tamanho {info.Length} bytes) em {path}"); return false; }
            // Abrir para leitura confirma que o arquivo é acessível e não está travado/corrompido
            using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return fs.Length > 0;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[GRAVADOR] ffmpeg inválido em {path}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private void StartRecording()
    {
        if (_recordingState == RecordingState.Recording)
        {
            DebugLog.Write("[GRAVADOR] Já está gravando — ignorando StartRecording.");
            return;
        }

        if (!IsFfmpegUsable(_ffmpegPath))
        {
            SetRecordingState(RecordingState.Error);
            DebugLog.Write("[GRAVADOR] Pré-validação do ffmpeg falhou — gravação não iniciada.");
            return;
        }

        try
        {
            string videosPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            string outputPath = Path.Combine(videosPath, $"HandheldLauncher_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
            string arguments = $"-y -f gdigrab -framerate 60 -i desktop -c:v libx264 -preset ultrafast -crf 23 \"{outputPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,   // v0.14.3: captura logs do ffmpeg pra debug
                RedirectStandardOutput = true
            };

            _ffmpegProcess = Process.Start(psi);
            if (_ffmpegProcess is null)
            {
                SetRecordingState(RecordingState.Error);
                DebugLog.Write("[GRAVADOR] Process.Start retornou null.");
                return;
            }

            // Drena stdout/stderr em background pra não travar o pipe do ffmpeg
            _ffmpegProcess.BeginOutputReadLine();
            _ffmpegProcess.BeginErrorReadLine();

            _lastOutputPath = outputPath;
            SetRecordingState(RecordingState.Recording);
            DebugLog.Write($"[GRAVADOR] Iniciado (PID {_ffmpegProcess.Id}). Salvando em: {outputPath}");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[GRAVADOR] Erro ao iniciar: {ex.GetType().Name}: {ex.Message}");
            _ffmpegProcess = null;
            _lastOutputPath = null;
            SetRecordingState(RecordingState.Error);
        }
    }

    private void StopRecording()
    {
        var pathToValidate = _lastOutputPath;
        try
        {
            if (_ffmpegProcess is null || _ffmpegProcess.HasExited)
            {
                DebugLog.Write("[GRAVADOR] StopRecording chamado sem processo ativo.");
                // Mesmo assim, valida o que tiver no _lastOutputPath
            }
            else
            {
                try
                {
                    _ffmpegProcess.StandardInput.WriteLine("q");
                    _ffmpegProcess.StandardInput.Flush();
                }
                catch (Exception ex)
                {
                    DebugLog.Write("[GRAVADOR] Falha ao enviar 'q' pro ffmpeg: " + ex.Message);
                }

                if (!_ffmpegProcess.WaitForExit(5000))
                {
                    DebugLog.Write("[GRAVADOR] FFmpeg não respondeu em 5s. Forçando encerramento (arquivo pode estar corrompido).");
                    try { _ffmpegProcess.Kill(entireProcessTree: true); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
                    try { _ffmpegProcess.WaitForExit(2000); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
                    SetRecordingState(RecordingState.Error);
                    return;
                }

                DebugLog.Write($"[GRAVADOR] FFmpeg finalizou com exit code {_ffmpegProcess.ExitCode}.");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[GRAVADOR] Erro ao parar: {ex.GetType().Name}: {ex.Message}");
            SetRecordingState(RecordingState.Error);
            return;
        }
        finally
        {
            try { _ffmpegProcess?.Dispose(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
            _ffmpegProcess = null;
        }

        // ── Validação do arquivo gerado ──
        if (string.IsNullOrEmpty(pathToValidate))
        {
            SetRecordingState(RecordingState.Error);
            DebugLog.Write("[GRAVADOR] Caminho de saída desconhecido — impossível validar.");
            return;
        }

        if (!File.Exists(pathToValidate))
        {
            SetRecordingState(RecordingState.Error);
            DebugLog.Write($"[GRAVADOR] Arquivo não encontrado após parada: {pathToValidate}");
            return;
        }

        var fi = new FileInfo(pathToValidate);
        if (fi.Length <= 0)
        {
            SetRecordingState(RecordingState.Error);
            DebugLog.Write($"[GRAVADOR] Arquivo gerado está vazio (0 bytes): {pathToValidate}");
            return;
        }

        SetRecordingState(RecordingState.Stopped);
        DebugLog.Write($"[GRAVADOR] Parado com sucesso: {pathToValidate} ({fi.Length:N0} bytes)");
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
        catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
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

// VolumeMixer
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
        [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int GetChannelCount(out uint channelCount);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(ref Guid eventContext);
        [PreserveSig] int VolumeStepDown(ref Guid eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }

    public static bool SetMasterVolume(float level)
    {
        object? enumeratorObject = null;
        object? deviceObject = null;
        object? volumeObject = null;
        try
        {
            enumeratorObject = new MMDeviceEnumeratorComObject();
            var enumerator = (IMMDeviceEnumerator)enumeratorObject;
            int hr = enumerator.GetDefaultAudioEndpoint(0, 1, out var device);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            deviceObject = device;

            var iid = typeof(IAudioEndpointVolume).GUID;
            hr = device.Activate(ref iid, 23, IntPtr.Zero, out var activated);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            volumeObject = activated;

            var volume = (IAudioEndpointVolume)volumeObject;
            Guid eventContext = Guid.Empty;
            hr = volume.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref eventContext);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write("[VOLUME] Core Audio falhou: " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
        finally
        {
            ReleaseComObject(volumeObject);
            ReleaseComObject(deviceObject);
            ReleaseComObject(enumeratorObject);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is null || !Marshal.IsComObject(value)) return;
        try { Marshal.FinalReleaseComObject(value); }
        catch (Exception ex) { DebugLog.Write("[VOLUME] Falha ao liberar objeto COM: " + ex.Message); }
    }
}
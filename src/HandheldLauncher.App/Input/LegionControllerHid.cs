using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HandheldLauncher.App.Input;

/// <summary>
/// 0.27 beta.72 — conversa direto com a interface de configuração (HID) do controle do
/// Legion Go, para acordar o controle "mudo" sem abrir o Legion Space.
///
/// O problema (log de 17/11): depois de ligar, o controle aparece no XInput (slot 0), mas
/// não manda nenhum botão até o Legion Space abrir. O firmware do controle tem uma chave
/// que liga/desliga a saída XInput física; com ela desligada o controle continua
/// conectado, mas mudo. O Legion Space liga essa chave ao abrir. Aqui o launcher manda o
/// mesmo pedido de "ligar" — que é o estado normal do controle, então mandar com ele já
/// ligado não muda nada.
///
/// Fonte dos FATOS do protocolo (não de código): o projeto Handheld Companion
/// (github.com/Valkirie/HandheldCompanion, Devices/Lenovo/LegionGoTablet.cs) documenta a
/// interface (VID 17EF, coleção de fabricante página 0xFFA0 / uso 0x0001), os PIDs dos
/// controles e o pedido "liga/desliga XInput físico" (relatório 0x05, 00 04 0F, alvo,
/// 01 = liga / 02 = desliga). O Handheld Daemon (github.com/hhd-dev/hhd) usa a mesma
/// interface e o mesmo formato de relatório para outros pedidos. Implementação própria,
/// com a API HID do Windows; nenhum código foi copiado (as licenças deles são diferentes
/// da GPL-3.0 do launcher). Detalhes em docs/architecture/legion-controle-hid.md.
///
/// Segurança: só manda o pedido "ligar" (nada de modo, giroscópio, RGB ou reset); só para
/// dispositivos com VID e PID do controle do Legion e a coleção certa; cada escrita tem
/// limite de 1 s; qualquer falha vira uma linha no log e o launcher segue normal.
/// </summary>
internal static class LegionControllerHid
{
    private const ushort LenovoVid = 0x17EF;

    // Controles do Legion Go: firmware antigo 0x6182–0x6185, firmware de 2025 0x61EB–0x61EE
    // (xinput, dinput, dual dinput, fps).
    private static readonly ushort[] ControllerPids =
        { 0x6182, 0x6183, 0x6184, 0x6185, 0x61EB, 0x61EC, 0x61ED, 0x61EE };

    private const ushort VendorUsagePage = 0xFFA0;
    private const ushort VendorUsage = 0x0001;

    private const byte ReportId = 0x05;
    // Alvos do pedido: 0x00 = conjunto, 0x03 = controle esquerdo, 0x04 = controle direito.
    private static readonly byte[] Targets = { 0x00, 0x03, 0x04 };
    private const byte TurnOn = 0x01;

    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(1);
    private static readonly object Gate = new();

    /// <summary>
    /// Manda "liga a saída XInput" para o controle do Legion Go. Devolve uma frase curta para
    /// o log (nunca lança exceção).
    /// </summary>
    public static string TryEnablePhysicalXInput()
    {
        if (!OperatingSystem.IsWindows()) return "não é Windows.";
        lock (Gate)
        {
            try
            {
                var candidates = 0;
                var sent = 0;
                var failed = 0;
                var lastError = 0;
                string? device = null;

                foreach (var path in LenovoHidPaths())
                {
                    if (!IsControllerConfigInterface(path, out var pid, out var outputLength)) continue;
                    candidates++;
                    device ??= $"PID {pid:X4}";

                    using var handle = OpenForWrite(path);
                    if (handle is null)
                    {
                        failed += Targets.Length;
                        lastError = Marshal.GetLastWin32Error();
                        continue;
                    }

                    foreach (var target in Targets)
                    {
                        var report = new byte[outputLength];
                        report[0] = ReportId;
                        report[1] = 0x00;
                        report[2] = 0x04;
                        report[3] = 0x0F;
                        report[4] = target;
                        report[5] = TurnOn;
                        if (Write(handle, report, out var error)) sent++;
                        else { failed++; lastError = error; }
                        Thread.Sleep(30);
                    }
                }

                if (candidates == 0)
                    return "controle do Legion não encontrado (nenhuma interface de configuração da Lenovo) — nada enviado.";
                return failed == 0
                    ? $"pedido \"liga XInput\" enviado ao controle ({device}, {sent} de {sent})."
                    : $"pedido \"liga XInput\" ao controle ({device}): {sent} ok, {failed} falharam (erro {lastError}).";
            }
            catch (Exception ex)
            {
                return $"falhou ({ex.GetType().Name}: {ex.Message}).";
            }
        }
    }

    /// <summary>Caminhos das interfaces HID presentes cujo nome tem o VID da Lenovo.</summary>
    private static List<string> LenovoHidPaths()
    {
        var result = new List<string>();
        HidD_GetHidGuid(out var hidGuid);
        var set = SetupDiGetClassDevsW(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == IntPtr.Zero || set == InvalidHandleValue) return result;
        try
        {
            for (var index = 0; index < 1024; index++)
            {
                var data = new SpDeviceInterfaceData { CbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref data)) break;

                SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out var size, IntPtr.Zero);
                if (size <= 4) continue;
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    // cbSize da estrutura SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 em 64 bits, 6 em 32 bits.
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref data, buffer, size, out _, IntPtr.Zero)) continue;
                    var path = Marshal.PtrToStringUni(buffer + 4);
                    if (!string.IsNullOrEmpty(path) && path.Contains("vid_17ef", StringComparison.OrdinalIgnoreCase))
                        result.Add(path);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    /// <summary>Confere VID, PID e a coleção de configuração da Lenovo (sem pedir leitura/escrita).</summary>
    private static bool IsControllerConfigInterface(string path, out ushort pid, out int outputLength)
    {
        pid = 0;
        outputLength = 0;
        using var probe = CreateFileW(path, 0, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (probe.IsInvalid) return false;

        var attributes = new HiddAttributes { Size = Marshal.SizeOf<HiddAttributes>() };
        if (!HidD_GetAttributes(probe, ref attributes)) return false;
        if (attributes.VendorId != LenovoVid || Array.IndexOf(ControllerPids, attributes.ProductId) < 0) return false;
        pid = attributes.ProductId;

        if (!HidD_GetPreparsedData(probe, out var preparsed)) return false;
        try
        {
            // HIDP_CAPS: 32 USHORTs. [0] Usage, [1] UsagePage, [2] tamanho do relatório de
            // entrada, [3] tamanho do relatório de saída.
            var caps = new ushort[32];
            if (HidP_GetCaps(preparsed, caps) != HidpStatusSuccess) return false;
            if (caps[1] != VendorUsagePage || caps[0] != VendorUsage) return false;
            outputLength = caps[3];
            return outputLength >= 6;
        }
        finally { HidD_FreePreparsedData(preparsed); }
    }

    private static SafeFileHandle? OpenForWrite(string path)
    {
        var handle = CreateFileW(path, GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero,
            OpenExisting, FileFlagOverlapped, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        handle.Dispose();

        handle = CreateFileW(path, GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero,
            OpenExisting, FileFlagOverlapped, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        handle.Dispose();
        return null;
    }

    /// <summary>Escrita assíncrona com limite de tempo: um controle que não responde não trava a thread.</summary>
    private static bool Write(SafeFileHandle handle, byte[] report, out int error)
    {
        error = 0;
        var buffer = Marshal.AllocHGlobal(report.Length);
        var overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
        var stillPending = false;
        using var done = new ManualResetEvent(false);
        try
        {
            Marshal.Copy(report, 0, buffer, report.Length);
            Marshal.StructureToPtr(new NativeOverlapped { EventHandle = done.SafeWaitHandle.DangerousGetHandle() }, overlapped, false);

            if (WriteFile(handle, buffer, report.Length, IntPtr.Zero, overlapped)) return true;
            error = Marshal.GetLastWin32Error();
            if (error != ErrorIoPending) return false;

            if (done.WaitOne(WriteTimeout))
            {
                if (GetOverlappedResult(handle, overlapped, out _, false)) { error = 0; return true; }
                error = Marshal.GetLastWin32Error();
                return false;
            }

            // Passou de 1 s: cancela e espera o cancelamento terminar antes de soltar a memória.
            CancelIoEx(handle, overlapped);
            if (!done.WaitOne(WriteTimeout)) stillPending = true;
            else GetOverlappedResult(handle, overlapped, out _, false);
            error = ErrorTimeout;
            return false;
        }
        finally
        {
            // Se o driver ainda estiver com a escrita (não deveria), a memória fica de propósito:
            // soltar algo que o driver ainda pode usar seria pior que um vazamento de 100 bytes.
            if (!stillPending)
            {
                Marshal.FreeHGlobal(buffer);
                Marshal.FreeHGlobal(overlapped);
            }
        }
    }

    // ===================================================================== Win32

    private const int DigcfPresent = 0x02;
    private const int DigcfDeviceInterface = 0x10;
    private static readonly IntPtr InvalidHandleValue = new(-1);
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x01;
    private const uint FileShareWrite = 0x02;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const int ErrorIoPending = 997;
    private const int ErrorTimeout = 1460;
    private const int HidpStatusSuccess = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int CbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    // As funções HidD_* devolvem BOOLEAN (1 byte), não BOOL (4 bytes).
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetAttributes(SafeFileHandle device, ref HiddAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsedData, [Out] ushort[] capabilities);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, IntPtr enumerator, IntPtr parent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData,
        ref Guid interfaceClassGuid, int memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle file, IntPtr buffer, int bytesToWrite, IntPtr bytesWritten, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr overlapped, out int bytesTransferred, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
}

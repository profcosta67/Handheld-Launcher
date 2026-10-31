using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HandheldLauncher.App.Platform;

public sealed class PawnIOWrapper : IDisposable
{
    private const int FN_LEN = 32;
    private const uint DEV_TYPE = 41394u << 16; // 0xA1B20000

    private enum Ctl : uint
    {
        Load = DEV_TYPE | (0x821 << 2),
        Execute = DEV_TYPE | (0x841 << 2),
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFile(string n, uint acc, uint share, IntPtr sec, uint disp, uint fl, IntPtr tmpl);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);

    // 🟢 Assinatura correta para SafeFileHandle e buffers de bytes
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, byte[] lpInBuffer, uint nInBufferSize, byte[] lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

    private IntPtr _raw = IntPtr.Zero;
    private SafeFileHandle? _safe;
    private bool _loaded, _disposed;

    public bool IsConnected => _raw != IntPtr.Zero && _raw.ToInt64() != -1;
    public bool IsModuleLoaded => _loaded && _safe != null && !_safe.IsInvalid;

    public enum ConnectResult { OK, NotInstalled, AccessDenied, OtherError }

    public ConnectResult Connect()
    {
        if (IsConnected) return ConnectResult.OK;
        const string path = @"\\?\GLOBALROOT\Device\PawnIO";
        _raw = CreateFile(path, 0xC0000000u, 0x3, IntPtr.Zero, 3, 0, IntPtr.Zero);

        if (_raw == IntPtr.Zero || _raw.ToInt64() == -1)
        {
            int err = Marshal.GetLastWin32Error();
            _raw = IntPtr.Zero;
            return err switch
            {
                2 or 3 => ConnectResult.NotInstalled,
                5 => ConnectResult.AccessDenied,
                _ => ConnectResult.OtherError,
            };
        }
        return ConnectResult.OK;
    }

    public bool LoadModule(byte[] data)
    {
        if (!IsConnected || data == null || data.Length == 0) return false;

        // Usa o handle raw por enquanto
        byte[]? outBuffer = null;
        uint ret = 0;
        bool ok = DeviceIoControl(new SafeFileHandle(_raw, false), (uint)Ctl.Load, data, (uint)data.Length, outBuffer!, 0, out ret, IntPtr.Zero);
        
        // Se der certo, tomamos posse do handle
        if (ok)
        {
            _safe = new SafeFileHandle(_raw, ownsHandle: true);
            _loaded = true;
        }
        return ok;
    }

    public bool Execute(string functionName, ulong[]? input, ulong[]? output)
    {
        if (!IsModuleLoaded) return false;

        byte[] nameBytes = Encoding.ASCII.GetBytes(functionName);
        int inputCount = input?.Length ?? 0;
        byte[] buffer = new byte[FN_LEN + inputCount * 8];
        Buffer.BlockCopy(nameBytes, 0, buffer, 0, Math.Min(nameBytes.Length, FN_LEN - 1));

        if (input != null && inputCount > 0)
        {
            byte[] inputBytes = new byte[inputCount * 8];
            Buffer.BlockCopy(input, 0, inputBytes, 0, inputBytes.Length);
            Buffer.BlockCopy(inputBytes, 0, buffer, FN_LEN, inputBytes.Length);
        }

        // 🟢 Prepara o buffer de saída como byte[]
        int outputCount = output?.Length ?? 0;
        byte[] outBytes = new byte[outputCount * 8];
        uint outSize = (uint)outBytes.Length;

        bool ok = DeviceIoControl(_safe!, (uint)Ctl.Execute, buffer, (uint)buffer.Length, outBytes, outSize, out _, IntPtr.Zero);

        // 🟢 Se deu certo, copia o resultado de volta para o ulong[]
        if (ok && output != null && outputCount > 0)
        {
            Buffer.BlockCopy(outBytes, 0, output, 0, outBytes.Length);
        }

        return ok;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_safe != null) _safe.Dispose();
        else if (IsConnected) CloseHandle(_raw);
        _raw = IntPtr.Zero;
    }
}
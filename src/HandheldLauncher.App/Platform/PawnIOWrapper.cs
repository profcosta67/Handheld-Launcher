using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Minimal managed bridge to the documented PawnIO device interface.
/// Handle ownership is transferred exactly once after a successful module load.
/// </summary>
public sealed class PawnIOWrapper : IDisposable
{
    private const int FunctionNameLength = 32;
    private const uint DeviceType = 41394u << 16; // 0xA1B20000

    private enum Ioctl : uint
    {
        Load = DeviceType | (0x821u << 2),    // 0xA1B22084
        Execute = DeviceType | (0x841u << 2)  // 0xA1B22104
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(string name, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr device, Ioctl code,
        byte[] input, uint inputSize, byte[]? output, uint outputSize,
        out uint bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, Ioctl code,
        [In] byte[] input, uint inputSize, [Out] byte[]? output, uint outputSize,
        out uint bytesReturned, IntPtr overlapped);

    private IntPtr _rawHandle = IntPtr.Zero;
    private SafeFileHandle? _moduleHandle;
    private bool _moduleLoaded;
    private bool _disposed;

    public bool IsConnected => _rawHandle != IntPtr.Zero && _rawHandle != new IntPtr(-1);
    public bool IsModuleLoaded => _moduleLoaded && _moduleHandle is { IsInvalid: false, IsClosed: false };

    public enum ConnectResult { OK, NotInstalled, AccessDenied, OtherError }

    public ConnectResult Connect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsConnected || IsModuleLoaded) return ConnectResult.OK;

        const string devicePath = @"\\?\GLOBALROOT\Device\PawnIO";
        _rawHandle = CreateFile(devicePath, 0xC0000000u, 0x3u,
            IntPtr.Zero, 3u, 0u, IntPtr.Zero);

        if (_rawHandle == IntPtr.Zero || _rawHandle == new IntPtr(-1))
        {
            int error = Marshal.GetLastWin32Error();
            _rawHandle = IntPtr.Zero;
            return error switch
            {
                2 or 3 => ConnectResult.NotInstalled,
                5 => ConnectResult.AccessDenied,
                _ => ConnectResult.OtherError
            };
        }

        return ConnectResult.OK;
    }

    public bool LoadModule(byte[] data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsConnected || data is null || data.Length == 0 || IsModuleLoaded)
            return false;

        if (!DeviceIoControl(_rawHandle, Ioctl.Load, data, (uint)data.Length,
            null, 0, out _, IntPtr.Zero))
            return false;

        // Transfer ownership to SafeFileHandle; do not close the same native handle twice.
        _moduleHandle = new SafeFileHandle(_rawHandle, ownsHandle: true);
        _rawHandle = IntPtr.Zero;
        _moduleLoaded = true;
        return true;
    }

    public bool Execute(string functionName, ulong[]? input, ulong[]? output)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsModuleLoaded || string.IsNullOrWhiteSpace(functionName) ||
            Encoding.ASCII.GetByteCount(functionName) >= FunctionNameLength)
            return false;

        int inputCount = input?.Length ?? 0;
        byte[] inputBuffer = new byte[FunctionNameLength + inputCount * sizeof(ulong)];
        byte[] nameBytes = Encoding.ASCII.GetBytes(functionName);
        Buffer.BlockCopy(nameBytes, 0, inputBuffer, 0, nameBytes.Length);

        if (inputCount > 0)
        {
            byte[] values = new byte[inputCount * sizeof(ulong)];
            Buffer.BlockCopy(input!, 0, values, 0, values.Length);
            Buffer.BlockCopy(values, 0, inputBuffer, FunctionNameLength, values.Length);
        }

        int outputCount = output?.Length ?? 0;
        byte[]? outputBuffer = outputCount > 0 ? new byte[outputCount * sizeof(ulong)] : null;
        bool ok = DeviceIoControl(_moduleHandle!, Ioctl.Execute, inputBuffer,
            (uint)inputBuffer.Length, outputBuffer, (uint)(outputBuffer?.Length ?? 0),
            out uint bytesReturned, IntPtr.Zero);

        if (!ok) return false;
        if (output is not null)
        {
            int expectedBytes = output.Length * sizeof(ulong);
            if (bytesReturned < expectedBytes) return false;
            Buffer.BlockCopy(outputBuffer!, 0, output, 0, expectedBytes);
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _moduleLoaded = false;
        _moduleHandle?.Dispose();
        _moduleHandle = null;

        if (IsConnected)
            CloseHandle(_rawHandle);

        _rawHandle = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }
}

using System.Runtime.InteropServices;

namespace HandheldLauncher.App.Input;

/// <summary>
/// Raw P/Invoke bindings for XInput. Deliberate choice: XInput ships as part
/// of Windows itself (xinput1_4.dll), so this needs zero NuGet packages —
/// important right now since this project is being written somewhere that
/// can't reach nuget.org to even verify a package-based alternative builds.
///
/// Covers one controller slot (index 0) on purpose for v1 — handhelds are
/// single-controller-by-definition (it's built into the device), so there's
/// no multi-pad scenario to support yet.
/// </summary>
internal static class XInput
{
    private const string DllName = "xinput1_4.dll";

    private const int ErrorSuccess = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputState
    {
        public uint dwPacketNumber;
        public XInputGamepad Gamepad;
    }

    [Flags]
    public enum Buttons : ushort
    {
        DPadUp = 0x0001,
        DPadDown = 0x0002,
        DPadLeft = 0x0004,
        DPadRight = 0x0008,
        Start = 0x0010,
        Back = 0x0020,
        LeftThumb = 0x0040,
        RightThumb = 0x0080,
        LeftShoulder = 0x0100,
        RightShoulder = 0x0200,
        A = 0x1000,
        B = 0x2000,
        X = 0x4000,
        Y = 0x8000,
    }

    [DllImport(DllName, EntryPoint = "XInputGetState")]
    private static extern int XInputGetStateNative(uint dwUserIndex, out XInputState pState);

    /// <summary>
    /// Polls controller slot 0. Returns false if nothing is connected there
    /// (or if xinput1_4.dll itself can't be loaded — e.g. running this on a
    /// machine without the DirectX runtime, which is effectively never on
    /// a real Windows 10/11 handheld, but we don't want a missing DLL to
    /// crash the whole launcher).
    /// </summary>
    public static bool TryGetState(out XInputState state)
    {
        try
        {
            return XInputGetStateNative(0, out state) == ErrorSuccess;
        }
        catch (DllNotFoundException)
        {
            state = default;
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            state = default;
            return false;
        }
    }
}

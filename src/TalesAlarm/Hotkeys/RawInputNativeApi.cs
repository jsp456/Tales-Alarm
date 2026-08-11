using System.Runtime.InteropServices;

namespace TalesAlarm.Hotkeys;

[Flags]
public enum RawKeyboardFlags : ushort
{
    None = 0,
    Break = 0x0001,
    E0 = 0x0002,
    E1 = 0x0004,
}

public readonly record struct RawKeyboardInput(
    nint DeviceHandle,
    ushort VirtualKey,
    ushort MakeCode,
    RawKeyboardFlags Flags)
{
    public bool IsKeyUp => (Flags & RawKeyboardFlags.Break) != 0;
}

public enum RawInputReadStatus
{
    Keyboard,
    Ignored,
    Failed,
}

public readonly record struct RawInputReadResult(
    RawInputReadStatus Status,
    RawKeyboardInput Keyboard,
    int ErrorCode)
{
    public static RawInputReadResult FromKeyboard(RawKeyboardInput keyboard) =>
        new(RawInputReadStatus.Keyboard, keyboard, 0);

    public static RawInputReadResult Ignored() =>
        new(RawInputReadStatus.Ignored, default, 0);

    public static RawInputReadResult Failed(int errorCode) =>
        new(RawInputReadStatus.Failed, default, errorCode);
}

public interface IRawInputNativeApi
{
    bool TryRegisterKeyboard(nint windowHandle, out int errorCode);

    bool TryUnregisterKeyboard(out int errorCode);

    RawInputReadResult ReadKeyboard(nint rawInputHandle);
}

[Flags]
internal enum RawInputDeviceFlags : uint
{
    Remove = 0x00000001,
    NoLegacy = 0x00000030,
    InputSink = 0x00000100,
    NoHotkeys = 0x00000200,
    DeviceNotify = 0x00002000,
}

[StructLayout(LayoutKind.Sequential)]
internal struct RawInputDevice
{
    public ushort UsagePage;
    public ushort Usage;
    public RawInputDeviceFlags Flags;
    public nint TargetWindow;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RawInputHeader
{
    public uint Type;
    public uint Size;
    public nint Device;
    public nuint WParam;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RawKeyboard
{
    public ushort MakeCode;
    public RawKeyboardFlags Flags;
    public ushort Reserved;
    public ushort VirtualKey;
    public uint Message;
    public uint ExtraInformation;
}

public sealed class Win32RawInputNativeApi : IRawInputNativeApi
{
    private const ushort GenericDesktopUsagePage = 0x01;
    private const ushort KeyboardUsage = 0x06;
    private const uint RidInput = 0x10000003;
    private const uint RimTypeKeyboard = 1;
    private const uint NativeError = uint.MaxValue;
    private const int ErrorInvalidData = 13;
    private const RawInputDeviceFlags KeyboardRegistrationFlags =
        RawInputDeviceFlags.InputSink | RawInputDeviceFlags.DeviceNotify;

    private readonly IRawInputNativeMethods methods;

    public Win32RawInputNativeApi()
        : this(new Win32RawInputNativeMethods())
    {
    }

    internal Win32RawInputNativeApi(IRawInputNativeMethods methods)
    {
        this.methods = methods ?? throw new ArgumentNullException(nameof(methods));
    }

    public bool TryRegisterKeyboard(nint windowHandle, out int errorCode)
    {
        if (windowHandle == 0)
        {
            throw new ArgumentException("A window handle cannot be zero.", nameof(windowHandle));
        }

        var device = CreateDevice(KeyboardRegistrationFlags, windowHandle);
        return methods.TryRegisterRawInputDevice(ref device, out errorCode);
    }

    public bool TryUnregisterKeyboard(out int errorCode)
    {
        var device = CreateDevice(RawInputDeviceFlags.Remove, 0);
        return methods.TryRegisterRawInputDevice(ref device, out errorCode);
    }

    public RawInputReadResult ReadKeyboard(nint rawInputHandle)
    {
        var headerSize = checked((uint)Marshal.SizeOf<RawInputHeader>());
        var keyboardSize = checked((uint)Marshal.SizeOf<RawKeyboard>());
        uint requiredSize = 0;
        var queryResult = methods.GetRawInputData(
            rawInputHandle,
            RidInput,
            0,
            ref requiredSize,
            headerSize,
            out var queryError);
        if (queryResult == NativeError)
        {
            return RawInputReadResult.Failed(queryError);
        }

        if (queryResult != 0
            || requiredSize < headerSize
            || requiredSize > int.MaxValue)
        {
            return RawInputReadResult.Failed(ErrorInvalidData);
        }

        var buffer = Marshal.AllocHGlobal(checked((int)requiredSize));
        try
        {
            var copiedSize = requiredSize;
            var copied = methods.GetRawInputData(
                rawInputHandle,
                RidInput,
                buffer,
                ref copiedSize,
                headerSize,
                out var copyError);
            if (copied == NativeError)
            {
                return RawInputReadResult.Failed(copyError);
            }

            if (copied != copiedSize || copied < headerSize)
            {
                return RawInputReadResult.Failed(ErrorInvalidData);
            }

            var header = Marshal.PtrToStructure<RawInputHeader>(buffer);
            if (header.Size != copied)
            {
                return RawInputReadResult.Failed(ErrorInvalidData);
            }

            if (header.Type != RimTypeKeyboard)
            {
                return RawInputReadResult.Ignored();
            }

            if (copied < headerSize + keyboardSize)
            {
                return RawInputReadResult.Failed(ErrorInvalidData);
            }

            var keyboard = Marshal.PtrToStructure<RawKeyboard>(
                nint.Add(buffer, checked((int)headerSize)));
            const RawKeyboardFlags knownFlags =
                RawKeyboardFlags.Break | RawKeyboardFlags.E0 | RawKeyboardFlags.E1;
            if (keyboard.Reserved != 0
                || (keyboard.Flags & ~knownFlags) != RawKeyboardFlags.None)
            {
                return RawInputReadResult.Failed(ErrorInvalidData);
            }

            return RawInputReadResult.FromKeyboard(new(
                header.Device,
                keyboard.VirtualKey,
                keyboard.MakeCode,
                keyboard.Flags));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static RawInputDevice CreateDevice(
        RawInputDeviceFlags flags,
        nint targetWindow) =>
        new()
        {
            UsagePage = GenericDesktopUsagePage,
            Usage = KeyboardUsage,
            Flags = flags,
            TargetWindow = targetWindow,
        };
}

internal interface IRawInputNativeMethods
{
    bool TryRegisterRawInputDevice(
        ref RawInputDevice device,
        out int errorCode);

    uint GetRawInputData(
        nint rawInputHandle,
        uint command,
        nint data,
        ref uint dataSize,
        uint headerSize,
        out int errorCode);
}

internal sealed class Win32RawInputNativeMethods : IRawInputNativeMethods
{
    public bool TryRegisterRawInputDevice(
        ref RawInputDevice device,
        out int errorCode)
    {
        var success = RawInputPInvoke.RegisterRawInputDevices(
            ref device,
            1,
            checked((uint)Marshal.SizeOf<RawInputDevice>()));
        errorCode = success ? 0 : Marshal.GetLastWin32Error();
        return success;
    }

    public uint GetRawInputData(
        nint rawInputHandle,
        uint command,
        nint data,
        ref uint dataSize,
        uint headerSize,
        out int errorCode)
    {
        var result = RawInputPInvoke.GetRawInputData(
            rawInputHandle,
            command,
            data,
            ref dataSize,
            headerSize);
        errorCode = result == uint.MaxValue ? Marshal.GetLastWin32Error() : 0;
        return result;
    }
}

internal static partial class RawInputPInvoke
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterRawInputDevices(
        ref RawInputDevice devices,
        uint deviceCount,
        uint deviceSize);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint GetRawInputData(
        nint rawInputHandle,
        uint command,
        nint data,
        ref uint dataSize,
        uint headerSize);
}

using System.Runtime.InteropServices;
using System.Windows.Input;

namespace TalesAlarm.Hotkeys;

public interface IHotkeyNativeApi
{
    bool TryRegister(
        nint windowHandle,
        int id,
        HotkeyGesture gesture,
        out int errorCode);

    bool Unregister(nint windowHandle, int id);
}

public sealed class Win32HotkeyNativeApi : IHotkeyNativeApi
{
    private const uint ModNoRepeat = 0x4000;

    public bool TryRegister(
        nint windowHandle,
        int id,
        HotkeyGesture gesture,
        out int errorCode)
    {
        var virtualKey = checked((uint)KeyInterop.VirtualKeyFromKey(gesture.Key));
        var registered = HotkeyNativeMethods.RegisterHotKey(
            windowHandle,
            id,
            (uint)gesture.Modifiers | ModNoRepeat,
            virtualKey);

        errorCode = registered ? 0 : Marshal.GetLastWin32Error();
        return registered;
    }

    public bool Unregister(nint windowHandle, int id) =>
        HotkeyNativeMethods.UnregisterHotKey(windowHandle, id);
}

internal static partial class HotkeyNativeMethods
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint hWnd, int id);
}

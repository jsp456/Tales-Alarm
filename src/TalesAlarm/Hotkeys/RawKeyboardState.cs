using System.Windows.Input;

namespace TalesAlarm.Hotkeys;

internal sealed class RawKeyboardState
{
    private const ushort KeyboardOverrunMakeCode = 0x00FF;
    private readonly Dictionary<nint, HashSet<RawKeyIdentity>> pressedByDevice = [];

    public bool TryCreateGesture(
        RawKeyboardInput input,
        out HotkeyGesture gesture)
    {
        gesture = default;
        if (!TryNormalize(input, out var identity, out var key, out var modifier))
        {
            return false;
        }

        if (input.IsKeyUp)
        {
            Release(input.DeviceHandle, identity);
            return false;
        }

        if (!pressedByDevice.TryGetValue(input.DeviceHandle, out var pressed))
        {
            pressed = [];
            pressedByDevice.Add(input.DeviceHandle, pressed);
        }

        if (!pressed.Add(identity) || modifier != HotkeyModifiers.None)
        {
            return false;
        }

        gesture = new HotkeyGesture(key, GetActiveModifiers());
        return gesture.HasNonModifierKey;
    }

    public void RemoveDevice(nint deviceHandle) =>
        pressedByDevice.Remove(deviceHandle);

    public void Clear() => pressedByDevice.Clear();

    private void Release(nint deviceHandle, RawKeyIdentity identity)
    {
        if (!pressedByDevice.TryGetValue(deviceHandle, out var pressed))
        {
            return;
        }

        pressed.Remove(identity);
        if (pressed.Count == 0)
        {
            pressedByDevice.Remove(deviceHandle);
        }
    }

    private HotkeyModifiers GetActiveModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        foreach (var identity in pressedByDevice.Values.SelectMany(keys => keys))
        {
            modifiers |= GetModifier(identity.VirtualKey);
        }

        return modifiers;
    }

    private static bool TryNormalize(
        RawKeyboardInput input,
        out RawKeyIdentity identity,
        out Key key,
        out HotkeyModifiers modifier)
    {
        identity = new(
            input.VirtualKey,
            input.MakeCode,
            input.Flags & (RawKeyboardFlags.E0 | RawKeyboardFlags.E1));
        key = Key.None;
        modifier = HotkeyModifiers.None;

        if (input.VirtualKey is 0 or 0x00FF
            || input.MakeCode == KeyboardOverrunMakeCode)
        {
            return false;
        }

        modifier = GetModifier(input.VirtualKey);
        if (modifier != HotkeyModifiers.None)
        {
            return true;
        }

        key = KeyInterop.KeyFromVirtualKey(input.VirtualKey);
        return new HotkeyGesture(key, HotkeyModifiers.None).HasNonModifierKey;
    }

    private static HotkeyModifiers GetModifier(ushort virtualKey) =>
        virtualKey switch
        {
            0x10 or 0xA0 or 0xA1 => HotkeyModifiers.Shift,
            0x11 or 0xA2 or 0xA3 => HotkeyModifiers.Control,
            0x12 or 0xA4 or 0xA5 => HotkeyModifiers.Alt,
            0x5B or 0x5C => HotkeyModifiers.Windows,
            _ => HotkeyModifiers.None,
        };

    private readonly record struct RawKeyIdentity(
        ushort VirtualKey,
        ushort MakeCode,
        RawKeyboardFlags ExtensionFlags);
}

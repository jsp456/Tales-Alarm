using System.Windows.Input;
using TalesAlarm.Hotkeys;

namespace TalesAlarm.Tests.Hotkeys;

public sealed class RawKeyboardStateTests
{
    // Break caught: a typematic repeat triggers the same gesture repeatedly, or key-up never rearms it.
    [Fact]
    public void TryCreateGesture_RepeatedDownRaisesOnceAndKeyUpRearms()
    {
        var state = new RawKeyboardState();
        var down = Input(1, 0x71, 0x3C); // F2

        Assert.True(state.TryCreateGesture(down, out var first));
        Assert.Equal(new HotkeyGesture(Key.F2, HotkeyModifiers.None), first);
        Assert.False(state.TryCreateGesture(down, out _));
        Assert.False(state.TryCreateGesture(
            down with { Flags = RawKeyboardFlags.Break },
            out _));
        Assert.True(state.TryCreateGesture(down, out var second));
        Assert.Equal(first, second);
    }

    // Break caught: E0/E1 identities merge with the corresponding non-extended physical key,
    // or key-up is treated as a distinct identity from the original key-down.
    [Theory]
    [InlineData(RawKeyboardFlags.E0)]
    [InlineData(RawKeyboardFlags.E1)]
    public void TryCreateGesture_ExtensionFlagsDistinguishPhysicalKeysAndIgnoreBreak(
        RawKeyboardFlags extensionFlag)
    {
        var state = new RawKeyboardState();
        var extendedDown = Input(1, 0x71, 0x3C, extensionFlag);
        var normalDown = Input(1, 0x71, 0x3C);

        Assert.True(state.TryCreateGesture(extendedDown, out _));
        Assert.True(state.TryCreateGesture(normalDown, out _));
        Assert.False(state.TryCreateGesture(extendedDown, out _));
        Assert.False(state.TryCreateGesture(
            extendedDown with { Flags = extensionFlag | RawKeyboardFlags.Break },
            out _));
        Assert.True(state.TryCreateGesture(extendedDown, out _));
    }

    // Break caught: scan codes are omitted from a physical-key identity, so different keys sharing a virtual key suppress each other.
    [Fact]
    public void TryCreateGesture_MakeCodesDistinguishPhysicalKeys()
    {
        var state = new RawKeyboardState();
        var firstKey = Input(1, 0x71, 0x3C);
        var secondKey = Input(1, 0x71, 0x3D);

        Assert.True(state.TryCreateGesture(firstKey, out _));
        Assert.True(state.TryCreateGesture(secondKey, out _));
        Assert.False(state.TryCreateGesture(firstKey, out _));
    }

    // Break caught: left/right modifier virtual keys are emitted as keys or fail to contribute their shared modifier flag.
    [Theory]
    [InlineData(0x10, HotkeyModifiers.Shift)]
    [InlineData(0xA0, HotkeyModifiers.Shift)]
    [InlineData(0xA1, HotkeyModifiers.Shift)]
    [InlineData(0x11, HotkeyModifiers.Control)]
    [InlineData(0xA2, HotkeyModifiers.Control)]
    [InlineData(0xA3, HotkeyModifiers.Control)]
    [InlineData(0x12, HotkeyModifiers.Alt)]
    [InlineData(0xA4, HotkeyModifiers.Alt)]
    [InlineData(0xA5, HotkeyModifiers.Alt)]
    [InlineData(0x5B, HotkeyModifiers.Windows)]
    [InlineData(0x5C, HotkeyModifiers.Windows)]
    public void TryCreateGesture_CollapsesModifierVirtualKeys(
        ushort modifierVirtualKey,
        HotkeyModifiers expected)
    {
        var state = new RawKeyboardState();
        Assert.False(state.TryCreateGesture(
            Input(1, modifierVirtualKey, 0x1D),
            out _));

        Assert.True(state.TryCreateGesture(Input(1, 0x71, 0x3C), out var gesture));
        Assert.Equal(new HotkeyGesture(Key.F2, expected), gesture);
    }

    // Break caught: modifiers are calculated only from the event device instead of the complete active keyboard set.
    [Fact]
    public void TryCreateGesture_AggregatesModifiersAcrossDevices()
    {
        var state = new RawKeyboardState();
        state.TryCreateGesture(Input(1, 0x11, 0x1D), out _); // Ctrl
        state.TryCreateGesture(Input(2, 0x12, 0x38), out _); // Alt

        Assert.True(state.TryCreateGesture(Input(3, 0x71, 0x3C), out var gesture));
        Assert.Equal(
            new HotkeyGesture(
                Key.F2,
                HotkeyModifiers.Control | HotkeyModifiers.Alt),
            gesture);
    }

    // Break caught: a device removal leaves modifiers held by the removed device active.
    [Fact]
    public void RemoveDevice_ClearsOnlyRemovedDeviceState()
    {
        var state = new RawKeyboardState();
        state.TryCreateGesture(Input(1, 0x11, 0x1D), out _);
        state.TryCreateGesture(Input(2, 0x12, 0x38), out _);

        state.RemoveDevice(1);

        Assert.True(state.TryCreateGesture(Input(3, 0x71, 0x3C), out var gesture));
        Assert.Equal(HotkeyModifiers.Alt, gesture.Modifiers);
    }

    // Break caught: clearing leaves a modifier or repeat suppression behind.
    [Fact]
    public void Clear_DropsRepeatAndModifierState()
    {
        var state = new RawKeyboardState();
        state.TryCreateGesture(Input(1, 0x11, 0x1D), out _);
        state.TryCreateGesture(Input(1, 0x71, 0x3C), out _);

        state.Clear();

        Assert.True(state.TryCreateGesture(Input(1, 0x71, 0x3C), out var gesture));
        Assert.Equal(HotkeyModifiers.None, gesture.Modifiers);
    }

    // Break caught: valid virtual keys are not converted to the WPF Key used by existing hotkey bindings.
    [Theory]
    [InlineData(0x41, Key.A)]
    [InlineData(0x70, Key.F1)]
    [InlineData(0xBA, Key.OemSemicolon)]
    public void TryCreateGesture_MapsVirtualKeysToWpfKeys(
        ushort virtualKey,
        Key expected)
    {
        var state = new RawKeyboardState();

        Assert.True(state.TryCreateGesture(Input(1, virtualKey, 0x20), out var gesture));
        Assert.Equal(expected, gesture.Key);
    }

    // Break caught: malformed or unmappable raw keyboard reports create gestures or affect pressed state.
    [Theory]
    [InlineData(0x00, 0x00)]
    [InlineData(0xFF, 0x00)]
    [InlineData(0x41, 0xFF)]
    public void TryCreateGesture_UnknownOrOverrunInputIsIgnored(
        ushort virtualKey,
        ushort makeCode)
    {
        var state = new RawKeyboardState();

        Assert.False(state.TryCreateGesture(
            Input(1, virtualKey, makeCode),
            out _));
    }

    private static RawKeyboardInput Input(
        nint device,
        ushort virtualKey,
        ushort makeCode,
        RawKeyboardFlags flags = RawKeyboardFlags.None) =>
        new(device, virtualKey, makeCode, flags);
}

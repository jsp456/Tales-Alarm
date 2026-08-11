using System.Windows.Input;
using TalesAlarm.Hotkeys;
using TalesAlarm.Tests.Helpers;

namespace TalesAlarm.Tests.Hotkeys;

public sealed class GlobalHotkeyServiceTests
{
    // Break caught: applying bindings registers one native hotkey per gesture or disposal unregisters repeatedly.
    [Fact]
    public void AttachApplyAndDispose_RegisterOnceAndUnregisterOnce()
    {
        var native = new FakeRawInputNativeApi();
        var service = new GlobalHotkeyService(native);

        service.Attach((nint)42);
        Assert.True(service.Apply(Bindings((1, Key.F4, HotkeyModifiers.None))).Success);
        Assert.True(service.Apply(Bindings((1, Key.F8, HotkeyModifiers.None))).Success);
        service.Dispose();
        service.Dispose();

        Assert.Equal(1, native.RegisterCallCount);
        Assert.Equal((nint)42, native.RegisteredWindowHandle);
        Assert.Equal(1, native.UnregisterCallCount);
    }

    // Break caught: repeating Attach re-registers input, or attaching the service to a second window silently succeeds.
    [Fact]
    public void Attach_IsIdempotentOnlyForTheOriginalWindow()
    {
        var native = new FakeRawInputNativeApi();
        using var service = new GlobalHotkeyService(native);

        service.Attach((nint)42);
        service.Attach((nint)42);

        Assert.Equal(1, native.RegisterCallCount);
        Assert.Throws<InvalidOperationException>(() => service.Attach((nint)43));
        Assert.Equal(1, native.RegisterCallCount);
    }

    // Break caught: Raw Input registration failure activates bindings or loses the native error needed by the user.
    [Fact]
    public void Apply_WhenRawInputRegistrationFailed_DisablesOnlyHotkeysAndReportsCode()
    {
        var native = new FakeRawInputNativeApi
        {
            RegisterSucceeds = false,
            RegisterErrorCode = 87,
        };
        var diagnostics = new List<string>();
        using var service = new GlobalHotkeyService(native, diagnostics.Add);
        service.Attach((nint)42);

        var result = service.Apply(Bindings((1, Key.F4, HotkeyModifiers.None)));

        Assert.False(result.Success);
        Assert.Contains("87", result.ErrorMessage);
        Assert.Contains(diagnostics, message => message.Contains("87"));
        Assert.Empty(service.ActiveBindings);
        Assert.Equal(0, native.UnregisterCallCount);
    }

    // Break caught: accepting bindings before a target window exists makes registration state ambiguous.
    [Fact]
    public void Apply_WhenUnattached_RejectsWithoutRegistering()
    {
        var native = new FakeRawInputNativeApi();
        using var service = new GlobalHotkeyService(native);

        var result = service.Apply(Bindings((1, Key.F4, HotkeyModifiers.None)));

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Empty(service.ActiveBindings);
        Assert.Equal(0, native.RegisterCallCount);
    }

    // Break caught: duplicate timer numbers replace the gesture routing entry for a timer.
    [Fact]
    public void Apply_WhenTimerNumbersDuplicate_RejectsWithoutChangingBindings()
    {
        var native = new FakeRawInputNativeApi();
        using var service = AttachedService(native);
        var previous = Bindings((3, Key.F3, HotkeyModifiers.None));
        Assert.True(service.Apply(previous).Success);

        var result = service.Apply(Bindings(
            (1, Key.F4, HotkeyModifiers.None),
            (1, Key.F8, HotkeyModifiers.None)));

        Assert.False(result.Success);
        Assert.Equal(previous, service.ActiveBindings);
        Assert.Equal(1, native.RegisterCallCount);
    }

    // Break caught: duplicate gestures route one physical input to an arbitrary timer.
    [Fact]
    public void Apply_WhenGesturesDuplicate_RejectsWithoutChangingBindings()
    {
        var native = new FakeRawInputNativeApi();
        using var service = AttachedService(native);
        var previous = Bindings((3, Key.F3, HotkeyModifiers.None));
        Assert.True(service.Apply(previous).Success);

        var result = service.Apply(Bindings(
            (1, Key.F4, HotkeyModifiers.Control),
            (2, Key.F4, HotkeyModifiers.Control)));

        Assert.False(result.Success);
        Assert.Equal(previous, service.ActiveBindings);
        Assert.Equal(1, native.RegisterCallCount);
    }

    // Break caught: a modifier-only gesture is accepted even though it cannot identify a timer action.
    [Fact]
    public void Apply_WhenGestureHasOnlyModifierKey_RejectsWithoutChangingBindings()
    {
        var native = new FakeRawInputNativeApi();
        using var service = AttachedService(native);
        var previous = Bindings((3, Key.F3, HotkeyModifiers.None));
        Assert.True(service.Apply(previous).Success);

        var result = service.Apply(Bindings((1, Key.LeftCtrl, HotkeyModifiers.Control)));

        Assert.False(result.Success);
        Assert.Equal(previous, service.ActiveBindings);
        Assert.Equal(1, native.RegisterCallCount);
    }

    // Break caught: key auto-repeat raises repeated timer events or the WM_INPUT lParam is not read.
    [Fact]
    public void ProcessWindowMessage_MatchingFirstDownRaisesAssignedTimerOnce()
    {
        var native = new FakeRawInputNativeApi();
        using var service = AttachedService(native);
        Assert.True(service.Apply(Bindings((2, Key.F2, HotkeyModifiers.Control))).Success);
        var pressed = new List<int>();
        service.HotkeyPressed += (_, timerIndex) => pressed.Add(timerIndex);

        Send(service, native, Input(1, 0x11, 0x1D));
        Send(service, native, Input(1, 0x71, 0x3C));
        Send(service, native, Input(1, 0x71, 0x3C));

        Assert.Equal(new[] { 2 }, pressed);
        Assert.Equal((nint)123, native.LastRawInputHandle);

        Send(service, native, Input(1, 0x71, 0x3C, RawKeyboardFlags.Break));
        Send(service, native, Input(1, 0x71, 0x3C));
        Assert.Equal(new[] { 2, 2 }, pressed);
    }

    // Break caught: a gesture matches when extra modifiers are held.
    [Fact]
    public void ProcessWindowMessage_WithAdditionalModifier_DoesNotMatch()
    {
        var native = new FakeRawInputNativeApi();
        using var service = AttachedService(native);
        service.Apply(Bindings((1, Key.F2, HotkeyModifiers.Control)));
        var presses = 0;
        service.HotkeyPressed += (_, _) => presses++;

        Send(service, native, Input(1, 0x11, 0x1D));
        Send(service, native, Input(1, 0x12, 0x38));
        Send(service, native, Input(1, 0x71, 0x3C));

        Assert.Equal(0, presses);
    }

    // Break caught: ending an inner capture lease resumes events, or captured key state survives the final lease.
    [Fact]
    public void SuspendForCapture_WithNestedLeases_SuppressesUntilFinalLeaseAndClearsState()
    {
        var native = new FakeRawInputNativeApi();
        using var service = AttachedService(native);
        service.Apply(Bindings((1, Key.F2, HotkeyModifiers.None)));
        var presses = 0;
        service.HotkeyPressed += (_, _) => presses++;
        var outer = service.SuspendForCapture();
        var inner = service.SuspendForCapture();

        Send(service, native, Input(1, 0x71, 0x3C));
        inner.Dispose();
        Send(service, native, Input(1, 0x71, 0x3C));
        Assert.Equal(0, presses);

        outer.Dispose();
        Send(service, native, Input(1, 0x71, 0x3C));
        Assert.Equal(1, presses);
        Assert.Equal(1, native.RegisterCallCount);
        Assert.Equal(0, native.UnregisterCallCount);
    }

    // Break caught: applying settings retains pressed-state repeats or leaves the old timer routing active.
    [Fact]
    public void Apply_ClearsPressedStateAndAtomicallyReplacesBindings()
    {
        var native = new FakeRawInputNativeApi();
        using var service = AttachedService(native);
        service.Apply(Bindings((1, Key.F2, HotkeyModifiers.None)));
        Send(service, native, Input(1, 0x71, 0x3C));

        Assert.True(service.Apply(Bindings((2, Key.F2, HotkeyModifiers.None))).Success);
        var pressed = 0;
        service.HotkeyPressed += (_, timerIndex) => pressed = timerIndex;
        Send(service, native, Input(1, 0x71, 0x3C));

        Assert.Equal(2, pressed);
    }

    // Break caught: device removal leaves that device's pressed keys stuck or clears state from every device.
    [Fact]
    public void DeviceRemoval_ClearsOnlyRemovedDeviceState()
    {
        var native = new FakeRawInputNativeApi();
        using var service = AttachedService(native);
        service.Apply(Bindings((1, Key.F2, HotkeyModifiers.Control)));
        var presses = 0;
        service.HotkeyPressed += (_, _) => presses++;
        Send(service, native, Input(8, 0x11, 0x1D));
        Send(service, native, Input(7, 0x71, 0x3C));

        service.ProcessWindowMessage(
            GlobalHotkeyService.WmInputDeviceChange,
            (nint)GlobalHotkeyService.GidcRemoval,
            (nint)7);
        Send(service, native, Input(7, 0x71, 0x3C));

        Assert.Equal(2, presses);
    }

    // Break caught: a native read failure leaks binding data into diagnostics or raises a timer.
    [Fact]
    public void ProcessWindowMessage_WhenReadFails_LogsCodeWithoutRaisingEvent()
    {
        var native = new FakeRawInputNativeApi
        {
            NextReadResult = RawInputReadResult.Failed(13),
        };
        var diagnostics = new List<string>();
        using var service = new GlobalHotkeyService(native, diagnostics.Add);
        service.Attach((nint)42);
        service.Apply(Bindings((1, Key.F2, HotkeyModifiers.None)));
        var presses = 0;
        service.HotkeyPressed += (_, _) => presses++;

        service.ProcessWindowMessage(GlobalHotkeyService.WmInput, 0, (nint)123);

        Assert.Equal(0, presses);
        Assert.Contains(diagnostics, message => message.Contains("13"));
        Assert.DoesNotContain(diagnostics, message => message.Contains("F2"));
    }

    // Break caught: ignored packets or unrelated messages trigger reads and timer events.
    [Fact]
    public void ProcessWindowMessage_WhenPacketMessageOrDeviceChangeIsUnknown_DoesNothing()
    {
        var native = new FakeRawInputNativeApi();
        using var service = AttachedService(native);
        service.Apply(Bindings((1, Key.F2, HotkeyModifiers.None)));
        var presses = 0;
        service.HotkeyPressed += (_, _) => presses++;

        service.ProcessWindowMessage(GlobalHotkeyService.WmInput, 0, (nint)123);
        service.ProcessWindowMessage(0, 0, (nint)123);
        service.ProcessWindowMessage(
            GlobalHotkeyService.WmInputDeviceChange,
            (nint)99,
            (nint)777);

        Assert.Equal(1, native.ReadCallCount);
        Assert.Equal(0, presses);
    }

    // Break caught: unregister failure is silent or logs active key values instead of the operation and error code.
    [Fact]
    public void Dispose_WhenUnregisterFails_LogsOnlyOperationAndErrorCode()
    {
        var native = new FakeRawInputNativeApi
        {
            UnregisterSucceeds = false,
            UnregisterErrorCode = 5,
        };
        var diagnostics = new List<string>();
        var service = new GlobalHotkeyService(native, diagnostics.Add);
        service.Attach((nint)42);
        service.Apply(Bindings((1, Key.F2, HotkeyModifiers.None)));

        service.Dispose();

        Assert.Empty(service.ActiveBindings);
        Assert.Contains(diagnostics, message => message.Contains("5"));
        Assert.DoesNotContain(diagnostics, message => message.Contains("F2"));
    }

    private static void Send(
        GlobalHotkeyService service,
        FakeRawInputNativeApi native,
        RawKeyboardInput input)
    {
        native.NextReadResult = RawInputReadResult.FromKeyboard(input);
        service.ProcessWindowMessage(GlobalHotkeyService.WmInput, 0, (nint)123);
    }

    private static GlobalHotkeyService AttachedService(
        FakeRawInputNativeApi native,
        Action<string>? writeDiagnostic = null)
    {
        var service = new GlobalHotkeyService(native, writeDiagnostic);
        service.Attach((nint)42);
        return service;
    }

    private static RawKeyboardInput Input(
        nint device,
        ushort virtualKey,
        ushort makeCode,
        RawKeyboardFlags flags = RawKeyboardFlags.None) =>
        new(device, virtualKey, makeCode, flags);

    private static HotkeyBinding[] Bindings(
        params (int TimerIndex, Key Key, HotkeyModifiers Modifiers)[] values) =>
        values.Select(value => new HotkeyBinding(
            value.TimerIndex,
            new HotkeyGesture(value.Key, value.Modifiers))).ToArray();
}

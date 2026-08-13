using System.Windows.Input;
using TalesAlarm.Hotkeys;
using TalesAlarm.Tests.Helpers;

namespace TalesAlarm.Tests.Hotkeys;

public sealed class GlobalHotkeyServiceTests
{
    // Break caught: partial registration replaces working bindings when a later candidate cannot register.
    [Fact]
    public void Apply_WhenSecondCandidateFails_RestoresBothPreviousBindings()
    {
        var native = new FakeHotkeyNativeApi();
        using var service = new GlobalHotkeyService(native);
        service.Attach((nint)42);
        var previous = Bindings((1, Key.F4, HotkeyModifiers.None), (2, Key.F8, HotkeyModifiers.None));
        Assert.True(service.Apply(previous).Success);
        native.FailGesture = new(Key.F10, HotkeyModifiers.Control);

        var result = service.Apply(Bindings((1, Key.F9, HotkeyModifiers.Control), (2, Key.F10, HotkeyModifiers.Control)));

        Assert.False(result.Success);
        Assert.Equal(previous, service.ActiveBindings);
        Assert.Equal(previous.Select(binding => binding.Gesture), native.RegisteredGestures);
    }

    // Break caught: an unsuccessful rollback is reported as if the old native registrations were restored.
    [Fact]
    public void Apply_WhenRollbackRegistrationFails_IncludesCandidateAndRollbackErrors()
    {
        var native = new FakeHotkeyNativeApi();
        using var service = new GlobalHotkeyService(native);
        service.Attach((nint)42);
        var previous = Bindings((1, Key.F4, HotkeyModifiers.None));
        Assert.True(service.Apply(previous).Success);
        native.RegistrationFailure = (gesture, _) => gesture switch
        {
            { Key: Key.F10 } => 1001,
            { Key: Key.F4 } => 1002,
            _ => null,
        };

        var result = service.Apply(Bindings((1, Key.F9, HotkeyModifiers.None), (2, Key.F10, HotkeyModifiers.None)));

        Assert.False(result.Success);
        Assert.Contains("1001", result.ErrorMessage);
        Assert.Contains("1002", result.ErrorMessage);
        Assert.Empty(native.RegisteredGestures);
    }

    // Break caught: accepting bindings before a target window exists makes the registration state ambiguous.
    [Fact]
    public void Apply_WhenUnattached_RejectsWithoutRegistering()
    {
        var native = new FakeHotkeyNativeApi();
        using var service = new GlobalHotkeyService(native);

        var result = service.Apply(Bindings((1, Key.F4, HotkeyModifiers.None)));

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Empty(native.RegisteredGestures);
    }

    // Break caught: duplicate gestures register conflicting hotkeys for two timers.
    [Fact]
    public void Apply_WhenGesturesDuplicate_RejectsWithoutRegistering()
    {
        var native = new FakeHotkeyNativeApi();
        using var service = new GlobalHotkeyService(native);
        service.Attach((nint)42);

        var result = service.Apply(Bindings((1, Key.F4, HotkeyModifiers.Control), (2, Key.F4, HotkeyModifiers.Control)));

        Assert.False(result.Success);
        Assert.Empty(native.RegisteredGestures);
    }

    // Break caught: known native message IDs fail to start their assigned timer.
    [Fact]
    public void ProcessWindowMessage_RaisesTimerIndexForKnownId()
    {
        using var service = new GlobalHotkeyService(new FakeHotkeyNativeApi());
        service.Attach((nint)42);
        service.Apply(Bindings((2, Key.F8, HotkeyModifiers.None)));
        var pressed = 0;
        service.HotkeyPressed += (_, timerIndex) => pressed = timerIndex;

        var handled = service.ProcessWindowMessage(GlobalHotkeyService.WmHotkey, (nint)2);

        Assert.True(handled);
        Assert.Equal(2, pressed);
    }

    // Break caught: unrelated window messages are swallowed or trigger an arbitrary timer.
    [Fact]
    public void ProcessWindowMessage_WhenMessageOrIdIsUnknown_ReturnsFalseWithoutRaisingEvent()
    {
        using var service = new GlobalHotkeyService(new FakeHotkeyNativeApi());
        service.Attach((nint)42);
        service.Apply(Bindings((1, Key.F4, HotkeyModifiers.None)));
        var presses = 0;
        service.HotkeyPressed += (_, _) => presses++;

        Assert.False(service.ProcessWindowMessage(0, (nint)1));
        Assert.False(service.ProcessWindowMessage(GlobalHotkeyService.WmHotkey, (nint)99));
        Assert.Equal(0, presses);
    }

    // Break caught: ending an inner capture lease restores hotkeys while an outer capture is still active.
    [Fact]
    public void SuspendForCapture_WithNestedLeases_RestoresBindingsOnlyAfterFinalLeaseDisposes()
    {
        var native = new FakeHotkeyNativeApi();
        using var service = AttachedService(native, out var bindings);

        var outer = service.SuspendForCapture();
        var inner = service.SuspendForCapture();
        Assert.Empty(native.RegisteredGestures);

        inner.Dispose();
        Assert.Empty(native.RegisteredGestures);
        outer.Dispose();

        Assert.Equal(bindings.Select(binding => binding.Gesture), native.RegisteredGestures);
    }

    // Break caught: changing bindings during capture either loses the desired bindings or registers them while capture is active.
    [Fact]
    public void Apply_WhileSuspended_UpdatesActiveBindingsWithoutRegisteringUntilCaptureEnds()
    {
        var native = new FakeHotkeyNativeApi();
        using var service = AttachedService(native, out _);
        using var capture = service.SuspendForCapture();
        var replacement = Bindings((1, Key.F9, HotkeyModifiers.Control));

        var result = service.Apply(replacement);

        Assert.True(result.Success);
        Assert.Equal(replacement, service.ActiveBindings);
        Assert.Empty(native.RegisteredGestures);
        capture.Dispose();
        Assert.Equal(replacement.Select(binding => binding.Gesture), native.RegisteredGestures);
    }

    // Break caught: repeated disposal calls native cleanup again or an expired capture lease re-registers after shutdown.
    [Fact]
    public void Dispose_IsIdempotentAndExpiredCaptureLeaseCannotRestoreBindings()
    {
        var native = new FakeHotkeyNativeApi();
        var service = AttachedService(native, out _);
        var capture = service.SuspendForCapture();

        service.Dispose();
        service.Dispose();
        capture.Dispose();

        Assert.Empty(native.RegisteredGestures);
    }

    private static GlobalHotkeyService AttachedService(FakeHotkeyNativeApi native, out HotkeyBinding[] bindings)
    {
        var service = new GlobalHotkeyService(native);
        service.Attach((nint)42);
        bindings = Bindings((1, Key.F4, HotkeyModifiers.None), (2, Key.F8, HotkeyModifiers.None));
        Assert.True(service.Apply(bindings).Success);
        return service;
    }

    private static HotkeyBinding[] Bindings(params (int TimerIndex, Key Key, HotkeyModifiers Modifiers)[] values) =>
        values.Select(value => new HotkeyBinding(value.TimerIndex, new HotkeyGesture(value.Key, value.Modifiers))).ToArray();
}

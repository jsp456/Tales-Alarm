using TalesAlarm.Hotkeys;

namespace TalesAlarm.Tests.Hotkeys;

public sealed class RawInputMessageHookTests
{
    // Break caught: the WPF adapter drops lParam or marks pass-through input as handled.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProcessWindowMessage_ForwardsAllMessageDataWithoutChangingHandled(
        bool initialHandled)
    {
        var service = new RecordingGlobalHotkeyService();
        var hook = new RawInputMessageHook(service);
        var handled = initialHandled;

        var result = hook.ProcessWindowMessage(
            (nint)42,
            GlobalHotkeyService.WmInput,
            (nint)1,
            (nint)123,
            ref handled);

        Assert.Equal(0, result);
        Assert.Equal(initialHandled, handled);
        Assert.Equal(
            (GlobalHotkeyService.WmInput, (nint)1, (nint)123),
            service.LastMessage);
    }

    private sealed class RecordingGlobalHotkeyService : IGlobalHotkeyService
    {
        public event EventHandler<int>? HotkeyPressed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<HotkeyBinding> ActiveBindings => [];
        public (int Message, nint WParam, nint LParam)? LastMessage { get; private set; }

        public void Attach(nint windowHandle)
        {
        }

        public HotkeyApplyResult Apply(IReadOnlyList<HotkeyBinding> bindings) =>
            new(true, null);

        public IDisposable SuspendForCapture() => new EmptyLease();

        public void ProcessWindowMessage(int message, nint wParam, nint lParam) =>
            LastMessage = (message, wParam, lParam);

        public void Dispose()
        {
        }

        private sealed class EmptyLease : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}

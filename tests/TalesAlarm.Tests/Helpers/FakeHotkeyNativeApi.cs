using TalesAlarm.Hotkeys;

namespace TalesAlarm.Tests.Helpers;

internal sealed class FakeHotkeyNativeApi : IHotkeyNativeApi
{
    private readonly Dictionary<int, HotkeyGesture> registered = [];

    public HotkeyGesture? FailGesture { get; set; }
    public int FailureErrorCode { get; set; } = 1409;
    public Func<HotkeyGesture, int, int?>? RegistrationFailure { get; set; }
    public IReadOnlyList<HotkeyGesture> RegisteredGestures => registered.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();
    public int RegisterCallCount { get; private set; }

    public bool TryRegister(nint windowHandle, int id, HotkeyGesture gesture, out int errorCode)
    {
        RegisterCallCount++;
        var plannedError = RegistrationFailure?.Invoke(gesture, RegisterCallCount);
        if (gesture == FailGesture || plannedError is not null)
        {
            errorCode = plannedError ?? FailureErrorCode;
            return false;
        }

        registered.Add(id, gesture);
        errorCode = 0;
        return true;
    }

    public bool Unregister(nint windowHandle, int id) => registered.Remove(id);
}

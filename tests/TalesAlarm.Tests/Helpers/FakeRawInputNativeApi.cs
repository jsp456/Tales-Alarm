using TalesAlarm.Hotkeys;

namespace TalesAlarm.Tests.Helpers;

internal sealed class FakeRawInputNativeApi : IRawInputNativeApi
{
    public bool RegisterSucceeds { get; set; } = true;
    public int RegisterErrorCode { get; set; } = 1001;
    public bool UnregisterSucceeds { get; set; } = true;
    public int UnregisterErrorCode { get; set; } = 1002;
    public RawInputReadResult NextReadResult { get; set; } =
        RawInputReadResult.Ignored();
    public int RegisterCallCount { get; private set; }
    public int UnregisterCallCount { get; private set; }
    public int ReadCallCount { get; private set; }
    public nint RegisteredWindowHandle { get; private set; }
    public nint LastRawInputHandle { get; private set; }

    public bool TryRegisterKeyboard(nint windowHandle, out int errorCode)
    {
        RegisterCallCount++;
        RegisteredWindowHandle = windowHandle;
        errorCode = RegisterSucceeds ? 0 : RegisterErrorCode;
        return RegisterSucceeds;
    }

    public bool TryUnregisterKeyboard(out int errorCode)
    {
        UnregisterCallCount++;
        errorCode = UnregisterSucceeds ? 0 : UnregisterErrorCode;
        return UnregisterSucceeds;
    }

    public RawInputReadResult ReadKeyboard(nint rawInputHandle)
    {
        ReadCallCount++;
        LastRawInputHandle = rawInputHandle;
        return NextReadResult;
    }
}

namespace TalesAlarm.Hotkeys;

internal sealed class RawInputMessageHook(IGlobalHotkeyService hotkeyService)
{
    public nint ProcessWindowMessage(
        nint windowHandle,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        hotkeyService.ProcessWindowMessage(message, wParam, lParam);
        return 0;
    }
}

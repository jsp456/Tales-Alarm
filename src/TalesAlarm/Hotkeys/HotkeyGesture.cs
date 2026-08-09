using System.Text.Json.Serialization;
using System.Windows.Input;

namespace TalesAlarm.Hotkeys;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
}

public readonly record struct HotkeyGesture(Key Key, HotkeyModifiers Modifiers)
{
    [JsonIgnore]
    public bool HasNonModifierKey => Key is not Key.None
        and not Key.LeftAlt and not Key.RightAlt
        and not Key.LeftCtrl and not Key.RightCtrl
        and not Key.LeftShift and not Key.RightShift
        and not Key.LWin and not Key.RWin and not Key.System;
}

public readonly record struct HotkeyBinding(int TimerIndex, HotkeyGesture Gesture);

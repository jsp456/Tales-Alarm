using System.Text.Json.Serialization;
using System.Windows.Input;
using TalesAlarm.Hotkeys;
using TalesAlarm.Timers;

namespace TalesAlarm.Configuration;

public sealed record TimerSettings(
    long DurationSeconds,
    HotkeyGesture Hotkey,
    ReactivationPolicy ReactivationPolicy)
{
    [JsonIgnore]
    public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);
}

public sealed record AlarmSettings(
    bool UseDefaultSound,
    string? CustomFileName,
    decimal PlaybackSeconds);

public sealed record AppSettings(
    int SchemaVersion,
    TimerSettings Timer1,
    TimerSettings Timer2,
    AlarmSettings Alarm)
{
    public const int CurrentSchemaVersion = 1;

    public static AppSettings CreateDefault() => new(
        CurrentSchemaVersion,
        new(1200, new(Key.F4, HotkeyModifiers.None), ReactivationPolicy.Restart),
        new(1800, new(Key.F8, HotkeyModifiers.None), ReactivationPolicy.Restart),
        new(true, null, 1.5m));
}

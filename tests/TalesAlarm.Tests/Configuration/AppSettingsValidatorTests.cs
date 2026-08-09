using System.Text.Json;
using System.Windows.Input;
using TalesAlarm.Configuration;
using TalesAlarm.Hotkeys;
using TalesAlarm.Timers;

namespace TalesAlarm.Tests.Configuration;

public sealed class AppSettingsValidatorTests
{
    // Break caught: changing a shipped default makes newly created settings unsafe or surprising.
    [Fact]
    public void CreateDefault_UsesApprovedTimerAndAlarmValues()
    {
        var settings = AppSettings.CreateDefault();

        Assert.Equal(TimeSpan.FromMinutes(20), settings.Timer1.Duration);
        Assert.Equal(Key.F4, settings.Timer1.Hotkey.Key);
        Assert.Equal(TimeSpan.FromMinutes(30), settings.Timer2.Duration);
        Assert.Equal(Key.F8, settings.Timer2.Hotkey.Key);
        Assert.Equal(ReactivationPolicy.Restart, settings.Timer1.ReactivationPolicy);
        Assert.Equal(ReactivationPolicy.Restart, settings.Timer2.ReactivationPolicy);
        Assert.True(settings.Alarm.UseDefaultSound);
        Assert.Equal(1.5m, settings.Alarm.PlaybackSeconds);
    }

    // Break caught: accepting equal hotkeys or playback beyond the supported maximum makes settings unusable.
    [Fact]
    public void Validate_RejectsDuplicateHotkeysAndPlaybackBeyondMaximum()
    {
        var defaults = AppSettings.CreateDefault();
        var invalid = defaults with
        {
            Timer2 = defaults.Timer2 with { Hotkey = defaults.Timer1.Hotkey },
            Alarm = defaults.Alarm with { PlaybackSeconds = 60.1m },
        };

        var errors = SettingsValidator.Validate(invalid);

        Assert.Contains(errors, error => error.Field == "Timer2.Hotkey");
        Assert.Contains(errors, error => error.Field == "Alarm.PlaybackSeconds");
    }

    // Break caught: allowing a duration outside TimerLimits permits a timer the countdown contract rejects.
    [Theory]
    [InlineData(0L)]
    [InlineData(3_600_000L)]
    public void Validate_RejectsTimerDurationOutsideTimerLimits(long durationSeconds)
    {
        var defaults = AppSettings.CreateDefault();
        var invalid = defaults with
        {
            Timer1 = defaults.Timer1 with { DurationSeconds = durationSeconds },
        };

        var errors = SettingsValidator.Validate(invalid);

        Assert.Contains(errors, error => error.Field == "Timer1.DurationSeconds");
    }

    // Break caught: converting persisted extreme seconds before validation throws instead of returning a field error.
    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void Validate_RejectsExtremeTimerDurationWithoutThrowing(long durationSeconds)
    {
        var defaults = AppSettings.CreateDefault();
        var invalid = defaults with
        {
            Timer1 = defaults.Timer1 with { DurationSeconds = durationSeconds },
        };

        var errors = SettingsValidator.Validate(invalid);

        Assert.Contains(errors, error => error.Field == "Timer1.DurationSeconds");
    }

    // Break caught: changing either inclusive timer boundary to exclusive rejects supported durations.
    [Theory]
    [InlineData(1, 1L, "Timer1.DurationSeconds")]
    [InlineData(1, 3_599_999L, "Timer1.DurationSeconds")]
    [InlineData(2, 1L, "Timer2.DurationSeconds")]
    [InlineData(2, 3_599_999L, "Timer2.DurationSeconds")]
    public void Validate_AcceptsInclusiveTimerDurationBoundaries(
        int timerIndex,
        long durationSeconds,
        string durationField)
    {
        var defaults = AppSettings.CreateDefault();
        var settings = timerIndex == 1
            ? defaults with { Timer1 = defaults.Timer1 with { DurationSeconds = durationSeconds } }
            : defaults with { Timer2 = defaults.Timer2 with { DurationSeconds = durationSeconds } };

        var errors = SettingsValidator.Validate(settings);

        Assert.DoesNotContain(errors, error => error.Field == durationField);
    }

    // Break caught: persisted numeric values outside the policy enum silently select undefined timer behavior.
    [Theory]
    [InlineData(1, "Timer1.ReactivationPolicy")]
    [InlineData(2, "Timer2.ReactivationPolicy")]
    public void Validate_RejectsUndefinedReactivationPolicy(int timerIndex, string policyField)
    {
        var defaults = AppSettings.CreateDefault();
        var settings = timerIndex == 1
            ? defaults with { Timer1 = defaults.Timer1 with { ReactivationPolicy = (ReactivationPolicy)99 } }
            : defaults with { Timer2 = defaults.Timer2 with { ReactivationPolicy = (ReactivationPolicy)99 } };

        var errors = SettingsValidator.Validate(settings);

        Assert.Contains(errors, error => error.Field == policyField);
    }

    // Break caught: registering a modifier or no key as a hotkey creates a non-actionable binding.
    [Theory]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.System)]
    [InlineData(Key.None)]
    public void Validate_RejectsHotkeyWithoutNonModifierKey(Key key)
    {
        var defaults = AppSettings.CreateDefault();
        var invalid = defaults with
        {
            Timer1 = defaults.Timer1 with
            {
                Hotkey = new HotkeyGesture(key, HotkeyModifiers.Control),
            },
        };

        var errors = SettingsValidator.Validate(invalid);

        Assert.Contains(errors, error => error.Field == "Timer1.Hotkey");
    }

    // Break caught: accepting values outside the playback contract or with excess precision causes unsupported playback.
    [Theory]
    [InlineData(0.1)]
    [InlineData(60.0)]
    public void Validate_AcceptsPlaybackAtInclusiveBoundaries(double playbackSeconds)
    {
        var defaults = AppSettings.CreateDefault();
        var valid = defaults with
        {
            Alarm = defaults.Alarm with { PlaybackSeconds = (decimal)playbackSeconds },
        };

        var errors = SettingsValidator.Validate(valid);

        Assert.DoesNotContain(errors, error => error.Field == "Alarm.PlaybackSeconds");
    }

    // Break caught: accepting subminimum, overmaximum, or hundredth-second playback violates the supported range.
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.05)]
    [InlineData(60.1)]
    public void Validate_RejectsInvalidPlaybackValue(double playbackSeconds)
    {
        var defaults = AppSettings.CreateDefault();
        var invalid = defaults with
        {
            Alarm = defaults.Alarm with { PlaybackSeconds = (decimal)playbackSeconds },
        };

        var errors = SettingsValidator.Validate(invalid);

        Assert.Contains(errors, error => error.Field == "Alarm.PlaybackSeconds");
    }

    // Break caught: a non-default sound with no filename leaves the alarm without an audio source.
    [Fact]
    public void Validate_RejectsCustomSoundWithoutFileName()
    {
        var defaults = AppSettings.CreateDefault();
        var invalid = defaults with
        {
            Alarm = defaults.Alarm with { UseDefaultSound = false, CustomFileName = null },
        };

        var errors = SettingsValidator.Validate(invalid);

        Assert.Contains(errors, error => error.Field == "Alarm.CustomFileName");
    }

    // Break caught: accepting a path as a managed filename permits alarm selection outside the managed sound store.
    [Theory]
    [InlineData("C:\\sounds\\alarm.wav")]
    [InlineData("..\\alarm.wav")]
    [InlineData("sounds\\alarm.wav")]
    [InlineData(".")]
    [InlineData("..")]
    public void Validate_RejectsCustomSoundPathInsteadOfFileName(string customFileName)
    {
        var defaults = AppSettings.CreateDefault();
        var invalid = defaults with
        {
            Alarm = defaults.Alarm with { UseDefaultSound = false, CustomFileName = customFileName },
        };

        var errors = SettingsValidator.Validate(invalid);

        Assert.Contains(errors, error => error.Field == "Alarm.CustomFileName");
    }

    // Break caught: persisting a computed convenience property changes the serialized settings contract.
    [Fact]
    public void HotkeyGesture_SerializationDoesNotPersistDerivedKeyClassification()
    {
        var json = JsonSerializer.Serialize(new HotkeyGesture(Key.F4, HotkeyModifiers.Control));

        Assert.DoesNotContain("HasNonModifierKey", json, StringComparison.Ordinal);
    }
}

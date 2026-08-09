using System.IO;
using TalesAlarm.Timers;

namespace TalesAlarm.Configuration;

public sealed record SettingsValidationError(string Field, string Message);

public static class SettingsValidator
{
    private const decimal MinimumPlaybackSeconds = 0.1m;
    private const decimal MaximumPlaybackSeconds = 60.0m;

    public static IReadOnlyList<SettingsValidationError> Validate(AppSettings settings)
    {
        var errors = new List<SettingsValidationError>();

        if (settings.SchemaVersion != AppSettings.CurrentSchemaVersion)
        {
            errors.Add(new("SchemaVersion", "지원하지 않는 설정 버전입니다."));
        }

        ValidateTimer(settings.Timer1, "Timer1", errors);
        ValidateTimer(settings.Timer2, "Timer2", errors);

        if (settings.Timer1.Hotkey == settings.Timer2.Hotkey)
        {
            errors.Add(new("Timer2.Hotkey", "타이머 단축키는 서로 달라야 합니다."));
        }

        ValidateAlarm(settings.Alarm, errors);
        return errors;
    }

    private static void ValidateTimer(
        TimerSettings timer,
        string fieldPrefix,
        List<SettingsValidationError> errors)
    {
        var minimumDurationSeconds = TimerLimits.MinimumDuration.Ticks / TimeSpan.TicksPerSecond;
        var maximumDurationSeconds = TimerLimits.MaximumDuration.Ticks / TimeSpan.TicksPerSecond;
        if (timer.DurationSeconds < minimumDurationSeconds
            || timer.DurationSeconds > maximumDurationSeconds)
        {
            errors.Add(new($"{fieldPrefix}.DurationSeconds", "타이머 시간은 1초 이상 1000시간 미만이어야 합니다."));
        }

        if (!timer.Hotkey.HasNonModifierKey)
        {
            errors.Add(new($"{fieldPrefix}.Hotkey", "단축키에는 수정 키가 아닌 키가 필요합니다."));
        }

        if (timer.ReactivationPolicy is not (ReactivationPolicy.Restart
            or ReactivationPolicy.PauseResume
            or ReactivationPolicy.Ignore))
        {
            errors.Add(new($"{fieldPrefix}.ReactivationPolicy", "지원하지 않는 재활성화 정책입니다."));
        }
    }

    private static void ValidateAlarm(AlarmSettings alarm, List<SettingsValidationError> errors)
    {
        if (alarm.PlaybackSeconds < MinimumPlaybackSeconds
            || alarm.PlaybackSeconds > MaximumPlaybackSeconds
            || decimal.Truncate(alarm.PlaybackSeconds * 10) != alarm.PlaybackSeconds * 10)
        {
            errors.Add(new("Alarm.PlaybackSeconds", "알람 재생 시간은 0.1초에서 60.0초 사이의 소수 첫째 자리 값이어야 합니다."));
        }

        if (!alarm.UseDefaultSound && !IsManagedFileName(alarm.CustomFileName))
        {
            errors.Add(new("Alarm.CustomFileName", "사용자 지정 알람 파일 이름이 필요합니다."));
        }
    }

    private static bool IsManagedFileName(string? fileName) =>
        fileName is not null
        && !string.IsNullOrWhiteSpace(fileName)
        && fileName is not "." and not ".."
        && !Path.IsPathRooted(fileName)
        && !fileName.Contains('\\')
        && !fileName.Contains('/');
}

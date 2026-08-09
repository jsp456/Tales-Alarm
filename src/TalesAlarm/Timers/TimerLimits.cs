namespace TalesAlarm.Timers;

public static class TimerLimits
{
    public static readonly TimeSpan MinimumDuration = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaximumDuration =
        TimeSpan.FromHours(1000) - TimeSpan.FromSeconds(1);
}

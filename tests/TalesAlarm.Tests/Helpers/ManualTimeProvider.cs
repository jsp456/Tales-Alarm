namespace TalesAlarm.Tests.Helpers;

public sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset? initialUtcNow = null)
    {
        _utcNow = initialUtcNow ?? DateTimeOffset.UnixEpoch;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _timestamp;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan duration)
    {
        _timestamp += duration.Ticks;
        _utcNow = _utcNow.Add(duration);
    }
}

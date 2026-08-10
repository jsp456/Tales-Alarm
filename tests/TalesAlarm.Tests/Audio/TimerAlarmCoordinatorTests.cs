using TalesAlarm.Audio;
using TalesAlarm.Tests.Helpers;

namespace TalesAlarm.Tests.Audio;

public sealed class TimerAlarmCoordinatorTests
{
    // Break caught: operating timer 2 stops an alarm owned only by timer 1.
    [Fact]
    public void AcknowledgeTimer_WhenOnlyOtherTimerOwnsAlarm_KeepsPlaying()
    {
        var fixture = new Fixture();
        fixture.StartTimer(1, TimeSpan.FromSeconds(10));

        fixture.Coordinator.AcknowledgeTimer(2);

        Assert.True(fixture.Audio.IsPlaying);
        Assert.Equal(0, fixture.Audio.StopCalls);
    }

    // Break caught: acknowledging either one of two timer alarms stops the shared sound too early.
    [Fact]
    public void AcknowledgeTimer_WhenBothOwnAlarm_StopsOnlyAfterLastOwner()
    {
        var fixture = new Fixture();
        fixture.StartTimer(1, TimeSpan.FromSeconds(10));
        fixture.StartTimer(2, TimeSpan.FromSeconds(10));

        fixture.Coordinator.AcknowledgeTimer(1);
        Assert.True(fixture.Audio.IsPlaying);
        Assert.Equal(0, fixture.Audio.StopCalls);

        fixture.Coordinator.AcknowledgeTimer(2);
        Assert.False(fixture.Audio.IsPlaying);
        Assert.Equal(1, fixture.Audio.StopCalls);
    }

    // Break caught: the first timer's expiry stops a later timer alarm before its own deadline.
    [Fact]
    public void Tick_ExpiresOwnersIndependently()
    {
        var fixture = new Fixture();
        fixture.StartTimer(1, TimeSpan.FromSeconds(2));
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        fixture.StartTimer(2, TimeSpan.FromSeconds(4));

        fixture.Time.Advance(TimeSpan.FromSeconds(1.1));
        fixture.Coordinator.Tick();
        Assert.True(fixture.Audio.IsPlaying);

        fixture.Time.Advance(TimeSpan.FromSeconds(3));
        fixture.Coordinator.Tick();
        Assert.False(fixture.Audio.IsPlaying);
    }

    // Break caught: removing the latest owner leaves the audio deadline extended past the remaining owner.
    [Fact]
    public void AcknowledgeTimer_WhenLatestOwnerIsRemoved_ShortensAudioDeadline()
    {
        var fixture = new Fixture();
        fixture.StartTimer(1, TimeSpan.FromSeconds(3));
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        fixture.StartTimer(2, TimeSpan.FromSeconds(5));
        fixture.Time.Advance(TimeSpan.FromSeconds(1));

        fixture.Coordinator.AcknowledgeTimer(2);

        Assert.Equal(
            TimeSpan.FromSeconds(1),
            fixture.Audio.StartRequests[^1].Duration);
    }

    // Break caught: completing the same timer again creates a duplicate owner with the old deadline.
    [Fact]
    public void StartTimerAlarm_ForSameTimer_ReplacesItsDeadline()
    {
        var fixture = new Fixture();
        fixture.StartTimer(1, TimeSpan.FromSeconds(2));
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        fixture.StartTimer(1, TimeSpan.FromSeconds(3));
        fixture.Time.Advance(TimeSpan.FromSeconds(2));

        fixture.Coordinator.Tick();

        Assert.True(fixture.Audio.IsPlaying);
    }

    // Break caught: acknowledging a timer also stops an unrelated preview claim.
    [Fact]
    public void AcknowledgeTimer_WhenPreviewRemains_DoesNotStopPreview()
    {
        var fixture = new Fixture();
        fixture.Coordinator.StartPreview(
            "preview.wav",
            "default.wav",
            TimeSpan.FromSeconds(3));
        fixture.StartTimer(1, TimeSpan.FromSeconds(3));

        fixture.Coordinator.AcknowledgeTimer(1);

        Assert.True(fixture.Audio.IsPlaying);
        Assert.Equal(0, fixture.Audio.StopCalls);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Time = new ManualTimeProvider();
            Audio = new FakeAlarmAudioService();
            Coordinator = new TimerAlarmCoordinator(Time, Audio);
        }

        public ManualTimeProvider Time { get; }

        public FakeAlarmAudioService Audio { get; }

        public TimerAlarmCoordinator Coordinator { get; }

        public void StartTimer(int timerIndex, TimeSpan duration) =>
            Coordinator.StartTimerAlarm(
                timerIndex,
                "default.wav",
                "default.wav",
                duration);
    }

    private sealed class FakeAlarmAudioService : IAlarmAudioService
    {
        public bool IsPlaying { get; private set; }

        public string? LastError => null;

        public List<StartRequest> StartRequests { get; } = [];

        public int StopCalls { get; private set; }

        public int TickCalls { get; private set; }

        public void StartOrExtend(
            string requestedPath,
            string fallbackPath,
            TimeSpan duration)
        {
            StartRequests.Add(new(requestedPath, fallbackPath, duration));
            IsPlaying = true;
        }

        public void Stop()
        {
            if (!IsPlaying)
            {
                return;
            }

            IsPlaying = false;
            StopCalls++;
        }

        public void Tick() => TickCalls++;
    }

    private sealed record StartRequest(
        string RequestedPath,
        string FallbackPath,
        TimeSpan Duration);
}

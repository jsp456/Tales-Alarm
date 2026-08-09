using TalesAlarm.Tests.Helpers;
using TalesAlarm.Timers;

namespace TalesAlarm.Tests.Timers;

public sealed class CountdownTimerTests
{
    [Fact]
    public void Tick_UsesActualElapsedTimeAndCompletesOnce()
    {
        var time = new ManualTimeProvider();
        var timer = new CountdownTimer(time, TimeSpan.FromSeconds(20));
        var completions = 0;
        timer.Completed += (_, _) => completions++;

        timer.Start();
        time.Advance(TimeSpan.FromSeconds(7.4));
        timer.Tick();
        Assert.Equal(TimeSpan.FromSeconds(12.6), timer.Remaining);

        time.Advance(TimeSpan.FromSeconds(20));
        timer.Tick();
        timer.Tick();
        Assert.Equal(TimerState.Completed, timer.State);
        Assert.Equal(TimeSpan.Zero, timer.Remaining);
        Assert.Equal(1, completions);
    }

    [Theory]
    [InlineData(ReactivationPolicy.Restart, TimerState.Running)]
    [InlineData(ReactivationPolicy.PauseResume, TimerState.Paused)]
    [InlineData(ReactivationPolicy.Ignore, TimerState.Running)]
    public void HandleActivation_AppliesPolicyWhileRunning(
        ReactivationPolicy policy,
        TimerState expected)
    {
        var time = new ManualTimeProvider();
        var timer = new CountdownTimer(time, TimeSpan.FromSeconds(20));
        timer.Start();
        time.Advance(TimeSpan.FromSeconds(5));
        timer.Tick();

        timer.HandleActivation(policy);

        Assert.Equal(expected, timer.State);
        Assert.Equal(
            policy == ReactivationPolicy.Restart
                ? TimeSpan.FromSeconds(20)
                : TimeSpan.FromSeconds(15),
            timer.Remaining);
    }

    [Theory]
    [InlineData(TimerState.Running)]
    [InlineData(TimerState.Paused)]
    public void Configure_WhileActiveOnlyChangesConfiguredDuration(TimerState state)
    {
        var time = new ManualTimeProvider();
        var timer = new CountdownTimer(time, TimeSpan.FromSeconds(20));
        timer.Start();
        time.Advance(TimeSpan.FromSeconds(5));
        timer.Tick();

        if (state == TimerState.Paused)
        {
            timer.Pause();
        }

        timer.Configure(TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(30), timer.ConfiguredDuration);
        Assert.Equal(TimeSpan.FromSeconds(15), timer.Remaining);

        timer.Reset();

        Assert.Equal(TimerState.Idle, timer.State);
        Assert.Equal(TimeSpan.FromSeconds(30), timer.Remaining);
    }

    [Theory]
    [InlineData(TimerState.Idle)]
    [InlineData(TimerState.Completed)]
    public void HandleActivation_StartsFromIdleOrCompletedRegardlessOfPolicy(TimerState initialState)
    {
        var time = new ManualTimeProvider();
        var timer = new CountdownTimer(time, TimeSpan.FromSeconds(20));

        if (initialState == TimerState.Completed)
        {
            timer.Start();
            time.Advance(TimeSpan.FromSeconds(20));
            timer.Tick();
        }

        foreach (var policy in Enum.GetValues<ReactivationPolicy>())
        {
            timer.HandleActivation(policy);

            Assert.Equal(TimerState.Running, timer.State);
            Assert.Equal(TimeSpan.FromSeconds(20), timer.Remaining);

            timer.Reset();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3_600_000)]
    public void Constructor_RejectsDurationsOutsideSupportedRange(int seconds)
    {
        var time = new ManualTimeProvider();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CountdownTimer(time, TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3_600_000)]
    public void Configure_RejectsDurationsOutsideSupportedRange(int seconds)
    {
        var timer = new CountdownTimer(new ManualTimeProvider(), TimeSpan.FromSeconds(20));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => timer.Configure(TimeSpan.FromSeconds(seconds)));
    }
}

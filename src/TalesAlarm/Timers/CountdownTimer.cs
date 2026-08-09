namespace TalesAlarm.Timers;

public sealed class CountdownTimer
{
    private readonly TimeProvider _timeProvider;
    private long _startedAt;
    private TimeSpan _remainingAtStart;
    private bool _completionRaised;

    public CountdownTimer(TimeProvider timeProvider, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ValidateDuration(duration);

        _timeProvider = timeProvider;
        ConfiguredDuration = duration;
        Remaining = duration;
    }

    public TimeSpan ConfiguredDuration { get; private set; }

    public TimeSpan Remaining { get; private set; }

    public TimerState State { get; private set; } = TimerState.Idle;

    public event EventHandler? Completed;

    public void Configure(TimeSpan duration)
    {
        ValidateDuration(duration);
        ConfiguredDuration = duration;

        if (State == TimerState.Idle)
        {
            Remaining = duration;
        }
    }

    public void Start()
    {
        Remaining = ConfiguredDuration;
        _remainingAtStart = Remaining;
        _startedAt = _timeProvider.GetTimestamp();
        _completionRaised = false;
        State = TimerState.Running;
    }

    public void Pause()
    {
        if (State != TimerState.Running)
        {
            return;
        }

        Tick();
        if (State == TimerState.Running)
        {
            State = TimerState.Paused;
        }
    }

    public void Resume()
    {
        if (State != TimerState.Paused)
        {
            return;
        }

        _remainingAtStart = Remaining;
        _startedAt = _timeProvider.GetTimestamp();
        State = TimerState.Running;
    }

    public void Reset()
    {
        Remaining = ConfiguredDuration;
        _remainingAtStart = Remaining;
        _completionRaised = false;
        State = TimerState.Idle;
    }

    public void HandleActivation(ReactivationPolicy policy)
    {
        if (State == TimerState.Running)
        {
            Tick();
        }

        if (State is TimerState.Idle or TimerState.Completed)
        {
            Start();
            return;
        }

        switch (policy)
        {
            case ReactivationPolicy.Restart:
                Start();
                break;
            case ReactivationPolicy.PauseResume:
                if (State == TimerState.Running)
                {
                    Pause();
                }
                else
                {
                    Resume();
                }

                break;
            case ReactivationPolicy.Ignore:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(policy));
        }
    }

    public void Tick()
    {
        if (State != TimerState.Running)
        {
            return;
        }

        var elapsed = _timeProvider.GetElapsedTime(_startedAt);
        Remaining = elapsed >= _remainingAtStart
            ? TimeSpan.Zero
            : _remainingAtStart - elapsed;

        if (Remaining != TimeSpan.Zero)
        {
            return;
        }

        State = TimerState.Completed;
        if (_completionRaised)
        {
            return;
        }

        _completionRaised = true;
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private static void ValidateDuration(TimeSpan duration)
    {
        if (duration < TimerLimits.MinimumDuration || duration > TimerLimits.MaximumDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }
    }
}

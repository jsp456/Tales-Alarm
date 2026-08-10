namespace TalesAlarm.Audio;

public interface ITimerAlarmCoordinator
{
    void StartTimerAlarm(
        int timerIndex,
        string requestedPath,
        string fallbackPath,
        TimeSpan duration);

    void StartPreview(
        string requestedPath,
        string fallbackPath,
        TimeSpan duration);

    void AcknowledgeTimer(int timerIndex);

    void Tick();
}

public sealed class TimerAlarmCoordinator : ITimerAlarmCoordinator
{
    private readonly TimeProvider timeProvider;
    private readonly IAlarmAudioService audioService;
    private readonly Dictionary<int, AlarmClaim> timerClaims = [];
    private AlarmClaim? previewClaim;

    public TimerAlarmCoordinator(
        TimeProvider timeProvider,
        IAlarmAudioService audioService)
    {
        this.timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        this.audioService = audioService
            ?? throw new ArgumentNullException(nameof(audioService));
    }

    public void StartTimerAlarm(
        int timerIndex,
        string requestedPath,
        string fallbackPath,
        TimeSpan duration)
    {
        if (timerIndex <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timerIndex),
                "타이머 번호는 1 이상이어야 합니다.");
        }

        var claim = CreateClaim(requestedPath, fallbackPath, duration);
        timerClaims[timerIndex] = claim;
        ReconcileAudio(allowStart: true);
    }

    public void StartPreview(
        string requestedPath,
        string fallbackPath,
        TimeSpan duration)
    {
        previewClaim = CreateClaim(requestedPath, fallbackPath, duration);
        ReconcileAudio(allowStart: true);
    }

    public void AcknowledgeTimer(int timerIndex)
    {
        if (!timerClaims.Remove(timerIndex))
        {
            return;
        }

        ReconcileAudio(allowStart: false);
    }

    public void Tick()
    {
        RemoveExpiredClaims(timeProvider.GetTimestamp());
        if (!HasClaims)
        {
            audioService.Stop();
        }

        audioService.Tick();
    }

    private bool HasClaims => timerClaims.Count > 0 || previewClaim is not null;

    private AlarmClaim CreateClaim(
        string requestedPath,
        string fallbackPath,
        TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackPath);
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "재생 시간은 0보다 커야 합니다.");
        }

        return new AlarmClaim(
            requestedPath,
            fallbackPath,
            timeProvider.GetTimestamp(),
            duration);
    }

    private void ReconcileAudio(bool allowStart)
    {
        var now = timeProvider.GetTimestamp();
        RemoveExpiredClaims(now);

        var selection = FindLatestClaim(now);
        if (selection is null)
        {
            audioService.Stop();
            return;
        }

        if (!allowStart && !audioService.IsPlaying)
        {
            return;
        }

        audioService.StartOrExtend(
            selection.Value.Claim.RequestedPath,
            selection.Value.Claim.FallbackPath,
            selection.Value.Remaining);
    }

    private void RemoveExpiredClaims(long now)
    {
        var expiredTimerIndexes = timerClaims
            .Where(pair => GetRemaining(pair.Value, now) <= TimeSpan.Zero)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var timerIndex in expiredTimerIndexes)
        {
            timerClaims.Remove(timerIndex);
        }

        if (previewClaim is not null
            && GetRemaining(previewClaim, now) <= TimeSpan.Zero)
        {
            previewClaim = null;
        }
    }

    private ClaimSelection? FindLatestClaim(long now)
    {
        ClaimSelection? latest = null;

        foreach (var claim in timerClaims.Values)
        {
            latest = SelectLater(latest, claim, now);
        }

        if (previewClaim is not null)
        {
            latest = SelectLater(latest, previewClaim, now);
        }

        return latest;
    }

    private ClaimSelection SelectLater(
        ClaimSelection? current,
        AlarmClaim candidate,
        long now)
    {
        var selection = new ClaimSelection(
            candidate,
            GetRemaining(candidate, now));

        return current is null || selection.Remaining > current.Value.Remaining
            ? selection
            : current.Value;
    }

    private TimeSpan GetRemaining(AlarmClaim claim, long now) =>
        claim.Duration - timeProvider.GetElapsedTime(claim.StartedAt, now);

    private sealed record AlarmClaim(
        string RequestedPath,
        string FallbackPath,
        long StartedAt,
        TimeSpan Duration);

    private readonly record struct ClaimSelection(
        AlarmClaim Claim,
        TimeSpan Remaining);
}

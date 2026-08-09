namespace TalesAlarm.Audio;

public interface IAlarmAudioService
{
    bool IsPlaying { get; }

    string? LastError { get; }

    void StartOrExtend(string requestedPath, string fallbackPath, TimeSpan duration);

    void Tick();

    void Stop();
}

public sealed class AlarmAudioService : IAlarmAudioService, IDisposable
{
    private readonly TimeProvider timeProvider;
    private readonly IAudioBackend backend;
    private string fallbackPath = string.Empty;
    private long startedAt;
    private TimeSpan duration;
    private bool usingFallback;
    private bool disposed;

    public AlarmAudioService(TimeProvider timeProvider, IAudioBackend backend)
    {
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        backend.MediaEnded += OnMediaEnded;
        backend.MediaFailed += OnMediaFailed;
    }

    public bool IsPlaying { get; private set; }

    public string? LastError { get; private set; }

    public void StartOrExtend(string requestedPath, string fallbackPath, TimeSpan duration)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackPath);
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "재생 시간은 0보다 커야 합니다.");
        }

        startedAt = timeProvider.GetTimestamp();
        this.duration = duration;
        this.fallbackPath = fallbackPath;

        if (IsPlaying)
        {
            return;
        }

        LastError = null;
        usingFallback = PathsEqual(requestedPath, fallbackPath);
        IsPlaying = true;
        try
        {
            backend.Open(requestedPath);
            backend.Play();
        }
        catch (Exception exception)
        {
            HandleFailure($"음원을 재생하지 못했습니다: {exception.Message}", exception);
        }
    }

    public void Tick()
    {
        if (!IsPlaying || disposed)
        {
            return;
        }

        if (timeProvider.GetElapsedTime(startedAt) >= duration)
        {
            Stop();
        }
    }

    public void Stop()
    {
        if (!IsPlaying)
        {
            return;
        }

        IsPlaying = false;
        try
        {
            backend.Stop();
        }
        catch (Exception exception)
        {
            LastError = $"음원 재생을 중지하지 못했습니다: {exception.Message}";
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Stop();
        backend.MediaEnded -= OnMediaEnded;
        backend.MediaFailed -= OnMediaFailed;
        backend.Dispose();
        disposed = true;
    }

    private void OnMediaEnded(object? sender, EventArgs eventArgs)
    {
        if (!IsPlaying || disposed)
        {
            return;
        }

        if (timeProvider.GetElapsedTime(startedAt) >= duration)
        {
            Stop();
            return;
        }

        try
        {
            backend.Rewind();
            backend.Play();
        }
        catch (Exception exception)
        {
            HandleFailure($"음원을 반복 재생하지 못했습니다: {exception.Message}", exception);
        }
    }

    private void OnMediaFailed(object? sender, AudioFailureEventArgs eventArgs) =>
        HandleFailure(eventArgs.Message, eventArgs.Exception);

    private void HandleFailure(string message, Exception? exception)
    {
        if (!IsPlaying || disposed)
        {
            return;
        }

        if (!usingFallback)
        {
            usingFallback = true;
            try
            {
                backend.Open(fallbackPath);
                backend.Play();
                LastError = null;
                return;
            }
            catch (Exception fallbackException)
            {
                message = $"기본 음원도 재생하지 못했습니다: {fallbackException.Message}";
                exception = fallbackException;
            }
        }

        LastError = exception is null ? message : $"{message} ({exception.GetType().Name})";
        Stop();
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

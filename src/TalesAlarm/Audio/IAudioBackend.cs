namespace TalesAlarm.Audio;

public interface IAudioBackend : IDisposable
{
    event EventHandler? MediaEnded;
    event EventHandler<AudioFailureEventArgs>? MediaFailed;

    void Open(string absolutePath);

    void Play();

    void Stop();

    void Rewind();
}

public sealed class AudioFailureEventArgs(
    string message,
    Exception? exception = null) : EventArgs
{
    public string Message { get; } = message;

    public Exception? Exception { get; } = exception;
}

using TalesAlarm.Audio;

namespace TalesAlarm.Tests.Helpers;

internal sealed class FakeAudioBackend : IAudioBackend
{
    public event EventHandler? MediaEnded;
    public event EventHandler<AudioFailureEventArgs>? MediaFailed;

    public List<string> OpenedPaths { get; } = [];
    public int OpenCalls => OpenedPaths.Count;
    public int PlayCalls { get; private set; }
    public int StopCalls { get; private set; }
    public int RewindCalls { get; private set; }
    public int DisposeCalls { get; private set; }

    public void Open(string absolutePath) => OpenedPaths.Add(absolutePath);

    public void Play() => PlayCalls++;

    public void Stop() => StopCalls++;

    public void Rewind() => RewindCalls++;

    public void RaiseMediaEnded() => MediaEnded?.Invoke(this, EventArgs.Empty);

    public void RaiseMediaFailed(string message, Exception? exception = null) =>
        MediaFailed?.Invoke(this, new AudioFailureEventArgs(message, exception));

    public void Dispose() => DisposeCalls++;
}

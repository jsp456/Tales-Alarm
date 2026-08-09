using System.IO;
using System.Windows;
using System.Windows.Media;

namespace TalesAlarm.Audio;

public sealed class MediaPlayerAudioBackend : IAudioBackend
{
    private readonly MediaPlayer player = new();
    private bool disposed;

    public MediaPlayerAudioBackend()
    {
        player.MediaEnded += OnMediaEnded;
        player.MediaFailed += OnMediaFailed;
    }

    public event EventHandler? MediaEnded;

    public event EventHandler<AudioFailureEventArgs>? MediaFailed;

    public void Open(string absolutePath)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        if (!Path.IsPathFullyQualified(absolutePath))
        {
            throw new ArgumentException("음원 경로는 절대 경로여야 합니다.", nameof(absolutePath));
        }

        var mediaUri = new Uri(Path.GetFullPath(absolutePath), UriKind.Absolute);
        Invoke(() => player.Open(mediaUri));
    }

    public void Play()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Invoke(player.Play);
    }

    public void Stop()
    {
        if (disposed)
        {
            return;
        }

        Invoke(player.Stop);
    }

    public void Rewind()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Invoke(() => player.Position = TimeSpan.Zero);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Invoke(() =>
        {
            player.MediaEnded -= OnMediaEnded;
            player.MediaFailed -= OnMediaFailed;
            player.Close();
        });
        disposed = true;
    }

    private void OnMediaEnded(object? sender, EventArgs eventArgs) =>
        MediaEnded?.Invoke(this, EventArgs.Empty);

    private void OnMediaFailed(object? sender, ExceptionEventArgs eventArgs) =>
        MediaFailed?.Invoke(
            this,
            new AudioFailureEventArgs(
                eventArgs.ErrorException?.Message ?? "음원 재생에 실패했습니다.",
                eventArgs.ErrorException));

    private void Invoke(Action action)
    {
        if (player.Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        player.Dispatcher.Invoke(action);
    }
}

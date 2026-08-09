using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace TalesAlarm.Audio;

public sealed class MediaPlayerAudioProbe
    : IAudioProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    private readonly Dispatcher dispatcher;

    public MediaPlayerAudioProbe(Dispatcher? dispatcher = null)
    {
        this.dispatcher = dispatcher
            ?? System.Windows.Application.Current?.Dispatcher
            ?? Dispatcher.CurrentDispatcher;
    }

    public async Task<AudioProbeResult> ProbeAsync(
        string absolutePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(absolutePath) || !File.Exists(absolutePath))
        {
            return new(false, "선택한 음원 파일을 찾을 수 없습니다.");
        }

        var completion = new TaskCompletionSource<AudioProbeResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        MediaPlayer? player = null;
        EventHandler? openedHandler = null;
        EventHandler<ExceptionEventArgs>? failedHandler = null;

        await dispatcher.InvokeAsync(() =>
        {
            player = new MediaPlayer();
            openedHandler = (_, _) => completion.TrySetResult(new(true, null));
            failedHandler = (_, args) => completion.TrySetResult(new(
                false,
                $"선택한 음원을 재생할 수 없습니다: {args.ErrorException?.Message ?? "알 수 없는 오류"}"));
            player.MediaOpened += openedHandler;
            player.MediaFailed += failedHandler;
            try
            {
                player.Open(new Uri(Path.GetFullPath(absolutePath), UriKind.Absolute));
            }
            catch (Exception exception)
            {
                completion.TrySetResult(new(
                    false,
                    $"선택한 음원을 열 수 없습니다: {exception.Message}"));
            }
        });

        try
        {
            var delay = Task.Delay(ProbeTimeout, cancellationToken);
            var completed = await Task.WhenAny(completion.Task, delay).ConfigureAwait(false);
            if (completed == completion.Task)
            {
                return await completion.Task.ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new(false, "음원 확인 시간이 초과되었습니다.");
        }
        finally
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (player is null)
                {
                    return;
                }

                player.MediaOpened -= openedHandler;
                player.MediaFailed -= failedHandler;
                player.Close();
            });
        }
    }
}

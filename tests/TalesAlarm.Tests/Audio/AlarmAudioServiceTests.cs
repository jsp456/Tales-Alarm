using TalesAlarm.Audio;
using TalesAlarm.Tests.Helpers;

namespace TalesAlarm.Tests.Audio;

public sealed class AlarmAudioServiceTests
{
    // Break caught: a short source file ends once instead of looping until the alarm deadline.
    [Fact]
    public void MediaEnded_BeforeDeadline_RewindsAndContinues()
    {
        var time = new ManualTimeProvider();
        var backend = new FakeAudioBackend();
        using var service = new AlarmAudioService(time, backend);

        service.StartOrExtend("custom.wav", "default.wav", TimeSpan.FromSeconds(1.5));
        backend.RaiseMediaEnded();

        Assert.Equal(1, backend.RewindCalls);
        Assert.Equal(2, backend.PlayCalls);
        time.Advance(TimeSpan.FromSeconds(1.5));
        service.Tick();
        Assert.Equal(1, backend.StopCalls);
        Assert.False(service.IsPlaying);
    }

    // Break caught: a second timer completion overlaps audio or fails to extend the shared deadline.
    [Fact]
    public void SecondCompletion_ExtendsWithoutOpeningAnotherPlayer()
    {
        var time = new ManualTimeProvider();
        var backend = new FakeAudioBackend();
        using var service = new AlarmAudioService(time, backend);
        service.StartOrExtend("default.wav", "default.wav", TimeSpan.FromSeconds(1));
        time.Advance(TimeSpan.FromSeconds(0.8));

        service.StartOrExtend("default.wav", "default.wav", TimeSpan.FromSeconds(1));
        time.Advance(TimeSpan.FromSeconds(0.8));
        service.Tick();

        Assert.Equal(1, backend.OpenCalls);
        Assert.Equal(0, backend.StopCalls);
        Assert.True(service.IsPlaying);
    }

    // Break caught: custom-audio failure stops the alarm instead of switching to the default sound.
    [Fact]
    public void MediaFailed_ForRequestedAudio_OpensFallbackOnce()
    {
        var backend = new FakeAudioBackend();
        using var service = new AlarmAudioService(new ManualTimeProvider(), backend);
        service.StartOrExtend("custom.wav", "default.wav", TimeSpan.FromSeconds(2));

        backend.RaiseMediaFailed("사용자 음원 오류");

        Assert.Equal(new[] { "custom.wav", "default.wav" }, backend.OpenedPaths);
        Assert.Equal(2, backend.PlayCalls);
        Assert.True(service.IsPlaying);
        Assert.Null(service.LastError);
    }

    // Break caught: failure of the fallback causes an endless retry loop or leaves playback marked active.
    [Fact]
    public void MediaFailed_ForFallback_StopsAndReportsError()
    {
        var backend = new FakeAudioBackend();
        using var service = new AlarmAudioService(new ManualTimeProvider(), backend);
        service.StartOrExtend("custom.wav", "default.wav", TimeSpan.FromSeconds(2));
        backend.RaiseMediaFailed("사용자 음원 오류");

        backend.RaiseMediaFailed("기본 음원 오류");

        Assert.Equal(2, backend.OpenCalls);
        Assert.False(service.IsPlaying);
        Assert.Contains("기본 음원 오류", service.LastError);
        Assert.Equal(1, backend.StopCalls);
    }

    // Break caught: an already stopped backend is restarted by a late MediaEnded event.
    [Fact]
    public void Stop_PreventsLaterMediaEndedFromRestartingPlayback()
    {
        var backend = new FakeAudioBackend();
        using var service = new AlarmAudioService(new ManualTimeProvider(), backend);
        service.StartOrExtend("default.wav", "default.wav", TimeSpan.FromSeconds(2));

        service.Stop();
        backend.RaiseMediaEnded();

        Assert.Equal(1, backend.PlayCalls);
        Assert.Equal(0, backend.RewindCalls);
        Assert.Equal(1, backend.StopCalls);
        Assert.False(service.IsPlaying);
    }

    // Break caught: disposing the coordinator leaks the backend and keeps event subscriptions alive.
    [Fact]
    public void Dispose_StopsAndDisposesBackendOnce()
    {
        var backend = new FakeAudioBackend();
        var service = new AlarmAudioService(new ManualTimeProvider(), backend);
        service.StartOrExtend("default.wav", "default.wav", TimeSpan.FromSeconds(2));

        service.Dispose();
        service.Dispose();
        backend.RaiseMediaEnded();

        Assert.Equal(1, backend.StopCalls);
        Assert.Equal(1, backend.DisposeCalls);
        Assert.Equal(1, backend.PlayCalls);
    }
}

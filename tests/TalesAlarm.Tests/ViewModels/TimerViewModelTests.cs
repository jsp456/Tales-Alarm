using System.Windows.Input;
using TalesAlarm.Configuration;
using TalesAlarm.Hotkeys;
using TalesAlarm.Tests.Helpers;
using TalesAlarm.Timers;
using TalesAlarm.ViewModels;

namespace TalesAlarm.Tests.ViewModels;

public sealed class TimerViewModelTests
{
    // Break caught: fractional remaining seconds are rounded down and completion is forwarded repeatedly.
    [Fact]
    public void Tick_FormatsCeilingSecondsAndRaisesOneCompletionRequest()
    {
        var time = new ManualTimeProvider();
        var model = new CountdownTimer(time, TimeSpan.FromSeconds(2));
        var viewModel = new TimerViewModel(1, model, Settings(2));
        var completed = 0;
        viewModel.Completed += (_, _) => completed++;

        viewModel.StartCommand.Execute(null);
        time.Advance(TimeSpan.FromSeconds(1.01));
        viewModel.Tick();
        Assert.Equal("00:00:01", viewModel.DisplayTime);
        time.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();
        viewModel.Tick();

        Assert.Equal("00:00:00", viewModel.DisplayTime);
        Assert.Equal("완료", viewModel.StatusText);
        Assert.Equal(1, completed);
    }

    // Break caught: the explicit Start button incorrectly follows Ignore instead of restarting a running timer.
    [Fact]
    public void StartCommand_WhileRunning_AlwaysRestartsConfiguredDuration()
    {
        var time = new ManualTimeProvider();
        var viewModel = new TimerViewModel(
            1,
            new CountdownTimer(time, TimeSpan.FromSeconds(10)),
            Settings(10, ReactivationPolicy.Ignore));
        viewModel.StartCommand.Execute(null);
        time.Advance(TimeSpan.FromSeconds(6));
        viewModel.Tick();

        viewModel.StartCommand.Execute(null);
        time.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();

        Assert.Equal("00:00:09", viewModel.DisplayTime);
        Assert.Equal("실행 중", viewModel.StatusText);
    }

    // Break caught: pause/resume is enabled in idle/completed states or does not toggle state.
    [Fact]
    public void PauseResumeCommand_IsEnabledOnlyWhileRunningOrPaused()
    {
        var viewModel = CreateViewModel(out _);

        Assert.False(viewModel.PauseResumeCommand.CanExecute(null));
        viewModel.StartCommand.Execute(null);
        Assert.True(viewModel.PauseResumeCommand.CanExecute(null));

        viewModel.PauseResumeCommand.Execute(null);
        Assert.Equal("일시정지", viewModel.StatusText);
        Assert.True(viewModel.PauseResumeCommand.CanExecute(null));

        viewModel.PauseResumeCommand.Execute(null);
        Assert.Equal("실행 중", viewModel.StatusText);
    }

    // Break caught: applying a new duration mutates a running countdown or Reset keeps the old duration.
    [Fact]
    public void ApplySavedSettings_DuringRun_TakesEffectOnResetNotImmediately()
    {
        var viewModel = CreateViewModel(out var time);
        viewModel.StartCommand.Execute(null);
        time.Advance(TimeSpan.FromSeconds(3));
        viewModel.Tick();

        viewModel.ApplySavedSettings(Settings(20));

        Assert.Equal("00:00:07", viewModel.DisplayTime);
        viewModel.ResetCommand.Execute(null);
        Assert.Equal("00:00:20", viewModel.DisplayTime);
        Assert.Equal("대기", viewModel.StatusText);
    }

    // Break caught: unsaved policy edits affect global-hotkey behavior before Apply Settings succeeds.
    [Fact]
    public void HandleHotkey_UsesLastAppliedPolicy()
    {
        var viewModel = new TimerViewModel(
            1,
            new CountdownTimer(new ManualTimeProvider(), TimeSpan.FromSeconds(10)),
            Settings(10, ReactivationPolicy.PauseResume));
        viewModel.HandleHotkey();
        viewModel.ReactivationPolicy = ReactivationPolicy.Ignore;

        viewModel.HandleHotkey();

        Assert.Equal("일시정지", viewModel.StatusText);
        viewModel.ApplySavedSettings(Settings(10, ReactivationPolicy.Ignore));
        viewModel.HandleHotkey();
        Assert.Equal("일시정지", viewModel.StatusText);
    }

    // Break caught: component ranges such as 60 minutes bypass validation through an equivalent total duration.
    [Fact]
    public void CreateDraftSettings_RejectsOutOfRangeComponents()
    {
        var viewModel = CreateViewModel(out _);
        viewModel.Minutes = 60;

        viewModel.CreateDraftSettings();

        Assert.NotNull(viewModel.ValidationMessage);
    }

    private static TimerViewModel CreateViewModel(out ManualTimeProvider time)
    {
        time = new ManualTimeProvider();
        return new(1, new CountdownTimer(time, TimeSpan.FromSeconds(10)), Settings(10));
    }

    private static TimerSettings Settings(
        long durationSeconds,
        ReactivationPolicy policy = ReactivationPolicy.Restart) =>
        new(durationSeconds, new HotkeyGesture(Key.F4, HotkeyModifiers.None), policy);
}

using System.IO;
using System.Windows.Input;
using TalesAlarm.Audio;
using TalesAlarm.Configuration;
using TalesAlarm.Hotkeys;
using TalesAlarm.Tests.Helpers;
using TalesAlarm.Timers;
using TalesAlarm.ViewModels;

namespace TalesAlarm.Tests.ViewModels;

public sealed class MainViewModelTests
{
    // Break caught: the saved compact-view preference is ignored or toggling the view is not persisted.
    [Fact]
    public async Task ToggleCompactView_FromSavedCompactMode_UpdatesAndPersistsDetailedMode()
    {
        var settings = AppSettings.CreateDefault() with { UseCompactView = true };
        using var fixture = Fixture.Create(settings);
        await fixture.ViewModel.InitializeAsync();

        await ((AsyncRelayCommand)fixture.ViewModel.ToggleCompactViewCommand).ExecuteAsync();

        Assert.False(fixture.ViewModel.IsCompactView);
        Assert.False(Assert.Single(fixture.Settings.SavedSettings).UseCompactView);
    }

    // Break caught: a failed settings save leaves candidate hotkeys active instead of restoring saved bindings.
    [Fact]
    public async Task ApplySettings_WhenSaveFails_RestoresPreviousHotkeysAndSettings()
    {
        using var fixture = Fixture.Create();
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.Timer1.Hotkey = new(Key.F9, HotkeyModifiers.Control);
        fixture.Settings.FailNextSave = true;

        var applied = await fixture.ViewModel.ApplySettingsAsync();

        Assert.False(applied);
        Assert.Equal(
            Key.F4,
            fixture.Hotkeys.ActiveBindings.Single(binding => binding.TimerIndex == 1).Gesture.Key);
        Assert.Contains("저장", fixture.ViewModel.ErrorMessage);
    }

    // Break caught: acknowledging either one of two completed timers stops both alarms.
    [Fact]
    public async Task TimerOperation_WhenBothAlarmsAreActive_KeepsOtherAlarmPlaying()
    {
        var settings = WithDurations(1, 1);
        using var fixture = Fixture.Create(settings);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.Timer1.StartCommand.Execute(null);
        fixture.ViewModel.Timer2.StartCommand.Execute(null);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));

        fixture.ViewModel.Tick();

        fixture.ViewModel.Timer1.ResetCommand.Execute(null);
        Assert.True(fixture.Audio.IsPlaying);

        fixture.ViewModel.Timer2.ResetCommand.Execute(null);
        Assert.False(fixture.Audio.IsPlaying);
        Assert.Equal(1, fixture.Audio.StopCalls);
    }

    // Break caught: a later completion while sound is active is ignored instead of extending its deadline.
    [Fact]
    public async Task Tick_WhenSecondTimerCompletesLater_ExtendsAudioWithSecondRequest()
    {
        using var fixture = Fixture.Create(WithDurations(1, 2));
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.Timer1.StartCommand.Execute(null);
        fixture.ViewModel.Timer2.StartCommand.Execute(null);

        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        fixture.ViewModel.Tick();
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        fixture.ViewModel.Tick();

        Assert.Equal(2, fixture.Audio.StartRequests.Count);
    }

    // Break caught: operating timer 2 stops an alarm that belongs only to timer 1.
    [Fact]
    public async Task TimerOperation_StopsOnlyAlarmOwnedByThatTimer()
    {
        using var fixture = Fixture.Create(WithDurations(1, 10));
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.Timer1.StartCommand.Execute(null);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        fixture.ViewModel.Tick();
        Assert.True(fixture.Audio.IsPlaying);

        fixture.ViewModel.Timer2.ResetCommand.Execute(null);
        Assert.True(fixture.Audio.IsPlaying);
        Assert.Equal(0, fixture.Audio.StopCalls);

        fixture.ViewModel.Timer1.ResetCommand.Execute(null);
        Assert.False(fixture.Audio.IsPlaying);
        Assert.Equal(1, fixture.Audio.StopCalls);
    }

    // Break caught: pausing exactly at the deadline re-registers the completion on the next UI tick.
    [Fact]
    public async Task PauseAtDeadline_DoesNotStartAlarmAfterSameOperationAcknowledgesIt()
    {
        using var fixture = Fixture.Create(WithDurations(1, 10));
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.Timer1.StartCommand.Execute(null);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));

        fixture.ViewModel.Timer1.PauseResumeCommand.Execute(null);
        fixture.ViewModel.Tick();

        Assert.False(fixture.Audio.IsPlaying);
        Assert.Empty(fixture.Audio.StartRequests);
    }

    // Break caught: a timer operation also stops a preview that has no timer owner.
    [Fact]
    public async Task TimerOperation_WhenOnlyPreviewOwnsPlayback_DoesNotStopPreview()
    {
        using var fixture = Fixture.Create();
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.Alarm.PreviewCommand.Execute(null);

        fixture.ViewModel.Timer1.ResetCommand.Execute(null);

        Assert.True(fixture.Audio.IsPlaying);
        Assert.Equal(0, fixture.Audio.StopCalls);
    }

    // Break caught: applying settings acknowledges an active completed-timer alarm.
    [Fact]
    public async Task ApplySettings_DoesNotAcknowledgeActiveTimerAlarm()
    {
        using var fixture = Fixture.Create(WithDurations(1, 10));
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.Timer1.StartCommand.Execute(null);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        fixture.ViewModel.Tick();
        fixture.ViewModel.Timer1.Hours = 0;
        fixture.ViewModel.Timer1.Minutes = 0;
        fixture.ViewModel.Timer1.Seconds = 2;

        Assert.True(await fixture.ViewModel.ApplySettingsAsync());
        Assert.True(fixture.Audio.IsPlaying);
    }

    // Break caught: switching compact view acknowledges an active completed-timer alarm.
    [Fact]
    public async Task ToggleCompactView_DoesNotAcknowledgeActiveTimerAlarm()
    {
        using var fixture = Fixture.Create(WithDurations(1, 10));
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.Timer1.StartCommand.Execute(null);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        fixture.ViewModel.Tick();

        await ((AsyncRelayCommand)fixture.ViewModel.ToggleCompactViewCommand).ExecuteAsync();

        Assert.True(fixture.Audio.IsPlaying);
    }

    // Break caught: either timer hotkey acknowledges every active timer alarm.
    [Fact]
    public async Task MatchingHotkey_AcknowledgesOnlyItsTimerAlarm()
    {
        using var fixture = Fixture.Create(WithDurations(1, 1));
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.Timer1.StartCommand.Execute(null);
        fixture.ViewModel.Timer2.StartCommand.Execute(null);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        fixture.ViewModel.Tick();

        fixture.Hotkeys.RaisePressed(1);
        Assert.True(fixture.Audio.IsPlaying);

        fixture.Hotkeys.RaisePressed(2);
        Assert.False(fixture.Audio.IsPlaying);
    }

    // Break caught: a known global hotkey ID starts both timers or the wrong timer.
    [Fact]
    public async Task HotkeyPressed_RoutesOnlyToMatchingTimer()
    {
        using var fixture = Fixture.Create();
        await fixture.ViewModel.InitializeAsync();

        fixture.Hotkeys.RaisePressed(1);

        Assert.Equal("실행 중", fixture.ViewModel.Timer1.StatusText);
        Assert.Equal("대기", fixture.ViewModel.Timer2.StatusText);
        fixture.Hotkeys.RaisePressed(2);
        Assert.Equal("실행 중", fixture.ViewModel.Timer2.StatusText);
    }

    // Break caught: invalid duration components still unregister and attempt candidate hotkeys.
    [Fact]
    public async Task ApplySettings_WithInvalidFields_DoesNotTouchHotkeyRegistration()
    {
        using var fixture = Fixture.Create();
        await fixture.ViewModel.InitializeAsync();
        var applyCalls = fixture.Hotkeys.ApplyCalls;
        fixture.ViewModel.Timer1.Minutes = 60;

        var applied = await fixture.ViewModel.ApplySettingsAsync();

        Assert.False(applied);
        Assert.Equal(applyCalls, fixture.Hotkeys.ApplyCalls);
        Assert.NotNull(fixture.ViewModel.Timer1.ValidationMessage);
    }

    // Break caught: Raw Input registration failure aborts app initialization or hides the hotkey-specific error.
    [Fact]
    public async Task InitializeAsync_WhenRawInputRegistrationFailed_ShowsHotkeyError()
    {
        using var fixture = Fixture.Create();
        fixture.Hotkeys.NextApplyResult = new(
            false,
            "키보드 Raw Input을 등록하지 못했습니다. Windows 오류 코드: 87.");

        await fixture.ViewModel.InitializeAsync();

        Assert.Contains("Raw Input", fixture.ViewModel.ErrorMessage);
        Assert.Contains("87", fixture.ViewModel.ErrorMessage);
        fixture.ViewModel.Timer1.StartCommand.Execute(null);
        Assert.Equal("실행 중", fixture.ViewModel.Timer1.StatusText);
    }

    // Break caught: corrupt-settings recovery is silently hidden from the user.
    [Fact]
    public async Task InitializeAsync_WhenSettingsRecovered_DisplaysRecoveryNotice()
    {
        using var fixture = Fixture.Create(
            recoveryMessage: "설정 파일이 손상되어 기본값으로 복구했습니다.",
            backupPath: "settings.corrupt.json");

        await fixture.ViewModel.InitializeAsync();

        Assert.Contains("복구", fixture.ViewModel.NoticeMessage);
        Assert.Contains("settings.corrupt.json", fixture.ViewModel.NoticeMessage);
    }

    // Break caught: preview ignores the visible playback duration or opens a different sound path.
    [Fact]
    public async Task Preview_UsesCurrentSoundAndVisibleDuration()
    {
        using var fixture = Fixture.Create();
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.Alarm.PlaybackSecondsText = "2.3";

        fixture.ViewModel.Alarm.PreviewCommand.Execute(null);

        var request = Assert.Single(fixture.Audio.StartRequests);
        Assert.Equal(fixture.DefaultAlarmPath, request.RequestedPath);
        Assert.Equal(fixture.DefaultAlarmPath, request.FallbackPath);
        Assert.Equal(TimeSpan.FromSeconds(2.3), request.Duration);
    }

    // Break caught: a successful managed import updates the label but does not persist the returned filename.
    [Fact]
    public async Task ImportAudioAsync_WhenSuccessful_PersistsReturnedFileName()
    {
        using var fixture = Fixture.Create();
        fixture.AudioStore.NextImportResult = new(true, "custom-new.wav", null);
        await fixture.ViewModel.InitializeAsync();

        var imported = await fixture.ViewModel.Alarm.ImportAudioAsync("C:\\picked.wav");

        Assert.True(imported);
        Assert.False(fixture.ViewModel.Alarm.UseDefaultSound);
        Assert.Equal("custom-new.wav", fixture.ViewModel.Alarm.CurrentFileName);
        Assert.Equal("custom-new.wav", Assert.Single(fixture.Settings.SavedSettings).Alarm.CustomFileName);
    }

    // Break caught: a failed import replaces the visible and saved previous selection.
    [Fact]
    public async Task ImportAudioAsync_WhenFailed_PreservesPreviousSelection()
    {
        using var fixture = Fixture.Create();
        fixture.AudioStore.NextImportResult = new(false, null, "가져오기 실패");
        await fixture.ViewModel.InitializeAsync();

        var imported = await fixture.ViewModel.Alarm.ImportAudioAsync("C:\\picked.wav");

        Assert.False(imported);
        Assert.True(fixture.ViewModel.Alarm.UseDefaultSound);
        Assert.Equal("기본 알람", fixture.ViewModel.Alarm.CurrentFileName);
        Assert.Empty(fixture.Settings.SavedSettings);
        Assert.Contains("가져오기 실패", fixture.ViewModel.ErrorMessage);
    }

    // Break caught: failed default-setting persistence clears the custom selection in memory.
    [Fact]
    public async Task RestoreDefault_WhenPersistenceFails_PreservesCustomSelection()
    {
        var settings = AppSettings.CreateDefault() with
        {
            Alarm = new(false, "custom-old.wav", 1.5m),
        };
        using var fixture = Fixture.Create(settings);
        await fixture.ViewModel.InitializeAsync();
        fixture.Settings.FailNextSave = true;

        await ((AsyncRelayCommand)fixture.ViewModel.Alarm.RestoreDefaultCommand).ExecuteAsync();

        Assert.False(fixture.ViewModel.Alarm.UseDefaultSound);
        Assert.Equal("custom-old.wav", fixture.ViewModel.Alarm.CurrentFileName);
        Assert.Contains("저장", fixture.ViewModel.ErrorMessage);
    }

    // Break caught: successful default restoration persists but leaves the custom selection active.
    [Fact]
    public async Task RestoreDefault_WhenPersistenceSucceeds_ClearsCustomSelection()
    {
        var settings = AppSettings.CreateDefault() with
        {
            Alarm = new(false, "custom-old.wav", 1.5m),
        };
        using var fixture = Fixture.Create(settings);
        await fixture.ViewModel.InitializeAsync();

        await ((AsyncRelayCommand)fixture.ViewModel.Alarm.RestoreDefaultCommand).ExecuteAsync();

        Assert.True(fixture.ViewModel.Alarm.UseDefaultSound);
        Assert.Null(Assert.Single(fixture.Settings.SavedSettings).Alarm.CustomFileName);
    }

    private static AppSettings WithDurations(long timer1Seconds, long timer2Seconds)
    {
        var defaults = AppSettings.CreateDefault();
        return defaults with
        {
            Timer1 = defaults.Timer1 with { DurationSeconds = timer1Seconds },
            Timer2 = defaults.Timer2 with { DurationSeconds = timer2Seconds },
        };
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(
            AppSettings settings,
            string? recoveryMessage,
            string? backupPath)
        {
            Temporary = new TemporaryDirectory();
            Paths = new AppPaths(Temporary.Path);
            Time = new ManualTimeProvider();
            Settings = new FakeSettingsService(new(settings, recoveryMessage, backupPath));
            Hotkeys = new FakeGlobalHotkeyService();
            Audio = new FakeAlarmAudioService();
            AlarmCoordinator = new TimerAlarmCoordinator(Time, Audio);
            AudioStore = new FakeUserAudioStore();
            DefaultAlarmPath = Path.Combine(Paths.AudioDirectory, "default-alarm.wav");
            var installer = new FakeDefaultAlarmInstaller(DefaultAlarmPath);
            ViewModel = new MainViewModel(
                Paths,
                new CountdownTimer(Time, settings.Timer1.Duration),
                new CountdownTimer(Time, settings.Timer2.Duration),
                Settings,
                Hotkeys,
                AlarmCoordinator,
                AudioStore,
                installer);
        }

        public TemporaryDirectory Temporary { get; }
        public AppPaths Paths { get; }
        public ManualTimeProvider Time { get; }
        public FakeSettingsService Settings { get; }
        public FakeGlobalHotkeyService Hotkeys { get; }
        public FakeAlarmAudioService Audio { get; }
        public TimerAlarmCoordinator AlarmCoordinator { get; }
        public FakeUserAudioStore AudioStore { get; }
        public string DefaultAlarmPath { get; }
        public MainViewModel ViewModel { get; }

        public static Fixture Create(
            AppSettings? settings = null,
            string? recoveryMessage = null,
            string? backupPath = null) =>
            new(settings ?? AppSettings.CreateDefault(), recoveryMessage, backupPath);

        public void Dispose() => Temporary.Dispose();
    }

    private sealed class FakeSettingsService(SettingsLoadResult loadResult) : ISettingsService
    {
        public bool FailNextSave { get; set; }
        public List<AppSettings> SavedSettings { get; } = [];

        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(loadResult);
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("설정 저장 실패");
            }

            SavedSettings.Add(settings);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeGlobalHotkeyService : IGlobalHotkeyService
    {
        private HotkeyBinding[] activeBindings = [];

        public event EventHandler<int>? HotkeyPressed;
        public IReadOnlyList<HotkeyBinding> ActiveBindings => activeBindings;
        public int ApplyCalls { get; private set; }
        public HotkeyApplyResult NextApplyResult { get; set; } = new(true, null);

        public void Attach(nint windowHandle)
        {
        }

        public HotkeyApplyResult Apply(IReadOnlyList<HotkeyBinding> bindings)
        {
            ApplyCalls++;
            if (!NextApplyResult.Success)
            {
                return NextApplyResult;
            }

            activeBindings = bindings.ToArray();
            return NextApplyResult;
        }

        public IDisposable SuspendForCapture() => new Lease();

        public void ProcessWindowMessage(int message, nint wParam, nint lParam)
        {
        }

        public void RaisePressed(int timerIndex) => HotkeyPressed?.Invoke(this, timerIndex);

        public void Dispose()
        {
        }

        private sealed class Lease : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class FakeAlarmAudioService : IAlarmAudioService
    {
        public bool IsPlaying { get; private set; }
        public string? LastError => null;
        public List<StartRequest> StartRequests { get; } = [];
        public int StopCalls { get; private set; }
        public int TickCalls { get; private set; }

        public void StartOrExtend(string requestedPath, string fallbackPath, TimeSpan duration)
        {
            StartRequests.Add(new(requestedPath, fallbackPath, duration));
            IsPlaying = true;
        }

        public void Tick() => TickCalls++;

        public void Stop()
        {
            if (!IsPlaying)
            {
                return;
            }

            IsPlaying = false;
            StopCalls++;
        }
    }

    private sealed record StartRequest(string RequestedPath, string FallbackPath, TimeSpan Duration);

    private sealed class FakeUserAudioStore : IUserAudioStore
    {
        public AudioImportResult NextImportResult { get; set; } = new(true, "custom-default.wav", null);

        public async Task<AudioImportResult> ImportAsync(
            string sourcePath,
            string? previousFileName,
            Func<string, Task> persistFileName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NextImportResult.Success)
            {
                await persistFileName(NextImportResult.FileName!);
            }

            return NextImportResult;
        }

        public async Task RestoreDefaultAsync(
            string? previousFileName,
            Func<Task> persistDefault,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await persistDefault();
        }
    }

    private sealed class FakeDefaultAlarmInstaller(string path) : IDefaultAlarmInstaller
    {
        public Task<string> EnsureInstalledAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(path);
        }
    }
}

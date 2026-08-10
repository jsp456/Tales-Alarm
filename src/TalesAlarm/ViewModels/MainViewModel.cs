using System.IO;
using System.Windows.Input;
using TalesAlarm.Audio;
using TalesAlarm.Configuration;
using TalesAlarm.Hotkeys;
using TalesAlarm.Timers;

namespace TalesAlarm.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppPaths paths;
    private readonly ISettingsService settingsService;
    private readonly IGlobalHotkeyService hotkeyService;
    private readonly ITimerAlarmCoordinator alarmCoordinator;
    private readonly IDefaultAlarmInstaller defaultAlarmInstaller;
    private readonly HashSet<int> pendingCompletions = [];
    private AppSettings savedSettings = AppSettings.CreateDefault();
    private string defaultAlarmPath = string.Empty;
    private string? errorMessage;
    private string? noticeMessage;
    private bool initialized;
    private bool isCompactView;

    public MainViewModel(
        AppPaths paths,
        CountdownTimer timer1,
        CountdownTimer timer2,
        ISettingsService settingsService,
        IGlobalHotkeyService hotkeyService,
        ITimerAlarmCoordinator alarmCoordinator,
        IUserAudioStore userAudioStore,
        IDefaultAlarmInstaller defaultAlarmInstaller)
    {
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
        this.alarmCoordinator = alarmCoordinator
            ?? throw new ArgumentNullException(nameof(alarmCoordinator));
        this.defaultAlarmInstaller = defaultAlarmInstaller
            ?? throw new ArgumentNullException(nameof(defaultAlarmInstaller));

        var defaults = AppSettings.CreateDefault();
        Timer1 = new TimerViewModel(1, timer1, defaults.Timer1);
        Timer2 = new TimerViewModel(2, timer2, defaults.Timer2);
        Alarm = new AlarmSettingsViewModel(
            paths,
            userAudioStore,
            alarmCoordinator,
            PersistAlarmSettingsAsync,
            SetErrorMessage);
        Alarm.ApplySavedSettings(defaults.Alarm);
        Timer1.Completed += OnTimerCompleted;
        Timer2.Completed += OnTimerCompleted;
        Timer1.Operated += OnTimerOperated;
        Timer2.Operated += OnTimerOperated;
        ApplySettingsCommand = new AsyncRelayCommand(
            async () => { await ApplySettingsAsync().ConfigureAwait(true); },
            onException: exception => SetErrorMessage($"설정을 적용하지 못했습니다: {exception.Message}"));
        ToggleCompactViewCommand = new AsyncRelayCommand(ToggleCompactViewAsync);
    }

    public TimerViewModel Timer1 { get; }

    public TimerViewModel Timer2 { get; }

    public AlarmSettingsViewModel Alarm { get; }

    public string? ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public string? NoticeMessage
    {
        get => noticeMessage;
        private set => SetProperty(ref noticeMessage, value);
    }

    public bool IsCompactView
    {
        get => isCompactView;
        private set => SetProperty(ref isCompactView, value);
    }

    public ICommand ApplySettingsCommand { get; }

    public ICommand ToggleCompactViewCommand { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized)
        {
            return;
        }

        var loadResult = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(true);
        savedSettings = loadResult.Settings;
        IsCompactView = savedSettings.UseCompactView;
        Timer1.ApplySavedSettings(savedSettings.Timer1);
        Timer2.ApplySavedSettings(savedSettings.Timer2);
        Alarm.ApplySavedSettings(savedSettings.Alarm);

        defaultAlarmPath = await defaultAlarmInstaller
            .EnsureInstalledAsync(cancellationToken)
            .ConfigureAwait(true);
        Alarm.SetDefaultAlarmPath(defaultAlarmPath);

        hotkeyService.HotkeyPressed += OnHotkeyPressed;
        var hotkeyResult = hotkeyService.Apply(CreateBindings(savedSettings));
        ErrorMessage = hotkeyResult.Success ? null : hotkeyResult.ErrorMessage;
        NoticeMessage = loadResult.RecoveryMessage;
        if (!string.IsNullOrWhiteSpace(loadResult.BackupPath))
        {
            NoticeMessage = string.IsNullOrWhiteSpace(NoticeMessage)
                ? $"설정 백업: {loadResult.BackupPath}"
                : $"{NoticeMessage} 백업: {loadResult.BackupPath}";
        }

        initialized = true;
    }

    public async Task<bool> ApplySettingsAsync(CancellationToken cancellationToken = default)
    {
        ErrorMessage = null;
        var timer1Settings = Timer1.CreateDraftSettings();
        var timer2Settings = Timer2.CreateDraftSettings();
        var alarmSettings = Alarm.CreateDraftSettings();
        if (Timer1.ValidationMessage is not null
            || Timer2.ValidationMessage is not null
            || Alarm.ValidationMessage is not null)
        {
            ErrorMessage = "입력값을 확인한 뒤 다시 적용하세요.";
            return false;
        }

        var candidate = savedSettings with
        {
            Timer1 = timer1Settings,
            Timer2 = timer2Settings,
            Alarm = alarmSettings,
        };
        var validationErrors = SettingsValidator.Validate(candidate);
        if (validationErrors.Count > 0)
        {
            ErrorMessage = string.Join(
                Environment.NewLine,
                validationErrors.Select(error => error.Message));
            return false;
        }

        var previousBindings = CreateBindings(savedSettings);
        var applyResult = hotkeyService.Apply(CreateBindings(candidate));
        if (!applyResult.Success)
        {
            ErrorMessage = applyResult.ErrorMessage;
            return false;
        }

        try
        {
            await settingsService.SaveAsync(candidate, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            hotkeyService.Apply(previousBindings);
            throw;
        }
        catch (Exception exception)
        {
            var rollback = hotkeyService.Apply(previousBindings);
            ErrorMessage = $"설정을 저장하지 못했습니다: {exception.Message}";
            if (!rollback.Success)
            {
                ErrorMessage += $" 이전 단축키 복원도 실패했습니다: {rollback.ErrorMessage}";
            }

            return false;
        }

        savedSettings = candidate;
        Timer1.ApplySavedSettings(candidate.Timer1);
        Timer2.ApplySavedSettings(candidate.Timer2);
        Alarm.ApplySavedSettings(candidate.Alarm);
        NoticeMessage = "설정을 저장했습니다.";
        return true;
    }

    public IDisposable BeginHotkeyCapture() => hotkeyService.SuspendForCapture();

    private async Task ToggleCompactViewAsync()
    {
        IsCompactView = !IsCompactView;
        var candidate = savedSettings with { UseCompactView = IsCompactView };
        try
        {
            await settingsService.SaveAsync(candidate, CancellationToken.None).ConfigureAwait(true);
            savedSettings = candidate;
            ErrorMessage = null;
        }
        catch (Exception exception)
        {
            ErrorMessage = $"보기 모드를 저장하지 못했습니다: {exception.Message}";
        }
    }

    public void Tick()
    {
        try
        {
            Timer1.Tick();
            Timer2.Tick();
            foreach (var timerIndex in pendingCompletions.ToArray())
            {
                alarmCoordinator.StartTimerAlarm(
                    timerIndex,
                    GetRequestedAudioPath(savedSettings.Alarm),
                    defaultAlarmPath,
                    TimeSpan.FromSeconds((double)savedSettings.Alarm.PlaybackSeconds));
            }

            pendingCompletions.Clear();
        }
        finally
        {
            alarmCoordinator.Tick();
        }
    }

    private async Task<bool> PersistAlarmSettingsAsync(
        AlarmSettings alarmSettings,
        CancellationToken cancellationToken)
    {
        var candidate = savedSettings with { Alarm = alarmSettings };
        var errors = SettingsValidator.Validate(candidate);
        if (errors.Count > 0)
        {
            ErrorMessage = string.Join(Environment.NewLine, errors.Select(error => error.Message));
            return false;
        }

        try
        {
            await settingsService.SaveAsync(candidate, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"설정을 저장하지 못했습니다: {exception.Message}";
            return false;
        }

        savedSettings = candidate;
        ErrorMessage = null;
        return true;
    }

    private string GetRequestedAudioPath(AlarmSettings settings)
    {
        if (settings.UseDefaultSound || string.IsNullOrWhiteSpace(settings.CustomFileName))
        {
            return defaultAlarmPath;
        }

        return Path.GetFullPath(Path.Combine(paths.AudioDirectory, settings.CustomFileName));
    }

    private void OnTimerCompleted(object? sender, EventArgs eventArgs)
    {
        if (sender is TimerViewModel timer)
        {
            pendingCompletions.Add(timer.TimerIndex);
        }
    }

    private void OnTimerOperated(object? sender, int timerIndex)
    {
        pendingCompletions.Remove(timerIndex);
        alarmCoordinator.AcknowledgeTimer(timerIndex);
    }

    private void OnHotkeyPressed(object? sender, int timerIndex)
    {
        if (timerIndex == Timer1.TimerIndex)
        {
            Timer1.HandleHotkey();
        }
        else if (timerIndex == Timer2.TimerIndex)
        {
            Timer2.HandleHotkey();
        }
    }

    private void SetErrorMessage(string? message) => ErrorMessage = message;

    private static HotkeyBinding[] CreateBindings(AppSettings settings) =>
    [
        new(1, settings.Timer1.Hotkey),
        new(2, settings.Timer2.Hotkey),
    ];
}

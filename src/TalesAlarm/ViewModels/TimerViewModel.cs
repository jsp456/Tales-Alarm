using System.Windows.Input;
using TalesAlarm.Configuration;
using TalesAlarm.Hotkeys;
using TalesAlarm.Timers;

namespace TalesAlarm.ViewModels;

public sealed class TimerViewModel : ObservableObject
{
    private readonly CountdownTimer timer;
    private readonly RelayCommand pauseResumeCommand;
    private int hours;
    private int minutes;
    private int seconds;
    private HotkeyGesture hotkey;
    private ReactivationPolicy reactivationPolicy;
    private ReactivationPolicy appliedReactivationPolicy;
    private string? validationMessage;

    public TimerViewModel(int timerIndex, CountdownTimer timer, TimerSettings settings)
    {
        if (timerIndex <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timerIndex));
        }

        TimerIndex = timerIndex;
        this.timer = timer ?? throw new ArgumentNullException(nameof(timer));
        StartCommand = new RelayCommand(Start);
        pauseResumeCommand = new RelayCommand(
            PauseOrResume,
            () => timer.State is TimerState.Running or TimerState.Paused);
        PauseResumeCommand = pauseResumeCommand;
        ResetCommand = new RelayCommand(Reset);
        timer.Completed += OnTimerCompleted;
        ApplySavedSettings(settings);
    }

    public event EventHandler? Completed;

    public int TimerIndex { get; }

    public int Hours
    {
        get => hours;
        set
        {
            if (SetProperty(ref hours, value))
            {
                ValidateDraft();
            }
        }
    }

    public int Minutes
    {
        get => minutes;
        set
        {
            if (SetProperty(ref minutes, value))
            {
                ValidateDraft();
            }
        }
    }

    public int Seconds
    {
        get => seconds;
        set
        {
            if (SetProperty(ref seconds, value))
            {
                ValidateDraft();
            }
        }
    }

    public HotkeyGesture Hotkey
    {
        get => hotkey;
        set
        {
            if (SetProperty(ref hotkey, value))
            {
                ValidateDraft();
            }
        }
    }

    public ReactivationPolicy ReactivationPolicy
    {
        get => reactivationPolicy;
        set => SetProperty(ref reactivationPolicy, value);
    }

    public string DisplayTime
    {
        get
        {
            var totalSeconds = Math.Max(0L, (long)Math.Ceiling(timer.Remaining.TotalSeconds));
            var displayHours = totalSeconds / 3600;
            var displayMinutes = totalSeconds % 3600 / 60;
            var displaySeconds = totalSeconds % 60;
            return $"{displayHours:00}:{displayMinutes:00}:{displaySeconds:00}";
        }
    }

    public string StatusText => timer.State switch
    {
        TimerState.Idle => "대기",
        TimerState.Running => "실행 중",
        TimerState.Paused => "일시정지",
        TimerState.Completed => "완료",
        _ => throw new InvalidOperationException("알 수 없는 타이머 상태입니다."),
    };

    public string? ValidationMessage
    {
        get => validationMessage;
        private set => SetProperty(ref validationMessage, value);
    }

    public ICommand StartCommand { get; }

    public ICommand PauseResumeCommand { get; }

    public ICommand ResetCommand { get; }

    public TimerSettings CreateDraftSettings()
    {
        ValidateDraft();
        var durationSeconds = (long)Hours * 3600 + Minutes * 60L + Seconds;
        return new(durationSeconds, Hotkey, ReactivationPolicy);
    }

    public void ApplySavedSettings(TimerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var totalSeconds = settings.DurationSeconds;
        Hours = checked((int)(totalSeconds / 3600));
        Minutes = checked((int)(totalSeconds % 3600 / 60));
        Seconds = checked((int)(totalSeconds % 60));
        Hotkey = settings.Hotkey;
        ReactivationPolicy = settings.ReactivationPolicy;
        appliedReactivationPolicy = settings.ReactivationPolicy;
        timer.Configure(settings.Duration);
        ValidateDraft();
        RefreshState();
    }

    public void HandleHotkey()
    {
        timer.HandleActivation(appliedReactivationPolicy);
        RefreshState();
    }

    public void Tick()
    {
        timer.Tick();
        RefreshState();
    }

    private void Start()
    {
        timer.Start();
        RefreshState();
    }

    private void PauseOrResume()
    {
        if (timer.State == TimerState.Running)
        {
            timer.Pause();
        }
        else if (timer.State == TimerState.Paused)
        {
            timer.Resume();
        }

        RefreshState();
    }

    private void Reset()
    {
        timer.Reset();
        RefreshState();
    }

    private void OnTimerCompleted(object? sender, EventArgs eventArgs)
    {
        RefreshState();
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(DisplayTime));
        OnPropertyChanged(nameof(StatusText));
        pauseResumeCommand.RaiseCanExecuteChanged();
    }

    private void ValidateDraft()
    {
        ValidationMessage = Hours is < 0 or > 999
            ? "시간은 0에서 999 사이여야 합니다."
            : Minutes is < 0 or > 59
                ? "분은 0에서 59 사이여야 합니다."
                : Seconds is < 0 or > 59
                    ? "초는 0에서 59 사이여야 합니다."
                    : (long)Hours * 3600 + Minutes * 60L + Seconds < 1
                        ? "타이머 시간은 1초 이상이어야 합니다."
                        : !Hotkey.HasNonModifierKey
                            ? "단축키에는 수정 키가 아닌 키가 필요합니다."
                            : null;
    }
}

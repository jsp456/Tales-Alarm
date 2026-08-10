using System.Globalization;
using System.IO;
using System.Windows.Input;
using TalesAlarm.Audio;
using TalesAlarm.Configuration;

namespace TalesAlarm.ViewModels;

public sealed class AlarmSettingsViewModel : ObservableObject
{
    private readonly AppPaths paths;
    private readonly IUserAudioStore userAudioStore;
    private readonly ITimerAlarmCoordinator alarmCoordinator;
    private readonly Func<AlarmSettings, CancellationToken, Task<bool>> persistSettings;
    private readonly Action<string?> reportError;
    private readonly RelayCommand previewCommand;
    private readonly AsyncRelayCommand restoreDefaultCommand;
    private string defaultAlarmPath = string.Empty;
    private bool useDefaultSound;
    private string? customFileName;
    private string playbackSecondsText = "1.5";
    private string? validationMessage;

    public AlarmSettingsViewModel(
        AppPaths paths,
        IUserAudioStore userAudioStore,
        ITimerAlarmCoordinator alarmCoordinator,
        Func<AlarmSettings, CancellationToken, Task<bool>> persistSettings,
        Action<string?> reportError)
    {
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.userAudioStore = userAudioStore ?? throw new ArgumentNullException(nameof(userAudioStore));
        this.alarmCoordinator = alarmCoordinator
            ?? throw new ArgumentNullException(nameof(alarmCoordinator));
        this.persistSettings = persistSettings ?? throw new ArgumentNullException(nameof(persistSettings));
        this.reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
        previewCommand = new RelayCommand(Preview, CanPreview);
        PreviewCommand = previewCommand;
        restoreDefaultCommand = new AsyncRelayCommand(
            RestoreDefaultAsync,
            () => !UseDefaultSound,
            exception => reportError($"기본 음원으로 복원하지 못했습니다: {exception.Message}"));
        RestoreDefaultCommand = restoreDefaultCommand;
    }

    public string CurrentFileName => UseDefaultSound
        ? "기본 알람"
        : customFileName ?? "사용자 음원";

    public bool UseDefaultSound
    {
        get => useDefaultSound;
        private set
        {
            if (SetProperty(ref useDefaultSound, value))
            {
                OnPropertyChanged(nameof(CurrentFileName));
                restoreDefaultCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string PlaybackSecondsText
    {
        get => playbackSecondsText;
        set
        {
            if (SetProperty(ref playbackSecondsText, value))
            {
                ValidatePlayback(out _);
                previewCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? ValidationMessage
    {
        get => validationMessage;
        private set => SetProperty(ref validationMessage, value);
    }

    public ICommand PreviewCommand { get; }

    public ICommand RestoreDefaultCommand { get; }

    public async Task<bool> ImportAudioAsync(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        if (!ValidatePlayback(out var playbackSeconds))
        {
            reportError(ValidationMessage);
            return false;
        }

        reportError(null);
        var previousFileName = UseDefaultSound ? null : customFileName;
        var desiredSettings = new AlarmSettings(false, null, playbackSeconds);
        var result = await userAudioStore.ImportAsync(
            absolutePath,
            previousFileName,
            async candidateName =>
            {
                var candidate = desiredSettings with { CustomFileName = candidateName };
                if (!await persistSettings(candidate, CancellationToken.None).ConfigureAwait(false))
                {
                    throw new IOException("설정을 저장하지 못했습니다.");
                }
            },
            CancellationToken.None).ConfigureAwait(true);

        if (!result.Success || string.IsNullOrWhiteSpace(result.FileName))
        {
            reportError(result.ErrorMessage ?? "음원을 가져오지 못했습니다.");
            return false;
        }

        ApplySavedSettings(desiredSettings with { CustomFileName = result.FileName });
        reportError(null);
        return true;
    }

    public AlarmSettings CreateDraftSettings()
    {
        ValidatePlayback(out var playbackSeconds);
        return new(UseDefaultSound, customFileName, playbackSeconds);
    }

    public void ApplySavedSettings(AlarmSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        UseDefaultSound = settings.UseDefaultSound;
        customFileName = settings.CustomFileName;
        OnPropertyChanged(nameof(CurrentFileName));
        PlaybackSecondsText = settings.PlaybackSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        ValidatePlayback(out _);
        restoreDefaultCommand.RaiseCanExecuteChanged();
        previewCommand.RaiseCanExecuteChanged();
    }

    public void SetDefaultAlarmPath(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        if (!Path.IsPathFullyQualified(absolutePath))
        {
            throw new ArgumentException("기본 음원 경로는 절대 경로여야 합니다.", nameof(absolutePath));
        }

        defaultAlarmPath = Path.GetFullPath(absolutePath);
        previewCommand.RaiseCanExecuteChanged();
    }

    private bool CanPreview() =>
        !string.IsNullOrWhiteSpace(defaultAlarmPath) && ValidationMessage is null;

    private void Preview()
    {
        if (!ValidatePlayback(out var playbackSeconds)
            || string.IsNullOrWhiteSpace(defaultAlarmPath))
        {
            return;
        }

        alarmCoordinator.StartPreview(
            GetRequestedPath(),
            defaultAlarmPath,
            TimeSpan.FromSeconds((double)playbackSeconds));
    }

    private async Task RestoreDefaultAsync()
    {
        if (!ValidatePlayback(out var playbackSeconds))
        {
            reportError(ValidationMessage);
            return;
        }

        var previousFileName = UseDefaultSound ? null : customFileName;
        var desiredSettings = new AlarmSettings(true, null, playbackSeconds);
        try
        {
            await userAudioStore.RestoreDefaultAsync(
                previousFileName,
                async () =>
                {
                    if (!await persistSettings(desiredSettings, CancellationToken.None).ConfigureAwait(false))
                    {
                        throw new IOException("설정을 저장하지 못했습니다.");
                    }
                },
                CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            reportError($"기본 음원 설정을 저장하지 못했습니다: {exception.Message}");
            return;
        }

        ApplySavedSettings(desiredSettings);
        reportError(null);
    }

    private string GetRequestedPath()
    {
        if (UseDefaultSound || string.IsNullOrWhiteSpace(customFileName))
        {
            return defaultAlarmPath;
        }

        return Path.GetFullPath(Path.Combine(paths.AudioDirectory, customFileName));
    }

    private bool ValidatePlayback(out decimal playbackSeconds)
    {
        if (!decimal.TryParse(
                PlaybackSecondsText,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out playbackSeconds))
        {
            ValidationMessage = "재생 시간은 소수 첫째 자리 숫자로 입력하세요.";
            return false;
        }

        if (playbackSeconds is < 0.1m or > 60.0m
            || decimal.Truncate(playbackSeconds * 10) != playbackSeconds * 10)
        {
            ValidationMessage = "재생 시간은 0.1초에서 60.0초 사이의 소수 첫째 자리 값이어야 합니다.";
            return false;
        }

        ValidationMessage = null;
        return true;
    }
}

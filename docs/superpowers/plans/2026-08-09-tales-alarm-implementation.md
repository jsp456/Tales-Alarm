# Tales Alarm Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Windows에서 두 개의 카운트다운을 전역 단축키로 동시에 제어하고, 사용자 음원을 지정된 시간 동안 반복 재생하며, 창을 닫아도 트레이에서 계속 실행되는 독립 실행형 앱을 만든다.

**Architecture:** C#/.NET 10 WPF 앱 하나와 xUnit 테스트 프로젝트 하나로 구성한다. 시간 계산, 설정 저장, 전역 단축키, 오디오, 단일 인스턴스를 UI에서 분리하고 인터페이스 뒤에 두어 단위 테스트가 Win32 창이나 실제 스피커 없이 실행되게 한다. WPF `MainViewModel`이 두 타이머의 완료 이벤트를 한 번으로 모아 오디오 서비스에 전달한다.

**Tech Stack:** C# 14, .NET 10 LTS, WPF, Windows Forms `NotifyIcon`, Win32 `RegisterHotKey`, WPF `MediaPlayer`, `System.Text.Json`, xUnit

## Global Constraints

- Target framework는 `net10.0-windows`, 배포 RID는 `win-x64`다.
- 최종 배포는 `SelfContained=true`, `PublishSingleFile=true`, `PublishTrimmed=false`인 단일 `.exe`다.
- 타이머는 정확히 2개이며 기본값은 `00:20:00/F4`와 `00:30:00/F8`다.
- 타이머 입력 범위는 `00:00:01`~`999:59:59`다.
- 재입력 정책은 타이머별 `Restart`, `PauseResume`, `Ignore`이며 기본값은 `Restart`다.
- 알람 재생 시간은 0.1~60.0초, 소수 첫째 자리, 기본 1.5초다.
- 가져오기 형식은 WAV와 MP3이며 짧은 음원은 제한 시간까지 반복한다.
- 기본 음원은 44.1kHz/16-bit/mono, 1.5초 길이의 직접 생성한 2단 차임 WAV다.
- 설정, 로그, 가져온 음원은 `%LocalAppData%\TalesAlarm` 아래에 저장한다.
- 창 닫기는 트레이 숨김이고 트레이의 `종료`만 프로세스를 끝낸다.
- Windows 로그인 자동 시작, 세 번째 타이머, 계정·네트워크·업데이트 기능은 만들지 않는다.
- 앱 런타임에는 제3자 NuGet 패키지를 추가하지 않는다. 테스트 프로젝트는 xUnit 템플릿 기본 패키지만 사용한다.

## File Structure

```text
TalesAlarm.sln
Directory.Build.props                         # 공통 nullable/경고/언어 버전
.gitignore
tools/Generate-Assets.ps1                    # 기본 WAV와 ICO를 결정적으로 생성
src/TalesAlarm/
  TalesAlarm.csproj                          # WPF/WinForms/게시 설정과 포함 자산
  App.xaml
  App.xaml.cs                                # 조립, 수명주기, 예외 처리
  MainWindow.xaml
  MainWindow.xaml.cs                         # 창 숨김과 캡처 시작/종료 연결만 담당
  Assets/default-alarm.wav
  Assets/tales-alarm.ico
  Audio/AlarmAudioService.cs                 # 제한 시간, 반복, 완료 병합
  Audio/DefaultAlarmInstaller.cs             # 내장 WAV를 앱 데이터로 추출
  Audio/IAudioBackend.cs
  Audio/MediaPlayerAudioBackend.cs           # WPF MediaPlayer 어댑터
  Audio/MediaPlayerAudioProbe.cs             # 가져온 파일 사전 검사
  Audio/UserAudioStore.cs                    # 후보 복사/검사/승인/이전 파일 정리
  Configuration/AppPaths.cs
  Configuration/AppSettings.cs
  Configuration/SettingsService.cs
  Configuration/SettingsValidation.cs
  Hotkeys/GlobalHotkeyService.cs             # 원자적 교체와 롤백
  Hotkeys/HotkeyGesture.cs
  Hotkeys/HotkeyNativeApi.cs                 # RegisterHotKey P/Invoke
  Infrastructure/FileLogger.cs
  Infrastructure/SingleInstanceService.cs
  Infrastructure/TrayService.cs
  Timers/CountdownTimer.cs
  Timers/ReactivationPolicy.cs
  Timers/TimerLimits.cs
  Timers/TimerState.cs
  ViewModels/AlarmSettingsViewModel.cs
  ViewModels/MainViewModel.cs
  ViewModels/ObservableObject.cs
  ViewModels/RelayCommand.cs
  ViewModels/TimerViewModel.cs
  Views/Controls/HotkeyCaptureBox.cs
  Views/Controls/NumericTextBoxBehavior.cs
  Views/Converters/HotkeyGestureConverter.cs
  Views/Converters/TimerDisplayConverter.cs
  Properties/PublishProfiles/win-x64.pubxml
tests/TalesAlarm.Tests/
  TalesAlarm.Tests.csproj
  Audio/AlarmAudioServiceTests.cs
  Audio/DefaultAlarmAssetTests.cs
  Audio/UserAudioStoreTests.cs
  Configuration/AppSettingsValidatorTests.cs
  Configuration/SettingsServiceTests.cs
  Helpers/FakeAudioBackend.cs
  Helpers/FakeHotkeyNativeApi.cs
  Helpers/ManualTimeProvider.cs
  Helpers/ProjectFiles.cs
  Helpers/TemporaryDirectory.cs
  Hotkeys/GlobalHotkeyServiceTests.cs
  Infrastructure/FileLoggerTests.cs
  Infrastructure/SingleInstanceServiceTests.cs
  Timers/CountdownTimerTests.cs
  ViewModels/MainViewModelTests.cs
  ViewModels/TimerViewModelTests.cs
  Verify-PublishArtifact.ps1
README.md
```

---

### Task 1: Solution Scaffold and Monotonic Countdown Domain

**Files:**
- Create: `TalesAlarm.sln`
- Create: `Directory.Build.props`
- Create: `.gitignore`
- Create: `src/TalesAlarm/TalesAlarm.csproj`
- Create: `src/TalesAlarm/Timers/TimerState.cs`
- Create: `src/TalesAlarm/Timers/ReactivationPolicy.cs`
- Create: `src/TalesAlarm/Timers/TimerLimits.cs`
- Create: `src/TalesAlarm/Timers/CountdownTimer.cs`
- Create: `tests/TalesAlarm.Tests/TalesAlarm.Tests.csproj`
- Create: `tests/TalesAlarm.Tests/Helpers/ManualTimeProvider.cs`
- Create: `tests/TalesAlarm.Tests/Timers/CountdownTimerTests.cs`

**Interfaces:**
- Produces: `CountdownTimer(TimeProvider, TimeSpan)`, `Configure(TimeSpan)`, `Start()`, `Pause()`, `Resume()`, `Reset()`, `HandleActivation(ReactivationPolicy)`, `Tick()`, `Completed`
- Produces: `TimerState { Idle, Running, Paused, Completed }`
- Produces: `ReactivationPolicy { Restart, PauseResume, Ignore }`
- Produces: `TimerLimits.MinimumDuration` and `TimerLimits.MaximumDuration`

- [ ] **Step 1: Verify or install the required SDK**

Run:

```powershell
dotnet --list-sdks
```

Expected: at least one `10.0.x` SDK. If absent, request approval for the system change, then run:

```powershell
winget install --id Microsoft.DotNet.SDK.10 --exact --accept-source-agreements --accept-package-agreements
```

Reopen the shell and confirm `dotnet --version` begins with `10.`.

- [ ] **Step 2: Scaffold the solution and set strict build defaults**

Run each command separately:

```powershell
dotnet new sln --format sln -n TalesAlarm
dotnet new wpf -n TalesAlarm -o src/TalesAlarm -f net10.0
dotnet new xunit -n TalesAlarm.Tests -o tests/TalesAlarm.Tests -f net10.0
dotnet sln TalesAlarm.sln add src/TalesAlarm/TalesAlarm.csproj
dotnet sln TalesAlarm.sln add tests/TalesAlarm.Tests/TalesAlarm.Tests.csproj
dotnet add tests/TalesAlarm.Tests/TalesAlarm.Tests.csproj reference src/TalesAlarm/TalesAlarm.csproj
```

Keep the package references generated by the templates and set the app project properties to:

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net10.0-windows</TargetFramework>
  <UseWPF>true</UseWPF>
</PropertyGroup>
```

Set the test project to `<TargetFramework>net10.0-windows</TargetFramework>` and `<UseWPF>true</UseWPF>`, retaining its generated xUnit package references and the `ProjectReference` added by the command. Add `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>14.0</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

Use this repository `.gitignore`:

```gitignore
.vs/
**/bin/
**/obj/
artifacts/
TestResults/
*.user
*.suo
```

- [ ] **Step 3: Write failing countdown tests**

Create a `ManualTimeProvider` whose optional constructor takes an initial `DateTimeOffset` (default Unix epoch), whose `TimestampFrequency` is `TimeSpan.TicksPerSecond`, and whose `GetTimestamp()` returns a mutable tick field. `Advance(TimeSpan)` increments both the timestamp field and UTC value; `GetUtcNow()` returns that UTC value. Add tests covering monotonic correction, one completion event, configuration isolation, and every reactivation policy:

```csharp
[Fact]
public void Tick_UsesActualElapsedTimeAndCompletesOnce()
{
    var time = new ManualTimeProvider();
    var timer = new CountdownTimer(time, TimeSpan.FromSeconds(20));
    var completions = 0;
    timer.Completed += (_, _) => completions++;

    timer.Start();
    time.Advance(TimeSpan.FromSeconds(7.4));
    timer.Tick();
    Assert.Equal(TimeSpan.FromSeconds(12.6), timer.Remaining);

    time.Advance(TimeSpan.FromSeconds(20));
    timer.Tick();
    timer.Tick();
    Assert.Equal(TimerState.Completed, timer.State);
    Assert.Equal(TimeSpan.Zero, timer.Remaining);
    Assert.Equal(1, completions);
}

[Theory]
[InlineData(ReactivationPolicy.Restart, TimerState.Running)]
[InlineData(ReactivationPolicy.PauseResume, TimerState.Paused)]
[InlineData(ReactivationPolicy.Ignore, TimerState.Running)]
public void HandleActivation_AppliesPolicyWhileRunning(
    ReactivationPolicy policy,
    TimerState expected)
{
    var time = new ManualTimeProvider();
    var timer = new CountdownTimer(time, TimeSpan.FromSeconds(20));
    timer.Start();
    time.Advance(TimeSpan.FromSeconds(5));
    timer.Tick();

    timer.HandleActivation(policy);

    Assert.Equal(expected, timer.State);
    Assert.Equal(
        policy == ReactivationPolicy.Restart
            ? TimeSpan.FromSeconds(20)
            : TimeSpan.FromSeconds(15),
        timer.Remaining);
}
```

Also test that `Configure()` during `Running` and `Paused` changes `ConfiguredDuration` but not `Remaining`; `Reset()` then uses the new duration. Test `Idle` and `Completed` activation always starts regardless of policy. Test values outside `00:00:01`~`999:59:59` throw `ArgumentOutOfRangeException`.

- [ ] **Step 4: Run the tests and verify the expected failure**

Run:

```powershell
dotnet test tests/TalesAlarm.Tests/TalesAlarm.Tests.csproj --filter FullyQualifiedName~CountdownTimerTests
```

Expected: compilation failure because `CountdownTimer`, `TimerState`, and `ReactivationPolicy` do not exist.

- [ ] **Step 5: Implement the minimal countdown state machine**

Use these limits and public shape:

```csharp
public static class TimerLimits
{
    public static readonly TimeSpan MinimumDuration = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaximumDuration =
        TimeSpan.FromHours(1000) - TimeSpan.FromSeconds(1);
}

public sealed class CountdownTimer
{
    private readonly TimeProvider _timeProvider;
    private long _startedAt;
    private TimeSpan _remainingAtStart;
    private bool _completionRaised;

    public CountdownTimer(TimeProvider timeProvider, TimeSpan duration);
    public TimeSpan ConfiguredDuration { get; private set; }
    public TimeSpan Remaining { get; private set; }
    public TimerState State { get; private set; } = TimerState.Idle;
    public event EventHandler? Completed;

    public void Configure(TimeSpan duration);
    public void Start();
    public void Pause();
    public void Resume();
    public void Reset();
    public void HandleActivation(ReactivationPolicy policy);
    public void Tick();
}
```

`Start()` sets `Remaining = ConfiguredDuration`, captures `GetTimestamp()`, and clears `_completionRaised`. `Tick()` uses `_timeProvider.GetElapsedTime(_startedAt)`, clamps remaining to zero, transitions to `Completed`, and raises the event only on that transition. `Pause()` calls `Tick()` before freezing. `Resume()` captures a new start timestamp. `Configure()` updates `Remaining` only in `Idle`; `Reset()` always adopts the latest configured duration.

- [ ] **Step 6: Run the focused tests and the whole solution**

Run:

```powershell
dotnet test tests/TalesAlarm.Tests/TalesAlarm.Tests.csproj --filter FullyQualifiedName~CountdownTimerTests
dotnet test TalesAlarm.sln
```

Expected: all tests pass and no warnings are emitted.

- [ ] **Step 7: Commit the timer domain**

```powershell
git add TalesAlarm.sln Directory.Build.props .gitignore src/TalesAlarm tests/TalesAlarm.Tests
git commit -m "feat: add monotonic countdown domain"
```

---

### Task 2: Typed Settings Defaults and Validation

**Files:**
- Create: `src/TalesAlarm/Hotkeys/HotkeyGesture.cs`
- Create: `src/TalesAlarm/Configuration/AppSettings.cs`
- Create: `src/TalesAlarm/Configuration/SettingsValidation.cs`
- Create: `tests/TalesAlarm.Tests/Configuration/AppSettingsValidatorTests.cs`

**Interfaces:**
- Consumes: `ReactivationPolicy`, `TimerLimits`
- Produces: immutable `AppSettings`, `TimerSettings`, `AlarmSettings`
- Produces: `HotkeyGesture(Key Key, HotkeyModifiers Modifiers)` and `HotkeyBinding(int TimerIndex, HotkeyGesture Gesture)`
- Produces: `SettingsValidator.Validate(AppSettings) -> IReadOnlyList<SettingsValidationError>`

- [ ] **Step 1: Write failing settings-default and validation tests**

```csharp
[Fact]
public void CreateDefault_HasApprovedValues()
{
    var settings = AppSettings.CreateDefault();

    Assert.Equal(TimeSpan.FromMinutes(20), settings.Timer1.Duration);
    Assert.Equal(Key.F4, settings.Timer1.Hotkey.Key);
    Assert.Equal(TimeSpan.FromMinutes(30), settings.Timer2.Duration);
    Assert.Equal(Key.F8, settings.Timer2.Hotkey.Key);
    Assert.Equal(ReactivationPolicy.Restart, settings.Timer1.ReactivationPolicy);
    Assert.Equal(ReactivationPolicy.Restart, settings.Timer2.ReactivationPolicy);
    Assert.True(settings.Alarm.UseDefaultSound);
    Assert.Equal(1.5m, settings.Alarm.PlaybackSeconds);
}

[Fact]
public void Validate_RejectsDuplicateHotkeysAndInvalidPlaybackDuration()
{
    var defaults = AppSettings.CreateDefault();
    var invalid = defaults with
    {
        Timer2 = defaults.Timer2 with { Hotkey = defaults.Timer1.Hotkey },
        Alarm = defaults.Alarm with { PlaybackSeconds = 60.1m }
    };

    var errors = SettingsValidator.Validate(invalid);

    Assert.Contains(errors, error => error.Field == "Timer2.Hotkey");
    Assert.Contains(errors, error => error.Field == "Alarm.PlaybackSeconds");
}
```

Add theory rows for 0-second/1000-hour timer values, modifier-only keys (`Key.LeftCtrl`, `Key.System`), `Key.None`, playback values `0`, `0.05`, `60.1`, and valid boundaries `0.1`, `60.0`.

- [ ] **Step 2: Run the focused tests and confirm failure**

```powershell
dotnet test tests/TalesAlarm.Tests/TalesAlarm.Tests.csproj --filter FullyQualifiedName~AppSettingsValidatorTests
```

Expected: compilation failure because the settings records and validator do not exist.

- [ ] **Step 3: Implement exact settings records and defaults**

```csharp
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008
}

public readonly record struct HotkeyGesture(Key Key, HotkeyModifiers Modifiers)
{
    public bool HasNonModifierKey => Key is not Key.None
        and not Key.LeftAlt and not Key.RightAlt
        and not Key.LeftCtrl and not Key.RightCtrl
        and not Key.LeftShift and not Key.RightShift
        and not Key.LWin and not Key.RWin and not Key.System;
}

public readonly record struct HotkeyBinding(int TimerIndex, HotkeyGesture Gesture);

public sealed record TimerSettings(
    long DurationSeconds,
    HotkeyGesture Hotkey,
    ReactivationPolicy ReactivationPolicy)
{
    [JsonIgnore]
    public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);
}

public sealed record AlarmSettings(
    bool UseDefaultSound,
    string? CustomFileName,
    decimal PlaybackSeconds);

public sealed record AppSettings(
    int SchemaVersion,
    TimerSettings Timer1,
    TimerSettings Timer2,
    AlarmSettings Alarm)
{
    public const int CurrentSchemaVersion = 1;
    public static AppSettings CreateDefault() => new(
        CurrentSchemaVersion,
        new(1200, new(Key.F4, HotkeyModifiers.None), ReactivationPolicy.Restart),
        new(1800, new(Key.F8, HotkeyModifiers.None), ReactivationPolicy.Restart),
        new(true, null, 1.5m));
}
```

`SettingsValidationError` is `record(Field, Message)`. `SettingsValidator` must return field-specific Korean messages, require schema version 1, validate both durations through `TimerLimits`, require a non-modifier key, reject duplicate gestures, require one decimal place at most, and require a custom filename whenever `UseDefaultSound` is false.

- [ ] **Step 4: Run settings and regression tests**

```powershell
dotnet test tests/TalesAlarm.Tests/TalesAlarm.Tests.csproj --filter FullyQualifiedName~AppSettingsValidatorTests
dotnet test TalesAlarm.sln
```

Expected: all tests pass.

- [ ] **Step 5: Commit settings types and validation**

```powershell
git add src/TalesAlarm/Configuration src/TalesAlarm/Hotkeys tests/TalesAlarm.Tests/Configuration
git commit -m "feat: define and validate alarm settings"
```

---

### Task 3: Atomic Settings Persistence and Recovery

**Files:**
- Create: `src/TalesAlarm/Configuration/AppPaths.cs`
- Create: `src/TalesAlarm/Configuration/SettingsService.cs`
- Create: `tests/TalesAlarm.Tests/Helpers/TemporaryDirectory.cs`
- Create: `tests/TalesAlarm.Tests/Configuration/SettingsServiceTests.cs`

**Interfaces:**
- Consumes: `AppSettings`, `SettingsValidator`
- Produces: `AppPaths.ForCurrentUser()` and injectable `AppPaths(string RootDirectory)`
- Produces: `ISettingsService.LoadAsync(CancellationToken) -> SettingsLoadResult`
- Produces: `ISettingsService.SaveAsync(AppSettings, CancellationToken)`
- Produces: `SettingsService : ISettingsService`
- Produces: `SettingsLoadResult(AppSettings Settings, string? RecoveryMessage, string? BackupPath)`

- [ ] **Step 1: Write failing round-trip, atomicity, and recovery tests**

```csharp
[Fact]
public async Task SaveThenLoad_RoundTripsEnumsAsReadableStrings()
{
    using var temp = new TemporaryDirectory();
    var paths = new AppPaths(temp.Path);
    var service = new SettingsService(paths, TimeProvider.System);
    var expected = AppSettings.CreateDefault() with
    {
        Timer1 = AppSettings.CreateDefault().Timer1 with
        {
            DurationSeconds = 75,
            ReactivationPolicy = ReactivationPolicy.PauseResume
        }
    };

    await service.SaveAsync(expected, CancellationToken.None);
    var result = await service.LoadAsync(CancellationToken.None);

    Assert.Equal(expected, result.Settings);
    Assert.Null(result.RecoveryMessage);
    Assert.Contains("PauseResume", await File.ReadAllTextAsync(paths.SettingsFile));
    Assert.False(File.Exists(paths.SettingsTemporaryFile));
}

[Fact]
public async Task Load_CorruptJsonBacksUpFileAndReturnsDefaults()
{
    using var temp = new TemporaryDirectory();
    var paths = new AppPaths(temp.Path);
    Directory.CreateDirectory(paths.RootDirectory);
    await File.WriteAllTextAsync(paths.SettingsFile, "{ broken");
    var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero));
    var service = new SettingsService(paths, time);

    var result = await service.LoadAsync(CancellationToken.None);

    Assert.Equal(AppSettings.CreateDefault(), result.Settings);
    Assert.NotNull(result.RecoveryMessage);
    Assert.True(File.Exists(result.BackupPath));
    Assert.False(File.Exists(paths.SettingsFile));
}
```

Also test a valid JSON document with invalid duration, a pre-cancelled save, and first launch with no file. The cancellation test must confirm the previous `settings.json` still contains its original content.

- [ ] **Step 2: Run focused tests and confirm failure**

```powershell
dotnet test tests/TalesAlarm.Tests/TalesAlarm.Tests.csproj --filter FullyQualifiedName~SettingsServiceTests
```

Expected: compilation failure for `AppPaths` and `SettingsService`.

- [ ] **Step 3: Implement deterministic paths and JSON options**

Define the persistence contract and recovery result exactly once in `SettingsService.cs`:

```csharp
public interface ISettingsService
{
    Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}

public sealed record SettingsLoadResult(
    AppSettings Settings,
    string? RecoveryMessage,
    string? BackupPath);

public sealed class SettingsValidationException(
    IReadOnlyList<SettingsValidationError> errors) : Exception("설정값이 올바르지 않습니다.")
{
    public IReadOnlyList<SettingsValidationError> Errors { get; } = errors;
}
```

`AppPaths` exposes exact paths:

```csharp
public sealed record AppPaths(string RootDirectory)
{
    public string SettingsFile => Path.Combine(RootDirectory, "settings.json");
    public string SettingsTemporaryFile => Path.Combine(RootDirectory, "settings.tmp");
    public string AudioDirectory => Path.Combine(RootDirectory, "Audio");
    public string LogsDirectory => Path.Combine(RootDirectory, "Logs");

    public static AppPaths ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TalesAlarm"));

    public static AppPaths FromArguments(
        IReadOnlyList<string> args,
        bool allowDataRootOverride)
    {
        if (allowDataRootOverride)
        {
            for (var index = 0; index < args.Count - 1; index++)
            {
                if (args[index] == "--data-root" && Path.IsPathFullyQualified(args[index + 1]))
                    return new(Path.GetFullPath(args[index + 1]));
            }
        }
        return ForCurrentUser();
    }
}
```

The caller passes `allowDataRootOverride: true` only in Debug builds; Release passes false, so production always uses the current user's local app-data directory. Add tests that true accepts an absolute temporary path, true rejects a relative path, and false ignores the switch. Use `JsonSerializerOptions { WriteIndented = true }` with `JsonStringEnumConverter`. Always create `RootDirectory` before reads/writes.

- [ ] **Step 4: Implement atomic save and corrupt-file recovery**

`SaveAsync` validates first and throws `SettingsValidationException` carrying all errors. Call `cancellationToken.ThrowIfCancellationRequested()` before opening the temp file and again after flushing it. Serialize to `settings.tmp`, flush and close it, then use `File.Replace(temp, settings, null, true)` when `settings.json` exists or `File.Move(temp, settings)` on first save. In `finally`, delete a leftover temp file. This keeps replacement on the same volume and preserves the previous complete file if writing fails before replacement.

`LoadAsync` returns defaults when no file exists. On `JsonException`, unsupported schema, or validation errors, move the bad file to `settings.corrupt-<yyyyMMddHHmmssfff>.json`, then return defaults plus a Korean recovery message and backup path. Do not overwrite the backup.

- [ ] **Step 5: Run persistence and full tests**

```powershell
dotnet test tests/TalesAlarm.Tests/TalesAlarm.Tests.csproj --filter FullyQualifiedName~SettingsServiceTests
dotnet test TalesAlarm.sln
```

Expected: all tests pass; the test run leaves no repository files under `%LocalAppData%` because tests inject temporary paths.

- [ ] **Step 6: Commit persistence**

```powershell
git add src/TalesAlarm/Configuration tests/TalesAlarm.Tests/Configuration tests/TalesAlarm.Tests/Helpers
git commit -m "feat: persist settings atomically"
```

---

### Task 4: Transactional Global Hotkey Registration

**Files:**
- Create: `src/TalesAlarm/Hotkeys/HotkeyNativeApi.cs`
- Create: `src/TalesAlarm/Hotkeys/GlobalHotkeyService.cs`
- Create: `tests/TalesAlarm.Tests/Helpers/FakeHotkeyNativeApi.cs`
- Create: `tests/TalesAlarm.Tests/Hotkeys/GlobalHotkeyServiceTests.cs`

**Interfaces:**
- Consumes: `HotkeyGesture`, `HotkeyBinding`
- Produces: `IGlobalHotkeyService.Attach(nint)`, `Apply(IReadOnlyList<HotkeyBinding>)`, `SuspendForCapture()`, `ProcessWindowMessage(int, nint)`, `HotkeyPressed`
- Produces: `HotkeyApplyResult(bool Success, string? ErrorMessage)`
- Produces: `IHotkeyNativeApi.TryRegister(nint, int, HotkeyGesture, out int)` and `Unregister(nint, int)`

- [ ] **Step 1: Write failing registration, rollback, message, and suspension tests**

```csharp
[Fact]
public void Apply_WhenSecondCandidateFails_RestoresBothPreviousBindings()
{
    var native = new FakeHotkeyNativeApi();
    var service = new GlobalHotkeyService(native);
    service.Attach((nint)42);
    var previous = new[]
    {
        new HotkeyBinding(1, new(Key.F4, HotkeyModifiers.None)),
        new HotkeyBinding(2, new(Key.F8, HotkeyModifiers.None))
    };
    Assert.True(service.Apply(previous).Success);
    native.FailGesture = new(Key.F10, HotkeyModifiers.Control);

    var result = service.Apply(new[]
    {
        new HotkeyBinding(1, new(Key.F9, HotkeyModifiers.Control)),
        new HotkeyBinding(2, new(Key.F10, HotkeyModifiers.Control))
    });

    Assert.False(result.Success);
    Assert.Equal(previous, service.ActiveBindings);
    Assert.Equal(previous.Select(x => x.Gesture), native.RegisteredGestures);
}

[Fact]
public void ProcessWindowMessage_RaisesTimerIndexForKnownId()
{
    var service = new GlobalHotkeyService(new FakeHotkeyNativeApi());
    service.Attach((nint)42);
    service.Apply(new[] { new HotkeyBinding(2, new(Key.F8, HotkeyModifiers.None)) });
    var pressed = 0;
    service.HotkeyPressed += (_, timerIndex) => pressed = timerIndex;

    var handled = service.ProcessWindowMessage(GlobalHotkeyService.WmHotkey, (nint)2);

    Assert.True(handled);
    Assert.Equal(2, pressed);
}
```

Test that `SuspendForCapture()` unregisters all current bindings, nested suspensions remain suspended until the last lease is disposed, and disposing the final lease restores the active bindings.

- [ ] **Step 2: Run focused tests and confirm failure**

```powershell
dotnet test tests/TalesAlarm.Tests/TalesAlarm.Tests.csproj --filter FullyQualifiedName~GlobalHotkeyServiceTests
```

Expected: compilation failure for the native API and service.

- [ ] **Step 3: Implement the Win32 adapter**

Define the service contract used by viewmodels and the test fake:

```csharp
public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler<int>? HotkeyPressed;
    IReadOnlyList<HotkeyBinding> ActiveBindings { get; }
    void Attach(nint windowHandle);
    HotkeyApplyResult Apply(IReadOnlyList<HotkeyBinding> bindings);
    IDisposable SuspendForCapture();
    bool ProcessWindowMessage(int message, nint wParam);
}

public sealed record HotkeyApplyResult(bool Success, string? ErrorMessage);

public interface IHotkeyNativeApi
{
    bool TryRegister(
        nint windowHandle,
        int id,
        HotkeyGesture gesture,
        out int errorCode);
    bool Unregister(nint windowHandle, int id);
}
```

Use `LibraryImport` with last-error capture:

```csharp
internal static partial class HotkeyNativeMethods
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint hWnd, int id);
}
```

`Win32HotkeyNativeApi.TryRegister` converts `HotkeyGesture.Key` through `KeyInterop.VirtualKeyFromKey`, ORs `0x4000` (`MOD_NOREPEAT`) into its modifier flags so holding F4/F8 does not emit repeat starts, calls the native method, and places `Marshal.GetLastWin32Error()` in the `out` argument on failure. The fake adapter can therefore select a failing `HotkeyGesture` without duplicating key conversion logic.

- [ ] **Step 4: Implement transactional apply and capture suspension**

`GlobalHotkeyService.Apply` must:

1. Reject unattached use and duplicate gestures.
2. Snapshot `ActiveBindings`.
3. Unregister the active IDs.
4. Register candidates in timer-index order.
5. If any registration fails, unregister every candidate registered so far and re-register the snapshot.
6. Replace `ActiveBindings` only after all candidates succeed.

If rollback registration itself fails, include both the candidate error and rollback error in the Korean message. `ProcessWindowMessage` handles only `WM_HOTKEY` and known IDs. `Dispose()` unregisters all IDs once.

- [ ] **Step 5: Run hotkey and full tests**

```powershell
dotnet test tests/TalesAlarm.Tests/TalesAlarm.Tests.csproj --filter FullyQualifiedName~GlobalHotkeyServiceTests
dotnet test TalesAlarm.sln
```

Expected: all tests pass without registering real global keys because the tests use `FakeHotkeyNativeApi`.

- [ ] **Step 6: Commit the global hotkey layer**

```powershell
git add src/TalesAlarm/Hotkeys tests/TalesAlarm.Tests/Hotkeys tests/TalesAlarm.Tests/Helpers/FakeHotkeyNativeApi.cs
git commit -m "feat: register global hotkeys transactionally"
```

---

### Task 5: Generated Alarm Assets and Bounded Looping Playback

**Files:**
- Create: `tools/Generate-Assets.ps1`
- Create: `src/TalesAlarm/Assets/default-alarm.wav`
- Create: `src/TalesAlarm/Assets/tales-alarm.ico`
- Modify: `src/TalesAlarm/TalesAlarm.csproj`
- Create: `src/TalesAlarm/Audio/IAudioBackend.cs`
- Create: `src/TalesAlarm/Audio/DefaultAlarmInstaller.cs`
- Create: `src/TalesAlarm/Audio/AlarmAudioService.cs`
- Create: `tests/TalesAlarm.Tests/Helpers/FakeAudioBackend.cs`
- Create: `tests/TalesAlarm.Tests/Helpers/ProjectFiles.cs`
- Create: `tests/TalesAlarm.Tests/Audio/DefaultAlarmAssetTests.cs`
- Create: `tests/TalesAlarm.Tests/Audio/AlarmAudioServiceTests.cs`

**Interfaces:**
- Consumes: `AppPaths`, `TimeProvider`
- Produces: `IDefaultAlarmInstaller.EnsureInstalledAsync(CancellationToken) -> string`
- Produces: `DefaultAlarmInstaller : IDefaultAlarmInstaller`
- Produces: `IAudioBackend.Open(string)`, `Play()`, `Stop()`, `Rewind()`, `MediaEnded`, `MediaFailed`
- Produces: `IAlarmAudioService.StartOrExtend(string requestedPath, string fallbackPath, TimeSpan duration)`, `Tick()`, `Stop()`
- Produces: `AlarmAudioService : IAlarmAudioService`

- [ ] **Step 1: Write failing asset and playback tests**

Create `ProjectFiles.FindRepositoryRoot()` by walking parent directories from `AppContext.BaseDirectory` until `TalesAlarm.sln` is found, throwing `DirectoryNotFoundException` if it reaches the drive root. Expose `DefaultAlarmWav` and `AppIcon` paths from that root. The asset test reads the committed WAV header and checks `RIFF`, `WAVE`, PCM format 1, one channel, 44,100Hz, 16 bits, and exactly 66,150 samples (1.5 seconds). It also checks that the ICO begins with reserved/type/count bytes `00 00 01 00 01 00`.

```csharp
[Fact]
public void MediaEnded_BeforeDeadline_RewindsAndContinues()
{
    var time = new ManualTimeProvider();
    var backend = new FakeAudioBackend();
    var service = new AlarmAudioService(time, backend);

    service.StartOrExtend("custom.wav", "default.wav", TimeSpan.FromSeconds(1.5));
    backend.RaiseMediaEnded();

    Assert.Equal(1, backend.RewindCalls);
    Assert.Equal(2, backend.PlayCalls);
    time.Advance(TimeSpan.FromSeconds(1.5));
    service.Tick();
    Assert.Equal(1, backend.StopCalls);
}

[Fact]
public void SecondCompletion_ExtendsWithoutOpeningAnotherPlayer()
{
    var time = new ManualTimeProvider();
    var backend = new FakeAudioBackend();
    var service = new AlarmAudioService(time, backend);
    service.StartOrExtend("default.wav", "default.wav", TimeSpan.FromSeconds(1));
    time.Advance(TimeSpan.FromSeconds(0.8));

    service.StartOrExtend("default.wav", "default.wav", TimeSpan.FromSeconds(1));
    time.Advance(TimeSpan.FromSeconds(0.8));
    service.Tick();

    Assert.Equal(1, backend.OpenCalls);
    Assert.Equal(0, backend.StopCalls);
}
```

Also test fallback after `MediaFailed`, failure of the fallback stops safely, and `Stop()` prevents a later `MediaEnded` event from restarting playback.

- [ ] **Step 2: Run tests to establish failures**

```powershell
dotnet test tests/TalesAlarm.Tests/TalesAlarm.Tests.csproj --filter "FullyQualifiedName~DefaultAlarmAssetTests|FullyQualifiedName~AlarmAudioServiceTests"
```

Expected: missing asset and missing audio types failures.

- [ ] **Step 3: Add the deterministic WAV and ICO generator**

`Generate-Assets.ps1` must generate output relative to its own location. For the WAV, write 66,150 signed 16-bit mono samples at 44,100Hz. Mix two decaying chimes:

```powershell
$sampleRate = 44100
$durationSeconds = 1.5
$sampleCount = [int]($sampleRate * $durationSeconds)

function Get-Tone([double]$time, [double]$start, [double]$length,
                  [double]$fundamental, [double]$upper) {
    $local = $time - $start
    if ($local -lt 0 -or $local -ge $length) { return 0.0 }
    $attack = [Math]::Min(1.0, $local / 0.012)
    $decay = [Math]::Exp(-4.2 * $local / $length)
    return $attack * $decay * (
        0.62 * [Math]::Sin(2 * [Math]::PI * $fundamental * $local) +
        0.25 * [Math]::Sin(2 * [Math]::PI * $upper * $local))
}

$samples = [short[]]::new($sampleCount)
for ($index = 0; $index -lt $sampleCount; $index++) {
    $time = $index / [double]$sampleRate
    $mixed = 0.72 * (
        (Get-Tone $time 0.03 0.62 659.25 987.77) +
        (Get-Tone $time 0.72 0.73 783.99 1174.66))
    $clamped = [Math]::Max(-1.0, [Math]::Min(1.0, $mixed))
    $samples[$index] = [short][Math]::Round($clamped * 32767)
}
```

Write the PCM RIFF fields in this exact order through `BinaryWriter`: ASCII `RIFF`, `36 + sampleCount * 2`, ASCII `WAVE`, ASCII `fmt `, `16`, format `1`, channels `1`, sample rate `44100`, byte rate `88200`, block align `2`, bits `16`, ASCII `data`, data length `sampleCount * 2`, then every sample. For the ICO, draw a 64x64 blue circular clock with white rim and hands using `System.Drawing`, encode it as PNG, then write ICO reserved `0`, type `1`, count `1`, width `64`, height `64`, color count `0`, reserved `0`, planes `1`, bit count `32`, PNG byte count, data offset `22`, and the PNG bytes. The script overwrites only the two exact asset paths.

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Generate-Assets.ps1
```

- [ ] **Step 4: Embed and install the default audio**

Add to `TalesAlarm.csproj`:

```xml
<PropertyGroup>
  <UseWindowsForms>true</UseWindowsForms>
  <ApplicationIcon>Assets\tales-alarm.ico</ApplicationIcon>
</PropertyGroup>
<ItemGroup>
  <EmbeddedResource Include="Assets\default-alarm.wav"
                    LogicalName="TalesAlarm.Assets.default-alarm.wav" />
</ItemGroup>
```

Define `IDefaultAlarmInstaller` as `Task<string> EnsureInstalledAsync(CancellationToken cancellationToken)`. `DefaultAlarmInstaller` reads that manifest resource and atomically replaces `%LocalAppData%\TalesAlarm\Audio\default-alarm.wav` only if the file is missing or its SHA-256 differs from the embedded resource. It returns the extracted absolute path.

- [ ] **Step 5: Implement the backend contract and playback coordinator**

```csharp
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

public interface IAlarmAudioService
{
    bool IsPlaying { get; }
    string? LastError { get; }
    void StartOrExtend(string requestedPath, string fallbackPath, TimeSpan duration);
    void Tick();
    void Stop();
}

public sealed class AlarmAudioService : IAlarmAudioService, IDisposable
{
    public AlarmAudioService(TimeProvider timeProvider, IAudioBackend backend);
    public bool IsPlaying { get; }
    public string? LastError { get; }
    public void StartOrExtend(
        string requestedPath,
        string fallbackPath,
        TimeSpan duration);
    public void Tick();
    public void Stop();
}
```

The first `StartOrExtend` opens and plays one backend and captures the current timestamp. A call while playing only resets the timestamp and duration; it does not reopen or overlap. `MediaEnded` rewinds and plays only while before the deadline. `MediaFailed` tries the fallback once; fallback failure sets `LastError` and stops. `Tick()` uses `TimeProvider.GetElapsedTime` and stops at or after the configured duration.

- [ ] **Step 6: Run audio, asset, and regression tests**

```powershell
dotnet test tests/TalesAlarm.Tests/TalesAlarm.Tests.csproj --filter "FullyQualifiedName~DefaultAlarmAssetTests|FullyQualifiedName~AlarmAudioServiceTests"
dotnet test TalesAlarm.sln
```

Expected: generated-asset metadata and all playback state tests pass.

- [ ] **Step 7: Commit generated assets and audio core**

```powershell
git add tools src/TalesAlarm/Assets src/TalesAlarm/Audio src/TalesAlarm/TalesAlarm.csproj tests/TalesAlarm.Tests/Audio tests/TalesAlarm.Tests/Helpers/FakeAudioBackend.cs
git commit -m "feat: add generated alarm and bounded playback"
```

---

### Task 6: Custom Audio Import and WPF Media Backend

**Files:**
- Create: `src/TalesAlarm/Audio/MediaPlayerAudioBackend.cs`
- Create: `src/TalesAlarm/Audio/MediaPlayerAudioProbe.cs`
- Create: `src/TalesAlarm/Audio/UserAudioStore.cs`
- Create: `tests/TalesAlarm.Tests/Audio/UserAudioStoreTests.cs`

**Interfaces:**
- Consumes: `AppPaths`, `IAudioBackend`
- Produces: `IAudioProbe.ProbeAsync(string, CancellationToken) -> AudioProbeResult`
- Produces: `IUserAudioStore.ImportAsync(string, string?, Func<string, Task>, CancellationToken) -> AudioImportResult`
- Produces: `IUserAudioStore.RestoreDefaultAsync(string?, Func<Task>, CancellationToken)`
- Produces: `UserAudioStore : IUserAudioStore`
- Produces: `MediaPlayerAudioBackend : IAudioBackend`

- [ ] **Step 1: Write failing transactional import tests**

```csharp
[Fact]
public async Task ImportAsync_CopiesBeforePersistAndDeletesOldOnlyAfterSuccess()
{
    using var temp = new TemporaryDirectory();
    var paths = new AppPaths(temp.Path);
    Directory.CreateDirectory(paths.AudioDirectory);
    var oldName = "custom-old.wav";
    await File.WriteAllBytesAsync(Path.Combine(paths.AudioDirectory, oldName), [1, 2]);
    var source = Path.Combine(temp.Path, "picked.wav");
    await File.WriteAllBytesAsync(source, [3, 4, 5]);
    var probe = new FakeAudioProbe(success: true);
    var store = new UserAudioStore(paths, probe);
    string? persistedName = null;

    var result = await store.ImportAsync(
        source,
        oldName,
        name => { persistedName = name; return Task.CompletedTask; },
        CancellationToken.None);

    Assert.True(result.Success);
    Assert.Equal(result.FileName, persistedName);
    Assert.True(File.Exists(Path.Combine(paths.AudioDirectory, result.FileName!)));
    Assert.False(File.Exists(Path.Combine(paths.AudioDirectory, oldName)));
}
```

Add tests for uppercase `.MP3`, unsupported extension, failed probe, failed persist callback, cancelled copy, and restore-default persist failure. Every failure test must assert that the old file still exists and candidate files are removed.

- [ ] **Step 2: Run focused tests and confirm failure**

```powershell
dotnet test tests/TalesAlarm.Tests/TalesAlarm.Tests.csproj --filter FullyQualifiedName~UserAudioStoreTests
```

Expected: missing `UserAudioStore` and `IAudioProbe` types.

- [ ] **Step 3: Implement transactional storage**

Define the exact asynchronous contracts and results:

```csharp
public sealed record AudioProbeResult(bool Success, string? ErrorMessage);
public sealed record AudioImportResult(bool Success, string? FileName, string? ErrorMessage);

public interface IAudioProbe
{
    Task<AudioProbeResult> ProbeAsync(string absolutePath, CancellationToken cancellationToken);
}

public interface IUserAudioStore
{
    Task<AudioImportResult> ImportAsync(
        string sourcePath,
        string? previousFileName,
        Func<string, Task> persistFileName,
        CancellationToken cancellationToken);

    Task RestoreDefaultAsync(
        string? previousFileName,
        Func<Task> persistDefault,
        CancellationToken cancellationToken);
}
```

Accept extensions through a case-insensitive set `{ ".wav", ".mp3" }`. Copy the source to `Audio/import-<Guid:N><ext>.tmp`, then rename it to `custom-<Guid:N><ext>`. Probe the final candidate before calling `persistFileName(candidateName)`. If probing or persistence fails, delete both temp and candidate and preserve the previous file. Only after persistence succeeds may the previous managed `custom-*` file be deleted. Never delete a path outside `AppPaths.AudioDirectory`.

`RestoreDefaultAsync` calls its persistence callback first and deletes the previous managed file only after success.

- [ ] **Step 4: Implement MediaPlayer probing and playback**

`MediaPlayerAudioBackend` wraps one `System.Windows.Media.MediaPlayer` on the WPF dispatcher. `Open` accepts only absolute paths and sets a file `Uri`; `Rewind` sets `Position = TimeSpan.Zero`; `MediaEnded` and `MediaFailed` are forwarded. Every public method verifies dispatcher access and uses `Dispatcher.Invoke` when called off-thread.

`MediaPlayerAudioProbe.ProbeAsync` creates a temporary `MediaPlayer`, subscribes to `MediaOpened` and `MediaFailed`, calls `Open`, and races the result against a three-second timeout and cancellation token. It always closes the player and detaches handlers. Return a Korean error message rather than throwing for unsupported or unreadable media.

- [ ] **Step 5: Run storage and full tests**

```powershell
dotnet test tests/TalesAlarm.Tests/TalesAlarm.Tests.csproj --filter FullyQualifiedName~UserAudioStoreTests
dotnet test TalesAlarm.sln
```

Expected: all tests pass. No test opens a real media player; `UserAudioStoreTests` injects `FakeAudioProbe`.

- [ ] **Step 6: Commit custom audio support**

```powershell
git add src/TalesAlarm/Audio tests/TalesAlarm.Tests/Audio
git commit -m "feat: import and validate custom alarm audio"
```

---

### Task 7: Timer and Application ViewModels

**Files:**
- Create: `src/TalesAlarm/ViewModels/ObservableObject.cs`
- Create: `src/TalesAlarm/ViewModels/RelayCommand.cs`
- Create: `src/TalesAlarm/ViewModels/TimerViewModel.cs`
- Create: `src/TalesAlarm/ViewModels/AlarmSettingsViewModel.cs`
- Create: `src/TalesAlarm/ViewModels/MainViewModel.cs`
- Create: `tests/TalesAlarm.Tests/ViewModels/TimerViewModelTests.cs`
- Create: `tests/TalesAlarm.Tests/ViewModels/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `CountdownTimer`, settings records, `ISettingsService`, `IGlobalHotkeyService`, `IAlarmAudioService`, `IUserAudioStore`, `IDefaultAlarmInstaller`
- Produces: `TimerViewModel` bindable duration fields, hotkey, policy, status, display, and commands
- Produces: `AlarmSettingsViewModel` bindable filename/playback duration and import/preview/restore commands
- Produces: `MainViewModel.InitializeAsync() -> Task`, `ApplySettingsAsync() -> Task<bool>`, `Tick()`, `BeginHotkeyCapture() -> IDisposable`

- [ ] **Step 1: Write failing TimerViewModel behavior tests**

```csharp
[Fact]
public void Tick_FormatsCeilingSecondsAndRaisesOneCompletionRequest()
{
    var time = new ManualTimeProvider();
    var model = new CountdownTimer(time, TimeSpan.FromSeconds(2));
    var viewModel = new TimerViewModel(1, model, AppSettings.CreateDefault().Timer1);
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
```

Test start-while-running explicitly restarts regardless of reactivation policy, pause command enablement by state, reset adopting new saved duration, and hotkey policy delegation.

- [ ] **Step 2: Write failing MainViewModel coordination tests**

Use fakes for settings, hotkeys, audio, and audio store. Test:

```csharp
[Fact]
public async Task ApplySettings_WhenSaveFails_RestoresPreviousHotkeysAndSettings()
{
    var fixture = MainViewModelFixture.Create();
    await fixture.ViewModel.InitializeAsync();
    fixture.ViewModel.Timer1.Hotkey = new(Key.F9, HotkeyModifiers.Control);
    fixture.Settings.FailNextSave = true;

    var applied = await fixture.ViewModel.ApplySettingsAsync();

    Assert.False(applied);
    Assert.Equal(Key.F4, fixture.Hotkeys.ActiveBindings.Single(x => x.TimerIndex == 1).Gesture.Key);
    Assert.Contains("저장", fixture.ViewModel.ErrorMessage);
}
```

Also test that both timers completing in one `Tick()` cause exactly one `StartOrExtend`, a later completion while audio plays extends it, hotkey ID 1/2 targets only its timer, invalid fields prevent hotkey registration, initialization displays a settings recovery notice, preview uses the current sound and duration, successful import persists the returned filename, failed import preserves the previous selection, and restore-default clears the managed filename only after persistence succeeds.

- [ ] **Step 3: Run viewmodel tests and confirm failure**

```powershell
dotnet test tests/TalesAlarm.Tests/TalesAlarm.Tests.csproj --filter FullyQualifiedName~ViewModels
```

Expected: missing viewmodel and command types.

- [ ] **Step 4: Implement observable and command primitives**

`ObservableObject.SetProperty<T>` compares with `EqualityComparer<T>.Default`, assigns, and raises `PropertyChanged`. `RelayCommand` and `AsyncRelayCommand` accept execute/can-execute delegates; async commands prevent a second concurrent execution and surface exceptions through a supplied error callback.

- [ ] **Step 5: Implement TimerViewModel**

Expose these bindable members with exact names:

```csharp
public int Hours { get; set; }
public int Minutes { get; set; }
public int Seconds { get; set; }
public HotkeyGesture Hotkey { get; set; }
public ReactivationPolicy ReactivationPolicy { get; set; }
public string DisplayTime { get; }
public string StatusText { get; }
public string? ValidationMessage { get; }
public ICommand StartCommand { get; }
public ICommand PauseResumeCommand { get; }
public ICommand ResetCommand { get; }
public event EventHandler? Completed;
public TimerSettings CreateDraftSettings();
public void ApplySavedSettings(TimerSettings settings);
public void HandleHotkey();
public void Tick();
```

Display uses `Math.Ceiling(Remaining.TotalSeconds)` and supports three-digit hours. Korean state labels are `대기`, `실행 중`, `일시정지`, `완료`.

`AlarmSettingsViewModel` exposes the exact members below. `PlaybackSecondsText` is parsed with `InvariantCulture`; invalid text stays visible and sets `ValidationMessage` instead of silently becoming zero.

```csharp
public string CurrentFileName { get; }
public bool UseDefaultSound { get; }
public string PlaybackSecondsText { get; set; }
public string? ValidationMessage { get; }
public ICommand PreviewCommand { get; }
public ICommand RestoreDefaultCommand { get; }
public Task<bool> ImportAudioAsync(string absolutePath);
public AlarmSettings CreateDraftSettings();
public void ApplySavedSettings(AlarmSettings settings);
```

- [ ] **Step 6: Implement MainViewModel's transactional flow**

Expose the composition surface used by XAML and `App`:

```csharp
public TimerViewModel Timer1 { get; }
public TimerViewModel Timer2 { get; }
public AlarmSettingsViewModel Alarm { get; }
public string? ErrorMessage { get; private set; }
public string? NoticeMessage { get; private set; }
public ICommand ApplySettingsCommand { get; }
public Task InitializeAsync(CancellationToken cancellationToken = default);
public Task<bool> ApplySettingsAsync(CancellationToken cancellationToken = default);
public IDisposable BeginHotkeyCapture();
public void Tick();
```

`InitializeAsync` loads settings, configures both timers, extracts the default WAV, registers both hotkeys, and subscribes to `HotkeyPressed`. `ApplySettingsAsync` builds one immutable candidate, validates it, applies candidate hotkeys, saves candidate settings, then updates memory. On save failure it calls `Apply(previousBindings)` and retains previous saved settings. Duration draft changes do not alter a running countdown until successful apply followed by its next start/reset.

During `Tick()`, set a local `completedAny` flag while ticking both timers, then call `IAlarmAudioService.StartOrExtend` once after both ticks. Always call `IAlarmAudioService.Tick()` afterward.

- [ ] **Step 7: Run viewmodel and regression tests**

```powershell
dotnet test tests/TalesAlarm.Tests/TalesAlarm.Tests.csproj --filter FullyQualifiedName~ViewModels
dotnet test TalesAlarm.sln
```

Expected: all tests pass.

- [ ] **Step 8: Commit the viewmodels**

```powershell
git add src/TalesAlarm/ViewModels tests/TalesAlarm.Tests/ViewModels
git commit -m "feat: coordinate timers and settings in viewmodels"
```

---

### Task 8: WPF Main Window and Hotkey Capture Controls

**Files:**
- Modify: `src/TalesAlarm/App.xaml`
- Modify: `src/TalesAlarm/MainWindow.xaml`
- Modify: `src/TalesAlarm/MainWindow.xaml.cs`
- Create: `src/TalesAlarm/Views/Controls/HotkeyCaptureBox.cs`
- Create: `src/TalesAlarm/Views/Controls/NumericTextBoxBehavior.cs`
- Create: `src/TalesAlarm/Views/Converters/HotkeyGestureConverter.cs`
- Create: `src/TalesAlarm/Views/Converters/TimerDisplayConverter.cs`

**Interfaces:**
- Consumes: all bindable properties and commands from `MainViewModel`, `TimerViewModel`, `AlarmSettingsViewModel`
- Produces: `HotkeyCaptureBox.Gesture` dependency property and `CaptureStarted`/`CaptureEnded` routed events
- Produces: `MainWindow.RequestHide` event, `AllowClose` property, and `ShowAndActivate()`; the window does not directly terminate the app

- [ ] **Step 1: Add converter and capture-logic tests before XAML**

Add tests to `tests/TalesAlarm.Tests/ViewModels/TimerViewModelTests.cs` that assert:

```csharp
Assert.Equal("F4", HotkeyGestureConverter.Format(new(Key.F4, HotkeyModifiers.None)));
Assert.Equal("Ctrl + Alt + 1", HotkeyGestureConverter.Format(
    new(Key.D1, HotkeyModifiers.Control | HotkeyModifiers.Alt)));
Assert.Equal("999:59:59", TimerDisplayConverter.Format(TimeSpan.FromSeconds(3_599_999)));
```

Extract a pure `HotkeyCaptureBox.CreateGesture(Key key, Key systemKey, ModifierKeys modifiers)` method and test that `key == Key.System` resolves to `systemKey`, modifier-only input returns `null`, and Escape cancels without changing the previous gesture.

- [ ] **Step 2: Run focused tests and confirm failure**

```powershell
dotnet test tests/TalesAlarm.Tests/TalesAlarm.Tests.csproj --filter "FullyQualifiedName~HotkeyGestureConverter|FullyQualifiedName~TimerDisplayConverter|FullyQualifiedName~HotkeyCapture"
```

Expected: missing converter/control types.

- [ ] **Step 3: Implement input controls**

`HotkeyCaptureBox` derives from `Control`, is focusable, shows the formatted current gesture, and enters capture on mouse click or keyboard focus. On capture start it raises `CaptureStarted`; `MainWindow` keeps the returned `IDisposable` from `MainViewModel.BeginHotkeyCapture()`. On a valid non-modifier `PreviewKeyDown`, it passes `e.Key`, `e.SystemKey`, and `Keyboard.Modifiers` to `CreateGesture`, updates the dependency property, raises `CaptureEnded`, and releases focus. Escape or losing keyboard focus raises `CaptureEnded` without modifying the gesture, ensuring suspended global keys are always restored.

`NumericTextBoxBehavior` is an attached behavior that allows digits only for timer fields and digits plus one culture-independent decimal separator for playback seconds. Pasted text is checked with the same predicate. Range validation remains in the viewmodel and is displayed beside the field.

- [ ] **Step 4: Build the Korean two-card UI**

Use a 1040x720 minimum-resizable window. The root contains:

1. Header: app name and `설정 적용` button.
2. Two equal-width timer cards in a two-column `Grid`.
3. Each card: state badge, 56px countdown text, hour/minute/second inputs, hotkey capture, reactivation combo, start/pause/reset buttons, validation message.
4. Bottom audio card: current filename, `음원 변경`, `미리 듣기`, `기본음 복원`, playback-second input, common error/recovery message.

Bind combo values to the three enum values with Korean display strings. Use system fonts, visible keyboard focus, minimum 44px button height, and colors that maintain readable contrast. Do not introduce a UI framework package.

- [ ] **Step 5: Connect only view-specific code-behind**

`MainWindow.xaml.cs` may handle file-dialog presentation, capture lease disposal, window activation, and `Closing`. It must forward selected absolute file paths to `AlarmSettingsViewModel.ImportAudioAsync(path)` and must not contain timer calculations, settings serialization, or audio timing.

On `Closing`, if `AllowClose` is false set `e.Cancel = true`, call `Hide()`, and raise `RequestHide`; when `AllowClose` is true permit normal close during explicit tray exit. `ShowAndActivate()` calls `Show()`, restores `WindowState` from minimized, and calls `Activate()`.

- [ ] **Step 6: Run tests and compile XAML**

```powershell
dotnet test TalesAlarm.sln
dotnet build src/TalesAlarm/TalesAlarm.csproj -c Debug
```

Expected: all tests pass and XAML compilation succeeds with zero warnings.

- [ ] **Step 7: Commit the WPF interface**

```powershell
git add src/TalesAlarm/App.xaml src/TalesAlarm/MainWindow.xaml src/TalesAlarm/MainWindow.xaml.cs src/TalesAlarm/Views tests/TalesAlarm.Tests/ViewModels
git commit -m "feat: add two-timer WPF interface"
```

---

### Task 9: Tray Lifecycle, Single Instance, Logging, and App Composition

**Files:**
- Create: `src/TalesAlarm/Infrastructure/TrayService.cs`
- Create: `src/TalesAlarm/Infrastructure/SingleInstanceService.cs`
- Create: `src/TalesAlarm/Infrastructure/FileLogger.cs`
- Create: `tests/TalesAlarm.Tests/Infrastructure/SingleInstanceServiceTests.cs`
- Create: `tests/TalesAlarm.Tests/Infrastructure/FileLoggerTests.cs`
- Modify: `src/TalesAlarm/App.xaml`
- Modify: `src/TalesAlarm/App.xaml.cs`
- Modify: `src/TalesAlarm/MainWindow.xaml.cs`
- Modify: `src/TalesAlarm/Configuration/AppPaths.cs`

**Interfaces:**
- Consumes: `AppPaths`, `MainViewModel`, `MainWindow`, generated ICO
- Produces: `SingleInstanceService.TryAcquireAsync(CancellationToken = default) -> Task<bool>`, `ActivationRequested`, `SignalOwnerAsync(CancellationToken = default) -> Task`
- Produces: `TrayService(Action show, Action exit)` with `Show()` and `Dispose()`
- Produces: `FileLogger.Write(string, Exception?)` and `PruneOldLogs()`

- [ ] **Step 1: Write failing single-instance and log-retention tests**

```csharp
[Fact]
public async Task SecondInstanceSignalsOwner()
{
    var name = $"TalesAlarm.Tests.{Guid.NewGuid():N}";
    await using var owner = new SingleInstanceService(name);
    await using var second = new SingleInstanceService(name);
    Assert.True(await owner.TryAcquireAsync());
    var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    owner.ActivationRequested += (_, _) => activated.TrySetResult();

    Assert.False(await second.TryAcquireAsync());
    await second.SignalOwnerAsync();

    await activated.Task.WaitAsync(TimeSpan.FromSeconds(2));
}
```

For logging, create files dated 8, 7, 1, and 0 days ago under a temporary Logs directory. `PruneOldLogs()` must delete only the 8-day file and retain the most recent seven calendar days. Verify `Write` appends UTF-8 timestamp, message, and exception type.

- [ ] **Step 2: Run infrastructure tests and confirm failure**

```powershell
dotnet test tests/TalesAlarm.Tests/TalesAlarm.Tests.csproj --filter FullyQualifiedName~Infrastructure
```

Expected: missing infrastructure types.

- [ ] **Step 3: Implement named mutex and pipe activation**

`SingleInstanceService` creates `new Mutex(false, "Local\\<name>.Mutex", out createdNew)` and retains the handle for its lifetime; `createdNew` determines ownership without `WaitOne`/`ReleaseMutex`, avoiding thread-affinity problems across async continuations. The owner starts one cancellable named-pipe server loop on `<name>.Activate` before `TryAcquireAsync` returns true. Each connection reads the exact UTF-8 line `SHOW` and raises `ActivationRequested`; malformed messages are ignored. The second instance connects with a two-second timeout, writes `SHOW`, flushes, and exits. `DisposeAsync` cancels and awaits the pipe loop, then disposes the mutex handle and tolerates `ObjectDisposedException` during shutdown.

- [ ] **Step 4: Implement tray and logging services**

`TrayService` uses `System.Windows.Forms.NotifyIcon`, loads the associated executable icon with `Icon.ExtractAssociatedIcon(Environment.ProcessPath!)`, falls back to `SystemIcons.Application` if extraction returns null, sets tooltip `Tales Alarm`, and creates exactly two menu items: `열기` and `종료`. Double-click and `열기` invoke the show callback. Only `종료` invokes the exit callback. `Dispose()` hides and disposes the icon and menu.

`FileLogger` writes `%LocalAppData%\TalesAlarm\Logs\app-YYYYMMDD.log` with a process-local lock. On startup, delete `app-*.log` files whose date is older than seven calendar days; ignore unrelated files and log-pruning failures.

- [ ] **Step 5: Compose the app lifecycle**

Remove `StartupUri` from `App.xaml`. In `App.OnStartup`:

1. Resolve `AppPaths.FromArguments(e.Args, AllowDataRootOverride)`, where `AllowDataRootOverride` is a compile-time true constant only under `#if DEBUG` and false otherwise; create `FileLogger` and prune logs. `DispatcherUnhandledException` logs, shows one Korean error dialog, and sets `Handled = true`; `TaskScheduler.UnobservedTaskException` logs and calls `SetObserved()`; `AppDomain.UnhandledException` logs without pretending the process can always recover.
2. Acquire `SingleInstanceService`; use name `TalesAlarm` in production and `TalesAlarm.Debug.<first-12-hex-of-SHA256-data-root>` only when the Debug data-root override is active. If not owner, signal owner and call `Shutdown(0)`.
3. Create settings, hotkeys, audio backend/service/store, two countdowns, viewmodels, and `MainWindow`.
4. Call `new WindowInteropHelper(mainWindow).EnsureHandle()`, attach `GlobalHotkeyService` to that nonzero handle, and add an `HwndSource` hook that delegates `WM_HOTKEY` to `ProcessWindowMessage`.
5. Await `MainViewModel.InitializeAsync()`. A default hotkey conflict becomes `MainViewModel.ErrorMessage` and does not abort startup. Then show the window and start one 50ms `DispatcherTimer` calling `MainViewModel.Tick()`.
6. Create `TrayService`; its show callback dispatches `MainWindow.ShowAndActivate()`, and its exit callback sets `_isExiting`, stops the timer, closes the window, and calls `Shutdown()`.
7. Pipe activation dispatches `ShowAndActivate()`.

In `OnExit`, dispose in reverse order: dispatcher timer, tray, hotkeys, alarm backend/service, single-instance service. Before explicit exit, set `MainWindow.AllowClose = true`; otherwise its `Closing` handler hides the window.

- [ ] **Step 6: Run infrastructure, full tests, and a hidden smoke launch**

```powershell
dotnet test TalesAlarm.sln
dotnet build src/TalesAlarm/TalesAlarm.csproj -c Release
```

Launch the Debug app with `Start-Process -WindowStyle Hidden -ArgumentList @('--data-root', $smokeRoot) -PassThru`, where `$smokeRoot` is a unique absolute directory under `[IO.Path]::GetTempPath()`. Wait up to five seconds for the exact returned process ID to remain alive, start a second copy with the same arguments, and assert the second returned process exits while the original remains. In `finally`, stop only the exact original ID with `Stop-Process -Id $owner.Id`, then delete `$smokeRoot` only after `GetFullPath($smokeRoot)` is verified to start with `GetFullPath([IO.Path]::GetTempPath())`.

Expected: tests/build pass; the second copy exits and the owner remains running.

- [ ] **Step 7: Commit lifecycle integration**

```powershell
git add src/TalesAlarm/App.xaml src/TalesAlarm/App.xaml.cs src/TalesAlarm/MainWindow.xaml.cs src/TalesAlarm/Infrastructure tests/TalesAlarm.Tests/Infrastructure
git commit -m "feat: keep alarm running in tray"
```

---

### Task 10: Self-Contained Publish, Documentation, and Acceptance Verification

**Files:**
- Create: `src/TalesAlarm/Properties/PublishProfiles/win-x64.pubxml`
- Modify: `src/TalesAlarm/TalesAlarm.csproj`
- Create: `tests/Verify-PublishArtifact.ps1`
- Create: `README.md`

**Interfaces:**
- Consumes: completed app and all acceptance tests
- Produces: `artifacts/TalesAlarm-win-x64/TalesAlarm.exe`

- [ ] **Step 1: Write the failing publish-artifact behavior check**

Create `tests/Verify-PublishArtifact.ps1` with a required absolute `-PublishDirectory` argument. The script must fail with a nonzero exit code unless all of these observable behaviors hold:

1. `TalesAlarm.exe` exists and has nonzero length.
2. The directory has no required loose `.dll`, `.deps.json`, `.runtimeconfig.json`, `.wav`, or `.ico` file.
3. With `LOCALAPPDATA` temporarily redirected to a unique directory below `[IO.Path]::GetTempPath()`, `Start-Process -WindowStyle Hidden -PassThru` keeps the exact executable process alive for three seconds.
4. In `finally`, the script stops only the exact returned process ID, restores its previous process-level `LOCALAPPDATA`, verifies the temporary path remains below the system temp directory, and removes only that temporary directory.

Run it before creating the profile:

```powershell
powershell -ExecutionPolicy Bypass -File tests/Verify-PublishArtifact.ps1 -PublishDirectory artifacts/TalesAlarm-win-x64
```

Expected: FAIL with `TalesAlarm.exe가 없습니다.` because no publish has occurred. This proves the check observes the artifact rather than the project XML.

- [ ] **Step 2: Add the exact publish profile and assembly metadata**

```xml
<Project>
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <PublishTrimmed>false</PublishTrimmed>
    <DebugType>embedded</DebugType>
    <PublishDir>$(MSBuildProjectDirectory)\..\..\artifacts\TalesAlarm-win-x64\</PublishDir>
  </PropertyGroup>
</Project>
```

Set `<AssemblyTitle>Tales Alarm</AssemblyTitle>`, `<Product>Tales Alarm</Product>`, `<Company>Tales Alarm</Company>`, and `<Version>1.0.0</Version>` in `TalesAlarm.csproj`. Ensure the icon and default WAV are embedded and no runtime NuGet package references exist.

- [ ] **Step 3: Write user and developer documentation**

`README.md` must contain:

- What the two timers do and defaults F4/F8.
- How the three reactivation policies behave.
- How to change time, capture hotkeys, apply settings, import WAV/MP3, preview, restore default, and set 0.1~60.0 seconds.
- Closing versus tray `종료` behavior.
- Hotkey-conflict recovery and default-audio fallback.
- Exact build/test/publish commands and final artifact path.
- Explicit statement that Windows startup registration is not included.

- [ ] **Step 4: Run all automated verification from a clean build**

Run separately:

```powershell
dotnet clean TalesAlarm.sln -c Release
dotnet test TalesAlarm.sln -c Release
dotnet publish src/TalesAlarm/TalesAlarm.csproj -p:PublishProfile=win-x64
powershell -ExecutionPolicy Bypass -File tests/Verify-PublishArtifact.ps1 -PublishDirectory artifacts/TalesAlarm-win-x64
```

Expected: zero warnings, every test passes, and `artifacts/TalesAlarm-win-x64/TalesAlarm.exe` exists. The artifact directory must not contain another required DLL, JSON runtime file, or loose WAV/ICO file.

- [ ] **Step 5: Run focused acceptance checks with short timer values**

Launch the Debug build visibly with `--data-root <absolute temporary directory>` so production data under `%LocalAppData%\TalesAlarm` remains untouched. Verify:

1. Defaults load as 20:00/F4 and 30:00/F8.
2. Change both durations to 00:00:02, apply, then start both with F4/F8.
3. Set timer 1 to Restart and verify F4 resets it; set timer 2 to PauseResume and verify F8 pauses/resumes; repeat with Ignore.
4. Close the window and verify both continue in tray.
5. Let both expire together and confirm one non-overlapped default chime.
6. Import a valid short WAV, set 2.3 seconds, and confirm it loops until the configured deadline.
7. Attempt duplicate keys and confirm validation prevents applying them; rerun `GlobalHotkeyServiceTests` to verify native registration failure restores both previous keys.
8. Corrupt the temporary root's disposable settings file, relaunch, and confirm defaults plus a timestamped backup.
9. Exit through the tray menu and confirm the process ends.

Record the commands and observations in the final handoff; do not commit test user data or logs.

- [ ] **Step 6: Inspect the final diff and commit**

```powershell
git diff --check
git status --short
git add src/TalesAlarm/Properties src/TalesAlarm/TalesAlarm.csproj tests/Verify-PublishArtifact.ps1 README.md
git commit -m "build: publish standalone Tales Alarm app"
```

- [ ] **Step 7: Perform final repository verification**

```powershell
git status --short
git log --oneline -10
Get-Item artifacts/TalesAlarm-win-x64/TalesAlarm.exe | Select-Object Name,Length,LastWriteTime
```

Expected: working tree is clean, the ten task commits are present after the design/plan commits, and the standalone executable has a nonzero size and current timestamp.

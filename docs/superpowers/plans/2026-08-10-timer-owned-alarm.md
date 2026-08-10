# 타이머별 알람 확인 구현 계획

> **에이전트 작업자용:** 필수 하위 스킬로 `superpowers:subagent-driven-development`(권장) 또는 `superpowers:executing-plans`를 사용해 이 계획을 작업별로 실행한다. 진행 상태는 체크박스(`- [ ]`)로 기록한다.

**목표:** 공통 알람 설정과 한 개의 오디오 채널은 유지하면서, 완료 알람을 발생시킨 타이머를 조작할 때 그 타이머의 알람 소유권만 확인 처리한다.

**구조:** `TimerViewModel`이 실제로 처리된 사용자 조작을 타이머 번호와 함께 알리고, 새 `TimerAlarmCoordinator`가 타이머별 완료 알람과 미리 듣기의 독립적인 만료 시각을 관리한다. `MainViewModel`은 완료 출처를 `HashSet<int>`로 보존해 조정기에 등록하며, 저수준 `AlarmAudioService`는 기존의 단일 채널 반복 재생과 기본음 대체만 담당한다.

**기술 스택:** C# 14, .NET 10, WPF, xUnit, `TimeProvider`

## 전역 제약

- `AlarmSettings`, `AppSettings.CurrentSchemaVersion=1`, 기존 설정 JSON과 공통 `알람 음원` 화면을 변경하지 않는다.
- 타이머별 음원·재생시간, 여러 음원 동시 재생, 알람 확인 버튼, 볼륨 기능을 추가하지 않는다.
- 타이머 1의 완료 알람은 타이머 2 조작으로 중지되지 않아야 한다.
- 두 타이머 알람이 활성 상태라면 한 타이머 조작 후에도 다른 타이머의 알람 때문에 재생을 유지해야 한다.
- 시작, 일시정지·재개, 초기화와 실제로 처리된 단축키만 해당 타이머의 완료 알람을 확인한다.
- `ReactivationPolicy.Ignore`로 무시된 단축키, 설정 적용, 보기 전환과 주기적 `Tick`은 알람을 확인하지 않는다.
- 미리 듣기는 타이머 조작으로 중단되지 않는다.
- 사용자 음원 실패 시 기본음 대체, 앱 종료 시 전체 중지, 간단 보기 동작을 유지한다.

---

### 작업 1: 처리된 타이머 조작 신호 만들기

**파일:**
- 수정: `src/TalesAlarm/Timers/CountdownTimer.cs:82`
- 수정: `src/TalesAlarm/ViewModels/TimerViewModel.cs:39,152-188`
- 테스트: `tests/TalesAlarm.Tests/Timers/CountdownTimerTests.cs`
- 테스트: `tests/TalesAlarm.Tests/ViewModels/TimerViewModelTests.cs`

**인터페이스:**
- 생성: `bool CountdownTimer.HandleActivation(ReactivationPolicy policy)`
- 생성: `event EventHandler<int>? TimerViewModel.Operated`
- `Operated`의 이벤트 인수는 `TimerIndex`이며 시작, 초기화, 실제 일시정지·재개와 처리된 단축키 뒤에 한 번 발생한다.

- [x] **1단계: 단축키 처리 결과의 실패 테스트 작성**

`CountdownTimerTests`에 실행 중 정책별 반환값을 검증한다.

```csharp
[Theory]
[InlineData(ReactivationPolicy.Restart, true)]
[InlineData(ReactivationPolicy.PauseResume, true)]
[InlineData(ReactivationPolicy.Ignore, false)]
public void HandleActivation_ReturnsWhetherRunningTimerWasOperated(
    ReactivationPolicy policy,
    bool expectedHandled)
{
    var timer = new CountdownTimer(
        new ManualTimeProvider(),
        TimeSpan.FromSeconds(20));
    timer.Start();

    var handled = timer.HandleActivation(policy);

    Assert.Equal(expectedHandled, handled);
}

[Theory]
[InlineData(TimerState.Idle)]
[InlineData(TimerState.Completed)]
public void HandleActivation_FromIdleOrCompleted_IsHandledEvenWithIgnore(
    TimerState initialState)
{
    var time = new ManualTimeProvider();
    var timer = new CountdownTimer(time, TimeSpan.FromSeconds(1));
    if (initialState == TimerState.Completed)
    {
        timer.Start();
        time.Advance(TimeSpan.FromSeconds(1));
        timer.Tick();
    }

    Assert.True(timer.HandleActivation(ReactivationPolicy.Ignore));
}
```

- [x] **2단계: 조작 이벤트의 실패 테스트 작성**

`TimerViewModelTests`에 화면 명령과 단축키를 구분하는 두 테스트를 추가한다.

```csharp
[Fact]
public void Commands_RaiseOperatedWithTimerIndexAfterAcceptedActions()
{
    var viewModel = CreateViewModel(out _);
    var operated = new List<int>();
    viewModel.Operated += (_, timerIndex) => operated.Add(timerIndex);

    viewModel.StartCommand.Execute(null);
    viewModel.PauseResumeCommand.Execute(null);
    viewModel.PauseResumeCommand.Execute(null);
    viewModel.ResetCommand.Execute(null);

    Assert.Equal(new[] { 1, 1, 1, 1 }, operated);
}

[Fact]
public void HandleHotkey_WhenAppliedPolicyIgnoresInput_DoesNotRaiseOperated()
{
    var viewModel = new TimerViewModel(
        1,
        new CountdownTimer(new ManualTimeProvider(), TimeSpan.FromSeconds(10)),
        Settings(10, ReactivationPolicy.Ignore));
    var operated = 0;
    viewModel.Operated += (_, _) => operated++;
    viewModel.StartCommand.Execute(null);
    operated = 0;

    viewModel.HandleHotkey();

    Assert.Equal(0, operated);
}

[Fact]
public void HandleHotkey_WhenInputIsHandled_RaisesOperated()
{
    var viewModel = CreateViewModel(out _);
    var timerIndexes = new List<int>();
    viewModel.Operated += (_, timerIndex) => timerIndexes.Add(timerIndex);

    viewModel.HandleHotkey();

    Assert.Equal(new[] { 1 }, timerIndexes);
}
```

- [x] **3단계: 집중 테스트를 실행해 RED 확인**

```powershell
dotnet test TalesAlarm.sln -c Release --filter "FullyQualifiedName~CountdownTimerTests|FullyQualifiedName~TimerViewModelTests"
```

예상 결과: `HandleActivation`이 `void`라 반환값을 받을 수 없고 `TimerViewModel.Operated`가 없어 컴파일이 실패한다.

실제 결과: `void` 반환값 할당 오류 2건과 `Operated` 미정의 오류 3건으로 예상한 RED를 확인했다.

- [x] **4단계: `HandleActivation` 처리 여부 반환 구현**

`CountdownTimer.HandleActivation`을 다음 흐름으로 변경한다. 기존 상태 변화는 그대로 유지한다.

```csharp
public bool HandleActivation(ReactivationPolicy policy)
{
    if (State == TimerState.Running)
    {
        Tick();
    }

    if (State is TimerState.Idle or TimerState.Completed)
    {
        Start();
        return true;
    }

    switch (policy)
    {
        case ReactivationPolicy.Restart:
            Start();
            return true;
        case ReactivationPolicy.PauseResume:
            if (State == TimerState.Running)
            {
                Pause();
            }
            else
            {
                Resume();
            }

            return true;
        case ReactivationPolicy.Ignore:
            return false;
        default:
            throw new ArgumentOutOfRangeException(nameof(policy));
    }
}
```

- [x] **5단계: `TimerViewModel.Operated` 구현**

이벤트를 추가하고 실제 처리 후에만 발생시킨다.

```csharp
public event EventHandler? Completed;

public event EventHandler<int>? Operated;

public void HandleHotkey()
{
    if (!timer.HandleActivation(appliedReactivationPolicy))
    {
        return;
    }

    RefreshState();
    RaiseOperated();
}

private void Start()
{
    timer.Start();
    RefreshState();
    RaiseOperated();
}

private void PauseOrResume()
{
    var operated = false;
    if (timer.State == TimerState.Running)
    {
        timer.Pause();
        operated = true;
    }
    else if (timer.State == TimerState.Paused)
    {
        timer.Resume();
        operated = true;
    }

    RefreshState();
    if (operated)
    {
        RaiseOperated();
    }
}

private void Reset()
{
    timer.Reset();
    RefreshState();
    RaiseOperated();
}

private void RaiseOperated() => Operated?.Invoke(this, TimerIndex);
```

- [x] **6단계: 집중 테스트 GREEN 확인**

```powershell
dotnet test TalesAlarm.sln -c Release --no-restore --filter "FullyQualifiedName~CountdownTimerTests|FullyQualifiedName~TimerViewModelTests"
```

예상 결과: 두 테스트 클래스의 모든 테스트가 통과한다.

실제 결과: 집중 테스트 `33/33`이 통과했다.

- [x] **7단계: 작업 1 커밋**

```powershell
git add src/TalesAlarm/Timers/CountdownTimer.cs src/TalesAlarm/ViewModels/TimerViewModel.cs tests/TalesAlarm.Tests/Timers/CountdownTimerTests.cs tests/TalesAlarm.Tests/ViewModels/TimerViewModelTests.cs
git commit -m "feat: report handled timer operations"
```

커밋: `6d37c65`

---

### 작업 2: 타이머별 알람 소유권 조정기 추가

**파일:**
- 생성: `src/TalesAlarm/Audio/TimerAlarmCoordinator.cs`
- 생성: `tests/TalesAlarm.Tests/Audio/TimerAlarmCoordinatorTests.cs`

**인터페이스:**
- 생성: `ITimerAlarmCoordinator.StartTimerAlarm(int, string, string, TimeSpan)`
- 생성: `ITimerAlarmCoordinator.StartPreview(string, string, TimeSpan)`
- 생성: `ITimerAlarmCoordinator.AcknowledgeTimer(int)`
- 생성: `ITimerAlarmCoordinator.Tick()`
- 사용: 기존 `IAlarmAudioService.StartOrExtend`, `Stop`, `Tick`, `IsPlaying`

- [x] **1단계: 소유권 분리 실패 테스트 작성**

새 `TimerAlarmCoordinatorTests`와 내부 `FakeAlarmAudioService`를 만든다.

```csharp
[Fact]
public void AcknowledgeTimer_WhenOnlyOtherTimerOwnsAlarm_KeepsPlaying()
{
    var fixture = new Fixture();
    fixture.Coordinator.StartTimerAlarm(
        1,
        "default.wav",
        "default.wav",
        TimeSpan.FromSeconds(10));

    fixture.Coordinator.AcknowledgeTimer(2);

    Assert.True(fixture.Audio.IsPlaying);
    Assert.Equal(0, fixture.Audio.StopCalls);
}

[Fact]
public void AcknowledgeTimer_WhenBothOwnAlarm_StopsOnlyAfterLastOwner()
{
    var fixture = new Fixture();
    fixture.StartTimer(1, TimeSpan.FromSeconds(10));
    fixture.StartTimer(2, TimeSpan.FromSeconds(10));

    fixture.Coordinator.AcknowledgeTimer(1);
    Assert.True(fixture.Audio.IsPlaying);
    Assert.Equal(0, fixture.Audio.StopCalls);

    fixture.Coordinator.AcknowledgeTimer(2);
    Assert.False(fixture.Audio.IsPlaying);
    Assert.Equal(1, fixture.Audio.StopCalls);
}
```

- [x] **2단계: 독립 만료와 종료 시각 재조정 실패 테스트 작성**

```csharp
[Fact]
public void Tick_ExpiresOwnersIndependently()
{
    var fixture = new Fixture();
    fixture.StartTimer(1, TimeSpan.FromSeconds(2));
    fixture.Time.Advance(TimeSpan.FromSeconds(1));
    fixture.StartTimer(2, TimeSpan.FromSeconds(4));

    fixture.Time.Advance(TimeSpan.FromSeconds(1.1));
    fixture.Coordinator.Tick();
    Assert.True(fixture.Audio.IsPlaying);

    fixture.Time.Advance(TimeSpan.FromSeconds(3));
    fixture.Coordinator.Tick();
    Assert.False(fixture.Audio.IsPlaying);
}

[Fact]
public void AcknowledgeTimer_WhenLatestOwnerIsRemoved_ShortensAudioDeadline()
{
    var fixture = new Fixture();
    fixture.StartTimer(1, TimeSpan.FromSeconds(3));
    fixture.Time.Advance(TimeSpan.FromSeconds(1));
    fixture.StartTimer(2, TimeSpan.FromSeconds(5));
    fixture.Time.Advance(TimeSpan.FromSeconds(1));

    fixture.Coordinator.AcknowledgeTimer(2);

    Assert.Equal(
        TimeSpan.FromSeconds(1),
        fixture.Audio.StartRequests[^1].Duration);
}

[Fact]
public void StartTimerAlarm_ForSameTimer_ReplacesItsDeadline()
{
    var fixture = new Fixture();
    fixture.StartTimer(1, TimeSpan.FromSeconds(2));
    fixture.Time.Advance(TimeSpan.FromSeconds(1));
    fixture.StartTimer(1, TimeSpan.FromSeconds(3));
    fixture.Time.Advance(TimeSpan.FromSeconds(2));

    fixture.Coordinator.Tick();

    Assert.True(fixture.Audio.IsPlaying);
}
```

- [x] **3단계: 미리 듣기 소유권 실패 테스트 작성**

```csharp
[Fact]
public void AcknowledgeTimer_WhenPreviewRemains_DoesNotStopPreview()
{
    var fixture = new Fixture();
    fixture.Coordinator.StartPreview(
        "preview.wav",
        "default.wav",
        TimeSpan.FromSeconds(3));
    fixture.StartTimer(1, TimeSpan.FromSeconds(3));

    fixture.Coordinator.AcknowledgeTimer(1);

    Assert.True(fixture.Audio.IsPlaying);
    Assert.Equal(0, fixture.Audio.StopCalls);
}
```

테스트 Fixture와 가짜 서비스는 실제 조정기의 시간·호출 효과를 관찰할 수 있게 다음 계약을 사용한다.

```csharp
private sealed class Fixture
{
    public Fixture()
    {
        Time = new ManualTimeProvider();
        Audio = new FakeAlarmAudioService();
        Coordinator = new TimerAlarmCoordinator(Time, Audio);
    }

    public ManualTimeProvider Time { get; }
    public FakeAlarmAudioService Audio { get; }
    public TimerAlarmCoordinator Coordinator { get; }

    public void StartTimer(int timerIndex, TimeSpan duration) =>
        Coordinator.StartTimerAlarm(
            timerIndex,
            "default.wav",
            "default.wav",
            duration);
}

private sealed class FakeAlarmAudioService : IAlarmAudioService
{
    public bool IsPlaying { get; private set; }
    public string? LastError => null;
    public List<StartRequest> StartRequests { get; } = [];
    public int StopCalls { get; private set; }
    public int TickCalls { get; private set; }

    public void StartOrExtend(
        string requestedPath,
        string fallbackPath,
        TimeSpan duration)
    {
        StartRequests.Add(new(requestedPath, fallbackPath, duration));
        IsPlaying = true;
    }

    public void Stop()
    {
        if (!IsPlaying)
        {
            return;
        }

        IsPlaying = false;
        StopCalls++;
    }

    public void Tick() => TickCalls++;
}

private sealed record StartRequest(
    string RequestedPath,
    string FallbackPath,
    TimeSpan Duration);
```

- [x] **4단계: 새 테스트를 실행해 RED 확인**

```powershell
dotnet test TalesAlarm.sln -c Release --no-restore --filter "FullyQualifiedName~TimerAlarmCoordinatorTests"
```

예상 결과: `ITimerAlarmCoordinator`와 `TimerAlarmCoordinator`가 없어 컴파일이 실패한다.

실제 결과: `TimerAlarmCoordinator` 형식이 없다는 `CS0246` 컴파일 오류로 예상한 RED를 확인했다.

- [x] **5단계: 조정기 인터페이스와 소유권 모델 구현**

`TimerAlarmCoordinator.cs`에 다음 공개 계약과 비공개 항목 모델을 만든다.

```csharp
namespace TalesAlarm.Audio;

public interface ITimerAlarmCoordinator
{
    void StartTimerAlarm(
        int timerIndex,
        string requestedPath,
        string fallbackPath,
        TimeSpan duration);

    void StartPreview(
        string requestedPath,
        string fallbackPath,
        TimeSpan duration);

    void AcknowledgeTimer(int timerIndex);

    void Tick();
}

public sealed class TimerAlarmCoordinator : ITimerAlarmCoordinator
{
    private readonly TimeProvider timeProvider;
    private readonly IAlarmAudioService audioService;
    private readonly Dictionary<int, AlarmClaim> timerClaims = [];
    private AlarmClaim? previewClaim;

    public TimerAlarmCoordinator(
        TimeProvider timeProvider,
        IAlarmAudioService audioService)
    {
        this.timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        this.audioService = audioService
            ?? throw new ArgumentNullException(nameof(audioService));
    }

    private sealed record AlarmClaim(
        string RequestedPath,
        string FallbackPath,
        long StartedAt,
        TimeSpan Duration);
}
```

`StartTimerAlarm`은 `timerIndex > 0`, 비어 있지 않은 두 경로와 양수 재생시간을 검사한 뒤 같은 번호의 항목을 교체한다. `StartPreview`는 별도 필드를 교체한다.

```csharp
public void StartTimerAlarm(
    int timerIndex,
    string requestedPath,
    string fallbackPath,
    TimeSpan duration)
{
    if (timerIndex <= 0)
    {
        throw new ArgumentOutOfRangeException(nameof(timerIndex));
    }

    timerClaims[timerIndex] = CreateClaim(
        requestedPath,
        fallbackPath,
        duration);
    ReconcilePlayback(allowStart: true);
}

public void StartPreview(
    string requestedPath,
    string fallbackPath,
    TimeSpan duration)
{
    previewClaim = CreateClaim(requestedPath, fallbackPath, duration);
    ReconcilePlayback(allowStart: true);
}

private AlarmClaim CreateClaim(
    string requestedPath,
    string fallbackPath,
    TimeSpan duration)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(requestedPath);
    ArgumentException.ThrowIfNullOrWhiteSpace(fallbackPath);
    if (duration <= TimeSpan.Zero)
    {
        throw new ArgumentOutOfRangeException(nameof(duration));
    }

    return new(
        requestedPath,
        fallbackPath,
        timeProvider.GetTimestamp(),
        duration);
}
```

- [x] **6단계: 확인·만료·재생 조정 구현**

남은 시간은 `TimeProvider.GetElapsedTime(claim.StartedAt)`으로 계산한다. 만료 항목을 제거한 뒤 가장 긴 남은 시간을 가진 항목으로 저수준 서비스의 종료 시점을 맞춘다.

```csharp
public void AcknowledgeTimer(int timerIndex)
{
    if (!timerClaims.Remove(timerIndex))
    {
        return;
    }

    ReconcilePlayback(allowStart: false);
}

public void Tick()
{
    RemoveExpiredClaims();
    if (!HasClaims)
    {
        audioService.Stop();
    }

    audioService.Tick();
}

private void ReconcilePlayback(bool allowStart)
{
    RemoveExpiredClaims();
    var latest = FindLatestClaim();
    if (latest is null)
    {
        audioService.Stop();
        return;
    }

    if (!allowStart && !audioService.IsPlaying)
    {
        return;
    }

    audioService.StartOrExtend(
        latest.Value.Claim.RequestedPath,
        latest.Value.Claim.FallbackPath,
        latest.Value.Remaining);
}
```

`FindLatestClaim`은 타이머 항목과 `previewClaim`을 모두 검사해 `(AlarmClaim Claim, TimeSpan Remaining)?`을 반환한다. `RemoveExpiredClaims`는 남은 시간이 `TimeSpan.Zero` 이하인 타이머 키와 미리 듣기 항목을 제거한다. 새 항목 등록 중 저수준 서비스가 이미 재생 중이면 기존 `StartOrExtend`가 음원을 다시 열지 않고 종료 시각만 갱신한다.

다음 헬퍼를 그대로 사용해 컬렉션 수정 중 열거 오류와 음수 남은 시간 전달을 막는다.

```csharp
private bool HasClaims => timerClaims.Count > 0 || previewClaim is not null;

private void RemoveExpiredClaims()
{
    var expiredTimerIndexes = timerClaims
        .Where(pair => GetRemaining(pair.Value) <= TimeSpan.Zero)
        .Select(pair => pair.Key)
        .ToArray();
    foreach (var timerIndex in expiredTimerIndexes)
    {
        timerClaims.Remove(timerIndex);
    }

    if (previewClaim is not null
        && GetRemaining(previewClaim) <= TimeSpan.Zero)
    {
        previewClaim = null;
    }
}

private (AlarmClaim Claim, TimeSpan Remaining)? FindLatestClaim()
{
    (AlarmClaim Claim, TimeSpan Remaining)? latest = null;
    foreach (var claim in timerClaims.Values)
    {
        latest = SelectLater(latest, claim);
    }

    if (previewClaim is not null)
    {
        latest = SelectLater(latest, previewClaim);
    }

    return latest;
}

private (AlarmClaim Claim, TimeSpan Remaining) SelectLater(
    (AlarmClaim Claim, TimeSpan Remaining)? current,
    AlarmClaim candidate)
{
    var remaining = GetRemaining(candidate);
    return current is null || remaining > current.Value.Remaining
        ? (candidate, remaining)
        : current.Value;
}

private TimeSpan GetRemaining(AlarmClaim claim) =>
    claim.Duration - timeProvider.GetElapsedTime(claim.StartedAt);
```

- [x] **7단계: 조정기 테스트 GREEN 확인**

```powershell
dotnet test TalesAlarm.sln -c Release --no-restore --filter "FullyQualifiedName~TimerAlarmCoordinatorTests|FullyQualifiedName~AlarmAudioServiceTests"
```

예상 결과: 새 조정기 테스트와 기존 저수준 오디오 반복·대체 테스트가 모두 통과한다.

실제 결과: 새 조정기 테스트 6개와 기존 오디오 서비스 테스트 6개, 총 12개가 모두 통과했다.

- [x] **8단계: 작업 2 커밋**

```powershell
git add src/TalesAlarm/Audio/TimerAlarmCoordinator.cs tests/TalesAlarm.Tests/Audio/TimerAlarmCoordinatorTests.cs
git commit -m "feat: coordinate timer-owned alarms"
```

커밋: `4c923a2`

---

### 작업 3: 완료 출처·사용자 조작·미리 듣기 연결

**파일:**
- 수정: `src/TalesAlarm/ViewModels/MainViewModel.cs:14-53,205-276`
- 수정: `src/TalesAlarm/ViewModels/AlarmSettingsViewModel.cs:9-32,154-166`
- 수정: `src/TalesAlarm/App.xaml.cs:143-164`
- 수정: `tests/TalesAlarm.Tests/ViewModels/MainViewModelTests.cs`
- 수정: `docs/superpowers/specs/2026-08-10-timer-owned-alarm-design.md`

**인터페이스:**
- `MainViewModel`과 `AlarmSettingsViewModel`은 `IAlarmAudioService` 대신 `ITimerAlarmCoordinator`를 소비한다.
- `MainViewModel`은 `HashSet<int> pendingCompletions`를 사용한다.
- `TimerViewModel.Operated`는 같은 번호의 대기 완료를 취소하고 `AcknowledgeTimer`를 호출한다.

- [x] **1단계: 다른 타이머 조작 분리 실패 테스트 작성**

`MainViewModelTests.Fixture`가 `FakeAlarmAudioService`와 실제 `TimerAlarmCoordinator`를 조합하도록 변경한 뒤 다음 테스트를 추가한다.

```csharp
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
```

- [x] **2단계: 두 소유권과 같은 조작 주기의 완료 취소 실패 테스트 작성**

```csharp
[Fact]
public async Task TimerOperation_WhenBothAlarmsAreActive_KeepsOtherAlarmPlaying()
{
    using var fixture = Fixture.Create(WithDurations(1, 1));
    await fixture.ViewModel.InitializeAsync();
    fixture.ViewModel.Timer1.StartCommand.Execute(null);
    fixture.ViewModel.Timer2.StartCommand.Execute(null);
    fixture.Time.Advance(TimeSpan.FromSeconds(1));
    fixture.ViewModel.Tick();

    fixture.ViewModel.Timer1.ResetCommand.Execute(null);
    Assert.True(fixture.Audio.IsPlaying);

    fixture.ViewModel.Timer2.ResetCommand.Execute(null);
    Assert.False(fixture.Audio.IsPlaying);
}

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
```

- [x] **3단계: 미리 듣기와 설정 적용 실패 테스트 작성**

```csharp
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

[Fact]
public async Task ToggleCompactView_DoesNotAcknowledgeActiveTimerAlarm()
{
    using var fixture = Fixture.Create(WithDurations(1, 10));
    await fixture.ViewModel.InitializeAsync();
    fixture.ViewModel.Timer1.StartCommand.Execute(null);
    fixture.Time.Advance(TimeSpan.FromSeconds(1));
    fixture.ViewModel.Tick();

    await ((AsyncRelayCommand)fixture.ViewModel.ToggleCompactViewCommand)
        .ExecuteAsync();

    Assert.True(fixture.Audio.IsPlaying);
}

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
```

- [x] **4단계: ViewModel 집중 테스트 RED 확인**

```powershell
dotnet test TalesAlarm.sln -c Release --no-restore --filter "FullyQualifiedName~MainViewModelTests"
```

예상 결과: 현재 `MainViewModel`은 완료 타이머 출처를 보존하지 않고 어떤 타이머 조작도 오디오 조정기에 전달하지 않으므로 새 테스트가 실패하거나 새 생성자 계약 때문에 컴파일이 실패한다.

실제 결과: 테스트 Fixture가 전달한 `TimerAlarmCoordinator`를 기존 생성자의 `IAlarmAudioService` 매개변수로 변환할 수 없다는 `CS1503` 오류로 예상한 RED를 확인했다.

- [x] **5단계: `AlarmSettingsViewModel` 미리 듣기 연결**

필드와 생성자 매개변수를 `ITimerAlarmCoordinator alarmCoordinator`로 바꾸고 `Preview`의 마지막 호출을 다음처럼 교체한다.

```csharp
alarmCoordinator.StartPreview(
    GetRequestedPath(),
    defaultAlarmPath,
    TimeSpan.FromSeconds((double)playbackSeconds));
```

가져오기, 기본음 복원과 설정 저장 코드는 변경하지 않는다.

- [x] **6단계: `MainViewModel`의 완료·조작 연결 구현**

필드와 생성자 의존성을 교체하고 이벤트를 연결한다.

```csharp
private readonly ITimerAlarmCoordinator alarmCoordinator;
private readonly HashSet<int> pendingCompletions = [];

// 생성자 내부
this.alarmCoordinator = alarmCoordinator
    ?? throw new ArgumentNullException(nameof(alarmCoordinator));
Timer1.Completed += OnTimerCompleted;
Timer2.Completed += OnTimerCompleted;
Timer1.Operated += OnTimerOperated;
Timer2.Operated += OnTimerOperated;
```

`Tick`은 한 주기의 완료 번호를 모두 등록하고 조정기의 주기 처리를 항상 호출한다.

```csharp
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
                TimeSpan.FromSeconds(
                    (double)savedSettings.Alarm.PlaybackSeconds));
        }

        pendingCompletions.Clear();
    }
    finally
    {
        alarmCoordinator.Tick();
    }
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
```

`pendingCompletions.Remove`는 `Pause`, 단축키 등의 내부 `Tick`에서 완료 이벤트가 먼저 발생한 뒤 같은 사용자 조작이 이어지는 경우 알람이 다음 화면 주기에 다시 시작되지 않게 한다.

- [x] **7단계: 앱 구성과 테스트 Fixture 갱신**

`App.ComposeAndShowApplication`에서 저수준 서비스 바로 다음에 조정기를 만든다.

```csharp
alarmAudioService = new AlarmAudioService(TimeProvider.System, audioBackend);
var alarmCoordinator = new TimerAlarmCoordinator(
    TimeProvider.System,
    alarmAudioService);
```

`MainViewModel`에는 `alarmCoordinator`를 전달한다. 테스트 Fixture도 다음 순서로 구성한다.

```csharp
Audio = new FakeAlarmAudioService();
AlarmCoordinator = new TimerAlarmCoordinator(Time, Audio);
ViewModel = new MainViewModel(
    Paths,
    new CountdownTimer(Time, settings.Timer1.Duration),
    new CountdownTimer(Time, settings.Timer2.Duration),
    Settings,
    Hotkeys,
    AlarmCoordinator,
    AudioStore,
    installer);
```

`FakeAlarmAudioService`에 `StopCalls`를 추가한다. 실제 서비스와 동일하게 `IsPlaying=false`이면 `Stop`은 아무 작업도 하지 않고, 재생 중일 때만 `StopCalls`를 증가시킨다. 기존 동시 완료 테스트는 메서드 호출 횟수 대신 두 타이머를 차례로 확인했을 때 마지막 확인에서만 중지되는 사용자 결과를 검증하도록 이름과 단언을 교체한다. 기존 지연 완료 테스트와 미리 듣기 경로·시간 테스트는 유지한다.

- [x] **8단계: 설계 문서의 같은 조작 주기 경계 보강**

`docs/superpowers/specs/2026-08-10-timer-owned-alarm-design.md`의 `MainViewModel` 부분에 다음 문장을 추가한다.

```markdown
사용자 조작 내부에서 완료 이벤트가 먼저 발생한 경우에는 같은 타이머 번호를 대기 완료 집합에서 제거한 뒤 확인 처리해, 다음 화면 갱신에서 알람이 다시 등록되지 않게 한다.
```

- [x] **9단계: ViewModel·조정기 테스트 GREEN 확인**

```powershell
dotnet test TalesAlarm.sln -c Release --no-restore --filter "FullyQualifiedName~MainViewModelTests|FullyQualifiedName~TimerAlarmCoordinatorTests|FullyQualifiedName~TimerViewModelTests"
```

예상 결과: 다른 타이머 조작, 두 소유권, 같은 조작 주기, 미리 듣기와 설정 적용 시나리오가 모두 통과한다.

실제 결과: `MainViewModelTests`, `TimerAlarmCoordinatorTests`, `TimerViewModelTests`의 관련 테스트 38개가 모두 통과했다.

- [x] **10단계: 작업 3 커밋**

```powershell
git add src/TalesAlarm/App.xaml.cs src/TalesAlarm/ViewModels/MainViewModel.cs src/TalesAlarm/ViewModels/AlarmSettingsViewModel.cs tests/TalesAlarm.Tests/ViewModels/MainViewModelTests.cs docs/superpowers/specs/2026-08-10-timer-owned-alarm-design.md
git commit -m "feat: acknowledge alarms by timer owner"
```

커밋: `e11682a`

---

### 작업 4: 사용자 문서와 전체 배포 검증

**파일:**
- 수정: `README.md`
- 수정: `docs/superpowers/plans/2026-08-10-timer-owned-alarm.md`

**인터페이스:**
- 사용자 문서에 공통 알람 설정, 해당 타이머 조작 정지, 동시 알람 유지 규칙을 설명한다.
- 최종 산출물은 `artifacts/TalesAlarm-win-x64/TalesAlarm.exe`다.

- [x] **1단계: README 알람 동작 설명 갱신**

`README.md`의 `알람 음원` 절에 다음 내용을 추가한다.

```markdown
- 알람 설정은 두 타이머가 공유하지만 완료 알람은 타이머별로 확인됩니다. 알람을 발생시킨 타이머를 시작·일시정지/재개·초기화하거나 해당 단축키로 조작하면 그 타이머의 알람만 멈춥니다.
- 두 타이머의 완료 알람이 함께 활성화된 경우 한 타이머를 조작해도 다른 타이머의 알람이 남아 있으면 소리는 계속 재생됩니다. 다른 타이머 조작과 미리 듣기는 해당 완료 알람을 중지하지 않습니다.
```

- [x] **2단계: 전체 Release 테스트 실행**

```powershell
dotnet test TalesAlarm.sln -c Release --no-restore
```

예상 결과: 실패 0개이며 기존 설정, 오디오, 단축키, 타이머, WPF 간단 보기 테스트를 포함해 모두 통과한다.

실제 결과: Release 전체 테스트 127개가 실패와 건너뜀 없이 모두 통과했다.

- [x] **3단계: Windows x64 런타임 대상 복원**

```powershell
dotnet restore src/TalesAlarm/TalesAlarm.csproj --runtime win-x64
```

예상 결과: `project.assets.json`에 `net10.0-windows/win-x64` 대상이 생성된다.

실제 결과: 복원에 성공했고 `project.assets.json`에서 `net10.0-windows/win-x64` 대상을 확인했다.

- [x] **4단계: Release 단일 파일 게시**

```powershell
dotnet publish src/TalesAlarm/TalesAlarm.csproj -p:PublishProfile=win-x64 --no-restore
```

예상 결과: `artifacts/TalesAlarm-win-x64/TalesAlarm.exe`가 생성된다.

실제 결과: Release 게시에 성공해 대상 경로에 `TalesAlarm.exe`를 생성했다.

- [x] **5단계: 게시 산출물 실행 검증**

```powershell
powershell -ExecutionPolicy Bypass -File tests/Verify-PublishArtifact.ps1 -PublishDirectory artifacts/TalesAlarm-win-x64
```

예상 결과: 단일 EXE 구성과 실행 검사가 통과한다.

실제 결과: 기존 실행 인스턴스와의 최초 충돌을 확인·해소한 뒤 다시 실행해, 외부 런타임 파일 없는 173,204,553바이트 단일 EXE가 3초 이상 실행 상태를 유지함을 검증했다.

- [x] **6단계: 문서·작업 트리 검사**

```powershell
git diff --check
git status --short
```

예상 결과: 공백 오류가 없고, 의도한 소스·테스트·문서 파일만 변경되어 있다. `artifacts/`, `.dotnet-cli/`, `bin/`, `obj/`는 추적되지 않는다.

실제 결과: `git diff --check`가 통과했고 README와 실행 계획만 미커밋 상태이며, 게시 EXE는 `.gitignore`의 `artifacts/` 규칙으로 제외됨을 확인했다.

- [x] **7단계: 계획의 실행 결과 기록**

이 계획의 완료된 체크박스를 `[x]`로 바꾸고 각 RED/GREEN, 전체 테스트 수, 게시 산출물 검증 결과를 해당 단계 아래에 실제 출력 기준으로 기록한다.

- [x] **8단계: 작업 4 커밋**

```powershell
git add README.md docs/superpowers/plans/2026-08-10-timer-owned-alarm.md
git commit -m "docs: explain timer-owned alarm acknowledgement"
```

- [x] **9단계: 완료 전 최종 검토**

`superpowers:requesting-code-review`로 설계 대비 구현과 테스트 누락을 검토하고, 지적 사항을 반영한 뒤 `superpowers:verification-before-completion`으로 전체 테스트와 게시 검증을 새로 실행한다. 그 다음 `superpowers:finishing-a-development-branch`로 병합·PR·브랜치 유지 중 사용자 선택을 받는다.

실제 결과: 독립 코드 검토에서 치명적·중요 문제가 없고 병합 준비 완료 판정을 받았다. 최종 Release 테스트 127개를 다시 통과하고 단일 EXE를 다시 게시한 뒤, 173,204,553바이트 산출물의 실행 검증도 통과했다. 사용자는 `master` 로컬 병합을 선택했다.

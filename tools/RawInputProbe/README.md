# Raw Input Probe (진단 전용)

테일즈위버가 포그라운드일 때 키보드 Raw Input이 Tales Alarm 같은 백그라운드 창에 **도달하는지**를 계측하는 일회성 진단 도구입니다.

- 제품(`TalesAlarm.exe`)에 포함되지 않으며 `TalesAlarm.sln`에도 들어 있지 않습니다.
- Tales Alarm 본체와 **같은 방식**으로 창을 만들고(`Window` → `EnsureHandle()` → `HwndSource.AddHook`) 같은 플래그로 등록합니다. 재현성을 위해 이 구조를 바꾸지 마세요.
- 키보드 훅 설치, 입력 주입·차단, 게임 프로세스 접근을 하지 않습니다. 포그라운드 프로세스 이름도 조회하지 않습니다.
- 키 식별자를 로그에 남기므로 **진단이 끝나면 로그를 삭제**하세요.

## 빌드

```powershell
dotnet build tools/RawInputProbe/RawInputProbe.csproj -c Release
```

실행 파일: `tools\RawInputProbe\bin\Release\net10.0-windows\RawInputProbe.exe`

## 실행

한 번에 한 모드만 실행합니다.

```powershell
.\tools\RawInputProbe\bin\Release\net10.0-windows\RawInputProbe.exe --mode sink
.\tools\RawInputProbe\bin\Release\net10.0-windows\RawInputProbe.exe --mode exsink
.\tools\RawInputProbe\bin\Release\net10.0-windows\RawInputProbe.exe --mode poll
```

| 모드 | 방식 | 확인하려는 것 |
|------|------|---------------|
| `sink` | `RIDEV_INPUTSINK \| RIDEV_DEVNOTIFY` (Tales Alarm 현재 구현과 동일) | 지금 구현이 게임 활성 중에도 입력을 받는가 |
| `exsink` | 위 + `RIDEV_EXINPUTSINK` | 포그라운드 앱이 같은 장치를 Raw Input으로 소비할 때도 받는가 |
| `poll` | Raw Input 미등록, 15ms `GetAsyncKeyState` 폴링 | Raw Input이 막혔을 때 남는 관찰 채널이 있는가 |

로그: `%LocalAppData%\TalesAlarmProbe\probe-<모드>-<날짜>-<시각>.log`

**감시 키(F1~F12, Insert, Pause, Q)의 키다운을 받으면 비프음이 납니다.** 전체 화면 게임에서는 화면을 볼 수 없으므로 소리가 실시간 신호입니다. 소리가 나면 도달한 것, 안 나면 안 온 것입니다.

로그의 `HEARTBEAT` 줄은 1초마다 찍힙니다. **"입력이 안 온다"와 "프로브가 멈췄다"를 구분하는 줄**이므로 해석할 때 반드시 확인하세요.

## 테일즈위버 없이 하는 실험 (게임을 못 켤 때)

`tools/ForegroundAntagonist`가 "포그라운드 앱이 키를 가로채는" 후보 메커니즘을 이 PC에서 재현합니다.
게임 없이도 **우리 등록 방식이 그 상황에서 살아남는지**를 판정할 수 있습니다.

실행 순서 (한 조합당 30초, 총 5조합):

1. 프로브를 먼저 켭니다. `RawInputProbe.exe --mode sink`
2. 상대역을 켜고 그 창을 클릭해 활성화합니다. `ForegroundAntagonist.exe --mode <none|nolegacy|llhook>`
3. `F5`를 3번 누릅니다. **프로브에서 비프음이 나면 도달, 안 나면 차단.**
4. 상대역을 Esc로 닫습니다.

| 프로브 | 상대역 | 의미 |
|--------|--------|------|
| `sink` | `none` | 대조군. 반드시 비프음이 나야 합니다. |
| `sink` | `nolegacy` | 포그라운드 앱이 `RIDEV_NOLEGACY \| RIDEV_NOHOTKEYS`로 키보드를 독점할 때 |
| `sink` | `llhook` | 포그라운드 앱이 저수준 훅으로 F1~F12를 삼킬 때 |
| `exsink` | `nolegacy` | 위 상황에서 `RIDEV_EXINPUTSINK`가 도움이 되는지 |
| `exsink` | `llhook` | 위 상황에서 `RIDEV_EXINPUTSINK`가 도움이 되는지 |

### 2026-08-12 실행 결과

`tools/Run-InputExperiment.ps1`로 실행. 판정은 대조키 `Q`와 프로브 로그로 했다.

| # | 프로브 | 상대역 | F5 | Q | 판정 |
|---|--------|--------|----|----|------|
| 1 | `sink` | `none` | 3 | 1 | 도달 |
| 2 | `sink` | `nolegacy` | 3 | 1 | 도달 |
| 3 | `sink` | `llhook` | **0** | 1 | **F5만 차단** |
| 4 | `exsink` | `nolegacy` | 0 | **0** | **전부 차단** |
| 5 | `exsink` | `llhook` | 0 | 1 | F5만 차단 |
| 6 | `poll` | `none` | 3 | 1 | 도달 |
| 7 | `poll` | `llhook` | **0** | 1 | **F5만 차단** |

확정된 세 가지:

1. **포그라운드 앱이 저수준 키보드 훅으로 F키를 삼키면 Raw Input으로도 그 키를 볼 수 없다.**
   3번에서 프로브는 하트비트 11개로 살아 있었고 대조키 `Q`는 받았는데 `F5`만 0개였다.
   즉 "우리가 못 받은 것"이지 "사용자가 안 누른 것"이 아니다.

2. **`RIDEV_EXINPUTSINK`는 넣으면 안 된다.** 이 플래그는 수신 범위를 넓히는 게 아니라
   "포그라운드 앱이 그 입력을 쓰지 않을 때만 받겠다"고 **좁히는** 플래그다.
   4번에서 상대역이 키보드 Raw Input을 등록하자 우리 쪽 수신이 통째로 0이 됐다(`kbd=0`).
   5번에서 `Q`가 도달한 것으로 보아 `exsink` 자체가 죽은 것은 아니다.
   제품 등록 플래그는 현재의 `RIDEV_INPUTSINK | RIDEV_DEVNOTIFY`가 셋 중 최선이다.

3. **폴링(`GetAsyncKeyState`)도 같은 방식으로 막힌다.** 7번에서 대조키 `Q`는 잡혔지만 `F5`는 0개였다.
   Raw Input과 완전히 다른 경로인데도 결과가 같다. 즉 저수준 훅이 키를 삼키면
   **사용자 모드에 남는 관찰 경로가 없다.**

`nolegacy` 상대역은 현재 구현(`sink`)을 막지 못했으므로 이 메커니즘은 원인 후보에서 빠진다.

결론: 이 종류의 가로채기가 원인이라면, 설계 문서 §10의 경계(훅·주입·게임 접근 금지)를 지키는 한
제품에서 고칠 수 있는 것이 없다. 남은 확인은 "테일즈위버가 실제로 이렇게 하는가"와
"게임이 쓰지 않는 키는 통과하는가" 두 가지이며, 둘 다 게임 실행이 필요하다.

**주의:** 이것은 *가설로 세운 메커니즘*을 재현한 실험이지 테일즈위버 자체를 시험한 것이 아닙니다.
여기서 나온 수정은 **잠정**이며, 게임을 켤 수 있을 때 위의 정식 절차로 확인해야 확정됩니다.

## 측정 절차 (테일즈위버를 켤 수 있을 때)

각 단계의 시작 시각을 메모해 두면 로그 대조가 쉽습니다.

### 0단계 — 코드 없이 하는 사전 판별 (먼저)

1. 실행 중인 TalesAlarm이 없는지 확인한 뒤 `artifacts\TalesAlarm-win-x64\TalesAlarm.exe`를 실행합니다.
   (단일 인스턴스라 예전 프로세스가 트레이에 남아 있으면 새 EXE가 조용히 종료되고 **옛 빌드가 계속 돕니다.**)
2. 타이머 1 단축키를 게임이 쓰지 않는 키(`Insert`, `Pause`, `Ctrl+F5` 중 하나)로 저장합니다.
3. 테일즈위버(창 모드)를 활성화하고 그 키를 누릅니다.

- 타이머가 **동작하면** → 게임/보안 모듈이 F1~F12만 선점하는 것이고 Raw Input 경로는 살아 있습니다.
- 타이머가 **동작 안 하면** → 게임이 포그라운드인 동안 모든 키 관찰이 막힙니다.

### 1단계 — 기준선 (`--mode sink`)

프로브를 실행하고 **메모장을 활성화한 상태에서** `F5`를 3번 누릅니다.
로그에 `KBD ... vk=0x74(F5)` 줄이 남고 비프음이 나야 합니다.
여기서 안 나오면 게임과 무관한 문제이므로 즉시 멈추고 그 지점을 조사합니다.

### 2단계 — 테일즈위버 창 모드

게임을 활성화하고 **5초 간격**으로:

`F5`×3 → `F8`×3 → `Insert`×3 → `Q`×3

각각 비프음이 나는지 기억해 둡니다.

### 3단계 — 전체 화면 모드

같은 순서를 반복합니다.

### 4단계 — 복귀 확인

Alt+Tab으로 바탕화면에 나와 `F5`×3. 다시 잡히면 프로브가 죽은 게 아니라 게임 활성 중에만 막혔다는 증거입니다.

### 5~6단계

`--mode exsink`, `--mode poll`로 2~4단계를 각각 반복합니다.

### 7단계 — 대조군

`--mode sink`를 **관리자 권한으로** 실행해 2단계만 반복합니다.

## 결과 전달

`%LocalAppData%\TalesAlarmProbe\` 아래 로그 파일들과 "어느 모드에서 비프음이 났는지"를 알려주시면 원인을 특정할 수 있습니다.

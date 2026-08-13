# Foreground Antagonist (진단 전용)

포그라운드 앱이 키 입력을 가로채는 **후보 메커니즘을 이 PC에서 재현**하는 테스트 상대역입니다.
테일즈위버를 실행할 수 없을 때 `RawInputProbe`와 짝지어 원인을 좁히는 데 씁니다.

전체 실험 절차는 `tools/RawInputProbe/README.md`의 "테일즈위버 없이 하는 실험" 절을 보세요.

## 모드

| 모드 | 재현하는 상황 |
|------|---------------|
| `none` | 대조군. 아무것도 가로채지 않는 평범한 창 |
| `nolegacy` | 포그라운드 앱이 키보드를 `RIDEV_NOLEGACY \| RIDEV_NOHOTKEYS`로 독점 등록 |
| `llhook` | 포그라운드 앱이 저수준 키보드 훅으로 F1~F12를 삼킴 |

```powershell
dotnet build tools/ForegroundAntagonist/ForegroundAntagonist.csproj -c Release
.\tools\ForegroundAntagonist\bin\Release\net10.0-windows\ForegroundAntagonist.exe --mode nolegacy
```

## 안전 장치

- **이 창이 활성일 때만** 가로챕니다. 다른 창으로 옮기면 즉시 멈춥니다.
- `Esc`로 즉시 종료됩니다.
- **3분 뒤 자동 종료**되며, 종료 시 등록 해제와 훅 해제를 반드시 수행합니다.
- 프로세스를 강제 종료해도 Windows가 Raw Input 등록과 훅을 자동 정리합니다.
- `llhook` 모드는 F1~F12만 삼킵니다. 다른 키는 건드리지 않습니다.

## 경계

- 게임이나 다른 프로세스에 접근하지 않습니다. 입력을 합성하지 않습니다.
- 제품(`TalesAlarm.exe`)에 포함되지 않으며 `TalesAlarm.sln`에도 들어 있지 않습니다.
  제품 소스의 금지 API 검사(`rg ... src tests`) 범위 밖인 `tools/`에만 존재합니다.
- 진단이 끝나면 이 도구와 로그를 정리하세요.

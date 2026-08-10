# Raw Input 전체 패스스루 단축키 구현 계획

> **에이전트 작업자용:** 필수 하위 스킬로 `superpowers:subagent-driven-development`(권장) 또는 `superpowers:executing-plans`를 사용해 이 계획을 작업별로 실행한다. 진행 상태는 체크박스(`- [ ]`)로 기록한다.

**목표:** `RegisterHotKey` 기반 전역 단축키를 Raw Input 관찰 방식으로 교체해, Tales Alarm과 현재 활성 프로그램이 같은 물리 키 입력에 동시에 반응하게 한다.

**구조:** `Win32RawInputNativeApi`가 키보드 장치 클래스 등록과 `WM_INPUT` 패킷 해석만 담당하고, `RawKeyboardState`가 장치별 눌림·수정키·자동 반복 상태를 순수 메모리에서 관리한다. `GlobalHotkeyService`는 기존 바인딩·캡처 임대·`HotkeyPressed` 계약을 유지하면서 두 구성 요소를 조정하며, WPF 메시지 훅은 `lParam`을 전달하되 메시지를 처리 완료로 표시하지 않는다.

**기술 스택:** C# 14, .NET 10, WPF, Win32 Raw Input, xUnit

## 전역 제약

- 모든 사용자 지정 단축키는 Raw Input으로 처리하며 기존 단축키 UI와 설정 JSON 스키마를 변경하지 않는다.
- 키보드 사용 페이지 `0x01`, 사용 ID `0x06`을 `RIDEV_INPUTSINK(0x00000100) | RIDEV_DEVNOTIFY(0x00002000)`로 한 번 등록한다.
- `WM_INPUT(0x00FF)`의 `lParam`에서 키보드 패킷을 읽고 `WM_INPUT_DEVICE_CHANGE(0x00FE)`의 제거 알림으로 장치 상태를 정리한다.
- `RIDEV_NOLEGACY`, `RIDEV_NOHOTKEYS`, `SetWindowsHookEx`, `SendInput`, `keybd_event`, 입력 재생·차단·주입을 사용하지 않는다.
- 테일즈위버와 게임 보안 프로세스의 프로세스·메모리·창·설치 파일에 접근하지 않는다.
- 수정키 집합은 정확히 일치해야 하며, 장치별 최초 키다운만 전달하고 키업 후 재무장한다.
- 좌우 Ctrl, Alt, Shift, Windows 키는 저장용 수정키 플래그로 합치고, 여러 키보드의 현재 수정키 상태는 합산한다.
- 단축키 캡처 중에도 Raw Input 상태는 계속 수신하지만 타이머 이벤트는 억제하며, 최종 캡처 임대 종료 시 눌림 상태를 초기화한다.
- 설정 적용, 키보드 장치 제거, Raw Input 등록, 서비스 종료 시 관련 눌림 상태를 초기화한다.
- Raw Input 등록 실패는 앱 시작 실패로 승격하지 않는다. 단축키만 비활성화하고 Win32 오류 코드를 사용자 메시지와 진단 로그에 남기며 `RegisterHotKey`로 대체하지 않는다.
- 일치하지 않은 키, 입력 문자·연속 내용, 장치 이름은 로그나 설정에 저장하지 않는다.
- 화면 버튼, 타이머 상태 전이, 재입력 정책, 알람, 트레이 숨김과 창 수명은 변경하지 않는다.
- 게시 대상은 일반 사용자 권한의 Windows x64 단일 실행 파일이다.

---

### 작업 1: Raw Input 네이티브 경계 추가

**파일:**
- 생성: `src/TalesAlarm/Hotkeys/RawInputNativeApi.cs`
- 수정: `src/TalesAlarm/AssemblyInfo.cs`
- 생성: `tests/TalesAlarm.Tests/Hotkeys/RawInputNativeApiTests.cs`

**인터페이스:**
- 생성: `IRawInputNativeApi.TryRegisterKeyboard(nint, out int)`
- 생성: `IRawInputNativeApi.TryUnregisterKeyboard(out int)`
- 생성: `IRawInputNativeApi.ReadKeyboard(nint)`
- 생성: `RawKeyboardInput`, `RawKeyboardFlags`, `RawInputReadStatus`, `RawInputReadResult`
- 생성: `Win32RawInputNativeApi`
- 이 작업에서는 현재 서비스를 빌드 가능한 상태로 유지하기 위해 기존 `IHotkeyNativeApi` 파일을 보존하고, 작업 3에서 서비스와 함께 제거한다.

- [ ] **1단계: 등록 플래그와 실제 창 등록 수명 테스트 작성**

`AssemblyInfo.cs`에 테스트 어셈블리에서 내부 등록 플래그를 볼 수 있게 다음 특성을 추가한다.

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TalesAlarm.Tests")]
```

새 `RawInputNativeApiTests`에 패스스루 플래그 계약과 실제 Windows 창 핸들 등록·해제를 검증한다.

```csharp
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Interop;
using TalesAlarm.Hotkeys;

namespace TalesAlarm.Tests.Hotkeys;

[Collection("Raw input integration")]
public sealed class RawInputNativeApiTests
{
    [Fact]
    public void KeyboardRegistrationFlags_EnableBackgroundAndDeviceChangeOnly()
    {
        Assert.Equal(
            RawInputDeviceFlags.InputSink | RawInputDeviceFlags.DeviceNotify,
            Win32RawInputNativeApi.KeyboardRegistrationFlags);
        Assert.False(Win32RawInputNativeApi.KeyboardRegistrationFlags
            .HasFlag(RawInputDeviceFlags.NoLegacy));
        Assert.False(Win32RawInputNativeApi.KeyboardRegistrationFlags
            .HasFlag(RawInputDeviceFlags.NoHotkeys));
    }

    [Fact]
    public void RegisterAndUnregisterKeyboard_WithRealWindowHandle_Succeeds()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var source = new HwndSource(new HwndSourceParameters(
                    "TalesAlarm.RawInputNativeApiTests")
                {
                    Width = 1,
                    Height = 1,
                });
                var api = new Win32RawInputNativeApi();

                Assert.True(
                    api.TryRegisterKeyboard(source.Handle, out var registerError),
                    $"Raw Input registration failed: {registerError}");
                Assert.True(
                    api.TryUnregisterKeyboard(out var unregisterError),
                    $"Raw Input removal failed: {unregisterError}");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}

[CollectionDefinition("Raw input integration", DisableParallelization = true)]
public sealed class RawInputIntegrationCollection
{
}
```

- [ ] **2단계: 네이티브 경계 집중 테스트를 실행해 RED 확인**

```powershell
dotnet test TalesAlarm.sln -c Release --filter "FullyQualifiedName~RawInputNativeApiTests"
```

예상 결과: `IRawInputNativeApi`, `RawInputDeviceFlags`, `Win32RawInputNativeApi`가 없어 컴파일이 실패한다.

- [ ] **3단계: Raw Input 공개 이벤트 계약 구현**

`RawInputNativeApi.cs`의 공개 계약을 다음과 같이 만든다. `RawKeyboardInput`에는 패킷 해석 이후 상태 관리에 필요한 값만 남긴다.

```csharp
using System.Runtime.InteropServices;

namespace TalesAlarm.Hotkeys;

[Flags]
public enum RawKeyboardFlags : ushort
{
    None = 0,
    Break = 0x0001,
    E0 = 0x0002,
    E1 = 0x0004,
}

public readonly record struct RawKeyboardInput(
    nint DeviceHandle,
    ushort VirtualKey,
    ushort MakeCode,
    RawKeyboardFlags Flags)
{
    public bool IsKeyUp => (Flags & RawKeyboardFlags.Break) != 0;
}

public enum RawInputReadStatus
{
    Keyboard,
    Ignored,
    Failed,
}

public readonly record struct RawInputReadResult(
    RawInputReadStatus Status,
    RawKeyboardInput Keyboard,
    int ErrorCode)
{
    public static RawInputReadResult FromKeyboard(RawKeyboardInput keyboard) =>
        new(RawInputReadStatus.Keyboard, keyboard, 0);

    public static RawInputReadResult Ignored() =>
        new(RawInputReadStatus.Ignored, default, 0);

    public static RawInputReadResult Failed(int errorCode) =>
        new(RawInputReadStatus.Failed, default, errorCode);
}

public interface IRawInputNativeApi
{
    bool TryRegisterKeyboard(nint windowHandle, out int errorCode);

    bool TryUnregisterKeyboard(out int errorCode);

    RawInputReadResult ReadKeyboard(nint rawInputHandle);
}
```

- [ ] **4단계: Win32 등록·해제와 구조체 선언 구현**

같은 파일에 등록 플래그, 네이티브 구조체와 P/Invoke를 추가한다. 제거 요청에서는 Microsoft 계약에 맞게 `TargetWindow=0`을 사용한다.

```csharp
[Flags]
internal enum RawInputDeviceFlags : uint
{
    Remove = 0x00000001,
    NoLegacy = 0x00000030,
    InputSink = 0x00000100,
    NoHotkeys = 0x00000200,
    DeviceNotify = 0x00002000,
}

[StructLayout(LayoutKind.Sequential)]
internal struct RawInputDevice
{
    public ushort UsagePage;
    public ushort Usage;
    public RawInputDeviceFlags Flags;
    public nint TargetWindow;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RawInputHeader
{
    public uint Type;
    public uint Size;
    public nint Device;
    public nuint WParam;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RawKeyboard
{
    public ushort MakeCode;
    public RawKeyboardFlags Flags;
    public ushort Reserved;
    public ushort VirtualKey;
    public uint Message;
    public uint ExtraInformation;
}

public sealed class Win32RawInputNativeApi : IRawInputNativeApi
{
    private const ushort GenericDesktopUsagePage = 0x01;
    private const ushort KeyboardUsage = 0x06;
    private const uint RidInput = 0x10000003;
    private const uint RimTypeKeyboard = 1;
    private const uint NativeError = uint.MaxValue;
    private const int ErrorInvalidData = 13;

    internal const RawInputDeviceFlags KeyboardRegistrationFlags =
        RawInputDeviceFlags.InputSink | RawInputDeviceFlags.DeviceNotify;

    public bool TryRegisterKeyboard(nint windowHandle, out int errorCode)
    {
        if (windowHandle == 0)
        {
            throw new ArgumentException("창 핸들은 0일 수 없습니다.", nameof(windowHandle));
        }

        var device = CreateDevice(KeyboardRegistrationFlags, windowHandle);
        var success = RawInputNativeMethods.RegisterRawInputDevices(
            ref device,
            1,
            checked((uint)Marshal.SizeOf<RawInputDevice>()));
        errorCode = success ? 0 : Marshal.GetLastWin32Error();
        return success;
    }

    public bool TryUnregisterKeyboard(out int errorCode)
    {
        var device = CreateDevice(RawInputDeviceFlags.Remove, 0);
        var success = RawInputNativeMethods.RegisterRawInputDevices(
            ref device,
            1,
            checked((uint)Marshal.SizeOf<RawInputDevice>()));
        errorCode = success ? 0 : Marshal.GetLastWin32Error();
        return success;
    }

    private static RawInputDevice CreateDevice(
        RawInputDeviceFlags flags,
        nint targetWindow) =>
        new()
        {
            UsagePage = GenericDesktopUsagePage,
            Usage = KeyboardUsage,
            Flags = flags,
            TargetWindow = targetWindow,
        };
}

internal static partial class RawInputNativeMethods
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterRawInputDevices(
        ref RawInputDevice devices,
        uint deviceCount,
        uint deviceSize);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint GetRawInputData(
        nint rawInputHandle,
        uint command,
        nint data,
        ref uint dataSize,
        uint headerSize);
}
```

- [ ] **5단계: `GetRawInputData`의 두 단계 읽기와 검증 구현**

`Win32RawInputNativeApi.ReadKeyboard`는 먼저 크기를 질의하고 정렬된 비관리 버퍼를 할당한 뒤 실제 데이터를 읽는다. API 실패는 `GetLastWin32Error`, 짧거나 모순된 패킷은 `ERROR_INVALID_DATA(13)`, 키보드가 아닌 패킷은 `Ignored`로 반환한다.

```csharp
public RawInputReadResult ReadKeyboard(nint rawInputHandle)
{
    var headerSize = checked((uint)Marshal.SizeOf<RawInputHeader>());
    var keyboardSize = checked((uint)Marshal.SizeOf<RawKeyboard>());
    uint requiredSize = 0;
    var queryResult = RawInputNativeMethods.GetRawInputData(
        rawInputHandle,
        RidInput,
        0,
        ref requiredSize,
        headerSize);
    if (queryResult == NativeError)
    {
        return RawInputReadResult.Failed(Marshal.GetLastWin32Error());
    }

    if (queryResult != 0
        || requiredSize < headerSize
        || requiredSize > (uint)int.MaxValue)
    {
        return RawInputReadResult.Failed(ErrorInvalidData);
    }

    var buffer = Marshal.AllocHGlobal(checked((int)requiredSize));
    try
    {
        var copiedSize = requiredSize;
        var copied = RawInputNativeMethods.GetRawInputData(
            rawInputHandle,
            RidInput,
            buffer,
            ref copiedSize,
            headerSize);
        if (copied == NativeError)
        {
            return RawInputReadResult.Failed(Marshal.GetLastWin32Error());
        }

        if (copied != copiedSize || copied < headerSize)
        {
            return RawInputReadResult.Failed(ErrorInvalidData);
        }

        var header = Marshal.PtrToStructure<RawInputHeader>(buffer);
        if (header.Size != copied)
        {
            return RawInputReadResult.Failed(ErrorInvalidData);
        }

        if (header.Type != RimTypeKeyboard)
        {
            return RawInputReadResult.Ignored();
        }

        if (copied < headerSize + keyboardSize)
        {
            return RawInputReadResult.Failed(ErrorInvalidData);
        }

        var keyboard = Marshal.PtrToStructure<RawKeyboard>(
            nint.Add(buffer, checked((int)headerSize)));
        const RawKeyboardFlags knownFlags =
            RawKeyboardFlags.Break | RawKeyboardFlags.E0 | RawKeyboardFlags.E1;
        if (keyboard.Reserved != 0
            || (keyboard.Flags & ~knownFlags) != RawKeyboardFlags.None)
        {
            return RawInputReadResult.Failed(ErrorInvalidData);
        }

        return RawInputReadResult.FromKeyboard(new(
            header.Device,
            keyboard.VirtualKey,
            keyboard.MakeCode,
            keyboard.Flags));
    }
    finally
    {
        Marshal.FreeHGlobal(buffer);
    }
}
```

- [ ] **6단계: 네이티브 경계 테스트 GREEN 확인**

```powershell
dotnet test TalesAlarm.sln -c Release --no-restore --filter "FullyQualifiedName~RawInputNativeApiTests"
```

예상 결과: 패스스루 플래그 단언과 실제 창 핸들 등록·해제 테스트가 모두 통과한다.

- [ ] **7단계: 작업 1 커밋**

```powershell
git add src/TalesAlarm/AssemblyInfo.cs src/TalesAlarm/Hotkeys/RawInputNativeApi.cs tests/TalesAlarm.Tests/Hotkeys/RawInputNativeApiTests.cs
git commit -m "feat: add raw input native boundary"
```

---

### 작업 2: 장치별 키 상태와 제스처 정규화 구현

**파일:**
- 생성: `src/TalesAlarm/Hotkeys/RawKeyboardState.cs`
- 생성: `tests/TalesAlarm.Tests/Hotkeys/RawKeyboardStateTests.cs`

**인터페이스:**
- 생성: `RawKeyboardState.TryCreateGesture(RawKeyboardInput, out HotkeyGesture)`
- 생성: `RawKeyboardState.RemoveDevice(nint)`
- 생성: `RawKeyboardState.Clear()`
- 소비: 기존 `HotkeyGesture`, `HotkeyModifiers`, WPF `KeyInterop`

- [ ] **1단계: 최초 키다운·자동 반복·재무장 실패 테스트 작성**

```csharp
using System.Windows.Input;
using TalesAlarm.Hotkeys;

namespace TalesAlarm.Tests.Hotkeys;

public sealed class RawKeyboardStateTests
{
    [Fact]
    public void TryCreateGesture_RepeatedDownRaisesOnceAndKeyUpRearms()
    {
        var state = new RawKeyboardState();
        var down = Input(1, 0x71, 0x3C); // F2

        Assert.True(state.TryCreateGesture(down, out var first));
        Assert.Equal(new HotkeyGesture(Key.F2, HotkeyModifiers.None), first);
        Assert.False(state.TryCreateGesture(down, out _));
        Assert.False(state.TryCreateGesture(
            down with { Flags = RawKeyboardFlags.Break },
            out _));
        Assert.True(state.TryCreateGesture(down, out var second));
        Assert.Equal(first, second);
    }

    private static RawKeyboardInput Input(
        nint device,
        ushort virtualKey,
        ushort makeCode,
        RawKeyboardFlags flags = RawKeyboardFlags.None) =>
        new(device, virtualKey, makeCode, flags);
}
```

- [ ] **2단계: 수정키 통합·다중 장치·정확한 집합 실패 테스트 작성**

같은 테스트 클래스에 좌우 수정키 통합과 장치 간 합산을 추가한다.

```csharp
[Theory]
[InlineData(0x10, HotkeyModifiers.Shift)]
[InlineData(0xA0, HotkeyModifiers.Shift)]
[InlineData(0xA1, HotkeyModifiers.Shift)]
[InlineData(0x11, HotkeyModifiers.Control)]
[InlineData(0xA2, HotkeyModifiers.Control)]
[InlineData(0xA3, HotkeyModifiers.Control)]
[InlineData(0x12, HotkeyModifiers.Alt)]
[InlineData(0xA4, HotkeyModifiers.Alt)]
[InlineData(0xA5, HotkeyModifiers.Alt)]
[InlineData(0x5B, HotkeyModifiers.Windows)]
[InlineData(0x5C, HotkeyModifiers.Windows)]
public void TryCreateGesture_CollapsesModifierVirtualKeys(
    ushort modifierVirtualKey,
    HotkeyModifiers expected)
{
    var state = new RawKeyboardState();
    Assert.False(state.TryCreateGesture(
        Input(1, modifierVirtualKey, 0x1D),
        out _));

    Assert.True(state.TryCreateGesture(Input(1, 0x71, 0x3C), out var gesture));
    Assert.Equal(new HotkeyGesture(Key.F2, expected), gesture);
}

[Fact]
public void TryCreateGesture_AggregatesModifiersAcrossDevices()
{
    var state = new RawKeyboardState();
    state.TryCreateGesture(Input(1, 0x11, 0x1D), out _); // Ctrl
    state.TryCreateGesture(Input(2, 0x12, 0x38), out _); // Alt

    Assert.True(state.TryCreateGesture(Input(3, 0x71, 0x3C), out var gesture));
    Assert.Equal(
        new HotkeyGesture(
            Key.F2,
            HotkeyModifiers.Control | HotkeyModifiers.Alt),
        gesture);
}
```

- [ ] **3단계: 장치 제거·전체 초기화·키 매핑 실패 테스트 작성**

```csharp
[Fact]
public void RemoveDevice_ClearsOnlyRemovedDeviceState()
{
    var state = new RawKeyboardState();
    state.TryCreateGesture(Input(1, 0x11, 0x1D), out _);
    state.TryCreateGesture(Input(2, 0x12, 0x38), out _);

    state.RemoveDevice(1);

    Assert.True(state.TryCreateGesture(Input(3, 0x71, 0x3C), out var gesture));
    Assert.Equal(HotkeyModifiers.Alt, gesture.Modifiers);
}

[Fact]
public void Clear_DropsRepeatAndModifierState()
{
    var state = new RawKeyboardState();
    state.TryCreateGesture(Input(1, 0x11, 0x1D), out _);
    state.TryCreateGesture(Input(1, 0x71, 0x3C), out _);

    state.Clear();

    Assert.True(state.TryCreateGesture(Input(1, 0x71, 0x3C), out var gesture));
    Assert.Equal(HotkeyModifiers.None, gesture.Modifiers);
}

[Theory]
[InlineData(0x41, Key.A)]
[InlineData(0x70, Key.F1)]
[InlineData(0xBA, Key.OemSemicolon)]
public void TryCreateGesture_MapsVirtualKeysToWpfKeys(
    ushort virtualKey,
    Key expected)
{
    var state = new RawKeyboardState();

    Assert.True(state.TryCreateGesture(Input(1, virtualKey, 0x20), out var gesture));
    Assert.Equal(expected, gesture.Key);
}

[Theory]
[InlineData(0x00, 0x00)]
[InlineData(0xFF, 0x00)]
[InlineData(0x41, 0xFF)]
public void TryCreateGesture_UnknownOrOverrunInputIsIgnored(
    ushort virtualKey,
    ushort makeCode)
{
    var state = new RawKeyboardState();

    Assert.False(state.TryCreateGesture(
        Input(1, virtualKey, makeCode),
        out _));
}
```

- [ ] **4단계: 상태 집중 테스트를 실행해 RED 확인**

```powershell
dotnet test TalesAlarm.sln -c Release --no-restore --filter "FullyQualifiedName~RawKeyboardStateTests"
```

예상 결과: `RawKeyboardState`가 없어 컴파일이 실패한다.

- [ ] **5단계: 장치별 물리 키 식별과 정규화 구현**

`RawKeyboardState.cs`를 다음 책임으로 구현한다. 물리 키 식별자에는 `Break`를 제외한 확장 플래그를 포함해 좌우 키와 자동 반복을 안정적으로 구분한다.

```csharp
using System.Windows.Input;

namespace TalesAlarm.Hotkeys;

internal sealed class RawKeyboardState
{
    private const ushort KeyboardOverrunMakeCode = 0x00FF;
    private readonly Dictionary<nint, HashSet<RawKeyIdentity>> pressedByDevice = [];

    public bool TryCreateGesture(
        RawKeyboardInput input,
        out HotkeyGesture gesture)
    {
        gesture = default;
        if (!TryNormalize(input, out var identity, out var key, out var modifier))
        {
            return false;
        }

        if (input.IsKeyUp)
        {
            Release(input.DeviceHandle, identity);
            return false;
        }

        if (!pressedByDevice.TryGetValue(input.DeviceHandle, out var pressed))
        {
            pressed = [];
            pressedByDevice.Add(input.DeviceHandle, pressed);
        }

        if (!pressed.Add(identity) || modifier != HotkeyModifiers.None)
        {
            return false;
        }

        gesture = new HotkeyGesture(key, GetActiveModifiers());
        return gesture.HasNonModifierKey;
    }

    public void RemoveDevice(nint deviceHandle) =>
        pressedByDevice.Remove(deviceHandle);

    public void Clear() => pressedByDevice.Clear();

    private void Release(nint deviceHandle, RawKeyIdentity identity)
    {
        if (!pressedByDevice.TryGetValue(deviceHandle, out var pressed))
        {
            return;
        }

        pressed.Remove(identity);
        if (pressed.Count == 0)
        {
            pressedByDevice.Remove(deviceHandle);
        }
    }

    private HotkeyModifiers GetActiveModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        foreach (var identity in pressedByDevice.Values.SelectMany(keys => keys))
        {
            modifiers |= GetModifier(identity.VirtualKey);
        }

        return modifiers;
    }

    private static bool TryNormalize(
        RawKeyboardInput input,
        out RawKeyIdentity identity,
        out Key key,
        out HotkeyModifiers modifier)
    {
        identity = new(
            input.VirtualKey,
            input.MakeCode,
            input.Flags & (RawKeyboardFlags.E0 | RawKeyboardFlags.E1));
        key = Key.None;
        modifier = HotkeyModifiers.None;

        if (input.VirtualKey is 0 or 0x00FF
            || input.MakeCode == KeyboardOverrunMakeCode)
        {
            return false;
        }

        modifier = GetModifier(input.VirtualKey);
        if (modifier != HotkeyModifiers.None)
        {
            return true;
        }

        key = KeyInterop.KeyFromVirtualKey(input.VirtualKey);
        return new HotkeyGesture(key, HotkeyModifiers.None).HasNonModifierKey;
    }

    private static HotkeyModifiers GetModifier(ushort virtualKey) =>
        virtualKey switch
        {
            0x10 or 0xA0 or 0xA1 => HotkeyModifiers.Shift,
            0x11 or 0xA2 or 0xA3 => HotkeyModifiers.Control,
            0x12 or 0xA4 or 0xA5 => HotkeyModifiers.Alt,
            0x5B or 0x5C => HotkeyModifiers.Windows,
            _ => HotkeyModifiers.None,
        };

    private readonly record struct RawKeyIdentity(
        ushort VirtualKey,
        ushort MakeCode,
        RawKeyboardFlags ExtensionFlags);
}
```

- [ ] **6단계: 상태 테스트 GREEN 확인**

```powershell
dotnet test TalesAlarm.sln -c Release --no-restore --filter "FullyQualifiedName~RawKeyboardStateTests"
```

예상 결과: 반복, 재무장, 좌우 수정키 통합, 다중 장치 합산, 장치 제거, 초기화, 문자·OEM 키 매핑 테스트가 모두 통과한다.

- [ ] **7단계: 작업 2 커밋**

```powershell
git add src/TalesAlarm/Hotkeys/RawKeyboardState.cs tests/TalesAlarm.Tests/Hotkeys/RawKeyboardStateTests.cs
git commit -m "feat: track raw keyboard gesture state"
```

---

### 작업 3: `GlobalHotkeyService`를 Raw Input 조정자로 교체

**파일:**
- 수정: `src/TalesAlarm/Hotkeys/GlobalHotkeyService.cs`
- 수정: `src/TalesAlarm/App.xaml.cs:24-33,143-190`
- 삭제: `src/TalesAlarm/Hotkeys/HotkeyNativeApi.cs`
- 삭제: `tests/TalesAlarm.Tests/Helpers/FakeHotkeyNativeApi.cs`
- 생성: `tests/TalesAlarm.Tests/Helpers/FakeRawInputNativeApi.cs`
- 수정: `tests/TalesAlarm.Tests/Hotkeys/GlobalHotkeyServiceTests.cs`
- 수정: `tests/TalesAlarm.Tests/ViewModels/MainViewModelTests.cs:397-423`
- 생성: `tests/TalesAlarm.Tests/Hotkeys/AppRawInputIntegrationTests.cs`

**인터페이스:**
- 유지: `HotkeyPressed`, `ActiveBindings`, `Attach`, `Apply`, `SuspendForCapture`, `HotkeyApplyResult`
- 변경: `IGlobalHotkeyService.ProcessWindowMessage(int, nint, nint)`은 반환값 없이 `lParam`까지 소비한다.
- 소비: `IRawInputNativeApi`, `RawKeyboardState`
- 생성: `GlobalHotkeyService.WmInput=0x00FF`, `WmInputDeviceChange=0x00FE`, `GidcRemoval=2`
- 제거: `IHotkeyNativeApi`, `Win32HotkeyNativeApi`, `RegisterHotKey`, `UnregisterHotKey`, `WM_HOTKEY`
- `App`은 `GlobalHotkeyService(new Win32RawInputNativeApi(), diagnostic)`를 구성하고 `lParam`을 전달하되 WPF `handled`를 변경하지 않는다.

- [ ] **1단계: 가짜 Raw Input API 작성**

`FakeRawInputNativeApi.cs`는 등록·해제 횟수와 다음 읽기 결과만 노출하며 실제 입력 내용은 저장하지 않는다.

```csharp
using TalesAlarm.Hotkeys;

namespace TalesAlarm.Tests.Helpers;

internal sealed class FakeRawInputNativeApi : IRawInputNativeApi
{
    public bool RegisterSucceeds { get; set; } = true;
    public int RegisterErrorCode { get; set; } = 1001;
    public bool UnregisterSucceeds { get; set; } = true;
    public int UnregisterErrorCode { get; set; } = 1002;
    public RawInputReadResult NextReadResult { get; set; } =
        RawInputReadResult.Ignored();
    public int RegisterCallCount { get; private set; }
    public int UnregisterCallCount { get; private set; }
    public int ReadCallCount { get; private set; }
    public nint RegisteredWindowHandle { get; private set; }
    public nint LastRawInputHandle { get; private set; }

    public bool TryRegisterKeyboard(nint windowHandle, out int errorCode)
    {
        RegisterCallCount++;
        RegisteredWindowHandle = windowHandle;
        errorCode = RegisterSucceeds ? 0 : RegisterErrorCode;
        return RegisterSucceeds;
    }

    public bool TryUnregisterKeyboard(out int errorCode)
    {
        UnregisterCallCount++;
        errorCode = UnregisterSucceeds ? 0 : UnregisterErrorCode;
        return UnregisterSucceeds;
    }

    public RawInputReadResult ReadKeyboard(nint rawInputHandle)
    {
        ReadCallCount++;
        LastRawInputHandle = rawInputHandle;
        return NextReadResult;
    }
}
```

- [ ] **2단계: 등록 수명·실패 격리 테스트 작성**

기존 `RegisterHotKey`별 등록·롤백 테스트를 제거하고 장치 클래스 한 번 등록 계약으로 교체한다.

```csharp
[Fact]
public void AttachApplyAndDispose_RegisterOnceAndUnregisterOnce()
{
    var native = new FakeRawInputNativeApi();
    var service = new GlobalHotkeyService(native);

    service.Attach((nint)42);
    Assert.True(service.Apply(Bindings((1, Key.F4, HotkeyModifiers.None))).Success);
    Assert.True(service.Apply(Bindings((1, Key.F8, HotkeyModifiers.None))).Success);
    service.Dispose();
    service.Dispose();

    Assert.Equal(1, native.RegisterCallCount);
    Assert.Equal((nint)42, native.RegisteredWindowHandle);
    Assert.Equal(1, native.UnregisterCallCount);
}

[Fact]
public void Apply_WhenRawInputRegistrationFailed_DisablesOnlyHotkeysAndReportsCode()
{
    var native = new FakeRawInputNativeApi
    {
        RegisterSucceeds = false,
        RegisterErrorCode = 87,
    };
    var diagnostics = new List<string>();
    using var service = new GlobalHotkeyService(native, diagnostics.Add);
    service.Attach((nint)42);

    var result = service.Apply(Bindings((1, Key.F4, HotkeyModifiers.None)));

    Assert.False(result.Success);
    Assert.Contains("87", result.ErrorMessage);
    Assert.Contains(diagnostics, message => message.Contains("87"));
    Assert.Empty(service.ActiveBindings);
}
```

- [ ] **3단계: 라우팅·반복·정확한 수정키 테스트 작성**

테스트 헬퍼 `Send`는 가짜 API의 다음 결과를 설정하고 `lParam` 전달 여부까지 검증한다.

```csharp
[Fact]
public void ProcessWindowMessage_MatchingFirstDownRaisesAssignedTimerOnce()
{
    var native = new FakeRawInputNativeApi();
    using var service = AttachedService(native);
    Assert.True(service.Apply(Bindings((2, Key.F2, HotkeyModifiers.Control))).Success);
    var pressed = new List<int>();
    service.HotkeyPressed += (_, timerIndex) => pressed.Add(timerIndex);

    Send(service, native, Input(1, 0x11, 0x1D));
    Send(service, native, Input(1, 0x71, 0x3C));
    Send(service, native, Input(1, 0x71, 0x3C));

    Assert.Equal(new[] { 2 }, pressed);
    Assert.Equal((nint)123, native.LastRawInputHandle);

    Send(service, native, Input(1, 0x71, 0x3C, RawKeyboardFlags.Break));
    Send(service, native, Input(1, 0x71, 0x3C));
    Assert.Equal(new[] { 2, 2 }, pressed);
}

[Fact]
public void ProcessWindowMessage_WithAdditionalModifier_DoesNotMatch()
{
    var native = new FakeRawInputNativeApi();
    using var service = AttachedService(native);
    service.Apply(Bindings((1, Key.F2, HotkeyModifiers.Control)));
    var presses = 0;
    service.HotkeyPressed += (_, _) => presses++;

    Send(service, native, Input(1, 0x11, 0x1D));
    Send(service, native, Input(1, 0x12, 0x38));
    Send(service, native, Input(1, 0x71, 0x3C));

    Assert.Equal(0, presses);
}

private static void Send(
    GlobalHotkeyService service,
    FakeRawInputNativeApi native,
    RawKeyboardInput input)
{
    native.NextReadResult = RawInputReadResult.FromKeyboard(input);
    service.ProcessWindowMessage(GlobalHotkeyService.WmInput, 0, (nint)123);
}

private static GlobalHotkeyService AttachedService(
    FakeRawInputNativeApi native,
    Action<string>? writeDiagnostic = null)
{
    var service = new GlobalHotkeyService(native, writeDiagnostic);
    service.Attach((nint)42);
    return service;
}

private static RawKeyboardInput Input(
    nint device,
    ushort virtualKey,
    ushort makeCode,
    RawKeyboardFlags flags = RawKeyboardFlags.None) =>
    new(device, virtualKey, makeCode, flags);

private static HotkeyBinding[] Bindings(
    params (int TimerIndex, Key Key, HotkeyModifiers Modifiers)[] values) =>
    values.Select(value => new HotkeyBinding(
        value.TimerIndex,
        new HotkeyGesture(value.Key, value.Modifiers))).ToArray();
```

- [ ] **4단계: 캡처·설정 적용·장치 제거 상태 경계 테스트 작성**

```csharp
[Fact]
public void SuspendForCapture_WithNestedLeases_SuppressesUntilFinalLeaseAndClearsState()
{
    var native = new FakeRawInputNativeApi();
    using var service = AttachedService(native);
    service.Apply(Bindings((1, Key.F2, HotkeyModifiers.None)));
    var presses = 0;
    service.HotkeyPressed += (_, _) => presses++;
    var outer = service.SuspendForCapture();
    var inner = service.SuspendForCapture();

    Send(service, native, Input(1, 0x71, 0x3C));
    inner.Dispose();
    Send(service, native, Input(1, 0x71, 0x3C));
    Assert.Equal(0, presses);

    outer.Dispose();
    Send(service, native, Input(1, 0x71, 0x3C));
    Assert.Equal(1, presses);
    Assert.Equal(1, native.RegisterCallCount);
    Assert.Equal(0, native.UnregisterCallCount);
}

[Fact]
public void Apply_ClearsPressedStateAndAtomicallyReplacesBindings()
{
    var native = new FakeRawInputNativeApi();
    using var service = AttachedService(native);
    service.Apply(Bindings((1, Key.F2, HotkeyModifiers.None)));
    Send(service, native, Input(1, 0x71, 0x3C));

    Assert.True(service.Apply(Bindings((2, Key.F2, HotkeyModifiers.None))).Success);
    var pressed = 0;
    service.HotkeyPressed += (_, timerIndex) => pressed = timerIndex;
    Send(service, native, Input(1, 0x71, 0x3C));

    Assert.Equal(2, pressed);
}

[Fact]
public void DeviceRemoval_ClearsOnlyRemovedDeviceState()
{
    var native = new FakeRawInputNativeApi();
    using var service = AttachedService(native);
    service.Apply(Bindings((1, Key.F2, HotkeyModifiers.None)));
    var presses = 0;
    service.HotkeyPressed += (_, _) => presses++;
    Send(service, native, Input(7, 0x71, 0x3C));

    service.ProcessWindowMessage(
        GlobalHotkeyService.WmInputDeviceChange,
        (nint)GlobalHotkeyService.GidcRemoval,
        (nint)7);
    Send(service, native, Input(7, 0x71, 0x3C));

    Assert.Equal(2, presses);
}
```

- [ ] **5단계: 잘못된 패킷·알 수 없는 메시지의 진단·무시 테스트 작성**

```csharp
[Fact]
public void ProcessWindowMessage_WhenReadFails_LogsCodeWithoutRaisingEvent()
{
    var native = new FakeRawInputNativeApi
    {
        NextReadResult = RawInputReadResult.Failed(13),
    };
    var diagnostics = new List<string>();
    using var service = new GlobalHotkeyService(native, diagnostics.Add);
    service.Attach((nint)42);
    service.Apply(Bindings((1, Key.F2, HotkeyModifiers.None)));
    var presses = 0;
    service.HotkeyPressed += (_, _) => presses++;

    service.ProcessWindowMessage(GlobalHotkeyService.WmInput, 0, (nint)123);

    Assert.Equal(0, presses);
    Assert.Contains(diagnostics, message => message.Contains("13"));
    Assert.DoesNotContain(diagnostics, message => message.Contains("F2"));
}

[Fact]
public void ProcessWindowMessage_WhenMessageOrDeviceChangeIsUnknown_DoesNothing()
{
    var native = new FakeRawInputNativeApi();
    using var service = AttachedService(native);

    service.ProcessWindowMessage(0, 0, (nint)123);
    service.ProcessWindowMessage(
        GlobalHotkeyService.WmInputDeviceChange,
        (nint)99,
        (nint)777);

    Assert.Equal(0, native.ReadCallCount);
}
```

기존 중복 타이머 번호, 중복 제스처, 수정키 전용 제스처, 연결 전 `Apply` 거부 테스트는 유지하되 네이티브 등록 목록 단언을 `ActiveBindings`와 `RegisterCallCount` 단언으로 바꾼다.

같은 RED 주기에 `AppRawInputIntegrationTests.cs`를 추가해 구성 루트의 `lParam` 전달, 비처리 훅과 금지된 런타임 의존성을 고정한다.

```csharp
using System.IO;
using TalesAlarm.Tests.Helpers;

namespace TalesAlarm.Tests.Hotkeys;

public sealed class AppRawInputIntegrationTests
{
    [Fact]
    public void AppHook_ForwardsLParamWithoutMarkingMessageHandled()
    {
        var source = File.ReadAllText(Path.Combine(
            ProjectFiles.RepositoryRoot,
            "src",
            "TalesAlarm",
            "App.xaml.cs"));

        Assert.Contains(
            "ProcessWindowMessage(message, wParam, lParam);",
            source);
        Assert.DoesNotContain("handled = true", source);
    }

    [Fact]
    public void ProductionSource_HasNoExclusiveHotkeyOrInputInjectionApi()
    {
        var sourceRoot = Path.Combine(
            ProjectFiles.RepositoryRoot,
            "src",
            "TalesAlarm");
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        Assert.DoesNotContain("RegisterHotKey", source);
        Assert.DoesNotContain("UnregisterHotKey", source);
        Assert.DoesNotContain("SetWindowsHookEx", source);
        Assert.DoesNotContain("SendInput", source);
        Assert.DoesNotContain("keybd_event", source);
    }
}
```

`MainViewModelTests.FakeGlobalHotkeyService`에 다음 실패 결과를 추가하고, 실패 시 기존 바인딩을 바꾸지 않게 한 뒤 사용자 오류 격리 테스트를 작성한다.

```csharp
public HotkeyApplyResult NextApplyResult { get; set; } = new(true, null);

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

public void ProcessWindowMessage(int message, nint wParam, nint lParam)
{
}
```

`MainViewModelTests`의 바깥 테스트 클래스 본문에는 다음 테스트를 추가한다.

```csharp
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
```

- [ ] **6단계: 서비스 집중 테스트를 실행해 RED 확인**

```powershell
dotnet test TalesAlarm.sln -c Release --no-restore --filter "FullyQualifiedName~GlobalHotkeyServiceTests|FullyQualifiedName~AppRawInputIntegrationTests|FullyQualifiedName~MainViewModelTests"
```

예상 결과: 생성자 의존성, 메시지 시그니처, Raw Input 상수와 앱 훅이 현재 `RegisterHotKey` 구현과 달라 컴파일 또는 새 단언이 실패한다.

- [ ] **7단계: 서비스 계약과 등록 가능 상태 구현**

`IGlobalHotkeyService`의 메시지 계약을 바꾸고 서비스 필드를 Raw Input 기준으로 교체한다.

```csharp
public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler<int>? HotkeyPressed;
    IReadOnlyList<HotkeyBinding> ActiveBindings { get; }
    void Attach(nint windowHandle);
    HotkeyApplyResult Apply(IReadOnlyList<HotkeyBinding> bindings);
    IDisposable SuspendForCapture();
    void ProcessWindowMessage(int message, nint wParam, nint lParam);
}

public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    public const int WmInput = 0x00FF;
    public const int WmInputDeviceChange = 0x00FE;
    public const int GidcRemoval = 2;

    private readonly IRawInputNativeApi nativeApi;
    private readonly Action<string> writeDiagnostic;
    private readonly RawKeyboardState keyboardState = new();
    private HotkeyBinding[] activeBindings = [];
    private Dictionary<HotkeyGesture, int> timerByGesture = [];
    private nint windowHandle;
    private int suspensionCount;
    private int registrationErrorCode;
    private bool inputRegistered;
    private bool attached;
    private bool disposed;

    public GlobalHotkeyService(
        IRawInputNativeApi nativeApi,
        Action<string>? writeDiagnostic = null)
    {
        this.nativeApi = nativeApi
            ?? throw new ArgumentNullException(nameof(nativeApi));
        this.writeDiagnostic = writeDiagnostic ?? (_ => { });
    }

    public event EventHandler<int>? HotkeyPressed;

    public IReadOnlyList<HotkeyBinding> ActiveBindings => activeBindings;
}
```

`Attach`는 같은 핸들에 대해 멱등이고 다른 핸들로의 재연결은 거부한다. 최초 연결에서 상태를 초기화하고 키보드 장치 클래스를 한 번 등록한다.

```csharp
public void Attach(nint windowHandle)
{
    ObjectDisposedException.ThrowIf(disposed, this);
    if (windowHandle == 0)
    {
        throw new ArgumentException("창 핸들은 0일 수 없습니다.", nameof(windowHandle));
    }

    if (attached)
    {
        if (this.windowHandle != windowHandle)
        {
            throw new InvalidOperationException(
                "Raw Input 서비스가 이미 다른 창에 연결되어 있습니다.");
        }

        return;
    }

    this.windowHandle = windowHandle;
    attached = true;
    keyboardState.Clear();
    inputRegistered = nativeApi.TryRegisterKeyboard(
        windowHandle,
        out registrationErrorCode);
    if (!inputRegistered)
    {
        writeDiagnostic(
            $"키보드 Raw Input 등록 실패. Windows 오류 코드: {registrationErrorCode}.");
    }
}
```

- [ ] **8단계: 검증된 바인딩 스냅샷 적용 구현**

`Apply`는 기존 검증을 유지하되 키별 네이티브 등록·롤백을 모두 제거한다. 등록 실패 상태에서는 후보를 활성화하지 않는다.

```csharp
public HotkeyApplyResult Apply(IReadOnlyList<HotkeyBinding> bindings)
{
    ObjectDisposedException.ThrowIf(disposed, this);
    ArgumentNullException.ThrowIfNull(bindings);
    if (!attached)
    {
        return new(false, "단축키 입력을 연결할 창이 아직 준비되지 않았습니다.");
    }

    var candidates = bindings.OrderBy(binding => binding.TimerIndex).ToArray();
    if (candidates.Select(binding => binding.TimerIndex).Distinct().Count()
        != candidates.Length)
    {
        return new(false, "타이머 번호가 중복되었습니다.");
    }

    if (candidates.Select(binding => binding.Gesture).Distinct().Count()
        != candidates.Length)
    {
        return new(false, "두 타이머에 같은 단축키를 사용할 수 없습니다.");
    }

    if (candidates.Any(binding => !binding.Gesture.HasNonModifierKey))
    {
        return new(false, "단축키에는 기능 키 또는 일반 키가 필요합니다.");
    }

    if (!inputRegistered)
    {
        return new(
            false,
            $"키보드 Raw Input을 등록하지 못했습니다. Windows 오류 코드: "
                + $"{registrationErrorCode}. 단축키 입력을 사용할 수 없습니다.");
    }

    activeBindings = candidates;
    timerByGesture = candidates.ToDictionary(
        binding => binding.Gesture,
        binding => binding.TimerIndex);
    keyboardState.Clear();
    return new(true, null);
}
```

- [ ] **9단계: 메시지 처리와 캡처 임대 구현**

`ProcessWindowMessage`는 `WM_INPUT`의 `lParam`을 읽고, 최초 키다운에서만 정확한 제스처를 라우팅한다. 장치 제거는 해당 상태만 지우며 도착·알 수 없는 변경은 무시한다.

```csharp
public void ProcessWindowMessage(int message, nint wParam, nint lParam)
{
    if (disposed || !inputRegistered)
    {
        return;
    }

    if (message == WmInputDeviceChange)
    {
        if (unchecked((int)wParam) == GidcRemoval)
        {
            keyboardState.RemoveDevice(lParam);
        }

        return;
    }

    if (message != WmInput)
    {
        return;
    }

    var result = nativeApi.ReadKeyboard(lParam);
    if (result.Status == RawInputReadStatus.Failed)
    {
        writeDiagnostic(
            $"Raw Input 읽기 실패. Windows 오류 코드: {result.ErrorCode}.");
        return;
    }

    if (result.Status != RawInputReadStatus.Keyboard
        || !keyboardState.TryCreateGesture(result.Keyboard, out var gesture)
        || suspensionCount > 0
        || !timerByGesture.TryGetValue(gesture, out var timerIndex))
    {
        return;
    }

    HotkeyPressed?.Invoke(this, timerIndex);
}

public IDisposable SuspendForCapture()
{
    ObjectDisposedException.ThrowIf(disposed, this);
    if (!attached)
    {
        throw new InvalidOperationException(
            "단축키 입력을 연결할 창이 아직 준비되지 않았습니다.");
    }

    suspensionCount++;
    return new CaptureLease(this);
}

private void ResumeAfterCapture()
{
    if (disposed || suspensionCount == 0)
    {
        return;
    }

    suspensionCount--;
    if (suspensionCount == 0)
    {
        keyboardState.Clear();
    }
}

private sealed class CaptureLease(GlobalHotkeyService owner) : IDisposable
{
    private GlobalHotkeyService? owner = owner;

    public void Dispose() =>
        Interlocked.Exchange(ref owner, null)?.ResumeAfterCapture();
}
```

- [ ] **10단계: 종료 정리와 진단 구현**

`Dispose`는 입력 상태와 이벤트를 먼저 무효화하고 등록된 장치 클래스만 한 번 해제한다. 해제 실패는 사용자 동작을 막지 않고 오류 코드만 기록한다.

```csharp
public void Dispose()
{
    if (disposed)
    {
        return;
    }

    disposed = true;
    keyboardState.Clear();
    activeBindings = [];
    timerByGesture = [];
    suspensionCount = 0;
    HotkeyPressed = null;

    if (inputRegistered
        && !nativeApi.TryUnregisterKeyboard(out var errorCode))
    {
        writeDiagnostic(
            $"키보드 Raw Input 해제 실패. Windows 오류 코드: {errorCode}.");
    }

    inputRegistered = false;
}
```

- [ ] **11단계: 앱 훅·ViewModel 가짜 계약 연결 후 기존 경계 삭제**

`App.ComposeAndShowApplication`에서 Raw Input 구현과 진단 콜백을 구성한다.

```csharp
hotkeyService = new GlobalHotkeyService(
    new Win32RawInputNativeApi(),
    message => logger?.Write(message));
```

WPF 훅은 `lParam`까지 전달하고 `handled`는 변경하지 않는다.

```csharp
private nint ProcessWindowMessage(
    nint windowHandle,
    int message,
    nint wParam,
    nint lParam,
    ref bool handled)
{
    hotkeyService?.ProcessWindowMessage(message, wParam, lParam);
    return 0;
}
```

`MainViewModelTests.FakeGlobalHotkeyService`에 5단계의 `NextApplyResult`, 실패 시 비변경 `Apply`, 세 매개변수 `void ProcessWindowMessage`를 적용한다. 프로덕션 `MainViewModel`의 초기화 오류 표시, 저장 실패 시 `Apply(previousBindings)` 복원과 `HotkeyPressed` 타이머 라우팅 코드는 유지한다.

마지막으로 `src/TalesAlarm/Hotkeys/HotkeyNativeApi.cs`와 `tests/TalesAlarm.Tests/Helpers/FakeHotkeyNativeApi.cs`를 삭제한다. 이 단계가 끝난 뒤 프로덕션 소스에는 `IHotkeyNativeApi`, `Win32HotkeyNativeApi`, `RegisterHotKey`, `UnregisterHotKey`, `ModNoRepeat`, `WmHotkey`가 남지 않아야 한다.

- [ ] **12단계: 서비스·상태 테스트 GREEN 확인**

```powershell
dotnet test TalesAlarm.sln -c Release --no-restore --filter "FullyQualifiedName~GlobalHotkeyServiceTests|FullyQualifiedName~RawKeyboardStateTests|FullyQualifiedName~AppRawInputIntegrationTests|FullyQualifiedName~MainViewModelTests|FullyQualifiedName~MainWindowTests|FullyQualifiedName~TimerViewModelTests"
```

예상 결과: 장치 등록 수명, 등록 실패 격리, 바인딩 검증, 정확한 수정키, 자동 반복, 캡처 중 억제, 설정 적용·장치 제거 초기화, `lParam` 전달, 비처리 WPF 훅, 오류 표시와 기존 타이머 라우팅 테스트가 모두 통과한다.

- [ ] **13단계: 작업 3 커밋**

```powershell
git add src/TalesAlarm/App.xaml.cs src/TalesAlarm/Hotkeys/GlobalHotkeyService.cs src/TalesAlarm/Hotkeys/HotkeyNativeApi.cs tests/TalesAlarm.Tests/Helpers/FakeHotkeyNativeApi.cs tests/TalesAlarm.Tests/Helpers/FakeRawInputNativeApi.cs tests/TalesAlarm.Tests/Hotkeys/GlobalHotkeyServiceTests.cs tests/TalesAlarm.Tests/Hotkeys/AppRawInputIntegrationTests.cs tests/TalesAlarm.Tests/ViewModels/MainViewModelTests.cs
git commit -m "feat: connect pass-through raw input hotkeys"
```

---

### 작업 4: 사용자 문서와 연결 회귀 검증

**파일:**
- 수정: `README.md`

**인터페이스:**
- 사용자 문서는 단축키의 전체 패스스루 의미, 자동 반복 억제, 트레이·캡처 동작, 운영체제 예약 조합과 보안 경계를 설명한다.
- 작업 3에서 연결한 WPF 훅, 초기 등록 실패 격리, 설정 복원과 타이머 라우팅을 집중 테스트로 다시 확인한다.

- [ ] **1단계: README의 패스스루 의미와 제한 갱신**

`README.md`의 단축키 설명을 다음 내용으로 교체한다.

```markdown
### 패스스루 단축키

- 설정한 단축키는 Tales Alarm이 독점하지 않습니다. 같은 물리 키 입력을 현재 활성 프로그램과 Tales Alarm이 함께 처리합니다.
- 테일즈위버에서 `F2` 또는 `F5`를 누르면 게임 동작과 연결된 타이머 동작이 동시에 실행됩니다.
- 키를 길게 눌러도 타이머는 최초 키다운에 한 번만 반응하며, 키를 놓았다 다시 누르면 다시 반응합니다.
- 앱 창을 트레이에 숨겨도 단축키가 작동하고, 단축키 입력 상자에서 새 키를 캡처하는 동안에는 타이머 실행이 억제됩니다.
- `Ctrl+Alt+Delete`와 Windows가 선점하는 일부 시스템 조합은 감지를 보장하지 않습니다.
- Tales Alarm은 키 입력을 차단·주입하지 않으며 테일즈위버나 게임 보안 프로세스에 접근하지 않습니다.
```

기존 설치, 설정, 알람, 간단 보기 설명은 유지한다.

- [ ] **2단계: 앱·ViewModel·사용자 경로 회귀 확인**

```powershell
dotnet test TalesAlarm.sln -c Release --no-restore --filter "FullyQualifiedName~AppRawInputIntegrationTests|FullyQualifiedName~MainViewModelTests|FullyQualifiedName~MainWindowTests|FullyQualifiedName~TimerViewModelTests"
```

예상 결과: `lParam` 전달, 비처리 훅, 초기 등록 실패 격리, 설정 저장 복원, 단축키 캡처와 타이머별 라우팅 테스트가 모두 통과한다.

- [ ] **3단계: 작업 4 커밋**

```powershell
git add README.md
git commit -m "docs: explain pass-through raw input hotkeys"
```

---

### 작업 5: 전체 회귀·게시·수동 호환성 검증

**파일:**
- 수정: `docs/superpowers/plans/2026-08-11-pass-through-hotkeys.md`

**인터페이스:**
- 자동 검증은 전체 Release 테스트, 금지 API 검색, win-x64 게시와 실행 검사를 포함한다.
- 수동 검증은 실제 테일즈위버 창 모드·전체 화면 모드에서 게임 동작과 타이머 동작의 동시 실행을 확인한다.

- [ ] **1단계: 전체 Release 테스트 실행**

```powershell
dotnet test TalesAlarm.sln -c Release --no-restore
```

예상 결과: 실패와 건너뜀 없이 기존 설정·타이머·알람·트레이·WPF 테스트와 새 Raw Input 테스트가 모두 통과한다.

- [ ] **2단계: 런타임 금지 API와 변경 범위 검사**

```powershell
rg -n "RegisterHotKey|UnregisterHotKey|WM_HOTKEY|SetWindowsHookEx|SendInput|keybd_event|RIDEV_NOLEGACY|RIDEV_NOHOTKEYS" src tests
git diff --check
git status --short
```

예상 결과: `src/`에는 금지 API 호출·상수가 없고, 테스트에는 금지 API가 없음을 확인하는 문자열 단언만 있다. 공백 오류가 없고 의도한 소스·테스트·README·계획 파일만 변경되어 있다.

- [ ] **3단계: Windows x64 런타임 복원과 단일 파일 게시**

```powershell
dotnet restore src/TalesAlarm/TalesAlarm.csproj --runtime win-x64
dotnet publish src/TalesAlarm/TalesAlarm.csproj -p:PublishProfile=win-x64 --no-restore
```

예상 결과: `artifacts/TalesAlarm-win-x64/TalesAlarm.exe`가 생성된다.

- [ ] **4단계: 게시 산출물 실행 검증**

```powershell
powershell -ExecutionPolicy Bypass -File tests/Verify-PublishArtifact.ps1 -PublishDirectory artifacts/TalesAlarm-win-x64
```

예상 결과: 외부 런타임 파일이 필요 없는 단일 EXE가 시작되고 검증 시간 동안 정상 실행 상태를 유지한다.

- [ ] **5단계: 일반 프로그램 패스스루 수동 검증**

게시 EXE를 일반 사용자 권한으로 실행하고 다음 순서로 확인한다.

1. 타이머 1에 `F2`, 타이머 2에 `Ctrl+F5`를 저장한다.
2. 메모장 또는 브라우저를 활성화한 상태에서 `F2`를 눌러 활성 프로그램의 키 동작과 타이머 1이 함께 반응하는지 확인한다.
3. `Ctrl+F5`는 정확한 수정키 조합에서만 타이머 2를 실행하고, `F5`와 `Ctrl+Shift+F5`는 실행하지 않는지 확인한다.
4. `F2`를 길게 눌러 타이머 1이 한 번만 반응하고, 키를 놓았다 다시 눌러 다시 반응하는지 확인한다.
5. 창을 트레이에 숨긴 뒤 같은 검사를 반복한다.
6. 한국어·영어 입력 배열을 각각 선택하고 일반 문자 `A`와 OEM 기호 키를 임시 단축키로 저장해, 두 배열에서 저장된 WPF 키 의미와 Raw Input 가상 키가 동일하게 일치하는지 확인한다.
7. 단축키 입력 상자를 활성화한 동안 타이머가 실행되지 않고 캡처 종료 뒤 다시 동작하는지 확인한다.

- [ ] **6단계: 테일즈위버 호환성 수동 검증**

테일즈위버를 일반 사용자 권한으로 실행하고 다음 결과를 기록한다.

1. 창 모드에서 `F2`와 `F5`가 각각 게임 동작과 지정 타이머를 동시에 실행한다.
2. 전체 화면 모드에서도 같은 동시 실행이 유지된다.
3. 키를 길게 눌렀을 때 게임의 자체 동작과 무관하게 Tales Alarm 타이머는 한 번만 반응한다.
4. 테스트 중 게임이 종료되지 않고 BlackCipher 또는 다른 보안 경고가 표시되지 않는다.
5. 운영체제 예약 조합은 지원 보장 대상이 아님을 결과 기록에 명시한다.

- [ ] **7단계: 계획에 실제 검증 결과 기록**

각 체크박스를 실제 완료 상태로 바꾸고 RED/GREEN 결과, 전체 테스트 수, 게시 파일 크기, 실행 검증 결과, 수동 검증 환경과 결과를 해당 단계 아래에 기록한다. 실패한 항목은 성공으로 표시하지 않고 재현 조건과 다음 조치를 남긴다.

- [ ] **8단계: 작업 5 문서 커밋**

```powershell
git add docs/superpowers/plans/2026-08-11-pass-through-hotkeys.md
git commit -m "docs: record raw input verification"
```

- [ ] **9단계: 완료 전 검토와 최종 검증**

`superpowers:requesting-code-review`로 설계 대비 구현과 테스트 누락을 검토하고 지적 사항을 반영한다. 이어서 `superpowers:verification-before-completion`으로 전체 Release 테스트, 금지 API 검사, 게시와 실행 검증을 새로 실행한 뒤 `superpowers:finishing-a-development-branch`로 통합 방법을 사용자에게 제시한다.

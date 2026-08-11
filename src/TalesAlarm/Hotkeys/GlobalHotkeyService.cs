namespace TalesAlarm.Hotkeys;

public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler<int>? HotkeyPressed;

    IReadOnlyList<HotkeyBinding> ActiveBindings { get; }

    void Attach(nint windowHandle);

    HotkeyApplyResult Apply(IReadOnlyList<HotkeyBinding> bindings);

    IDisposable SuspendForCapture();

    void ProcessWindowMessage(int message, nint wParam, nint lParam);
}

public sealed record HotkeyApplyResult(bool Success, string? ErrorMessage);

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
}

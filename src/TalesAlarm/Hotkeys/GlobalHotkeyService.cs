namespace TalesAlarm.Hotkeys;

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

public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    public const int WmHotkey = 0x0312;

    private readonly IHotkeyNativeApi nativeApi;
    private HotkeyBinding[] activeBindings = [];
    private nint windowHandle;
    private int suspensionCount;
    private bool attached;
    private bool disposed;

    public GlobalHotkeyService(IHotkeyNativeApi nativeApi)
    {
        this.nativeApi = nativeApi ?? throw new ArgumentNullException(nameof(nativeApi));
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

        if (attached && this.windowHandle != windowHandle)
        {
            throw new InvalidOperationException("전역 단축키 서비스가 이미 다른 창에 연결되어 있습니다.");
        }

        this.windowHandle = windowHandle;
        attached = true;
    }

    public HotkeyApplyResult Apply(IReadOnlyList<HotkeyBinding> bindings)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(bindings);

        if (!attached)
        {
            return new(false, "전역 단축키를 등록할 창이 아직 준비되지 않았습니다.");
        }

        var candidates = bindings.OrderBy(binding => binding.TimerIndex).ToArray();
        if (candidates.Select(binding => binding.TimerIndex).Distinct().Count() != candidates.Length)
        {
            return new(false, "타이머 번호가 중복되었습니다.");
        }

        if (candidates.Select(binding => binding.Gesture).Distinct().Count() != candidates.Length)
        {
            return new(false, "두 타이머에 같은 전역 단축키를 사용할 수 없습니다.");
        }

        if (candidates.Any(binding => !binding.Gesture.HasNonModifierKey))
        {
            return new(false, "전역 단축키에는 기능 키 또는 일반 키가 필요합니다.");
        }

        if (suspensionCount > 0)
        {
            activeBindings = candidates;
            return new(true, null);
        }

        var previous = activeBindings;
        Unregister(previous);

        var registeredCandidates = new List<HotkeyBinding>(candidates.Length);
        foreach (var candidate in candidates)
        {
            if (nativeApi.TryRegister(windowHandle, candidate.TimerIndex, candidate.Gesture, out var candidateError))
            {
                registeredCandidates.Add(candidate);
                continue;
            }

            Unregister(registeredCandidates);
            var rollbackErrors = Register(previous);
            activeBindings = previous;

            var message = $"단축키를 등록하지 못했습니다. Windows 오류 코드: {candidateError}.";
            if (rollbackErrors.Count > 0)
            {
                message += $" 이전 단축키 복원도 실패했습니다. Windows 오류 코드: {string.Join(", ", rollbackErrors)}.";
            }

            return new(false, message);
        }

        activeBindings = candidates;
        return new(true, null);
    }

    public IDisposable SuspendForCapture()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!attached)
        {
            throw new InvalidOperationException("전역 단축키를 등록할 창이 아직 준비되지 않았습니다.");
        }

        if (suspensionCount++ == 0)
        {
            Unregister(activeBindings);
        }

        return new CaptureLease(this);
    }

    public bool ProcessWindowMessage(int message, nint wParam)
    {
        if (disposed || message != WmHotkey)
        {
            return false;
        }

        var id = unchecked((int)wParam);
        if (!activeBindings.Any(binding => binding.TimerIndex == id))
        {
            return false;
        }

        HotkeyPressed?.Invoke(this, id);
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (attached && suspensionCount == 0)
        {
            Unregister(activeBindings);
        }

        activeBindings = [];
        suspensionCount = 0;
        HotkeyPressed = null;
    }

    private List<int> Register(IEnumerable<HotkeyBinding> bindings)
    {
        var errors = new List<int>();
        foreach (var binding in bindings)
        {
            if (!nativeApi.TryRegister(windowHandle, binding.TimerIndex, binding.Gesture, out var errorCode))
            {
                errors.Add(errorCode);
            }
        }

        return errors;
    }

    private void Unregister(IEnumerable<HotkeyBinding> bindings)
    {
        foreach (var binding in bindings)
        {
            nativeApi.Unregister(windowHandle, binding.TimerIndex);
        }
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
            Register(activeBindings);
        }
    }

    private sealed class CaptureLease(GlobalHotkeyService owner) : IDisposable
    {
        private GlobalHotkeyService? owner = owner;

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.ResumeAfterCapture();
    }
}

using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace TalesAlarm.Tools.RawInputProbe;

/// <summary>
/// 테일즈위버가 포그라운드일 때 키보드 Raw Input이 백그라운드 창에 도달하는지 계측하는
/// 진단 전용 도구다. Tales Alarm 본체와 동일한 창·훅·등록 방식을 재현하되, 패킷 값을
/// 그대로 기록해 어느 경계에서 입력이 끊기는지 판별한다.
///
/// 이 도구는 키 식별자를 로그에 남기므로 진단 전용이며 제품에 포함하지 않는다.
/// 훅 설치, 입력 주입, 게임 프로세스 접근은 하지 않는다.
/// </summary>
internal enum ProbeMode
{
    Sink,
    ExSink,
    Poll,
}

internal static partial class Program
{
    private const int WmInput = 0x00FF;
    private const int WmInputDeviceChange = 0x00FE;

    private const uint RidInput = 0x10000003;
    private const uint RimTypeKeyboard = 1;
    private const uint NativeError = uint.MaxValue;

    private const uint RidevRemove = 0x00000001;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevExInputSink = 0x00001000;
    private const uint RidevDevNotify = 0x00002000;

    private const ushort GenericDesktopUsagePage = 0x01;
    private const ushort KeyboardUsage = 0x06;

    private const ushort KnownKeyboardFlags = 0x0001 | 0x0002 | 0x0004;

    /// <summary>F1~F12, Insert, Pause, Q. 소리 신호와 폴링 감시 대상.</summary>
    private static readonly ushort[] WatchKeys =
    [
        0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x7B,
        0x2D, 0x13, 0x51,
    ];

    private static readonly Dictionary<ushort, bool> PollState = [];

    private static StreamWriter logWriter = StreamWriter.Null;
    private static TextBlock? statusText;
    private static ProbeMode mode;
    private static string logPath = string.Empty;

    private static long rawInputMessages;
    private static long keyboardPackets;
    private static long keyDowns;
    private static long readFailures;
    private static long deviceChanges;
    private static long pollTransitions;
    private static long heartbeats;
    private static string lastEvent = "(아직 없음)";

    [STAThread]
    private static int Main(string[] args)
    {
        mode = ParseMode(args);

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TalesAlarmProbe");
        Directory.CreateDirectory(directory);
        logPath = Path.Combine(
            directory,
            $"probe-{mode.ToString().ToLowerInvariant()}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        logWriter = new StreamWriter(
            new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
            // BOM을 남겨야 Windows PowerShell과 메모장이 한글 로그를 깨지 않고 읽는다.
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
        {
            AutoFlush = true,
        };

        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose,
        };

        // Tales Alarm 본체(App.xaml.cs)와 같은 순서로 창 핸들을 만들고 훅을 건다.
        statusText = new TextBlock
        {
            Margin = new Thickness(10),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
        };
        var window = new Window
        {
            Title = $"Raw Input Probe [{mode}]",
            Width = 560,
            Height = 260,
            Topmost = true,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new ScrollViewer { Content = statusText },
        };

        var windowHandle = new WindowInteropHelper(window).EnsureHandle();
        var source = HwndSource.FromHwnd(windowHandle);
        if (source is null)
        {
            Write("치명적: 창 메시지 소스를 만들지 못했습니다.");
            return -1;
        }

        source.AddHook(ProcessWindowMessage);

        Write($"모드={mode} 창핸들=0x{windowHandle:X} 프로세스ID={Environment.ProcessId}");
        Write($"관리자권한추정={IsElevated()} 로그={logPath}");

        if (mode == ProbeMode.Poll)
        {
            Write("Raw Input을 등록하지 않고 15ms 간격 GetAsyncKeyState 폴링만 수행합니다.");
            foreach (var key in WatchKeys)
            {
                PollState[key] = false;
            }

            var pollTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(15),
                DispatcherPriority.Input,
                OnPollTick,
                Dispatcher.CurrentDispatcher);
            pollTimer.Start();
        }
        else
        {
            var flags = RidevInputSink | RidevDevNotify;
            if (mode == ProbeMode.ExSink)
            {
                flags |= RidevExInputSink;
            }

            var device = new RawInputDevice
            {
                UsagePage = GenericDesktopUsagePage,
                Usage = KeyboardUsage,
                Flags = flags,
                TargetWindow = windowHandle,
            };
            var registered = RegisterRawInputDevices(
                ref device,
                1,
                checked((uint)Marshal.SizeOf<RawInputDevice>()));
            if (registered)
            {
                Write($"키보드 Raw Input 등록 성공. flags=0x{flags:X8}");
            }
            else
            {
                Write($"키보드 Raw Input 등록 실패. flags=0x{flags:X8} "
                    + $"오류코드={Marshal.GetLastWin32Error()}");
            }
        }

        var heartbeat = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            OnHeartbeat,
            Dispatcher.CurrentDispatcher);
        heartbeat.Start();

        window.Closed += (_, _) =>
        {
            source.RemoveHook(ProcessWindowMessage);
            if (mode != ProbeMode.Poll)
            {
                var removal = new RawInputDevice
                {
                    UsagePage = GenericDesktopUsagePage,
                    Usage = KeyboardUsage,
                    Flags = RidevRemove,
                    TargetWindow = 0,
                };
                RegisterRawInputDevices(
                    ref removal,
                    1,
                    checked((uint)Marshal.SizeOf<RawInputDevice>()));
            }

            Write("프로브를 종료합니다.");
            logWriter.Flush();
            logWriter.Dispose();
        };

        UpdateStatus();
        window.Show();
        return application.Run(window);
    }

    private static ProbeMode ParseMode(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (!string.Equals(args[index], "--mode", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return args[index + 1].ToLowerInvariant() switch
            {
                "exsink" => ProbeMode.ExSink,
                "poll" => ProbeMode.Poll,
                _ => ProbeMode.Sink,
            };
        }

        return ProbeMode.Sink;
    }

    private static nint ProcessWindowMessage(
        nint windowHandle,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == WmInputDeviceChange)
        {
            deviceChanges++;
            Write($"DEVCHANGE wparam={unchecked((int)wParam)} device=0x{lParam:X}");
            return 0;
        }

        if (message != WmInput)
        {
            return 0;
        }

        rawInputMessages++;
        ReadAndLog(wParam, lParam);
        return 0;
    }

    private static void ReadAndLog(nint wParam, nint lParam)
    {
        var headerSize = checked((uint)Marshal.SizeOf<RawInputHeader>());
        var keyboardSize = checked((uint)Marshal.SizeOf<RawKeyboard>());
        uint requiredSize = 0;
        var queryResult = GetRawInputData(lParam, RidInput, 0, ref requiredSize, headerSize);
        if (queryResult == NativeError)
        {
            readFailures++;
            Write($"READFAIL 크기질의 실패 오류코드={Marshal.GetLastWin32Error()}");
            return;
        }

        if (requiredSize < headerSize || requiredSize > int.MaxValue)
        {
            readFailures++;
            Write($"READFAIL 크기이상 required={requiredSize} header={headerSize}");
            return;
        }

        var buffer = Marshal.AllocHGlobal(checked((int)requiredSize));
        try
        {
            var copiedSize = requiredSize;
            var copied = GetRawInputData(lParam, RidInput, buffer, ref copiedSize, headerSize);
            if (copied == NativeError)
            {
                readFailures++;
                Write($"READFAIL 본문읽기 실패 오류코드={Marshal.GetLastWin32Error()}");
                return;
            }

            var header = Marshal.PtrToStructure<RawInputHeader>(buffer);
            if (header.Type != RimTypeKeyboard)
            {
                // 마우스/HID 패킷. 개수만 세고 내용은 남기지 않는다.
                return;
            }

            if (copied < headerSize + keyboardSize)
            {
                readFailures++;
                Write($"READFAIL 키보드패킷 짧음 copied={copied}");
                return;
            }

            var keyboard = Marshal.PtrToStructure<RawKeyboard>(
                nint.Add(buffer, checked((int)headerSize)));
            keyboardPackets++;

            var isKeyUp = (keyboard.Flags & 0x0001) != 0;
            if (!isKeyUp)
            {
                keyDowns++;
            }

            // Tales Alarm 본체의 검증 규칙(RawInputNativeApi.ReadKeyboard)을 통과할 패킷인지 같이 기록한다.
            var productAccepts = keyboard.Reserved == 0
                && (keyboard.Flags & ~KnownKeyboardFlags) == 0
                && keyboard.VirtualKey is not (0 or 0x00FF);
            var wpfKey = keyboard.VirtualKey is 0 or 0x00FF
                ? Key.None
                : KeyInterop.KeyFromVirtualKey(keyboard.VirtualKey);
            var sink = (unchecked((int)wParam) & 0xFF) == 1 ? "SINK" : "FG";

            var line = $"KBD src={sink} dev=0x{header.Device:X} "
                + $"vk=0x{keyboard.VirtualKey:X2}({wpfKey}) make=0x{keyboard.MakeCode:X2} "
                + $"flags=0x{(ushort)keyboard.Flags:X4}({(isKeyUp ? "up" : "down")}) "
                + $"msg=0x{keyboard.Message:X4} extra=0x{keyboard.ExtraInformation:X8} "
                + $"reserved={keyboard.Reserved} 본체수용={(productAccepts ? "예" : "아니오")}";
            lastEvent = line;
            Write(line);

            if (!isKeyUp && Array.IndexOf(WatchKeys, keyboard.VirtualKey) >= 0)
            {
                SignalArrival();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void OnPollTick(object? sender, EventArgs eventArgs)
    {
        foreach (var key in WatchKeys)
        {
            var down = (GetAsyncKeyState(key) & 0x8000) != 0;
            if (down == PollState[key])
            {
                continue;
            }

            PollState[key] = down;
            pollTransitions++;
            var line = $"POLL vk=0x{key:X2}({KeyInterop.KeyFromVirtualKey(key)}) "
                + (down ? "down" : "up");
            lastEvent = line;
            Write(line);
            if (down)
            {
                keyDowns++;
                SignalArrival();
            }
        }
    }

    private static void OnHeartbeat(object? sender, EventArgs eventArgs)
    {
        heartbeats++;
        Write($"HEARTBEAT wm_input={rawInputMessages} kbd={keyboardPackets} down={keyDowns} "
            + $"readfail={readFailures} devchange={deviceChanges} poll={pollTransitions}");
        UpdateStatus();
    }

    private static void UpdateStatus()
    {
        if (statusText is null)
        {
            return;
        }

        statusText.Text = $"""
            모드: {mode}
            로그: {logPath}

            WM_INPUT 메시지: {rawInputMessages}
            키보드 패킷: {keyboardPackets}
            키다운: {keyDowns}
            읽기 실패: {readFailures}
            장치 변경: {deviceChanges}
            폴링 전이: {pollTransitions}
            하트비트: {heartbeats}

            마지막 이벤트:
            {lastEvent}

            감시 키(F1~F12, Insert, Pause, Q) 키다운을 받으면 비프음이 납니다.
            창을 닫으면 종료됩니다.
            """;
    }

    /// <summary>전체 화면 게임 중에는 화면을 볼 수 없으므로 소리로 도달 여부를 알린다.</summary>
    private static void SignalArrival() =>
        ThreadPool.QueueUserWorkItem(static _ => Beep(880, 40));

    private static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static void Write(string message) =>
        logWriter.WriteLine(
            $"[{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}] {message}");

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint TargetWindow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public nint Device;
        public nuint WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VirtualKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterRawInputDevices(
        ref RawInputDevice devices,
        uint deviceCount,
        uint deviceSize);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint GetRawInputData(
        nint rawInputHandle,
        uint command,
        nint data,
        ref uint dataSize,
        uint headerSize);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Beep(uint frequency, uint duration);
}

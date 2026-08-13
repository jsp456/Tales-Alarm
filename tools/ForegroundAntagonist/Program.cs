using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace TalesAlarm.Tools.ForegroundAntagonist;

/// <summary>
/// 진단 전용 테스트 상대역. 게임이 포그라운드일 때 키 입력이 백그라운드 관찰자에게
/// 도달하지 않는 현상의 후보 메커니즘을 이 PC에서 재현한다. 이 창이 활성인 동안
/// RawInputProbe가 여전히 키를 받는지 측정하는 것이 목적이다.
///
/// 게임이나 다른 프로세스를 건드리지 않는다. 입력을 합성하지 않는다.
/// 제품(TalesAlarm.exe)에 포함되지 않으며 TalesAlarm.sln에도 들어 있지 않다.
///
/// 안전 장치: 이 창이 활성일 때만 동작하고, Esc로 즉시 종료되며,
/// 3분 뒤 자동 종료하면서 등록과 후크를 반드시 해제한다.
/// </summary>
internal enum AntagonistMode
{
    /// <summary>대조군. 아무것도 가로채지 않는 평범한 창.</summary>
    None,

    /// <summary>포그라운드 앱이 RIDEV_NOLEGACY | RIDEV_NOHOTKEYS로 키보드를 독점하는 상황.</summary>
    NoLegacy,

    /// <summary>포그라운드 앱이 저수준 키보드 훅으로 F1~F12를 삼키는 상황.</summary>
    LowLevelHook,

    /// <summary>
    /// F1~F12의 <b>키업만</b> 삼키는 상황. 키다운은 그대로 통과시킨다.
    /// 전체 화면 전환이나 선택적 가로채기로 키업 한 개를 놓쳤을 때
    /// Tales Alarm의 눌림 상태가 고착되는지 재현한다.
    /// 수정키가 아니므로 시스템 입력에는 영향이 없다.
    /// </summary>
    EatKeyUp,
}

internal static unsafe partial class Program
{
    private const int WmInput = 0x00FF;

    private const uint RidevRemove = 0x00000001;
    private const uint RidevNoLegacy = 0x00000030;
    private const uint RidevNoHotkeys = 0x00000200;

    private const ushort GenericDesktopUsagePage = 0x01;
    private const ushort KeyboardUsage = 0x06;

    private const int WhKeyboardLowLevel = 13;
    private const int HcAction = 0;
    private const uint FirstFunctionKey = 0x70;
    private const uint LastFunctionKey = 0x7B;
    private const uint LowLevelKeyUpFlag = 0x0080;

    private static readonly TimeSpan AutoExit = TimeSpan.FromMinutes(3);

    private static AntagonistMode mode;
    private static nint hookHandle;
    private static bool rawInputRegistered;
    private static volatile bool windowActive;
    private static long interceptedRawInput;
    private static long swallowedKeys;
    private static TextBlock? statusText;
    private static string setupResult = string.Empty;
    private static DateTime deadline;

    [STAThread]
    private static int Main(string[] args)
    {
        mode = ParseMode(args);
        deadline = DateTime.Now + AutoExit;

        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose,
        };

        statusText = new TextBlock
        {
            Margin = new Thickness(16),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
        };
        var window = new Window
        {
            Title = $"Foreground Antagonist [{mode}]",
            Width = 620,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new ScrollViewer { Content = statusText },
        };

        window.Activated += (_, _) => windowActive = true;
        window.Deactivated += (_, _) => windowActive = false;
        window.KeyDown += (_, keyArgs) =>
        {
            if (keyArgs.Key == Key.Escape)
            {
                window.Close();
            }
        };

        var windowHandle = new WindowInteropHelper(window).EnsureHandle();
        var source = HwndSource.FromHwnd(windowHandle);
        if (source is null)
        {
            return -1;
        }

        source.AddHook(ProcessWindowMessage);
        setupResult = Setup(windowHandle);
        WriteSetupLog();

        // 창이 어떤 이유로 사라져도 시스템 상태를 원래대로 돌려놓는다.
        window.Closed += (_, _) => Teardown();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Teardown();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Teardown();

        var timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250),
            DispatcherPriority.Background,
            (_, _) =>
            {
                if (DateTime.Now >= deadline)
                {
                    window.Close();
                    return;
                }

                UpdateStatus();
            },
            Dispatcher.CurrentDispatcher);
        timer.Start();

        UpdateStatus();
        window.Show();
        window.Activate();
        return application.Run(window);
    }

    private static AntagonistMode ParseMode(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (!string.Equals(args[index], "--mode", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return args[index + 1].ToLowerInvariant() switch
            {
                "nolegacy" => AntagonistMode.NoLegacy,
                "llhook" => AntagonistMode.LowLevelHook,
                "eatkeyup" => AntagonistMode.EatKeyUp,
                _ => AntagonistMode.None,
            };
        }

        return AntagonistMode.None;
    }

    private static string Setup(nint windowHandle)
    {
        switch (mode)
        {
            case AntagonistMode.NoLegacy:
            {
                var device = new RawInputDevice
                {
                    UsagePage = GenericDesktopUsagePage,
                    Usage = KeyboardUsage,
                    Flags = RidevNoLegacy | RidevNoHotkeys,
                    TargetWindow = windowHandle,
                };
                rawInputRegistered = RegisterRawInputDevices(
                    ref device,
                    1,
                    checked((uint)sizeof(RawInputDevice)));
                return rawInputRegistered
                    ? "키보드를 RIDEV_NOLEGACY | RIDEV_NOHOTKEYS로 독점 등록했습니다."
                    : $"독점 등록 실패. 오류코드={Marshal.GetLastWin32Error()}";
            }

            case AntagonistMode.LowLevelHook:
            case AntagonistMode.EatKeyUp:
            {
                hookHandle = SetWindowsHookExW(
                    WhKeyboardLowLevel,
                    &HookCallback,
                    GetModuleHandleW(null),
                    0);
                if (hookHandle == 0)
                {
                    return $"훅 설치 실패. 오류코드={Marshal.GetLastWin32Error()}";
                }

                return mode == AntagonistMode.EatKeyUp
                    ? "저수준 키보드 훅을 설치했습니다. 이 창이 활성일 때 F1~F12의 키업만 삼킵니다."
                    : "저수준 키보드 훅을 설치했습니다. 이 창이 활성일 때만 F1~F12를 삼킵니다.";
            }

            default:
                return "대조군입니다. 아무것도 가로채지 않습니다.";
        }
    }

    /// <summary>상대역이 실제로 가로채기에 성공했는지 남긴다. 키 입력 내용은 남기지 않는다.</summary>
    private static void WriteSetupLog()
    {
        try
        {
            var directory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TalesAlarmProbe");
            System.IO.Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(
                directory,
                $"antagonist-{mode.ToString().ToLowerInvariant()}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            System.IO.File.WriteAllText(
                path,
                $"[{DateTime.Now:HH:mm:ss.fff}] 모드={mode} 프로세스ID={Environment.ProcessId}"
                    + Environment.NewLine
                    + $"[{DateTime.Now:HH:mm:ss.fff}] {setupResult}" + Environment.NewLine,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (System.IO.IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void Teardown()
    {
        if (rawInputRegistered)
        {
            var removal = new RawInputDevice
            {
                UsagePage = GenericDesktopUsagePage,
                Usage = KeyboardUsage,
                Flags = RidevRemove,
                TargetWindow = 0,
            };
            RegisterRawInputDevices(ref removal, 1, checked((uint)sizeof(RawInputDevice)));
            rawInputRegistered = false;
        }

        if (hookHandle != 0)
        {
            UnhookWindowsHookEx(hookHandle);
            hookHandle = 0;
        }

        windowActive = false;
    }

    private static nint ProcessWindowMessage(
        nint windowHandle,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == WmInput)
        {
            interceptedRawInput++;
        }

        return 0;
    }

    [UnmanagedCallersOnly]
    private static nint HookCallback(int code, nint wParam, nint lParam)
    {
        try
        {
            if (code == HcAction && windowActive && lParam != 0)
            {
                var info = *(KeyboardLowLevelHookStruct*)lParam;
                if (info.VirtualKey is >= FirstFunctionKey and <= LastFunctionKey)
                {
                    // EatKeyUp은 키다운을 통과시키고 키업만 삼켜 눌림 상태 고착을 재현한다.
                    var isKeyUp = (info.Flags & LowLevelKeyUpFlag) != 0;
                    if (mode != AntagonistMode.EatKeyUp || isKeyUp)
                    {
                        Interlocked.Increment(ref swallowedKeys);
                        return 1;
                    }
                }
            }
        }
        catch
        {
            // 훅 콜백에서 예외가 네이티브 경계를 넘지 않게 한다.
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    private static void UpdateStatus()
    {
        if (statusText is null)
        {
            return;
        }

        var remaining = deadline - DateTime.Now;
        statusText.Text = $"""
            모드: {mode}
            {setupResult}

            이 창이 활성: {(windowActive ? "예" : "아니오")}
            가로챈 WM_INPUT: {interceptedRawInput}
            삼킨 F키: {Interlocked.Read(ref swallowedKeys)}

            자동 종료까지: {Math.Max(0, (int)remaining.TotalSeconds)}초 (Esc로 즉시 종료)

            사용법
            1) 먼저 RawInputProbe를 실행해 둡니다.
            2) 이 창을 클릭해 활성화합니다.
            3) F5를 3번 누릅니다.
            4) 프로브에서 비프음이 나면 도달, 안 나면 차단입니다.
            """;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint TargetWindow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardLowLevelHookStruct
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInformation;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterRawInputDevices(
        ref RawInputDevice devices,
        uint deviceCount,
        uint deviceSize);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookExW(
        int hookId,
        delegate* unmanaged<int, nint, nint, nint> callback,
        nint moduleHandle,
        uint threadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hookHandle);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(
        nint hookHandle,
        int code,
        nint wParam,
        nint lParam);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? moduleName);
}

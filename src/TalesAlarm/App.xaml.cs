using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TalesAlarm.Audio;
using TalesAlarm.Configuration;
using TalesAlarm.Hotkeys;
using TalesAlarm.Infrastructure;
using TalesAlarm.Timers;
using TalesAlarm.ViewModels;

namespace TalesAlarm;

public partial class App : System.Windows.Application
{
#if DEBUG
    private const bool AllowDataRootOverride = true;
#else
    private const bool AllowDataRootOverride = false;
#endif

    private FileLogger? logger;
    private SingleInstanceService? singleInstance;
    private GlobalHotkeyService? hotkeyService;
    private AlarmAudioService? alarmAudioService;
    private TrayService? trayService;
    private DispatcherTimer? dispatcherTimer;
    private MainWindow? mainWindow;
    private MainViewModel? mainViewModel;
    private HwndSource? windowSource;
    private HwndSourceHook? windowHook;
    private bool isExiting;
    private int errorDialogShown;
    private int pendingActivation;

    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var paths = AppPaths.FromArguments(eventArgs.Args, AllowDataRootOverride);
        logger = new FileLogger(paths, TimeProvider.System);
        logger.PruneOldLogs();
        RegisterExceptionHandlers();

        try
        {
            singleInstance = new SingleInstanceService(CreateInstanceName(eventArgs.Args, paths));
            if (!await singleInstance.TryAcquireAsync().ConfigureAwait(true))
            {
                try
                {
                    await singleInstance.SignalOwnerAsync().ConfigureAwait(true);
                }
                catch (Exception exception)
                {
                    logger.Write("기존 Tales Alarm 인스턴스를 활성화하지 못했습니다.", exception);
                }

                Shutdown(0);
                return;
            }

            singleInstance.ActivationRequested += OnActivationRequested;
            ComposeAndShowApplication(paths);
            await mainViewModel!.InitializeAsync().ConfigureAwait(true);

            mainWindow!.Show();
            dispatcherTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(50),
                DispatcherPriority.Background,
                OnDispatcherTick,
                Dispatcher);
            dispatcherTimer.Start();

            trayService = new TrayService(
                () => _ = Dispatcher.BeginInvoke((Action)ShowMainWindow),
                () => _ = Dispatcher.BeginInvoke((Action)ExitApplication));
            trayService.Show();

            if (Interlocked.Exchange(ref pendingActivation, 0) != 0)
            {
                ShowMainWindow();
            }
        }
        catch (Exception exception)
        {
            logger.Write("Tales Alarm 시작에 실패했습니다.", exception);
            ShowErrorDialog("Tales Alarm을 시작하지 못했습니다. 자세한 내용은 로그를 확인하세요.");
            isExiting = true;
            if (mainWindow is not null)
            {
                mainWindow.AllowClose = true;
            }

            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        isExiting = true;
        if (mainWindow is not null)
        {
            mainWindow.AllowClose = true;
        }

        if (dispatcherTimer is not null)
        {
            dispatcherTimer.Stop();
            dispatcherTimer.Tick -= OnDispatcherTick;
            dispatcherTimer = null;
        }

        trayService?.Dispose();
        trayService = null;

        if (windowSource is not null && windowHook is not null)
        {
            windowSource.RemoveHook(windowHook);
        }

        windowSource = null;
        windowHook = null;
        hotkeyService?.Dispose();
        hotkeyService = null;
        alarmAudioService?.Dispose();
        alarmAudioService = null;

        if (singleInstance is not null)
        {
            singleInstance.ActivationRequested -= OnActivationRequested;
            singleInstance.DisposeAsync().AsTask().GetAwaiter().GetResult();
            singleInstance = null;
        }

        UnregisterExceptionHandlers();
        base.OnExit(eventArgs);
    }

    private void ComposeAndShowApplication(AppPaths paths)
    {
        var defaults = AppSettings.CreateDefault();
        var settingsService = new SettingsService(paths, TimeProvider.System);
        hotkeyService = new GlobalHotkeyService(new Win32HotkeyNativeApi());
        var audioBackend = new MediaPlayerAudioBackend();
        alarmAudioService = new AlarmAudioService(TimeProvider.System, audioBackend);
        var audioProbe = new MediaPlayerAudioProbe(Dispatcher);
        var userAudioStore = new UserAudioStore(paths, audioProbe);
        var defaultAlarmInstaller = new DefaultAlarmInstaller(paths);
        mainViewModel = new MainViewModel(
            paths,
            new CountdownTimer(TimeProvider.System, defaults.Timer1.Duration),
            new CountdownTimer(TimeProvider.System, defaults.Timer2.Duration),
            settingsService,
            hotkeyService,
            alarmAudioService,
            userAudioStore,
            defaultAlarmInstaller);

        mainWindow = new MainWindow
        {
            DataContext = mainViewModel,
        };
        MainWindow = mainWindow;
        var windowHandle = new WindowInteropHelper(mainWindow).EnsureHandle();
        hotkeyService.Attach(windowHandle);
        windowSource = HwndSource.FromHwnd(windowHandle)
            ?? throw new InvalidOperationException("메인 창 메시지 소스를 만들지 못했습니다.");
        windowHook = ProcessWindowMessage;
        windowSource.AddHook(windowHook);
    }

    private nint ProcessWindowMessage(
        nint windowHandle,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (hotkeyService?.ProcessWindowMessage(message, wParam) == true)
        {
            handled = true;
        }

        return 0;
    }

    private void OnDispatcherTick(object? sender, EventArgs eventArgs) => mainViewModel?.Tick();

    private void OnActivationRequested(object? sender, EventArgs eventArgs)
    {
        if (mainWindow is null)
        {
            Interlocked.Exchange(ref pendingActivation, 1);
            return;
        }

        _ = Dispatcher.BeginInvoke((Action)ShowMainWindow);
    }

    private void ShowMainWindow()
    {
        if (!isExiting)
        {
            mainWindow?.ShowAndActivate();
        }
    }

    private void ExitApplication()
    {
        if (isExiting)
        {
            return;
        }

        isExiting = true;
        dispatcherTimer?.Stop();
        if (mainWindow is not null)
        {
            mainWindow.AllowClose = true;
            mainWindow.Close();
        }

        Shutdown(0);
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    private void UnregisterExceptionHandlers()
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        logger?.Write("처리되지 않은 UI 오류가 발생했습니다.", eventArgs.Exception);
        ShowErrorDialog("예기치 않은 오류가 발생했습니다. 앱은 계속 실행됩니다.");
        eventArgs.Handled = true;
    }

    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs eventArgs)
    {
        logger?.Write("관찰되지 않은 비동기 작업 오류가 발생했습니다.", eventArgs.Exception);
        eventArgs.SetObserved();
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
    {
        logger?.Write(
            "복구할 수 없는 프로세스 오류가 발생했습니다.",
            eventArgs.ExceptionObject as Exception);
    }

    private void ShowErrorDialog(string message)
    {
        if (Interlocked.Exchange(ref errorDialogShown, 1) != 0)
        {
            return;
        }

        System.Windows.MessageBox.Show(
            message,
            "Tales Alarm",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static string CreateInstanceName(IReadOnlyList<string> args, AppPaths paths)
    {
        if (!HasActiveDataRootOverride(args))
        {
            return "TalesAlarm";
        }

        var normalizedRoot = Path.GetFullPath(paths.RootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot)));
        return $"TalesAlarm.Debug.{hash[..12]}";
    }

    private static bool HasActiveDataRootOverride(IReadOnlyList<string> args)
    {
#if DEBUG
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (args[index] == "--data-root" && Path.IsPathFullyQualified(args[index + 1]))
            {
                return true;
            }
        }

        return false;
#else
        return false;
#endif
    }
}

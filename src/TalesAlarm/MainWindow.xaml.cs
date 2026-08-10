using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using TalesAlarm.ViewModels;

namespace TalesAlarm;

public partial class MainWindow : System.Windows.Window
{
    private const double DetailedWindowWidth = 1100;
    private const double DetailedWindowHeight = 760;
    private const double DetailedWindowMinWidth = 1040;
    private const double DetailedWindowMinHeight = 720;
    private const double CompactWindowWidth = 520;
    private const double CompactWindowHeight = 56;

    private IDisposable? hotkeyCaptureLease;

    public MainWindow()
    {
        InitializeComponent();
        ApplyDetailedWindowLayout();
    }

    public event EventHandler? RequestHide;

    public bool AllowClose { get; set; }

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private async void OnChangeAudioClick(object sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "알람 음원 선택",
            Filter = "지원 음원 (*.wav;*.mp3)|*.wav;*.mp3|WAV 파일 (*.wav)|*.wav|MP3 파일 (*.mp3)|*.mp3",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) == true)
        {
            await viewModel.Alarm.ImportAudioAsync(dialog.FileName);
        }
    }

    private void OnCaptureStarted(object sender, RoutedEventArgs eventArgs)
    {
        if (hotkeyCaptureLease is null && DataContext is MainViewModel viewModel)
        {
            hotkeyCaptureLease = viewModel.BeginHotkeyCapture();
        }
    }

    private void OnCaptureEnded(object sender, RoutedEventArgs eventArgs)
    {
        hotkeyCaptureLease?.Dispose();
        hotkeyCaptureLease = null;
    }

    private void OnCompactViewIsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.NewValue is true)
        {
            ApplyCompactWindowLayout();
        }
        else
        {
            ApplyDetailedWindowLayout();
        }
    }

    private void OnCompactDragDelta(object sender, DragDeltaEventArgs eventArgs)
    {
        Left += eventArgs.HorizontalChange;
        Top += eventArgs.VerticalChange;
    }

    private void OnCompactCloseClick(object sender, RoutedEventArgs eventArgs)
    {
        Close();
    }

    private void ApplyCompactWindowLayout()
    {
        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }

        MinWidth = CompactWindowWidth;
        MinHeight = CompactWindowHeight;
        Width = CompactWindowWidth;
        Height = CompactWindowHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
    }

    private void ApplyDetailedWindowLayout()
    {
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        MinWidth = DetailedWindowMinWidth;
        MinHeight = DetailedWindowMinHeight;
        Width = DetailedWindowWidth;
        Height = DetailedWindowHeight;
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        hotkeyCaptureLease?.Dispose();
        hotkeyCaptureLease = null;
        if (AllowClose)
        {
            return;
        }

        eventArgs.Cancel = true;
        Hide();
        RequestHide?.Invoke(this, EventArgs.Empty);
    }
}

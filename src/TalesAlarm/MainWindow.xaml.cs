using System.ComponentModel;
using System.Windows;
using TalesAlarm.ViewModels;

namespace TalesAlarm;

public partial class MainWindow : System.Windows.Window
{
    private IDisposable? hotkeyCaptureLease;

    public MainWindow()
    {
        InitializeComponent();
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

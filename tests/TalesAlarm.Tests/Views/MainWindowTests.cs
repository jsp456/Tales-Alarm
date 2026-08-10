using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TalesAlarm;

namespace TalesAlarm.Tests.Views;

public sealed class MainWindowTests
{
    // Break caught: compact mode regrows into a card window or loses its strip interactions.
    [Fact]
    public void CompactView_ProvidesTaskbarSizedWindowBehavior()
    {
        using var host = new WpfApplicationHost();
        MainWindow? window = null;
        WindowViewModel? viewModel = null;

        try
        {
            host.Invoke(() =>
            {
                viewModel = new WindowViewModel();
                window = new MainWindow
                {
                    DataContext = viewModel,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Left = -32_000,
                    Top = -32_000,
                };
                window.Show();
            });

            host.Invoke(() =>
            {
                var currentWindow = window!;
                var currentViewModel = viewModel!;
                var currentCompactView = Assert.IsAssignableFrom<FrameworkElement>(
                    currentWindow.FindName("CompactView"));
                Assert.Equal(Visibility.Visible, currentCompactView.Visibility);
                Assert.Equal(520, currentWindow.Width);
                Assert.Equal(56, currentWindow.Height);
                Assert.Equal(520, currentWindow.MinWidth);
                Assert.Equal(56, currentWindow.MinHeight);
                Assert.Equal(WindowStyle.None, currentWindow.WindowStyle);
                Assert.Equal(ResizeMode.NoResize, currentWindow.ResizeMode);

                var compactView = Assert.IsType<Border>(currentCompactView);
                Assert.Equal(Visibility.Visible, compactView.Visibility);
                var strip = Assert.IsType<Grid>(compactView.Child);
                Assert.Empty(strip.RowDefinitions);

                var template = Assert.IsType<DataTemplate>(
                    currentWindow.FindResource("CompactTimerTemplate"));
                currentWindow.UpdateLayout();
                var timerControl = Assert.Single(
                    strip.Children.OfType<ContentControl>(),
                    control => ReferenceEquals(control.Content, currentViewModel.Timer1));
                Assert.Same(template, timerControl.ContentTemplate);
                var timerTexts = GetVisualDescendants<TextBlock>(timerControl)
                    .Select(textBlock => textBlock.Text)
                    .ToArray();
                Assert.Contains("1", timerTexts);
                Assert.Contains("999:59:59", timerTexts);
                Assert.Contains("일시정지", timerTexts);
                Assert.InRange(timerControl.DesiredSize.Width, 1, 204);
                Assert.True(
                    CountDarkPixels(compactView, new Int32Rect(12, 8, 185, 40)) > 50,
                    "Timer 1 information is covered in the rendered strip.");
                Assert.True(
                    CountDarkPixels(compactView, new Int32Rect(225, 8, 185, 40)) > 50,
                    "Timer 2 information is covered in the rendered strip.");

                var compactButtons = GetLogicalDescendants<Button>(compactView).ToArray();
                var detailButton = Assert.Single(
                    compactButtons,
                    button => Equals(button.Content, "상세"));
                Assert.Same(currentViewModel.ToggleCompactViewCommand, detailButton.Command);
                Assert.Single(
                    compactButtons,
                    button => Equals(button.Content, "×"));

                var dragSurface = Assert.IsType<Thumb>(
                    currentWindow.FindName("CompactDragSurface"));
                currentWindow.Left = 100;
                currentWindow.Top = 200;
                dragSurface.RaiseEvent(new DragDeltaEventArgs(12, -7)
                {
                    RoutedEvent = Thumb.DragDeltaEvent,
                });
                Assert.Equal(112, currentWindow.Left);
                Assert.Equal(193, currentWindow.Top);
            }, DispatcherPriority.ApplicationIdle);

            host.Invoke(() => viewModel!.IsCompactView = false);
            host.Invoke(() =>
            {
                var currentWindow = window!;
                Assert.Equal(1100, currentWindow.Width);
                Assert.Equal(760, currentWindow.Height);
                Assert.Equal(WindowStyle.SingleBorderWindow, currentWindow.WindowStyle);
                Assert.Equal(ResizeMode.CanResize, currentWindow.ResizeMode);

                currentWindow.WindowState = WindowState.Maximized;
                viewModel!.IsCompactView = true;
            }, DispatcherPriority.ApplicationIdle);

            host.Invoke(() =>
            {
                var currentWindow = window!;
                Assert.Equal(WindowState.Normal, currentWindow.WindowState);
                Assert.Equal(520, currentWindow.Width);
                Assert.Equal(56, currentWindow.Height);

                var compactView = Assert.IsType<Border>(currentWindow.FindName("CompactView"));
                var closeButton = Assert.Single(
                    GetLogicalDescendants<Button>(compactView),
                    button => Equals(button.Content, "×"));
                var hideRequested = false;
                currentWindow.RequestHide += (_, _) => hideRequested = true;
                closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(hideRequested);
            }, DispatcherPriority.ApplicationIdle);
        }
        finally
        {
            if (window is not null)
            {
                host.Invoke(() =>
                {
                    window.AllowClose = true;
                    window.Close();
                });
            }
        }
    }

    private static IEnumerable<T> GetLogicalDescendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in GetLogicalDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<T> GetVisualDescendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in GetVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static int CountDarkPixels(Visual visual, Int32Rect region)
    {
        const int bitmapWidth = 520;
        const int bitmapHeight = 56;
        const int bytesPerPixel = 4;
        var bitmap = new RenderTargetBitmap(
            bitmapWidth,
            bitmapHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var stride = bitmapWidth * bytesPerPixel;
        var pixels = new byte[stride * bitmapHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        var darkPixelCount = 0;
        for (var y = region.Y; y < region.Y + region.Height; y++)
        {
            for (var x = region.X; x < region.X + region.Width; x++)
            {
                var offset = y * stride + x * bytesPerPixel;
                var blue = pixels[offset];
                var green = pixels[offset + 1];
                var red = pixels[offset + 2];
                if (red < 180 && green < 180 && blue < 180)
                {
                    darkPixelCount++;
                }
            }
        }

        return darkPixelCount;
    }

    public sealed class WindowViewModel : INotifyPropertyChanged
    {
        private bool isCompactView = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsCompactView
        {
            get => isCompactView;
            set
            {
                if (isCompactView == value)
                {
                    return;
                }

                isCompactView = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCompactView)));
            }
        }

        public TimerDisplay Timer1 { get; } = new(1, "999:59:59", "일시정지");

        public TimerDisplay Timer2 { get; } = new(2, "00:00:00", "완료");

        public ICommand ToggleCompactViewCommand { get; } = new NoOpCommand();
    }

    public sealed record TimerDisplay(int TimerIndex, string DisplayTime, string StatusText);

    private sealed class NoOpCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }
    }

    private sealed class WpfApplicationHost : IDisposable
    {
        private readonly ManualResetEventSlim ready = new(false);
        private readonly Thread thread;
        private Dispatcher? dispatcher;
        private TestApplication? application;
        private Exception? startupException;

        public WpfApplicationHost()
        {
            thread = new Thread(RunDispatcher)
            {
                IsBackground = true,
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            Assert.True(ready.Wait(TimeSpan.FromSeconds(15)), "WPF dispatcher did not start.");
            if (startupException is not null)
            {
                throw new InvalidOperationException(
                    "WPF application initialization failed.",
                    startupException);
            }
        }

        public void Invoke(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
        {
            dispatcher!.Invoke(action, priority);
        }

        public void Dispose()
        {
            if (dispatcher is not null && !dispatcher.HasShutdownStarted)
            {
                dispatcher.Invoke(() => application!.Shutdown(), DispatcherPriority.Send);
            }

            Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "WPF dispatcher did not stop.");
            ready.Dispose();
        }

        private void RunDispatcher()
        {
            try
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                application = new TestApplication();
            }
            catch (Exception exception)
            {
                startupException = exception;
            }
            finally
            {
                ready.Set();
            }

            if (startupException is null)
            {
                application!.Run();
            }
        }
    }

    private sealed class TestApplication : Application
    {
        public TestApplication()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var buttonStyle = new Style(typeof(Button));
            buttonStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 44d));
            buttonStyle.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(18, 8, 18, 8)));
            buttonStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4)));
            buttonStyle.Setters.Add(new Setter(Button.FontSizeProperty, 14d));
            buttonStyle.Setters.Add(new Setter(
                FrameworkElement.CursorProperty,
                System.Windows.Input.Cursors.Hand));
            Resources.Add(typeof(Button), buttonStyle);
            Resources.Add("PrimaryButton", new Style(typeof(Button), buttonStyle));
            Resources.Add("CardBorder", new Style(typeof(Border)));
        }
    }
}

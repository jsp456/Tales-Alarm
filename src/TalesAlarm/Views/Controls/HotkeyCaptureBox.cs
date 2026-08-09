using System.Windows;
using System.Windows.Input;
using TalesAlarm.Hotkeys;

namespace TalesAlarm.Views.Controls;

public sealed class HotkeyCaptureBox : System.Windows.Controls.Control
{
    public static readonly DependencyProperty GestureProperty = DependencyProperty.Register(
        nameof(Gesture),
        typeof(HotkeyGesture),
        typeof(HotkeyCaptureBox),
        new FrameworkPropertyMetadata(
            default(HotkeyGesture),
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly RoutedEvent CaptureStartedEvent = EventManager.RegisterRoutedEvent(
        nameof(CaptureStarted),
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(HotkeyCaptureBox));

    public static readonly RoutedEvent CaptureEndedEvent = EventManager.RegisterRoutedEvent(
        nameof(CaptureEnded),
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(HotkeyCaptureBox));

    private bool isCapturing;

    static HotkeyCaptureBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(HotkeyCaptureBox),
            new FrameworkPropertyMetadata(typeof(HotkeyCaptureBox)));
    }

    public HotkeyCaptureBox()
    {
        Focusable = true;
        IsTabStop = true;
    }

    public event RoutedEventHandler CaptureStarted
    {
        add => AddHandler(CaptureStartedEvent, value);
        remove => RemoveHandler(CaptureStartedEvent, value);
    }

    public event RoutedEventHandler CaptureEnded
    {
        add => AddHandler(CaptureEndedEvent, value);
        remove => RemoveHandler(CaptureEndedEvent, value);
    }

    public HotkeyGesture Gesture
    {
        get => (HotkeyGesture)GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    public static HotkeyGesture? CreateGesture(
        Key key,
        Key systemKey,
        ModifierKeys modifiers)
    {
        if (key == Key.Escape)
        {
            return null;
        }

        var resolvedKey = key == Key.System ? systemKey : key;
        var hotkeyModifiers = HotkeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            hotkeyModifiers |= HotkeyModifiers.Control;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            hotkeyModifiers |= HotkeyModifiers.Alt;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            hotkeyModifiers |= HotkeyModifiers.Shift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            hotkeyModifiers |= HotkeyModifiers.Windows;
        }

        var gesture = new HotkeyGesture(resolvedKey, hotkeyModifiers);
        return gesture.HasNonModifierKey ? gesture : null;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs eventArgs)
    {
        base.OnPreviewMouseLeftButtonDown(eventArgs);
        Focus();
        BeginCapture();
        eventArgs.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs eventArgs)
    {
        base.OnGotKeyboardFocus(eventArgs);
        BeginCapture();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs eventArgs)
    {
        EndCapture();
        base.OnLostKeyboardFocus(eventArgs);
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs eventArgs)
    {
        base.OnPreviewKeyDown(eventArgs);
        BeginCapture();
        if (eventArgs.Key == Key.Escape)
        {
            eventArgs.Handled = true;
            EndCapture();
            Keyboard.ClearFocus();
            return;
        }

        var gesture = CreateGesture(eventArgs.Key, eventArgs.SystemKey, Keyboard.Modifiers);
        eventArgs.Handled = true;
        if (gesture is null)
        {
            return;
        }

        SetCurrentValue(GestureProperty, gesture.Value);
        EndCapture();
        Keyboard.ClearFocus();
    }

    private void BeginCapture()
    {
        if (isCapturing)
        {
            return;
        }

        isCapturing = true;
        RaiseEvent(new RoutedEventArgs(CaptureStartedEvent, this));
    }

    private void EndCapture()
    {
        if (!isCapturing)
        {
            return;
        }

        isCapturing = false;
        RaiseEvent(new RoutedEventArgs(CaptureEndedEvent, this));
    }
}

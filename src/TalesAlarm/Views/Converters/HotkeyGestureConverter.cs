using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;
using TalesAlarm.Hotkeys;

namespace TalesAlarm.Views.Converters;

public sealed class HotkeyGestureConverter : IValueConverter
{
    public static string Format(HotkeyGesture gesture)
    {
        if (!gesture.HasNonModifierKey)
        {
            return "키를 입력하세요";
        }

        var parts = new List<string>(5);
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(FormatKey(gesture.Key));
        return string.Join(" + ", parts);
    }

    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        value is HotkeyGesture gesture ? Format(gesture) : "키를 입력하세요";

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) => System.Windows.Data.Binding.DoNothing;

    private static string FormatKey(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)key - (int)Key.D0).ToString(CultureInfo.InvariantCulture);
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return $"Num {((int)key - (int)Key.NumPad0).ToString(CultureInfo.InvariantCulture)}";
        }

        return key switch
        {
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            _ => key.ToString(),
        };
    }
}

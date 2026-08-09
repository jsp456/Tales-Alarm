using System.Windows;
using System.Windows.Input;

namespace TalesAlarm.Views.Controls;

public enum NumericInputMode
{
    None,
    Digits,
    Decimal,
}

public static class NumericTextBoxBehavior
{
    public static readonly DependencyProperty ModeProperty = DependencyProperty.RegisterAttached(
        "Mode",
        typeof(NumericInputMode),
        typeof(NumericTextBoxBehavior),
        new PropertyMetadata(NumericInputMode.None, OnModeChanged));

    public static NumericInputMode GetMode(DependencyObject element) =>
        (NumericInputMode)element.GetValue(ModeProperty);

    public static void SetMode(DependencyObject element, NumericInputMode value) =>
        element.SetValue(ModeProperty, value);

    public static bool IsValid(string text, NumericInputMode mode) => mode switch
    {
        NumericInputMode.Digits => text.All(character => character is >= '0' and <= '9'),
        NumericInputMode.Decimal =>
            text.Count(character => character == '.') <= 1
            && text.All(character => character == '.' || character is >= '0' and <= '9'),
        _ => true,
    };

    private static void OnModeChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not System.Windows.Controls.TextBox textBox)
        {
            throw new ArgumentException("NumericTextBoxBehavior는 TextBox에만 사용할 수 있습니다.");
        }

        if ((NumericInputMode)eventArgs.OldValue != NumericInputMode.None)
        {
            textBox.PreviewTextInput -= OnPreviewTextInput;
            System.Windows.DataObject.RemovePastingHandler(textBox, OnPaste);
        }

        if ((NumericInputMode)eventArgs.NewValue != NumericInputMode.None)
        {
            textBox.PreviewTextInput += OnPreviewTextInput;
            System.Windows.DataObject.AddPastingHandler(textBox, OnPaste);
        }
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs eventArgs)
    {
        var textBox = (System.Windows.Controls.TextBox)sender;
        var candidate = ReplaceSelection(textBox, eventArgs.Text);
        eventArgs.Handled = !IsValid(candidate, GetMode(textBox));
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs eventArgs)
    {
        var textBox = (System.Windows.Controls.TextBox)sender;
        if (!eventArgs.DataObject.GetDataPresent(System.Windows.DataFormats.UnicodeText)
            || eventArgs.DataObject.GetData(System.Windows.DataFormats.UnicodeText) is not string pastedText
            || !IsValid(ReplaceSelection(textBox, pastedText), GetMode(textBox)))
        {
            eventArgs.CancelCommand();
        }
    }

    private static string ReplaceSelection(
        System.Windows.Controls.TextBox textBox,
        string replacement) =>
        textBox.Text
            .Remove(textBox.SelectionStart, textBox.SelectionLength)
            .Insert(textBox.SelectionStart, replacement);
}

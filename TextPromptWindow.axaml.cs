using Avalonia.Input;
using Avalonia.Interactivity;

using AppUi = DropAndForget.UI;

namespace DropAndForget;

public partial class TextPromptWindow : AppUi.Window
{
    public TextPromptWindow()
        : this("Input", "Prompt", string.Empty)
    {
    }

    public TextPromptWindow(string title, string prompt, string initialValue)
    {
        InitializeComponent();

        Title = title;
        PromptTextBlock.Text = prompt;
        ValueTextBox.Text = initialValue;

        Opened += OnOpened;
        ValueTextBox.KeyDown += ValueTextBox_KeyDown;
    }

    private void OnOpened(object? sender, System.EventArgs e)
    {
        ValueTextBox.Focus();
        ValueTextBox.SelectAll();
    }

    private void ConfirmButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(ValueTextBox.Text?.Trim());
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void ValueTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Close(ValueTextBox.Text?.Trim());
        }
        else if (e.Key == Key.Escape)
        {
            Close(null);
        }
    }
}

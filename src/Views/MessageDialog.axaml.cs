using Avalonia.Controls;
using Avalonia.Interactivity;
using pianoroll.Helpers;

namespace pianoroll.Views;

/// <summary>A message with an OK button: a bold heading over the details.</summary>
public partial class MessageDialog : DialogWindow
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    public static Task ShowAsync(Window owner, string heading, string message, bool isError)
    {
        var dialog = new MessageDialog { Title = isError ? "Error" : owner.Title };
        dialog.IconText.Text = isError ? Icons.AlertCircleOutline : Icons.CheckCircleOutline;
        dialog.IconText.Classes.Add(isError ? "error" : "success");
        dialog.HeadingText.Text = heading;
        dialog.MessageText.Text = message;
        return dialog.ShowModalAsync(owner);
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close();
}

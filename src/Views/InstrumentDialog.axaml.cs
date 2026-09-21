using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using pianoroll.Models;

namespace pianoroll.Views;

/// <summary>Picks the instrument the keyboard is played with. Cancelling returns null.</summary>
public partial class InstrumentDialog : DialogWindow
{
    public InstrumentDialog()
    {
        InitializeComponent();
        InstrumentList.ItemsSource = InstrumentInfo.All;
    }

    /// <summary>Shows the dialog with <paramref name="current"/> selected.</summary>
    public static Task<InstrumentInfo?> AskAsync(Window owner, InstrumentKind current)
    {
        var dialog = new InstrumentDialog();
        dialog.InstrumentList.SelectedItem = InstrumentInfo.For(current);
        return dialog.ShowModalAsync<InstrumentInfo?>(owner);
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Choose();

    private void List_DoubleTapped(object? sender, TappedEventArgs e) => Choose();

    private void Choose()
    {
        if (InstrumentList.SelectedItem is InstrumentInfo instrument)
            CloseWith(instrument);
        else
            Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}

using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using pianoroll.Helpers;

namespace pianoroll.Views;

/// <summary>The About screen: the program's name, what it does, and build information.</summary>
public partial class AboutWindow : DialogWindow
{
    private readonly List<(string Name, SelectableTextBlock Value)> _rows = [];

    public AboutWindow()
    {
        InitializeComponent();
        NameText.Text = BuildInfo.ProductName;
        VersionText.Text = $"Version {BuildInfo.Version}";

        foreach (var (name, value) in BuildInfo.Details())
            AddRow(name, value);
    }

    private SelectableTextBlock AddRow(string name, string value)
    {
        var row = _rows.Count;
        DetailsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var nameText = new TextBlock { Text = name, Margin = new(0, 1, 16, 1) };
        nameText.Classes.Add("name");
        Grid.SetRow(nameText, row);

        var valueText = new SelectableTextBlock
        {
            Text = value,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new(0, 1),
        };
        Grid.SetRow(valueText, row);
        Grid.SetColumn(valueText, 1);

        DetailsGrid.Children.Add(nameText);
        DetailsGrid.Children.Add(valueText);
        _rows.Add((name, valueText));
        return valueText;
    }

    private async void CopyDetails_Click(object? sender, RoutedEventArgs e)
    {
        var text = $"{BuildInfo.ProductName}\n" +
            string.Join("\n", _rows.Select(r => $"{r.Name}: {r.Value.Text}"));
        try
        {
            if (Clipboard is { } clipboard)
                await ClipboardExtensions.SetTextAsync(clipboard, text);
        }
        catch (Exception)
        {
            // Clipboard unavailable; the details can still be selected and copied by hand.
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}

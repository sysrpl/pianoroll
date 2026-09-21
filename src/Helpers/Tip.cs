using Avalonia;
using Avalonia.Controls;

namespace pianoroll.Helpers;

/// <summary>What a rich tooltip shows: a bold title, and a short description below it.</summary>
public sealed record TipContent(string Title, string? Text);

/// <summary>
/// Attached properties for rich tooltips. In XAML:
/// <code>h:Tip.Title="Upload" h:Tip.Text="Upload the selected files."</code>
/// sets the control's ToolTip.Tip to a <see cref="TipContent"/>, which the theme draws as a bold
/// title over the description, in a bubble whose tail points up at the control.
/// </summary>
public sealed class Tip : AvaloniaObject
{
    public static readonly AttachedProperty<string?> TitleProperty =
        AvaloniaProperty.RegisterAttached<Tip, Control, string?>("Title");

    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<Tip, Control, string?>("Text");

    static Tip()
    {
        TitleProperty.Changed.AddClassHandler<Control>((control, _) => Update(control));
        TextProperty.Changed.AddClassHandler<Control>((control, _) => Update(control));
    }

    public static string? GetTitle(Control control) => control.GetValue(TitleProperty);
    public static void SetTitle(Control control, string? value) => control.SetValue(TitleProperty, value);

    public static string? GetText(Control control) => control.GetValue(TextProperty);
    public static void SetText(Control control, string? value) => control.SetValue(TextProperty, value);

    /// <summary>Sets both parts at once, e.g. for tooltips that change with state.</summary>
    public static void Set(Control control, string title, string? text)
    {
        SetTitle(control, title);
        SetText(control, text);
    }

    private static void Update(Control control)
    {
        var title = GetTitle(control);
        ToolTip.SetTip(control, title is null ? null : new TipContent(title, GetText(control)));
    }
}

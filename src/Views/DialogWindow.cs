using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace pianoroll.Views;

/// <summary>
/// Base for the app's dialogs. Show one with <see cref="ShowModalAsync{T}"/> and finish it with
/// <see cref="CloseWith"/>; closing it any other way (Cancel, Escape, the window's X) gives the
/// result type's default (null or false).
/// </summary>
public class DialogWindow : Window
{
    private object? _result;

    /// <summary>Closes the dialog, making <paramref name="result"/> what <see cref="ShowModalAsync{T}"/> returns.</summary>
    protected void CloseWith(object? result)
    {
        _result = result;
        Close();
    }

    /// <summary>Shows the dialog over <paramref name="owner"/> and waits for it to close.</summary>
    public Task ShowModalAsync(Window owner) => ShowModalAsync<object?>(owner);

    /// <summary>Shows the dialog over <paramref name="owner"/> and returns what it closed with.</summary>
    public async Task<T> ShowModalAsync<T>(Window owner)
    {
        if (OperatingSystem.IsLinux())
            await ShowOverOwnerAsync(owner);
        else
            await ShowDialog(owner);
        return _result is T value ? value : default!;
    }

    /// <summary>
    /// Modal without <see cref="Window.ShowDialog(Window)"/>. On X11, ShowDialog disables the owner
    /// by removing its maximize function and fixing its size, and window managers such as Cinnamon
    /// then un-maximize and move a maximized owner. Instead, the dialog is shown as the owner's
    /// child (so it stays on top), the owner ignores the mouse, and activating the owner brings
    /// the dialog back to the front.
    /// </summary>
    private Task ShowOverOwnerAsync(Window owner)
    {
        var done = new TaskCompletionSource();
        var ownerContent = owner.Content as InputElement;

        void BringToFront(object? sender, EventArgs e) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (IsVisible)
                    Activate();
            });

        owner.Activated += BringToFront;
        if (ownerContent is not null)
            ownerContent.IsHitTestVisible = false;

        Closed += (_, _) =>
        {
            owner.Activated -= BringToFront;
            if (ownerContent is not null)
                ownerContent.IsHitTestVisible = true;
            owner.Activate();
            done.TrySetResult();
        };

        Show(owner);
        return done.Task;
    }
}

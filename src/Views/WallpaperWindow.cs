using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using pianoroll.Helpers;

namespace pianoroll.Views;

/// <summary>
/// A plain picture filling the screen behind the player (F1), so the player can be recorded
/// against a clean backdrop instead of whatever is on the desktop — panel included.
///
/// It is a full-screen window, since only full-screen windows are allowed above the desktop
/// panel, with the player marked as belonging in front of it (see <see cref="X11Stacking"/>).
/// Whenever it is clicked or otherwise brought forward, it hands the focus straight back to
/// the player.
/// </summary>
public sealed class WallpaperWindow : Window
{
    private readonly Window _front;

    /// <param name="front">The window to keep in front: the player.</param>
    public WallpaperWindow(Window front)
    {
        _front = front;

        Title = "Piano Roll backdrop";
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Background = Brushes.Black;
        Icon = front.Icon;

        using (var stream = AssetLoader.Open(new Uri("avares://pianoroll/images/wallpaper.jpg")))
        {
            Content = new Image
            {
                Source = new Bitmap(stream),
                // Fills the screen whatever its shape, cropping rather than leaving bars.
                Stretch = Stretch.UniformToFill,
            };
        }

        // Full screen on the screen the player is on: placed there first, then filled.
        WindowStartupLocation = WindowStartupLocation.Manual;
        if ((front.Screens.ScreenFromWindow(front) ?? front.Screens.Primary) is { } screen)
        {
            Position = screen.Bounds.Position;
            Width = screen.Bounds.Width / screen.Scaling;
            Height = screen.Bounds.Height / screen.Scaling;
        }
        WindowState = WindowState.FullScreen;

        // Once both windows exist, the player is marked as belonging in front of this one,
        // and given the focus back.
        Opened += (_, _) =>
        {
            X11Stacking.KeepInFront(_front, this);
            Dispatcher.UIThread.Post(() => _front.Activate());
        };

        // The player must stand on its own again before this window goes.
        Closing += (_, _) => X11Stacking.Release(_front);

        // Never let the backdrop stay in front of the player.
        Activated += (_, _) => Dispatcher.UIThread.Post(() => _front.Activate());
    }

    /// <summary>F1 here too, in case the backdrop has the keyboard for a moment.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            e.Handled = true;
            Close();
        }
        base.OnKeyDown(e);
    }
}

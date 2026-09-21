using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace pianoroll.Helpers;

/// <summary>
/// Tells an X11 window manager that one window belongs in front of another, without Avalonia's
/// window ownership (which would close the player along with the backdrop).
///
/// This is what lets the F1 backdrop cover the desktop panel. A panel sits above ordinary
/// windows; only a full-screen window goes above it, and window managers keep a full-screen
/// window up there only while it — or a window transient for it, like a dialog — has the focus.
/// Marking the player transient for the backdrop keeps the backdrop over the panel and the
/// player over the backdrop, the way a dialog sits over a full-screen game.
///
/// Anywhere but X11 these do nothing.
/// </summary>
public static class X11Stacking
{
    /// <summary>The predefined WM_TRANSIENT_FOR atom.</summary>
    private static readonly IntPtr TransientForAtom = 68;

    /// <summary>Marks <paramref name="front"/> as belonging in front of <paramref name="behind"/>.</summary>
    public static void KeepInFront(Window front, Window behind)
    {
        if (Xid(front) is not { } frontXid || Xid(behind) is not { } behindXid)
            return;

        WithDisplay(display => XSetTransientForHint(display, frontXid, behindXid));
    }

    /// <summary>Undoes <see cref="KeepInFront"/>, so the window stands on its own again.</summary>
    public static void Release(Window front)
    {
        if (Xid(front) is not { } frontXid)
            return;

        WithDisplay(display => XDeleteProperty(display, frontXid, TransientForAtom));
    }

    private static IntPtr? Xid(Window window) =>
        OperatingSystem.IsLinux() && window.TryGetPlatformHandle() is { HandleDescriptor: "XID" } handle
            ? handle.Handle
            : null;

    /// <summary>
    /// Runs <paramref name="action"/> on a connection of our own to the X server. Window
    /// properties can be set from any connection, so there's no need for Avalonia's.
    /// </summary>
    private static void WithDisplay(Action<IntPtr> action)
    {
        try
        {
            var display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
                return;

            try
            {
                action(display);
                XFlush(display);
            }
            finally
            {
                XCloseDisplay(display);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // No Xlib (Wayland without XWayland, say): the backdrop just won't cover the panel.
        }
    }

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr name);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XSetTransientForHint(IntPtr display, IntPtr window, IntPtr owner);

    [DllImport("libX11.so.6")]
    private static extern int XDeleteProperty(IntPtr display, IntPtr window, IntPtr property);
}

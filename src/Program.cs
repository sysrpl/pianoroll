using Avalonia;

namespace pianoroll;

internal static class Program
{
    // Don't use any Avalonia, third-party APIs or any SynchronizationContext-reliant
    // code before AppMain is called: things aren't initialized yet.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Avalonia's desktop-portal file picker never passes a starting folder to the
            // portal, so Open always came up in the home folder. Turning it off falls back to
            // Avalonia's own file dialog, which opens where it is told to (the music folder).
            // The option is ignored on Windows and macOS, whose dialogs honour it already.
            .With(new X11PlatformOptions { UseDBusFilePicker = false })
            .LogToTrace();
}

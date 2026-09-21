using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;

namespace pianoroll.Helpers;

/// <summary>Version and build details for Help > About.</summary>
public static class BuildInfo
{
    private static readonly Assembly App = typeof(BuildInfo).Assembly;

    public static string ProductName =>
        App.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Piano Roll";

    /// <summary>The version from the project file (without the "+commit" suffix the SDK may add).</summary>
    public static string Version => InformationalVersion(App).Split('+')[0];

    /// <summary>
    /// (name, value) lines describing this build and where it's running. The FFmpeg version isn't
    /// here, as finding it runs ffmpeg; see <see cref="Services.FFmpeg.VersionAsync"/>.
    /// </summary>
    public static IReadOnlyList<(string Name, string Value)> Details()
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        return
        [
            ("Version", InformationalVersion(App)),
            ("Built", BuildDate()),
            ("Configuration", configuration),
            (".NET", RuntimeInformation.FrameworkDescription),
            ("Operating system", $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})"),
            ("Avalonia", InformationalVersion(typeof(AvaloniaObject).Assembly)),
        ];
    }

    /// <summary>The BuildDate stamped in by pianoroll.csproj, in local time.</summary>
    private static string BuildDate()
    {
        var value = App.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildDate")?.Value;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? date.ToLocalTime().ToString("ddd d MMM yyyy, HH:mm")
            : "unknown";
    }

    private static string InformationalVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "unknown";
}

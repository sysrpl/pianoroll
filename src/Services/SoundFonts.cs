namespace pianoroll.Services;

/// <summary>Finding a SoundFont to play with, without asking the user on every platform.</summary>
public static class SoundFonts
{
    /// <summary>
    /// The first SoundFont found in the usual places, or null. Linux distributions install
    /// FluidR3 or TimGM under /usr/share/sounds/sf2; on macOS and Windows there is no standard
    /// bank, so the app falls back to its own synthesis until one is chosen by hand.
    /// </summary>
    public static string? FindDefault()
    {
        foreach (var candidate in Candidates())
        {
            try
            {
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable path is simply not a candidate.
            }
        }
        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        // Next to the program first, so a copy shipped with it wins.
        var beside = AppContext.BaseDirectory;
        yield return Path.Combine(beside, "soundfonts", "default.sf2");
        yield return Path.Combine(beside, "default.sf2");

        if (OperatingSystem.IsLinux())
        {
            yield return "/usr/share/sounds/sf2/default-GM.sf2";
            yield return "/usr/share/sounds/sf2/FluidR3_GM.sf2";
            yield return "/usr/share/soundfonts/default.sf2";
            yield return "/usr/share/soundfonts/FluidR3_GM.sf2";
            yield return "/usr/share/sounds/sf2/TimGM6mb.sf2";
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return "/Library/Audio/Sounds/Banks/FluidR3_GM.sf2";
            yield return Path.Combine(home, "Library", "Audio", "Sounds", "Banks", "FluidR3_GM.sf2");
        }
        else if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Path.Combine(programFiles, "soundfonts", "FluidR3_GM.sf2");
        }
    }
}

namespace pianoroll.Services;

/// <summary>The MIDI files that come with the program, which the Open dialog starts in.</summary>
public static class MusicLibrary
{
    /// <summary>The music folder, or null when it isn't there.</summary>
    public static string? FindDefault() => ContentFolder.Find("music", "*.mid");
}

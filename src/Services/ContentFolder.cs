namespace pianoroll.Services;

/// <summary>
/// Finds the folders of content that ship beside the program — samples and music — without
/// baking them into the build. They sit next to the executable once published, and up in the
/// project folder while developing, so both are looked at.
/// </summary>
public static class ContentFolder
{
    /// <summary>
    /// The first folder called <paramref name="path"/> that holds a file matching
    /// <paramref name="pattern"/>, or null when there isn't one.
    /// </summary>
    public static string? Find(string path, string pattern)
    {
        foreach (var root in Roots())
        {
            try
            {
                var folder = Path.Combine(root, path);
                if (Directory.Exists(folder) && Directory.EnumerateFiles(folder, pattern).Any())
                    return folder;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable path is simply not a candidate.
            }
        }
        return null;
    }

    private static IEnumerable<string> Roots()
    {
        // Next to the program, where publish.sh puts things.
        yield return AppContext.BaseDirectory;

        // And, while developing, up out of bin/Debug/net8.0 to the project's own folder.
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 5 && folder is not null; i++, folder = folder.Parent)
            yield return folder.FullName;
    }
}

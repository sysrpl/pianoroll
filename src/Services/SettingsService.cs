using System.Text.Json;

namespace pianoroll.Services;

/// <summary>Values the main window remembers between sessions.</summary>
public sealed class AppSettings
{
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }

    /// <summary>The instrument last played, by <c>InstrumentKind</c> name.</summary>
    public string Instrument { get; set; } = "Piano";

    /// <summary>How loud the keyboard is, 0 to 1.</summary>
    public double Volume { get; set; } = 0.6;

    /// <summary>The folder the last MIDI file was opened from, so the picker starts there again.</summary>
    public string LastMidiFolder { get; set; } = "";

    /// <summary>
    /// The SoundFont to play with. Empty means "find one", and "none" means the user chose the
    /// built-in synthesis on purpose.
    /// </summary>
    public string SoundFontPath { get; set; } = "";
}

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as settings.json in the app data folder
/// (~/.config/pianoroll on Linux).
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public SettingsService(string? folder = null)
    {
        folder ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pianoroll");
        _path = Path.Combine(folder, "settings.json");
        Settings = Load();
    }

    public AppSettings Settings { get; private set; }

    /// <summary>Saves through a temporary file, so a crash can't leave a half-written file.</summary>
    public void Save(AppSettings settings)
    {
        Settings = settings;
        var temporary = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Saving settings must never stop the window from closing.
            try { File.Delete(temporary); } catch (Exception) { }
        }
    }

    /// <summary>The saved settings, or the defaults if there are none or the file can't be read.</summary>
    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Fall back to the defaults; the next save writes a good file again.
        }
        return new AppSettings();
    }
}

namespace pianoroll.Models;

/// <summary>
/// One note from a MIDI file, in seconds from the start of the song. Length is what makes a
/// whole note's bar four times as long as a quarter note's on the roll.
/// </summary>
public sealed record MidiNote(int Note, double Start, double Duration, int Velocity)
{
    public double End => Start + Duration;
}

/// <summary>A press or release of the sustain pedal, in seconds from the start.</summary>
public sealed record SustainChange(double Time, bool Down);

/// <summary>A MIDI file, flattened to the notes it plays and when.</summary>
public sealed class MidiSong
{
    public MidiSong(string path, IReadOnlyList<MidiNote> notes, IReadOnlyList<SustainChange> sustain, double duration)
    {
        Sustain = sustain;
        Path = path;
        Name = System.IO.Path.GetFileNameWithoutExtension(path);
        Notes = notes;
        Duration = duration;

        LowestNote = notes.Count > 0 ? notes.Min(n => n.Note) : 60;
        HighestNote = notes.Count > 0 ? notes.Max(n => n.Note) : 72;
    }

    public string Path { get; }
    public string Name { get; }

    /// <summary>
    /// The name tidied for the window title: underscores and hyphens become spaces, a stray
    /// "mid" or "midi" left over from the file name is dropped, and each word is capitalised —
    /// "rocket_man-mid" becomes "Rocket Man".
    /// </summary>
    public string Title
    {
        get
        {
            var words = Name.Replace('_', ' ').Replace('-', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !w.Equals("mid", StringComparison.OrdinalIgnoreCase)
                         && !w.Equals("midi", StringComparison.OrdinalIgnoreCase))
                .Select(w => char.ToUpper(w[0]) + w[1..].ToLower());

            var title = string.Join(' ', words);
            return title.Length > 0 ? title : Name;
        }
    }

    /// <summary>Every note in the file, ordered by when it starts.</summary>
    public IReadOnlyList<MidiNote> Notes { get; }

    /// <summary>Every sustain pedal change, in order. Empty when the file doesn't use the pedal.</summary>
    public IReadOnlyList<SustainChange> Sustain { get; }

    /// <summary>How long the song runs, in seconds.</summary>
    public double Duration { get; }

    public int LowestNote { get; }
    public int HighestNote { get; }

    /// <summary>The index of the first note starting at or after <paramref name="seconds"/>.</summary>
    public int IndexAt(double seconds)
    {
        var low = 0;
        var high = Notes.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (Notes[middle].Start < seconds)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }
}

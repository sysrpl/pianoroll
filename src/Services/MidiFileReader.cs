using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using pianoroll.Models;

namespace pianoroll.Services;

/// <summary>Reads a MIDI file into the notes the roll draws and the keyboard plays.</summary>
public static class MidiFileReader
{
    /// <summary>The file patterns the Open dialog offers.</summary>
    public static IReadOnlyList<string> Patterns { get; } = ["*.mid", "*.midi", "*.kar", "*.rmi"];

    /// <summary>
    /// Loads <paramref name="path"/>, turning MIDI ticks into seconds with the file's tempo map,
    /// so tempo changes partway through a song are followed.
    /// </summary>
    /// <param name="leadIn">
    /// How long to wait before the first note sounds. Plenty of files start a note at the very
    /// beginning, which would have it touching the keyboard the moment the file opened. Given
    /// the roll's look-ahead, the first note instead starts at the top of the screen and falls
    /// the whole way down.
    /// </param>
    /// <exception cref="InvalidDataException">The file isn't MIDI, or is damaged.</exception>
    public static MidiSong Load(string path, double leadIn = 4)
    {
        MidiFile file;
        try
        {
            file = MidiFile.Read(path);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} could not be read as a MIDI file.", ex);
        }

        var tempoMap = file.GetTempoMap();
        var notes = new List<MidiNote>();

        foreach (var note in file.GetNotes())
        {
            // Channel 10 is percussion: its "notes" are drum sounds, not pitches, so playing
            // them on a piano keyboard would just be noise.
            if (note.Channel == 9)
                continue;

            var start = note.TimeAs<MetricTimeSpan>(tempoMap).TotalMicroseconds / 1_000_000.0;
            var length = note.LengthAs<MetricTimeSpan>(tempoMap).TotalMicroseconds / 1_000_000.0;

            notes.Add(new MidiNote(note.NoteNumber, start, Math.Max(length, 0.05), note.Velocity));
        }

        notes.Sort((a, b) => a.Start.CompareTo(b.Start));

        // Controller 64 is the sustain pedal: anything from 64 up counts as down.
        var sustain = new List<SustainChange>();
        foreach (var timedEvent in file.GetTimedEvents())
        {
            if (timedEvent.Event is not ControlChangeEvent control || control.ControlNumber != 64)
                continue;

            var time = TimeConverter.ConvertTo<MetricTimeSpan>(timedEvent.Time, tempoMap)
                .TotalMicroseconds / 1_000_000.0;
            var down = control.ControlValue >= 64;

            // Only the changes matter, not every repeat of the same position.
            if (sustain.Count == 0 || sustain[^1].Down != down)
                sustain.Add(new SustainChange(time, down));
        }
        sustain.Sort((a, b) => a.Time.CompareTo(b.Time));

        if (notes.Count == 0)
            throw new InvalidDataException($"{Path.GetFileName(path)} has no notes to play.");

        var duration = file.GetDuration<MetricTimeSpan>().TotalMicroseconds / 1_000_000.0;
        duration = Math.Max(duration, notes.Max(n => n.End));

        // Push the whole piece back so the first note starts at the top of the roll.
        var lead = Math.Max(0, leadIn - notes[0].Start);
        if (lead > 0)
        {
            for (var i = 0; i < notes.Count; i++)
                notes[i] = notes[i] with { Start = notes[i].Start + lead };

            for (var i = 0; i < sustain.Count; i++)
                sustain[i] = sustain[i] with { Time = sustain[i].Time + lead };

            duration += lead;
        }

        return new MidiSong(path, notes, sustain, duration);
    }
}

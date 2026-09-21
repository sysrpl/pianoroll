using System.Diagnostics;
using pianoroll.Models;

namespace pianoroll.Services;

/// <summary>
/// Plays a <see cref="MidiSong"/> against a clock: as the playhead passes each note's start it
/// raises <see cref="NoteOn"/>, and at its end <see cref="NoteOff"/>. The window drives it by
/// calling <see cref="Tick"/> on every frame, and the same position drives the falling bars, so
/// what you see and what you hear can't drift apart.
/// </summary>
public sealed class SongPlayer
{
    private readonly Stopwatch _clock = new();
    private readonly List<MidiNote> _sounding = [];

    /// <summary>Where the playhead was when the clock last started; the clock adds to it.</summary>
    private double _base;

    /// <summary>The next note to start, as an index into the song's notes.</summary>
    private int _next;

    /// <summary>The next sustain pedal change, as an index into the song's sustain list.</summary>
    private int _nextSustain;

    /// <summary>
    /// Raised as a note starts, with its MIDI note number and the velocity it was recorded at,
    /// and as it ends. Both come on the UI thread, from Tick.
    /// </summary>
    public event Action<int, int>? NoteOn;
    public event Action<int>? NoteOff;

    /// <summary>Raised when the sustain pedal goes down or comes up in the file.</summary>
    public event Action<bool>? SustainChanged;

    /// <summary>Raised when the playhead reaches the end of the song.</summary>
    public event EventHandler? Finished;

    public MidiSong? Song { get; private set; }

    public bool IsPlaying => _clock.IsRunning;

    /// <summary>Where the playhead is, in seconds from the start of the song.</summary>
    public double Position => _base + _clock.Elapsed.TotalSeconds;

    public double Duration => Song?.Duration ?? 0;

    /// <summary>Takes a new song, stopped at the beginning.</summary>
    public void Load(MidiSong song)
    {
        Stop();
        Song = song;
    }

    /// <summary>Forgets the song and silences anything it left sounding.</summary>
    public void Unload()
    {
        Stop();
        Song = null;
        SustainChanged?.Invoke(false);
    }

    public void Play()
    {
        if (Song is null || _clock.IsRunning)
            return;

        // Starting from the very end plays the song again from the top.
        if (Position >= Duration)
            Seek(0);

        _clock.Restart();
    }

    /// <summary>Stops the clock where it is, letting the notes that are down fade out.</summary>
    public void Pause()
    {
        if (!_clock.IsRunning)
            return;

        _base = Position;
        _clock.Reset();
        ReleaseAll();
    }

    /// <summary>Pauses and rewinds to the start.</summary>
    public void Stop()
    {
        _clock.Reset();
        _base = 0;
        _next = 0;
        _nextSustain = 0;
        ReleaseAll();
        SustainChanged?.Invoke(false);
    }

    /// <summary>
    /// Moves the playhead. Notes already sounding are released, and the note index is moved to
    /// the first note at or after the new position, so nothing from the skipped part plays.
    /// </summary>
    public void Seek(double seconds)
    {
        var wasPlaying = _clock.IsRunning;
        _clock.Reset();

        _base = Math.Clamp(seconds, 0, Math.Max(0, Duration));
        _next = Song?.IndexAt(_base) ?? 0;
        ReleaseAll();

        // The pedal is wherever the file last left it before this point.
        _nextSustain = 0;
        var down = false;
        if (Song is not null)
        {
            while (_nextSustain < Song.Sustain.Count && Song.Sustain[_nextSustain].Time <= _base)
                down = Song.Sustain[_nextSustain++].Down;
        }
        SustainChanged?.Invoke(down);

        if (wasPlaying)
            _clock.Restart();
    }

    /// <summary>Called once a frame: start the notes that are due and end the ones that are over.</summary>
    public void Tick()
    {
        if (Song is null || !_clock.IsRunning)
            return;

        var position = Position;

        while (_nextSustain < Song.Sustain.Count && Song.Sustain[_nextSustain].Time <= position)
            SustainChanged?.Invoke(Song.Sustain[_nextSustain++].Down);

        while (_next < Song.Notes.Count && Song.Notes[_next].Start <= position)
        {
            var note = Song.Notes[_next++];
            _sounding.Add(note);
            NoteOn?.Invoke(note.Note, note.Velocity);
        }

        for (var i = _sounding.Count - 1; i >= 0; i--)
        {
            if (_sounding[i].End > position)
                continue;

            var note = _sounding[i];
            _sounding.RemoveAt(i);
            NoteOff?.Invoke(note.Note);
        }

        if (position >= Duration)
        {
            Pause();
            Finished?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ReleaseAll()
    {
        foreach (var note in _sounding)
            NoteOff?.Invoke(note.Note);
        _sounding.Clear();
    }
}

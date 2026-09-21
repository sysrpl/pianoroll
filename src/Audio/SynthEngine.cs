using pianoroll.Models;

namespace pianoroll.Audio;

/// <summary>
/// Mixes the notes being held into one stereo stream. The UI thread calls <see cref="NoteOn"/>
/// and <see cref="NoteOff"/>; SDL's audio thread calls <see cref="RenderStereo"/>. The lock is
/// held only for the few microseconds either takes.
///
/// Notes are played from a SoundFont when one is loaded — real recorded instruments — and by the
/// built-in synthesis in <see cref="Voice"/> when there isn't one, so the app still makes a sound
/// on a machine with no SoundFont installed.
/// </summary>
public sealed class SynthEngine
{
    /// <summary>How many notes can sound at once with the built-in synthesis.</summary>
    private const int Polyphony = 16;

    private readonly Voice[] _voices;
    private readonly Random _random = new();
    private readonly object _gate = new();

    private SoundFontSynth? _soundFont;

    /// <summary>The recorded instruments that have been loaded, by which instrument they are.</summary>
    private readonly Dictionary<InstrumentKind, SampleInstrument> _sampled = [];
    private InstrumentInfo _instrument = InstrumentInfo.For(InstrumentKind.Piano);

    /// <summary>Notes let go while the sustain pedal is down, waiting for it to come up.</summary>
    private readonly HashSet<int> _pedalled = [];
    private bool _sustain;

    // Scratch buffers for one block, grown on the first callback.
    private float[] _left = [];
    private float[] _right = [];

    public SynthEngine(int sampleRate = AudioDevice.SampleRate)
    {
        _voices = new Voice[Polyphony];
        for (var i = 0; i < _voices.Length; i++)
            _voices[i] = new Voice(sampleRate);
    }

    /// <summary>How loud the whole keyboard is, 0 to 1.</summary>
    public float Volume { get; set; } = 0.6f;

    /// <summary>The recordings loaded for an instrument, or null when it has none.</summary>
    public SampleInstrument? Sampled(InstrumentKind kind)
    {
        lock (_gate)
            return _sampled.GetValueOrDefault(kind);
    }

    /// <summary>The SoundFont in use, or null when the built-in synthesis is playing.</summary>
    public SoundFontSynth? SoundFont
    {
        get
        {
            lock (_gate)
                return _soundFont;
        }
    }

    public InstrumentKind Instrument
    {
        get
        {
            lock (_gate)
                return _instrument.Kind;
        }
        set
        {
            lock (_gate)
            {
                // Switching between the sampled piano, the SoundFont and the built-in voices
                // changes which of them is rendering, so anything ringing is stopped first.
                var before = Source;
                _instrument = InstrumentInfo.For(value);
                _soundFont?.SetProgram(_instrument.Program);

                if (Source != before)
                    SilenceAll();
                // Within one source, notes already sounding keep their old instrument.
            }
        }
    }

    /// <summary>
    /// Where sound is coming from: the recorded piano when it's loaded and chosen, a SoundFont
    /// when one is loaded, and the built-in synthesis otherwise.
    /// </summary>
    private SoundSource Source =>
        _sampled.ContainsKey(_instrument.Kind) ? SoundSource.Samples
        : _soundFont is not null ? SoundSource.SoundFont
        : SoundSource.BuiltIn;

    /// <summary>The recordings for the instrument being played, when it has any.</summary>
    private SampleInstrument? Current => _sampled.GetValueOrDefault(_instrument.Kind);

    private enum SoundSource
    {
        Samples,
        SoundFont,
        BuiltIn,
    }

    /// <summary>
    /// Starts playing from <paramref name="soundFont"/>, or goes back to the built-in synthesis
    /// when it's null. Anything sounding is stopped first, so nothing is left hanging.
    /// </summary>
    public void UseSoundFont(SoundFontSynth? soundFont)
    {
        lock (_gate)
        {
            SilenceAll();
            _soundFont = soundFont;
            _soundFont?.SetProgram(_instrument.Program);
        }
    }

    /// <summary>Hands over an instrument's recordings, which it is then played from.</summary>
    public void UseSamples(InstrumentKind kind, SampleInstrument instrument)
    {
        lock (_gate)
        {
            SilenceAll();
            _sampled[kind] = instrument;
        }
    }

    /// <summary>
    /// The sustain pedal, from the MIDI file's controller 64. While it's down, releasing a key
    /// leaves the note ringing; when it comes up, everything waiting on it is let go at once.
    /// </summary>
    public void SetSustain(bool down)
    {
        lock (_gate)
        {
            _sustain = down;
            foreach (var instrument in _sampled.Values)
                instrument.SetSustain(down);
            _soundFont?.SetSustain(down);

            if (down)
                return;

            foreach (var note in _pedalled)
                Find(note)?.Release();
            _pedalled.Clear();
        }
    }

    /// <summary>
    /// Starts a note. <paramref name="velocity"/> is MIDI's 1-127: how hard the key was struck,
    /// which sets the note's loudness and brightness. Mouse clicks use a firm default.
    /// </summary>
    public void NoteOn(int note, int velocity = 100)
    {
        lock (_gate)
        {
            switch (Source)
            {
                case SoundSource.Samples:
                    Current!.NoteOn(note, velocity);
                    return;

                case SoundSource.SoundFont:
                    _soundFont!.NoteOn(note, velocity);
                    return;
            }

            _pedalled.Remove(note);
            var voice = Find(note) ?? FindFree() ?? _voices[0];
            voice.Start(note, _instrument.Kind, _random, velocity);
        }
    }

    /// <summary>Lets a note go. Piano and plucked notes ring on a little; the organ stops.</summary>
    public void NoteOff(int note)
    {
        lock (_gate)
        {
            switch (Source)
            {
                case SoundSource.Samples:
                    Current!.NoteOff(note);
                    return;

                case SoundSource.SoundFont:
                    _soundFont!.NoteOff(note);
                    return;
            }

            // The built-in voices have no pedal of their own, so it's kept here.
            if (_sustain)
                _pedalled.Add(note);
            else
                Find(note)?.Release();
        }
    }

    /// <summary>Stops everything at once — used when the window closes.</summary>
    public void AllNotesOff()
    {
        lock (_gate)
            SilenceAll();
    }

    /// <summary>Stops every source at once. The caller holds the lock.</summary>
    private void SilenceAll()
    {
        _soundFont?.AllNotesOff();
        foreach (var instrument in _sampled.Values)
            instrument.AllNotesOff();
        foreach (var voice in _voices)
            voice.Silence();
        _pedalled.Clear();
    }

    /// <summary>
    /// Called on SDL's audio thread: fill <paramref name="frames"/> frames of left/right pairs
    /// into <paramref name="interleaved"/>.
    /// </summary>
    public void RenderStereo(float[] interleaved, int frames)
    {
        if (_left.Length < frames)
        {
            _left = new float[frames];
            _right = new float[frames];
        }

        lock (_gate)
        {
            switch (Source)
            {
                case SoundSource.Samples:
                    Current!.Render(_left.AsSpan(0, frames), _right.AsSpan(0, frames));
                    break;

                case SoundSource.SoundFont:
                    _soundFont!.Render(_left.AsSpan(0, frames), _right.AsSpan(0, frames));
                    break;

                default:
                    // The built-in synthesis is mono; the same signal goes to both ears.
                    Array.Clear(_left, 0, frames);
                    foreach (var voice in _voices)
                        voice.Render(_left, frames);
                    Array.Copy(_left, _right, frames);
                    break;
            }
        }

        var volume = Volume;
        for (var i = 0; i < frames; i++)
        {
            interleaved[i * 2] = _left[i] * volume;
            interleaved[i * 2 + 1] = _right[i] * volume;
        }
    }

    /// <summary>The voice sounding this note, or null. Released voices count: pressing re-takes them.</summary>
    private Voice? Find(int note) => Array.Find(_voices, v => v.Note == note);

    private Voice? FindFree() => Array.Find(_voices, v => v.IsFinished);
}

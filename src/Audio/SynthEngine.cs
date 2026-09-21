using System.Runtime.CompilerServices;
using pianoroll.Models;

namespace pianoroll.Audio;

/// <summary>
/// Mixes the notes being held into one stereo stream. The UI thread calls <see cref="NoteOn"/>
/// and <see cref="NoteOff"/>; SDL's audio thread calls <see cref="RenderStereo"/>. The lock is
/// held only for the few microseconds either takes.
///
/// The keyboard is played by one or two <em>parts</em>. Normally the main part plays every key.
/// With the keyboard split on, keys below the split point go to the left part and the rest to
/// the main part, each with its own instrument and its own gain.
///
/// A part's notes come from the instrument's recordings when they're loaded, from a SoundFont
/// when one is loaded, and from the built-in synthesis in <see cref="Voice"/> otherwise.
/// </summary>
public sealed class SynthEngine
{
    /// <summary>How many notes can sound at once with the built-in synthesis.</summary>
    private const int Polyphony = 16;

    private readonly Voice[] _voices;
    private readonly Random _random = new();
    private readonly object _gate = new();

    /// <summary>The recorded instruments that have been loaded, by which instrument they are.</summary>
    private readonly Dictionary<InstrumentKind, SampleInstrument> _sampled = [];

    /// <summary>The whole keyboard when it isn't split; the right half when it is.</summary>
    private readonly Part _main = new();

    /// <summary>The keys below the split point, while the keyboard is split.</summary>
    private readonly Part _leftPart = new() { Instrument = InstrumentInfo.For(InstrumentKind.RealBass) };

    private bool _split;
    private int _splitNote = 60;

    /// <summary>
    /// What started each pitch that is sounding: the sampled instrument, the SoundFont player, or
    /// <see cref="BuiltInVoices"/>. A note's release always goes back to the very thing that
    /// started it — not to whatever its half of the keyboard plays now — so switching instruments
    /// or moving the split line while notes ring can never leave one stuck on.
    /// </summary>
    private readonly object?[] _startedOn = new object?[128];

    /// <summary>Stands for the built-in synthesis in <see cref="_startedOn"/>.</summary>
    private static readonly object BuiltInVoices = new();

    /// <summary>Notes let go while the sustain pedal is down, waiting for it to come up.</summary>
    private readonly HashSet<int> _pedalled = [];
    private bool _sustain;

    // Scratch buffers for one block, grown on the first callback.
    private float[] _left = [];
    private float[] _right = [];
    private float[] _partLeft = [];
    private float[] _partRight = [];

    public SynthEngine(int sampleRate = AudioDevice.SampleRate)
    {
        _voices = new Voice[Polyphony];
        for (var i = 0; i < _voices.Length; i++)
            _voices[i] = new Voice(sampleRate);
    }

    /// <summary>
    /// How long the last <see cref="RenderStereo"/> waited for the lock before it could start,
    /// in milliseconds, for F2.
    /// </summary>
    public double LastLockWait { get; private set; }

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
                return _main.Synth;
        }
    }

    /// <summary>The main part's instrument: the whole keyboard, or its right half when split.</summary>
    public InstrumentKind Instrument
    {
        get
        {
            lock (_gate)
                return _main.Instrument.Kind;
        }
        set
        {
            lock (_gate)
                SetInstrument(_main, value);
        }
    }

    /// <summary>The left half's instrument, used while the keyboard is split.</summary>
    public InstrumentKind LeftInstrument
    {
        get
        {
            lock (_gate)
                return _leftPart.Instrument.Kind;
        }
        set
        {
            lock (_gate)
                SetInstrument(_leftPart, value);
        }
    }

    /// <summary>
    /// Turns the keyboard split on or off, and says where it falls: keys below
    /// <paramref name="note"/> play the left part, the rest the main part.
    /// </summary>
    public void SetSplit(bool enabled, int note)
    {
        lock (_gate)
        {
            _split = enabled;
            _splitNote = Math.Clamp(note, 0, 127);
        }
    }

    /// <summary>A half's extra level in decibels: positive is louder, negative quieter.</summary>
    public void SetGain(bool left, float decibels)
    {
        lock (_gate)
            (left ? _leftPart : _main).Gain = MathF.Pow(10f, decibels / 20f);
    }

    /// <summary>
    /// Starts playing from <paramref name="soundFont"/>, or goes back to the built-in synthesis
    /// when it's null. Each part gets its own player on the one bank, so the two halves of a
    /// split can play different instruments from it. Anything sounding is stopped first.
    /// </summary>
    public void UseSoundFont(SoundFontSynth? soundFont)
    {
        lock (_gate)
        {
            SilenceAll();
            _main.Synth = soundFont;
            _leftPart.Synth = soundFont?.CreateSibling();
            _main.Synth?.SetProgram(_main.Instrument.Program);
            _leftPart.Synth?.SetProgram(_leftPart.Instrument.Program);
            _main.Synth?.SetSustain(_sustain);
            _leftPart.Synth?.SetSustain(_sustain);
        }
    }

    /// <summary>Hands over an instrument's recordings, which it is then played from.</summary>
    public void UseSamples(InstrumentKind kind, SampleInstrument instrument)
    {
        // Nothing needs stopping: notes already sounding end on whatever started them.
        lock (_gate)
            _sampled[kind] = instrument;
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
            _main.Synth?.SetSustain(down);
            _leftPart.Synth?.SetSustain(down);

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
        if (note is < 0 or > 127)
            return;

        lock (_gate)
        {
            var part = _split && note < _splitNote ? _leftPart : _main;
            var gain = _split ? part.Gain : 1f;
            var target = TargetOf(part);

            // The same pitch still sounding somewhere else — on the other half, after the split
            // line moved, or on an instrument this half has since been switched away from — is
            // ended there first, so the new strike's record doesn't hide it.
            if (_startedOn[note] is { } previous && !ReferenceEquals(previous, target))
                End(previous, note);

            _startedOn[note] = target;

            switch (target)
            {
                case SampleInstrument samples:
                    samples.NoteOn(note, velocity, gain);
                    return;

                case SoundFontSynth synth:
                    // A SoundFont part's gain is applied as its sound is mixed in.
                    synth.NoteOn(note, velocity);
                    return;
            }

            // A key struck again lets its last note ring off on its own voice, and starts the new
            // one on another: restarting a voice that is still sounding makes its waveform jump.
            _pedalled.Remove(note);
            Find(note)?.Release();
            var voice = FindFree() ?? Quietest();
            voice.Start(note, part.Instrument.Kind, _random, velocity, gain);
        }
    }

    /// <summary>Lets a note go. Piano and plucked notes ring on a little; the organ stops.</summary>
    public void NoteOff(int note)
    {
        if (note is < 0 or > 127)
            return;

        lock (_gate)
        {
            var target = _startedOn[note];
            _startedOn[note] = null;

            if (target is not null)
            {
                End(target, note);
            }
            else
            {
                // No record of what played it: end it on both halves rather than risk missing it.
                // Ending a note that isn't sounding does nothing.
                End(TargetOf(_main), note);
                End(TargetOf(_leftPart), note);
            }
        }
    }

    /// <summary>What a half's notes are played on right now. The caller holds the lock.</summary>
    private object TargetOf(Part part) => SourceOf(part) switch
    {
        SoundSource.Samples => _sampled[part.Instrument.Kind],
        SoundSource.SoundFont => part.Synth!,
        _ => BuiltInVoices,
    };

    /// <summary>Ends a note on the thing that is playing it. The caller holds the lock.</summary>
    private void End(object target, int note)
    {
        switch (target)
        {
            case SampleInstrument samples:
                samples.NoteOff(note);
                return;

            case SoundFontSynth synth:
                synth.NoteOff(note);
                return;
        }

        // The built-in voices have no pedal of their own, so it's kept here.
        if (_sustain)
            _pedalled.Add(note);
        else
            Find(note)?.Release();
    }

    /// <summary>Records the most voices sounding at once, for F2. Called on the mixer thread.</summary>
    public void CountVoices(AudioStats stats)
    {
        lock (_gate)
        {
            var soundFont = (_main.Synth?.ActiveVoices ?? 0) + (_leftPart.Synth?.ActiveVoices ?? 0);
            var samples = 0;
            foreach (var instrument in _sampled.Values)
                samples += instrument.ActiveVoices;

            stats.MostSoundFontVoices = Math.Max(stats.MostSoundFontVoices, soundFont);
            stats.MostSampleVoices = Math.Max(stats.MostSampleVoices, samples);
        }
    }

    /// <summary>Stops everything at once — used when the window closes.</summary>
    public void AllNotesOff()
    {
        lock (_gate)
            SilenceAll();
    }

    /// <summary>
    /// Called on SDL's audio thread: fill <paramref name="frames"/> frames of left/right pairs
    /// into <paramref name="interleaved"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void RenderStereo(float[] interleaved, int frames)
    {
        if (_left.Length < frames)
        {
            _left = new float[frames];
            _right = new float[frames];
            _partLeft = new float[frames];
            _partRight = new float[frames];
        }

        var left = _left.AsSpan(0, frames);
        var right = _right.AsSpan(0, frames);
        left.Clear();
        right.Clear();

        var waiting = System.Diagnostics.Stopwatch.GetTimestamp();
        lock (_gate)
        {
            LastLockWait = AudioStats.Milliseconds(waiting);

            // Every loaded recording is mixed in: each note's part gain was applied as it began.
            foreach (var instrument in _sampled.Values)
                instrument.Render(left, right);

            // Each part's SoundFont player, at that part's gain — but only the ones in use. Before
            // the split there was one player; rendering a second on every block, split or not,
            // doubled the work on the audio thread, and a block that isn't ready in time pops.
            MixSoundFont(_main, SourceOf(_main) == SoundSource.SoundFont, frames);
            MixSoundFont(_leftPart, _split && SourceOf(_leftPart) == SoundSource.SoundFont, frames);

            // The built-in synthesis is mono; the same signal goes to both ears.
            var mono = _partLeft.AsSpan(0, frames);
            mono.Clear();
            foreach (var voice in _voices)
                voice.Render(_partLeft, frames);
            for (var i = 0; i < frames; i++)
            {
                left[i] += mono[i];
                right[i] += mono[i];
            }
        }

        var volume = Volume;
        for (var i = 0; i < frames; i++)
        {
            interleaved[i * 2] = _left[i] * volume;
            interleaved[i * 2 + 1] = _right[i] * volume;
        }
    }

    /// <summary>
    /// How long a player that has gone out of use keeps being rendered: its last notes and their
    /// reverb need to die away, since stopping it while they're still audible would cut them off.
    /// </summary>
    private const int TailFrames = 3 * AudioDevice.SampleRate;

    /// <summary>
    /// Renders one part's SoundFont player and adds it in, if it's in use or still dying away.
    /// The caller holds the lock.
    /// </summary>
    private void MixSoundFont(Part part, bool inUse, int frames)
    {
        if (part.Synth is not { } synth)
            return;

        if (inUse || synth.IsSounding)
            part.IdleFrames = 0;
        else if (part.IdleFrames >= TailFrames)
            return;         // silent for seconds: nothing left to hear, so no work to do
        else
            part.IdleFrames += frames;

        // Zeroed first, whatever the SoundFont player does with them: nothing from an earlier
        // block may carry into this one.
        var partLeft = _partLeft.AsSpan(0, frames);
        var partRight = _partRight.AsSpan(0, frames);
        partLeft.Clear();
        partRight.Clear();
        synth.Render(partLeft, partRight);

        var gain = _split ? part.Gain : 1f;
        for (var i = 0; i < frames; i++)
        {
            _left[i] += partLeft[i] * gain;
            _right[i] += partRight[i] * gain;
        }
    }

    /// <summary>
    /// Gives a part a new instrument. New notes play on it; notes already sounding finish on the
    /// instrument that started them, as they would on a real keyboard with several sounds, and
    /// their releases still reach it. The caller holds the lock.
    /// </summary>
    private void SetInstrument(Part part, InstrumentKind kind)
    {
        part.Instrument = InstrumentInfo.For(kind);
        part.Synth?.SetProgram(part.Instrument.Program);
    }

    /// <summary>
    /// Where a part's sound comes from: its recordings when they're loaded, a SoundFont when one
    /// is loaded, and the built-in synthesis otherwise.
    /// </summary>
    private SoundSource SourceOf(Part part) =>
        _sampled.ContainsKey(part.Instrument.Kind) ? SoundSource.Samples
        : part.Synth is not null ? SoundSource.SoundFont
        : SoundSource.BuiltIn;

    /// <summary>
    /// Stops every note on every source. They fade rather than being cut off, since an instant
    /// stop mid-waveform is a click. The caller holds the lock.
    /// </summary>
    private void SilenceAll()
    {
        _main.Synth?.AllNotesOff();
        _leftPart.Synth?.AllNotesOff();
        foreach (var instrument in _sampled.Values)
            instrument.AllNotesOff();
        foreach (var voice in _voices)
            voice.Release();
        _pedalled.Clear();
        Array.Clear(_startedOn);
    }

    /// <summary>The voice holding this note, or null. Notes already dying away don't count.</summary>
    private Voice? Find(int note) => Array.Find(_voices, v => v.Note == note && !v.IsReleasing);

    private Voice? FindFree() => Array.Find(_voices, v => v.IsFinished);

    /// <summary>When every voice is busy, the quietest one gives way.</summary>
    private Voice Quietest()
    {
        var quietest = _voices[0];
        foreach (var voice in _voices)
        {
            if (voice.Loudness < quietest.Loudness)
                quietest = voice;
        }
        return quietest;
    }

    private enum SoundSource
    {
        Samples,
        SoundFont,
        BuiltIn,
    }

    /// <summary>One side of the keyboard: what it plays, how loud, and its SoundFont player.</summary>
    private sealed class Part
    {
        public InstrumentInfo Instrument = InstrumentInfo.For(InstrumentKind.Piano);

        /// <summary>The split's gain control, as a multiplier; 1 is unchanged.</summary>
        public float Gain = 1f;

        public SoundFontSynth? Synth;

        /// <summary>How long the part's player has been out of use and silent, in frames.</summary>
        public int IdleFrames;
    }
}

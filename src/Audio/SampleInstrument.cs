namespace pianoroll.Audio;

/// <summary>
/// A real instrument, played from recordings: one WAV per note, named by note (A1.wav,
/// Asharp1.wav, C4.wav…), where C4 is middle C. A key with no recording of its own is played
/// from the nearest one, resampled, so a set that covers only part of the keyboard still plays
/// across all of it.
///
/// Instruments that hold a note — a saxophone — are recorded with a sustain loop in the file,
/// which repeats for as long as the key is down. Struck instruments like the piano simply decay.
///
/// Velocity sets both how loud a note is and, through a gentle filter, how bright: the samples
/// are recorded at one strength, so a quiet note has to be dulled as well as turned down or it
/// sounds like the same note played through a volume knob.
///
/// Everything here runs under <see cref="SynthEngine"/>'s lock.
/// </summary>
public sealed class SampleInstrument
{
    /// <summary>How many recorded notes can ring at once, counting ones fading out.</summary>
    private const int Polyphony = 48;

    /// <summary>Semitones from A, in the order the file names count octaves.</summary>
    private static readonly (string Name, int Semitone)[] NoteNames =
    [
        ("A", 0), ("Asharp", 1), ("B", 2), ("C", 3), ("Csharp", 4), ("D", 5),
        ("Dsharp", 6), ("E", 7), ("F", 8), ("Fsharp", 9), ("G", 10), ("Gsharp", 11),
    ];

    private readonly Sample?[] _samples = new Sample?[128];
    private readonly SampleVoice[] _voices = new SampleVoice[Polyphony];
    private readonly int _sampleRate;

    /// <summary>
    /// How long a note takes to die away once the key is up: a piano's dampers take a moment,
    /// a wind instrument stops as the breath does.
    /// </summary>
    private readonly float _release;

    /// <summary>The dullest a softly played note gets, in Hz.</summary>
    private readonly float _softest;

    private bool _sustain;

    private SampleInstrument(int sampleRate, float release, float softest)
    {
        _sampleRate = sampleRate;
        _release = release;
        _softest = softest;
        for (var i = 0; i < _voices.Length; i++)
            _voices[i] = new SampleVoice();
    }

    /// <summary>The folder the recordings came from.</summary>
    public string Folder { get; private set; } = "";

    /// <summary>How many notes were loaded.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// Reads every WAV in <paramref name="folder"/> whose name is a note. About 150MB of audio,
    /// so this belongs on a background thread.
    /// </summary>
    /// <param name="sustained">
    /// True for an instrument that is blown or bowed rather than struck: it stops sooner when the
    /// key is let go, and stays brighter when played softly.
    /// </param>
    /// <exception cref="InvalidDataException">The folder holds no usable notes.</exception>
    public static SampleInstrument Load(string folder, int sampleRate, bool sustained = false)
    {
        var instrument = new SampleInstrument(
            sampleRate,
            release: sustained ? 0.12f : 0.4f,
            softest: sustained ? 2600f : 900f) { Folder = folder };

        foreach (var path in Directory.EnumerateFiles(folder, "*.wav"))
        {
            var note = NoteFor(Path.GetFileNameWithoutExtension(path));
            if (note is null)
                continue;

            try
            {
                var wav = WavFile.Load(path);
                instrument._samples[note.Value] = new Sample(
                    wav.Samples, wav.Channels, wav.Frames, (double)wav.SampleRate / sampleRate,
                    wav.HasLoop ? wav.LoopStart : 0,
                    wav.HasLoop ? wav.LoopEnd : 0);
                instrument.Count++;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                // One unreadable file shouldn't stop the other eighty-six from loading.
            }
        }

        if (instrument.Count == 0)
            throw new InvalidDataException($"No notes were found in {folder}.");

        return instrument;
    }

    /// <summary>
    /// The MIDI note a file name means, or null. The pack counts octaves from A, so A1 is the
    /// lowest key on a piano (MIDI 21) and C4 is middle C (MIDI 60).
    /// </summary>
    public static int? NoteFor(string fileName)
    {
        foreach (var (name, semitone) in NoteNames)
        {
            if (!fileName.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                continue;

            // "A" also matches the start of "Asharp", so the rest must be just the octave.
            var rest = fileName[name.Length..];
            if (!int.TryParse(rest, out var octave))
                continue;

            var note = 21 + (octave - 1) * 12 + semitone;
            return note is >= 0 and < 128 ? note : null;
        }
        return null;
    }

    /// <summary>The sustain pedal. Holding it keeps released notes ringing, as the dampers stay up.</summary>
    public void SetSustain(bool down)
    {
        _sustain = down;
        if (down)
            return;

        // Pedal up: every note waiting on it starts fading now.
        foreach (var voice in _voices)
        {
            if (voice.Active && voice.Sustained)
                voice.StartRelease(_sampleRate, _release);
        }
    }

    public void NoteOn(int note, int velocity)
    {
        var (sample, sampleNote) = Nearest(note);
        if (sample is null)
            return;

        // Playing a key that's already ringing takes over from it quickly, as a real damper does.
        foreach (var voice in _voices)
        {
            if (voice.Active && voice.Note == note)
                voice.StartRelease(_sampleRate, seconds: 0.08f);
        }

        var free = Array.Find(_voices, v => !v.Active) ?? Quietest();
        var force = Math.Clamp(velocity, 1, 127) / 127f;

        free.Start(
            note,
            sample,
            step: sample.RateRatio * Math.Pow(2, (note - sampleNote) / 12.0),
            gain: MathF.Pow(force, 1.4f),
            // Soft notes are duller as well as quieter, opening up as they are played harder.
            cutoff: Cutoff(_softest + 13000f * force * force));
    }

    public void NoteOff(int note)
    {
        foreach (var voice in _voices)
        {
            if (!voice.Active || voice.Note != note || voice.Releasing)
                continue;

            if (_sustain)
                voice.Sustained = true;
            else
                voice.StartRelease(_sampleRate, _release);
        }
    }

    public void AllNotesOff()
    {
        foreach (var voice in _voices)
            voice.Stop();
    }

    /// <summary>Mixes every ringing note into the two channels.</summary>
    public void Render(Span<float> left, Span<float> right)
    {
        left.Clear();
        right.Clear();

        foreach (var voice in _voices)
        {
            if (voice.Active)
                voice.Render(left, right);
        }
    }

    /// <summary>The recording for a note, or the closest one there is.</summary>
    private (Sample? Sample, int Note) Nearest(int note)
    {
        if (_samples[note] is { } exact)
            return (exact, note);

        for (var distance = 1; distance < 128; distance++)
        {
            if (note - distance >= 0 && _samples[note - distance] is { } below)
                return (below, note - distance);
            if (note + distance < 128 && _samples[note + distance] is { } above)
                return (above, note + distance);
        }
        return (null, note);
    }

    /// <summary>When every voice is busy, the quietest one gives way.</summary>
    private SampleVoice Quietest()
    {
        var quietest = _voices[0];
        foreach (var voice in _voices)
        {
            if (voice.Level < quietest.Level)
                quietest = voice;
        }
        return quietest;
    }

    private float Cutoff(float hertz) => 1f - MathF.Exp(-2f * MathF.PI * hertz / _sampleRate);

    /// <summary>One loaded recording, with the sustain loop the file declares (if any).</summary>
    private sealed record Sample(short[] Data, int Channels, int Frames, double RateRatio,
        int LoopStart, int LoopEnd)
    {
        public bool HasLoop => LoopEnd > LoopStart;
    }

    /// <summary>One note ringing: where it is in its recording, and how it's fading.</summary>
    private sealed class SampleVoice
    {
        private const float Scale = 1f / 32768f;

        private Sample? _sample;
        private double _position;
        private double _step;
        private float _gain;
        private float _cutoff;
        private float _leftFilter;
        private float _rightFilter;
        private float _releaseStep;

        public bool Active { get; private set; }
        public bool Releasing { get; private set; }

        /// <summary>True when the key is up but the sustain pedal is holding the note on.</summary>
        public bool Sustained { get; set; }

        public int Note { get; private set; }

        /// <summary>How loud this voice is now, for deciding which to steal.</summary>
        public float Level { get; private set; }

        public void Start(int note, Sample sample, double step, float gain, float cutoff)
        {
            Note = note;
            _sample = sample;
            _position = 0;
            _step = step;
            _gain = gain;
            _cutoff = cutoff;
            _leftFilter = 0f;
            _rightFilter = 0f;
            Level = gain;
            Releasing = false;
            Sustained = false;
            Active = true;
        }

        public void StartRelease(int sampleRate, float seconds)
        {
            Releasing = true;
            Sustained = false;
            _releaseStep = Level / Math.Max(1f, seconds * sampleRate);
        }

        public void Stop()
        {
            Active = false;
            Level = 0f;
            _sample = null;
        }

        public void Render(Span<float> left, Span<float> right)
        {
            if (_sample is not { } sample)
                return;

            for (var i = 0; i < left.Length; i++)
            {
                // A held note runs round the file's sustain loop rather than running out.
                if (sample.HasLoop && !Releasing && _position >= sample.LoopEnd)
                    _position -= sample.LoopEnd - sample.LoopStart;

                var index = (int)_position;
                if (index + 1 >= sample.Frames)
                {
                    Stop();
                    return;
                }

                if (Releasing)
                {
                    Level -= _releaseStep;
                    if (Level <= 0f)
                    {
                        Stop();
                        return;
                    }
                }

                var fraction = (float)(_position - index);
                var offset = index * sample.Channels;
                var nextOffset = offset + sample.Channels;

                var l = Mix(sample.Data[offset], sample.Data[nextOffset], fraction);
                var r = sample.Channels > 1
                    ? Mix(sample.Data[offset + 1], sample.Data[nextOffset + 1], fraction)
                    : l;

                // The softer the note, the more of its top end is filtered away.
                _leftFilter += _cutoff * (l - _leftFilter);
                _rightFilter += _cutoff * (r - _rightFilter);

                left[i] += _leftFilter * Level;
                right[i] += _rightFilter * Level;

                _position += _step;
            }

            if (!Releasing)
                Level = _gain;
        }

        private static float Mix(short a, short b, float fraction) =>
            (a + (b - a) * fraction) * Scale;
    }
}

using pianoroll.Models;

namespace pianoroll.Audio;

/// <summary>
/// One note being sounded. Two kinds of synthesis cover the four instruments:
///
/// - Piano and organ are additive: a stack of partials, each with its own level and decay. The
///   piano's are stretched slightly sharp (real strings are stiff), die away faster the higher
///   they are, and beat gently against each other the way a piano's two or three strings per note
///   do. A short filtered noise burst stands in for the hammer.
/// - Electric guitar and banjo are plucked strings (Karplus-Strong): a burst of noise runs round a
///   delay line one wavelength long, losing brightness each trip. Where the "pick" strikes is a
///   comb filter on that burst, which is most of what makes the two sound different, and the
///   output runs through a tone filter and a soft, slightly asymmetric clip for amplifier warmth.
///
/// Rendering happens on SDL's audio thread, so a voice never touches Avalonia or allocates.
/// </summary>
public sealed class Voice
{
    private const int MaxPartials = 16;

    private readonly int _sampleRate;

    // Additive (piano, organ). Phase and step are in turns per sample.
    private readonly float[] _partialPhase = new float[MaxPartials];
    private readonly float[] _partialStep = new float[MaxPartials];
    private readonly float[] _partialLevel = new float[MaxPartials];
    private readonly float[] _partialDecay = new float[MaxPartials];
    private readonly float[] _beatPhase = new float[MaxPartials];
    private readonly float[] _beatStep = new float[MaxPartials];
    private int _partials;

    // The hammer: a noise burst, low-passed, gone within about 20ms.
    private float _hammerLevel;
    private float _hammerDecay;
    private float _hammerFilter;

    // Plucked (guitar, banjo): a circular delay line one wavelength long.
    private float[] _string = [];
    private int _stringLength;
    private float _readFraction;
    private int _readIndex;
    private float _lastSample;
    private float _damping;
    private float _stringDecay;
    private float _drive;

    // Output shaping for the plucked instruments: a pickup/amplifier tone control, and a DC
    // blocker to undo the offset that asymmetric clipping introduces.
    private float _tone;
    private float _toneCoefficient;
    private float _dcLast;
    private float _dcOut;

    private float _amplitude = 1f;
    private Random _random = new();

    // Envelope, in linear gain. Attack ramps up, release ramps down; between them the
    // instrument's own decay (or none, for the organ) does the work.
    private float _level;
    private float _attackStep;
    private float _releaseStep;
    private bool _releasing;

    public Voice(int sampleRate)
    {
        _sampleRate = sampleRate;
    }

    /// <summary>The MIDI note this voice is sounding, or -1 when it's free.</summary>
    public int Note { get; private set; } = -1;

    public bool IsPlucked { get; private set; }

    /// <summary>True once the voice has faded to nothing and its slot can be reused.</summary>
    public bool IsFinished => Note < 0;

    /// <summary>
    /// Starts <paramref name="note"/> on this voice, replacing whatever it held.
    /// <paramref name="velocity"/> is MIDI's 1-127: it sets how loud and how bright the note is,
    /// because a string struck or plucked harder gives out far more high harmonics, and that
    /// change in tone is most of what "played harder" sounds like.
    /// </summary>
    public void Start(int note, InstrumentKind instrument, Random random, int velocity = 100)
    {
        Note = note;
        _releasing = false;
        _level = 0f;
        _random = random;
        _hammerLevel = 0f;

        var frequency = Frequency(note);
        var force = Math.Clamp(velocity, 1, 127) / 127f;

        switch (instrument)
        {
            case InstrumentKind.Piano:
                IsPlucked = false;
                StartPiano(note, frequency, force);
                _attackStep = Step(seconds: 0.003f);
                _releaseStep = Step(seconds: 0.2f);       // the damper coming down
                break;

            case InstrumentKind.Organ:
                IsPlucked = false;
                StartDrawbars(frequency, force);
                _attackStep = Step(seconds: 0.012f);
                _releaseStep = Step(seconds: 0.05f);
                break;

            case InstrumentKind.ElectricGuitar:
                IsPlucked = true;
                // Little damping and a very slow loss: an electric guitar sustains for seconds.
                StartString(frequency, force,
                    damping: 0.30f + 0.15f * PitchFactor(note),
                    decay: 0.99985f,
                    drive: 2.6f,
                    pickPosition: 0.22f,
                    toneHz: 3200f);
                _attackStep = Step(seconds: 0.002f);
                _releaseStep = Step(seconds: 0.15f);
                break;

            case InstrumentKind.Banjo:
                IsPlucked = true;
                // Picked hard by the bridge, and damped by the skin head within a second.
                StartString(frequency, force,
                    damping: 0.14f,
                    decay: 0.9968f,
                    drive: 1.7f,
                    pickPosition: 0.11f,
                    toneHz: 6500f);
                _attackStep = Step(seconds: 0.001f);
                _releaseStep = Step(seconds: 0.05f);
                break;
        }
    }

    /// <summary>The key was let go: fade the note out over the instrument's release time.</summary>
    public void Release() => _releasing = true;

    /// <summary>Stops the note at once, for panic / instrument changes.</summary>
    public void Silence() => Note = -1;

    /// <summary>Adds this voice's next <paramref name="count"/> samples into <paramref name="buffer"/>.</summary>
    public void Render(float[] buffer, int count)
    {
        if (Note < 0)
            return;

        for (var i = 0; i < count; i++)
        {
            if (_releasing)
            {
                _level -= _releaseStep;
                if (_level <= 0f)
                {
                    Note = -1;
                    return;
                }
            }
            else if (_level < 1f)
            {
                _level = MathF.Min(1f, _level + _attackStep);
            }

            var sample = IsPlucked ? NextString() : NextAdditive();
            buffer[i] += sample * _level * _amplitude;
        }

        if (!IsPlucked && !_releasing && PartialsSilent())
            Note = -1;
    }

    // ---- piano and organ ---------------------------------------------------------------------

    /// <summary>
    /// A struck string. Each partial gets its own decay — the top of the spectrum is gone within a
    /// second while the fundamental rings on — and bass notes ring far longer than treble ones,
    /// which is the single biggest giveaway when it's wrong.
    /// </summary>
    private void StartPiano(int note, float frequency, float force)
    {
        // How long the fundamental takes to fade: about 13 seconds at the bottom of the keyboard,
        // a second and a bit at the top.
        var fundamentalDecay = 13f * MathF.Exp(-(note - 21) * 0.032f);

        // Struck harder means brighter, not just louder.
        var brightness = 0.35f + 0.65f * force;

        // String stiffness: the higher partials sit progressively sharp of whole multiples. Short
        // thick bass strings are the most inharmonic, which is what gives them their growl.
        var stiffness = 0.0002f + 0.0009f * MathF.Exp(-(note - 21) * 0.025f);

        _partials = 0;
        for (var h = 1; h <= MaxPartials; h++)
        {
            var ratio = h * MathF.Sqrt(1f + stiffness * h * h);
            var partialFrequency = frequency * ratio;
            if (partialFrequency > _sampleRate * 0.45f)
                break;

            _partialStep[_partials] = partialFrequency / _sampleRate;
            _partialPhase[_partials] = 0f;
            _partialLevel[_partials] =
                MathF.Pow(h, -1.1f) * MathF.Exp(-(h - 1) * (1.05f - brightness) * 0.55f);
            _partialDecay[_partials] = Decay(fundamentalDecay / (1f + 0.30f * MathF.Pow(h, 1.15f)));

            // Two or three strings per note, tuned a hair apart: they beat slowly against each
            // other, and that shimmer is what a single oscillator per partial always misses.
            _beatPhase[_partials] = (float)_random.NextDouble();
            _beatStep[_partials] = (0.4f + 1.6f * (float)_random.NextDouble()) * h * 0.4f / _sampleRate;
            _partials++;
        }

        // The hammer's thump: noise, gone in about 20ms, louder the harder the note is struck.
        _hammerLevel = 0.5f * force * force;
        _hammerDecay = Decay(0.02f);
        _hammerFilter = 0f;

        _amplitude = 0.7f * (0.3f + 0.7f * force);
    }

    /// <summary>The organ's drawbars: octaves and fifths above the note, all holding steady.</summary>
    private void StartDrawbars(float frequency, float force)
    {
        float[] ratios = [0.5f, 1f, 1.5f, 2f, 3f, 4f];
        float[] levels = [0.35f, 1f, 0.5f, 0.55f, 0.3f, 0.22f];

        _partials = 0;
        for (var i = 0; i < ratios.Length; i++)
        {
            var partialFrequency = frequency * ratios[i];
            if (partialFrequency > _sampleRate * 0.45f)
                continue;

            _partialStep[_partials] = partialFrequency / _sampleRate;
            _partialPhase[_partials] = (float)_random.NextDouble();   // avoids a hard click
            _partialLevel[_partials] = levels[i] * 0.5f;
            _partialDecay[_partials] = 1f;    // no decay: an organ holds while the key is down
            _beatStep[_partials] = 0f;
            _partials++;
        }

        _amplitude = 0.6f * (0.5f + 0.5f * force);
    }

    private float NextAdditive()
    {
        var sample = 0f;
        for (var p = 0; p < _partials; p++)
        {
            var level = _partialLevel[p];

            if (_beatStep[p] > 0f)
            {
                // ±8% wobble: the beating between a note's strings.
                _beatPhase[p] += _beatStep[p];
                level *= 1f + 0.08f * SineTable.Sin(_beatPhase[p]);
            }

            sample += SineTable.Sin(_partialPhase[p]) * level;
            _partialLevel[p] *= _partialDecay[p];

            _partialPhase[p] += _partialStep[p];
            if (_partialPhase[p] > 1f)
                _partialPhase[p] -= 1f;
        }

        if (_hammerLevel > 0.0001f)
        {
            // A dull knock rather than a hiss: the noise is heavily low-passed.
            var noise = (float)(_random.NextDouble() * 2 - 1);
            _hammerFilter += 0.25f * (noise - _hammerFilter);
            sample += _hammerFilter * _hammerLevel;
            _hammerLevel *= _hammerDecay;
        }

        return sample * 0.4f;
    }

    private bool PartialsSilent()
    {
        for (var p = 0; p < _partials; p++)
        {
            if (_partialLevel[p] > 0.0005f)
                return false;
        }
        return true;
    }

    /// <summary>The per-sample multiplier that fades a partial to near nothing in that many seconds.</summary>
    private float Decay(float seconds) => MathF.Exp(-6f / MathF.Max(0.001f, seconds * _sampleRate));

    // ---- plucked string ----------------------------------------------------------------------

    private void StartString(float frequency, float force, float damping, float decay, float drive,
        float pickPosition, float toneHz)
    {
        var length = _sampleRate / frequency;
        _stringLength = Math.Max(2, (int)length);
        _readFraction = length - _stringLength;
        _damping = damping;
        _stringDecay = decay;
        _drive = drive * (0.6f + 0.8f * force);
        _readIndex = 0;
        _lastSample = 0f;
        _tone = 0f;
        _dcLast = 0f;
        _dcOut = 0f;

        // One-pole tone control, standing in for the pickup and the speaker.
        _toneCoefficient = 1f - MathF.Exp(-2f * MathF.PI * toneHz / _sampleRate);

        if (_string.Length < _stringLength + 1)
            _string = new float[_stringLength + 1];

        // The pluck. A softer pick (lower velocity) means a duller burst, so the note starts
        // rounder as well as quieter.
        var softness = 0.6f - 0.45f * force;
        var filtered = 0f;
        for (var i = 0; i < _stringLength; i++)
        {
            var noise = (float)(_random.NextDouble() * 2 - 1);
            filtered += (1f - softness) * (noise - filtered);
            _string[i] = filtered;
        }

        // Pick position: plucking a string a fifth of the way along cancels every fifth harmonic.
        // This comb is why a guitar picked over the neck sounds round and a banjo picked by the
        // bridge sounds nasal.
        var offset = Math.Max(1, (int)(_stringLength * pickPosition));
        for (var i = _stringLength - 1; i >= offset; i--)
            _string[i] -= _string[i - offset] * 0.85f;

        _amplitude = 0.55f * (0.35f + 0.65f * force);
    }

    private float NextString()
    {
        var next = _readIndex + 1 < _stringLength ? _readIndex + 1 : 0;

        // A fractional read tunes the string between whole samples, so high notes stay in tune.
        var sample = _string[_readIndex] + (_string[next] - _string[_readIndex]) * _readFraction;

        // One-pole average: each trip round the loop loses a little top end, as a real string does.
        var looped = (sample * (1f - _damping) + _lastSample * _damping) * _stringDecay;
        _lastSample = sample;

        _string[_readIndex] = looped;
        _readIndex = next;

        // Amplifier: tone control, then a soft asymmetric clip, then block the DC the asymmetry
        // leaves behind.
        _tone += _toneCoefficient * (looped - _tone);
        var driven = _tone * _drive;
        var clipped = driven >= 0f ? MathF.Tanh(driven) : MathF.Tanh(driven * 0.8f);

        _dcOut = clipped - _dcLast + 0.995f * _dcOut;
        _dcLast = clipped;
        return _dcOut;
    }

    // ---- helpers -----------------------------------------------------------------------------

    /// <summary>0 at the bottom of the keyboard, 1 at the top: for values that follow pitch.</summary>
    private static float PitchFactor(int note) => Math.Clamp((note - 21) / 87f, 0f, 1f);

    /// <summary>The envelope step that covers full scale in <paramref name="seconds"/>.</summary>
    private float Step(float seconds) => 1f / MathF.Max(1f, seconds * _sampleRate);

    /// <summary>Equal temperament: A above middle C (MIDI 69) is 440 Hz.</summary>
    public static float Frequency(int note) => 440f * MathF.Pow(2f, (note - 69) / 12f);
}

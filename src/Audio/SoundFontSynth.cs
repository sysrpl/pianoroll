using MeltySynth;

namespace pianoroll.Audio;

/// <summary>
/// Plays real recorded instruments from a SoundFont (.sf2) file, through MeltySynth — a
/// SoundFont synthesiser in plain C#, so there is still nothing native to ship.
///
/// One MIDI channel is used for everything, with a program change when the instrument is
/// switched. Every call here happens under <see cref="SynthEngine"/>'s lock, because MeltySynth's
/// synthesizer is not safe to use from the UI thread and SDL's audio thread at once.
/// </summary>
public sealed class SoundFontSynth
{
    /// <summary>The MIDI channel everything is played on.</summary>
    private const int Channel = 0;

    /// <summary>The most voices MeltySynth allows ("must be between 8 and 256").</summary>
    private const int MaxVoices = 256;

    /// <summary>A MIDI program change: "play this channel with instrument N".</summary>
    private const int ProgramChange = 0xC0;

    /// <summary>A MIDI control change, used here for the sustain pedal (controller 64).</summary>
    private const int ControlChange = 0xB0;
    private const int SustainController = 64;

    private readonly Synthesizer _synthesizer;
    private readonly SoundFont _soundFont;
    private readonly int _sampleRate;

    private SoundFontSynth(SoundFont soundFont, int sampleRate, string path, string name)
    {
        _soundFont = soundFont;
        _sampleRate = sampleRate;
        _synthesizer = new Synthesizer(soundFont, new SynthesizerSettings(sampleRate)
        {
            // A sampled piano uses a voice or two per note, and the sustain pedal keeps them all
            // ringing, so a busy passage needs far more voices than notes are held. Running out
            // means cutting notes off to make room, which pops. 256 is the most MeltySynth
            // allows, and voices cost nothing until they are actually sounding.
            MaximumPolyphony = MaxVoices,
        });
        Path = path;
        Name = name;
    }

    /// <summary>The file this was loaded from.</summary>
    public string Path { get; }

    /// <summary>The bank's own name, e.g. "Fluid R3 GM", for the status line.</summary>
    public string Name { get; }

    /// <summary>
    /// Loads a SoundFont. Large banks take a moment and a good deal of memory (FluidR3_GM is
    /// 148MB), so call this off the UI thread.
    /// </summary>
    /// <exception cref="InvalidDataException">The file isn't a usable SoundFont.</exception>
    public static SoundFontSynth Load(string path, int sampleRate)
    {
        try
        {
            var soundFont = new SoundFont(path);
            var name = string.IsNullOrWhiteSpace(soundFont.Info.BankName)
                ? System.IO.Path.GetFileNameWithoutExtension(path)
                : soundFont.Info.BankName.Trim();

            return new SoundFontSynth(soundFont, sampleRate, path, name);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"{System.IO.Path.GetFileName(path)} could not be read as a SoundFont: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Another player on the same bank, with its own instrument and notes. The bank's samples
    /// are shared, so the keyboard split's two halves cost no more memory than one.
    /// </summary>
    public SoundFontSynth CreateSibling() => new(_soundFont, _sampleRate, Path, Name);

    /// <summary>Switches to a General MIDI instrument number (0 is an acoustic grand piano).</summary>
    public void SetProgram(int program) =>
        _synthesizer.ProcessMidiMessage(Channel, ProgramChange, program, 0);

    /// <summary>The sustain pedal, which MeltySynth applies to the notes itself.</summary>
    public void SetSustain(bool down) =>
        _synthesizer.ProcessMidiMessage(Channel, ControlChange, SustainController, down ? 127 : 0);

    public void NoteOn(int note, int velocity) => _synthesizer.NoteOn(Channel, note, velocity);

    public void NoteOff(int note) => _synthesizer.NoteOff(Channel, note);

    /// <summary>
    /// Stops every note. By default each is released as if its key had been let go, so it dies
    /// away instead of being cut off with a click; immediate cuts them dead.
    /// </summary>
    public void AllNotesOff(bool immediate = false) => _synthesizer.NoteOffAll(immediate);

    /// <summary>How many voices are sounding now.</summary>
    public int ActiveVoices => _synthesizer.ActiveVoiceCount;

    /// <summary>True while any note (or the end of one) is still sounding.</summary>
    public bool IsSounding => _synthesizer.ActiveVoiceCount > 0;

    /// <summary>Fills the two channels with the next block of audio.</summary>
    public void Render(Span<float> left, Span<float> right) => _synthesizer.Render(left, right);
}

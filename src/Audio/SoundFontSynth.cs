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

    /// <summary>A MIDI program change: "play this channel with instrument N".</summary>
    private const int ProgramChange = 0xC0;

    /// <summary>A MIDI control change, used here for the sustain pedal (controller 64).</summary>
    private const int ControlChange = 0xB0;
    private const int SustainController = 64;

    private readonly Synthesizer _synthesizer;

    private SoundFontSynth(Synthesizer synthesizer, string path, string name)
    {
        _synthesizer = synthesizer;
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
            var synthesizer = new Synthesizer(soundFont, sampleRate);
            var name = string.IsNullOrWhiteSpace(soundFont.Info.BankName)
                ? System.IO.Path.GetFileNameWithoutExtension(path)
                : soundFont.Info.BankName.Trim();

            return new SoundFontSynth(synthesizer, path, name);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"{System.IO.Path.GetFileName(path)} could not be read as a SoundFont: {ex.Message}", ex);
        }
    }

    /// <summary>Switches to a General MIDI instrument number (0 is an acoustic grand piano).</summary>
    public void SetProgram(int program) =>
        _synthesizer.ProcessMidiMessage(Channel, ProgramChange, program, 0);

    /// <summary>The sustain pedal, which MeltySynth applies to the notes itself.</summary>
    public void SetSustain(bool down) =>
        _synthesizer.ProcessMidiMessage(Channel, ControlChange, SustainController, down ? 127 : 0);

    public void NoteOn(int note, int velocity) => _synthesizer.NoteOn(Channel, note, velocity);

    public void NoteOff(int note) => _synthesizer.NoteOff(Channel, note);

    /// <summary>Stops everything, leaving no tails ringing.</summary>
    public void AllNotesOff() => _synthesizer.NoteOffAll(immediate: true);

    /// <summary>Fills the two channels with the next block of audio.</summary>
    public void Render(Span<float> left, Span<float> right) => _synthesizer.Render(left, right);
}

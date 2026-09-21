namespace pianoroll.Models;

/// <summary>The instruments the keyboard can be played with.</summary>
public enum InstrumentKind
{
    /// <summary>A recorded piano, one WAV per key, rather than synthesis.</summary>
    RealPiano,

    /// <summary>A recorded saxophone, likewise.</summary>
    RealSaxophone,

    /// <summary>A recorded bass guitar, likewise.</summary>
    RealBass,

    /// <summary>The two recorded electric guitars, named after the packs they came from.</summary>
    BlackGuitar,
    GreenGuitar,

    Piano,
    Organ,
    ElectricGuitar,
    Banjo,
}

/// <summary>
/// An instrument as the Instrument dialog lists it. <paramref name="Program"/> is its General
/// MIDI number, used to pick the right instrument out of a SoundFont. <paramref name="SampleFolder"/>
/// names its folder under samples/ when it is played from recordings,
/// <paramref name="Sustained"/> marks the ones that are blown or bowed rather than struck, and
/// <paramref name="LowBoost"/> is how many decibels louder its lowest notes are played, fading
/// to nothing by middle C.
/// </summary>
public sealed record InstrumentInfo(
    InstrumentKind Kind,
    string Name,
    string Description,
    int Program,
    string? SampleFolder = null,
    bool Sustained = false,
    float LowBoost = 0)
{
    /// <summary>Every instrument, in the order the dialog shows them.</summary>
    public static IReadOnlyList<InstrumentInfo> All { get; } =
    [
        new(InstrumentKind.RealPiano, "Real piano",
            "A recorded grand piano, one sampled note per key. Needs the samples folder; falls back to the SoundFont without it.",
            Program: 0,         // Acoustic Grand Piano, for the SoundFont fallback
            SampleFolder: "piano"),
        new(InstrumentKind.RealSaxophone, "Real saxophone",
            "A recorded saxophone. Held notes run round the loop in the recording, so they last as long as the key is down.",
            Program: 65,        // Alto Sax, for the SoundFont fallback
            SampleFolder: "saxaphone",
            Sustained: true),
        new(InstrumentKind.RealBass, "Real bass",
            "Karoryfer's Big Little Bass: a bass played high on the neck, recorded on every note from B1 up.",
            Program: 33,        // Electric Bass (finger), for the SoundFont fallback
            SampleFolder: "bass",
            LowBoost: 6),       // its bottom notes are hard to hear otherwise
        new(InstrumentKind.BlackGuitar, "Real black guitar",
            "A recorded electric guitar, chromatically sampled across the whole fretboard.",
            Program: 27,        // Electric Guitar (clean), for the SoundFont fallback
            SampleFolder: "black"),
        new(InstrumentKind.GreenGuitar, "Real green guitar",
            "The other recorded electric guitar — a different instrument, sampled the same way.",
            Program: 27,
            SampleFolder: "green"),
        new(InstrumentKind.Piano, "Piano",
            "Synthesised struck strings: a bright attack that rings on and fades, even while the key is held.",
            Program: 0),        // Acoustic Grand Piano
        new(InstrumentKind.Organ, "Organ",
            "Drawbar tone wheels: holds at full strength for as long as the key is down.",
            Program: 16),       // Drawbar Organ
        new(InstrumentKind.ElectricGuitar, "Electric guitar",
            "A plucked string with a little amplifier grit, decaying over a few seconds.",
            Program: 27),       // Electric Guitar (clean)
        new(InstrumentKind.Banjo, "Banjo",
            "A plucked string too, but brighter and much shorter — the twang of a tight head.",
            Program: 105),      // Banjo
    ];

    public static InstrumentInfo For(InstrumentKind kind) => All.First(i => i.Kind == kind);

    /// <summary>The instrument saved under this name, or the piano when the name isn't one of ours.</summary>
    public static InstrumentInfo ByName(string? name) =>
        All.FirstOrDefault(i => string.Equals(i.Kind.ToString(), name, StringComparison.OrdinalIgnoreCase))
        ?? For(InstrumentKind.Piano);

    public override string ToString() => Name;
}

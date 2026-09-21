using System.Diagnostics;

namespace pianoroll.Audio;

/// <summary>
/// What the audio threads have been doing since the program started, for tracking down clicks
/// and pops. Written on the mixer thread and SDL's callback, read (approximately) from the UI
/// thread by F2; the numbers are only for reading by eye, so they don't need a lock.
/// </summary>
public sealed class AudioStats
{
    /// <summary>How many blocks SDL has asked for.</summary>
    public long Blocks;

    /// <summary>How long each block lasts, and so the time the mixer has to prepare the next.</summary>
    public double BlockMilliseconds;

    /// <summary>The longest the mixer has taken to mix a block, in milliseconds.</summary>
    public double SlowestMilliseconds;

    /// <summary>
    /// The slowest block's mixing split into its parts, in milliseconds: the instruments, the
    /// limiter, and time spent only waiting for the engine's lock while the UI thread held it.
    /// Each is the worst seen for that part, so they needn't add up to the slowest mix.
    /// </summary>
    public double SlowestInstruments;
    public double SlowestLimiter;
    public double LongestLockWait;

    /// <summary>Blocks that took the mixer more than half the time available: getting close.</summary>
    public long SlowBlocks;

    /// <summary>Blocks SDL asks for while starting up, when it fills its own buffer in a rush.</summary>
    public const int StartingBlocks = 20;

    /// <summary>When the device was opened, for timing the gaps.</summary>
    public long OpenedTicks;

    /// <summary>Blocks asked for during start-up with none ready: silent, before anything plays.</summary>
    public long StartingGaps;

    /// <summary>When the most recent playback gap happened, in seconds after the device opened.</summary>
    public double LastGapSeconds;

    /// <summary>Times SDL asked for a block during playback and none was ready: each is a gap you hear.</summary>
    public long LateBlocks;

    /// <summary>
    /// The longest time between SDL asking for one block and the next, in milliseconds. It should
    /// stay close to <see cref="BlockMilliseconds"/>; much longer means the audio thread was held
    /// up before our code ran at all (a garbage collection, or the system busy elsewhere).
    /// </summary>
    public double LongestGapMilliseconds;

    /// <summary>When the last block was asked for, for measuring the gaps.</summary>
    public long LastBlockTicks;

    /// <summary>Blocks in which the limiter turned the mix down.</summary>
    public long LimitedBlocks;

    /// <summary>The furthest the limiter has turned the mix down; 1 means never.</summary>
    public float LowestLimit = 1f;

    /// <summary>The most voices the SoundFont and the sampler have had sounding at once.</summary>
    public int MostSoundFontVoices;
    public int MostSampleVoices;

    public static double Milliseconds(long startTicks) =>
        (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;

    public override string ToString() =>
        $"Blocks: {Blocks:N0}, {BlockMilliseconds:0.0}ms each\n" +
        $"Slowest mix: {SlowestMilliseconds:0.00}ms\n" +
        $"   slowest instruments: {SlowestInstruments:0.00}ms\n" +
        $"   slowest limiter: {SlowestLimiter:0.00}ms\n" +
        $"   longest wait for the engine lock: {LongestLockWait:0.00}ms\n" +
        $"Mixes over half their time: {SlowBlocks:N0}\n" +
        $"Blocks not ready while starting up (silent): {StartingGaps:N0}\n" +
        $"Blocks not ready during playback (a gap you hear): {LateBlocks:N0}" +
        (LateBlocks > 0 ? $", the last {LastGapSeconds:0.0}s after starting\n" : "\n") +
        $"Longest wait between blocks: {LongestGapMilliseconds:0.0}ms\n" +
        $"Blocks where the limiter acted: {LimitedBlocks:N0} (lowest gain {LowestLimit:0.00})\n" +
        $"Most SoundFont voices at once: {MostSoundFontVoices}\n" +
        $"Most sample voices at once: {MostSampleVoices}";
}

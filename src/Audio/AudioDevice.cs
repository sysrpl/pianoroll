using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL2;

namespace pianoroll.Audio;

/// <summary>
/// The sound card, through SDL2 — the only thing SDL is used for here; the window is Avalonia's.
/// SDL is opened with just its audio subsystem, so no window, renderer or event loop is created.
///
/// Mixing happens ahead of time, on a thread of our own. The mixer thread renders blocks (the
/// notes, the limiter, the conversion to 16-bit) into a queue of up to two finished blocks, and
/// sleeps while the queue is full. SDL's callback, which runs against the sound card's deadline,
/// does nothing but lock, copy out the oldest finished block, unlock and wake the mixer — so the
/// callback is never slow, and when SDL falls behind and then asks for two blocks in a row, the
/// second is already waiting.
/// </summary>
public sealed class AudioDevice : IDisposable
{
    public const int SampleRate = 44100;

    /// <summary>
    /// Frames per block: 1024 is about 23ms, the time the mixer has to prepare each one. With
    /// two blocks waiting (<see cref="ReadyBlocks"/>) that's plenty of room for SDL's stalls.
    /// </summary>
    private const ushort BufferFrames = 1024;

    /// <summary>
    /// How many finished blocks may wait for SDL. Two means that when SDL stalls and then asks
    /// twice in quick succession, the second block is ready too, at the cost of one block more
    /// delay between mixing and the speaker.
    /// </summary>
    private const int ReadyBlocks = 2;

    /// <summary>Stereo: SoundFont instruments are recorded in stereo, and it costs nothing here.</summary>
    private const byte Channels = 2;

    private readonly SynthEngine _engine;

    // Kept in a field so the garbage collector can't collect the delegate SDL is calling.
    private readonly SDL.SDL_AudioCallback _callback;

    private uint _device;

    // ---- the hand-over between the mixer thread and SDL's callback -----------------------------

    /// <summary>Held only while a finished block changes hands: a swap on one side, a copy on the other.</summary>
    private readonly object _handover = new();

    /// <summary>Set by the callback when it has copied a block; the mixer sleeps on it while the queue is full.</summary>
    private readonly AutoResetEvent _copied = new(false);

    /// <summary>
    /// The finished blocks waiting for SDL, oldest at <see cref="_readyFront"/>. With the block
    /// the mixer is working on, that's three buffers in all.
    /// </summary>
    private readonly short[][] _ready = new short[ReadyBlocks][];
    private int _readyFront;
    private int _readyCount;

    /// <summary>A block of silence, played only if the mixer ever falls behind.</summary>
    private short[] _silence = [];

    private Thread? _mixer;
    private volatile bool _running;

    // ---- the mixer thread's own buffers ---------------------------------------------------------

    private float[] _mix = [];
    private short[] _work = [];

    // ---- the limiter ------------------------------------------------------------------------
    //
    // When the mix would go past the ceiling, the whole mix is turned down, then eased back up.
    // It looks ahead: the sound is delayed by a few milliseconds, so the gain can glide down
    // before a loud moment arrives instead of dropping in a single sample. An instant drop is a
    // step in the waveform of whichever channel isn't at its peak, and a step is a click.

    /// <summary>The loudest the output is allowed to get, just short of full scale.</summary>
    private const float Ceiling = 0.95f;

    /// <summary>How far ahead the limiter looks, in frames: about 3ms, and the delay it adds.</summary>
    private const int Lookahead = 128;

    /// <summary>Per-frame glide down: the gain has all but arrived by the time the loud frame is played.</summary>
    private static readonly float Attack = 1f - MathF.Exp(-4f / Lookahead);

    /// <summary>Per-frame glide back up: about a fifth of a second, too slow to be heard as pumping.</summary>
    private static readonly float Release = 1f - MathF.Exp(-1f / (0.2f * SampleRate));

    private readonly float[] _delayLeft = new float[Lookahead];
    private readonly float[] _delayRight = new float[Lookahead];
    private int _head;

    // The lowest gain anything in the delay needs, kept up to date rather than searched for:
    // a queue of candidates, lowest at the front, each with the frame it arrived on. A new gain
    // clears out any candidates it undercuts, and a candidate leaves once its frame has been
    // played. Each frame is added and removed once, so the cost per frame stays the same
    // however loud the music is. (Searching the whole window every frame was the slowest part
    // of mixing whenever the limiter was working.)
    private readonly float[] _candidateGain = new float[Lookahead + 1];
    private readonly long[] _candidateFrame = new long[Lookahead + 1];
    private int _candidateFront;
    private int _candidateCount;
    private long _frameNumber;

    /// <summary>The limiter's current gain: 1 normally, less while the mix would be too loud.</summary>
    private float _limit = 1f;

    public AudioDevice(SynthEngine engine)
    {
        _engine = engine;
        _callback = Callback;
    }

    /// <summary>What the audio threads have been doing, for F2.</summary>
    public AudioStats Stats { get; } = new();

    /// <summary>Why the device couldn't be opened, or null when it's playing.</summary>
    public string? Error { get; private set; }

    public bool IsOpen => _device != 0;

    /// <summary>Opens the default output device, starts the mixer thread, then starts playing.</summary>
    public void Open()
    {
        if (_device != 0)
            return;

        if (SDL.SDL_Init(SDL.SDL_INIT_AUDIO) != 0)
        {
            Error = $"SDL could not start its audio system: {SDL.SDL_GetError()}";
            return;
        }

        var desired = new SDL.SDL_AudioSpec
        {
            freq = SampleRate,
            format = SDL.AUDIO_S16,
            channels = Channels,
            samples = BufferFrames,
            callback = _callback,
        };

        // 0 allowed changes: SDL converts for us if the card wants something else, so the
        // engine can always work at one rate and block size.
        _device = SDL.SDL_OpenAudioDevice(null, 0, ref desired, out var obtained, 0);
        if (_device == 0)
        {
            Error = $"No sound output is available: {SDL.SDL_GetError()}";
            SDL.SDL_Quit();
            return;
        }

        Error = null;

        var samples = Math.Max(1, (int)obtained.samples) * Channels;
        _mix = new float[samples];
        _work = new short[samples];
        for (var i = 0; i < ReadyBlocks; i++)
            _ready[i] = new short[samples];
        _silence = new short[samples];
        Stats.BlockMilliseconds = samples / Channels * 1000.0 / SampleRate;
        Stats.OpenedTicks = Stopwatch.GetTimestamp();

        // The mixer starts first, so the first block is ready by the time SDL asks for it.
        _running = true;
        _mixer = new Thread(MixLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest,
            Name = "Audio mixer",
        };
        _mixer.Start();

        SDL.SDL_PauseAudioDevice(_device, 0);
    }

    /// <summary>
    /// The mixer thread: while there's room in the queue, mix the next block and add it; when the
    /// queue is full, sleep until SDL has taken one.
    /// </summary>
    private void MixLoop()
    {
        while (_running)
        {
            // Room is checked afresh every time round. A wake-up can arrive while the mixer was
            // busy rather than asleep, so waking is never taken to mean there must be room.
            bool room;
            lock (_handover)
                room = _readyCount < ReadyBlocks;
            if (!room)
            {
                _copied.WaitOne();
                continue;
            }

            var started = Stopwatch.GetTimestamp();
            var limited = Mix();
            var took = AudioStats.Milliseconds(started);

            Stats.SlowestMilliseconds = Math.Max(Stats.SlowestMilliseconds, took);
            if (took > Stats.BlockMilliseconds / 2)
                Stats.SlowBlocks++;
            if (limited)
                Stats.LimitedBlocks++;
            _engine.CountVoices(Stats);

            // Add the finished block to the back of the queue by swapping buffers: the slot's old
            // buffer, already played, becomes the one to mix into next. The lock is held for an
            // instant. Only the mixer adds, so the room checked above is still there.
            lock (_handover)
            {
                var slot = (_readyFront + _readyCount) % ReadyBlocks;
                (_ready[slot], _work) = (_work, _ready[slot]);
                _readyCount++;
            }
        }
    }

    /// <summary>Mixes one block into <see cref="_work"/>. True if the limiter had to act.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool Mix()
    {
        var frames = _work.Length / Channels;
        var limited = false;

        // Every buffer starts each block as silence, before anything is mixed into it, so nothing
        // left over from an earlier block can ever be heard again.
        Array.Clear(_mix);
        Array.Clear(_work);

        var instruments = Stopwatch.GetTimestamp();
        _engine.RenderStereo(_mix, frames);
        Stats.SlowestInstruments = Math.Max(Stats.SlowestInstruments, AudioStats.Milliseconds(instruments));
        Stats.LongestLockWait = Math.Max(Stats.LongestLockWait, _engine.LastLockWait);

        var limiter = Stopwatch.GetTimestamp();

        for (var frame = 0; frame < frames; frame++)
        {
            var left = _mix[frame * Channels];
            var right = _mix[frame * Channels + 1];

            // The frame leaving the delay is the one played now; the new frame takes its place.
            var outLeft = _delayLeft[_head];
            var outRight = _delayRight[_head];
            var peak = MathF.Max(MathF.Abs(left), MathF.Abs(right));
            _delayLeft[_head] = left;
            _delayRight[_head] = right;

            _head = (_head + 1) % Lookahead;

            // Aim for the lowest gain anything in the delay needs, and glide towards it.
            var target = LowestNeeded(peak > Ceiling ? Ceiling / peak : 1f);
            _limit += (target - _limit) * (target < _limit ? Attack : Release);
            if (_limit < 0.999f)
            {
                limited = true;
                Stats.LowestLimit = MathF.Min(Stats.LowestLimit, _limit);
            }

            // The clamp is a last line of defence; the limiter keeps the output inside it.
            _work[frame * Channels] = ToShort(outLeft * _limit);
            _work[frame * Channels + 1] = ToShort(outRight * _limit);
        }

        Stats.SlowestLimiter = Math.Max(Stats.SlowestLimiter, AudioStats.Milliseconds(limiter));
        return limited;
    }

    /// <summary>
    /// SDL's audio thread: copy out the block the mixer has ready, and wake the mixer to prepare
    /// the next. Nothing is mixed here, so this is always quick.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void Callback(IntPtr userData, IntPtr stream, int length)
    {
        var count = length / sizeof(short);
        var now = Stopwatch.GetTimestamp();

        // The gap since the last request: long ones mean the thread was held up before we ran.
        // The first few blocks are skipped, while the device is still settling.
        if (Stats.LastBlockTicks != 0 && Stats.Blocks > 10)
        {
            var gap = (now - Stats.LastBlockTicks) * 1000.0 / Stopwatch.Frequency;
            Stats.LongestGapMilliseconds = Math.Max(Stats.LongestGapMilliseconds, gap);
        }
        Stats.LastBlockTicks = now;
        Stats.Blocks++;

        bool copied;
        lock (_handover)
        {
            copied = _readyCount > 0;
            if (copied)
            {
                // The oldest finished block goes out.
                var block = _ready[_readyFront];
                Marshal.Copy(block, 0, stream, Math.Min(count, block.Length));
                _readyFront = (_readyFront + 1) % ReadyBlocks;
                _readyCount--;
            }
            else
            {
                // No block ready: play silence rather than an old block, and count it. SDL asks
                // for several blocks at once as it starts, before anything is playing, so those
                // are counted apart from gaps during playback, which are the ones you hear.
                Marshal.Copy(_silence, 0, stream, Math.Min(count, _silence.Length));
                if (Stats.Blocks <= AudioStats.StartingBlocks)
                {
                    Stats.StartingGaps++;
                }
                else
                {
                    Stats.LateBlocks++;
                    Stats.LastGapSeconds = (now - Stats.OpenedTicks) / (double)Stopwatch.Frequency;
                }
            }
        }

        // A block taken frees a slot, so the mixer may have work to do.
        if (copied)
            _copied.Set();
    }

    /// <summary>
    /// Adds this frame's needed gain to the window and returns the lowest gain needed by any
    /// frame still in it.
    /// </summary>
    private float LowestNeeded(float need)
    {
        var capacity = _candidateGain.Length;
        var frame = _frameNumber++;

        // Candidates this one undercuts can never be the lowest again.
        while (_candidateCount > 0)
        {
            var back = (_candidateFront + _candidateCount - 1) % capacity;
            if (_candidateGain[back] < need)
                break;
            _candidateCount--;
        }

        var slot = (_candidateFront + _candidateCount) % capacity;
        _candidateGain[slot] = need;
        _candidateFrame[slot] = frame;
        _candidateCount++;

        // Candidates whose frames have left the delay no longer count.
        while (_candidateFrame[_candidateFront] <= frame - Lookahead)
        {
            _candidateFront = (_candidateFront + 1) % capacity;
            _candidateCount--;
        }

        return _candidateGain[_candidateFront];
    }

    private static short ToShort(float value) =>
        (short)(Math.Clamp(value, -1f, 1f) * short.MaxValue);

    public void Dispose()
    {
        if (_device == 0)
            return;

        // Stop SDL asking for blocks first, then wake the mixer so it can see it should finish.
        SDL.SDL_PauseAudioDevice(_device, 1);
        _running = false;
        _copied.Set();
        _mixer?.Join(TimeSpan.FromSeconds(1));

        SDL.SDL_CloseAudioDevice(_device);
        _device = 0;
        SDL.SDL_Quit();
        _copied.Dispose();
    }
}

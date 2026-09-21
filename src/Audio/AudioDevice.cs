using System.Runtime.InteropServices;
using SDL2;

namespace pianoroll.Audio;

/// <summary>
/// The sound card, through SDL2 — the only thing SDL is used for here; the window is Avalonia's.
/// SDL is opened with just its audio subsystem, so no window, renderer or event loop is created.
///
/// SDL calls <see cref="Callback"/> on its own high-priority audio thread whenever it needs more
/// samples, and the engine fills the buffer. Nothing on that path may touch the UI or allocate.
/// </summary>
public sealed class AudioDevice : IDisposable
{
    public const int SampleRate = 44100;

    /// <summary>Frames per callback: 512 is about 12ms, short enough that keys feel immediate.</summary>
    private const ushort BufferFrames = 512;

    /// <summary>Stereo: SoundFont instruments are recorded in stereo, and it costs nothing here.</summary>
    private const byte Channels = 2;

    private readonly SynthEngine _engine;

    // Kept in a field so the garbage collector can't collect the delegate SDL is calling.
    private readonly SDL.SDL_AudioCallback _callback;

    private uint _device;
    private float[] _mix = [];
    private short[] _samples = [];

    public AudioDevice(SynthEngine engine)
    {
        _engine = engine;
        _callback = Callback;
    }

    /// <summary>Why the device couldn't be opened, or null when it's playing.</summary>
    public string? Error { get; private set; }

    public bool IsOpen => _device != 0;

    /// <summary>Opens the default output device and starts asking the engine for samples.</summary>
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

        // 0 allowed changes: SDL resamples for us if the card wants something else, so the
        // engine can always work at one rate.
        _device = SDL.SDL_OpenAudioDevice(null, 0, ref desired, out _, 0);
        if (_device == 0)
        {
            Error = $"No sound output is available: {SDL.SDL_GetError()}";
            SDL.SDL_Quit();
            return;
        }

        Error = null;
        SDL.SDL_PauseAudioDevice(_device, 0);
    }

    /// <summary>SDL's audio thread: fill <paramref name="length"/> bytes at <paramref name="stream"/>.</summary>
    private void Callback(IntPtr userData, IntPtr stream, int length)
    {
        var count = length / sizeof(short);
        var frames = count / Channels;
        if (_mix.Length < count)
        {
            // Only ever happens on the first callback, before any note can have been pressed.
            _mix = new float[count];
            _samples = new short[count];
        }

        _engine.RenderStereo(_mix, frames);

        for (var i = 0; i < count; i++)
        {
            // Soft clip, so a fistful of keys at once distorts gently instead of crackling.
            var value = MathF.Tanh(_mix[i]);
            _samples[i] = (short)(value * short.MaxValue);
        }

        Marshal.Copy(_samples, 0, stream, count);
    }

    public void Dispose()
    {
        if (_device == 0)
            return;

        SDL.SDL_PauseAudioDevice(_device, 1);
        SDL.SDL_CloseAudioDevice(_device);
        _device = 0;
        SDL.SDL_Quit();
    }
}

namespace pianoroll.Audio;

/// <summary>
/// A WAV file read into memory as 16-bit samples — enough of the RIFF format for recorded
/// instrument samples, without another dependency. Unknown chunks are skipped, so files with
/// LIST or cue chunks in them read fine.
///
/// Sample libraries come in whatever the studio recorded in: 8, 16, 24 and 32-bit whole numbers
/// and 32 or 64-bit floating point are all read, and all converted to 16-bit here. That loses a
/// little of a 24-bit recording's noise floor, which is inaudible under a note, and halves what
/// a set costs in memory.
/// </summary>
public sealed class WavFile
{
    private WavFile(short[] samples, int channels, int sampleRate, int loopStart, int loopEnd)
    {
        Samples = samples;
        Channels = channels;
        SampleRate = sampleRate;
        Frames = channels > 0 ? samples.Length / channels : 0;
        LoopStart = loopStart;
        LoopEnd = Math.Min(loopEnd, Math.Max(0, Frames - 1));
    }

    /// <summary>The audio, with the channels interleaved.</summary>
    public short[] Samples { get; }

    public int Channels { get; }
    public int SampleRate { get; }

    /// <summary>How many frames (one frame is one sample on every channel).</summary>
    public int Frames { get; }

    /// <summary>
    /// The sustain loop from the file's "smpl" chunk, in frames. Instruments that hold a note —
    /// a saxophone, an organ — are recorded with one, so the middle of the note can repeat for as
    /// long as the key is held. <see cref="HasLoop"/> is false when the file has none.
    /// </summary>
    public int LoopStart { get; }

    public int LoopEnd { get; }

    public bool HasLoop => LoopEnd > LoopStart && LoopStart >= 0;

    /// <exception cref="InvalidDataException">Not a 16-bit PCM WAV file.</exception>
    public static WavFile Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        if (new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException($"{Path.GetFileName(path)} is not a WAV file.");

        reader.ReadInt32();     // total size, which we don't need
        if (new string(reader.ReadChars(4)) != "WAVE")
            throw new InvalidDataException($"{Path.GetFileName(path)} is not a WAV file.");

        var channels = 0;
        var sampleRate = 0;
        var bits = 0;
        var format = 0;
        short[]? samples = null;
        var loopStart = 0;
        var loopEnd = 0;

        while (stream.Position < stream.Length - 8)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadInt32();
            if (size < 0)
                throw new InvalidDataException($"{Path.GetFileName(path)} is damaged.");

            if (id == "fmt ")
            {
                // Read unsigned: the "extensible" format code (0xFFFE) doesn't fit in a short.
                format = (ushort)reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();                 // bytes per second
                reader.ReadInt16();                 // block align
                bits = reader.ReadInt16();

                // 1 is whole-number PCM and 3 is floating point. 0xFFFE is "extensible", where
                // the real format is the first two bytes of a GUID further into the chunk.
                if (format == 0xFFFE && size >= 40)
                {
                    var extension = reader.ReadBytes(size - 16);
                    format = BitConverter.ToUInt16(extension, 8);
                    if (format is not (1 or 3))
                        throw new InvalidDataException($"{Path.GetFileName(path)} is in a format this program can't read.");
                }
                else
                {
                    if (format is not (1 or 3))
                    {
                        throw new InvalidDataException(
                            $"{Path.GetFileName(path)} must be PCM or floating-point audio (this one is format {format}).");
                    }

                    Skip(stream, size - 16);
                }

                var supported = format == 3 ? bits is 32 or 64 : bits is 8 or 16 or 24 or 32;
                if (!supported)
                {
                    throw new InvalidDataException(
                        $"{Path.GetFileName(path)} is {bits}-bit, which this program can't read.");
                }
            }
            else if (id == "data")
            {
                if (channels == 0)
                    throw new InvalidDataException($"{Path.GetFileName(path)} has no format information.");

                samples = ToSixteenBit(reader.ReadBytes(size), format, bits);

                // Reading doesn't stop here: the loop points usually come after the audio.
            }
            else if (id == "smpl" && size >= 36)
            {
                var chunk = reader.ReadBytes(size);
                var loops = BitConverter.ToInt32(chunk, 28);
                if (loops > 0 && size >= 36 + 24)
                {
                    loopStart = BitConverter.ToInt32(chunk, 36 + 8);
                    loopEnd = BitConverter.ToInt32(chunk, 36 + 12);
                }
            }
            else
            {
                Skip(stream, size);
            }

            // Chunks are padded to an even length.
            if (size % 2 == 1 && stream.Position < stream.Length)
                stream.Position++;
        }

        if (samples is null)
            throw new InvalidDataException($"{Path.GetFileName(path)} has no audio in it.");

        return new WavFile(samples, channels, sampleRate, loopStart, loopEnd);
    }

    /// <summary>Converts a data chunk from whatever the file holds into 16-bit samples.</summary>
    private static short[] ToSixteenBit(byte[] bytes, int format, int bits)
    {
        var bytesPerSample = bits / 8;
        var count = bytes.Length / bytesPerSample;
        var samples = new short[count];

        for (var i = 0; i < count; i++)
        {
            var at = i * bytesPerSample;
            samples[i] = (format, bits) switch
            {
                // 8-bit PCM is unsigned, with silence at 128; everything else is signed.
                (1, 8) => (short)((bytes[at] - 128) << 8),
                (1, 16) => BitConverter.ToInt16(bytes, at),
                (1, 24) => (short)(bytes[at + 1] | (sbyte)bytes[at + 2] << 8),
                (1, 32) => (short)(BitConverter.ToInt32(bytes, at) >> 16),
                (3, 32) => FromFloat(BitConverter.ToSingle(bytes, at)),
                (3, 64) => FromFloat((float)BitConverter.ToDouble(bytes, at)),
                _ => 0,
            };
        }
        return samples;
    }

    /// <summary>Floating-point audio runs -1 to 1, but can overshoot, so it is clamped.</summary>
    private static short FromFloat(float value) =>
        (short)(Math.Clamp(value, -1f, 1f) * short.MaxValue);

    private static void Skip(Stream stream, int bytes)
    {
        if (bytes > 0)
            stream.Position = Math.Min(stream.Length, stream.Position + bytes);
    }
}

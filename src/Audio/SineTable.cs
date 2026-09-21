namespace pianoroll.Audio;

/// <summary>
/// A sine wave looked up from a table instead of computed. A piano note is a stack of sixteen
/// partials, and sixteen notes can sound at once, so this runs about a quarter of a million times
/// a second — far too often for MathF.Sin.
///
/// Phase is measured in turns (0 to 1 is one cycle), which makes wrapping it a subtraction.
/// </summary>
internal static class SineTable
{
    private const int Size = 4096;
    private static readonly float[] Values = Build();

    /// <summary>The sine of <paramref name="turns"/>, interpolated between table entries.</summary>
    public static float Sin(float turns)
    {
        var position = (turns - MathF.Floor(turns)) * Size;
        var index = (int)position;
        var fraction = position - index;

        var current = Values[index & (Size - 1)];
        var next = Values[(index + 1) & (Size - 1)];
        return current + (next - current) * fraction;
    }

    private static float[] Build()
    {
        var values = new float[Size];
        for (var i = 0; i < Size; i++)
            values[i] = MathF.Sin(2f * MathF.PI * i / Size);
        return values;
    }
}

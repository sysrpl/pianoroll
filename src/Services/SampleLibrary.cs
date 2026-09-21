namespace pianoroll.Services;

/// <summary>
/// Finding the recorded instruments. Each one is a folder of WAV files named by note — A1.wav,
/// Asharp1.wav, C4.wav and so on — inside the samples folder: samples/piano, samples/saxaphone,
/// samples/bass. The samples are left out of the build output because they run to tens of
/// megabytes; publish.sh copies them next to the program instead.
/// </summary>
public static class SampleLibrary
{
    /// <summary>
    /// The folder holding one instrument's recordings, e.g. "piano", or null when it isn't there.
    /// </summary>
    public static string? Find(string instrument) =>
        ContentFolder.Find(Path.Combine("samples", instrument), "*.wav");
}

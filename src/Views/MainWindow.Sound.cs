using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using pianoroll.Audio;
using pianoroll.Helpers;
using pianoroll.Models;
using pianoroll.Services;

namespace pianoroll.Views;

/// <summary>Where the instrument sounds come from: a SoundFont, or the built-in synthesis.</summary>
public partial class MainWindow
{
    /// <summary>Written in the settings when the built-in sounds were chosen deliberately.</summary>
    private const string BuiltIn = "none";

    /// <summary>
    /// Loads the SoundFont the settings name, or the first one found in the usual places. A big
    /// bank takes a second or two and a lot of memory, so it's read off the UI thread; until it
    /// arrives the keyboard plays with the built-in synthesis.
    /// </summary>
    private async Task LoadSoundFontAsync(string? path, bool announceErrors)
    {
        if (path == BuiltIn)
        {
            UseBuiltIn();
            return;
        }

        path ??= SoundFonts.FindDefault();
        if (path is null)
        {
            ShowSoundSource("No SoundFont found — playing with the built-in sounds.");
            return;
        }

        SoundFontSynth soundFont;
        try
        {
            soundFont = await Task.Run(() => SoundFontSynth.Load(path!, AudioDevice.SampleRate));
        }
        catch (Exception ex)
        {
            ShowSoundSource($"{Path.GetFileName(path)} could not be loaded — playing with the built-in sounds.", isError: true);
            if (announceErrors)
                await MessageDialog.ShowAsync(this, "That SoundFont could not be loaded", ex.Message, isError: true);
            return;
        }

        _engine.UseSoundFont(soundFont);
        Remember(path);
        ShowSoundSource($"Instruments from {soundFont.Name}.");
    }

    /// <summary>The instruments whose samples are being read, so two goes can't overlap.</summary>
    private readonly HashSet<InstrumentKind> _loadingSamples = [];

    /// <summary>
    /// Reads an instrument's recordings the first time it's wanted. They run to hundreds of
    /// megabytes, so it happens on a background thread and the keyboard keeps playing meanwhile —
    /// the instrument falls back to the SoundFont until its samples arrive.
    /// </summary>
    private async Task EnsureSamplesAsync(InstrumentInfo instrument, bool announceErrors)
    {
        if (instrument.SampleFolder is not { } name
            || _engine.Sampled(instrument.Kind) is not null
            || !_loadingSamples.Add(instrument.Kind))
        {
            return;
        }

        try
        {
            var folder = SampleLibrary.Find(name);
            if (folder is null)
            {
                ShowSoundSource($"No samples/{name} folder was found — {instrument.Name} needs one.", isError: true);
                return;
            }

            ShowSoundSource($"Loading the {instrument.Name.ToLowerInvariant()}...");
            var samples = await Task.Run(() =>
                SampleInstrument.Load(folder, AudioDevice.SampleRate, instrument.Sustained));

            _engine.UseSamples(instrument.Kind, samples);
            ShowSoundSource($"{instrument.Name}: {samples.Count} recorded notes.");
        }
        catch (Exception ex)
        {
            ShowSoundSource($"The {instrument.Name.ToLowerInvariant()} samples could not be loaded: {ex.Message}", isError: true);
            if (announceErrors)
                await MessageDialog.ShowAsync(this, "Those samples could not be loaded", ex.Message, isError: true);
        }
        finally
        {
            _loadingSamples.Remove(instrument.Kind);
        }
    }

    /// <summary>Plays with the app's own synthesis instead of a SoundFont.</summary>
    private void UseBuiltIn()
    {
        _engine.UseSoundFont(null);
        ShowSoundSource("Playing with the built-in sounds.");
    }

    private void Remember(string path)
    {
        var settings = _settingsService.Settings;
        if (settings.SoundFontPath == path)
            return;

        settings.SoundFontPath = path;
        _settingsService.Save(settings);
    }

    /// <summary>
    /// Says where the sounds are coming from — on the status line while nothing is open, and
    /// always in the instrument button's tooltip, which is where it's wanted later on.
    /// </summary>
    private void ShowSoundSource(string message, bool isError = false)
    {
        Tip.Set(InstrumentButton, "Instrument",
            $"Choose what the keys sound like: piano, organ, electric guitar or banjo. {message}");

        if (_player.Song is not null)
            return;

        StatusText.Text = message;
        StatusText.Classes.Set("error", isError);
        StatusText.Classes.Set("dim", !isError);
    }
}

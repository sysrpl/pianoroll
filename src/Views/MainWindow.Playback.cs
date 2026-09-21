using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using pianoroll.Models;
using pianoroll.Services;

namespace pianoroll.Views;

/// <summary>Opening a MIDI file, playing it, and keeping the roll and seek bar in step with it.</summary>
public partial class MainWindow
{
    private readonly SongPlayer _player = new();

    /// <summary>Redraws the falling notes and starts the notes that have come due.</summary>
    private readonly DispatcherTimer _frames = new() { Interval = TimeSpan.FromMilliseconds(16) };

    /// <summary>Measures how long each frame took, so the sparks move at the same speed whatever the rate.</summary>
    private readonly Stopwatch _frameClock = Stopwatch.StartNew();
    private double _lastFrame;

    /// <summary>Wires the player to the keyboard, the roll and the seek bar.</summary>
    private void SetUpPlayback()
    {
        Roll.Keyboard = Keyboard;

        _player.NoteOn += (note, velocity) =>
        {
            _engine.NoteOn(note, velocity);
            Keyboard.ShowPressed(note, true);
            Roll.Burst(note, velocity);
        };
        _player.NoteOff += note =>
        {
            _engine.NoteOff(note);
            Keyboard.ShowPressed(note, false);
        };
        _player.Finished += (_, _) => UpdateTransport();
        _player.SustainChanged += down => _engine.SetSustain(down);

        Seek.Seeked += position =>
        {
            _player.Seek(position);
            ShowPosition();
        };

        _frames.Tick += (_, _) => Frame();
        _frames.Start();

        UpdateTransport();
    }

    /// <summary>One frame: let the player fire its notes, move the sparks, then redraw what moved.</summary>
    private void Frame()
    {
        var now = _frameClock.Elapsed.TotalSeconds;
        var elapsed = now - _lastFrame;
        _lastFrame = now;

        var playing = _player is { Song: not null, IsPlaying: true };
        if (playing)
        {
            _player.Tick();
            ShowPosition();     // which redraws the roll and the seek bar
        }

        Roll.Advance(elapsed);

        // Sparks from keys clicked by hand keep the roll redrawing while nothing is playing.
        if (!playing && Roll.HasSparks)
            Roll.InvalidateVisual();
    }

    /// <summary>Moves the playhead on the roll, the seek bar and the clock in the toolbar.</summary>
    private void ShowPosition()
    {
        var position = _player.Position;

        Roll.Position = position;
        Seek.Position = position;
        TimeText.Text = $"{Clock(position)} / {Clock(_player.Duration)}";

        Roll.InvalidateVisual();
        Seek.InvalidateVisual();
    }

    private static string Clock(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    /// <summary>File > Open MIDI: pick a file and load it.</summary>
    private async void OpenMidi_Click(object? sender, RoutedEventArgs e)
    {
        var options = new FilePickerOpenOptions
        {
            Title = "Open a MIDI file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                // Upper case too: file name patterns are case-sensitive on Linux.
                new FilePickerFileType("MIDI files")
                {
                    Patterns = [.. MidiFileReader.Patterns, .. MidiFileReader.Patterns.Select(p => p.ToUpperInvariant())],
                },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        };

        // Start in the music that comes with the program. Only when that folder is missing does
        // it fall back to wherever the last file was opened from.
        var lastFolder = _settingsService.Settings.LastMidiFolder;
        var start = MusicLibrary.FindDefault()
            ?? (!string.IsNullOrEmpty(lastFolder) && Directory.Exists(lastFolder) ? lastFolder : null);

        if (start is not null)
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(start);

        var files = await StorageProvider.OpenFilePickerAsync(options);
        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path is not null)
            await LoadSongAsync(path);
    }

    /// <summary>Reads the file off the UI thread, then hands the notes to the player and the roll.</summary>
    private async Task LoadSongAsync(string path)
    {
        // The lead-in is the roll's own look-ahead, so the first note enters at the very top.
        var leadIn = Roll.LookAhead;

        MidiSong song;
        try
        {
            song = await Task.Run(() => MidiFileReader.Load(path, leadIn));
        }
        catch (Exception ex)
        {
            await MessageDialog.ShowAsync(this, "That file could not be opened", ex.Message, isError: true);
            return;
        }

        _player.Load(song);
        Roll.Song = song;
        Seek.Duration = song.Duration;

        var settings = _settingsService.Settings;
        settings.LastMidiFolder = Path.GetDirectoryName(path) ?? "";
        _settingsService.Save(settings);

        StatusText.Text = $"{song.Name} — {song.Notes.Count:N0} notes, {Clock(song.Duration)}";
        PlaceholderText.IsVisible = false;
        ShowPosition();
        UpdateTransport();

        _player.Play();
        UpdateTransport();
    }

    private void Play_Click(object? sender, RoutedEventArgs e)
    {
        _player.Play();
        UpdateTransport();
    }

    private void Pause_Click(object? sender, RoutedEventArgs e)
    {
        _player.Pause();
        UpdateTransport();
    }

    private void Stop_Click(object? sender, RoutedEventArgs e)
    {
        _player.Stop();
        ShowPosition();
        UpdateTransport();
    }

    /// <summary>Greys out what can't be done: transport needs a song, play and pause swap over.</summary>
    private void UpdateTransport()
    {
        var hasSong = _player.Song is not null;

        PlayButton.IsEnabled = hasSong && !_player.IsPlaying;
        PauseButton.IsEnabled = hasSong && _player.IsPlaying;
        StopButton.IsEnabled = hasSong;
    }
}

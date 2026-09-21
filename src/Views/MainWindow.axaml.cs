using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using pianoroll.Audio;
using pianoroll.Helpers;
using pianoroll.Models;
using pianoroll.Services;

namespace pianoroll.Views;

/// <summary>
/// The main window: a menu and toolbar at the top, the playable keyboard along the bottom, and
/// the middle left empty for the roll that comes next.
/// </summary>
public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = null!;
    private readonly SynthEngine _engine = new();
    private readonly AudioDevice _audio;

    private InstrumentInfo _instrument = InstrumentInfo.For(InstrumentKind.Piano);

    /// <summary>True while the saved settings are being applied, so they aren't saved straight back.</summary>
    private bool _loading;

    /// <summary>Parameterless constructor for the visual designer.</summary>
    public MainWindow()
    {
        _audio = new AudioDevice(_engine);
        InitializeComponent();
    }

    public MainWindow(SettingsService settings) : this()
    {
        _settingsService = settings;
        var saved = settings.Settings;
        _loading = true;

        if (saved is { WindowWidth: > 0, WindowHeight: > 0 })
        {
            Width = saved.WindowWidth;
            Height = saved.WindowHeight;
        }

        VolumeSlider.Value = Math.Clamp(saved.Volume, 0, 1);
        _engine.Volume = (float)VolumeSlider.Value;

        SetInstrument(InstrumentInfo.ByName(saved.Instrument), save: false);

        Keyboard.NotePressed += note =>
        {
            _engine.NoteOn(note);
            Roll.Burst(note, velocity: 100);
        };
        Keyboard.NoteReleased += note => _engine.NoteOff(note);
        VolumeSlider.PropertyChanged += Volume_PropertyChanged;
        _loading = false;

        SetUpPlayback();

        // Opening the device late enough that a failure can be shown on the toolbar.
        _audio.Open();
        if (_audio.Error is { } error)
        {
            StatusText.Text = error;
            StatusText.Classes.Set("error", true);
            StatusText.Classes.Set("dim", false);
        }

        // A SoundFont can be 150MB, so it loads in the background once the window is up. The
        // keyboard plays with the built-in sounds until it arrives.
        Opened += async (_, _) => await LoadSoundFontAsync(
            saved.SoundFontPath is { Length: > 0 } savedFont ? savedFont : null,
            announceErrors: false);
    }

    /// <summary>Switches the sound the keys are played with, and remembers it.</summary>
    private void SetInstrument(InstrumentInfo instrument, bool save = true)
    {
        _instrument = instrument;
        _engine.Instrument = instrument.Kind;

        // Recordings are only read when the instrument they belong to is actually asked for.
        if (instrument.SampleFolder is not null)
            _ = EnsureSamplesAsync(instrument, announceErrors: save);
        InstrumentText.Text = instrument.Name;

        if (!save)
            return;

        var settings = _settingsService.Settings;
        settings.Instrument = instrument.Kind.ToString();
        _settingsService.Save(settings);
    }

    private async void Instrument_Click(object? sender, RoutedEventArgs e)
    {
        if (await InstrumentDialog.AskAsync(this, _instrument.Kind) is { } chosen)
            SetInstrument(chosen);
    }

    private void Volume_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != RangeBase.ValueProperty || _loading)
            return;

        _engine.Volume = (float)VolumeSlider.Value;

        var settings = _settingsService.Settings;
        settings.Volume = VolumeSlider.Value;
        _settingsService.Save(settings);
    }

    private async void About_Click(object? sender, RoutedEventArgs e) =>
        await new AboutWindow().ShowModalAsync(this);

    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();

    /// <summary>The backdrop window behind the player, while F1 has it up.</summary>
    private WallpaperWindow? _wallpaper;

    /// <summary>
    /// F1: puts images/wallpaper.jpg up on its own window filling the screen behind the player,
    /// so the player can be recorded against a clean background. F1 again takes it down.
    /// </summary>
    private void ToggleWallpaper()
    {
        if (_wallpaper is not null)
        {
            _wallpaper.Close();
            return;
        }

        _wallpaper = new WallpaperWindow(this);
        _wallpaper.Closed += (_, _) => _wallpaper = null;
        _wallpaper.Show();

        // Showing it can raise it over the player on some window managers; put the player back.
        Activate();
    }

    /// <summary>The window state to go back to when full screen is turned off.</summary>
    private WindowState _beforeFullScreen = WindowState.Normal;

    private void FullScreen_Click(object? sender, RoutedEventArgs e) => ToggleFullScreen();

    /// <summary>
    /// Fills the screen, or comes back to whatever the window was before — maximised windows
    /// return maximised.
    /// </summary>
    private void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = _beforeFullScreen;
            FullScreenIcon.Text = Icons.Fullscreen;
            Tip.Set(FullScreenButton, "Full screen (F11)",
                "Fill the screen with the keyboard and the falling notes.");
        }
        else
        {
            _beforeFullScreen = WindowState;
            WindowState = WindowState.FullScreen;
            FullScreenIcon.Text = Icons.FullscreenExit;
            Tip.Set(FullScreenButton, "Leave full screen (F11 or Escape)",
                "Put the window back the way it was.");
        }
    }

    /// <summary>The menu shows Ctrl+O; this is what makes it work.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            OpenMidi_Click(this, new RoutedEventArgs());
        }
        else if (e.Key == Key.F11 || (e.Key == Key.Escape && WindowState == WindowState.FullScreen))
        {
            e.Handled = true;
            ToggleFullScreen();
        }
        else if (e.Key == Key.F1)
        {
            e.Handled = true;
            ToggleWallpaper();
        }
        base.OnKeyDown(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _wallpaper?.Close();
        _frames.Stop();
        _player.Unload();
        _engine.AllNotesOff();
        _audio.Dispose();

        var settings = _settingsService.Settings;
        if (WindowState == WindowState.Normal)
        {
            // Closing from full screen or maximised would otherwise save the screen's size as
            // the window's, and it would open that big next time.
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
        }
        _settingsService.Save(settings);

        base.OnClosing(e);
    }
}

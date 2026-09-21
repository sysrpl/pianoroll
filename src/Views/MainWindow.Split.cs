using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using pianoroll.Models;

namespace pianoroll.Views;

/// <summary>
/// The keyboard split: two instruments in one song. Keys left of the red line in the roll play
/// the left instrument, keys right of it the main one, and each half has its own gain.
/// </summary>
public partial class MainWindow
{
    /// <summary>The left half's instrument. The right half's is the main instrument.</summary>
    private InstrumentInfo _leftInstrument = InstrumentInfo.For(InstrumentKind.RealBass);

    private bool SplitOn => SplitToggle.IsChecked == true;

    /// <summary>Whether the split's controls are shown. The split plays either way.</summary>
    private bool SplitControlsShown => SplitControlsToggle.IsChecked == true;

    /// <summary>Restores the split from the settings. Called while the window is loading them.</summary>
    private void SetUpSplit()
    {
        var saved = _settingsService.Settings;

        LeftGainSlider.Value = Math.Clamp(saved.LeftGain, LeftGainSlider.Minimum, LeftGainSlider.Maximum);
        RightGainSlider.Value = Math.Clamp(saved.RightGain, RightGainSlider.Minimum, RightGainSlider.Maximum);
        LeftGainSlider.PropertyChanged += Gain_PropertyChanged;
        RightGainSlider.PropertyChanged += Gain_PropertyChanged;

        Roll.SplitNote = saved.SplitNote;
        Roll.SplitMoved += note =>
        {
            _engine.SetSplit(SplitOn, note);
            var settings = _settingsService.Settings;
            settings.SplitNote = note;
            _settingsService.Save(settings);
        };

        SetLeftInstrument(InstrumentInfo.ByName(saved.LeftInstrument), save: false);
        SplitControlsToggle.IsChecked = saved.SplitControlsVisible;
        SplitToggle.IsChecked = saved.SplitEnabled;
        ApplySplit(save: false);
    }

    private void SplitToggle_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_loading)
            ApplySplit(save: true);
    }

    /// <summary>Shows or hides the split's row and red line, leaving the split itself playing.</summary>
    private void SplitControls_Changed(object? sender, RoutedEventArgs e)
    {
        ShowSplitControls();
        if (_loading)
            return;

        var settings = _settingsService.Settings;
        settings.SplitControlsVisible = SplitControlsShown;
        _settingsService.Save(settings);
    }

    /// <summary>
    /// The second toolbar row and the red line appear only while the split is on and its
    /// controls are shown; hidden, the toolbar is back to one row and the roll is clean.
    /// </summary>
    private void ShowSplitControls()
    {
        var visible = SplitOn && SplitControlsShown;
        SplitBar.IsVisible = visible;
        Roll.SplitEnabled = visible;
    }

    /// <summary>
    /// Turns the split on or off. The main instrument button steps aside while it is on, since
    /// the right half's button sets that instrument, and the show-controls toggle comes alive.
    /// </summary>
    private void ApplySplit(bool save)
    {
        var on = SplitOn;

        SplitControlsToggle.IsEnabled = on;
        ShowSplitControls();
        InstrumentButton.IsEnabled = !on;

        _engine.SetSplit(on, Roll.SplitNote);
        _engine.SetGain(left: true, (float)LeftGainSlider.Value);
        _engine.SetGain(left: false, (float)RightGainSlider.Value);
        ShowGains();

        if (on && _leftInstrument.SampleFolder is not null)
            _ = EnsureSamplesAsync(_leftInstrument, announceErrors: save);

        if (!save)
            return;

        var settings = _settingsService.Settings;
        settings.SplitEnabled = on;
        _settingsService.Save(settings);
    }

    private async void LeftInstrument_Click(object? sender, RoutedEventArgs e)
    {
        if (await InstrumentDialog.AskAsync(this, _leftInstrument.Kind) is { } chosen)
            SetLeftInstrument(chosen, save: true);
    }

    private void SetLeftInstrument(InstrumentInfo instrument, bool save)
    {
        _leftInstrument = instrument;
        _engine.LeftInstrument = instrument.Kind;
        LeftInstrumentText.Text = instrument.Name;

        // Recordings are only read when an instrument is actually going to be heard.
        if (SplitOn && instrument.SampleFolder is not null)
            _ = EnsureSamplesAsync(instrument, announceErrors: save);

        if (!save)
            return;

        var settings = _settingsService.Settings;
        settings.LeftInstrument = instrument.Kind.ToString();
        _settingsService.Save(settings);
    }

    private void Gain_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != RangeBase.ValueProperty)
            return;

        var left = ReferenceEquals(sender, LeftGainSlider);
        var decibels = left ? LeftGainSlider.Value : RightGainSlider.Value;
        _engine.SetGain(left, (float)decibels);
        ShowGains();

        if (_loading)
            return;

        var settings = _settingsService.Settings;
        if (left)
            settings.LeftGain = decibels;
        else
            settings.RightGain = decibels;
        _settingsService.Save(settings);
    }

    private void ShowGains()
    {
        LeftGainText.Text = Decibels(LeftGainSlider.Value);
        RightGainText.Text = Decibels(RightGainSlider.Value);
    }

    private static string Decibels(double value) => value switch
    {
        > 0 => $"+{value:0} dB",
        < 0 => $"{value:0} dB",
        _ => "0 dB",
    };
}

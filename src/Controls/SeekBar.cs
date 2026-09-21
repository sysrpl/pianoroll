using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace pianoroll.Controls;

/// <summary>
/// The vertical bar down the right-hand side: where the playhead is in the song, and how to move
/// it. The top is the start of the song and the bottom the end, so the filled part grows downward
/// as the song plays.
///
/// Click anywhere on the track to seek there, drag the handle to scrub, or roll the wheel to jump
/// back and forward a few seconds at a time.
/// </summary>
public class SeekBar : Control
{
    /// <summary>Seconds moved per wheel notch.</summary>
    private const double WheelSeconds = 5;

    private static readonly IBrush Track = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static readonly IBrush Elapsed = new SolidColorBrush(Color.FromRgb(0x6A, 0xA0, 0xBD));
    private static readonly IBrush Handle = new SolidColorBrush(Color.FromRgb(0xDA, 0xDA, 0xDA));
    private static readonly IBrush HandleHot = new SolidColorBrush(Color.FromRgb(0xA8, 0xD4, 0xE8));
    private static readonly IBrush Mark = new SolidColorBrush(Colors.White, 0.12);

    private bool _dragging;
    private bool _hot;

    /// <summary>Raised as the user seeks, with the position in seconds.</summary>
    public event Action<double>? Seeked;

    /// <summary>How long the song is, in seconds. Zero disables the bar.</summary>
    public double Duration { get; set; }

    /// <summary>Where the playhead is, in seconds. The window sets this on every frame.</summary>
    public double Position { get; set; }

    protected override Size MeasureOverride(Size availableSize) => new(22, 0);

    public override void Render(DrawingContext context)
    {
        var height = Bounds.Height;
        var width = Bounds.Width;
        if (height <= 0)
            return;

        var trackWidth = 6.0;
        var x = (width - trackWidth) / 2;
        var track = new Rect(x, 6, trackWidth, Math.Max(0, height - 12));
        context.DrawRectangle(Track, null, new RoundedRect(track, trackWidth / 2));

        // A mark every 30 seconds gives a sense of scale on a long song.
        if (Duration > 0)
        {
            for (var seconds = 30.0; seconds < Duration; seconds += 30)
            {
                var y = track.Y + track.Height * (seconds / Duration);
                context.FillRectangle(Mark, new Rect(x - 3, y, trackWidth + 6, 1));
            }
        }

        var fraction = Duration > 0 ? Math.Clamp(Position / Duration, 0, 1) : 0;
        var played = new Rect(track.X, track.Y, trackWidth, track.Height * fraction);
        context.DrawRectangle(Elapsed, null, new RoundedRect(played, trackWidth / 2));

        // The handle: a rounded bar the full width of the control, easy to grab.
        var handleY = track.Y + track.Height * fraction;
        var handle = new Rect(2, handleY - 5, Math.Max(0, width - 4), 10);
        context.DrawRectangle(_dragging || _hot ? HandleHot : Handle, null, new RoundedRect(handle, 3));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Duration <= 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _dragging = true;
        e.Pointer.Capture(this);
        SeekTo(e.GetPosition(this).Y);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging)
            SeekTo(e.GetPosition(this).Y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
        InvalidateVisual();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hot = true;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hot = false;
        InvalidateVisual();
    }

    /// <summary>The wheel rewinds and fast-forwards: up goes back, down goes on.</summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Duration <= 0)
            return;

        Seeked?.Invoke(Math.Clamp(Position - e.Delta.Y * WheelSeconds, 0, Duration));
        e.Handled = true;
    }

    /// <summary>Turns a y position on the track into a place in the song.</summary>
    private void SeekTo(double y)
    {
        var usable = Math.Max(1, Bounds.Height - 12);
        var fraction = Math.Clamp((y - 6) / usable, 0, 1);
        Seeked?.Invoke(fraction * Duration);
    }
}

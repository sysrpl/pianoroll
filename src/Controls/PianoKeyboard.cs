using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace pianoroll.Controls;

/// <summary>
/// A playable piano keyboard. It draws the white and black keys across its width, and raises
/// <see cref="NotePressed"/> and <see cref="NoteReleased"/> as the mouse presses them.
///
/// Dragging across the keys slides from note to note, the way running a finger along a real
/// keyboard does: the note under the pointer is released and the next one pressed.
/// </summary>
public class PianoKeyboard : Control
{
    /// <summary>Pitch classes with a black key to their right (C, D, F, G, A).</summary>
    private static readonly bool[] HasSharp = [true, false, true, false, false, true, false, true, false, true, false, false];

    /// <summary>Semitones from C that are black keys.</summary>
    private static readonly bool[] IsBlackKey = [false, true, false, true, false, false, true, false, true, false, true, false];

    // A full-sized piano: 88 keys from A0 up to C8.
    public static readonly StyledProperty<int> FirstNoteProperty =
        AvaloniaProperty.Register<PianoKeyboard, int>(nameof(FirstNote), 21);      // A0

    public static readonly StyledProperty<int> LastNoteProperty =
        AvaloniaProperty.Register<PianoKeyboard, int>(nameof(LastNote), 108);      // C8

    /// <summary>
    /// How long a white key is against its width. A piano's are about 23mm across and 150mm from
    /// the felt to the front edge, so the keyboard's height follows the window's width rather
    /// than being fixed: narrow the window and the keys get shorter as well as thinner.
    /// </summary>
    private const double KeyProportion = 6.4;

    /// <summary>Limits on that, so a very narrow or very wide window still leaves a playable keyboard.</summary>
    private const double MinimumHeight = 76;
    private const double MaximumHeight = 210;

    private readonly List<Key> _keys = [];
    private readonly HashSet<int> _held = [];

    /// <summary>The note the mouse is holding down, or -1. Only one at a time with one pointer.</summary>
    private int _mouseNote = -1;

    static PianoKeyboard()
    {
        AffectsRender<PianoKeyboard>(FirstNoteProperty, LastNoteProperty);
        AffectsMeasure<PianoKeyboard>(FirstNoteProperty, LastNoteProperty);
    }

    /// <summary>Raised when a key goes down, with its MIDI note number.</summary>
    public event Action<int>? NotePressed;

    /// <summary>Raised when a key comes back up.</summary>
    public event Action<int>? NoteReleased;

    /// <summary>The MIDI note of the leftmost key. 21 is A0, the bottom of a full-sized piano.</summary>
    public int FirstNote
    {
        get => GetValue(FirstNoteProperty);
        set => SetValue(FirstNoteProperty, value);
    }

    /// <summary>The MIDI note of the rightmost key. 108 is C8, the top of a full-sized piano.</summary>
    public int LastNote
    {
        get => GetValue(LastNoteProperty);
        set => SetValue(LastNoteProperty, value);
    }

    /// <summary>
    /// How wide a falling note should be drawn: the width of a black key, whatever key it lands
    /// on, so the bars above the keyboard are all the same size.
    /// </summary>
    public double NoteWidth { get; private set; }

    /// <summary>
    /// Where a key is drawn, so the falling notes above can line up with it. False when the note
    /// is outside the keyboard's range, or before the first layout pass.
    /// </summary>
    public bool TryGetKeyBounds(int note, out Rect bounds)
    {
        foreach (var key in _keys)
        {
            if (key.Note != note)
                continue;

            bounds = key.Bounds;
            return true;
        }

        bounds = default;
        return false;
    }

    /// <summary>
    /// Shows every key as up again and forgets the one the mouse was holding. Used when playback
    /// pauses or stops, after the sound itself has been silenced, so no key is left lit.
    /// </summary>
    public void ReleaseAll()
    {
        _held.Clear();
        _mouseNote = -1;
        InvalidateVisual();
    }

    /// <summary>Shows a note as held, e.g. one played from somewhere other than the mouse.</summary>
    public void ShowPressed(int note, bool pressed)
    {
        if (pressed ? _held.Add(note) : _held.Remove(note))
            InvalidateVisual();
    }

    /// <summary>Asks for the height that keeps the keys in proportion at this width.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : Bounds.Width;
        if (width <= 0)
            return new Size(0, MinimumHeight);

        var whites = 0;
        for (var note = FirstNote; note <= LastNote; note++)
        {
            if (!IsBlackKey[note % 12])
                whites++;
        }

        var height = whites > 0
            ? Math.Clamp(width / whites * KeyProportion, MinimumHeight, MaximumHeight)
            : MinimumHeight;

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize);
        return base.ArrangeOverride(finalSize);
    }

    /// <summary>
    /// Works out where every key goes. White keys share the width equally; black keys are
    /// narrower, two thirds as tall, and straddle the line between their white neighbours.
    /// </summary>
    private void Layout(Size size)
    {
        _keys.Clear();

        var whiteCount = 0;
        for (var note = FirstNote; note <= LastNote; note++)
        {
            if (!IsBlackKey[note % 12])
                whiteCount++;
        }
        if (whiteCount == 0 || size.Width <= 0)
            return;

        var whiteWidth = size.Width / whiteCount;
        var blackWidth = whiteWidth * 0.62;
        var blackHeight = size.Height * 0.62;
        NoteWidth = blackWidth;

        var white = 0;
        for (var note = FirstNote; note <= LastNote; note++)
        {
            var pitchClass = note % 12;

            if (IsBlackKey[pitchClass])
                continue;

            _keys.Add(new Key(note, new Rect(white * whiteWidth, 0, whiteWidth, size.Height), false));

            // The black key that sits to the right of this white one, if the range still has it.
            if (HasSharp[pitchClass] && note + 1 <= LastNote)
            {
                var x = (white + 1) * whiteWidth - blackWidth / 2;
                _keys.Add(new Key(note + 1, new Rect(x, 0, blackWidth, blackHeight), true));
            }
            white++;
        }
    }

    // Built once: Render runs on every repaint, and a held key repaints the whole keyboard.
    // Ivory is never flat white: it catches light at the back and is brightest at the front edge.
    private static readonly IBrush WhiteKey = Shade(Color.FromRgb(0xC9, 0xC9, 0xC2), Color.FromRgb(0xFA, 0xFA, 0xF7));
    private static readonly IBrush WhiteKeyDown = Shade(Color.FromRgb(0x5E, 0x9C, 0xBE), Color.FromRgb(0xB4, 0xDF, 0xF4));
    private static readonly IBrush BlackKey = Shade(Color.FromRgb(0x3C, 0x3C, 0x3E), Color.FromRgb(0x08, 0x08, 0x0A));
    private static readonly IBrush BlackKeyDown = Shade(Color.FromRgb(0x3E, 0x7E, 0x9C), Color.FromRgb(0x10, 0x32, 0x44));

    /// <summary>The gap between white keys, and the shadow the black keys cast into it.</summary>
    private static readonly IBrush KeyGap = new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x17));
    private static readonly IBrush KeyShadow = new SolidColorBrush(Colors.Black, 0.45);

    /// <summary>The front edge of a white key, where the ivory turns under.</summary>
    private static readonly IBrush WhiteKeyLip = new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xA2), 0.9);

    /// <summary>The light along the top of a black key.</summary>
    private static readonly IBrush BlackKeyTop = new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x72), 0.85);

    /// <summary>The felt strip behind the keys, as on a real piano.</summary>
    private static readonly IBrush Felt = new SolidColorBrush(Color.FromRgb(0x7A, 0x1F, 0x2E));

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
            return;

        var accent = this.FindResource("MintAccentBrush") as ISolidColorBrush;
        var glow = accent?.Color ?? Color.FromRgb(0x6A, 0xA0, 0xBD);

        // The felt strip the keys rest against.
        context.FillRectangle(Felt, new Rect(0, 0, width, 3));

        // White keys, with a thin dark gap between them and a turned-under front edge.
        foreach (var key in _keys)
        {
            if (key.IsBlack)
                continue;

            var held = _held.Contains(key.Note);
            var body = new Rect(key.Bounds.X, 3, key.Bounds.Width - 1, height - 3);

            context.FillRectangle(KeyGap, key.Bounds);
            context.DrawRectangle(held ? WhiteKeyDown : WhiteKey, null,
                new RoundedRect(body, new CornerRadius(0, 0, 3, 3)));

            // A held key is pressed down at the front, so the lip almost disappears.
            var lip = held ? 2.0 : 4.0;
            context.FillRectangle(WhiteKeyLip, new Rect(body.X, body.Bottom - lip, body.Width, 1));

            if (held)
                context.FillRectangle(new SolidColorBrush(glow, 0.35), new Rect(body.X, 3, body.Width, 10));
        }

        // Black keys sit on top, with a shadow under them and a lit top face.
        foreach (var key in _keys)
        {
            if (!key.IsBlack)
                continue;

            var held = _held.Contains(key.Note);
            var bounds = key.Bounds;

            context.DrawRectangle(KeyShadow, null,
                new RoundedRect(bounds.Translate(new Vector(1.5, 2)), new CornerRadius(0, 0, 3, 3)));
            context.DrawRectangle(held ? BlackKeyDown : BlackKey, null,
                new RoundedRect(bounds, new CornerRadius(0, 0, 3, 3)));

            // The bevel along the top of the key, which is what reads as three-dimensional.
            context.FillRectangle(held ? new SolidColorBrush(glow, 0.7) : BlackKeyTop,
                new Rect(bounds.X + 1, bounds.Y + 1, bounds.Width - 2, 1.5));
        }
    }

    /// <summary>A vertical gradient: the shading that makes a key look like an object.</summary>
    private static IBrush Shade(Color top, Color bottom) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(top, 0),
            new GradientStop(bottom, 1),
        },
    };

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        e.Pointer.Capture(this);
        PressAt(e.GetPosition(this));
        e.Handled = true;
    }

    /// <summary>Dragging with the button down slides from key to key.</summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_mouseNote >= 0)
            PressAt(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        ReleaseMouseNote();
        e.Pointer.Capture(null);
    }

    /// <summary>Losing the pointer (the window being dragged away, say) must not leave a note stuck on.</summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        ReleaseMouseNote();
    }

    private void PressAt(Point point)
    {
        var note = NoteAt(point);
        if (note == _mouseNote)
            return;

        ReleaseMouseNote();
        if (note < 0)
            return;

        _mouseNote = note;
        _held.Add(note);
        InvalidateVisual();
        NotePressed?.Invoke(note);
    }

    private void ReleaseMouseNote()
    {
        if (_mouseNote < 0)
            return;

        var note = _mouseNote;
        _mouseNote = -1;
        _held.Remove(note);
        InvalidateVisual();
        NoteReleased?.Invoke(note);
    }

    /// <summary>The note under the pointer, black keys winning where they overlap a white one.</summary>
    private int NoteAt(Point point)
    {
        foreach (var key in _keys)
        {
            if (key.IsBlack && key.Bounds.Contains(point))
                return key.Note;
        }

        foreach (var key in _keys)
        {
            if (!key.IsBlack && key.Bounds.Contains(point))
                return key.Note;
        }

        return -1;
    }

    private readonly record struct Key(int Note, Rect Bounds, bool IsBlack);
}

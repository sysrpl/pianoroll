using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using pianoroll.Models;

namespace pianoroll.Controls;

/// <summary>
/// The falling notes. Each note is a bar in the column of the key it will play, moving down so
/// that its bottom edge touches the keyboard exactly when the note sounds. A bar's length is the
/// note's length, so a whole note is four times the bar of a quarter note, and every bar is the
/// same width whether it lands on a white key or a black one.
///
/// Bars are coloured by register — cool at the bass end, warm at the treble — and glow as they
/// fall. Landing throws off a burst of sparks along the keyboard, which drift up and fade.
///
/// It reads the key positions from the <see cref="Keyboard"/> below it, which is the same width,
/// so a bar and its key line up whatever the window size.
/// </summary>
public class PianoRoll : Control
{
    public static readonly StyledProperty<double> LookAheadProperty =
        AvaloniaProperty.Register<PianoRoll, double>(nameof(LookAhead), 4.0);

    /// <summary>
    /// How far back to start looking for notes that are still sounding. Ten seconds covers any
    /// held chord without walking the whole song on every frame.
    /// </summary>
    private const double MaxNoteLength = 10.0;

    private const int MaxParticles = 900;

    /// <summary>
    /// The colours notes are drawn in. The last repeats the first, so the palette is a loop: a
    /// note's place in it comes from its pitch and from when it is played, and the whole thing
    /// turns slowly through the loop as a piece goes on, so the same phrase never comes back in
    /// the same colours.
    /// </summary>
    private static readonly Color[] Registers =
    [
        Color.FromRgb(0x38, 0xBD, 0xF8),      // cyan
        Color.FromRgb(0x62, 0x8C, 0xF7),      // blue
        Color.FromRgb(0xA0, 0x78, 0xF0),      // violet
        Color.FromRgb(0xEE, 0x72, 0xC8),      // pink
        Color.FromRgb(0xFF, 0x9C, 0x6A),      // warm orange
        Color.FromRgb(0x5A, 0xD9, 0xB0),      // sea green
        Color.FromRgb(0x38, 0xBD, 0xF8),      // back to the cyan it started on
    ];

    /// <summary>How long the palette takes to come all the way round, in seconds.</summary>
    private const double CycleSeconds = 38;

    /// <summary>
    /// How much of the palette the keyboard itself spans. Less than the whole loop, so that the
    /// bass and the treble are always different but never opposite.
    /// </summary>
    private const double KeyboardSpan = 2.2;

    private static readonly IBrush HitLine = new SolidColorBrush(Colors.White, 0.35);
    private static readonly IBrush OctaveLine = new SolidColorBrush(Colors.White, 0.04);

    /// <summary>Semitones from C that are black keys.</summary>
    private static readonly bool[] IsBlackKey =
        [false, true, false, true, false, false, true, false, true, false, true, false];

    /// <summary>Brushes are cached by colour: a few hundred sparks a frame mustn't allocate.</summary>
    private readonly Dictionary<uint, IBrush> _brushes = [];

    private readonly Particle[] _particles = new Particle[MaxParticles];
    private int _particleCount;
    private readonly Random _random = new();

    /// <summary>Seconds of animation so far, which turns the palette when nothing is playing.</summary>
    private double _elapsed;

    /// <summary>The keyboard the bars fall onto. Set once, in the window.</summary>
    public PianoKeyboard? Keyboard { get; set; }

    /// <summary>The song being played, or null when nothing is loaded.</summary>
    public MidiSong? Song { get; set; }

    /// <summary>Where the playhead is, in seconds. The window sets this on every frame.</summary>
    public double Position { get; set; }

    /// <summary>True while sparks are still in the air, so the window knows to keep redrawing.</summary>
    public bool HasSparks => _particleCount > 0;

    /// <summary>How many seconds of music are visible from the keyboard to the top of the roll.</summary>
    public double LookAhead
    {
        get => GetValue(LookAheadProperty);
        set => SetValue(LookAheadProperty, value);
    }

    /// <summary>
    /// Throws a burst of sparks up off a key, as a note lands on it. A note struck harder throws
    /// more of them, and further.
    /// </summary>
    public void Burst(int note, int velocity)
    {
        if (Keyboard is null || !Keyboard.TryGetKeyBounds(note, out var key) || Bounds.Height <= 0)
            return;

        var colour = ColourFor(note, When);
        var force = Math.Clamp(velocity, 1, 127) / 127.0;
        var count = (int)(6 + 18 * force);
        var spread = Math.Max(4.0, Keyboard.NoteWidth);

        for (var i = 0; i < count && _particleCount < MaxParticles; i++)
        {
            // Sparks leave from across the width of the key, mostly upward.
            var angle = (_random.NextDouble() - 0.5) * 1.1;
            var speed = (70 + 320 * force) * (0.45 + 0.55 * _random.NextDouble());

            _particles[_particleCount++] = new Particle
            {
                X = key.Center.X + (_random.NextDouble() - 0.5) * spread,
                Y = Bounds.Height - _random.NextDouble() * 6,
                VelocityX = Math.Sin(angle) * speed * 0.55,
                VelocityY = -Math.Cos(angle) * speed,
                Life = 0.45 + 1.1 * _random.NextDouble(),
                Age = 0,
                Size = 1.1 + 2.2 * _random.NextDouble(),
                // A few sparks are near-white, the rest carry the note's colour.
                Colour = _random.NextDouble() < 0.25 ? Lighten(colour, 0.7f) : colour,
            };
        }
    }

    /// <summary>Moves the sparks on by <paramref name="seconds"/>, and drops the spent ones.</summary>
    public void Advance(double seconds)
    {
        var step = Math.Clamp(seconds, 0, 0.1);
        _elapsed += step;

        if (_particleCount == 0)
            return;

        for (var i = _particleCount - 1; i >= 0; i--)
        {
            ref var particle = ref _particles[i];
            particle.Age += step;

            if (particle.Age >= particle.Life)
            {
                // Swap the last one into this slot instead of shuffling the whole array down.
                _particles[i] = _particles[--_particleCount];
                continue;
            }

            particle.X += particle.VelocityX * step;
            particle.Y += particle.VelocityY * step;

            // Sparks slow as they rise, drift a little, and start to fall back.
            particle.VelocityY += 150 * step;
            particle.VelocityX *= 1 - 0.9 * step;
        }
    }

    public override void Render(DrawingContext context)
    {
        var height = Bounds.Height;
        var width = Bounds.Width;
        if (height <= 0 || width <= 0)
            return;

        DrawOctaveLines(context, height);
        DrawFallingNotes(context, height);
        DrawHitLine(context, width, height);
        DrawParticles(context);
    }

    private void DrawFallingNotes(DrawingContext context, double height)
    {
        if (Song is null || Keyboard is null)
            return;

        var look = Math.Max(0.5, LookAhead);
        var pixelsPerSecond = height / look;
        var position = Position;
        var barWidth = Math.Max(2, Keyboard.NoteWidth);

        // Only the notes in the visible window are drawn; a long song is mostly off-screen.
        var first = Song.IndexAt(position - MaxNoteLength);
        for (var i = first; i < Song.Notes.Count; i++)
        {
            var note = Song.Notes[i];
            if (note.Start > position + look)
                break;              // the rest start later still: nothing more is visible
            if (note.End < position)
                continue;           // already played and gone below the keyboard

            if (!Keyboard.TryGetKeyBounds(note.Note, out var key))
                continue;           // outside the keys being shown

            // The bar's bottom reaches the keyboard as the note starts.
            var bottom = height - (note.Start - position) * pixelsPerSecond;
            var top = bottom - note.Duration * pixelsPerSecond;
            if (bottom <= 0)
                continue;

            var bar = new Rect(
                key.Center.X - barWidth / 2,
                Math.Max(0, top),
                barWidth,
                Math.Max(2, Math.Min(bottom, height) - Math.Max(0, top)));

            var sounding = note.Start <= position && note.End > position;

            // Colour comes from when the note is played, not from where the playhead is, so a
            // bar keeps its colour all the way down instead of shifting as it falls.
            var colour = ColourFor(note.Note, note.Start);

            // Two soft haloes behind the bar do the work of a blur.
            context.DrawRectangle(Brush(colour, sounding ? 0.30f : 0.16f), null,
                new RoundedRect(bar.Inflate(6), 8));
            context.DrawRectangle(Brush(colour, sounding ? 0.45f : 0.26f), null,
                new RoundedRect(bar.Inflate(2.5), 5));

            // The bar itself, lighter down its length, and brighter still while it sounds.
            var body = sounding ? Lighten(colour, 0.45f) : colour;
            context.DrawRectangle(Fill(body), null, new RoundedRect(bar, 3));

            // A bright cap on the leading edge: the part about to hit the key.
            if (bar.Height > 5)
            {
                var cap = new Rect(bar.X, bar.Bottom - 2.5, bar.Width, 2.5);
                context.DrawRectangle(Brush(Lighten(colour, 0.8f), 0.85f), null, new RoundedRect(cap, 2));
            }
        }
    }

    /// <summary>The line the bars land on, along the top of the keyboard.</summary>
    private void DrawHitLine(DrawingContext context, double width, double height)
    {
        context.FillRectangle(HitLine, new Rect(0, height - 1.5, width, 1.5));
    }

    private void DrawParticles(DrawingContext context)
    {
        for (var i = 0; i < _particleCount; i++)
        {
            ref var particle = ref _particles[i];

            // Fade out over the second half of the spark's life, and shrink as it goes.
            var remaining = 1 - particle.Age / particle.Life;
            var alpha = (float)Math.Clamp(remaining * 1.4, 0, 1);
            var size = particle.Size * (0.35 + 0.65 * remaining);

            var centre = new Point(particle.X, particle.Y);
            context.DrawEllipse(Brush(particle.Colour, alpha * 0.25f), null, centre, size * 2.4, size * 2.4);
            context.DrawEllipse(Brush(particle.Colour, alpha), null, centre, size, size);
        }
    }

    /// <summary>A faint line at every C, so it's clear where the octaves are as notes fall.</summary>
    private void DrawOctaveLines(DrawingContext context, double height)
    {
        if (Keyboard is null)
            return;

        for (var note = Keyboard.FirstNote; note <= Keyboard.LastNote; note++)
        {
            if (note % 12 != 0 || !Keyboard.TryGetKeyBounds(note, out var key))
                continue;

            context.FillRectangle(OctaveLine, new Rect(key.X, 0, 1, height));
        }
    }

    /// <summary>The moment the palette is turned to when a note isn't part of a song.</summary>
    private double When => Song is not null ? Position : _elapsed;

    /// <summary>
    /// The colour of a note: its pitch places it along the palette, and <paramref name="when"/>
    /// turns the whole palette round as the piece goes on, so a passage played now and the same
    /// passage a minute later come up in different colours. Black-key notes are a shade deeper,
    /// which keeps the two rows apart when the bars are all the same width.
    /// </summary>
    private Color ColourFor(int note, double when)
    {
        var first = Keyboard?.FirstNote ?? 21;
        var last = Keyboard?.LastNote ?? 108;
        var pitch = Math.Clamp((note - first) / (double)Math.Max(1, last - first), 0, 1);

        var steps = Registers.Length - 1;
        var place = pitch * KeyboardSpan + when / CycleSeconds * steps;

        // The palette is a loop, so the place in it wraps rather than clamping.
        place -= Math.Floor(place / steps) * steps;

        var index = Math.Min(steps - 1, (int)place);
        var colour = Blend(Registers[index], Registers[index + 1], (float)(place - index));

        return IsBlackKey[note % 12] ? Darken(colour, 0.22f) : colour;
    }

    private static Color Blend(Color from, Color to, float amount) => Color.FromRgb(
        (byte)(from.R + (to.R - from.R) * amount),
        (byte)(from.G + (to.G - from.G) * amount),
        (byte)(from.B + (to.B - from.B) * amount));

    private static Color Lighten(Color colour, float amount) => Blend(colour, Colors.White, amount);

    private static Color Darken(Color colour, float amount) => Blend(colour, Colors.Black, amount);

    /// <summary>A translucent brush, cached: the same few colours come round every frame.</summary>
    private IBrush Brush(Color colour, float alpha)
    {
        // Alpha is rounded to 32 steps, so the cache can't grow without bound.
        var step = (byte)(Math.Clamp(alpha, 0f, 1f) * 31) * 8;
        var key = ((uint)step << 24) | ((uint)colour.R << 16) | ((uint)colour.G << 8) | colour.B;

        if (!_brushes.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(colour, step / 248.0);
            _brushes[key] = brush;
        }
        return brush;
    }

    private IBrush Fill(Color colour) => Brush(colour, 1f);

    /// <summary>One spark thrown up from a key.</summary>
    private struct Particle
    {
        public double X;
        public double Y;
        public double VelocityX;
        public double VelocityY;
        public double Life;
        public double Age;
        public double Size;
        public Color Colour;
    }
}

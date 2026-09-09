using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Sonolume.UI;

/// <summary>
/// An on-screen piano keyboard for triggering zones/groups without a MIDI controller attached. Dragging across
/// keys with the button held glissandos between them (NoteOff for the old key, NoteOn for the new one), the same
/// way a physical keyboard's key travel would. <see cref="ShiftOctave"/> slides the visible two-octave window up
/// or down across the full MIDI range.
/// </summary>
public sealed class VirtualKeyboardControl : FrameworkElement
{
    private const int BaseLowNote = 48; // C3
    private const int BaseHighNote = 72; // C5
    private const int MinNote = 0;
    private const int MaxNote = 127;
    private const double BlackKeyWidthFactor = 0.55;
    private const double BlackKeyHeightFactor = 0.62;
    private const int TotalWhiteKeys = (BaseHighNote - BaseLowNote) / 12 * 7 + 1;

    private static readonly bool[] SemitoneIsBlack = { false, true, false, true, false, false, true, false, true, false, true, false };

    // Each black key is centered on the boundary between the two white keys it sits between (e.g. C# sits
    // squarely on the C/D seam), rather than the real-piano convention of leaning toward one side - clearer
    // at this control's small size, where a lean reads as "the black key belongs to one white key" instead of
    // "between two of them".
    private static readonly double[] SemitoneWhiteUnitOffset = { 0, 1, 1, 2, 2, 3, 4, 4, 5, 5, 6, 6 };
    private static readonly int[] WhiteIndexToSemitone = { 0, 2, 4, 5, 7, 9, 11 };

    private static readonly Pen KeyBorderPen = MakePen(Color.FromArgb(70, 0, 0, 0), 1);
    private static readonly Brush WhiteKeyBrush = MakeBrush(Color.FromRgb(0xEC, 0xEC, 0xF0));
    private static readonly Brush WhiteKeyPressedBrush = MakeBrush(Color.FromRgb(0xB0, 0xB0, 0xF0));
    private static readonly Brush BlackKeyBrush = MakeBrush(Color.FromRgb(0x20, 0x20, 0x24));
    private static readonly Brush BlackKeyPressedBrush = MakeBrush(Color.FromRgb(0x6C, 0x6C, 0xF5));

    private int octaveShift;
    private int? pressedNote;
    private int? hoveredNote;

    /// <summary>Raised when a key is pressed (mouse down on it, or dragged onto it while the button is held).</summary>
    public event Action<int>? NoteOn;

    /// <summary>Raised when a key is released (mouse up, dragged off it, or capture lost some other way).</summary>
    public event Action<int>? NoteOff;

    /// <summary>Raised whenever the key under the mouse changes, including to null when the cursor leaves the
    /// control or sits over the gap outside any key.</summary>
    public event Action<int?>? NoteHovered;

    /// <summary>Raised after <see cref="ShiftOctave"/> moves the visible range, so the host can refresh a range label.</summary>
    public event Action? RangeChanged;

    /// <summary>Lowest note currently visible (the leftmost key).</summary>
    public int LowNote => BaseLowNote + octaveShift * 12;

    /// <summary>Highest note currently visible (the rightmost key).</summary>
    public int HighNote => BaseHighNote + octaveShift * 12;

    public bool CanShiftDown => LowNote - 12 >= MinNote;

    public bool CanShiftUp => HighNote + 12 <= MaxNote;

    /// <summary>Slides the visible range by whole octaves; no-op if that would go past the 0-127 MIDI range.
    /// Releases any held note first, since the key under the mouse is about to represent a different note.</summary>
    public void ShiftOctave(int octaves)
    {
        int shifted = octaveShift + octaves;
        if (BaseLowNote + shifted * 12 < MinNote || BaseHighNote + shifted * 12 > MaxNote) return;

        if (pressedNote is { } note) Release(note);
        ReleaseMouseCapture();
        octaveShift = shifted;
        InvalidateVisual();
        RangeChanged?.Invoke();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? TotalWhiteKeys * 24 : availableSize.Width, 80);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double keyWidth = w / TotalWhiteKeys;
        int lowNote = LowNote, highNote = HighNote;

        int whiteIndex = 0;
        for (int note = lowNote; note <= highNote; note++)
        {
            if (SemitoneIsBlack[note % 12]) continue;
            var rect = new Rect(whiteIndex * keyWidth, 0, keyWidth, h);
            dc.DrawRectangle(note == pressedNote ? WhiteKeyPressedBrush : WhiteKeyBrush, KeyBorderPen, rect);
            whiteIndex++;
        }

        double blackWidth = keyWidth * BlackKeyWidthFactor;
        double blackHeight = h * BlackKeyHeightFactor;
        for (int note = lowNote; note <= highNote; note++)
        {
            int semitone = note % 12;
            if (!SemitoneIsBlack[semitone]) continue;
            double centerX = KeyUnitOffset(note, lowNote, semitone) * keyWidth;
            var rect = new Rect(centerX - blackWidth / 2, 0, blackWidth, blackHeight);
            dc.DrawRectangle(note == pressedNote ? BlackKeyPressedBrush : BlackKeyBrush, KeyBorderPen, rect);
        }
    }

    private static double KeyUnitOffset(int note, int lowNote, int semitone) => (note - lowNote) / 12 * 7 + SemitoneWhiteUnitOffset[semitone];

    private int? NoteAt(Point p)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return null;
        double keyWidth = w / TotalWhiteKeys;

        if (p.X < 0 || p.X >= w) return null;

        int lowNote = LowNote, highNote = HighNote;

        if (p.Y <= h * BlackKeyHeightFactor)
        {
            double blackWidth = keyWidth * BlackKeyWidthFactor;
            for (int note = lowNote; note <= highNote; note++)
            {
                int semitone = note % 12;
                if (!SemitoneIsBlack[semitone]) continue;
                double centerX = KeyUnitOffset(note, lowNote, semitone) * keyWidth;
                if (p.X >= centerX - blackWidth / 2 && p.X <= centerX + blackWidth / 2) return note;
            }
        }

        int whiteIndex = (int)(p.X / keyWidth);
        if (whiteIndex < 0 || whiteIndex >= TotalWhiteKeys) return null;
        return lowNote + whiteIndex / 7 * 12 + WhiteIndexToSemitone[whiteIndex % 7];
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var note = NoteAt(e.GetPosition(this));
        if (note is null) return;
        CaptureMouse();
        Press(note.Value);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var note = NoteAt(e.GetPosition(this));
        UpdateHover(note);

        if (pressedNote is null || e.LeftButton != MouseButtonState.Pressed) return;
        if (note is null || note == pressedNote) return;
        Release(pressedNote.Value);
        Press(note.Value);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        UpdateHover(null);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        // Release directly instead of relying solely on the OnLostMouseCapture below: CaptureMouse() in
        // OnMouseLeftButtonDown can silently fail to actually acquire capture (e.g. the very first click after
        // launch, while focus is still moving elsewhere), in which case ReleaseMouseCapture() here is a no-op
        // and OnLostMouseCapture never fires - leaving the note (and the zone it gates) stuck on forever.
        if (pressedNote is { } note) Release(note);
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (pressedNote is { } note) Release(note);
    }

    private void Press(int note)
    {
        pressedNote = note;
        InvalidateVisual();
        NoteOn?.Invoke(note);
    }

    private void Release(int note)
    {
        pressedNote = null;
        InvalidateVisual();
        NoteOff?.Invoke(note);
    }

    private void UpdateHover(int? note)
    {
        if (hoveredNote == note) return;
        hoveredNote = note;
        NoteHovered?.Invoke(note);
    }

    private static Pen MakePen(Color color, double thickness)
    {
        var pen = new Pen(MakeBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    private static Brush MakeBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

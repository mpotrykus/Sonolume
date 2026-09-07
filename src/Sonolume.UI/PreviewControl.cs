using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Sonolume.Engine.Render;

namespace Sonolume.UI;

/// <summary>Draws an engine snapshot: the same regions SignalRGB receives, so the preview matches the lights.</summary>
public sealed class PreviewControl : FrameworkElement
{
    private static readonly Pen OutlinePen = MakePen(Color.FromArgb(90, 255, 255, 255), 1);
    private static readonly Brush LabelBrush = MakeBrush(Color.FromArgb(170, 255, 255, 255));
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private EngineSnapshot? snapshot;

    public EngineSnapshot? Snapshot
    {
        get => snapshot;
        set
        {
            if (ReferenceEquals(snapshot, value)) return;
            snapshot = value;
            InvalidateVisual();
        }
    }

    public bool ShowLabels { get; set; } = true;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.DrawRoundedRectangle(Brushes.Black, null, new Rect(0, 0, w, h), 6, 6);
        var current = snapshot;
        if (current is null) return;

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        foreach (var zone in current.Zones)
        {
            var rect = new Rect(zone.Rect.X * w, zone.Rect.Y * h, zone.Rect.W * w, zone.Rect.H * h);
            double cellW = rect.Width / zone.CellsW;
            double cellH = rect.Height / zone.CellsH;

            for (int cy = 0; cy < zone.CellsH; cy++)
            {
                for (int cx = 0; cx < zone.CellsW; cx++)
                {
                    var c = zone.Cells[cy * zone.CellsW + cx];
                    if (c.R == 0 && c.G == 0 && c.B == 0) continue;
                    var brush = MakeBrush(Color.FromRgb(c.R, c.G, c.B));
                    dc.DrawRectangle(brush, null, new Rect(rect.X + cx * cellW, rect.Y + cy * cellH, cellW + 0.5, cellH + 0.5));
                }
            }

            dc.DrawRectangle(null, OutlinePen, rect);

            if (ShowLabels)
            {
                var text = new FormattedText(zone.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, 11, LabelBrush, pixelsPerDip);
                dc.DrawText(text, new Point(rect.X + 5, rect.Y + 3));
            }
        }
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

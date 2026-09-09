using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Sonolume.Engine.Core;

namespace Sonolume.UI;

/// <summary>
/// A saturation/brightness square plus a hue strip, standing in for separate Hue/Saturation/Brightness
/// sliders. Drag either area to change the color; <see cref="ColorChanged"/> fires continuously while dragging,
/// mirroring how the sliders it replaces report live values via ValueChanged.
/// </summary>
public sealed class ColorPickerControl : FrameworkElement
{
    private const double HueBarWidth = 18;
    private const double Gap = 8;
    private const double MaxSquareSize = 200;

    private static readonly Pen BorderPen = MakePen(Color.FromArgb(90, 255, 255, 255), 1);
    private static readonly Pen MarkerPen = MakePen(Color.FromArgb(230, 255, 255, 255), 2);
    private static readonly Brush HueBarBrush = MakeHueBarBrush();

    private enum DragRegion { None, SatVal, Hue }

    private float hue;
    private float saturation;
    private float brightness = 1f;
    private DragRegion dragging = DragRegion.None;

    /// <summary>Raised with (hue, saturation, brightness), each 0..1, while the user drags.</summary>
    public event Action<float, float, float>? ColorChanged;

    /// <summary>Sets the displayed color without raising <see cref="ColorChanged"/>.</summary>
    public void SetColor(float h, float s, float b)
    {
        hue = Clamp01(h);
        saturation = Clamp01(s);
        brightness = Clamp01(b);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? MaxSquareSize + Gap + HueBarWidth : availableSize.Width;
        double squareSize = Math.Clamp(w - Gap - HueBarWidth, 20, MaxSquareSize);
        return new Size(squareSize + Gap + HueBarWidth, squareSize);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double squareSize = ActualHeight;
        if (squareSize <= 0) return;

        var svRect = new Rect(0, 0, squareSize, squareSize);
        var hueRect = new Rect(squareSize + Gap, 0, HueBarWidth, squareSize);

        var hueColor = new Hsb(hue, 1f, 1f).ToRgb8().ToMediaColor();
        var satBrush = new LinearGradientBrush(Colors.White, hueColor, new Point(0, 0), new Point(1, 0));
        var valBrush = new LinearGradientBrush(Colors.Transparent, Colors.Black, new Point(0, 0), new Point(0, 1));
        dc.DrawRectangle(satBrush, null, svRect);
        dc.DrawRectangle(valBrush, null, svRect);
        dc.DrawRectangle(null, BorderPen, svRect);

        var marker = new Point(svRect.X + saturation * svRect.Width, svRect.Y + (1 - brightness) * svRect.Height);
        dc.DrawEllipse(null, MarkerPen, marker, 5, 5);

        dc.DrawRectangle(HueBarBrush, null, hueRect);
        dc.DrawRectangle(null, BorderPen, hueRect);
        double hueY = hueRect.Y + hue * hueRect.Height;
        dc.DrawRectangle(null, MarkerPen, new Rect(hueRect.X - 2, hueY - 2, hueRect.Width + 4, 4));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        double squareSize = ActualHeight;
        var pos = e.GetPosition(this);

        dragging = pos.X <= squareSize ? DragRegion.SatVal : DragRegion.Hue;
        UpdateFromPoint(pos);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging == DragRegion.None) return;
        UpdateFromPoint(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (dragging == DragRegion.None) return;
        dragging = DragRegion.None;
        ReleaseMouseCapture();
    }

    private void UpdateFromPoint(Point p)
    {
        double squareSize = ActualHeight;
        if (squareSize <= 0) return;

        if (dragging == DragRegion.SatVal)
        {
            saturation = Clamp01((float)(p.X / squareSize));
            brightness = Clamp01((float)(1 - p.Y / squareSize));
        }
        else if (dragging == DragRegion.Hue)
        {
            hue = Clamp01((float)(p.Y / squareSize));
        }

        InvalidateVisual();
        ColorChanged?.Invoke(hue, saturation, brightness);
    }

    private static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);

    private static Brush MakeHueBarBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        for (int i = 0; i <= 6; i++)
        {
            float h = i / 6f;
            brush.GradientStops.Add(new GradientStop(new Hsb(h, 1f, 1f).ToRgb8().ToMediaColor(), h));
        }
        brush.Freeze();
        return brush;
    }

    private static Pen MakePen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
}

internal static class Rgb8Extensions
{
    public static Color ToMediaColor(this Rgb8 c) => Color.FromRgb(c.R, c.G, c.B);
}

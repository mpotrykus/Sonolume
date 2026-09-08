using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Sonolume.Engine.Core;
using Sonolume.Engine.Render;

namespace Sonolume.UI;

/// <summary>
/// Draws an engine snapshot: the same regions SignalRGB receives, so the preview matches the lights.
/// When <see cref="Editable"/> is set, the selected zone can be dragged to move it and its handles dragged
/// to resize it, the same way a shape editor would; <see cref="ZoneRectCommitted"/> fires once per drag.
/// </summary>
public sealed class PreviewControl : FrameworkElement
{
    private const double HandleSize = 8;
    private const float MinZoneSize = 0.02f;
    private const double SnapPixels = 8;
    private const double ZoneCornerRadius = 4;

    private static readonly Pen OutlinePen = MakePen(Color.FromArgb(40, 255, 255, 255), 1);
    private static readonly Pen SelectedPen = MakePen(Color.FromArgb(230, 108, 108, 245), 2);
    private static readonly Pen HandlePen = MakePen(Color.FromArgb(230, 108, 108, 245), 1.5);
    private static readonly Brush HandleBrush = MakeBrush(Color.FromArgb(255, 30, 30, 36));
    private static readonly Brush LabelBrush = MakeBrush(Color.FromArgb(170, 255, 255, 255));
    private static readonly Brush KeyLabelBrush = MakeBrush(Color.FromArgb(90, 255, 255, 255));
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private enum DragMode { None, Move, ResizeTopLeft, ResizeTop, ResizeTopRight, ResizeRight, ResizeBottomRight, ResizeBottom, ResizeBottomLeft, ResizeLeft }

    private EngineSnapshot? snapshot;
    private string? selectedZoneId;
    private bool editable;

    private DragMode dragMode = DragMode.None;
    private string? draggingZoneId;
    private RectF dragStartRect;
    private Point dragStartPoint;
    private RectF? liveDragRect;

    public EngineSnapshot? Snapshot
    {
        get => snapshot;
        set
        {
            if (ReferenceEquals(snapshot, value)) return;
            snapshot = value;
            // A committed drag keeps overriding its zone's rect (see OnMouseLeftButtonUp) until a snapshot
            // reflecting the change actually arrives, so the canvas never flashes back to the pre-drag
            // position while the change is still propagating through the engine.
            if (dragMode == DragMode.None) { draggingZoneId = null; liveDragRect = null; }
            InvalidateVisual();
        }
    }

    public bool ShowLabels { get; set; } = true;

    /// <summary>When true, the selected zone can be dragged and resized on the canvas.</summary>
    public bool Editable
    {
        get => editable;
        set
        {
            if (editable == value) return;
            editable = value;
            InvalidateVisual();
        }
    }

    /// <summary>The zone whose outline is highlighted and whose resize handles are drawn.</summary>
    public string? SelectedZoneId
    {
        get => selectedZoneId;
        set
        {
            if (selectedZoneId == value) return;
            selectedZoneId = value;
            InvalidateVisual();
        }
    }

    /// <summary>Raised when the user clicks a zone that wasn't already selected.</summary>
    public event Action<string?>? ZoneClicked;

    /// <summary>Raised once, with the final normalized rect, when a move or resize drag ends.</summary>
    public event Action<string, RectF>? ZoneRectCommitted;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.DrawRoundedRectangle(Brushes.Black, null, new Rect(0, 0, w, h), 6, 6);
        var current = snapshot;
        if (current is null) return;

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var zoneKeyLabels = BuildZoneKeyLabels(current);

        // Painted in the same bottom-to-top stacking order the compositor blends in, so an overlapping zone's
        // outline/handles (and any zone drawn fully opaque) don't visually bury a higher-ZIndex zone underneath it.
        foreach (var zone in current.Zones.OrderBy(z => z.ZIndex))
        {
            var zoneRect = editable && zone.Id == draggingZoneId && liveDragRect is { } live ? live : zone.Rect;
            var rect = new Rect(zoneRect.X * w, zoneRect.Y * h, zoneRect.W * w, zoneRect.H * h);
            double cellW = rect.Width / zone.CellsW;
            double cellH = rect.Height / zone.CellsH;

            dc.PushClip(new RectangleGeometry(rect, ZoneCornerRadius, ZoneCornerRadius));
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
            dc.Pop();

            bool isSelected = editable && zone.Id == selectedZoneId;
            dc.DrawRoundedRectangle(null, isSelected ? SelectedPen : OutlinePen, rect, ZoneCornerRadius, ZoneCornerRadius);

            if (ShowLabels)
            {
                var text = new FormattedText(zone.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, 11, LabelBrush, pixelsPerDip);
                dc.DrawText(text, new Point(rect.X + 5, rect.Y + 3));

                if (zoneKeyLabels.TryGetValue(zone.Id, out var keyLabel))
                {
                    var keyText = new FormattedText(keyLabel, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, 11, KeyLabelBrush, pixelsPerDip);
                    dc.DrawText(keyText, new Point(rect.Right - 5 - keyText.Width, rect.Y + 3));
                }
            }

            if (isSelected) DrawHandles(dc, rect);
        }
    }

    /// <summary>Zone id -> the key(s)/controller(s) mapped to it (e.g. "Note 60"), for the top-right canvas label.</summary>
    private static Dictionary<string, string> BuildZoneKeyLabels(EngineSnapshot snapshot)
    {
        var labels = new Dictionary<string, string>();
        foreach (var m in snapshot.Mappings)
        {
            // Only the note-triggered "Key" mapping belongs on the canvas label - Set-mode macro mappings (see
            // DefaultMacros) and Select-mode keyswitch mappings (see SonolumeView.BuildEffectRow) aren't part of it.
            if (!m.Enabled || !m.Target.StartsWith("Zone ", StringComparison.Ordinal) || m.Mode is "Set" or "Select") continue;
            string zoneId = m.Target["Zone ".Length..];
            string source = FormatSource(m.Source);
            if (labels.TryGetValue(zoneId, out var existing))
            {
                if (!existing.Contains(source, StringComparison.Ordinal)) labels[zoneId] = $"{existing}, {source}";
            }
            else
            {
                labels[zoneId] = source;
            }
        }
        return labels;
    }

    // Trims the "ch*" (any-channel wildcard) suffix that SourceAddress.ToString() adds by default, since
    // it's noise for the canvas label where the channel is almost never pinned to something specific.
    private static string FormatSource(string source) =>
        source.EndsWith(" ch*", StringComparison.Ordinal) ? source[..^4] : source;

    private static void DrawHandles(DrawingContext dc, Rect r)
    {
        double radius = HandleSize / 2;
        foreach (var p in HandlePoints(r))
            dc.DrawEllipse(HandleBrush, HandlePen, p, radius, radius);
    }

    private static IEnumerable<Point> HandlePoints(Rect r)
    {
        yield return new Point(r.Left, r.Top);
        yield return new Point(r.Left + r.Width / 2, r.Top);
        yield return new Point(r.Right, r.Top);
        yield return new Point(r.Right, r.Top + r.Height / 2);
        yield return new Point(r.Right, r.Bottom);
        yield return new Point(r.Left + r.Width / 2, r.Bottom);
        yield return new Point(r.Left, r.Bottom);
        yield return new Point(r.Left, r.Top + r.Height / 2);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!editable || snapshot is null) return;
        var pos = e.GetPosition(this);

        if (selectedZoneId is { } selId)
        {
            var selectedZone = snapshot.Zones.FirstOrDefault(z => z.Id == selId);
            if (selectedZone is not null)
            {
                var mode = HitTestHandle(ToScreenRect(selectedZone.Rect), pos);
                if (mode != DragMode.None)
                {
                    StartDrag(selId, selectedZone.Rect, mode, pos);
                    CaptureMouse();
                    e.Handled = true;
                    return;
                }
            }
        }

        ZoneSnapshot? hit = null;
        for (int i = snapshot.Zones.Count - 1; i >= 0; i--)
        {
            if (ToScreenRect(snapshot.Zones[i].Rect).Contains(pos)) { hit = snapshot.Zones[i]; break; }
        }
        if (hit is null)
        {
            if (selectedZoneId is not null) ZoneClicked?.Invoke(null);
            return;
        }

        if (hit.Id != selectedZoneId) ZoneClicked?.Invoke(hit.Id);
        StartDrag(hit.Id, hit.Rect, DragMode.Move, pos);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!editable) return;

        if (dragMode == DragMode.None)
        {
            if (selectedZoneId is { } selId && snapshot is not null)
            {
                var zone = snapshot.Zones.FirstOrDefault(z => z.Id == selId);
                Cursor = zone is not null ? CursorFor(HitTestHandle(ToScreenRect(zone.Rect), e.GetPosition(this))) : Cursors.Arrow;
            }
            return;
        }

        var pos = e.GetPosition(this);
        double w = Math.Max(1, ActualWidth), h = Math.Max(1, ActualHeight);
        float dx = (float)((pos.X - dragStartPoint.X) / w);
        float dy = (float)((pos.Y - dragStartPoint.Y) / h);

        bool left = dragMode is DragMode.Move or DragMode.ResizeLeft or DragMode.ResizeTopLeft or DragMode.ResizeBottomLeft;
        bool right = dragMode is DragMode.Move or DragMode.ResizeRight or DragMode.ResizeTopRight or DragMode.ResizeBottomRight;
        bool top = dragMode is DragMode.Move or DragMode.ResizeTop or DragMode.ResizeTopLeft or DragMode.ResizeTopRight;
        bool bottom = dragMode is DragMode.Move or DragMode.ResizeBottom or DragMode.ResizeBottomLeft or DragMode.ResizeBottomRight;

        float edgeLeft = dragStartRect.X + (left ? dx : 0f);
        float edgeRight = dragStartRect.X + dragStartRect.W + (right ? dx : 0f);
        float edgeTop = dragStartRect.Y + (top ? dy : 0f);
        float edgeBottom = dragStartRect.Y + dragStartRect.H + (bottom ? dy : 0f);

        float tolX = (float)(SnapPixels / w), tolY = (float)(SnapPixels / h);
        var xCandidates = BuildSnapCandidates(horizontal: true);
        var yCandidates = BuildSnapCandidates(horizontal: false);

        if (dragMode == DragMode.Move)
        {
            float width = edgeRight - edgeLeft, height = edgeBottom - edgeTop;
            edgeLeft = SnapMoveAxis(edgeLeft, edgeRight, xCandidates, tolX);
            edgeRight = edgeLeft + width;
            edgeTop = SnapMoveAxis(edgeTop, edgeBottom, yCandidates, tolY);
            edgeBottom = edgeTop + height;
        }
        else
        {
            if (left) edgeLeft = Snap(edgeLeft, xCandidates, tolX);
            if (right) edgeRight = Snap(edgeRight, xCandidates, tolX);
            if (top) edgeTop = Snap(edgeTop, yCandidates, tolY);
            if (bottom) edgeBottom = Snap(edgeBottom, yCandidates, tolY);
        }

        float x = edgeLeft, y = edgeTop, rw = edgeRight - edgeLeft, rh = edgeBottom - edgeTop;
        liveDragRect = new RectF(Math.Clamp(x, 0f, 1f), Math.Clamp(y, 0f, 1f), Math.Clamp(rw, MinZoneSize, 1f), Math.Clamp(rh, MinZoneSize, 1f));
        InvalidateVisual();
    }

    /// <summary>Canvas edges (0, 1) plus every other zone's near/far edge on the given axis, for snapping.</summary>
    private List<float> BuildSnapCandidates(bool horizontal)
    {
        var candidates = new List<float> { 0f, 1f };
        if (snapshot is null) return candidates;
        foreach (var z in snapshot.Zones)
        {
            if (z.Id == draggingZoneId) continue;
            if (horizontal) { candidates.Add(z.Rect.X); candidates.Add(z.Rect.X + z.Rect.W); }
            else { candidates.Add(z.Rect.Y); candidates.Add(z.Rect.Y + z.Rect.H); }
        }
        return candidates;
    }

    private static float Snap(float value, List<float> candidates, float tolerance)
    {
        float best = value, bestDist = tolerance;
        foreach (var c in candidates)
        {
            float dist = Math.Abs(value - c);
            if (dist <= bestDist) { bestDist = dist; best = c; }
        }
        return best;
    }

    /// <summary>Snaps whichever of the two moving edges is closer to a candidate, shifting both by the same
    /// amount so a move drag translates the rect rigidly instead of resizing it.</summary>
    private static float SnapMoveAxis(float nearEdge, float farEdge, List<float> candidates, float tolerance)
    {
        float size = farEdge - nearEdge;
        float best = nearEdge, bestDist = tolerance;
        foreach (var c in candidates)
        {
            float nearDist = Math.Abs(nearEdge - c);
            if (nearDist <= bestDist) { bestDist = nearDist; best = c; }
            float farDist = Math.Abs(farEdge - c);
            if (farDist <= bestDist) { bestDist = farDist; best = c - size; }
        }
        return best;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (dragMode == DragMode.None || draggingZoneId is null) return;

        string id = draggingZoneId;
        var finalRect = liveDragRect ?? dragStartRect;
        bool changed = !Approximately(finalRect, dragStartRect);

        dragMode = DragMode.None;
        ReleaseMouseCapture();

        if (changed)
        {
            // Keep overriding this zone's rendered rect with the committed value (the Snapshot setter clears
            // it once a fresh snapshot arrives) instead of clearing it now, which would fall back to the
            // stale pre-drag snapshot for the one or two frames before the engine's update reaches this control.
            liveDragRect = finalRect;
            ZoneRectCommitted?.Invoke(id, finalRect);
        }
        else
        {
            draggingZoneId = null;
            liveDragRect = null;
            InvalidateVisual();
        }
    }

    private void StartDrag(string id, RectF rect, DragMode mode, Point pos)
    {
        draggingZoneId = id;
        dragMode = mode;
        dragStartRect = rect;
        dragStartPoint = pos;
        liveDragRect = rect;
    }

    private Rect ToScreenRect(RectF r) => new(r.X * ActualWidth, r.Y * ActualHeight, r.W * ActualWidth, r.H * ActualHeight);

    private static DragMode HitTestHandle(Rect r, Point p)
    {
        double half = HandleSize / 2 + 2;
        var modes = new[]
        {
            DragMode.ResizeTopLeft, DragMode.ResizeTop, DragMode.ResizeTopRight, DragMode.ResizeRight,
            DragMode.ResizeBottomRight, DragMode.ResizeBottom, DragMode.ResizeBottomLeft, DragMode.ResizeLeft,
        };
        int i = 0;
        foreach (var point in HandlePoints(r))
        {
            if (Math.Abs(p.X - point.X) <= half && Math.Abs(p.Y - point.Y) <= half) return modes[i];
            i++;
        }
        return DragMode.None;
    }

    private static Cursor CursorFor(DragMode mode) => mode switch
    {
        DragMode.ResizeTopLeft or DragMode.ResizeBottomRight => Cursors.SizeNWSE,
        DragMode.ResizeTopRight or DragMode.ResizeBottomLeft => Cursors.SizeNESW,
        DragMode.ResizeTop or DragMode.ResizeBottom => Cursors.SizeNS,
        DragMode.ResizeLeft or DragMode.ResizeRight => Cursors.SizeWE,
        _ => Cursors.Arrow,
    };

    private static bool Approximately(RectF a, RectF b) =>
        Math.Abs(a.X - b.X) < 0.0005f && Math.Abs(a.Y - b.Y) < 0.0005f && Math.Abs(a.W - b.W) < 0.0005f && Math.Abs(a.H - b.H) < 0.0005f;

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

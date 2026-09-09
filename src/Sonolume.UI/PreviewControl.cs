using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Sonolume.Engine.Core;
using Sonolume.Engine.Render;

namespace Sonolume.UI;

/// <summary>
/// Draws an engine snapshot: the same regions SignalRGB receives, so the preview matches the lights.
/// When <see cref="Editable"/> is set, the selected zone can be dragged to move it, its handles dragged to
/// resize it, and the handle above it dragged to rotate it, the same way a shape editor would;
/// <see cref="ZoneRectCommitted"/> and <see cref="ZoneRotationCommitted"/> each fire once per drag.
/// </summary>
public sealed class PreviewControl : FrameworkElement
{
    private const double HandleSize = 8;
    private const double RotateHandleOffset = 22;
    private const float MinZoneSize = 0.02f;
    private const double SnapPixels = 8;
    private const double ZoneCornerRadius = 4;
    private const float RotateSnapStep = 15f;
    private const float RotateSnapTolerance = 4f;

    private static readonly Pen OutlinePen = MakePen(Color.FromArgb(40, 255, 255, 255), 1);
    private static readonly Pen SelectedPen = MakePen(Color.FromArgb(230, 108, 108, 245), 2);
    private static readonly Pen HandlePen = MakePen(Color.FromArgb(230, 108, 108, 245), 1.5);
    private static readonly Brush HandleBrush = MakeBrush(Color.FromArgb(255, 30, 30, 36));
    private static readonly Brush LabelBrush = MakeBrush(Color.FromArgb(170, 255, 255, 255));
    private static readonly Brush KeyLabelBrush = MakeBrush(Color.FromArgb(90, 255, 255, 255));
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private enum DragMode { None, Move, ResizeTopLeft, ResizeTop, ResizeTopRight, ResizeRight, ResizeBottomRight, ResizeBottom, ResizeBottomLeft, ResizeLeft, Rotate }

    private EngineSnapshot? snapshot;
    private string? selectedZoneId;
    private bool editable;

    private DragMode dragMode = DragMode.None;
    private string? draggingZoneId;
    private float draggingZoneRotation;
    private RectF dragStartRect;
    private Point dragStartPoint;
    private RectF? liveDragRect;
    private float? liveDragRotation;

    public EngineSnapshot? Snapshot
    {
        get => snapshot;
        set
        {
            if (ReferenceEquals(snapshot, value)) return;
            snapshot = value;
            // A committed drag keeps overriding its zone's rect/rotation (see OnMouseLeftButtonUp) until a
            // snapshot reflecting the change actually arrives, so the canvas never flashes back to the
            // pre-drag position while the change is still propagating through the engine.
            if (dragMode == DragMode.None) { draggingZoneId = null; liveDragRect = null; liveDragRotation = null; }
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

    /// <summary>Raised once, with the final rotation in degrees, when a rotate-handle drag ends.</summary>
    public event Action<string, float>? ZoneRotationCommitted;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.DrawRoundedRectangle(Brushes.Black, null, new Rect(0, 0, w, h), 6, 6);
        var current = snapshot;
        if (current is null) return;

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var zoneKeyLabels = BuildZoneKeyLabels(current);

        // Cell colors, outlines, and labels never render past the canvas edge - rotating a zone whose rotated
        // corners fall outside [0,1] just clips those corners off, matching how the real SignalRGB canvas (a
        // plain HTML5 <canvas> element, which clips all drawing to its own bounds automatically) will show it.
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), 6, 6));

        // Painted in the same bottom-to-top stacking order the compositor blends in, so an overlapping zone's
        // outline (and any zone drawn fully opaque) don't visually bury a higher-ZIndex zone underneath it.
        foreach (var zone in current.Zones.OrderBy(z => z.ZIndex))
        {
            var rect = ScreenRectFor(zone, w, h);
            double cellW = rect.Width / zone.CellsW;
            double cellH = rect.Height / zone.CellsH;
            float rotation = RotationFor(zone);

            // The zone's own rectangle (and everything drawn relative to it below) rotates as a rigid whole
            // around its center - this is a layout/mounting transform, not a per-cell resample, so cell colors
            // are untouched; only where they land on the canvas changes.
            bool rotated = rotation % 360f != 0f;
            if (rotated) dc.PushTransform(new RotateTransform(rotation, rect.X + rect.Width / 2, rect.Y + rect.Height / 2));

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

            if (rotated) dc.Pop();
        }
        dc.Pop();

        // Handles are an editing affordance, not part of what SignalRGB actually shows, so - unlike everything
        // above - they stay visible past the canvas edge (e.g. the rotate handle above a zone pinned to the top).
        if (editable && selectedZoneId is { } selId)
        {
            var selectedZone = current.Zones.FirstOrDefault(z => z.Id == selId);
            if (selectedZone is not null)
            {
                var rect = ScreenRectFor(selectedZone, w, h);
                float rotation = RotationFor(selectedZone);
                bool rotated = rotation % 360f != 0f;
                if (rotated) dc.PushTransform(new RotateTransform(rotation, rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
                DrawHandles(dc, rect);
                if (rotated) dc.Pop();
            }
        }
    }

    private Rect ScreenRectFor(ZoneSnapshot zone, double w, double h)
    {
        var zoneRect = editable && zone.Id == draggingZoneId && liveDragRect is { } live ? live : zone.Rect;
        return new Rect(zoneRect.X * w, zoneRect.Y * h, zoneRect.W * w, zoneRect.H * h);
    }

    private float RotationFor(ZoneSnapshot zone) =>
        editable && zone.Id == draggingZoneId && liveDragRotation is { } liveRot ? liveRot : zone.Rotation;

    /// <summary>Zone id -> the key(s)/controller(s) mapped to it (e.g. "Note 60"), for the top-right canvas label.</summary>
    private static Dictionary<string, string> BuildZoneKeyLabels(EngineSnapshot snapshot)
    {
        var labels = new Dictionary<string, string>();
        foreach (var m in snapshot.Mappings)
        {
            // Only the note-triggered "Key" mapping belongs on the canvas label - Set-mode macro mappings (see
            // DefaultMacros) and Select-mode effect-type CC mappings (see SonolumeView.BuildEffectRow) aren't part of it.
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

        var topCenter = new Point(r.Left + r.Width / 2, r.Top);
        var rotateHandle = RotateHandlePoint(r);
        dc.DrawLine(HandlePen, topCenter, rotateHandle);
        dc.DrawEllipse(HandleBrush, HandlePen, rotateHandle, radius, radius);

        foreach (var p in HandlePoints(r))
            dc.DrawEllipse(HandleBrush, HandlePen, p, radius, radius);
    }

    private static Point RotateHandlePoint(Rect r) => new(r.Left + r.Width / 2, r.Top - RotateHandleOffset);

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
                var screenRect = ToScreenRect(selectedZone.Rect);
                var localPos = ToLocal(pos, screenRect, selectedZone.Rotation);
                var mode = HitTestHandle(screenRect, localPos);
                if (mode != DragMode.None)
                {
                    StartDrag(selId, selectedZone.Rect, selectedZone.Rotation, mode, localPos);
                    CaptureMouse();
                    e.Handled = true;
                    return;
                }
            }
        }

        ZoneSnapshot? hit = null;
        for (int i = snapshot.Zones.Count - 1; i >= 0; i--)
        {
            var z = snapshot.Zones[i];
            var screenRect = ToScreenRect(z.Rect);
            if (screenRect.Contains(ToLocal(pos, screenRect, z.Rotation))) { hit = z; break; }
        }
        if (hit is null)
        {
            if (selectedZoneId is not null) ZoneClicked?.Invoke(null);
            return;
        }

        if (hit.Id != selectedZoneId) ZoneClicked?.Invoke(hit.Id);
        // Move drags work in plain screen space, not the zone's local frame: Rect.X/Y live in the same
        // canvas-space coordinates regardless of rotation, so translating them needs no counter-rotation
        // (unlike a resize handle, which sits on the zone's rotated edge and must be read in its local frame).
        StartDrag(hit.Id, hit.Rect, hit.Rotation, DragMode.Move, pos);
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
                if (zone is not null)
                {
                    var screenRect = ToScreenRect(zone.Rect);
                    Cursor = CursorFor(HitTestHandle(screenRect, ToLocal(e.GetPosition(this), screenRect, zone.Rotation)));
                }
                else Cursor = Cursors.Arrow;
            }
            return;
        }

        if (dragMode == DragMode.Rotate)
        {
            var center = ToScreenRect(dragStartRect);
            var centerPoint = new Point(center.X + center.Width / 2, center.Y + center.Height / 2);
            var raw = e.GetPosition(this);
            double rx = raw.X - centerPoint.X, ry = raw.Y - centerPoint.Y;
            // 0 degrees points straight up (where the handle starts, above the top-center handle);
            // positive degrees sweep clockwise, matching RotateTransform's convention.
            float degrees = NormalizeDegrees((float)(Math.Atan2(rx, -ry) * 180.0 / Math.PI));
            liveDragRotation = SnapAngle(degrees);
            InvalidateVisual();
            return;
        }

        // A resize handle sits on the zone's rotated edge, so its drag must be read in the zone's own local
        // (unrotated) frame - localizing the live mouse point against a fixed drag-start center lets every
        // edge/snap/clamp computation below stay exactly as it is for an unrotated zone. A move drag, in
        // contrast, works in plain screen space (see the comment in OnMouseLeftButtonDown), so it skips this.
        var pos = dragMode == DragMode.Move
            ? e.GetPosition(this)
            : ToLocal(e.GetPosition(this), ToScreenRect(dragStartRect), draggingZoneRotation);
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
            edgeTop = SnapMoveAxis(edgeTop, edgeBottom, yCandidates, tolY);

            // Clamp position against the (unchanged) size so a move drag can't push either edge
            // past the canvas bounds - clamping X/Y alone would let the far edge escape instead.
            edgeLeft = Math.Clamp(edgeLeft, 0f, 1f - width);
            edgeTop = Math.Clamp(edgeTop, 0f, 1f - height);
            edgeRight = edgeLeft + width;
            edgeBottom = edgeTop + height;
        }
        else
        {
            if (left) edgeLeft = Snap(edgeLeft, xCandidates, tolX);
            if (right) edgeRight = Snap(edgeRight, xCandidates, tolX);
            if (top) edgeTop = Snap(edgeTop, yCandidates, tolY);
            if (bottom) edgeBottom = Snap(edgeBottom, yCandidates, tolY);

            edgeLeft = Math.Clamp(edgeLeft, 0f, 1f);
            edgeRight = Math.Clamp(edgeRight, 0f, 1f);
            edgeTop = Math.Clamp(edgeTop, 0f, 1f);
            edgeBottom = Math.Clamp(edgeBottom, 0f, 1f);
        }

        float x = edgeLeft, y = edgeTop, rw = edgeRight - edgeLeft, rh = edgeBottom - edgeTop;
        liveDragRect = new RectF(x, y, Math.Clamp(rw, MinZoneSize, 1f), Math.Clamp(rh, MinZoneSize, 1f));
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
        bool wasRotate = dragMode == DragMode.Rotate;
        dragMode = DragMode.None;
        ReleaseMouseCapture();

        if (wasRotate)
        {
            float finalRotation = liveDragRotation ?? draggingZoneRotation;
            bool rotationChanged = Math.Abs(finalRotation - draggingZoneRotation) > 0.05f;
            if (rotationChanged)
            {
                // Keep overriding this zone's rendered rotation with the committed value (the Snapshot setter
                // clears it once a fresh snapshot arrives) instead of clearing it now, which would flash back
                // to the stale pre-drag angle for the one or two frames before the engine's update lands.
                liveDragRotation = finalRotation;
                ZoneRotationCommitted?.Invoke(id, finalRotation);
            }
            else
            {
                draggingZoneId = null;
                liveDragRotation = null;
                InvalidateVisual();
            }
            return;
        }

        var finalRect = liveDragRect ?? dragStartRect;
        bool changed = !Approximately(finalRect, dragStartRect);

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

    private void StartDrag(string id, RectF rect, float rotation, DragMode mode, Point localPos)
    {
        draggingZoneId = id;
        draggingZoneRotation = rotation;
        dragMode = mode;
        dragStartRect = rect;
        dragStartPoint = localPos;
        liveDragRect = rect;
    }

    private Rect ToScreenRect(RectF r) => new(r.X * ActualWidth, r.Y * ActualHeight, r.W * ActualWidth, r.H * ActualHeight);

    /// <summary>Converts a point on screen into the zone's own unrotated frame (the inverse of the
    /// <see cref="RotateTransform"/> <see cref="OnRender"/> applies around <paramref name="screenRect"/>'s center),
    /// so hit-testing and dragging can keep comparing against the zone's plain axis-aligned rect.</summary>
    private static Point ToLocal(Point screenPoint, Rect screenRect, float rotationDeg)
    {
        if (rotationDeg % 360f == 0f) return screenPoint;
        var center = new Point(screenRect.X + screenRect.Width / 2, screenRect.Y + screenRect.Height / 2);
        return RotateAround(screenPoint, center, -rotationDeg);
    }

    /// <summary>Rotates <paramref name="p"/> by <paramref name="degrees"/> clockwise around <paramref name="center"/>,
    /// matching <see cref="RotateTransform"/>'s convention.</summary>
    private static Point RotateAround(Point p, Point center, double degrees)
    {
        double rad = degrees * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        double dx = p.X - center.X, dy = p.Y - center.Y;
        return new Point(center.X + dx * cos - dy * sin, center.Y + dx * sin + dy * cos);
    }

    private static float NormalizeDegrees(float deg)
    {
        deg %= 360f;
        return deg < 0f ? deg + 360f : deg;
    }

    /// <summary>Snaps to the nearest 15-degree increment when within a small tolerance, so square/diagonal
    /// mounting angles are easy to land on exactly while dragging.</summary>
    private static float SnapAngle(float deg)
    {
        float nearest = NormalizeDegrees(MathF.Round(deg / RotateSnapStep) * RotateSnapStep % 360f);
        float delta = Math.Abs(deg - nearest);
        delta = Math.Min(delta, 360f - delta);
        return delta < RotateSnapTolerance ? nearest : deg;
    }

    private static DragMode HitTestHandle(Rect r, Point p)
    {
        double half = HandleSize / 2 + 2;
        if (Distance(p, RotateHandlePoint(r)) <= half) return DragMode.Rotate;

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

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static Cursor CursorFor(DragMode mode) => mode switch
    {
        DragMode.ResizeTopLeft or DragMode.ResizeBottomRight => Cursors.SizeNWSE,
        DragMode.ResizeTopRight or DragMode.ResizeBottomLeft => Cursors.SizeNESW,
        DragMode.ResizeTop or DragMode.ResizeBottom => Cursors.SizeNS,
        DragMode.ResizeLeft or DragMode.ResizeRight => Cursors.SizeWE,
        DragMode.Rotate => Cursors.Hand,
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

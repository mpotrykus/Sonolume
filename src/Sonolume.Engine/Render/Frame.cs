using Sonolume.Engine.Core;

namespace Sonolume.Engine.Render;

/// <summary>The rendered cells of one zone. Cells are row-major, CellsW x CellsH.</summary>
public sealed record Region(int ZoneIndex, string ZoneId, RectF Rect, int CellsW, int CellsH, Rgb8[] Cells);

/// <summary>A set of changed regions (or every region when IsFull). What goes over the wire.</summary>
public sealed record Frame(ulong Seq, long TimestampTicks, IReadOnlyList<Region> Regions, bool IsFull);

public sealed record LayoutZone(int Index, string Id, string Name, RectF Rect, int CellsW, int CellsH, float Rotation);

/// <summary>Zone geometry. Sent rarely; state frames refer to zones by index into this list.</summary>
public sealed record Layout(string InstanceId, string ProjectName, IReadOnlyList<LayoutZone> Zones);

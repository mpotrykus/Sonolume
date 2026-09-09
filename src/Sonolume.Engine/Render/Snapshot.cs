using Sonolume.Engine.Core;

namespace Sonolume.Engine.Render;

public sealed record ZoneSnapshot(string Id, string Name, RectF Rect, int CellsW, int CellsH, Rgb8[] Cells, bool IsEventDriven, float DecaySeconds, int ZIndex, float Rotation);

public sealed record MappingSnapshot(string Id, string Source, string Target, string Param, string Mode, string? Effect, bool Enabled);

/// <summary>Immutable copy of engine state for UI threads. Produced on the engine thread, read anywhere.</summary>
public sealed record EngineSnapshot(
    string ProjectName,
    IReadOnlyList<ZoneSnapshot> Zones,
    IReadOnlyList<MappingSnapshot> Mappings,
    bool IsLearning,
    long TimestampTicks);

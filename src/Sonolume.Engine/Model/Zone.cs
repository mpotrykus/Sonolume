using Sonolume.Engine.Core;

namespace Sonolume.Engine.Model;

/// <summary>An arbitrary area of the Sonolume layout. Zones do not know about MIDI; mappings point at them.</summary>
public sealed class Zone
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public RectF Rect { get; set; } = new(0f, 0f, 1f, 1f);
    public int CellsW { get; set; } = 1;
    public int CellsH { get; set; } = 1;
    public bool InvertX { get; set; }
    public bool InvertY { get; set; }
    /// <summary>Clockwise rotation of the zone's rendered content, in degrees. Any value is allowed; angles that
    /// aren't a multiple of 90 are bilinear-sampled, and content rotated past the zone's edge is filled black.</summary>
    public float Rotation { get; set; }
    public string? GroupId { get; set; }
    /// <summary>Stacking order among overlapping zones; higher draws on top. Ties break by list order.</summary>
    public int ZIndex { get; set; }
    /// <summary>How this zone composites over lower-ZIndex zones wherever their rects overlap it.</summary>
    public BlendMode Blend { get; set; } = BlendMode.Screen;
    public ParamSet Params { get; set; } = new();

    public Zone Clone() => new()
    {
        Id = Id,
        Name = Name,
        Rect = Rect,
        CellsW = CellsW,
        CellsH = CellsH,
        InvertX = InvertX,
        InvertY = InvertY,
        Rotation = Rotation,
        GroupId = GroupId,
        ZIndex = ZIndex,
        Blend = Blend,
        Params = Params.Clone(),
    };
}

public sealed class Group
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ParentId { get; set; }
    public ParamSet Params { get; set; } = new();

    public Group Clone() => new()
    {
        Id = Id,
        Name = Name,
        ParentId = ParentId,
        Params = Params.Clone(),
    };
}

public sealed class Palette
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<Rgb8> Colors { get; set; } = new();
}

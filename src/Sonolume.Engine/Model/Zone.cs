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
    public string? GroupId { get; set; }
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
        GroupId = GroupId,
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

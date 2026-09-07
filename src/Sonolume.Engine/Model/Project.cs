using Sonolume.Engine.Core;
using Sonolume.Engine.Effects;
using Sonolume.Engine.Input;
using Sonolume.Engine.Mappings;

namespace Sonolume.Engine.Model;

/// <summary>A complete lighting configuration (preset): zones, groups, mappings, palettes.</summary>
public sealed class Project
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Name { get; set; } = "Untitled";
    public List<Zone> Zones { get; set; } = new();
    public List<Group> Groups { get; set; } = new();
    public List<Mapping> Mappings { get; set; } = new();
    public List<Palette> Palettes { get; set; } = new();

    public Zone? FindZone(string id) => Zones.Find(z => z.Id == id);

    public Group? FindGroup(string id) => Groups.Find(g => g.Id == id);

    /// <summary>The vertical-slice default: a drum kit of flash zones. C1 (note 36) is the kick.</summary>
    public static Project CreateDefault()
    {
        var project = new Project { Name = "Default Kit" };

        project.Groups.Add(new Group { Id = "drums", Name = "Drums" });

        project.Zones.Add(MakeZone("kick", "Kick", new RectF(0.04f, 0.56f, 0.28f, 0.38f), hue: 0.0f, groupId: "drums"));
        project.Zones.Add(MakeZone("snare", "Snare", new RectF(0.36f, 0.56f, 0.28f, 0.38f), hue: 0.62f, groupId: "drums"));
        project.Zones.Add(MakeZone("hihat", "Hi-Hat", new RectF(0.68f, 0.56f, 0.28f, 0.38f), hue: 0.15f, groupId: "drums"));

        project.Mappings.Add(FlashMapping("map-kick", 36, "kick"));
        project.Mappings.Add(FlashMapping("map-snare", 38, "snare"));
        project.Mappings.Add(FlashMapping("map-hihat", 42, "hihat"));

        return project;
    }

    private static Zone MakeZone(string id, string name, RectF rect, float hue, string? groupId)
    {
        var zone = new Zone { Id = id, Name = name, Rect = rect, GroupId = groupId };
        zone.Params[ParamId.Hue] = hue;
        zone.Params[ParamId.Saturation] = 1f;
        zone.Params[ParamId.Brightness] = 1f;
        zone.Params[ParamId.EffectDecay] = 0.25f;
        return zone;
    }

    private static Mapping FlashMapping(string id, int note, string zoneId) => new()
    {
        Id = id,
        Source = SourceAddress.Note(note),
        Target = TargetRef.Zone(zoneId),
        Param = ParamId.EffectIntensity,
        Mode = MappingMode.Trigger,
        EffectId = FlashEffect.TypeName,
        Transform = Transform.Identity,
    };
}

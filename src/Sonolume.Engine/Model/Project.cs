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

    public void AddZone(Zone zone)
    {
        Zones.Add(zone);
        try { ValidateIntegrity(); }
        catch { Zones.Remove(zone); throw; }
    }

    /// <summary>Removes a zone and any mappings that targeted it. Returns false if no zone had that id.</summary>
    public bool RemoveZone(string id)
    {
        int removed = Zones.RemoveAll(z => z.Id == id);
        if (removed == 0) return false;
        Mappings.RemoveAll(m => m.Target.Kind == TargetKind.Zone && m.Target.Id == id);
        return true;
    }

    /// <summary>Mutates a clone of the existing zone (so untouched fields like live Params survive) and swaps it in.</summary>
    public void UpdateZone(string id, Action<Zone> apply)
    {
        int index = Zones.FindIndex(z => z.Id == id);
        if (index < 0) throw new InvalidDataException($"Unknown zone '{id}'.");
        var updated = Zones[index].Clone();
        apply(updated);
        updated.Id = id;
        var previous = Zones[index];
        Zones[index] = updated;
        try { ValidateIntegrity(); }
        catch { Zones[index] = previous; throw; }
    }

    public void AddGroup(Group group)
    {
        Groups.Add(group);
        try { ValidateIntegrity(); }
        catch { Groups.Remove(group); throw; }
    }

    /// <summary>Removes a group, ungrouping its zones, reparenting its child groups to top-level, and removing
    /// mappings that targeted it. Returns false if no group had that id.</summary>
    public bool RemoveGroup(string id)
    {
        int removed = Groups.RemoveAll(g => g.Id == id);
        if (removed == 0) return false;
        foreach (var z in Zones) if (z.GroupId == id) z.GroupId = null;
        foreach (var g in Groups) if (g.ParentId == id) g.ParentId = null;
        Mappings.RemoveAll(m => m.Target.Kind == TargetKind.Group && m.Target.Id == id);
        return true;
    }

    /// <summary>Mutates a clone of the existing group (so untouched fields like live Params survive) and swaps it in.</summary>
    public void UpdateGroup(string id, Action<Group> apply)
    {
        int index = Groups.FindIndex(g => g.Id == id);
        if (index < 0) throw new InvalidDataException($"Unknown group '{id}'.");
        var updated = Groups[index].Clone();
        apply(updated);
        updated.Id = id;
        var previous = Groups[index];
        Groups[index] = updated;
        try { ValidateIntegrity(); }
        catch { Groups[index] = previous; throw; }
    }

    /// <summary>Checks id uniqueness, group/parent references, parent-chain cycles, and mapping targets.
    /// Shared by JSON deserialize and every structural CRUD mutation.</summary>
    public void ValidateIntegrity()
    {
        var zoneIds = new HashSet<string>();
        foreach (var z in Zones)
        {
            if (string.IsNullOrWhiteSpace(z.Id)) throw new InvalidDataException("A zone has no id.");
            if (!zoneIds.Add(z.Id)) throw new InvalidDataException($"Duplicate zone id '{z.Id}'.");
        }

        var groupIds = new HashSet<string>();
        foreach (var g in Groups)
        {
            if (string.IsNullOrWhiteSpace(g.Id)) throw new InvalidDataException("A group has no id.");
            if (!groupIds.Add(g.Id)) throw new InvalidDataException($"Duplicate group id '{g.Id}'.");
        }

        foreach (var z in Zones)
        {
            if (z.GroupId is not null && !groupIds.Contains(z.GroupId))
                throw new InvalidDataException($"Zone '{z.Id}' references unknown group '{z.GroupId}'.");
        }

        foreach (var g in Groups)
        {
            if (g.ParentId is not null && !groupIds.Contains(g.ParentId))
                throw new InvalidDataException($"Group '{g.Id}' references unknown parent group '{g.ParentId}'.");
        }

        foreach (var g in Groups) CheckNoParentCycle(g);

        foreach (var m in Mappings)
        {
            bool known = m.Target.Kind == TargetKind.Zone ? zoneIds.Contains(m.Target.Id) : groupIds.Contains(m.Target.Id);
            if (!known) throw new InvalidDataException($"Mapping '{m.Id}' targets unknown {m.Target}.");
        }
    }

    private void CheckNoParentCycle(Group group)
    {
        var seen = new HashSet<string> { group.Id };
        string? parentId = group.ParentId;
        while (parentId is not null)
        {
            if (!seen.Add(parentId)) throw new InvalidDataException($"Group '{group.Id}' has a cyclic parent chain.");
            parentId = FindGroup(parentId)?.ParentId;
        }
    }

    public static Project CreateEmpty(string name = "Untitled") => new() { Name = name };

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

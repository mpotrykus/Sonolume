using Sonolume.Engine.Core;
using Sonolume.Engine.Effects;
using Sonolume.Engine.Mappings;
using Sonolume.Engine.Model;

namespace Sonolume.Engine.Render;

/// <summary>Per-zone runtime state: group chain, running effect, current and last-sent cells.</summary>
internal sealed class ZoneRuntime
{
    public ZoneRuntime(Zone zone, int index, Group[] groupChain)
    {
        Zone = zone;
        Index = index;
        GroupChain = groupChain;
        int count = Math.Max(1, zone.CellsW) * Math.Max(1, zone.CellsH);
        Cells = new Rgb8[count];
        LastSent = new Rgb8[count];
        Array.Fill(LastSent, new Rgb8(1, 1, 1));
    }

    public Zone Zone { get; }
    public int Index { get; }
    /// <summary>Nearest group first.</summary>
    public Group[] GroupChain { get; }
    public IEffect? Effect { get; set; }
    public Rgb8[] Cells { get; }
    public Rgb8[] LastSent { get; }
    public bool Dirty { get; set; }
    /// <summary>True when a Trigger/Gate mapping targets this zone (or a group above it): idle is dark, effects light it up.</summary>
    public bool IsEventDriven { get; set; }

    public int CellsW => Math.Max(1, Zone.CellsW);
    public int CellsH => Math.Max(1, Zone.CellsH);

    public bool IsTargetedBy(TargetRef target)
    {
        if (target.Kind == TargetKind.Zone) return target.Id == Zone.Id;
        foreach (var g in GroupChain)
            if (g.Id == target.Id) return true;
        return false;
    }

    /// <summary>Group composition: brightness, saturation, intensity multiply; hue adds; active ANDs.</summary>
    public ResolvedParams Resolve()
    {
        var p = Zone.Params;
        float brightness = p[ParamId.Brightness];
        float hue = p[ParamId.Hue];
        float saturation = p[ParamId.Saturation];
        bool active = p[ParamId.Active] >= 0.5f;
        float intensity = p[ParamId.EffectIntensity];

        foreach (var g in GroupChain)
        {
            var gp = g.Params;
            brightness *= gp[ParamId.Brightness];
            hue += gp[ParamId.Hue];
            saturation *= gp[ParamId.Saturation];
            active &= gp[ParamId.Active] >= 0.5f;
            intensity *= gp[ParamId.EffectIntensity];
        }

        return new ResolvedParams(
            brightness,
            hue - MathF.Floor(hue),
            saturation,
            active,
            intensity,
            p[ParamId.EffectSpeed],
            p[ParamId.EffectDecay],
            p[ParamId.PosX],
            p[ParamId.PosY],
            p[ParamId.PaletteIndex]);
    }
}

/// <summary>Turns zones, groups, and running effects into cells, and tracks what changed since the last frame.</summary>
public sealed class Compositor
{
    private readonly Project project;
    private readonly EffectRegistry effects;
    private readonly ZoneRuntime[] zones;
    private readonly Dictionary<string, Group> groupsById;

    public Compositor(Project project, EffectRegistry effects)
    {
        this.project = project;
        this.effects = effects;
        groupsById = project.Groups.ToDictionary(g => g.Id);
        zones = new ZoneRuntime[project.Zones.Count];
        for (int i = 0; i < zones.Length; i++)
            zones[i] = new ZoneRuntime(project.Zones[i], i, BuildChain(project.Zones[i].GroupId));
        RefreshEventDriven(project.Mappings);
    }

    public int ZoneCount => zones.Length;

    internal IReadOnlyList<ZoneRuntime> Zones => zones;

    public void RefreshEventDriven(IEnumerable<Mapping> mappings)
    {
        var eventMappings = mappings.Where(m => m.Enabled && m.Mode != MappingMode.Set).ToArray();
        foreach (var z in zones)
        {
            bool driven = false;
            foreach (var m in eventMappings)
                if (z.IsTargetedBy(m.Target)) { driven = true; break; }
            z.IsEventDriven = driven;
        }
    }

    public void Trigger(TargetRef target, string effectId, in TriggerInfo info)
    {
        foreach (var z in zones)
        {
            if (!z.IsTargetedBy(target)) continue;
            if (z.Effect is null || !string.Equals(z.Effect.TypeId, effectId, StringComparison.OrdinalIgnoreCase))
                z.Effect = effects.Create(effectId);
            z.Effect.Trigger(info, z.Resolve());
            z.Dirty = true;
        }
    }

    public void Release(TargetRef target)
    {
        foreach (var z in zones)
            if (z.IsTargetedBy(target)) z.Effect?.Release();
    }

    public void SetParamNormalized(TargetRef target, ParamId id, float x01) =>
        SetParam(target, id, ParamInfos.Denormalize(id, x01));

    public void SetParam(TargetRef target, ParamId id, float raw)
    {
        if (target.Kind == TargetKind.Zone)
        {
            var zone = project.FindZone(target.Id);
            if (zone is not null) zone.Params[id] = raw;
        }
        else if (groupsById.TryGetValue(target.Id, out var group))
        {
            group.Params[id] = raw;
        }
    }

    /// <summary>Advances effects and re-renders every zone. Returns true when any zone differs from what was last collected.</summary>
    public bool Update(float dt)
    {
        bool anyDirty = false;
        foreach (var z in zones)
        {
            var p = z.Resolve();
            Span<Rgb8> cells = z.Cells;

            if (z.Effect is { IsActive: true } effect)
            {
                effect.Update(dt, p);
                if (effect.IsActive)
                    effect.Render(cells, z.CellsW, z.CellsH, p);
                else
                    RenderIdle(z, cells, p);
            }
            else
            {
                RenderIdle(z, cells, p);
            }

            z.Dirty = !cells.SequenceEqual(z.LastSent);
            anyDirty |= z.Dirty;
        }
        return anyDirty;
    }

    private static void RenderIdle(ZoneRuntime z, Span<Rgb8> cells, in ResolvedParams p) =>
        cells.Fill(z.IsEventDriven ? Rgb8.Black : p.ZoneColor);

    /// <summary>Returns changed regions (or all when full) and marks them as sent.</summary>
    public List<Region> Collect(bool full)
    {
        var regions = new List<Region>(full ? zones.Length : 4);
        foreach (var z in zones)
        {
            if (!full && !z.Dirty) continue;
            regions.Add(new Region(z.Index, z.Zone.Id, z.Zone.Rect, z.CellsW, z.CellsH, (Rgb8[])z.Cells.Clone()));
            z.Cells.CopyTo(z.LastSent, 0);
            z.Dirty = false;
        }
        return regions;
    }

    public ZoneSnapshot[] SnapshotZones()
    {
        var result = new ZoneSnapshot[zones.Length];
        for (int i = 0; i < zones.Length; i++)
        {
            var z = zones[i];
            result[i] = new ZoneSnapshot(z.Zone.Id, z.Zone.Name, z.Zone.Rect, z.CellsW, z.CellsH, (Rgb8[])z.Cells.Clone(), z.IsEventDriven, z.Zone.Params[ParamId.EffectDecay]);
        }
        return result;
    }

    private Group[] BuildChain(string? groupId)
    {
        var chain = new List<Group>();
        var seen = new HashSet<string>();
        while (groupId is not null && seen.Add(groupId) && groupsById.TryGetValue(groupId, out var g))
        {
            chain.Add(g);
            groupId = g.ParentId;
        }
        return chain.ToArray();
    }
}

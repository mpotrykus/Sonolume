using System.Linq;
using Sonolume.Engine.Core;
using Sonolume.Engine.Effects;
using Sonolume.Engine.Mappings;
using Sonolume.Engine.Model;

namespace Sonolume.Engine.Render;

/// <summary>Per-zone runtime state: group chain, running effect instance(s), current and last-sent cells.</summary>
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
        Scratch = new Rgb8[count];
        Array.Fill(LastSent, new Rgb8(1, 1, 1));
    }

    public Zone Zone { get; }
    public int Index { get; }
    /// <summary>Nearest group first.</summary>
    public Group[] GroupChain { get; }
    /// <summary>Every currently-running effect instance for this zone. Usually at most one; an effect that opts
    /// into <see cref="IEffect.AllowsOverlap"/> (Pulse, Ripple) gets a fresh instance per trigger instead of being
    /// reset, so repeated hits layer additively (see <see cref="Compositor.Update"/>) rather than cancel each other.</summary>
    public List<IEffect> Effects { get; } = new();
    public Rgb8[] Cells { get; }
    public Rgb8[] LastSent { get; }
    /// <summary>Reused to render each overlapping effect instance beyond the first, so instances don't overwrite
    /// each other's output in <see cref="Cells"/> before they can be blended together.</summary>
    public Rgb8[] Scratch { get; }
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
    public ResolvedParams Resolve(float beatsPerSecond = 0f, double songBeats = 0.0, bool tempoSynced = false)
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
            p[ParamId.PaletteIndex],
            p[ParamId.EffectRotation],
            beatsPerSecond,
            songBeats,
            tempoSynced);
    }

    /// <summary>Starts <paramref name="effectId"/>: reuses (resets) an existing instance of the same type unless
    /// it allows overlap, in which case a fresh instance is layered on top of whatever's already running.</summary>
    public void Trigger(EffectRegistry registry, string effectId, in TriggerInfo info, in ResolvedParams p)
    {
        var existing = Effects.FirstOrDefault(e => string.Equals(e.TypeId, effectId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !existing.AllowsOverlap)
        {
            existing.Trigger(info, p);
            return;
        }

        var created = registry.Create(effectId);
        created.Trigger(info, p);
        Effects.Add(created);
    }

    public void Release()
    {
        foreach (var e in Effects) e.Release();
    }
}

/// <summary>Turns zones, groups, and running effects into cells, and tracks what changed since the last frame.</summary>
public sealed class Compositor
{
    private readonly Project project;
    private readonly EffectRegistry effects;
    private readonly ZoneRuntime[] zones;
    private readonly Dictionary<string, Group> groupsById;
    /// <summary>Elapsed beats since the host started transport; frozen while stopped, unused when free-running.
    /// Effects read this (not their own elapsed time) so tempo-synced zones stay in phase with each other.</summary>
    private double songBeats;

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
        var eventMappings = mappings.Where(m => m.Enabled && m.Mode is MappingMode.Trigger or MappingMode.Gate).ToArray();
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
            z.Trigger(effects, effectId, info, z.Resolve());
            z.Dirty = true;
        }
    }

    public void Release(TargetRef target)
    {
        foreach (var z in zones)
            if (z.IsTargetedBy(target)) z.Release();
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

    /// <summary>Sets a zone's blend mode directly (there is no Group.Blend); read live by <see cref="ApplyBlending"/>
    /// every <see cref="Update"/>, so no dirty-marking is needed here.</summary>
    public void SetBlend(TargetRef target, BlendMode mode)
    {
        if (target.Kind != TargetKind.Zone) return;
        var zone = project.FindZone(target.Id);
        if (zone is not null) zone.Blend = mode;
    }

    /// <summary>Advances effects and re-renders every zone. Returns true when any zone differs from what was last collected.
    /// <paramref name="bpm"/> is the host's tempo (0 when there is none - the standalone app always runs free-running);
    /// <paramref name="isPlaying"/> gates whether <see cref="songBeats"/> advances, so tempo-locked effects freeze
    /// with the timeline instead of drifting while the host is stopped.</summary>
    public bool Update(float dt, double bpm = 0.0, bool isPlaying = false)
    {
        bool tempoSynced = bpm > 0.0;
        float beatsPerSecond = tempoSynced ? (float)(bpm / 60.0) : 0f;
        if (tempoSynced && isPlaying) songBeats += dt * beatsPerSecond;

        foreach (var z in zones)
        {
            var p = z.Resolve(beatsPerSecond, songBeats, tempoSynced);
            Span<Rgb8> cells = z.Cells;

            for (int i = z.Effects.Count - 1; i >= 0; i--)
            {
                z.Effects[i].Update(dt, p);
                if (!z.Effects[i].IsActive) z.Effects.RemoveAt(i);
            }

            if (z.Effects.Count == 0)
            {
                RenderIdle(z, cells, p);
            }
            else
            {
                z.Effects[0].Render(cells, z.CellsW, z.CellsH, p);
                for (int i = 1; i < z.Effects.Count; i++)
                {
                    Span<Rgb8> scratch = z.Scratch;
                    z.Effects[i].Render(scratch, z.CellsW, z.CellsH, p);
                    for (int c = 0; c < cells.Length; c++)
                        cells[c] = Rgb8.Blend(BlendMode.Additive, scratch[c], cells[c]);
                }
            }

            ApplyInvert(z);
        }

        ApplyBlending();

        bool anyDirty = false;
        foreach (var z in zones)
        {
            z.Dirty = !z.Cells.AsSpan().SequenceEqual(z.LastSent);
            anyDirty |= z.Dirty;
        }
        return anyDirty;
    }

    private static void RenderIdle(ZoneRuntime z, Span<Rgb8> cells, in ResolvedParams p) =>
        cells.Fill(z.IsEventDriven ? Rgb8.Black : p.ZoneColor);

    /// <summary>Mirrors the zone's just-rendered cells per <see cref="Zone.InvertX"/>/<see cref="Zone.InvertY"/>, so
    /// an effect drawn left-to-right or top-to-bottom runs the other way without every effect needing to know about
    /// zone orientation.</summary>
    private static void ApplyInvert(ZoneRuntime z)
    {
        var zone = z.Zone;
        if (!zone.InvertX && !zone.InvertY) return;

        int w = z.CellsW, h = z.CellsH;
        Span<Rgb8> cells = z.Cells;
        Span<Rgb8> scratch = z.Scratch;
        cells.CopyTo(scratch);

        for (int y = 0; y < h; y++)
        {
            int sy = zone.InvertY ? h - 1 - y : y;
            for (int x = 0; x < w; x++)
            {
                int sx = zone.InvertX ? w - 1 - x : x;
                cells[y * w + x] = scratch[sy * w + sx];
            }
        }
    }

    /// <summary>Blends each zone's own cells over any lower-ZIndex zones whose rect overlaps it, using its own
    /// <see cref="BlendMode"/>. Weighted by actual geometric overlap per cell, so a zone that only partly overlaps
    /// another only has its overlapping cells (or fraction of a cell) affected - the rest of the zone stays exactly
    /// its own raw color regardless of blend mode. Zones left at the default Normal mode are untouched (they simply
    /// occlude whatever is below them, matching pre-blending behavior), so this is a no-op unless a non-Normal blend
    /// is configured.</summary>
    private void ApplyBlending()
    {
        bool anyBlend = false;
        foreach (var z in zones)
        {
            if (z.Zone.Blend != BlendMode.Normal) { anyBlend = true; break; }
        }
        if (!anyBlend) return;

        var order = zones.OrderBy(z => z.Zone.ZIndex).ToArray();
        for (int oi = 0; oi < order.Length; oi++)
        {
            var z = order[oi];
            if (z.Zone.Blend == BlendMode.Normal) continue;

            var rect = z.Zone.Rect;
            if (rect.W <= 0 || rect.H <= 0) continue;
            float cellW = rect.W / z.CellsW;
            float cellH = rect.H / z.CellsH;
            float cellArea = cellW * cellH;
            if (cellArea <= 0f) continue;

            for (int cy = 0; cy < z.CellsH; cy++)
            {
                for (int cx = 0; cx < z.CellsW; cx++)
                {
                    var cellRect = new RectF(rect.X + cx * cellW, rect.Y + cy * cellH, cellW, cellH);

                    Rgb8 backdrop = Rgb8.Black;
                    float coverage = 0f;
                    for (int oj = 0; oj < oi; oj++)
                    {
                        var below = order[oj];
                        var overlap = Intersect(cellRect, below.Zone.Rect);
                        if (overlap.W <= 0f || overlap.H <= 0f) continue;

                        float covW = Math.Clamp(overlap.W * overlap.H / cellArea, 0f, 1f);
                        Rgb8 belowColor = AverageColor(below, overlap);
                        Rgb8 combined = Rgb8.Blend(below.Zone.Blend, belowColor, backdrop);
                        backdrop = Rgb8.Lerp(backdrop, combined, covW);
                        coverage = Math.Min(1f, coverage + covW);
                    }

                    if (coverage <= 0f) continue;

                    int idx = cy * z.CellsW + cx;
                    Rgb8 raw = z.Cells[idx];
                    Rgb8 blended = Rgb8.Blend(z.Zone.Blend, raw, backdrop);
                    z.Cells[idx] = Rgb8.Lerp(raw, blended, coverage);
                }
            }
        }
    }

    private static RectF Intersect(RectF a, RectF b)
    {
        float x0 = Math.Max(a.X, b.X), y0 = Math.Max(a.Y, b.Y);
        float x1 = Math.Min(a.X + a.W, b.X + b.W), y1 = Math.Min(a.Y + a.H, b.Y + b.H);
        return new RectF(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>The area-weighted average color of <paramref name="z"/>'s cells that fall within <paramref name="region"/>.</summary>
    private static Rgb8 AverageColor(ZoneRuntime z, RectF region)
    {
        var r = z.Zone.Rect;
        float cellW = r.W / z.CellsW;
        float cellH = r.H / z.CellsH;
        if (cellW <= 0f || cellH <= 0f) return Rgb8.Black;

        float totalArea = 0f, sumR = 0f, sumG = 0f, sumB = 0f;
        for (int cy = 0; cy < z.CellsH; cy++)
        {
            for (int cx = 0; cx < z.CellsW; cx++)
            {
                var cellRect = new RectF(r.X + cx * cellW, r.Y + cy * cellH, cellW, cellH);
                var overlap = Intersect(cellRect, region);
                if (overlap.W <= 0f || overlap.H <= 0f) continue;

                float area = overlap.W * overlap.H;
                var c = z.Cells[cy * z.CellsW + cx];
                sumR += c.R * area;
                sumG += c.G * area;
                sumB += c.B * area;
                totalArea += area;
            }
        }
        if (totalArea <= 0f) return Rgb8.Black;
        return new Rgb8((byte)MathF.Round(sumR / totalArea), (byte)MathF.Round(sumG / totalArea), (byte)MathF.Round(sumB / totalArea));
    }

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
            result[i] = new ZoneSnapshot(z.Zone.Id, z.Zone.Name, z.Zone.Rect, z.CellsW, z.CellsH, (Rgb8[])z.Cells.Clone(), z.IsEventDriven, z.Zone.Params[ParamId.EffectDecay], z.Zone.ZIndex, z.Zone.Rotation);
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

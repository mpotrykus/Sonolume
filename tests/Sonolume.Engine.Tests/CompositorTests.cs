using System.Linq;
using Sonolume.Engine.Core;
using Sonolume.Engine.Effects;
using Sonolume.Engine.Input;
using Sonolume.Engine.Mappings;
using Sonolume.Engine.Model;
using Sonolume.Engine.Render;
using Xunit;

namespace Sonolume.Engine.Tests;

public class CompositorTests
{
    private static Zone MakeZone(string id, RectF rect, int zIndex, BlendMode blend, float brightness)
    {
        var zone = new Zone { Id = id, Name = id, Rect = rect, CellsW = 1, CellsH = 1, ZIndex = zIndex, Blend = blend };
        zone.Params[ParamId.Saturation] = 0f;
        zone.Params[ParamId.Brightness] = brightness;
        return zone;
    }

    private static Rgb8 Color(Frame frame, string zoneId) => frame.Regions.Single(r => r.ZoneId == zoneId).Cells[0];

    private static Zone MakeFlashZone(string id, RectF rect, int zIndex, BlendMode blend)
    {
        var zone = new Zone { Id = id, Name = id, Rect = rect, CellsW = 1, CellsH = 1, ZIndex = zIndex, Blend = blend };
        zone.Params[ParamId.Hue] = 0f;
        zone.Params[ParamId.Saturation] = 1f;
        zone.Params[ParamId.Brightness] = 1f;
        zone.Params[ParamId.EffectDecay] = 5f;
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

    [Fact]
    public void AdditiveBlend_CombinesActivelyFlashingOverlappingZones()
    {
        var project = Project.CreateEmpty();
        var fullRect = new RectF(0f, 0f, 1f, 1f);
        project.Zones.Add(MakeFlashZone("bottom", fullRect, zIndex: 0, BlendMode.Normal));
        project.Zones.Add(MakeFlashZone("top", fullRect, zIndex: 1, BlendMode.Additive));
        project.Mappings.Add(FlashMapping("m1", 36, "bottom"));
        project.Mappings.Add(FlashMapping("m2", 37, "top"));

        var engine = new Engine(project, instanceId: "test0001");
        engine.Push(MidiEvent.NoteOn(0, 36, 0.4f, 0));
        engine.Push(MidiEvent.NoteOn(0, 37, 0.3f, 0));
        engine.Tick(0f);
        var frame = engine.TakeFrame(full: true)!;

        Assert.Equal(new Rgb8(102, 0, 0), Color(frame, "bottom"));
        Assert.Equal(new Rgb8(179, 0, 0), Color(frame, "top"));
    }

    [Fact]
    public void Multiply_WithNothingBelow_LeavesZoneUntouched()
    {
        var project = Project.CreateEmpty();
        project.Zones.Add(MakeZone("solo", new RectF(0f, 0f, 1f, 1f), zIndex: 0, BlendMode.Multiply, brightness: 0.4f));

        var engine = new Engine(project, instanceId: "test0001");
        engine.Tick(0f);
        var frame = engine.TakeFrame(full: true)!;

        Assert.Equal(new Rgb8(102, 102, 102), Color(frame, "solo"));
    }

    [Fact]
    public void PartialOverlap_OnlyBlendsTheOverlappingFraction()
    {
        var project = Project.CreateEmpty();
        project.Zones.Add(MakeZone("bottom", new RectF(0.5f, 0f, 0.5f, 1f), zIndex: 0, BlendMode.Normal, brightness: 0.4f));
        project.Zones.Add(MakeZone("top", new RectF(0f, 0f, 1f, 1f), zIndex: 1, BlendMode.Additive, brightness: 0.2f));

        var engine = new Engine(project, instanceId: "test0001");
        engine.Tick(0f);
        var frame = engine.TakeFrame(full: true)!;

        // "top" is a single 1x1 cell spanning the whole width, half of which overlaps "bottom" (0.5..1.0).
        // The overlapping half should blend additively; averaged across the whole cell, that's a half-strength lift.
        var top = Color(frame, "top");
        Assert.True(top.R > 51 && top.R < 102, $"expected a partial lift above the raw 51, got {top.R}");
    }

    [Fact]
    public void NormalBlend_IgnoresZonesBelow()
    {
        var project = Project.CreateEmpty();
        var fullRect = new RectF(0f, 0f, 1f, 1f);
        project.Zones.Add(MakeZone("bottom", fullRect, zIndex: 0, BlendMode.Normal, brightness: 0.2f));
        project.Zones.Add(MakeZone("top", fullRect, zIndex: 1, BlendMode.Normal, brightness: 0.4f));

        var engine = new Engine(project, instanceId: "test0001");
        engine.Tick(0f);
        var frame = engine.TakeFrame(full: true)!;

        Assert.Equal(new Rgb8(51, 51, 51), Color(frame, "bottom"));
        Assert.Equal(new Rgb8(102, 102, 102), Color(frame, "top"));
    }

    [Fact]
    public void AdditiveBlend_CombinesWithOverlappingZoneBelow()
    {
        var project = Project.CreateEmpty();
        var fullRect = new RectF(0f, 0f, 1f, 1f);
        project.Zones.Add(MakeZone("bottom", fullRect, zIndex: 0, BlendMode.Normal, brightness: 0.2f));
        project.Zones.Add(MakeZone("top", fullRect, zIndex: 1, BlendMode.Additive, brightness: 0.4f));

        var engine = new Engine(project, instanceId: "test0001");
        engine.Tick(0f);
        var frame = engine.TakeFrame(full: true)!;

        Assert.Equal(new Rgb8(51, 51, 51), Color(frame, "bottom"));
        Assert.Equal(new Rgb8(153, 153, 153), Color(frame, "top"));
    }

    [Fact]
    public void AdditiveBlend_UnaffectedByNonOverlappingZoneBelow()
    {
        var project = Project.CreateEmpty();
        project.Zones.Add(MakeZone("bottom", new RectF(0f, 0f, 0.5f, 1f), zIndex: 0, BlendMode.Normal, brightness: 0.2f));
        project.Zones.Add(MakeZone("top", new RectF(0.5f, 0f, 0.5f, 1f), zIndex: 1, BlendMode.Additive, brightness: 0.4f));

        var engine = new Engine(project, instanceId: "test0001");
        engine.Tick(0f);
        var frame = engine.TakeFrame(full: true)!;

        Assert.Equal(new Rgb8(51, 51, 51), Color(frame, "bottom"));
        Assert.Equal(new Rgb8(102, 102, 102), Color(frame, "top"));
    }
}

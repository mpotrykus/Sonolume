using Sonolume.Engine.Core;
using Sonolume.Engine.Input;
using Sonolume.Engine.Mappings;
using Sonolume.Engine.Model;
using Xunit;

namespace Sonolume.Engine.Tests;

public class EngineTests
{
    private const float Dt = 1f / 120f;

    private static Engine NewEngine() => new(Project.CreateDefault(), instanceId: "test0001");

    private static Rgb8 KickColor(Engine engine)
    {
        var frame = engine.TakeFrame(full: true)!;
        return frame.Regions.Single(r => r.ZoneId == "kick").Cells[0];
    }

    [Fact]
    public void FirstFullFrame_IsBlackForEventDrivenZones()
    {
        var engine = NewEngine();
        engine.Tick(0f);
        var frame = engine.TakeFrame(full: true);
        Assert.NotNull(frame);
        Assert.Equal(3, frame!.Regions.Count);
        Assert.All(frame.Regions, r => Assert.Equal(Rgb8.Black, r.Cells[0]));
    }

    [Fact]
    public void NoteOn_FlashesKickRedAtVelocity()
    {
        var engine = NewEngine();
        engine.Tick(0f);
        engine.TakeFrame(full: true);

        engine.Push(MidiEvent.NoteOn(0, 36, 1f, 0));
        engine.Tick(0f);
        var frame = engine.TakeFrame();

        Assert.NotNull(frame);
        var region = Assert.Single(frame!.Regions);
        Assert.Equal("kick", region.ZoneId);
        Assert.Equal(new Rgb8(255, 0, 0), region.Cells[0]);
    }

    [Fact]
    public void Velocity_ScalesBrightness()
    {
        var engine = NewEngine();
        engine.Push(MidiEvent.NoteOn(0, 36, 0.5f, 0));
        engine.Tick(0f);
        var c = KickColor(engine);
        Assert.InRange(c.R, 126, 129);
        Assert.Equal(0, c.G);
        Assert.Equal(0, c.B);
    }

    [Fact]
    public void Flash_DecaysAndThenStopsProducingFrames()
    {
        var engine = NewEngine();
        engine.Tick(0f);
        engine.TakeFrame(full: true);

        engine.Push(MidiEvent.NoteOn(0, 36, 1f, 0));
        engine.Tick(0f);
        engine.TakeFrame();

        for (int i = 0; i < 30; i++) engine.Tick(Dt);
        var atDecay = KickColor(engine);
        Assert.InRange(atDecay.R, 10, 16);

        int framesWhileDecaying = 0;
        for (int i = 0; i < 240; i++)
        {
            engine.Tick(Dt);
            if (engine.TakeFrame() is not null) framesWhileDecaying++;
        }
        Assert.True(framesWhileDecaying > 0);

        for (int i = 0; i < 120; i++)
        {
            engine.Tick(Dt);
            Assert.Null(engine.TakeFrame());
        }
        Assert.Equal(Rgb8.Black, KickColor(engine));
    }

    [Fact]
    public void NoChange_ProducesNoFrame()
    {
        var engine = NewEngine();
        engine.Tick(0f);
        engine.TakeFrame(full: true);
        for (int i = 0; i < 10; i++)
        {
            engine.Tick(Dt);
            Assert.Null(engine.TakeFrame());
        }
    }

    [Fact]
    public void GroupBrightness_MultipliesZoneOutput()
    {
        var engine = NewEngine();
        engine.SetParam(TargetRef.Group("drums"), ParamId.Brightness, 0.5f);
        engine.Push(MidiEvent.NoteOn(0, 36, 1f, 0));
        engine.Tick(0f);
        var c = KickColor(engine);
        Assert.InRange(c.R, 126, 129);
    }

    [Fact]
    public void SetMapping_ChangesPersistentParameter()
    {
        var engine = NewEngine();
        engine.AddMapping(new Mapping
        {
            Id = "cc7-drums",
            Source = SourceAddress.CC(7),
            Target = TargetRef.Group("drums"),
            Param = ParamId.Brightness,
            Mode = MappingMode.Set,
        });

        engine.Push(MidiEvent.ControlChange(0, 7, 0, 0));
        engine.Push(MidiEvent.NoteOn(0, 36, 1f, 0));
        engine.Tick(0f);
        Assert.Equal(Rgb8.Black, KickColor(engine));

        engine.Push(MidiEvent.ControlChange(0, 7, 127, 0));
        engine.Push(MidiEvent.NoteOn(0, 36, 1f, 0));
        engine.Tick(0f);
        Assert.Equal(new Rgb8(255, 0, 0), KickColor(engine));
    }

    [Fact]
    public void GroupTrigger_FlashesEveryZoneInGroup()
    {
        var engine = NewEngine();
        engine.AddMapping(new Mapping
        {
            Id = "note60-drums",
            Source = SourceAddress.Note(60),
            Target = TargetRef.Group("drums"),
            Mode = MappingMode.Trigger,
            EffectId = "flash",
        });
        engine.Tick(0f);
        engine.TakeFrame(full: true);

        engine.Push(MidiEvent.NoteOn(0, 60, 1f, 0));
        engine.Tick(0f);
        var frame = engine.TakeFrame();
        Assert.Equal(3, frame!.Regions.Count);
        Assert.All(frame.Regions, r => Assert.NotEqual(Rgb8.Black, r.Cells[0]));
    }

    [Fact]
    public void MidiLearn_AddsMappingAndSkipsNormalProcessing()
    {
        var engine = NewEngine();
        Mapping? learned = null;
        engine.MappingLearned += m => learned = m;
        engine.BeginLearn(new LearnRequest(TargetRef.Zone("kick"), ParamId.Brightness, MappingMode.Set));

        engine.Push(MidiEvent.ControlChange(0, 74, 100, 0));

        Assert.NotNull(learned);
        Assert.False(engine.IsLearning);
        Assert.Equal(74, learned!.Source.Number);
        Assert.Contains(engine.Project.Mappings, m => m.Id == learned.Id);

        engine.Push(MidiEvent.ControlChange(0, 74, 0, 0));
        engine.Push(MidiEvent.NoteOn(0, 36, 1f, 0));
        engine.Tick(0f);
        Assert.Equal(Rgb8.Black, KickColor(engine));
    }

    [Fact]
    public void LoadProject_MarksLayoutChanged()
    {
        var engine = NewEngine();
        Assert.True(engine.LayoutChanged);
        engine.GetLayout();
        Assert.False(engine.LayoutChanged);
        engine.LoadProject(Project.CreateDefault());
        Assert.True(engine.LayoutChanged);
    }

    [Fact]
    public void AddZone_MarksLayoutChangedAndAppearsInLayout()
    {
        var engine = NewEngine();
        engine.GetLayout();
        Assert.False(engine.LayoutChanged);

        engine.AddZone(new Zone { Id = "extra", Name = "Extra" });

        Assert.True(engine.LayoutChanged);
        var layout = engine.GetLayout();
        Assert.Contains(layout.Zones, z => z.Id == "extra");
    }

    [Fact]
    public void AddZone_DuplicateId_ThrowsAndDoesNotChangeLayout()
    {
        var engine = NewEngine();
        engine.GetLayout();

        Assert.Throws<InvalidDataException>(() => engine.AddZone(new Zone { Id = "kick" }));

        Assert.False(engine.LayoutChanged);
        Assert.Equal(3, engine.Project.Zones.Count);
    }

    [Fact]
    public void RemoveZone_RemovesItFromLayoutAndFrames()
    {
        var engine = NewEngine();
        engine.Tick(0f);
        engine.TakeFrame(full: true);

        engine.RemoveZone("kick");

        var layout = engine.GetLayout();
        Assert.DoesNotContain(layout.Zones, z => z.Id == "kick");

        var frame = engine.TakeFrame(full: true);
        Assert.DoesNotContain(frame!.Regions, r => r.ZoneId == "kick");
    }

    [Fact]
    public void RemoveGroup_UngroupsZonesAndRebuildsRuntime()
    {
        var engine = NewEngine();
        engine.SetParam(TargetRef.Group("drums"), ParamId.Brightness, 0.5f);

        engine.RemoveGroup("drums");

        Assert.Null(engine.Project.FindZone("kick")!.GroupId);
        engine.Push(MidiEvent.NoteOn(0, 36, 1f, 0));
        engine.Tick(0f);
        var c = KickColor(engine);
        Assert.Equal(255, c.R);
    }

    [Fact]
    public void UpdateZone_AppliesChangeAndRebuildsRuntime()
    {
        var engine = NewEngine();
        engine.UpdateZone("kick", z => z.Name = "Bass Drum");
        Assert.Equal("Bass Drum", engine.Project.FindZone("kick")!.Name);
        Assert.True(engine.LayoutChanged);
    }

    [Fact]
    public void SetTempo_ChangesPulseEffectPhaseFromFreeRunning()
    {
        // Wave and Ripple are one-shot (tied to real elapsed time, not a beat grid, like a physical wave/ripple
        // wouldn't be); Pulse still loops on a tempo-lockable rate, so it's the one that exercises the plumbing.
        var freeRunning = MakeEffectProject("pulse");
        var freeEngine = new Engine(freeRunning, instanceId: "pulse0free");
        freeEngine.Push(MidiEvent.NoteOn(0, 36, 1f, 0));
        freeEngine.Tick(0.3f);
        var freeCells = freeEngine.TakeFrame(full: true)!.Regions.Single(r => r.ZoneId == "strip").Cells;

        var synced = MakeEffectProject("pulse");
        var syncedEngine = new Engine(synced, instanceId: "pulse0sync");
        syncedEngine.SetTempo(60, isPlaying: true);
        syncedEngine.Push(MidiEvent.NoteOn(0, 36, 1f, 0));
        syncedEngine.Tick(0.3f);
        var syncedCells = syncedEngine.TakeFrame(full: true)!.Regions.Single(r => r.ZoneId == "strip").Cells;

        Assert.False(freeCells.AsSpan().SequenceEqual(syncedCells));
    }

    [Fact]
    public void SetTempo_NeverCalled_StandaloneStaysFreeRunning()
    {
        var project = MakeEffectProject("pulse");
        var engine = new Engine(project, instanceId: "pulse0std");
        engine.Push(MidiEvent.NoteOn(0, 36, 1f, 0));
        engine.Tick(0.3f);
        var frame = engine.TakeFrame(full: true)!;
        Assert.NotNull(frame);
        // No SetTempo call: bpm defaults to 0, which is "free-running" - this must not throw or hang.
    }

    [Fact]
    public void GateMapping_SustainsWhileHeldThenDecaysAfterRelease()
    {
        var project = Project.CreateEmpty();
        var zone = new Zone { Id = "pad", Name = "pad" };
        zone.Params[ParamId.Hue] = 0f;
        zone.Params[ParamId.Saturation] = 1f;
        zone.Params[ParamId.Brightness] = 1f;
        zone.Params[ParamId.EffectDecay] = 0.05f;
        project.Zones.Add(zone);
        project.Mappings.Add(new Mapping
        {
            Id = "gate-map",
            Source = SourceAddress.Note(40),
            Target = TargetRef.Zone("pad"),
            Param = ParamId.EffectIntensity,
            Mode = MappingMode.Gate,
            EffectId = "flash",
        });

        var engine = new Engine(project, instanceId: "gate0001");
        engine.Push(MidiEvent.NoteOn(0, 40, 1f, 0));

        // Held well past what a 0.05s decay would normally allow - must stay lit while the key is down.
        for (int i = 0; i < 60; i++) engine.Tick(Dt);
        Assert.Equal(new Rgb8(255, 0, 0), KickColorOf(engine, "pad"));

        engine.Push(MidiEvent.NoteOff(0, 40, 0f, 0));
        for (int i = 0; i < 60; i++) engine.Tick(Dt);
        Assert.Equal(Rgb8.Black, KickColorOf(engine, "pad"));
    }

    private static Rgb8 KickColorOf(Engine engine, string zoneId)
    {
        var frame = engine.TakeFrame(full: true)!;
        return frame.Regions.Single(r => r.ZoneId == zoneId).Cells[0];
    }

    private static Project MakeEffectProject(string effectId)
    {
        var project = Project.CreateEmpty();
        var zone = new Zone { Id = "strip", Name = "strip", Rect = new RectF(0f, 0f, 1f, 1f), CellsW = 8, CellsH = 1 };
        zone.Params[ParamId.Hue] = 0f;
        zone.Params[ParamId.Saturation] = 1f;
        zone.Params[ParamId.Brightness] = 1f;
        zone.Params[ParamId.EffectDecay] = 5f;
        project.Zones.Add(zone);
        project.Mappings.Add(new Mapping
        {
            Id = "effect-map",
            Source = SourceAddress.Note(36),
            Target = TargetRef.Zone("strip"),
            Param = ParamId.EffectIntensity,
            Mode = MappingMode.Trigger,
            EffectId = effectId,
            Transform = Transform.Identity,
        });
        return project;
    }

    [Fact]
    public void RenameProject_UpdatesNameAndMarksLayoutChanged()
    {
        var engine = NewEngine();
        engine.GetLayout();

        engine.RenameProject("New Name");

        Assert.Equal("New Name", engine.Project.Name);
        Assert.True(engine.LayoutChanged);
    }
}

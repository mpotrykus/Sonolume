using Sonolume.Engine.Core;
using Sonolume.Engine.Input;
using Sonolume.Engine.Mappings;
using Sonolume.Engine.Model;
using Xunit;

namespace Sonolume.Engine.Tests;

public class MappingTests
{
    [Fact]
    public void DefaultProject_KickKey_ResolvesToKickSolid()
    {
        var project = Project.CreateDefault();
        var kickKey = DefaultMacros.KeySourceOf(project, TargetRef.Zone("kick"))!.Value;
        var engine = new MappingEngine(project.Mappings);
        var e = new ControlEvent(kickKey, ControlEventType.Trigger, 0.75f, 0);

        var actions = new MappingAction[8];
        int n = engine.Resolve(e, actions);

        Assert.Equal(1, n);
        Assert.Equal("kick", actions[0].Mapping.Target.Id);
        Assert.Equal(MappingMode.Gate, actions[0].Mapping.Mode);
        Assert.Equal("solid", actions[0].Mapping.EffectId);
        Assert.Equal(0.75f, actions[0].Value01, 4);
    }

    [Fact]
    public void UnmappedNote_ResolvesToNothing()
    {
        // Well past every default-kit target's octave (kick/snare/hihat/drums only claim notes 0-47 on channel 0).
        var engine = new MappingEngine(Project.CreateDefault().Mappings);
        var e = new ControlEvent(new SourceAddress(SourceKind.MidiNote, 0, 100), ControlEventType.Trigger, 1f, 0);
        Assert.Equal(0, engine.Resolve(e, new MappingAction[8]));
    }

    [Fact]
    public void TriggerMapping_IgnoresRelease()
    {
        var mapping = new Mapping
        {
            Id = "m",
            Source = SourceAddress.Note(36),
            Target = TargetRef.Zone("kick"),
            Mode = MappingMode.Trigger,
            EffectId = "solid",
        };
        var engine = new MappingEngine(new[] { mapping });
        var e = new ControlEvent(SourceAddress.Note(36), ControlEventType.Release, 0f, 0);
        Assert.Equal(0, engine.Resolve(e, new MappingAction[8]));
    }

    [Fact]
    public void DisabledMapping_IsSkipped()
    {
        var project = Project.CreateDefault();
        var target = TargetRef.Zone("kick");
        var kickKey = DefaultMacros.KeySourceOf(project, target)!.Value;
        project.Mappings.Single(m => m.Target == target && m.Mode == MappingMode.Gate).Enabled = false;

        var engine = new MappingEngine(project.Mappings);
        var e = new ControlEvent(kickKey, ControlEventType.Trigger, 1f, 0);
        Assert.Equal(0, engine.Resolve(e, new MappingAction[8]));
    }

    [Fact]
    public void Transform_RangeInvertAndCurve()
    {
        var t = new Transform(OutMin: 0.1f, OutMax: 1f);
        Assert.Equal(0.1f, t.Apply(0f), 4);
        Assert.Equal(1f, t.Apply(1f), 4);
        Assert.Equal(0.55f, t.Apply(0.5f), 4);

        var inverted = new Transform(Invert: true);
        Assert.Equal(0.25f, inverted.Apply(0.75f), 4);

        var window = new Transform(InMin: 0.5f, InMax: 1f);
        Assert.Equal(0f, window.Apply(0.25f), 4);
        Assert.Equal(0.5f, window.Apply(0.75f), 4);

        var expo = new Transform(Curve: Curve.Exponential);
        Assert.Equal(0.25f, expo.Apply(0.5f), 4);
    }

    [Fact]
    public void MidiLearn_CreatesMappingFromNextEvent()
    {
        var request = new LearnRequest(TargetRef.Zone("kick"), ParamId.Brightness, MappingMode.Set);
        var cc = new ControlEvent(new SourceAddress(SourceKind.MidiCC, 4, 74), ControlEventType.Set, 0.3f, 0);
        var note = new ControlEvent(new SourceAddress(SourceKind.MidiNote, 4, 36), ControlEventType.Trigger, 1f, 0);

        Assert.False(MidiLearn.Accepts(request, note));
        Assert.True(MidiLearn.Accepts(request, cc));

        var mapping = MidiLearn.CreateMapping(request, cc, "learned");
        Assert.Equal(SourceKind.MidiCC, mapping.Source.Kind);
        Assert.Equal(74, mapping.Source.Number);
        Assert.Equal(SourceAddress.Any, mapping.Source.Channel);
        Assert.Equal(ParamId.Brightness, mapping.Param);
        Assert.Equal(MappingMode.Set, mapping.Mode);
    }
}

using System.Linq;
using Sonolume.Engine.Input;
using Sonolume.Engine.Mappings;
using Sonolume.Engine.Model;
using Xunit;

namespace Sonolume.Engine.Tests;

public class DefaultMacrosTests
{
    [Fact]
    public void Sync_ReplacesStaleMacroMappingsWithCurrentConvention()
    {
        var project = Project.CreateEmpty();
        project.AddZone(new Zone { Id = "strip", Name = "Strip" });

        // Simulate a project saved under an earlier macro layout (e.g. a hand-picked note driving Hue directly,
        // no Select-mode Effect Type/Blend notes) that predates today's fixed per-octave convention.
        project.Mappings.Add(new Mapping
        {
            Id = "macro:param:Hue:Zone:strip",
            Source = SourceAddress.Note(99),
            Target = TargetRef.Zone("strip"),
            Param = ParamId.Hue,
            Mode = MappingMode.Set,
        });

        DefaultMacros.Sync(project);

        var target = TargetRef.Zone("strip");

        var typeSource = DefaultMacros.EffectTypeSourceOf(project, target);
        Assert.NotNull(typeSource);
        Assert.Equal(MappingMode.Select, project.Mappings.Single(m => m.Id == "macro:type:Zone:strip").Mode);

        var hueSource = DefaultMacros.ParamSourceOf(project, target, ParamId.Hue);
        Assert.NotNull(hueSource);
        Assert.NotEqual(99, hueSource!.Value.Number);
    }

    [Fact]
    public void For_AssignsSameOffsetDifferentOctave_AcrossZones()
    {
        var project = Project.CreateEmpty();
        project.AddZone(new Zone { Id = "a", Name = "A" });
        project.AddZone(new Zone { Id = "b", Name = "B" });
        DefaultMacros.Sync(project);

        var hueA = DefaultMacros.ParamSourceOf(project, TargetRef.Zone("a"), ParamId.Hue)!.Value;
        var hueB = DefaultMacros.ParamSourceOf(project, TargetRef.Zone("b"), ParamId.Hue)!.Value;

        Assert.Equal(hueA.Channel, hueB.Channel);
        Assert.NotEqual(hueA.Number, hueB.Number);
        Assert.Equal(0, (hueB.Number - hueA.Number) % 12);
    }

    [Fact]
    public void For_SpillsToNextChannel_WhenEveryOctaveOnAChannelIsTaken()
    {
        var project = Project.CreateEmpty();
        for (int i = 0; i < 11; i++) project.AddZone(new Zone { Id = $"z{i}", Name = $"Z{i}" });
        DefaultMacros.Sync(project);

        var first = DefaultMacros.EffectTypeSourceOf(project, TargetRef.Zone("z0"))!.Value;
        var eleventh = DefaultMacros.EffectTypeSourceOf(project, TargetRef.Zone("z10"))!.Value;

        Assert.Equal(0, first.Channel);
        Assert.Equal(1, eleventh.Channel);
    }

    [Fact]
    public void For_SkipsOctave_ThatOverlapsAnExistingKeyNote()
    {
        var project = Project.CreateEmpty();
        project.AddZone(new Zone { Id = "kick", Name = "Kick" });

        // A Key trigger note (any-channel wildcard, like SonolumeSession.AddZone creates) sitting inside what
        // would otherwise be the second zone's octave (12-23).
        project.Mappings.Add(new Mapping
        {
            Id = "key-1",
            Source = SourceAddress.Note(15),
            Target = TargetRef.Zone("kick"),
            Param = ParamId.EffectIntensity,
            Mode = MappingMode.Gate,
        });

        project.AddZone(new Zone { Id = "snare", Name = "Snare" });
        DefaultMacros.Sync(project);

        var snareType = DefaultMacros.EffectTypeSourceOf(project, TargetRef.Zone("snare"))!.Value;
        Assert.True(snareType.Number < 12 || snareType.Number >= 24, $"Expected the snare's octave to skip notes 12-23, got {snareType.Number}.");
    }

    [Fact]
    public void KeySourceOf_IsTheOctavesRootNote_OneBelowEffectType()
    {
        var project = Project.CreateEmpty();
        project.AddZone(new Zone { Id = "strip", Name = "Strip" });
        DefaultMacros.Sync(project);

        var target = TargetRef.Zone("strip");
        var key = DefaultMacros.KeySourceOf(project, target)!.Value;
        var type = DefaultMacros.EffectTypeSourceOf(project, target)!.Value;

        Assert.Equal(type.Channel, key.Channel);
        Assert.Equal(type.Number - 1, key.Number);
        Assert.Equal(0, key.Number % 12); // the octave's root is always "C"
    }

    [Fact]
    public void Sync_IsIdempotent_OnceATargetsKeyMappingSitsAtItsOctaveRoot()
    {
        var project = Project.CreateEmpty();
        project.AddZone(new Zone { Id = "strip", Name = "Strip" });
        DefaultMacros.Sync(project);

        var target = TargetRef.Zone("strip");
        var key = DefaultMacros.KeySourceOf(project, target)!.Value;
        // A Key mapping - like SonolumeSession.AddZone/Project.CreateDefault add - placed right at the root note
        // Sync itself just derived, exactly as it would exist in a saved-and-reloaded project.
        project.Mappings.Add(new Mapping
        {
            Id = "key-strip",
            Source = key,
            Target = target,
            Param = ParamId.EffectIntensity,
            Mode = MappingMode.Gate,
        });
        var typeBefore = DefaultMacros.EffectTypeSourceOf(project, target)!.Value;

        DefaultMacros.Sync(project); // simulates a reload (Engine's constructor/LoadProject call this too)

        Assert.Equal(typeBefore, DefaultMacros.EffectTypeSourceOf(project, target)!.Value);
        Assert.Equal(key, DefaultMacros.KeySourceOf(project, target)!.Value);
    }
}

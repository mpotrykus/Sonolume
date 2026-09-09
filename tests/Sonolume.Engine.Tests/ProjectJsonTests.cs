using Sonolume.Engine.Core;
using Sonolume.Engine.Input;
using Sonolume.Engine.Mappings;
using Sonolume.Engine.Model;
using Sonolume.Persist;
using Xunit;

namespace Sonolume.Engine.Tests;

public class ProjectJsonTests
{
    [Fact]
    public void DefaultProject_RoundTrips()
    {
        var original = Project.CreateDefault();
        original.Mappings.Add(new Mapping
        {
            Id = "cc7",
            Source = SourceAddress.CC(7, channel: 2),
            Target = TargetRef.Group("drums"),
            Param = ParamId.Brightness,
            Mode = MappingMode.Set,
            Transform = new Transform(OutMin: 0.1f, Curve: Curve.Exponential, Invert: true),
        });

        string json = ProjectJson.Serialize(original);
        var copy = ProjectJson.Deserialize(json);

        Assert.Equal(original.Name, copy.Name);
        Assert.Equal(original.Zones.Count, copy.Zones.Count);
        Assert.Equal(original.Groups.Count, copy.Groups.Count);
        Assert.Equal(original.Mappings.Count, copy.Mappings.Count);

        var kick = copy.FindZone("kick")!;
        Assert.Equal(0.04f, kick.Rect.X, 4);
        Assert.Equal(0f, kick.Params[ParamId.Hue], 4);
        Assert.Equal(0.25f, kick.Params[ParamId.EffectDecay], 4);
        Assert.Equal("drums", kick.GroupId);

        var cc7 = copy.Mappings.Single(m => m.Id == "cc7");
        Assert.Equal(SourceKind.MidiCC, cc7.Source.Kind);
        Assert.Equal(2, cc7.Source.Channel);
        Assert.Equal(TargetKind.Group, cc7.Target.Kind);
        Assert.Equal(0.1f, cc7.Transform.OutMin, 4);
        Assert.True(cc7.Transform.Invert);
        Assert.Equal(Curve.Exponential, cc7.Transform.Curve);

        Assert.Contains("\"mode\": \"gate\"", json);
        Assert.Contains("\"hue\": 0.62", json);
    }

    [Fact]
    public void UnknownMappingTarget_IsRejected()
    {
        var project = Project.CreateDefault();
        project.Mappings.Add(new Mapping { Id = "bad", Source = SourceAddress.CC(1), Target = TargetRef.Zone("nope") });
        var json = ProjectJson.Serialize(project);
        Assert.Throws<InvalidDataException>(() => ProjectJson.Deserialize(json));
    }

    [Fact]
    public void UnknownZoneGroupId_IsRejected()
    {
        var project = Project.CreateDefault();
        project.FindZone("kick")!.GroupId = "nope";
        var json = ProjectJson.Serialize(project);
        Assert.Throws<InvalidDataException>(() => ProjectJson.Deserialize(json));
    }

    [Fact]
    public void UnknownGroupParentId_IsRejected()
    {
        var project = Project.CreateDefault();
        project.Groups.Add(new Group { Id = "child", ParentId = "nope" });
        var json = ProjectJson.Serialize(project);
        Assert.Throws<InvalidDataException>(() => ProjectJson.Deserialize(json));
    }

    [Fact]
    public void CyclicGroupParentChain_IsRejected()
    {
        var project = Project.CreateDefault();
        project.Groups.Add(new Group { Id = "a", ParentId = "b" });
        project.Groups.Add(new Group { Id = "b", ParentId = "a" });
        var json = ProjectJson.Serialize(project);
        Assert.Throws<InvalidDataException>(() => ProjectJson.Deserialize(json));
    }
}

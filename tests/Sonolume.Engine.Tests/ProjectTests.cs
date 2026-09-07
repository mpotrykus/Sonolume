using Sonolume.Engine.Input;
using Sonolume.Engine.Mappings;
using Sonolume.Engine.Model;
using Xunit;

namespace Sonolume.Engine.Tests;

public class ProjectTests
{
    [Fact]
    public void AddZone_DuplicateId_ThrowsAndLeavesProjectUnchanged()
    {
        var project = Project.CreateDefault();
        int before = project.Zones.Count;

        Assert.Throws<InvalidDataException>(() => project.AddZone(new Zone { Id = "kick", Name = "Duplicate" }));

        Assert.Equal(before, project.Zones.Count);
    }

    [Fact]
    public void AddZone_UnknownGroupId_ThrowsAndLeavesProjectUnchanged()
    {
        var project = Project.CreateDefault();
        int before = project.Zones.Count;

        Assert.Throws<InvalidDataException>(() => project.AddZone(new Zone { Id = "new", GroupId = "nope" }));

        Assert.Equal(before, project.Zones.Count);
    }

    [Fact]
    public void RemoveZone_CascadesMappingRemoval()
    {
        var project = Project.CreateDefault();
        Assert.Contains(project.Mappings, m => m.Target == TargetRef.Zone("kick"));

        Assert.True(project.RemoveZone("kick"));

        Assert.Null(project.FindZone("kick"));
        Assert.DoesNotContain(project.Mappings, m => m.Target == TargetRef.Zone("kick"));
    }

    [Fact]
    public void RemoveZone_UnknownId_ReturnsFalse()
    {
        var project = Project.CreateDefault();
        Assert.False(project.RemoveZone("nope"));
    }

    [Fact]
    public void UpdateZone_PreservesUntouchedParams()
    {
        var project = Project.CreateDefault();
        var before = project.FindZone("kick")!.Params[ParamId.Hue];

        project.UpdateZone("kick", z => z.Name = "Renamed");

        var kick = project.FindZone("kick")!;
        Assert.Equal("Renamed", kick.Name);
        Assert.Equal(before, kick.Params[ParamId.Hue]);
    }

    [Fact]
    public void UpdateZone_UnknownGroupId_ThrowsAndRollsBack()
    {
        var project = Project.CreateDefault();
        var originalName = project.FindZone("kick")!.Name;

        Assert.Throws<InvalidDataException>(() => project.UpdateZone("kick", z => z.GroupId = "nope"));

        var kick = project.FindZone("kick")!;
        Assert.Equal(originalName, kick.Name);
        Assert.Equal("drums", kick.GroupId);
    }

    [Fact]
    public void UpdateZone_UnknownId_Throws()
    {
        var project = Project.CreateDefault();
        Assert.Throws<InvalidDataException>(() => project.UpdateZone("nope", z => z.Name = "x"));
    }

    [Fact]
    public void AddGroup_DuplicateId_Throws()
    {
        var project = Project.CreateDefault();
        Assert.Throws<InvalidDataException>(() => project.AddGroup(new Group { Id = "drums" }));
    }

    [Fact]
    public void AddGroup_CyclicParent_Throws()
    {
        var project = Project.CreateDefault();
        project.AddGroup(new Group { Id = "a", ParentId = "drums" });

        Assert.Throws<InvalidDataException>(() => project.UpdateGroup("drums", g => g.ParentId = "a"));
    }

    [Fact]
    public void AddGroup_SelfParent_Throws()
    {
        var project = Project.CreateDefault();
        Assert.Throws<InvalidDataException>(() => project.AddGroup(new Group { Id = "self", ParentId = "self" }));
    }

    [Fact]
    public void RemoveGroup_UngroupsZonesReparentsChildGroupsAndRemovesMappings()
    {
        var project = Project.CreateDefault();
        project.AddGroup(new Group { Id = "child", ParentId = "drums" });
        project.Mappings.Add(new Mapping { Id = "group-map", Source = SourceAddress.CC(1), Target = TargetRef.Group("drums") });

        Assert.True(project.RemoveGroup("drums"));

        Assert.Null(project.FindGroup("drums"));
        Assert.All(project.Zones, z => Assert.Null(z.GroupId));
        Assert.Null(project.FindGroup("child")!.ParentId);
        Assert.DoesNotContain(project.Mappings, m => m.Id == "group-map");
    }

    [Fact]
    public void RemoveGroup_UnknownId_ReturnsFalse()
    {
        var project = Project.CreateDefault();
        Assert.False(project.RemoveGroup("nope"));
    }

    [Fact]
    public void UpdateGroup_PreservesUntouchedParams()
    {
        var project = Project.CreateDefault();
        project.UpdateGroup("drums", g => g.Params[ParamId.Brightness] = 0.5f);

        project.UpdateGroup("drums", g => g.Name = "Renamed");

        var drums = project.FindGroup("drums")!;
        Assert.Equal("Renamed", drums.Name);
        Assert.Equal(0.5f, drums.Params[ParamId.Brightness]);
    }
}

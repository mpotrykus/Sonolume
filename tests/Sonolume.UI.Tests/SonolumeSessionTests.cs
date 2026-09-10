using Sonolume.Engine.Mappings;
using Sonolume.Engine.Model;
using Xunit;

namespace Sonolume.UI.Tests;

public class SonolumeSessionTests
{
    private static SonolumeSession NewSession() => new(Project.CreateDefault());

    [Fact]
    public void AddZone_Undo_RestoresPreviousProject()
    {
        using var session = NewSession();
        session.AddZone(new Zone { Id = "new1", Name = "New" });
        Assert.NotNull(session.GetProjectCopy().FindZone("new1"));

        session.Undo();

        Assert.Null(session.GetProjectCopy().FindZone("new1"));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void AddZone_KeyMapping_IsItsOctavesRootNote()
    {
        using var session = NewSession();

        session.AddZone(new Zone { Id = "new1", Name = "New 1" });
        session.AddZone(new Zone { Id = "new2", Name = "New 2" });

        var project = session.GetProjectCopy();
        var key1 = project.Mappings.Single(m => m.Target == TargetRef.Zone("new1") && m.Mode == MappingMode.Gate);
        var key2 = project.Mappings.Single(m => m.Target == TargetRef.Zone("new2") && m.Mode == MappingMode.Gate);

        Assert.Equal(DefaultMacros.KeySourceOf(project, TargetRef.Zone("new1"))!.Value, key1.Source);
        Assert.Equal(DefaultMacros.KeySourceOf(project, TargetRef.Zone("new2"))!.Value, key2.Source);
        Assert.NotEqual(key1.Source, key2.Source);
    }

    [Fact]
    public void AddGroup_KeyMapping_IsItsOctavesRootNote()
    {
        using var session = NewSession();

        session.AddGroup(new Group { Id = "newgroup", Name = "New Group" });

        var project = session.GetProjectCopy();
        var key = project.Mappings.Single(m => m.Target == TargetRef.Group("newgroup") && m.Mode == MappingMode.Gate);

        Assert.Equal(DefaultMacros.KeySourceOf(project, TargetRef.Group("newgroup"))!.Value, key.Source);
    }

    [Fact]
    public void Redo_AfterUndo_ReappliesChange()
    {
        using var session = NewSession();
        session.RenameProject("Renamed");
        session.Undo();
        Assert.Equal("Default Kit", session.GetProjectCopy().Name);

        session.Redo();

        Assert.Equal("Renamed", session.GetProjectCopy().Name);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void UndoStack_CapsAtMaxDepth()
    {
        using var session = NewSession();
        for (int i = 1; i <= 51; i++) session.RenameProject($"R{i}");

        for (int i = 0; i < 50; i++) session.Undo();

        Assert.False(session.CanUndo);
        Assert.Equal("R1", session.GetProjectCopy().Name);
    }

    [Fact]
    public void NewProject_ClearsUndoHistory()
    {
        using var session = NewSession();
        session.RenameProject("Renamed");
        Assert.True(session.CanUndo);

        session.NewProject("Fresh");

        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void ImportProjectJson_ClearsUndoHistory()
    {
        using var session = NewSession();
        session.RenameProject("Renamed");
        Assert.True(session.CanUndo);

        session.ImportProjectJson(session.ExportProjectJson());

        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void Undo_WithEmptyStack_IsNoOp()
    {
        using var session = NewSession();
        session.Undo();
        Assert.Equal("Default Kit", session.GetProjectCopy().Name);
    }

    [Fact]
    public void FailedMutation_DoesNotPushUndoEntry()
    {
        using var session = NewSession();
        Assert.Throws<InvalidDataException>(() => session.AddZone(new Zone { Id = "kick" }));
        Assert.False(session.CanUndo);
    }

    /// <summary>Builds a session on a default-kit project whose "kick" mapping is still Trigger-mode, simulating
    /// one learned before Gate became the default (see <see cref="SonolumeSession.SetEffect"/>).</summary>
    private static SonolumeSession NewSessionWithTriggerKick()
    {
        var project = Project.CreateDefault();
        project.Mappings.Single(m => m.Target == TargetRef.Zone("kick") && m.Mode == MappingMode.Gate).Mode = MappingMode.Trigger;
        return new SonolumeSession(project);
    }

    [Fact]
    public void SetEffect_UpgradesDefaultKitTriggerMappingToGate()
    {
        using var session = NewSessionWithTriggerKick();
        var kick = session.GetProjectCopy().Mappings.Single(m => m.Target == TargetRef.Zone("kick") && m.Mode is MappingMode.Trigger or MappingMode.Gate);
        Assert.Equal(MappingMode.Trigger, kick.Mode);

        session.SetEffect(TargetRef.Zone("kick"), "flash");

        var updated = session.GetProjectCopy().Mappings.Single(m => m.Target == TargetRef.Zone("kick") && m.Mode is MappingMode.Trigger or MappingMode.Gate);
        Assert.Equal(MappingMode.Gate, updated.Mode);
        Assert.Equal("flash", updated.EffectId);
    }

    [Fact]
    public void SetEffect_CalledAgainWithSameValue_DoesNotPushASecondUndoEntry()
    {
        using var session = NewSessionWithTriggerKick();
        session.SetEffect(TargetRef.Zone("kick"), "flash");
        session.SetEffect(TargetRef.Zone("kick"), "flash"); // already Gate + "flash" - should be a no-op

        session.Undo();

        Assert.False(session.CanUndo); // only one undo entry existed, despite two calls
        var reverted = session.GetProjectCopy().Mappings.Single(m => m.Target == TargetRef.Zone("kick") && m.Mode is MappingMode.Trigger or MappingMode.Gate);
        Assert.Equal(MappingMode.Trigger, reverted.Mode);
    }

    [Fact]
    public void SetEffect_NoMappingForTarget_IsNoOp()
    {
        using var session = NewSession();
        session.SetEffect(TargetRef.Zone("nonexistent"), "wave");
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void RenameProject_IsUndoable()
    {
        using var session = NewSession();
        session.RenameProject("Renamed");
        Assert.Equal("Renamed", session.GetProjectCopy().Name);

        session.Undo();

        Assert.Equal("Default Kit", session.GetProjectCopy().Name);
    }
}

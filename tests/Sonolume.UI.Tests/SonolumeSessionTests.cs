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

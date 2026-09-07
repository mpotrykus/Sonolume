using Sonolume.Engine;
using Sonolume.Engine.Input;
using Sonolume.Engine.Mappings;
using Sonolume.Engine.Model;
using Sonolume.Persist;
using Sonolume.Transport;

namespace Sonolume.UI;

/// <summary>Composition root shared by the plugin and the standalone app: engine + runner thread + SignalRGB sink.</summary>
public sealed class SonolumeSession : IDisposable
{
    private const int MaxUndoDepth = 50;

    private readonly List<string> undoStack = new();
    private readonly List<string> redoStack = new();

    public SonolumeSession(Project? project = null, HttpCanvasSinkOptions? sinkOptions = null)
    {
        InstanceId = Guid.NewGuid().ToString("N")[..8];
        Engine = new Engine.Engine(project ?? Project.CreateDefault(), instanceId: InstanceId);
        Sink = new HttpCanvasSink(sinkOptions ?? new HttpCanvasSinkOptions(InstanceId));
        Runner = new EngineRunner(Engine, Sink);
        Runner.Start();
    }

    public string InstanceId { get; }

    public Engine.Engine Engine { get; }

    public HttpCanvasSink Sink { get; }

    public EngineRunner Runner { get; }

    public bool CanUndo => undoStack.Count > 0;

    public bool CanRedo => redoStack.Count > 0;

    /// <summary>The zone highlighted/edited on the live canvas. Shared so the always-visible preview and the
    /// Zone Editor window's list agree on what's selected, whichever one the user interacts with. UI-thread-only;
    /// not persisted and not part of undo/redo.</summary>
    public string? SelectedZoneId { get; set; }

    public void Enqueue(in MidiEvent e) => Runner.Enqueue(e);

    public string ExportProjectJson() => Runner.Invoke(e => ProjectJson.Serialize(e.Project));

    /// <summary>A detached copy of the current project, safe to read/mutate off the engine thread (e.g. to
    /// populate a zone/group list or preview a cascade-delete's impact).</summary>
    public Project GetProjectCopy() => ProjectJson.Deserialize(ExportProjectJson());

    /// <summary>Throws on invalid JSON before anything reaches the engine thread.</summary>
    public void ImportProjectJson(string json)
    {
        var project = ProjectJson.Deserialize(json);
        Runner.Post(e => e.LoadProject(project));
        ClearHistory();
    }

    public void NewProject(string name)
    {
        var project = Project.CreateEmpty(name);
        Runner.Post(e => e.LoadProject(project));
        ClearHistory();
    }

    public void SetDecaySeconds(float seconds) => Runner.Post(e => e.SetParamAllZones(ParamId.EffectDecay, seconds));

    /// <summary>Live parameter tweak (brightness, hue, ...). Not undoable, same as <see cref="SetDecaySeconds"/>.</summary>
    public void SetParam(TargetRef target, ParamId id, float raw) => Runner.Post(e => e.SetParam(target, id, raw));

    public void AddZone(Zone zone) => MutateWithUndo(e => e.AddZone(zone));

    public void RemoveZone(string id) => MutateWithUndo(e => e.RemoveZone(id));

    public void UpdateZone(string id, Action<Zone> apply) => MutateWithUndo(e => e.UpdateZone(id, apply));

    public void AddGroup(Group group) => MutateWithUndo(e => e.AddGroup(group));

    public void RemoveGroup(string id) => MutateWithUndo(e => e.RemoveGroup(id));

    public void UpdateGroup(string id, Action<Group> apply) => MutateWithUndo(e => e.UpdateGroup(id, apply));

    public void RenameProject(string name) => MutateWithUndo(e => e.RenameProject(name));

    public void Undo()
    {
        if (undoStack.Count == 0) return;
        string target = undoStack[^1];
        undoStack.RemoveAt(undoStack.Count - 1);
        Runner.Invoke(e =>
        {
            PushCapped(redoStack, ProjectJson.Serialize(e.Project));
            e.LoadProject(ProjectJson.Deserialize(target));
        });
    }

    public void Redo()
    {
        if (redoStack.Count == 0) return;
        string target = redoStack[^1];
        redoStack.RemoveAt(redoStack.Count - 1);
        Runner.Invoke(e =>
        {
            PushCapped(undoStack, ProjectJson.Serialize(e.Project));
            e.LoadProject(ProjectJson.Deserialize(target));
        });
    }

    /// <summary>Snapshots the project before <paramref name="mutate"/> runs, then records that snapshot for undo
    /// only if the mutation succeeds. Uses <see cref="EngineRunner.Invoke(Action{Engine.Engine},int)"/> (not
    /// <c>Post</c>) so validation exceptions propagate back to the caller instead of crashing the engine thread.</summary>
    private void MutateWithUndo(Action<Engine.Engine> mutate)
    {
        string? before = null;
        Runner.Invoke(e =>
        {
            before = ProjectJson.Serialize(e.Project);
            mutate(e);
        });
        PushCapped(undoStack, before!);
        redoStack.Clear();
    }

    private void ClearHistory()
    {
        undoStack.Clear();
        redoStack.Clear();
    }

    private static void PushCapped(List<string> stack, string value)
    {
        stack.Add(value);
        if (stack.Count > MaxUndoDepth) stack.RemoveAt(0);
    }

    public void Dispose()
    {
        Runner.Dispose();
        Sink.Dispose();
    }
}

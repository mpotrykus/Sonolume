using System.Threading;
using Sonolume.Engine;
using Sonolume.Engine.Core;
using Sonolume.Engine.Effects;
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

    /// <summary>Serialized project as of the last load/save, so <see cref="HasUnsavedChanges"/> reflects the actual
    /// content rather than just undo-stack depth (e.g. undoing back to it, or editing back to the same values, both
    /// count as clean).</summary>
    private string savedSnapshot;

    /// <summary>Project state as of the last <see cref="BeginLearn"/>, so <see cref="PollLearn"/> can fold the
    /// mapping the engine thread adds directly (see <see cref="Engine.Engine.PushControl"/>) into undo history.</summary>
    private string? learnBeforeSnapshot;

    public SonolumeSession(Project? project = null, HttpCanvasSinkOptions? sinkOptions = null)
    {
        InstanceId = Guid.NewGuid().ToString("N")[..8];
        Engine = new Engine.Engine(project ?? Project.CreateDefault(), instanceId: InstanceId);
        Sink = new HttpCanvasSink(sinkOptions ?? new HttpCanvasSinkOptions(InstanceId));
        Runner = new EngineRunner(Engine, Sink);
        savedSnapshot = ProjectJson.Serialize(Engine.Project);
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

    /// <summary>Path last opened or saved to, so "Save" can write back without prompting. UI-thread-only;
    /// not persisted and not part of undo/redo.</summary>
    public string? CurrentFilePath { get; set; }

    /// <summary>True while the project differs from what was last loaded/saved, so "Save" can disable itself
    /// when there is nothing to write (including after undoing back to that point).</summary>
    public bool HasUnsavedChanges { get; private set; }

    /// <summary>The target/param/mode currently armed to learn the next matching MIDI event, if any.
    /// UI-thread-only. Only one control can be listening at a time (the engine holds a single pending request).</summary>
    public (TargetRef Target, ParamId Param, MappingMode Mode)? PendingLearn { get; private set; }

    /// <summary>True while the engine is actually waiting on a controller move for <see cref="PendingLearn"/>
    /// (lags <see cref="PendingLearn"/> slightly, since it comes from the polled engine snapshot).</summary>
    public bool IsLearning => Runner.LatestSnapshot?.IsLearning ?? false;

    public bool IsPendingLearn(TargetRef target, ParamId param, MappingMode mode) =>
        PendingLearn is { } p && p.Target == target && p.Param == param && p.Mode == mode;

    public void Enqueue(in MidiEvent e) => Runner.Enqueue(e);

    /// <summary>Reports the host's tempo (the plugin calls this every audio buffer). Never called by the
    /// standalone app, which has no host and always runs effects free-running off EffectSpeed.</summary>
    public void SetTempo(double bpm, bool isPlaying) => Runner.SetTempo(bpm, isPlaying);

    public string ExportProjectJson() => Runner.Invoke(e => ProjectJson.Serialize(e.Project));

    /// <summary>A detached copy of the current project, safe to read/mutate off the engine thread (e.g. to
    /// populate a zone/group list or preview a cascade-delete's impact).</summary>
    public Project GetProjectCopy() => ProjectJson.Deserialize(ExportProjectJson());

    /// <summary>Throws on invalid JSON before anything reaches the engine thread.</summary>
    public void ImportProjectJson(string json)
    {
        var project = ProjectJson.Deserialize(json);
        savedSnapshot = ProjectJson.Serialize(project);
        Runner.Post(e => e.LoadProject(project));
        ClearHistory();
        HasUnsavedChanges = false;
    }

    public void NewProject(string name)
    {
        var project = Project.CreateEmpty(name);
        savedSnapshot = ProjectJson.Serialize(project);
        Runner.Post(e => e.LoadProject(project));
        ClearHistory();
        CurrentFilePath = null;
        HasUnsavedChanges = false;
    }

    /// <summary>Marks <paramref name="json"/> (just written to <see cref="CurrentFilePath"/>) as the clean point,
    /// so "Save" disables itself again.</summary>
    public void MarkSaved(string json)
    {
        savedSnapshot = json;
        HasUnsavedChanges = false;
    }

    /// <summary>Live parameter tweak (brightness, hue, ...). Not undoable.</summary>
    public void SetParam(TargetRef target, ParamId id, float raw)
    {
        Runner.Post(e => e.SetParam(target, id, raw));
        HasUnsavedChanges = true;
    }

    /// <summary>Adds the zone pre-wired with the default macro-per-param convention (see <see cref="DefaultMacros"/>)
    /// so its continuous params are controllable from the DAW's host parameters immediately, no setup needed, and a
    /// Gate "Key" mapping on the next MIDI note not already claimed by another mapping, so the zone lights up as
    /// soon as it's added instead of sitting silent until the user manually learns a key.</summary>
    public void AddZone(Zone zone) => MutateWithUndo(e =>
    {
        e.AddZone(zone);
        foreach (var m in DefaultMacros.For(TargetRef.Zone(zone.Id))) e.AddMapping(m);
        e.AddMapping(new Mapping
        {
            Id = $"key-{Guid.NewGuid():N}",
            Source = SourceAddress.Note(NextAvailableNote(e.Project.Mappings)),
            Target = TargetRef.Zone(zone.Id),
            Param = ParamId.EffectIntensity,
            Mode = MappingMode.Gate,
            EffectId = SolidEffect.TypeName,
            Transform = Transform.Identity,
        });
    });

    /// <summary>Lowest MIDI note (starting from C1/36, matching the default kit's kick) not already used by any
    /// existing note mapping, regardless of target or mode - two mappings sharing a note would both fire together
    /// whenever it plays.</summary>
    private static int NextAvailableNote(IEnumerable<Mapping> mappings)
    {
        var used = new HashSet<int>(mappings.Where(m => m.Source.Kind == SourceKind.MidiNote).Select(m => m.Source.Number));
        int note = 36;
        while (used.Contains(note)) note++;
        return note;
    }

    public void RemoveZone(string id) => MutateWithUndo(e => e.RemoveZone(id));

    public void UpdateZone(string id, Action<Zone> apply) => MutateWithUndo(e => e.UpdateZone(id, apply));

    /// <summary>Reassigns ZIndex for every zone so the render order matches <paramref name="orderedIds"/>, which
    /// runs topmost-first (highest ZIndex first) as shown in the zone list.</summary>
    public void ReorderZones(IReadOnlyList<string> orderedIds) => MutateWithUndo(e =>
    {
        for (int i = 0; i < orderedIds.Count; i++)
        {
            int zIndex = orderedIds.Count - 1 - i;
            e.UpdateZone(orderedIds[i], z => z.ZIndex = zIndex);
        }
    });

    /// <summary>Adds the group pre-wired with the default macro-per-param convention (see <see cref="DefaultMacros"/>),
    /// same as <see cref="AddZone"/>, plus a Gate "Key" mapping on the next MIDI note not already claimed by another
    /// mapping, so it lights up as soon as it's added instead of sitting silent until the user manually learns a
    /// key.</summary>
    public void AddGroup(Group group) => MutateWithUndo(e =>
    {
        e.AddGroup(group);
        foreach (var m in DefaultMacros.For(TargetRef.Group(group.Id))) e.AddMapping(m);
        e.AddMapping(new Mapping
        {
            Id = $"key-{Guid.NewGuid():N}",
            Source = SourceAddress.Note(NextAvailableNote(e.Project.Mappings)),
            Target = TargetRef.Group(group.Id),
            Param = ParamId.EffectIntensity,
            Mode = MappingMode.Gate,
            EffectId = SolidEffect.TypeName,
            Transform = Transform.Identity,
        });
    });

    public void RemoveGroup(string id) => MutateWithUndo(e => e.RemoveGroup(id));

    public void UpdateGroup(string id, Action<Group> apply) => MutateWithUndo(e => e.UpdateGroup(id, apply));

    public void RenameProject(string name) => MutateWithUndo(e => e.RenameProject(name));

    /// <summary>Clears <paramref name="target"/>'s note-triggered "Key" mapping(s) - Trigger/Gate only, leaving its
    /// fixed macro mappings (see <see cref="DefaultMacros"/>) untouched.</summary>
    public void RemoveMappingsForTarget(TargetRef target) => MutateWithUndo(e =>
    {
        var ids = e.Project.Mappings.Where(m => m.Target == target && m.Mode is MappingMode.Trigger or MappingMode.Gate)
            .Select(m => m.Id).ToList();
        foreach (var id in ids) e.RemoveMapping(id);
    });

    /// <summary>Switches the effect used by <paramref name="target"/>'s Trigger/Gate mapping(s), if any exist
    /// (Set-mode macro mappings and the Select-mode effect-type macro mapping ignore this and are left alone), and upgrades
    /// them to Gate so the effect holds for as long as the key is down - covers mappings learned before Gate became
    /// the default, and the built-in default kit's Trigger mappings. No-op - and no undo entry - if nothing is
    /// learned yet (the effect picker's selection is then just remembered for the next <see cref="BeginLearn"/>) or
    /// if every matching mapping already has this EffectId and is already Gate, so the effect picker can call this
    /// unconditionally on every open without spamming undo history.</summary>
    public void SetEffect(TargetRef target, string effectId)
    {
        var matching = GetProjectCopy().Mappings.Where(m => m.Target == target && m.Mode is MappingMode.Trigger or MappingMode.Gate).ToList();
        if (matching.Count == 0) return;
        if (matching.All(m => m.EffectId == effectId && m.Mode == MappingMode.Gate)) return;

        MutateWithUndo(e =>
        {
            foreach (var m in e.Project.Mappings)
            {
                if (m.Target != target || m.Mode is not (MappingMode.Trigger or MappingMode.Gate)) continue;
                m.EffectId = effectId;
                m.Mode = MappingMode.Gate;
            }
        });
    }

    /// <summary>Arms the engine to turn the next matching MIDI event into a mapping targeting
    /// <paramref name="target"/>. Not undoable itself; <see cref="PollLearn"/> folds the result in once it lands.</summary>
    public void BeginLearn(TargetRef target, ParamId param, MappingMode mode, string? effectId = null)
    {
        PendingLearn = (target, param, mode);
        learnBeforeSnapshot = ExportProjectJson();
        Runner.Post(e => e.BeginLearn(new LearnRequest(target, param, mode, effectId)));
    }

    public void CancelLearn()
    {
        Runner.Post(e => e.CancelLearn());
        PendingLearn = null;
        learnBeforeSnapshot = null;
    }

    private long macroEventsReceived;

    /// <summary>How many host macro parameter changes have reached <see cref="SetMacroValue"/>, for the "CC: N"
    /// status readout - if that number never moves while you're automating a CC in the DAW, the host isn't
    /// delivering the parameter change to the plugin at all (nothing downstream of it is the problem).</summary>
    public long MacroEventsReceived => Interlocked.Read(ref macroEventsReceived);

    /// <summary>Feeds a host macro parameter's new value into the engine as a control event, exactly like an
    /// incoming MIDI CC would be - the plugin calls this from its parameter-change callback.</summary>
    public void SetMacroValue(int macroIndex, float value01)
    {
        Interlocked.Increment(ref macroEventsReceived);
        Runner.Post(e => e.PushControl(new ControlEvent(SourceAddress.HostMacro(macroIndex), ControlEventType.Set, value01, Clock.Now())));
    }

    /// <summary>Call periodically (the editor already polls at a fixed interval). Once a pending learn resolves -
    /// the engine thread adds the mapping directly, outside <see cref="MutateWithUndo"/> - folds it into undo
    /// history and unsaved-changes tracking like any other structural edit.</summary>
    public void PollLearn()
    {
        if (PendingLearn is null || IsLearning) return;
        string before = learnBeforeSnapshot!;
        learnBeforeSnapshot = null;
        PendingLearn = null;
        PushCapped(undoStack, before);
        redoStack.Clear();
        HasUnsavedChanges = ExportProjectJson() != savedSnapshot;
    }

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
        HasUnsavedChanges = target != savedSnapshot;
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
        HasUnsavedChanges = target != savedSnapshot;
    }

    /// <summary>Snapshots the project before <paramref name="mutate"/> runs, then records that snapshot for undo
    /// only if the mutation succeeds. Uses <see cref="EngineRunner.Invoke(Action{Engine.Engine},int)"/> (not
    /// <c>Post</c>) so validation exceptions propagate back to the caller instead of crashing the engine thread.</summary>
    private void MutateWithUndo(Action<Engine.Engine> mutate)
    {
        string? before = null;
        string? after = null;
        Runner.Invoke(e =>
        {
            before = ProjectJson.Serialize(e.Project);
            mutate(e);
            after = ProjectJson.Serialize(e.Project);
        });
        PushCapped(undoStack, before!);
        redoStack.Clear();
        HasUnsavedChanges = after != savedSnapshot;
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

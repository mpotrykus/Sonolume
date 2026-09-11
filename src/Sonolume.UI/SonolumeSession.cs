using Sonolume.Engine;
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

    /// <summary>Adds the zone pre-wired with the default note-per-param convention (see <see cref="DefaultMacros"/>)
    /// so its continuous params are controllable from its auto-assigned octave immediately, no setup needed, and a
    /// Gate "Key" mapping on that same octave's root note ("C", see <see cref="DefaultMacros.KeySourceOf"/>) and
    /// channel, so the zone lights up as soon as it's added instead of sitting silent until the user picks a
    /// different key octave.</summary>
    public void AddZone(Zone zone) => MutateWithUndo(e =>
    {
        e.AddZone(zone);
        AddDefaultMappings(e, TargetRef.Zone(zone.Id));
    });

    /// <summary>Wires the note-per-param convention (see <see cref="DefaultMacros"/>) plus a Gate "Key" mapping
    /// on the target's auto-assigned octave, shared by every path that creates a zone or group (manual add,
    /// SignalRGB import) so they all light up immediately the same way.</summary>
    private static void AddDefaultMappings(Engine.Engine e, TargetRef target)
    {
        foreach (var m in DefaultMacros.For(e.Project, target)) e.AddMapping(m);
        e.AddMapping(new Mapping
        {
            Id = $"key-{Guid.NewGuid():N}",
            Source = DefaultMacros.KeySourceOf(e.Project, target)!.Value,
            Target = target,
            Param = ParamId.EffectIntensity,
            Mode = MappingMode.Gate,
            EffectId = SolidEffect.TypeName,
            Transform = Transform.Identity,
        });
    }

    /// <summary>Fetches the current device layout from a locally running SignalRGB (via its MCP server) and
    /// creates/updates one zone per unambiguously-positioned device, matched by a "signalrgb-{uid}" id so
    /// re-importing after moving devices in SignalRGB's own Layout editor updates the same zones instead of
    /// duplicating them. New zones get the same default mappings as a manually-added zone (see
    /// <see cref="AddDefaultMappings"/>). Existing zones keep their id, group, params and mappings - only
    /// position/name/rotation are refreshed. One undo entry for the whole import. Cell grid resolution is left at
    /// each zone's default: SignalRGB's API exposes only a whole-device rect (position/size/rotation), not
    /// per-LED layout. Multi-component controllers (a fan hub wired to several fans, say) are reported in the
    /// result's <see cref="SignalRgbImportResult.SkippedMultiComponent"/> instead of being imported - see
    /// <see cref="SignalRgbMcpClient.ReadDeviceLayoutsAsync"/> for why.</summary>
    public async Task<SignalRgbImportResult> ImportSignalRgbLayoutAsync(SignalRgbMcpClient client, CancellationToken ct = default)
    {
        var result = await client.ReadDeviceLayoutsAsync(ct);
        MutateWithUndo(e =>
        {
            foreach (var d in result.Devices)
            {
                string id = $"signalrgb-{d.Uid}";
                if (e.Project.FindZone(id) is not null)
                {
                    e.UpdateZone(id, z =>
                    {
                        z.Name = d.Name;
                        z.Rect = d.Rect;
                        z.Rotation = d.Rotation;
                    });
                }
                else
                {
                    e.AddZone(new Zone { Id = id, Name = d.Name, Rect = d.Rect, Rotation = d.Rotation });
                    AddDefaultMappings(e, TargetRef.Zone(id));
                }
            }
        });
        return result;
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

    /// <summary>Adds the group pre-wired with the default note-per-param convention (see <see cref="DefaultMacros"/>),
    /// same as <see cref="AddZone"/>, plus a Gate "Key" mapping on that same octave's root note ("C") and channel,
    /// so it lights up as soon as it's added instead of sitting silent until the user picks a different key
    /// octave.</summary>
    public void AddGroup(Group group) => MutateWithUndo(e =>
    {
        e.AddGroup(group);
        AddDefaultMappings(e, TargetRef.Group(group.Id));
    });

    public void RemoveGroup(string id) => MutateWithUndo(e => e.RemoveGroup(id));

    public void UpdateGroup(string id, Action<Group> apply) => MutateWithUndo(e => e.UpdateGroup(id, apply));

    public void RenameProject(string name) => MutateWithUndo(e => e.RenameProject(name));

    /// <summary>Switches the effect used by <paramref name="target"/>'s Trigger/Gate mapping(s), if any exist
    /// (Set-mode macro mappings and the Select-mode effect-type macro mapping ignore this and are left alone), and upgrades
    /// them to Gate so the effect holds for as long as the key is down - covers mappings learned before Gate became
    /// the default, and the built-in default kit's Trigger mappings. No-op - and no undo entry - if nothing is
    /// learned yet (the effect picker's selection is then just remembered until a Key mapping exists) or
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

    /// <summary>Sets the MIDI channel <paramref name="target"/>'s Key mapping(s) must match -
    /// <see cref="SourceAddress.Any"/> for "any channel" (the default until the user picks one), or a specific
    /// 0-based channel. Trigger/Gate only, same filter as <see cref="SetEffect"/>; no-op if there's no Key mapping
    /// yet or every match already has this channel.</summary>
    public void SetKeyChannel(TargetRef target, int channel)
    {
        var matching = GetProjectCopy().Mappings.Where(m => m.Target == target && m.Mode is MappingMode.Trigger or MappingMode.Gate).ToList();
        if (matching.Count == 0) return;
        if (matching.All(m => m.Source.Channel == channel)) return;

        MutateWithUndo(e =>
        {
            foreach (var m in e.Project.Mappings)
            {
                if (m.Target != target || m.Mode is not (MappingMode.Trigger or MappingMode.Gate)) continue;
                m.Source = m.Source with { Channel = channel };
            }
        });
    }

    /// <summary>Sets the MIDI note <paramref name="target"/>'s Key mapping(s) must match to the root ("C") of
    /// the given octave (<paramref name="note"/> is that C's note number, e.g. 60 for C4). Trigger/Gate only, same
    /// filter as <see cref="SetKeyChannel"/>; no-op if there's no Key mapping yet or every match already has this
    /// note.</summary>
    public void SetKeyOctave(TargetRef target, int note)
    {
        var matching = GetProjectCopy().Mappings.Where(m => m.Target == target && m.Mode is MappingMode.Trigger or MappingMode.Gate).ToList();
        if (matching.Count == 0) return;
        if (matching.All(m => m.Source.Number == note)) return;

        MutateWithUndo(e =>
        {
            foreach (var m in e.Project.Mappings)
            {
                if (m.Target != target || m.Mode is not (MappingMode.Trigger or MappingMode.Gate)) continue;
                m.Source = m.Source with { Number = note };
            }
        });
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

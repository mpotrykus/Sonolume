using Sonolume.Engine.Core;
using Sonolume.Engine.Effects;
using Sonolume.Engine.Input;
using Sonolume.Engine.Mappings;
using Sonolume.Engine.Model;
using Sonolume.Engine.Render;

namespace Sonolume.Engine;

/// <summary>
/// The Sonolume engine facade: MIDI in, frames out. Not thread-safe; <see cref="EngineRunner"/> owns the thread.
/// Pipeline: MidiEvent -> ControlEvent -> MappingEngine -> Compositor (params, effects) -> Frame.
/// </summary>
public sealed class Engine
{
    private readonly MappingAction[] scratch = new MappingAction[128];
    private MappingEngine mappingEngine;
    private Compositor compositor;
    private ulong seq;
    private bool layoutDirty = true;
    private LearnRequest? learn;
    private int learnCounter;

    public Engine(Project project, EffectRegistry? effects = null, string? instanceId = null)
    {
        Effects = effects ?? EffectRegistry.CreateDefault();
        InstanceId = instanceId ?? Guid.NewGuid().ToString("N")[..8];
        Project = project;
        mappingEngine = new MappingEngine(project.Mappings);
        compositor = new Compositor(project, Effects);
    }

    public string InstanceId { get; }

    public EffectRegistry Effects { get; }

    public Project Project { get; private set; }

    public bool LayoutChanged => layoutDirty;

    public bool IsLearning => learn is not null;

    public event Action<Mapping>? MappingLearned;

    public void LoadProject(Project project)
    {
        Project = project;
        RebuildRuntime();
    }

    public void AddZone(Zone zone)
    {
        Project.AddZone(zone);
        RebuildRuntime();
    }

    public void RemoveZone(string id)
    {
        if (Project.RemoveZone(id)) RebuildRuntime();
    }

    public void UpdateZone(string id, Action<Zone> apply)
    {
        Project.UpdateZone(id, apply);
        RebuildRuntime();
    }

    public void AddGroup(Group group)
    {
        Project.AddGroup(group);
        RebuildRuntime();
    }

    public void RemoveGroup(string id)
    {
        if (Project.RemoveGroup(id)) RebuildRuntime();
    }

    public void UpdateGroup(string id, Action<Group> apply)
    {
        Project.UpdateGroup(id, apply);
        RebuildRuntime();
    }

    public void RenameProject(string name)
    {
        Project.Name = name;
        layoutDirty = true;
    }

    public void Push(in MidiEvent e)
    {
        if (MidiInterpreter.TryInterpret(e, out var control)) PushControl(control);
    }

    public void PushControl(in ControlEvent e)
    {
        if (learn is { } request && MidiLearn.Accepts(request, e))
        {
            var mapping = MidiLearn.CreateMapping(request, e, $"learned-{++learnCounter}-{e.Source.Kind}-{e.Source.Number}");
            learn = null;
            AddMapping(mapping);
            MappingLearned?.Invoke(mapping);
            return;
        }

        int count = mappingEngine.Resolve(e, scratch);
        for (int i = 0; i < count; i++) Apply(scratch[i]);
    }

    private void Apply(in MappingAction action)
    {
        var m = action.Mapping;
        switch (m.Mode)
        {
            case MappingMode.Set:
                compositor.SetParamNormalized(m.Target, m.Param, action.Value01);
                break;
            case MappingMode.Trigger:
                Trigger(m, action);
                break;
            case MappingMode.Gate:
                if (action.Event.Type == ControlEventType.Trigger) Trigger(m, action);
                else compositor.Release(m.Target);
                break;
        }
    }

    private void Trigger(Mapping m, in MappingAction action)
    {
        string effectId = m.EffectId ?? FlashEffect.TypeName;
        if (!Effects.Contains(effectId)) effectId = FlashEffect.TypeName;
        var info = new TriggerInfo(action.Value01, action.Event.Source.Number, action.Event.Value01, action.Event.TimestampTicks);
        compositor.Trigger(m.Target, effectId, info);
    }

    /// <summary>Advances time. Returns true when any zone changed since the last <see cref="TakeFrame"/>.</summary>
    public bool Tick(float dt) => compositor.Update(dt);

    public Layout GetLayout()
    {
        layoutDirty = false;
        var zones = new LayoutZone[Project.Zones.Count];
        for (int i = 0; i < zones.Length; i++)
        {
            var z = Project.Zones[i];
            zones[i] = new LayoutZone(i, z.Id, z.Name, z.Rect, Math.Max(1, z.CellsW), Math.Max(1, z.CellsH));
        }
        return new Layout(InstanceId, Project.Name, zones);
    }

    /// <summary>Changed regions since the last call, or every region when <paramref name="full"/>. Null when nothing changed.</summary>
    public Frame? TakeFrame(bool full = false)
    {
        var regions = compositor.Collect(full);
        if (regions.Count == 0) return null;
        return new Frame(++seq, Clock.Now(), regions, full);
    }

    public void SetParam(TargetRef target, ParamId id, float raw) => compositor.SetParam(target, id, raw);

    public void AddMapping(Mapping mapping)
    {
        Project.Mappings.Add(mapping);
        RebuildMappings();
    }

    public bool RemoveMapping(string id)
    {
        int removed = Project.Mappings.RemoveAll(m => m.Id == id);
        if (removed > 0) RebuildMappings();
        return removed > 0;
    }

    public void BeginLearn(LearnRequest request) => learn = request;

    public void CancelLearn() => learn = null;

    public EngineSnapshot Snapshot()
    {
        var mappings = new MappingSnapshot[Project.Mappings.Count];
        for (int i = 0; i < mappings.Length; i++)
        {
            var m = Project.Mappings[i];
            mappings[i] = new MappingSnapshot(m.Id, m.Source.ToString(), m.Target.ToString(), m.Param.ToString(), m.Mode.ToString(), m.EffectId, m.Enabled);
        }
        return new EngineSnapshot(Project.Name, compositor.SnapshotZones(), mappings, IsLearning, Clock.Now());
    }

    private void RebuildMappings()
    {
        mappingEngine = new MappingEngine(Project.Mappings);
        compositor.RefreshEventDriven(Project.Mappings);
    }

    private void RebuildRuntime()
    {
        mappingEngine = new MappingEngine(Project.Mappings);
        compositor = new Compositor(Project, Effects);
        layoutDirty = true;
    }
}

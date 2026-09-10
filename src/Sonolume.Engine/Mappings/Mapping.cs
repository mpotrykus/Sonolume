using Sonolume.Engine.Input;
using Sonolume.Engine.Model;

namespace Sonolume.Engine.Mappings;

public enum MappingMode
{
    /// <summary>Persistent: the source value sets the target parameter (CC 7 sets brightness).</summary>
    Set,
    /// <summary>One-shot: a trigger starts the effect; release is ignored (note on flashes the kick).</summary>
    Trigger,
    /// <summary>Gated: trigger starts the effect, release ends it.</summary>
    Gate,
    /// <summary>Effect type: a continuous CC repoints the target's Trigger/Gate mapping(s) at whichever effect
    /// its current value lands on (the registered effects divided into equal slices of 0..1, see
    /// <see cref="Engine.Engine.SelectEffect"/>), live - it never renders anything itself.</summary>
    Select,
}

public enum TargetKind
{
    Zone,
    Group,
}

public sealed record TargetRef(TargetKind Kind, string Id)
{
    public static TargetRef Zone(string id) => new(TargetKind.Zone, id);

    public static TargetRef Group(string id) => new(TargetKind.Group, id);

    public override string ToString() => $"{Kind} {Id}";
}

/// <summary>Source -> transform -> parameter on a zone or group. The heart of Sonolume.</summary>
public sealed class Mapping
{
    public string Id { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public SourceAddress Source { get; set; }
    public TargetRef Target { get; set; } = TargetRef.Zone("");
    public ParamId Param { get; set; } = ParamId.Brightness;
    public MappingMode Mode { get; set; } = MappingMode.Set;
    /// <summary>Effect started by Trigger/Gate mappings. Ignored for Set and Select.</summary>
    public string? EffectId { get; set; }
    public Transform Transform { get; set; } = Transform.Identity;

    // Set/Select also accept Trigger: a MIDI note (see DefaultMacros) has no Set-typed event of its own - pressing
    // it IS the "key = parameter, velocity = value" gesture, carried as a Trigger with Value01 = velocity. CC,
    // pitch bend, and aftertouch keep producing native Set events, so this doesn't change how they're handled.
    public bool AcceptsEventType(ControlEventType type) => Mode switch
    {
        MappingMode.Set => type is ControlEventType.Set or ControlEventType.Trigger,
        MappingMode.Trigger => type == ControlEventType.Trigger,
        MappingMode.Gate => type is ControlEventType.Trigger or ControlEventType.Release,
        MappingMode.Select => type is ControlEventType.Set or ControlEventType.Trigger,
        _ => false,
    };
}

namespace Sonolume.Engine.Input;

public enum ControlEventType : byte
{
    /// <summary>A momentary event (note on). Starts effects.</summary>
    Trigger,
    /// <summary>The end of a gated event (note off).</summary>
    Release,
    /// <summary>A persistent value (CC, pitch bend, aftertouch, automation).</summary>
    Set,
}

/// <summary>A source-agnostic control event. Everything downstream of MIDI interpretation consumes these.</summary>
public readonly record struct ControlEvent(SourceAddress Source, ControlEventType Type, float Value01, long TimestampTicks);

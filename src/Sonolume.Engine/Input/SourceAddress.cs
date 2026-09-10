namespace Sonolume.Engine.Input;

public enum SourceKind : byte
{
    MidiNote,
    MidiCC,
    MidiPitchBend,
    MidiChannelAftertouch,
    MidiPolyAftertouch,

    /// <summary>Legacy: host-automation macro parameters, before the fixed per-zone/group convention moved to
    /// plain MIDI notes (see DefaultMacros). Nothing produces this anymore - kept only so a project saved under
    /// the old convention still deserializes instead of throwing; DefaultMacros.Sync replaces any mapping sourced
    /// this way with the current note convention on load.</summary>
    HostMacro,
}

/// <summary>
/// Identifies a control source. In a mapping, Channel and Number may be <see cref="Any"/> to act as wildcards.
/// Future input kinds (OSC, audio) add enum members only.
/// </summary>
public readonly record struct SourceAddress(SourceKind Kind, int Channel, int Number)
{
    public const int Any = -1;

    public bool Matches(in SourceAddress concrete) =>
        Kind == concrete.Kind
        && (Channel == Any || Channel == concrete.Channel)
        && (Number == Any || Number == concrete.Number);

    public static SourceAddress Note(int number, int channel = Any) => new(SourceKind.MidiNote, channel, number);

    public static SourceAddress CC(int number, int channel = Any) => new(SourceKind.MidiCC, channel, number);

    public static SourceAddress PitchBend(int channel = Any) => new(SourceKind.MidiPitchBend, channel, 0);

    public static SourceAddress ChannelAftertouch(int channel = Any) => new(SourceKind.MidiChannelAftertouch, channel, 0);

    public override string ToString()
    {
        string ch = Channel == Any ? "*" : (Channel + 1).ToString();
        return Kind switch
        {
            SourceKind.MidiNote => $"{NoteName(Number)} ch{ch}",
            SourceKind.MidiCC => $"CC {Number} ch{ch}",
            SourceKind.MidiPitchBend => $"Pitch bend ch{ch}",
            SourceKind.MidiChannelAftertouch => $"Aftertouch ch{ch}",
            SourceKind.MidiPolyAftertouch => $"Poly aftertouch {NoteName(Number)} ch{ch}",
            SourceKind.HostMacro => $"Macro {Number + 1}",
            _ => $"{Kind} {Number} ch{ch}",
        };
    }

    private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    // Middle C (note 60) is C4, the common DAW/MIDI convention (as opposed to the C3 used by some Yamaha gear).
    private static string NoteName(int number) => $"{NoteNames[((number % 12) + 12) % 12]}{number / 12 - 1}";
}

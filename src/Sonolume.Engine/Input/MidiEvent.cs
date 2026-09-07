namespace Sonolume.Engine.Input;

public enum MidiEventKind : byte
{
    NoteOn,
    NoteOff,
    ControlChange,
    PitchBend,
    ChannelAftertouch,
    PolyAftertouch,
}

/// <summary>
/// A raw MIDI message. Channel is 0..15. Data1 is the note or controller number.
/// Data2 always carries the value at 14-bit resolution (0..16383) so velocity, CC, and pitch bend share one scale.
/// </summary>
public readonly record struct MidiEvent(MidiEventKind Kind, byte Channel, byte Data1, ushort Data2, long TimestampTicks)
{
    public const ushort MaxValue14 = 16383;

    public float Value01 => Data2 / (float)MaxValue14;

    public static ushort From7Bit(int value) => (ushort)(Math.Clamp(value, 0, 127) * 129);

    public static ushort FromUnit(float value) => (ushort)MathF.Round(Math.Clamp(value, 0f, 1f) * MaxValue14);

    public static MidiEvent NoteOn(int channel, int note, float velocity01, long timestampTicks) =>
        new(MidiEventKind.NoteOn, (byte)channel, (byte)note, FromUnit(velocity01), timestampTicks);

    public static MidiEvent NoteOff(int channel, int note, float velocity01, long timestampTicks) =>
        new(MidiEventKind.NoteOff, (byte)channel, (byte)note, FromUnit(velocity01), timestampTicks);

    public static MidiEvent ControlChange(int channel, int controller, int value7, long timestampTicks) =>
        new(MidiEventKind.ControlChange, (byte)channel, (byte)controller, From7Bit(value7), timestampTicks);

    public static MidiEvent PitchBend(int channel, int value14, long timestampTicks) =>
        new(MidiEventKind.PitchBend, (byte)channel, 0, (ushort)Math.Clamp(value14, 0, MaxValue14), timestampTicks);

    public static MidiEvent ChannelAftertouch(int channel, int pressure7, long timestampTicks) =>
        new(MidiEventKind.ChannelAftertouch, (byte)channel, 0, From7Bit(pressure7), timestampTicks);

    public static MidiEvent PolyAftertouch(int channel, int note, int pressure7, long timestampTicks) =>
        new(MidiEventKind.PolyAftertouch, (byte)channel, (byte)note, From7Bit(pressure7), timestampTicks);
}

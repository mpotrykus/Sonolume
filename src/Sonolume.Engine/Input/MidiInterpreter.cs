namespace Sonolume.Engine.Input;

public static class MidiInterpreter
{
    public static bool TryInterpret(in MidiEvent e, out ControlEvent result)
    {
        switch (e.Kind)
        {
            case MidiEventKind.NoteOn when e.Data2 == 0:
            case MidiEventKind.NoteOff:
                result = new ControlEvent(new SourceAddress(SourceKind.MidiNote, e.Channel, e.Data1), ControlEventType.Release, e.Value01, e.TimestampTicks);
                return true;
            case MidiEventKind.NoteOn:
                result = new ControlEvent(new SourceAddress(SourceKind.MidiNote, e.Channel, e.Data1), ControlEventType.Trigger, e.Value01, e.TimestampTicks);
                return true;
            case MidiEventKind.ControlChange:
                result = new ControlEvent(new SourceAddress(SourceKind.MidiCC, e.Channel, e.Data1), ControlEventType.Set, e.Value01, e.TimestampTicks);
                return true;
            case MidiEventKind.PitchBend:
                result = new ControlEvent(new SourceAddress(SourceKind.MidiPitchBend, e.Channel, 0), ControlEventType.Set, e.Value01, e.TimestampTicks);
                return true;
            case MidiEventKind.ChannelAftertouch:
                result = new ControlEvent(new SourceAddress(SourceKind.MidiChannelAftertouch, e.Channel, 0), ControlEventType.Set, e.Value01, e.TimestampTicks);
                return true;
            case MidiEventKind.PolyAftertouch:
                result = new ControlEvent(new SourceAddress(SourceKind.MidiPolyAftertouch, e.Channel, e.Data1), ControlEventType.Set, e.Value01, e.TimestampTicks);
                return true;
            default:
                result = default;
                return false;
        }
    }
}

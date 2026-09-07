using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using Sonolume.Engine.Core;
using SonoMidi = Sonolume.Engine.Input.MidiEvent;

namespace Sonolume.Standalone;

/// <summary>Opens one Windows MIDI input and converts DryWetMIDI events into engine events.</summary>
public sealed class MidiInputBridge : IDisposable
{
    private readonly Action<SonoMidi> sink;
    private InputDevice? device;

    public MidiInputBridge(Action<SonoMidi> sink) => this.sink = sink;

    public static IReadOnlyList<string> DeviceNames()
    {
        var names = new List<string>();
        foreach (var d in InputDevice.GetAll())
        {
            names.Add(d.Name);
            d.Dispose();
        }
        return names;
    }

    public void Open(string name)
    {
        Close();
        var d = InputDevice.GetByName(name);
        d.EventReceived += OnEvent;
        d.StartEventsListening();
        device = d;
    }

    public void Close()
    {
        if (device is null) return;
        device.EventReceived -= OnEvent;
        try { device.StopEventsListening(); } catch { }
        device.Dispose();
        device = null;
    }

    private void OnEvent(object? sender, MidiEventReceivedEventArgs e)
    {
        long ts = Clock.Now();
        switch (e.Event)
        {
            case NoteOnEvent n when n.Velocity == 0:
                sink(SonoMidi.NoteOff(n.Channel, n.NoteNumber, 0f, ts));
                break;
            case NoteOnEvent n:
                sink(SonoMidi.NoteOn(n.Channel, n.NoteNumber, n.Velocity / 127f, ts));
                break;
            case NoteOffEvent n:
                sink(SonoMidi.NoteOff(n.Channel, n.NoteNumber, n.Velocity / 127f, ts));
                break;
            case ControlChangeEvent cc:
                sink(SonoMidi.ControlChange(cc.Channel, cc.ControlNumber, cc.ControlValue, ts));
                break;
            case PitchBendEvent pb:
                sink(SonoMidi.PitchBend(pb.Channel, pb.PitchValue, ts));
                break;
            case ChannelAftertouchEvent at:
                sink(SonoMidi.ChannelAftertouch(at.Channel, at.AftertouchValue, ts));
                break;
            case NoteAftertouchEvent pat:
                sink(SonoMidi.PolyAftertouch(pat.Channel, pat.NoteNumber, pat.AftertouchValue, ts));
                break;
        }
    }

    public void Dispose() => Close();
}

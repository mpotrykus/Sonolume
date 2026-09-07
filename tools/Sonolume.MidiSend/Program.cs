using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

// Usage: Sonolume.MidiSend [--port "loopMIDI Port"] [--note 36] [--velocity 127] [--count 8] [--interval 500] [--list]
var args2 = new Queue<string>(args);
string? port = null;
int note = 36, velocity = 127, count = 8, intervalMs = 500;
bool list = false;
while (args2.Count > 0)
{
    var a = args2.Dequeue();
    switch (a)
    {
        case "--port": port = args2.Dequeue(); break;
        case "--note": note = int.Parse(args2.Dequeue()); break;
        case "--velocity": velocity = int.Parse(args2.Dequeue()); break;
        case "--count": count = int.Parse(args2.Dequeue()); break;
        case "--interval": intervalMs = int.Parse(args2.Dequeue()); break;
        case "--list": list = true; break;
        default: Console.Error.WriteLine($"Unknown argument {a}"); return 2;
    }
}

var outputs = OutputDevice.GetAll().ToList();
if (list || port is null)
{
    Console.WriteLine("MIDI outputs:");
    foreach (var d in outputs) Console.WriteLine("  " + d.Name);
    if (list) return 0;
    port = outputs.FirstOrDefault(d => d.Name.Contains("loopMIDI", StringComparison.OrdinalIgnoreCase))?.Name
        ?? outputs.FirstOrDefault()?.Name;
    if (port is null) { Console.Error.WriteLine("No MIDI output found."); return 1; }
}

using var device = OutputDevice.GetByName(port);
Console.WriteLine($"Sending {count} x note {note} (velocity {velocity}) to '{port}' every {intervalMs} ms");
for (int i = 0; i < count; i++)
{
    int vel = velocity;
    device.SendEvent(new NoteOnEvent((SevenBitNumber)note, (SevenBitNumber)vel));
    Thread.Sleep(Math.Min(60, intervalMs / 2));
    device.SendEvent(new NoteOffEvent((SevenBitNumber)note, (SevenBitNumber)0));
    Thread.Sleep(Math.Max(0, intervalMs - Math.Min(60, intervalMs / 2)));
}
return 0;

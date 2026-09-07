namespace Sonolume.Engine.Input;

/// <summary>
/// Single-producer, single-consumer lock-free ring buffer. The producer is the audio/MIDI callback thread,
/// the consumer is the engine thread. Capacity is rounded up to a power of two.
/// </summary>
public sealed class MidiRingBuffer
{
    private readonly MidiEvent[] buffer;
    private readonly int mask;
    private long head;
    private long tail;

    public MidiRingBuffer(int capacity)
    {
        int size = 1;
        while (size < capacity) size <<= 1;
        buffer = new MidiEvent[size];
        mask = size - 1;
    }

    public int Capacity => buffer.Length;

    public bool TryEnqueue(in MidiEvent e)
    {
        long h = Volatile.Read(ref head);
        if (h - Volatile.Read(ref tail) >= buffer.Length) return false;
        buffer[h & mask] = e;
        Volatile.Write(ref head, h + 1);
        return true;
    }

    public bool TryDequeue(out MidiEvent e)
    {
        long t = Volatile.Read(ref tail);
        if (t == Volatile.Read(ref head))
        {
            e = default;
            return false;
        }
        e = buffer[t & mask];
        Volatile.Write(ref tail, t + 1);
        return true;
    }
}

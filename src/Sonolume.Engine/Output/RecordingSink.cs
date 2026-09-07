using Sonolume.Engine.Render;

namespace Sonolume.Engine.Output;

/// <summary>Test double: records everything it is given.</summary>
public sealed class RecordingSink : IFrameSink
{
    private readonly object gate = new();

    public List<Layout> Layouts { get; } = new();
    public List<Frame> Frames { get; } = new();
    public int Clears { get; private set; }

    public SinkStatus Status
    {
        get { lock (gate) return new SinkStatus(SinkState.Connected, null, Frames.Count, 0, 0); }
    }

    public void SendLayout(Layout layout)
    {
        lock (gate) Layouts.Add(layout);
    }

    public void SendFrame(Frame frame)
    {
        lock (gate) Frames.Add(frame);
    }

    public void SendClear()
    {
        lock (gate) Clears++;
    }

    public void Dispose()
    {
    }
}

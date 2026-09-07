using Sonolume.Engine.Render;

namespace Sonolume.Engine.Output;

public enum SinkState
{
    Idle,
    Connected,
    Offline,
    Error,
}

public sealed record SinkStatus(SinkState State, string? Message, long FramesSent, long FramesDropped, double LastSendMs)
{
    public static readonly SinkStatus Initial = new(SinkState.Idle, null, 0, 0, 0);
}

/// <summary>Where frames go. SignalRGB in production, a recorder in tests. Implementations must not block the caller.</summary>
public interface IFrameSink : IDisposable
{
    void SendLayout(Layout layout);

    void SendFrame(Frame frame);

    void SendClear();

    SinkStatus Status { get; }
}

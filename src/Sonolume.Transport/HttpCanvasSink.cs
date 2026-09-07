using System.Diagnostics;
using System.Net;
using Sonolume.Engine.Output;
using Sonolume.Engine.Render;

namespace Sonolume.Transport;

public sealed record HttpCanvasSinkOptions(
    string InstanceId,
    string Endpoint = "http://localhost:16034/canvas/event",
    string Sender = "Sonolume",
    int RequestTimeoutMs = 250,
    int OfflineRetryMs = 1000);

/// <summary>
/// Sends encoded frames to SignalRGB's Canvas API on a dedicated thread over one keep-alive connection.
/// Frames are latest-wins: if SignalRGB stalls, intermediate frames are dropped rather than queued.
/// Layout and clear messages are queued in order and always delivered before the pending frame.
/// </summary>
public sealed class HttpCanvasSink : IFrameSink
{
    private readonly HttpCanvasSinkOptions options;
    private readonly HttpClient client;
    private readonly string urlPrefix;
    private readonly Thread thread;
    private readonly AutoResetEvent wake = new(false);
    private readonly Queue<string> control = new();
    private readonly object controlGate = new();
    private string? pendingFrame;
    private volatile bool running = true;
    private volatile SinkStatus status = SinkStatus.Initial;
    private long framesSent;
    private long framesDropped;
    private long lastFailureTicks = long.MinValue;

    public HttpCanvasSink(HttpCanvasSinkOptions options)
    {
        this.options = options;
        var handler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
            PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
            MaxConnectionsPerServer = 2,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.None,
        };
        client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(options.RequestTimeoutMs) };
        urlPrefix = $"{options.Endpoint}?sender={Uri.EscapeDataString(options.Sender)}&event=";

        thread = new Thread(Run) { Name = "Sonolume.Sender", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    public SinkStatus Status => status;

    public void SendLayout(Layout layout) => EnqueueControl(FrameEncoder.EncodeLayout(layout));

    public void SendClear() => EnqueueControl(FrameEncoder.EncodeClear(options.InstanceId));

    public void SendFrame(Frame frame)
    {
        string encoded = FrameEncoder.EncodeFrame(frame, options.InstanceId);
        if (Interlocked.Exchange(ref pendingFrame, encoded) is not null) Interlocked.Increment(ref framesDropped);
        wake.Set();
    }

    private void EnqueueControl(string payload)
    {
        lock (controlGate) control.Enqueue(payload);
        wake.Set();
    }

    public void Dispose()
    {
        if (!running) return;
        SendClear();
        running = false;
        wake.Set();
        thread.Join(1000);
        client.Dispose();
        wake.Dispose();
    }

    private void Run()
    {
        while (running || HasPendingControl())
        {
            if (running) wake.WaitOne(250);

            while (TryDequeueControl(out var payload)) Send(payload);

            var frame = Interlocked.Exchange(ref pendingFrame, null);
            if (frame is not null)
            {
                if (IsBackingOff()) Interlocked.Increment(ref framesDropped);
                else Send(frame);
            }
        }
    }

    private bool HasPendingControl()
    {
        lock (controlGate) return control.Count > 0;
    }

    private bool TryDequeueControl(out string payload)
    {
        lock (controlGate) return control.TryDequeue(out payload!);
    }

    private bool IsBackingOff()
    {
        long last = lastFailureTicks;
        return last != long.MinValue && Stopwatch.GetElapsedTime(last).TotalMilliseconds < options.OfflineRetryMs;
    }

    private void Send(string payload)
    {
        var url = urlPrefix + Uri.EscapeDataString(payload);
        long start = Stopwatch.GetTimestamp();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = client.Send(request, HttpCompletionOption.ResponseHeadersRead);
            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (response.IsSuccessStatusCode)
            {
                lastFailureTicks = long.MinValue;
                Interlocked.Increment(ref framesSent);
                status = new SinkStatus(SinkState.Connected, null, framesSent, framesDropped, ms);
            }
            else
            {
                lastFailureTicks = Stopwatch.GetTimestamp();
                status = new SinkStatus(SinkState.Error, $"SignalRGB answered HTTP {(int)response.StatusCode}", framesSent, framesDropped, ms);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            lastFailureTicks = Stopwatch.GetTimestamp();
            status = new SinkStatus(SinkState.Offline, "SignalRGB not reachable on " + options.Endpoint, framesSent, framesDropped, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }
}

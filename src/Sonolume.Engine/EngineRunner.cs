using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Sonolume.Engine.Core;
using Sonolume.Engine.Input;
using Sonolume.Engine.Output;
using Sonolume.Engine.Render;

namespace Sonolume.Engine;

public sealed record EngineRunnerOptions(
    double TickHz = 120,
    double HeartbeatSeconds = 1.0,
    double LayoutResendSeconds = 10.0,
    int QueueCapacity = 1024,
    double SnapshotIntervalSeconds = 1.0 / 30);

/// <summary>
/// Owns the engine thread. MIDI arrives from the audio thread via a lock-free queue and wakes the thread
/// immediately; between events the thread ticks at a fixed rate so envelopes keep animating.
/// Frames go to the sink only when something changed, plus a periodic full-frame heartbeat.
/// </summary>
public sealed class EngineRunner : IDisposable
{
    private readonly Engine engine;
    private readonly IFrameSink sink;
    private readonly EngineRunnerOptions options;
    private readonly MidiRingBuffer queue;
    private readonly AutoResetEvent wake = new(false);
    private readonly ConcurrentQueue<Action<Engine>> actions = new();
    private Thread? thread;
    private volatile bool running;
    private volatile EngineSnapshot? snapshot;
    private long droppedEvents;
    private long processedEvents;

    public EngineRunner(Engine engine, IFrameSink sink, EngineRunnerOptions? options = null)
    {
        this.engine = engine;
        this.sink = sink;
        this.options = options ?? new EngineRunnerOptions();
        queue = new MidiRingBuffer(this.options.QueueCapacity);
    }

    public Engine Engine => engine;

    public IFrameSink Sink => sink;

    /// <summary>Latest state for UI. Never null after the first tick.</summary>
    public EngineSnapshot? LatestSnapshot => snapshot;

    public long DroppedEvents => Interlocked.Read(ref droppedEvents);

    public long ProcessedEvents => Interlocked.Read(ref processedEvents);

    public bool IsRunning => running;

    /// <summary>Audio-thread safe: no allocation, no locks. Must be called from one producer thread at a time.</summary>
    public void Enqueue(in MidiEvent e)
    {
        if (!queue.TryEnqueue(e)) Interlocked.Increment(ref droppedEvents);
        wake.Set();
    }

    /// <summary>Runs an action on the engine thread. Use for UI edits (parameters, mappings, project load).</summary>
    public void Post(Action<Engine> action)
    {
        actions.Enqueue(action);
        wake.Set();
    }

    /// <summary>Runs a function on the engine thread and waits for the result.</summary>
    public T Invoke<T>(Func<Engine, T> func, int timeoutMs = 1000)
    {
        if (!running || Thread.CurrentThread == thread) return func(engine);

        using var done = new ManualResetEventSlim(false);
        T? result = default;
        Exception? error = null;
        Post(e =>
        {
            try { result = func(e); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        });
        if (!done.Wait(timeoutMs)) throw new TimeoutException("Engine thread did not respond.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
        return result!;
    }

    /// <summary>Runs an action on the engine thread and waits for it to finish. Use for edits that can throw
    /// on validation (zone/group CRUD) so the original exception reaches the caller unwrapped.</summary>
    public void Invoke(Action<Engine> action, int timeoutMs = 1000)
    {
        if (!running || Thread.CurrentThread == thread) { action(engine); return; }

        using var done = new ManualResetEventSlim(false);
        Exception? error = null;
        Post(e =>
        {
            try { action(e); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        });
        if (!done.Wait(timeoutMs)) throw new TimeoutException("Engine thread did not respond.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    public void Start()
    {
        if (running) return;
        running = true;
        thread = new Thread(Run)
        {
            Name = "Sonolume.Engine",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
        };
        thread.Start();
    }

    public void Stop()
    {
        if (!running) return;
        running = false;
        wake.Set();
        thread?.Join(2000);
        thread = null;
    }

    public void Dispose()
    {
        Stop();
        wake.Dispose();
    }

    private void Run()
    {
        using var _ = HighResolutionTimer.Acquire();

        long tickPeriod = Clock.SecondsToTicks(1.0 / options.TickHz);
        long heartbeat = Clock.SecondsToTicks(options.HeartbeatSeconds);
        long layoutResend = Clock.SecondsToTicks(options.LayoutResendSeconds);
        long snapshotInterval = Clock.SecondsToTicks(options.SnapshotIntervalSeconds);

        long now = Clock.Now();
        long lastTick = now;
        long nextTick = now + tickPeriod;
        long lastFull = now - heartbeat;
        long lastLayout = now;
        long lastSnapshot = now;

        sink.SendLayout(engine.GetLayout());
        snapshot = engine.Snapshot();

        while (running)
        {
            long waitTicks = nextTick - Clock.Now();
            if (waitTicks > 0)
            {
                int waitMs = (int)Math.Max(1, Math.Round(Clock.TicksToMilliseconds(waitTicks)));
                wake.WaitOne(waitMs);
            }
            if (!running) break;

            now = Clock.Now();
            float dt = (float)Clock.TicksToSeconds(now - lastTick);
            lastTick = now;

            bool activity = false;
            while (actions.TryDequeue(out var action))
            {
                action(engine);
                activity = true;
            }
            while (queue.TryDequeue(out var midi))
            {
                engine.Push(midi);
                Interlocked.Increment(ref processedEvents);
                activity = true;
            }

            bool changed = engine.Tick(dt);

            if (engine.LayoutChanged || now - lastLayout >= layoutResend)
            {
                sink.SendLayout(engine.GetLayout());
                lastLayout = now;
                lastFull = now - heartbeat;
            }

            bool full = now - lastFull >= heartbeat;
            var frame = engine.TakeFrame(full);
            if (frame is not null)
            {
                sink.SendFrame(frame);
                if (frame.IsFull) lastFull = now;
            }

            if (changed || activity || frame is not null || now - lastSnapshot >= snapshotInterval)
            {
                snapshot = engine.Snapshot();
                lastSnapshot = now;
            }

            nextTick += tickPeriod;
            if (nextTick < now) nextTick = now + tickPeriod;
        }

        sink.SendClear();
    }

    /// <summary>
    /// Windows wakes waiting threads on a 15.6 ms timer unless some process has raised the system timer
    /// resolution. A 120 Hz tick needs 1 ms, so hold it for the lifetime of the engine thread.
    /// </summary>
    private sealed class HighResolutionTimer : IDisposable
    {
        private readonly bool acquired;

        private HighResolutionTimer(bool acquired) => this.acquired = acquired;

        public static HighResolutionTimer Acquire()
        {
            if (!OperatingSystem.IsWindows()) return new HighResolutionTimer(false);
            try { return new HighResolutionTimer(timeBeginPeriod(1) == 0); }
            catch (DllNotFoundException) { return new HighResolutionTimer(false); }
            catch (EntryPointNotFoundException) { return new HighResolutionTimer(false); }
        }

        public void Dispose()
        {
            if (acquired && OperatingSystem.IsWindows()) timeEndPeriod(1);
        }

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint period);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint period);
    }
}

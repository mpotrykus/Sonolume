using Sonolume.Engine.Input;
using Sonolume.Engine.Model;
using Sonolume.Engine.Output;
using Xunit;

namespace Sonolume.Engine.Tests;

public class EngineRunnerTests
{
    [Fact]
    public async Task SingleHit_ProducesDecayFramesThenOnlyHeartbeats()
    {
        var sink = new RecordingSink();
        var engine = new Engine(Project.CreateDefault(), instanceId: "runner01");
        using var runner = new EngineRunner(engine, sink, new EngineRunnerOptions(TickHz: 120, HeartbeatSeconds: 0.5));
        runner.Start();

        await Task.Delay(100);
        Assert.Single(sink.Layouts);
        Assert.NotNull(runner.LatestSnapshot);

        runner.Enqueue(MidiEvent.NoteOn(0, 36, 1f, 0));
        await Task.Delay(600);

        int partialFrames;
        lock (sink) partialFrames = sink.Frames.Count(f => !f.IsFull);
        Assert.InRange(partialFrames, 10, 80);

        int before;
        lock (sink) before = sink.Frames.Count;
        await Task.Delay(1100);
        int after;
        lock (sink) after = sink.Frames.Count;
        int heartbeats = after - before;
        Assert.InRange(heartbeats, 1, 4);

        runner.Stop();
        Assert.Equal(1, sink.Clears);
        Assert.Equal(1, runner.ProcessedEvents);
        Assert.Equal(0, runner.DroppedEvents);
    }

    [Fact]
    public void Invoke_RunsOnEngineThread()
    {
        var sink = new RecordingSink();
        using var runner = new EngineRunner(new Engine(Project.CreateDefault()), sink);
        runner.Start();
        var name = runner.Invoke(e => e.Project.Name);
        Assert.Equal("Default Kit", name);
    }

    [Fact]
    public void VoidInvoke_RunsOnEngineThread()
    {
        var sink = new RecordingSink();
        using var runner = new EngineRunner(new Engine(Project.CreateDefault()), sink);
        runner.Start();
        runner.Invoke(e => e.RenameProject("Renamed"));
        Assert.Equal("Renamed", runner.Invoke(e => e.Project.Name));
    }

    [Fact]
    public void Invoke_PropagatesOriginalExceptionUnwrapped()
    {
        var sink = new RecordingSink();
        using var runner = new EngineRunner(new Engine(Project.CreateDefault()), sink);
        runner.Start();

        var ex = Assert.Throws<InvalidDataException>(() => runner.Invoke(e => e.AddZone(new Zone { Id = "kick" })));
        Assert.Contains("kick", ex.Message);
    }

    [Fact]
    public void RingBuffer_IsFifoAndReportsFull()
    {
        var ring = new MidiRingBuffer(4);
        Assert.Equal(4, ring.Capacity);
        for (int i = 0; i < 4; i++) Assert.True(ring.TryEnqueue(MidiEvent.NoteOn(0, i, 1f, i)));
        Assert.False(ring.TryEnqueue(MidiEvent.NoteOn(0, 99, 1f, 99)));
        for (int i = 0; i < 4; i++)
        {
            Assert.True(ring.TryDequeue(out var e));
            Assert.Equal(i, e.Data1);
        }
        Assert.False(ring.TryDequeue(out _));
    }
}

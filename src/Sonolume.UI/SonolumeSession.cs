using Sonolume.Engine;
using Sonolume.Engine.Input;
using Sonolume.Engine.Model;
using Sonolume.Persist;
using Sonolume.Transport;

namespace Sonolume.UI;

/// <summary>Composition root shared by the plugin and the standalone app: engine + runner thread + SignalRGB sink.</summary>
public sealed class SonolumeSession : IDisposable
{
    public SonolumeSession(Project? project = null, HttpCanvasSinkOptions? sinkOptions = null)
    {
        InstanceId = Guid.NewGuid().ToString("N")[..8];
        Engine = new Engine.Engine(project ?? Project.CreateDefault(), instanceId: InstanceId);
        Sink = new HttpCanvasSink(sinkOptions ?? new HttpCanvasSinkOptions(InstanceId));
        Runner = new EngineRunner(Engine, Sink);
        Runner.Start();
    }

    public string InstanceId { get; }

    public Engine.Engine Engine { get; }

    public HttpCanvasSink Sink { get; }

    public EngineRunner Runner { get; }

    public void Enqueue(in MidiEvent e) => Runner.Enqueue(e);

    public string ExportProjectJson() => Runner.Invoke(e => ProjectJson.Serialize(e.Project));

    /// <summary>Throws on invalid JSON before anything reaches the engine thread.</summary>
    public void ImportProjectJson(string json)
    {
        var project = ProjectJson.Deserialize(json);
        Runner.Post(e => e.LoadProject(project));
    }

    public void SetDecaySeconds(float seconds) => Runner.Post(e => e.SetParamAllZones(ParamId.EffectDecay, seconds));

    public void Dispose()
    {
        Runner.Dispose();
        Sink.Dispose();
    }
}

using System.Text.Json;

using AudioPlugSharp;
using AudioPlugSharpWPF;
using Sonolume.Engine.Core;
using Sonolume.Engine.Input;
using Sonolume.UI;

namespace Sonolume.Plugin;

/// <summary>
/// VST3 shell. Receives MIDI from the host, hands it to the engine thread, exposes the shared WPF view,
/// and stores the project JSON in the host's plugin state. No audio is produced; a silent stereo output keeps
/// hosts that expect instruments to have outputs happy.
/// </summary>
public sealed class SonolumePlugin : AudioPluginWPF
{
    private SonolumeSession? session;
    private string? pendingProjectJson;
    private DoubleAudioIOPort? output;

    public SonolumePlugin()
    {
        Company = "Sonolume";
        Website = "https://sonolume.local";
        Contact = "";
        PluginName = "Sonolume";
        PluginCategory = "Instrument|Synth";
        PluginVersion = "0.1.0";
        PluginID = 0x534F4E4F4C554D45; // "SONOLUME"

        HasUserInterface = true;
        EditorWidth = 720;
        EditorHeight = 440;
    }

    public override void Initialize()
    {
        base.Initialize();
        OutputPorts = [output = new DoubleAudioIOPort("Silent Output", EAudioChannelConfiguration.Stereo)];
        EnsureSession();
    }

    private SonolumeSession EnsureSession()
    {
        if (session is null)
        {
            session = new SonolumeSession();
            if (pendingProjectJson is not null)
            {
                try { session.ImportProjectJson(pendingProjectJson); }
                catch (Exception ex) { Logger.Log("Sonolume: stored project rejected: " + ex.Message); }
                pendingProjectJson = null;
            }
        }
        return session;
    }

    public override void HandleNoteOn(int channel, int noteNumber, float velocity, int sampleOffset) =>
        session?.Enqueue(MidiEvent.NoteOn(channel, noteNumber, velocity, Clock.Now()));

    public override void HandleNoteOff(int channel, int noteNumber, float velocity, int sampleOffset) =>
        session?.Enqueue(MidiEvent.NoteOff(channel, noteNumber, velocity, Clock.Now()));

    public override void HandlePolyPressure(int channel, int noteNumber, float pressure, int sampleOffset) =>
        session?.Enqueue(MidiEvent.PolyAftertouch(channel, noteNumber, (int)MathF.Round(pressure * 127f), Clock.Now()));

    public override void Process()
    {
        base.Process();
        session?.SetTempo(Host.BPM, Host.IsPlaying);
        Host.ProcessAllEvents();
        if (output is null) return;
        output.GetAudioBuffer(0).Clear();
        output.GetAudioBuffer(1).Clear();
    }

    public override System.Windows.Controls.UserControl GetEditorView() => new SonolumeView(EnsureSession());

    public override byte[] SaveState()
    {
        var envelope = new StateEnvelope
        {
            Base = base.SaveState() is { } baseState ? Convert.ToBase64String(baseState) : null,
            Project = session is not null ? TryExport(session) : pendingProjectJson,
        };
        return JsonSerializer.SerializeToUtf8Bytes(envelope);
    }

    public override void RestoreState(byte[] stateData)
    {
        if (stateData is null || stateData.Length == 0) return;
        StateEnvelope? envelope;
        try { envelope = JsonSerializer.Deserialize<StateEnvelope>(stateData); }
        catch (JsonException)
        {
            base.RestoreState(stateData);
            return;
        }
        if (envelope is null) return;

        if (envelope.Base is not null)
        {
            try { base.RestoreState(Convert.FromBase64String(envelope.Base)); }
            catch (FormatException) { }
        }

        if (envelope.Project is null) return;
        if (session is null)
        {
            pendingProjectJson = envelope.Project;
            return;
        }
        try { session.ImportProjectJson(envelope.Project); }
        catch (Exception ex) { Logger.Log("Sonolume: stored project rejected: " + ex.Message); }
    }

    private static string? TryExport(SonolumeSession session)
    {
        try { return session.ExportProjectJson(); }
        catch (Exception ex)
        {
            Logger.Log("Sonolume: project export failed: " + ex.Message);
            return null;
        }
    }

    ~SonolumePlugin()
    {
        try { session?.Dispose(); }
        catch { }
    }

    private sealed class StateEnvelope
    {
        public int Version { get; set; } = 1;
        public string? Base { get; set; }
        public string? Project { get; set; }
    }
}

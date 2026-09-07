using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sonolume.Engine.Output;

namespace Sonolume.UI;

public partial class SonolumeView : UserControl
{
    private readonly SonolumeSession session;
    private readonly DispatcherTimer timer;
    private bool sliderInitialized;

    public SonolumeView(SonolumeSession session)
    {
        this.session = session;
        InitializeComponent();

        timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += (_, _) => Refresh();

        Loaded += (_, _) => { Refresh(); timer.Start(); };
        Unloaded += (_, _) => timer.Stop();
    }

    private void Refresh()
    {
        var snapshot = session.Runner.LatestSnapshot;
        Preview.Snapshot = snapshot;

        if (snapshot is not null)
        {
            ProjectNameText.Text = snapshot.ProjectName;
            if (!sliderInitialized && snapshot.Zones.Count > 0)
            {
                sliderInitialized = true;
                DecaySlider.Value = snapshot.Zones[0].DecaySeconds;
            }
        }

        StatusText.Text = Describe(session.Sink.Status);
        MidiText.Text = $"MIDI: {session.Runner.ProcessedEvents} events" + (session.Runner.DroppedEvents > 0 ? $", {session.Runner.DroppedEvents} dropped" : "");
    }

    private static string Describe(SinkStatus status) => status.State switch
    {
        SinkState.Connected => $"SignalRGB: connected  {status.LastSendMs:0.0} ms  {status.FramesSent} frames" + (status.FramesDropped > 0 ? $"  ({status.FramesDropped} dropped)" : ""),
        SinkState.Offline => "SignalRGB: offline (is SignalRGB running?)",
        SinkState.Error => $"SignalRGB: {status.Message}",
        _ => "SignalRGB: connecting...",
    };

    private void DecaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DecayText is null) return;
        DecayText.Text = $"{e.NewValue * 1000:0} ms";
        if (sliderInitialized) session.SetDecaySeconds((float)e.NewValue);
    }
}

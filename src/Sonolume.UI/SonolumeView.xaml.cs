using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sonolume.Engine.Core;
using Sonolume.Engine.Output;

namespace Sonolume.UI;

public partial class SonolumeView : UserControl
{
    private readonly SonolumeSession session;
    private readonly DispatcherTimer timer;
    private bool sliderInitialized;
    private ZoneEditorWindow? editorWindow;

    public SonolumeView(SonolumeSession session)
    {
        this.session = session;
        InitializeComponent();

        Preview.Editable = true;
        Preview.ZoneClicked += id => session.SelectedZoneId = id;
        Preview.ZoneRectCommitted += CommitZoneRect;

        timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += (_, _) => Refresh();

        Loaded += (_, _) => { Refresh(); timer.Start(); };
        Unloaded += (_, _) =>
        {
            timer.Stop();
            editorWindow?.Close();
        };
    }

    private void CommitZoneRect(string id, RectF rect)
    {
        try
        {
            session.UpdateZone(id, z => z.Rect = rect);
        }
        catch (InvalidDataException)
        {
            // The zone was deleted (e.g. via the Zone Editor) while the drag was in flight; nothing to update.
        }
    }

    private void EditZones_Click(object sender, RoutedEventArgs e)
    {
        if (editorWindow is null)
        {
            editorWindow = new ZoneEditorWindow(session);
            editorWindow.Closed += (_, _) => editorWindow = null;
            editorWindow.Show();
        }
        else
        {
            editorWindow.Activate();
        }
    }

    private void Refresh()
    {
        var snapshot = session.Runner.LatestSnapshot;
        Preview.Snapshot = snapshot;
        Preview.SelectedZoneId = session.SelectedZoneId;

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

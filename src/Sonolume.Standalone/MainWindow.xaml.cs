using System.Windows;
using System.Windows.Controls;
using Sonolume.UI;

namespace Sonolume.Standalone;

public partial class MainWindow : Window
{
    private readonly SonolumeSession session = new();
    private readonly MidiInputBridge midi;

    public MainWindow()
    {
        InitializeComponent();
        midi = new MidiInputBridge(e => session.Enqueue(e));
        ViewHost.Content = new SonolumeView(session);
        Closed += (_, _) =>
        {
            midi.Dispose();
            session.Dispose();
        };
        RefreshDevices();
    }

    private void RefreshDevices()
    {
        string? selected = DeviceCombo.SelectedItem as string;
        var names = MidiInputBridge.DeviceNames();
        DeviceCombo.ItemsSource = names;
        if (names.Count == 0) return;
        DeviceCombo.SelectedItem = selected is not null && names.Contains(selected) ? selected : names[0];
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshDevices();

    private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceCombo.SelectedItem is not string name) return;
        try
        {
            midi.Open(name);
            Title = $"Sonolume  -  {name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open '{name}':\n{ex.Message}", "MIDI input", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

}

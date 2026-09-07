using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Sonolume.UI;

namespace Sonolume.Standalone;

public partial class MainWindow : Window
{
    private readonly SonolumeSession session = new();
    private readonly MidiInputBridge midi;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => UseDarkTitleBar();
        midi = new MidiInputBridge(e => session.Enqueue(e));
        ViewHost.Content = new SonolumeView(session);
        Closed += (_, _) =>
        {
            midi.Dispose();
            session.Dispose();
        };
        RefreshDevices();
    }

    private void UseDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int enabled = 1;
        // Attribute 20 on Windows 10 20H1+/Windows 11; older builds use 19.
        if (DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

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

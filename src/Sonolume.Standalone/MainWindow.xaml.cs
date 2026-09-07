using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
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

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Sonolume project (*.sonolume.json)|*.sonolume.json|JSON (*.json)|*.json|All files|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            session.ImportProjectJson(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Sonolume project (*.sonolume.json)|*.sonolume.json", FileName = "project.sonolume.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, session.ExportProjectJson());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Sonolume.UI;

namespace Sonolume.Standalone;

public partial class MainWindow : Window
{
    private readonly SonolumeSession session = new();
    private readonly MidiInputBridge midi;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            UseDarkTitleBar(hwnd);
            TryEnableMica(hwnd);
        };
        midi = new MidiInputBridge(e => session.Enqueue(e));
        ViewHost.Content = new SonolumeView(session);
        Closed += (_, _) =>
        {
            midi.Dispose();
            session.Dispose();
        };
        RefreshDevices();
    }

    private static void UseDarkTitleBar(IntPtr hwnd)
    {
        int enabled = 1;
        // Attribute 20 on Windows 10 20H1+/Windows 11; older builds use 19.
        if (DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
    }

    private void TryEnableMica(IntPtr hwnd)
    {
        const int DwmwaSystemBackdropType = 38;
        const int DwmsbtMainWindow = 2; // Mica
        int backdropType = DwmsbtMainWindow;
        // Fails on Windows 10 and pre-22H2 Windows 11 builds; the XAML-declared solid
        // background stays in place there instead of a transparent window to nowhere.
        if (DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdropType, sizeof(int)) != 0)
            return;

        Background = Brushes.Transparent;
        if (HwndSource.FromHwnd(hwnd) is { } hwndSource)
            hwndSource.CompositionTarget.BackgroundColor = Colors.Transparent;

        // Tell DWM the whole client area (not just the caption) participates in the
        // backdrop material; otherwise the transparent regions above show whatever's
        // behind the window instead of Mica.
        var wholeClientArea = new Margins(-1, -1, -1, -1);
        DwmExtendFrameIntoClientArea(hwnd, ref wholeClientArea);

        // SonolumeView paints its own opaque background so it also works unmodified as
        // the DAW plugin's editor (no Mica there). Only the standalone host's copy, once
        // Mica is confirmed on, switches to transparent so the backdrop shows through
        // the margins between its panels too.
        if (ViewHost.Content is SonolumeView view)
        {
            view.Background = Brushes.Transparent;
            view.UseTranslucentPanels(230); // ~90% opaque: a hint of Mica through the cards, text stays crisp.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins(int left, int right, int top, int bottom)
    {
        public int Left = left, Right = right, Top = top, Bottom = bottom;
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

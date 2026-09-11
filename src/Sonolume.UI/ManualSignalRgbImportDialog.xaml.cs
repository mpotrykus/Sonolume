using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Sonolume.Engine.Core;

namespace Sonolume.UI;

/// <summary>Lets the user hand-enter one component's SignalRGB Layout-tab fields (position, size, rotation,
/// canvas size) and turns them into a zone rect - the fallback for controllers
/// <see cref="SonolumeSession.ImportSignalRgbLayoutAsync"/> can't get an individual position for from SignalRGB's
/// MCP API (see its result's "Skipped" list).</summary>
public partial class ManualSignalRgbImportDialog : Window
{
    public sealed record Result(string Name, RectF Rect, float Rotation);

    private Result? result;

    private ManualSignalRgbImportDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    /// <summary>Shows the dialog modally; returns the entered zone, or null if cancelled.
    /// <paramref name="defaultCanvasWidth"/>/<paramref name="defaultCanvasHeight"/> prefill the canvas size
    /// fields (it rarely changes between devices), typically from the last automatic import's result.</summary>
    public static Result? Show(Window owner, float? defaultCanvasWidth, float? defaultCanvasHeight)
    {
        var dialog = new ManualSignalRgbImportDialog { Owner = owner };
        if (defaultCanvasWidth is { } cw) dialog.CanvasWidthBox.Text = Fmt(cw);
        if (defaultCanvasHeight is { } ch) dialog.CanvasHeightBox.Text = Fmt(ch);

        // A borderless, owner-sized overlay window instead of WindowStartupLocation=CenterOwner so the dimmed
        // backdrop covers the whole app, not just a small floating card - same idiom as AppDialog.
        dialog.Left = owner.Left;
        dialog.Top = owner.Top;
        dialog.Width = owner.ActualWidth;
        dialog.Height = owner.ActualHeight;
        dialog.ShowDialog();
        return dialog.result;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParse(XBox.Text, out float x) || !TryParse(YBox.Text, out float y)
            || !TryParse(WidthBox.Text, out float w) || !TryParse(HeightBox.Text, out float h)
            || !TryParse(RotationBox.Text, out float rotation)
            || !TryParse(CanvasWidthBox.Text, out float canvasW) || !TryParse(CanvasHeightBox.Text, out float canvasH))
        {
            ShowError("Enter a number in every field.");
            return;
        }
        if (canvasW <= 0 || canvasH <= 0)
        {
            ShowError("Canvas width and height must be greater than zero.");
            return;
        }

        string name = string.IsNullOrWhiteSpace(NameBox.Text) ? "SignalRGB Device" : NameBox.Text.Trim();
        var rect = new RectF(x / canvasW, y / canvasH, w / canvasW, h / canvasH);
        result = new Result(name, rect, rotation);
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static bool TryParse(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string Fmt(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e) => Close();

    private void Card_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}

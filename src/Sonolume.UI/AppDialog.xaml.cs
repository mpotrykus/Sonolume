using System.Windows;
using System.Windows.Input;

namespace Sonolume.UI;

/// <summary>App-styled replacement for <see cref="MessageBox"/>: an owner-window-sized overlay with a dimmed
/// backdrop and a centered card, so confirmations (e.g. delete) and error notices match the rest of the UI
/// instead of popping a native OS dialog.</summary>
public partial class AppDialog : Window
{
    private bool confirmed;

    private AppDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => (CancelButton.Visibility == Visibility.Visible ? CancelButton : ConfirmButton).Focus();
    }

    /// <summary>OK-only notice, for errors and other information the user just needs to acknowledge.</summary>
    public static void ShowMessage(Window owner, string message, string title)
    {
        var dialog = Build(owner, message, title);
        dialog.ConfirmButton.Content = "OK";
        dialog.ShowDialog();
    }

    /// <summary>Yes/No-style confirmation. Returns true if the user confirmed. <paramref name="destructive"/>
    /// styles the confirm button as a warning (red) for actions like delete that can't be undone from the UI.</summary>
    public static bool ShowConfirm(Window owner, string message, string title, string confirmText = "Yes", string cancelText = "No", bool destructive = false)
    {
        var dialog = Build(owner, message, title);
        dialog.ConfirmButton.Content = confirmText;
        dialog.CancelButton.Content = cancelText;
        dialog.CancelButton.Visibility = Visibility.Visible;
        if (destructive) dialog.ConfirmButton.Style = (Style)dialog.FindResource("DangerButtonStyle");
        dialog.ShowDialog();
        return dialog.confirmed;
    }

    private static AppDialog Build(Window owner, string message, string title)
    {
        var dialog = new AppDialog { Owner = owner };
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;

        // A borderless, owner-sized overlay window instead of WindowStartupLocation=CenterOwner so the dimmed
        // backdrop covers the whole app, not just a small floating card - the dialog IS the dim layer.
        dialog.Left = owner.Left;
        dialog.Top = owner.Top;
        dialog.Width = owner.ActualWidth;
        dialog.Height = owner.ActualHeight;
        return dialog;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        confirmed = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e) => Close();

    private void Card_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}

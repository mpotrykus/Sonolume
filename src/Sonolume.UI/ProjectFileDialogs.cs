using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Sonolume.UI;

/// <summary>The shared Open/Save-As file-picker idiom, used by both the standalone chrome and the Zone Editor.</summary>
public static class ProjectFileDialogs
{
    public static void Open(Window owner, SonolumeSession session)
    {
        var dialog = new OpenFileDialog { Filter = "Sonolume project (*.sonolume.json)|*.sonolume.json|JSON (*.json)|*.json|All files|*.*" };
        if (dialog.ShowDialog(owner) != true) return;
        try
        {
            session.ImportProjectJson(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "Open project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void SaveAs(Window owner, SonolumeSession session)
    {
        var dialog = new SaveFileDialog { Filter = "Sonolume project (*.sonolume.json)|*.sonolume.json", FileName = "project.sonolume.json" };
        if (dialog.ShowDialog(owner) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, session.ExportProjectJson());
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "Save project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

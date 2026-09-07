using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Sonolume.UI;

/// <summary>The shared Open/Save/Save-As file-picker idiom, used by both the standalone chrome and the Zone Editor.</summary>
public static class ProjectFileDialogs
{
    public static void Open(Window owner, SonolumeSession session)
    {
        var dialog = new OpenFileDialog { Filter = "Sonolume project (*.sonolume.json)|*.sonolume.json|JSON (*.json)|*.json|All files|*.*" };
        if (dialog.ShowDialog(owner) != true) return;
        try
        {
            session.ImportProjectJson(File.ReadAllText(dialog.FileName));
            session.CurrentFilePath = dialog.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "Open project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Writes back to <see cref="SonolumeSession.CurrentFilePath"/> if known, otherwise prompts like <see cref="SaveAs"/>.
    /// No-ops if there are no unsaved changes and a path is already known.</summary>
    public static void Save(Window owner, SonolumeSession session)
    {
        if (session.CurrentFilePath is null)
        {
            SaveAs(owner, session);
            return;
        }
        if (!session.HasUnsavedChanges) return;
        try
        {
            string json = session.ExportProjectJson();
            File.WriteAllText(session.CurrentFilePath, json);
            session.MarkSaved(json);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "Save project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void SaveAs(Window owner, SonolumeSession session)
    {
        var dialog = new SaveFileDialog { Filter = "Sonolume project (*.sonolume.json)|*.sonolume.json", FileName = "project.sonolume.json" };
        if (dialog.ShowDialog(owner) != true) return;
        try
        {
            string json = session.ExportProjectJson();
            File.WriteAllText(dialog.FileName, json);
            session.CurrentFilePath = dialog.FileName;
            session.MarkSaved(json);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "Save project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

using System.IO;
using System.Text.Json;

namespace Sonolume.UI;

/// <summary>Persists the "Open Recent" list across launches to %AppData%\Sonolume\recent-files.json.</summary>
public static class RecentFiles
{
    private const int MaxEntries = 10;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sonolume", "recent-files.json");

    /// <summary>Paths that still exist on disk, most-recently-opened first.</summary>
    public static List<string> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var paths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? new();
            return paths.Where(File.Exists).ToList();
        }
        catch
        {
            return new();
        }
    }

    public static void Add(string path)
    {
        var paths = Load();
        paths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        paths.Insert(0, path);
        if (paths.Count > MaxEntries) paths.RemoveRange(MaxEntries, paths.Count - MaxEntries);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(paths));
        }
        catch
        {
            // recent-files list is a convenience; a failed write here shouldn't block the open/save it followed
        }
    }
}

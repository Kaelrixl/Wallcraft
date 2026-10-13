using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace Wallcraft;

internal static class SteamPaths
{
    public static string? FindMyProjects() => WallpaperEngineDirectories()
        .Select(path => Path.Combine(path, "projects", "myprojects"))
        .FirstOrDefault();

    public static IEnumerable<string> WallpaperRoots(bool subscribedOnly)
    {
        if (!subscribedOnly)
            foreach (var engine in WallpaperEngineDirectories())
                yield return Path.Combine(engine, "projects", "myprojects");

        foreach (var library in LibraryDirectories())
            yield return Path.Combine(library, "steamapps", "workshop", "content", "431960");
    }

    private static IEnumerable<string> WallpaperEngineDirectories() => LibraryDirectories()
        .Select(path => Path.Combine(path, "steamapps", "common", "wallpaper_engine"))
        .Where(Directory.Exists);

    private static IEnumerable<string> LibraryDirectories()
    {
        var candidates = new List<string>();
        var registered = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam")?.GetValue("SteamPath") as string;
        if (!string.IsNullOrWhiteSpace(registered)) candidates.Add(registered);
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));

        foreach (var steam in candidates.ToArray())
        {
            var librariesFile = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(librariesFile)) continue;
            try
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(librariesFile),
                    "\"path\"\\s*\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase))
                    candidates.Add(match.Groups["path"].Value.Replace(@"\\", @"\"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return candidates.Select(Path.GetFullPath).Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}

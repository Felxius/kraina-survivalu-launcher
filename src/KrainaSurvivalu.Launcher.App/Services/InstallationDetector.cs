using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using KrainaSurvivalu.Launcher.Core;

namespace KrainaSurvivalu.Launcher.App.Services;

public static partial class InstallationDetector
{
    public static LauncherSettings Detect(LauncherSettings current)
    {
        string? steamRoot = null;
        try
        {
            steamRoot = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            steamRoot ??= Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        }
        catch (System.Security.SecurityException) { }
        var steamExe = current.SteamExecutable;
        if (!File.Exists(steamExe) && !string.IsNullOrEmpty(steamRoot))
        {
            var candidate = Path.Combine(steamRoot, "steam.exe");
            if (File.Exists(candidate)) steamExe = candidate;
        }
        if (string.IsNullOrWhiteSpace(steamRoot) && File.Exists(steamExe)) steamRoot = Path.GetDirectoryName(steamExe);
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(steamRoot))
        {
            libraries.Add(steamRoot);
            var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                try
                {
                    foreach (Match match in LibraryPathPattern().Matches(File.ReadAllText(vdf)))
                        libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        var dayz = current.DayZDirectory;
        if (!File.Exists(Path.Combine(dayz, "DayZ_x64.exe")))
            foreach (var library in libraries)
            {
                var candidate = Path.Combine(library, "steamapps", "common", "DayZ");
                if (File.Exists(Path.Combine(candidate, "DayZ_x64.exe"))) { dayz = candidate; break; }
            }
        return new LauncherSettings { DayZDirectory = dayz, SteamExecutable = steamExe };
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPathPattern();
}

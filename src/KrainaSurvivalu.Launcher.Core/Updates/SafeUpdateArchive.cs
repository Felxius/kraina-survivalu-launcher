using System.IO.Compression;

namespace KrainaSurvivalu.Launcher.Core.Updates;

public static class SafeUpdateArchive
{
    public static IReadOnlyList<string> RequiredFileNames { get; } = Array.AsReadOnly(new[]
    {
        UpdateTrust.ApplicationFileName, "KrainaSurvivalu.Launcher.dll", "KrainaSurvivalu.Launcher.Core.dll",
        "KrainaSurvivalu.Launcher.deps.json", "KrainaSurvivalu.Launcher.runtimeconfig.json", "config/updates.json",
        "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "System.Private.CoreLib.dll",
        "PresentationCore.dll", "PresentationFramework.dll", "WindowsBase.dll", "wpfgfx_cor3.dll",
        "Updater/" + UpdateTrust.UpdaterFileName, "Updater/KrainaSurvivalu.Launcher.Updater.dll",
        "Updater/KrainaSurvivalu.Launcher.Updater.deps.json", "Updater/KrainaSurvivalu.Launcher.Updater.runtimeconfig.json",
        "Updater/KrainaSurvivalu.Launcher.Core.dll", "Updater/hostfxr.dll", "Updater/hostpolicy.dll", "Updater/coreclr.dll", "Updater/System.Private.CoreLib.dll",
        "SteamWorker/KrainaSurvivalu.SteamWorker.exe", "SteamWorker/KrainaSurvivalu.SteamWorker.dll",
        "SteamWorker/KrainaSurvivalu.SteamWorker.deps.json", "SteamWorker/KrainaSurvivalu.SteamWorker.runtimeconfig.json",
        "SteamWorker/hostfxr.dll", "SteamWorker/hostpolicy.dll", "SteamWorker/coreclr.dll", "SteamWorker/System.Private.CoreLib.dll"
    });

    public static async Task ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        UpdateTrust.RejectReparsePoint(destinationDirectory);
        if (Directory.Exists(destinationDirectory)) throw new IOException("Folder rozpakowania aktualizacji musi być nowy.");
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count is 0 or > UpdateTrust.MaximumEntries) throw new InvalidDataException("Niedozwolona liczba elementów archiwum.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var regularFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalLength = 0;
        foreach (var entry in archive.Entries)
        {
            _ = ValidateEntry(entry, destinationDirectory);
            var normalized = entry.FullName.Replace('\\', '/').TrimEnd('/');
            if (!paths.Add(normalized)) throw new InvalidDataException("Archiwum zawiera powtórzone nazwy plików.");
            if (!IsDirectory(entry)) regularFiles.Add(normalized);
            totalLength = checked(totalLength + entry.Length);
            if (entry.Length > UpdateTrust.MaximumArchiveBytes || totalLength > UpdateTrust.MaximumExpandedBytes) throw new InvalidDataException("Rozpakowana aktualizacja jest zbyt duża.");
            if (entry.Length > 1024 * 1024 && (entry.CompressedLength == 0 || entry.Length / Math.Max(1, entry.CompressedLength) > 500)) throw new InvalidDataException("Podejrzany stopień kompresji archiwum.");
        }
        if (RequiredFileNames.Any(required => !regularFiles.Contains(required)))
            throw new InvalidDataException("Archiwum nie zawiera pełnej publikacji launchera oraz pomocników Steam i aktualizacji z ich środowiskami uruchomieniowymi.");
        Directory.CreateDirectory(destinationDirectory);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = ValidateEntry(entry, destinationDirectory);
            if (IsDirectory(entry)) { Directory.CreateDirectory(destination); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            UpdateTrust.RejectReparsePoint(destination);
            await using var input = entry.Open();
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, true);
            var buffer = new byte[81_920]; long written = 0; int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                written = checked(written + read);
                if (written > entry.Length) throw new InvalidDataException("Dane elementu przekraczają deklarowany rozmiar.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            if (written != entry.Length) throw new InvalidDataException("Niepełny element archiwum.");
        }
        if (RequiredFileNames.Any(required => !File.Exists(Path.Combine(destinationDirectory, required.Replace('/', Path.DirectorySeparatorChar)))))
            throw new InvalidDataException("Po rozpakowaniu brakuje wymaganego pliku publikacji.");
    }

    private static string ValidateEntry(ZipArchiveEntry entry, string destinationDirectory)
    {
        var name = entry.FullName.Replace('\\', '/');
        if (name.Equals(UpdateTrust.InstallLockFileName, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Archiwum nie może zastępować blokady instalacji.");
        if (name.Length is 0 or > 220 || name.StartsWith('/') || name.Contains(':') || name.Contains('\0') || name.Split('/').Any(segment => segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ')))
            throw new InvalidDataException("Archiwum zawiera niebezpieczną ścieżkę.");
        var segments = name.TrimEnd('/').Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0 || IsDeviceName(segment))) throw new InvalidDataException("Niedozwolona nazwa elementu archiwum.");
        var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
        if (unixType is not (0 or 0x8000 or 0x4000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Archiwum zawiera dowiązanie lub element specjalny.");
        return UpdateTrust.RequireChildPath(destinationDirectory, Path.Combine(destinationDirectory, name.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static bool IsDirectory(ZipArchiveEntry entry) => entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\') || ((entry.ExternalAttributes >> 16) & 0xF000) == 0x4000 || (entry.ExternalAttributes & (int)FileAttributes.Directory) != 0;

    private static bool IsDeviceName(string segment)
    {
        var stem = segment.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '0' and <= '9';
    }
}

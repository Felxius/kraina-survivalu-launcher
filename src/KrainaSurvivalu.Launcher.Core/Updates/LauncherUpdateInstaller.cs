using System.Diagnostics;
using System.Text.Json;

namespace KrainaSurvivalu.Launcher.Core.Updates;

public static class LauncherUpdateInstaller
{
    /// <summary>Runs only the installed, fixed-name helper, copied out of the files it will replace.</summary>
    public static void StartApply(StagedLauncherUpdate staged, string installDirectory, int parentProcessId, DateTime parentStartTimeUtc)
    {
        var stage = ValidateReceiptLocation(staged.ReceiptPath, UpdateTrust.DefaultStagingRoot);
        if (!Path.GetFullPath(staged.StagingDirectory).Equals(stage, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Niezgodny folder aktualizacji.");
        var install = ValidateInstallDirectory(installDirectory);
        using var installLock = AcquireInstallLock(install);
        var helperSource = Path.Combine(install, "Updater");
        UpdateTrust.RejectReparsePoint(helperSource);
        if (!File.Exists(Path.Combine(helperSource, UpdateTrust.UpdaterFileName))) throw new FileNotFoundException("Brak pomocnika aktualizacji. Pobierz pełną paczkę launchera.");
        var bootstrap = Path.Combine(stage, "bootstrap");
        if (Directory.Exists(bootstrap)) throw new IOException("Pomocnik tej aktualizacji został już przygotowany.");
        Directory.CreateDirectory(bootstrap);
        CopyTree(helperSource, bootstrap);
        var info = new ProcessStartInfo(Path.Combine(bootstrap, UpdateTrust.UpdaterFileName)) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = bootstrap };
        foreach (var argument in new[] { "--apply", staged.ReceiptPath, "--install", install, "--parent", parentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), "--parent-start", parentStartTimeUtc.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), "--restart" }) info.ArgumentList.Add(argument);
        _ = Process.Start(info) ?? throw new IOException("Nie uruchomiono pomocnika aktualizacji.");
    }

    public static Task ApplyAsync(string receiptPath, string installDirectory, CancellationToken cancellationToken = default) => ApplyForTestingAsync(receiptPath, installDirectory, UpdateTrust.DefaultStagingRoot, null, cancellationToken);

    internal static async Task ApplyForTestingAsync(string receiptPath, string installDirectory, string stagingRoot, Action<string>? afterCopy, CancellationToken cancellationToken = default)
    {
        var stage = ValidateReceiptLocation(receiptPath, stagingRoot);
        var install = ValidateInstallDirectory(installDirectory);
        if (install.Equals(stage, StringComparison.OrdinalIgnoreCase) || install.StartsWith(stage + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || stage.StartsWith(install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Folder aktualizacji musi być oddzielony od instalacji.");
        // An actual file handle provides the same inter-process exclusion even if two
        // callers spell an install path differently. Keep it through version check,
        // extraction, all replacements, and any rollback.
        using var installLock = AcquireInstallLock(install);
        var config = UpdateConfiguration.Load(Path.Combine(install, "config", "updates.json"));
        UpdateTrust.ValidateConfiguration(config);
        var receiptBytes = await File.ReadAllBytesAsync(receiptPath, cancellationToken);
        if (receiptBytes.Length > 32_768) throw new InvalidDataException("Potwierdzenie aktualizacji jest zbyt duże.");
        var receipt = JsonSerializer.Deserialize<UpdateStageReceipt>(receiptBytes, UpdateJson.Options) ?? throw new InvalidDataException("Brak potwierdzenia aktualizacji.");
        if (receipt.SchemaVersion != 1 || receipt.ArchiveFileName != "release.zip") throw new InvalidDataException("Nieprawidłowe potwierdzenie aktualizacji.");
        var manifest = UpdateTrust.VerifyManifest(config, receipt.Envelope);
        var currentVersion = FileVersionInfo.GetVersionInfo(Path.Combine(install, "KrainaSurvivalu.Launcher.dll")).ProductVersion;
        if (string.IsNullOrWhiteSpace(currentVersion)) throw new InvalidDataException("Nie można odczytać wersji zainstalowanej aplikacji.");
        if (LauncherVersion.Parse(manifest.Version).CompareTo(LauncherVersion.Parse(currentVersion)) <= 0) throw new InvalidDataException("Starsza lub ta sama wersja launchera nie może zastąpić zainstalowanej.");
        var archivePath = Path.Combine(stage, "release.zip");
        await UpdateTrust.VerifyArchiveAsync(archivePath, manifest, cancellationToken);
        var payload = Path.Combine(stage, "apply-" + Guid.NewGuid().ToString("N"));
        await SafeUpdateArchive.ExtractAsync(archivePath, payload, cancellationToken);
        var payloadVersion = FileVersionInfo.GetVersionInfo(Path.Combine(payload, "KrainaSurvivalu.Launcher.dll")).ProductVersion;
        if (string.IsNullOrWhiteSpace(payloadVersion) || LauncherVersion.Parse(payloadVersion).CompareTo(LauncherVersion.Parse(manifest.Version)) != 0)
            throw new InvalidDataException("Wersja aplikacji wewnątrz ZIP-a nie odpowiada podpisanemu manifestowi.");
        var backup = Path.Combine(stage, "backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        var changed = new List<(string Destination, string? Backup)>();
        try
        {
            foreach (var source in Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(payload, source);
                var destination = UpdateTrust.RequireChildPath(install, Path.Combine(install, relative));
                UpdateTrust.RejectReparsePoint(destination);
                string? original = null;
                if (File.Exists(destination))
                {
                    original = UpdateTrust.RequireChildPath(backup, Path.Combine(backup, relative));
                    Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                    File.Copy(destination, original, false);
                }
                changed.Add((destination, original));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, true);
                afterCopy?.Invoke(relative);
            }
            await File.WriteAllTextAsync(Path.Combine(stage, "applied.json"), JsonSerializer.Serialize(new { version = manifest.Version, appliedAtUtc = DateTimeOffset.UtcNow, backupDirectory = backup }, UpdateJson.Options), cancellationToken);
        }
        catch (Exception originalFailure)
        {
            var rollbackFailures = new List<Exception>();
            foreach (var file in changed.AsEnumerable().Reverse())
            {
                try
                {
                    UpdateTrust.RejectReparsePoint(file.Destination);
                    if (file.Backup is null) { if (File.Exists(file.Destination)) File.Delete(file.Destination); }
                    else File.Copy(file.Backup, file.Destination, true);
                }
                catch (Exception rollbackFailure) { rollbackFailures.Add(rollbackFailure); }
            }
            if (rollbackFailures.Count != 0) throw new AggregateException("Aktualizacja nie powiodła się; przywracanie niektórych plików wymaga ręcznej pomocy. Kopia została zachowana.", [originalFailure, .. rollbackFailures]);
            throw new IOException("Aktualizacja nie powiodła się. Przywrócono poprzednie pliki; kopia została zachowana.", originalFailure);
        }
    }

    private static string ValidateReceiptLocation(string receiptPath, string stagingRoot)
    {
        var fullReceipt = UpdateTrust.RequireChildPath(stagingRoot, receiptPath);
        if (Path.GetFileName(fullReceipt) != "receipt.json") throw new InvalidDataException("Nieprawidłowa nazwa potwierdzenia aktualizacji.");
        var stage = Path.GetDirectoryName(fullReceipt)!;
        if (!Path.GetDirectoryName(stage)!.Equals(Path.GetFullPath(stagingRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(stage), "N", out _)) throw new InvalidDataException("Potwierdzenie nie należy do folderu przygotowanej aktualizacji.");
        UpdateTrust.RejectReparsePoint(fullReceipt);
        return stage;
    }

    private static string ValidateInstallDirectory(string installDirectory)
    {
        var install = Path.GetFullPath(installDirectory).TrimEnd(Path.DirectorySeparatorChar);
        UpdateTrust.RejectReparsePoint(install);
        if (!Directory.Exists(install) || !File.Exists(Path.Combine(install, UpdateTrust.ApplicationFileName)) || !File.Exists(Path.Combine(install, "KrainaSurvivalu.Launcher.dll")) || !File.Exists(Path.Combine(install, "config", "updates.json"))) throw new InvalidDataException("Folder docelowy nie jest istniejącą instalacją launchera.");
        return install;
    }

    private static FileStream AcquireInstallLock(string installDirectory)
    {
        var lockPath = Path.Combine(installDirectory, UpdateTrust.InstallLockFileName);
        UpdateTrust.RejectReparsePoint(lockPath);
        try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) { throw new IOException("Inna instancja aktualizuje ten launcher. Poczekaj na jej zakończenie.", error); }
    }

    private static void CopyTree(string source, string destination)
    {
        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToArray();
        if (files.Length is 0 or > UpdateTrust.MaximumEntries || files.Sum(file => new FileInfo(file).Length) > UpdateTrust.MaximumExpandedBytes) throw new InvalidDataException("Nieprawidłowy folder pomocnika aktualizacji.");
        foreach (var file in files)
        {
            UpdateTrust.RejectReparsePoint(file);
            var target = UpdateTrust.RequireChildPath(destination, Path.Combine(destination, Path.GetRelativePath(source, file)));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, false);
        }
    }
}

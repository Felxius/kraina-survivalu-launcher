using System.Diagnostics;
using System.Globalization;
using KrainaSurvivalu.Launcher.Core.Updates;

if (args.Length != 9 || args[0] != "--apply" || args[2] != "--install" || args[4] != "--parent" || args[6] != "--parent-start" || args[8] != "--restart" || !int.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out var parentId) || !long.TryParse(args[7], NumberStyles.None, CultureInfo.InvariantCulture, out var parentTicks))
{
    Console.Error.WriteLine("Pomocnik jest uruchamiany wyłącznie przez launcher po przygotowaniu aktualizacji.");
    return 2;
}

string? stage = null;
try
{
    // Reject arbitrary receipts before inspecting/waiting on any process.
    var receipt = UpdateTrust.RequireChildPath(UpdateTrust.DefaultStagingRoot, args[1]);
    UpdateTrust.RejectReparsePoint(receipt);
    stage = Path.GetDirectoryName(receipt)!;
    if (Path.GetFileName(receipt) != "receipt.json" || !Guid.TryParseExact(Path.GetFileName(stage), "N", out _) || !Path.GetDirectoryName(stage)!.Equals(Path.GetFullPath(UpdateTrust.DefaultStagingRoot), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Nieprawidłowa lokalizacja aktualizacji.");
    var install = Path.GetFullPath(args[3]);
    UpdateTrust.RejectReparsePoint(install);
    if (parentId <= 0 || parentId == Environment.ProcessId) throw new InvalidDataException("Nieprawidłowy proces launchera.");
    using var waitDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    try
    {
        using var parent = Process.GetProcessById(parentId);
        if (parent.StartTime.ToUniversalTime().Ticks != parentTicks || !string.Equals(parent.MainModule?.FileName, Path.Combine(install, UpdateTrust.ApplicationFileName), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Proces nie jest wskazaną instancją launchera.");
        await parent.WaitForExitAsync(waitDeadline.Token);
    }
    catch (ArgumentException) { /* Parent exited before the helper reached it. */ }
    // No processes are killed. A running game or unrelated launcher is never controlled here.
    await LauncherUpdateInstaller.ApplyAsync(receipt, install);
    var restart = new ProcessStartInfo(Path.Combine(install, UpdateTrust.ApplicationFileName)) { UseShellExecute = false, WorkingDirectory = install };
    _ = Process.Start(restart) ?? throw new IOException("Nie uruchomiono nowej wersji launchera.");
    return 0;
}
catch (Exception error)
{
    if (stage is not null)
    {
        try { await File.WriteAllTextAsync(Path.Combine(stage, "apply-error.txt"), $"{DateTimeOffset.UtcNow:O}\n{error.GetType().Name}: {error.Message}\n"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    Console.Error.WriteLine("Aktualizacja nie została zastosowana. Szczegóły zapisano w folderze przygotowanej aktualizacji; zachowaj poprzednią paczkę.");
    return 1;
}

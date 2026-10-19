using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using KrainaSurvivalu.Launcher.Core;

namespace KrainaSurvivalu.Launcher.App.Services;

/// <summary>Steam is isolated in a short-lived helper, never loaded into the WPF process.</summary>
public sealed class SteamWorkshopClient : IWorkshopClient
{
    private readonly string _workerPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SteamWorkshopClient(string? workerPath = null) => _workerPath = workerPath ??
        Path.Combine(AppContext.BaseDirectory, "SteamWorker", "KrainaSurvivalu.SteamWorker.exe");

    public Task<WorkshopSyncResult> InspectAsync(LauncherSettings settings, IReadOnlyList<ServerWorkshopMod> mods,
        IProgress<WorkshopProgress>? progress = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync(false, settings, mods, progress, cancellationToken);

    public Task<WorkshopSyncResult> SynchronizeAsync(LauncherSettings settings, IReadOnlyList<ServerWorkshopMod> mods,
        IProgress<WorkshopProgress>? progress = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync(true, settings, mods, progress, cancellationToken);

    private async Task<WorkshopSyncResult> ExecuteAsync(bool synchronize, LauncherSettings settings,
        IReadOnlyList<ServerWorkshopMod> mods, IProgress<WorkshopProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(mods);
        cancellationToken.ThrowIfCancellationRequested();
        var paths = PathValidation.Validate(settings);
        if (!paths.IsValid) return Failure(mods, paths.Message);
        if (mods.Count > 500 || mods.Any(mod => mod is null || !mod.HasWorkshopId))
            return Failure(mods, "Lista zawiera nieprawidłowy identyfikator Workshop lub przekracza limit 500 modów.");
        if (mods.Count == 0) return new(true, Array.Empty<WorkshopItemSnapshot>(), "Serwer nie zgłosił modów Workshop do sprawdzenia.");
        if (!File.Exists(_workerPath))
            return Failure(mods, "Brakuje pomocnika Steam. Rozpakuj cały folder launchera razem z podfolderem SteamWorker.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunWorkerAsync(synchronize, settings, mods, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or
            System.ComponentModel.Win32Exception or UnauthorizedAccessException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException or DecoderFallbackException)
        {
            return Failure(mods, "Nie udało się zakończyć operacji Steam. " + exception.Message);
        }
        finally { _gate.Release(); }
    }

    private async Task<WorkshopSyncResult> RunWorkerAsync(bool synchronize, LauncherSettings settings,
        IReadOnlyList<ServerWorkshopMod> mods, IProgress<WorkshopProgress>? progress, CancellationToken cancellationToken)
    {
        var unique = mods.DistinctBy(mod => ulong.Parse(mod.WorkshopId, CultureInfo.InvariantCulture))
            .ToDictionary(mod => ulong.Parse(mod.WorkshopId, CultureInfo.InvariantCulture));
        var snapshots = new Dictionary<ulong, WorkshopItemSnapshot>();
        var start = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(_workerPath),
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(_workerPath))!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false, true),
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(synchronize ? "--sync" : "--inspect");
        start.ArgumentList.Add("--dayz"); start.ArgumentList.Add(settings.DayZDirectory);
        start.ArgumentList.Add("--steam"); start.ArgumentList.Add(settings.SteamExecutable);
        start.ArgumentList.Add("--ids");
        start.ArgumentList.Add(string.Join(',', unique.Keys.Select(id => id.ToString(CultureInfo.InvariantCulture))));
        // The worker establishes its own process-local app context. WPF and Steam are untouched.
        start.Environment.Remove("SteamAppId");
        start.Environment.Remove("SteamGameId");
        using var child = new Process { StartInfo = start };
        if (!child.Start()) throw new IOException("Nie udało się uruchomić pomocnika Steam.");
        using var cancellationRegistration = cancellationToken.Register(() => TerminateOwnedWorker(child));
        var errorDrain = DrainErrorAsync(child.StandardError, child, cancellationToken);
        var complete = false;
        var success = false;
        var terminalMessage = "Steam nie potwierdził zakończenia operacji.";
        try
        {
            await foreach (var line in ReadProtocolLinesAsync(child.StandardOutput, cancellationToken).ConfigureAwait(false))
            {
                if (line.Length == 0) continue;
                using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 16 });
                var data = document.RootElement;
                if (data.ValueKind != JsonValueKind.Object || data.GetProperty("schemaVersion").GetInt32() != 1)
                    throw new InvalidDataException("Nieobsługiwana odpowiedź pomocnika Steam.");
                if (complete) throw new InvalidDataException("Pomocnik Steam wysłał dane po zakończeniu operacji.");
                var type = data.GetProperty("type").GetString();
                var message = OptionalText(data, "message", 1800) ?? "Operacja Steam…";
                if (type is "complete" or "error")
                {
                    complete = true;
                    success = type == "complete" && data.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True;
                    terminalMessage = message;
                    continue;
                }
                if (type is not ("item" or "progress"))
                    throw new InvalidDataException("Pomocnik Steam zwrócił nieznany komunikat.");
                var id = data.GetProperty("id").GetUInt64();
                if (!unique.TryGetValue(id, out var mod))
                    throw new InvalidDataException("Pomocnik Steam zwrócił mod spoza zamówionej listy.");
                var downloaded = OptionalNumber(data, "bytesDownloaded");
                var total = OptionalNumber(data, "bytesTotal");
                if (type == "item")
                {
                    if (snapshots.ContainsKey(id)) throw new InvalidDataException("Powtórzony wynik modu z pomocnika Steam.");
                    var flags = (WorkshopItemState)data.GetProperty("state").GetUInt32();
                    var folder = OptionalText(data, "installedPath", 32767);
                    snapshots.Add(id, new(mod.WorkshopId, mod.Name, flags, folder, downloaded, total));
                }
                progress?.Report(new(mod.WorkshopId, message, downloaded, total, snapshots.Count, unique.Count));
            }
            await child.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await errorDrain.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            success = complete && success && child.ExitCode == 0 && snapshots.Count == unique.Count;
            var result = mods.Select(mod => snapshots.TryGetValue(ulong.Parse(mod.WorkshopId, CultureInfo.InvariantCulture), out var snapshot)
                ? snapshot with { WorkshopId = mod.WorkshopId, Name = mod.Name }
                : new WorkshopItemSnapshot(mod.WorkshopId, mod.Name, Error: terminalMessage)).ToArray();
            if (synchronize && success && result.Any(item => !item.IsReady))
            {
                success = false;
                terminalMessage = "Steam zakończył operację, ale nie wszystkie mody są gotowe. Sprawdź listę i kolejkę pobierania w Steam.";
            }
            return new(success, result, terminalMessage);
        }
        finally
        {
            TerminateOwnedWorker(child);
            try { await child.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (InvalidOperationException) { }
            try { await errorDrain.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    }

    private static WorkshopSyncResult Failure(IReadOnlyList<ServerWorkshopMod> mods, string message) =>
        new(false, mods.Where(mod => mod is not null).Select(mod => new WorkshopItemSnapshot(mod.WorkshopId, mod.Name, Error: message)).ToArray(), message);

    private static void TerminateOwnedWorker(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: false); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        // Cancellation detaches only this helper. Steam may continue its already scheduled job.
        // Never pause all downloads or terminate the user's Steam process.
    }

    private static string? OptionalText(JsonElement element, string name, int maximum)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null) return null;
        if (property.ValueKind != JsonValueKind.String) throw new InvalidDataException("Nieprawidłowy tekst w odpowiedzi pomocnika Steam.");
        var value = property.GetString();
        if (value is not null && value.Length > maximum) throw new InvalidDataException("Zbyt długi tekst w odpowiedzi pomocnika Steam.");
        return value;
    }

    private static ulong OptionalNumber(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.GetUInt64() : 0;

    private static async IAsyncEnumerable<string> ReadProtocolLinesAsync(StreamReader reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new char[2048];
        var line = new StringBuilder();
        long total = 0;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
        {
            total += count;
            if (total > 64 * 1024 * 1024) throw new InvalidDataException("Przekroczono limit danych pomocnika Steam.");
            for (var index = 0; index < count; index++)
            {
                var character = buffer[index];
                if (character == '\n') { yield return line.ToString().TrimEnd('\r'); line.Clear(); }
                else
                {
                    line.Append(character);
                    if (line.Length > 48 * 1024) throw new InvalidDataException("Przekroczono limit wiersza pomocnika Steam.");
                }
            }
        }
        if (line.Length != 0) throw new InvalidDataException("Pomocnik Steam przerwał komunikat.");
    }

    private static async Task DrainErrorAsync(StreamReader reader, Process child, CancellationToken cancellationToken)
    {
        // Native SDK diagnostics are drained, not rendered as untrusted UI text or retained in logs.
        var buffer = new char[2048];
        long total = 0;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
        {
            total += count;
            if (total > 8 * 1024 * 1024)
            {
                TerminateOwnedWorker(child);
                throw new InvalidDataException("Przekroczono limit diagnostyki pomocnika Steam.");
            }
        }
    }
}

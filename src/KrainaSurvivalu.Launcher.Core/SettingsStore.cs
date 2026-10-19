using System.Text.Json;
using System.Text.Json.Serialization;

namespace KrainaSurvivalu.Launcher.Core;

public sealed class SettingsStore
{
    private const int MaximumSettingsBytes = 65536;
    private readonly string _filePath;
    private bool _preserveExistingBeforeSave;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public SettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public async Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > MaximumSettingsBytes)
                throw new InvalidDataException("Plik ustawień jest zbyt duży.");
            var document = await JsonSerializer.DeserializeAsync<SettingsDocument>(stream, JsonOptions, cancellationToken);
            if (document is null || document.SchemaVersion != 1 || document.DayZDirectory is null ||
                document.SteamExecutable is null || document.PlayerName is null)
                throw new InvalidDataException("Nieprawidłowa struktura lub wersja ustawień.");
            var settings = new LauncherSettings
            {
                DayZDirectory = document.DayZDirectory,
                SteamExecutable = document.SteamExecutable,
                PlayerName = document.PlayerName
            };
            if (PathValidation.ValidateContent(settings) is { } error)
                throw new InvalidDataException(error);
            if (PlayerNameValidation.ValidateStoredName(settings.PlayerName) is { } playerNameError)
                throw new InvalidDataException(playerNameError);
            _preserveExistingBeforeSave = false;
            return new(settings, null);
        }
        catch (FileNotFoundException)
        {
            _preserveExistingBeforeSave = false;
            return new(new(), null);
        }
        catch (DirectoryNotFoundException)
        {
            _preserveExistingBeforeSave = false;
            return new(new(), null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return RecoverInvalidSettings(exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _preserveExistingBeforeSave = true;
            return new(new(), $"Nie udało się odczytać ustawień. Użyto pustych ustawień. {exception.Message}");
        }
    }

    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (PathValidation.ValidateContent(settings) is { } error)
            throw new ArgumentException(error, nameof(settings));
        if (PlayerNameValidation.ValidateStoredName(settings.PlayerName) is { } playerNameError)
            throw new ArgumentException(playerNameError, nameof(settings));
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var document = new SettingsDocument
            {
                SchemaVersion = 1,
                DayZDirectory = settings.DayZDirectory,
                SteamExecutable = settings.SteamExecutable,
                PlayerName = settings.PlayerName
            };
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (_preserveExistingBeforeSave)
                await PreserveUnreadSettingsAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // A same-directory rename prevents a partial JSON document becoming the active settings file.
            File.Move(temporaryPath, _filePath, overwrite: true);
            _preserveExistingBeforeSave = false;
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private SettingsLoadResult RecoverInvalidSettings(string reason)
    {
        var backupPath = _filePath + $".invalid-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.bak";
        try
        {
            File.Move(_filePath, backupPath);
            _preserveExistingBeforeSave = false;
            return new(new(), $"Nieprawidłowe ustawienia: {reason} Użyto pustych ustawień. Kopia oryginału: {backupPath}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _preserveExistingBeforeSave = true;
            return new(new(), $"Nieprawidłowe ustawienia: {reason} Użyto pustych ustawień. Nie udało się utworzyć kopii; oryginalny plik pozostawiono. {exception.Message}");
        }
    }

    private async Task PreserveUnreadSettingsAsync(CancellationToken cancellationToken)
    {
        var backupPath = _filePath + $".recovery-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.bak";
        var temporaryBackupPath = backupPath + ".tmp";
        try
        {
            // A previous failed read must never turn an unread original into an unconditional overwrite.
            // FileShare.Read also prevents another writer from modifying the bytes during the backup.
            await using (var original = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var backup = new FileStream(temporaryBackupPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await original.CopyToAsync(backup, cancellationToken);
                await backup.FlushAsync(cancellationToken);
                backup.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryBackupPath, backupPath);
        }
        catch (FileNotFoundException)
        {
            // The original was independently removed; there is no remaining file to overwrite.
        }
        catch (DirectoryNotFoundException)
        {
            // The original's directory was independently removed; the final rename still has to succeed.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Zapis wstrzymano: nie udało się bezpiecznie zachować poprzedniego, nieodczytanego pliku ustawień.", exception);
        }
        finally
        {
            try { File.Delete(temporaryBackupPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed class SettingsDocument
    {
        public int SchemaVersion { get; init; }
        public string? DayZDirectory { get; init; }
        public string? SteamExecutable { get; init; }
        public string? PlayerName { get; init; } = "";
    }
}

using System.Text.Json;

namespace KrainaSurvivalu.Launcher.Core.Updates;

public sealed record UpdateConfiguration
{
    public string? GitHubOwner { get; init; }
    public string? GitHubRepository { get; init; }
    public string? PublicKeyPem { get; init; }
    public const string ArchiveAssetName = "KS-Launcher-Windows-x64.zip";
    public const string ManifestAssetName = "KS-Launcher-update.json";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(GitHubOwner) && !string.IsNullOrWhiteSpace(GitHubRepository) && !string.IsNullOrWhiteSpace(PublicKeyPem);

    public static UpdateConfiguration Load(string path)
    {
        if (!File.Exists(path)) return new();
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 32_768) throw new InvalidDataException("Konfiguracja aktualizacji jest zbyt duża.");
        return JsonSerializer.Deserialize<UpdateConfiguration>(bytes, UpdateJson.Options) ?? new();
    }
}

public enum LauncherUpdateState { Unconfigured, UpToDate, Available, Error }
public sealed record LauncherUpdateCheckResult(LauncherUpdateState State, string Message, LauncherUpdateDescriptor? Update = null);
public sealed record LauncherUpdateManifest(int SchemaVersion, string Version, string AssetName, long ArchiveSize, string ArchiveSha256, DateTimeOffset PublishedAtUtc);
public sealed record SignedUpdateEnvelope(string PayloadBase64, string SignatureBase64);
public sealed record LauncherUpdateDescriptor(UpdateConfiguration Configuration, SignedUpdateEnvelope Envelope, LauncherUpdateManifest Manifest, Uri ArchiveUri);
public sealed record LauncherUpdateProgress(long BytesReceived, long TotalBytes)
{
    public double Percent => TotalBytes > 0 ? 100.0 * BytesReceived / TotalBytes : 0;
}
public sealed record StagedLauncherUpdate(string Version, string ReceiptPath, string StagingDirectory);
public sealed record UpdateStageReceipt(int SchemaVersion, SignedUpdateEnvelope Envelope, string ArchiveFileName);

public interface ILauncherUpdateService
{
    Task<LauncherUpdateCheckResult> CheckAsync(UpdateConfiguration configuration, string currentVersion, CancellationToken cancellationToken = default);
    Task<StagedLauncherUpdate> DownloadAsync(LauncherUpdateDescriptor update, IProgress<LauncherUpdateProgress>? progress = null, CancellationToken cancellationToken = default);
}

public static class UpdateJson
{
    public static JsonSerializerOptions Options { get; } = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
}

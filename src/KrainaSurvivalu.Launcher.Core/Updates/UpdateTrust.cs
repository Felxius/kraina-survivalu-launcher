using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KrainaSurvivalu.Launcher.Core.Updates;

public static partial class UpdateTrust
{
    public const long MaximumArchiveBytes = 512L * 1024 * 1024;
    public const long MaximumExpandedBytes = 1536L * 1024 * 1024;
    public const int MaximumEntries = 5000;
    public const string ApplicationFileName = "KrainaSurvivalu.Launcher.exe";
    public const string UpdaterFileName = "KrainaSurvivalu.Launcher.Updater.exe";
    public const string InstallLockFileName = ".ks-update.lock";
    public static string DefaultStagingRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KrainaSurvivalu", "Launcher", "Updates");

    public static void ValidateConfiguration(UpdateConfiguration configuration)
    {
        if (!configuration.IsConfigured || !RepositoryPart().IsMatch(configuration.GitHubOwner!) || !RepositoryPart().IsMatch(configuration.GitHubRepository!))
            throw new InvalidDataException("Brak poprawnego repozytorium GitHub i klucza aktualizacji.");
        using var key = ECDsa.Create();
        key.ImportFromPem(configuration.PublicKeyPem);
        if (key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
            throw new CryptographicException("Aktualizacje wymagają klucza ECDSA P-256.");
        // The public configuration must never carry a signing/private key.
        if (configuration.PublicKeyPem!.Contains("PRIVATE KEY", StringComparison.Ordinal)) throw new CryptographicException("Konfiguracja nie może zawierać klucza prywatnego.");
    }

    public static LauncherUpdateManifest VerifyManifest(UpdateConfiguration configuration, SignedUpdateEnvelope envelope)
    {
        ValidateConfiguration(configuration);
        if (envelope.PayloadBase64 is null || envelope.PayloadBase64.Length > 24_000 || envelope.SignatureBase64 is null || envelope.SignatureBase64.Length > 256)
            throw new InvalidDataException("Manifest przekracza dozwolony rozmiar.");
        var payload = Convert.FromBase64String(envelope.PayloadBase64);
        var signature = Convert.FromBase64String(envelope.SignatureBase64);
        using var key = ECDsa.Create(); key.ImportFromPem(configuration.PublicKeyPem);
        if (signature.Length != 64 || !key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new CryptographicException("Podpis aktualizacji jest nieprawidłowy.");
        var manifest = JsonSerializer.Deserialize<LauncherUpdateManifest>(payload, UpdateJson.Options) ?? throw new InvalidDataException("Brak manifestu aktualizacji.");
        if (manifest.SchemaVersion != 1 || manifest.AssetName != UpdateConfiguration.ArchiveAssetName || manifest.ArchiveSize < 1 || manifest.ArchiveSize > MaximumArchiveBytes || manifest.ArchiveSha256 is null || !Sha256Pattern().IsMatch(manifest.ArchiveSha256))
            throw new InvalidDataException("Nieprawidłowe dane podpisanego manifestu.");
        _ = LauncherVersion.Parse(manifest.Version);
        return manifest;
    }

    public static void ValidateArchiveUri(UpdateConfiguration configuration, Uri uri, string expectedName)
    {
        var prefix = $"/{configuration.GitHubOwner}/{configuration.GitHubRepository}/releases/download/";
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.Host != "github.com" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || !uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !uri.AbsolutePath.EndsWith("/" + expectedName, StringComparison.Ordinal))
            throw new InvalidDataException("Aktualizacja nie pochodzi z wybranego repozytorium GitHub.");
    }

    public static async Task VerifyArchiveAsync(string path, LauncherUpdateManifest manifest, CancellationToken cancellationToken = default)
    {
        RejectReparsePoint(path);
        var info = new FileInfo(path);
        if (info.Length != manifest.ArchiveSize) throw new InvalidDataException("Rozmiar aktualizacji różni się od podpisanego manifestu.");
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(manifest.ArchiveSha256))) throw new CryptographicException("Suma SHA-256 aktualizacji jest nieprawidłowa.");
    }

    public static string RequireChildPath(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullCandidate = Path.GetFullPath(candidate);
        if (!fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Ścieżka wykracza poza folder aktualizacji.");
        return fullCandidate;
    }

    public static void RejectReparsePoint(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            try
            {
                // FileInfo.Exists is false for directories, including an existing junction.
                // GetAttributes checks the named entry itself for both files and directories.
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Dowiązania i punkty ponownej analizy są niedozwolone podczas aktualizacji.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = new DirectoryInfo(current).Parent?.FullName;
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$", RegexOptions.CultureInvariant)] private static partial Regex RepositoryPart();
    [GeneratedRegex(@"^[0-9A-Fa-f]{64}$", RegexOptions.CultureInvariant)] private static partial Regex Sha256Pattern();
}

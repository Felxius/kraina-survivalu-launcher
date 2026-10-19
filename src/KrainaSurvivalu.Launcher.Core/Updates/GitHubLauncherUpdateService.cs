using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace KrainaSurvivalu.Launcher.Core.Updates;

public sealed class GitHubLauncherUpdateService : ILauncherUpdateService, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _stagingRoot;
    public GitHubLauncherUpdateService(HttpClient? httpClient = null, string? stagingRoot = null)
    {
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        _stagingRoot = Path.GetFullPath(stagingRoot ?? UpdateTrust.DefaultStagingRoot);
    }

    public async Task<LauncherUpdateCheckResult> CheckAsync(UpdateConfiguration configuration, string currentVersion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!configuration.IsConfigured) return new(LauncherUpdateState.Unconfigured, "Aktualizacje launchera są niedostępne: administracja nie skonfigurowała repozytorium i klucza publikacji.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            UpdateTrust.ValidateConfiguration(configuration);
            var current = LauncherVersion.Parse(currentVersion);
            var api = new Uri($"https://api.github.com/repos/{configuration.GitHubOwner}/{configuration.GitHubRepository}/releases/latest");
            using var releaseResponse = await SendAsync(api, deadline.Token);
            if (releaseResponse.StatusCode == HttpStatusCode.NotFound) return new(LauncherUpdateState.Error, "Nie znaleziono opublikowanego wydania launchera w skonfigurowanym repozytorium.");
            releaseResponse.EnsureSuccessStatusCode();
            var releaseBytes = await ReadBoundedAsync(releaseResponse.Content, 512 * 1024, deadline.Token);
            using var release = JsonDocument.Parse(releaseBytes, new JsonDocumentOptions { MaxDepth = 20 });
            var root = release.RootElement;
            if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) throw new InvalidDataException("Wydanie nie jest publicznym wydaniem stabilnym.");
            var tag = root.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("Brak wersji wydania.");
            var assets = root.GetProperty("assets");
            if (assets.ValueKind != JsonValueKind.Array || assets.GetArrayLength() > 100) throw new InvalidDataException("Nieprawidłowa lista plików wydania.");
            var manifestAsset = FindAsset(assets, UpdateConfiguration.ManifestAssetName);
            var archiveAsset = FindAsset(assets, UpdateConfiguration.ArchiveAssetName);
            var manifestUri = GetAssetUri(configuration, manifestAsset, UpdateConfiguration.ManifestAssetName);
            var archiveUri = GetAssetUri(configuration, archiveAsset, UpdateConfiguration.ArchiveAssetName);
            using var manifestResponse = await SendAsync(manifestUri, deadline.Token);
            manifestResponse.EnsureSuccessStatusCode();
            var envelope = JsonSerializer.Deserialize<SignedUpdateEnvelope>(await ReadBoundedAsync(manifestResponse.Content, 32_768, deadline.Token), UpdateJson.Options) ?? throw new InvalidDataException("Brak podpisanego manifestu.");
            var manifest = UpdateTrust.VerifyManifest(configuration, envelope);
            if (tag != manifest.Version && tag != "v" + manifest.Version) throw new InvalidDataException("Wersja manifestu nie odpowiada wydaniu GitHub.");
            if (archiveAsset.GetProperty("size").GetInt64() != manifest.ArchiveSize) throw new InvalidDataException("Rozmiar pliku GitHub różni się od podpisanego manifestu.");
            if (LauncherVersion.Parse(manifest.Version).CompareTo(current) <= 0) return new(LauncherUpdateState.UpToDate, "Masz aktualną wersję launchera. Starsze wydania nie będą instalowane.");
            return new(LauncherUpdateState.Available, $"Dostępna aktualizacja launchera: {manifest.Version}.", new(configuration, envelope, manifest, archiveUri));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new(LauncherUpdateState.Error, "Serwer aktualizacji nie odpowiedział w wyznaczonym czasie."); }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException or System.Security.Cryptography.CryptographicException or FormatException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            return new(LauncherUpdateState.Error, "Nie można bezpiecznie sprawdzić aktualizacji. Sprawdź połączenie i konfigurację publikacji; obecna wersja pozostaje dostępna.");
        }
    }

    public async Task<StagedLauncherUpdate> DownloadAsync(LauncherUpdateDescriptor update, IProgress<LauncherUpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = UpdateTrust.VerifyManifest(update.Configuration, update.Envelope);
        if (manifest != update.Manifest) throw new InvalidDataException("Dane aktualizacji zostały zmienione po weryfikacji.");
        UpdateTrust.ValidateArchiveUri(update.Configuration, update.ArchiveUri, manifest.AssetName);
        UpdateTrust.RejectReparsePoint(_stagingRoot);
        Directory.CreateDirectory(_stagingRoot);
        var stage = UpdateTrust.RequireChildPath(_stagingRoot, Path.Combine(_stagingRoot, Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(stage);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            var archive = Path.Combine(stage, "release.zip");
            using var response = await SendAsync(update.ArchiveUri, deadline.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long advertised && advertised != manifest.ArchiveSize) throw new InvalidDataException("Serwer podał nieprawidłowy rozmiar aktualizacji.");
            await using (var source = await response.Content.ReadAsStreamAsync(deadline.Token))
            await using (var destination = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, true))
            {
                var buffer = new byte[81_920]; long received = 0;
                progress?.Report(new(0, manifest.ArchiveSize));
                int read;
                while ((read = await source.ReadAsync(buffer, deadline.Token)) > 0)
                {
                    received = checked(received + read);
                    if (received > manifest.ArchiveSize) throw new InvalidDataException("Pobrana aktualizacja przekracza podpisany rozmiar.");
                    await destination.WriteAsync(buffer.AsMemory(0, read), deadline.Token);
                    progress?.Report(new(received, manifest.ArchiveSize));
                }
            }
            await UpdateTrust.VerifyArchiveAsync(archive, manifest, deadline.Token);
            await SafeUpdateArchive.ExtractAsync(archive, Path.Combine(stage, "payload"), deadline.Token);
            var receiptPath = Path.Combine(stage, "receipt.json");
            await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(new UpdateStageReceipt(1, update.Envelope, "release.zip"), UpdateJson.Options), deadline.Token);
            return new(manifest.Version, receiptPath, stage);
        }
        catch
        {
            // This directory was freshly created by this call, is under our dedicated staging root,
            // and has never been supplied by a remote manifest.
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            throw;
        }
    }

    private static JsonElement FindAsset(JsonElement assets, string name)
    {
        var found = assets.EnumerateArray().Where(asset => asset.TryGetProperty("name", out var value) && value.GetString() == name).ToArray();
        if (found.Length != 1) throw new InvalidDataException("Brak jednego jednoznacznego pliku aktualizacji w wydaniu.");
        return found[0];
    }

    private static Uri GetAssetUri(UpdateConfiguration configuration, JsonElement asset, string name)
    {
        var uri = new Uri(asset.GetProperty("browser_download_url").GetString() ?? "", UriKind.Absolute);
        UpdateTrust.ValidateArchiveUri(configuration, uri, name);
        return uri;
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (var redirect = 0; redirect < 6; redirect++)
        {
            if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Host is not ("api.github.com" or "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("Niedozwolone przekierowanie pobierania aktualizacji.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("KrainaSurvivalu-Launcher/1.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null) throw new InvalidDataException("Brak adresu przekierowania aktualizacji.");
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
        }
        throw new InvalidDataException("Zbyt wiele przekierowań aktualizacji.");
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maximum, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maximum) throw new InvalidDataException("Odpowiedź aktualizacji jest zbyt duża.");
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > maximum) throw new InvalidDataException("Odpowiedź aktualizacji jest zbyt duża.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}

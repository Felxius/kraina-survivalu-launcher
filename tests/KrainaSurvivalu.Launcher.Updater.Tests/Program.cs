using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KrainaSurvivalu.Launcher.Core.Updates;

var passed = 0; var failed = 0; var skipped = 0;
await Test("Brak konfiguracji nie wykonuje połączenia", async () =>
{
    using var fixture = new Fixture(); using var service = fixture.Service();
    var result = await service.CheckAsync(new(), "0.6.0-alpha");
    Equal(LauncherUpdateState.Unconfigured, result.State); Equal(0, fixture.Handler.Requests);
});
await Test("Poprawny podpis i nowsza wersja udostępniają aktualizację", async () =>
{
    using var fixture = new Fixture(); using var service = fixture.Service();
    var result = await service.CheckAsync(fixture.Config, "0.6.0-alpha");
    Equal(LauncherUpdateState.Available, result.State); Equal("999.0.0", result.Update!.Manifest.Version); Equal(2, fixture.Handler.Requests);
});
await Test("Ta sama wersja nie jest ponownie instalowana", async () =>
{
    using var fixture = new Fixture(); using var service = fixture.Service();
    Equal(LauncherUpdateState.UpToDate, (await service.CheckAsync(fixture.Config, "999.0.0")).State);
});
await Test("Starsze wydanie jest blokowane", async () =>
{
    using var fixture = new Fixture(); using var service = fixture.Service();
    Equal(LauncherUpdateState.UpToDate, (await service.CheckAsync(fixture.Config, "1000.0.0")).State);
});
await Test("SemVer uwzględnia alpha, numery identyfikatorów i ignoruje build metadata", () =>
{
    True(LauncherVersion.Parse("1.0.0-alpha.9").CompareTo(LauncherVersion.Parse("1.0.0-alpha.10")) < 0);
    True(LauncherVersion.Parse("1.0.0-alpha").CompareTo(LauncherVersion.Parse("1.0.0")) < 0);
    Equal(0, LauncherVersion.Parse("1.0.0+build.1").CompareTo(LauncherVersion.Parse("1.0.0+build.2")));
    Throws<FormatException>(() => LauncherVersion.Parse("1.0.0-01"));
    Throws<FormatException>(() => LauncherVersion.Parse("01.0.0"));
    return Task.CompletedTask;
});
await Test("Zmieniony podpis zostaje odrzucony", async () =>
{
    using var fixture = new Fixture(); fixture.Envelope = fixture.Envelope with { SignatureBase64 = Convert.ToBase64String(new byte[64]) };
    using var service = fixture.Service(); Equal(LauncherUpdateState.Error, (await service.CheckAsync(fixture.Config, "0.6.0-alpha")).State);
});
await Test("Zmieniony surowy payload zostaje odrzucony", async () =>
{
    using var fixture = new Fixture(); var bytes = Convert.FromBase64String(fixture.Envelope.PayloadBase64); bytes[^2] ^= 1;
    fixture.Envelope = fixture.Envelope with { PayloadBase64 = Convert.ToBase64String(bytes) };
    using var service = fixture.Service(); Equal(LauncherUpdateState.Error, (await service.CheckAsync(fixture.Config, "0.6.0-alpha")).State);
});
await Test("Klucz innej administracji nie weryfikuje wydania", async () =>
{
    using var fixture = new Fixture(); using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    using var service = fixture.Service(); Equal(LauncherUpdateState.Error, (await service.CheckAsync(fixture.Config with { PublicKeyPem = other.ExportSubjectPublicKeyInfoPem() }, "0.6.0-alpha")).State);
});
await Test("Klucz prywatny w publicznej konfiguracji jest odrzucany", () =>
{
    using var fixture = new Fixture(); Throws<CryptographicException>(() => UpdateTrust.ValidateConfiguration(fixture.Config with { PublicKeyPem = fixture.Key.ExportPkcs8PrivateKeyPem() })); return Task.CompletedTask;
});
await Test("Klucz innej krzywej jest odrzucany", () =>
{
    using var fixture = new Fixture(); using var wrong = ECDsa.Create(ECCurve.NamedCurves.nistP384);
    Throws<CryptographicException>(() => UpdateTrust.ValidateConfiguration(fixture.Config with { PublicKeyPem = wrong.ExportSubjectPublicKeyInfoPem() })); return Task.CompletedTask;
});
await Test("Zły tag wydania jest odrzucany", async () =>
{
    using var fixture = new Fixture(); fixture.Tag = "v998.0.0"; using var service = fixture.Service(); Equal(LauncherUpdateState.Error, (await service.CheckAsync(fixture.Config, "0.6.0-alpha")).State);
});
await Test("Brak jednego z dwóch assetów jest odrzucany", async () =>
{
    using var fixture = new Fixture(); fixture.MissingArchive = true; using var service = fixture.Service(); Equal(LauncherUpdateState.Error, (await service.CheckAsync(fixture.Config, "0.6.0-alpha")).State);
});
await Test("Duplikat assetu jest odrzucany", async () =>
{
    using var fixture = new Fixture(); fixture.DuplicateArchive = true; using var service = fixture.Service(); Equal(LauncherUpdateState.Error, (await service.CheckAsync(fixture.Config, "0.6.0-alpha")).State);
});
await Test("GitHub draft i prerelease nie wchodzą do kanału latest", async () =>
{
    using var fixture = new Fixture(); fixture.Draft = true; using var service = fixture.Service(); Equal(LauncherUpdateState.Error, (await service.CheckAsync(fixture.Config, "0.6.0-alpha")).State);
    fixture.Draft = false; fixture.Prerelease = true; Equal(LauncherUpdateState.Error, (await service.CheckAsync(fixture.Config, "0.6.0-alpha")).State);
});
await Test("Odpowiedź 404 i rate limit nie udają dostępnej aktualizacji", async () =>
{
    using var fixture = new Fixture(); fixture.Handler.ApiStatus = HttpStatusCode.NotFound; using var service = fixture.Service(); Equal(LauncherUpdateState.Error, (await service.CheckAsync(fixture.Config, "0.6.0-alpha")).State);
    fixture.Handler.ApiStatus = HttpStatusCode.Forbidden; Equal(LauncherUpdateState.Error, (await service.CheckAsync(fixture.Config, "0.6.0-alpha")).State);
});
await Test("HTTP lub obce repozytorium nie może dostarczyć ZIP-a", () =>
{
    using var fixture = new Fixture();
    foreach (var uri in new[] { "http://github.com/TestAdmin/TestLauncher/releases/download/v999.0.0/KS-Launcher-Windows-x64.zip", "https://example.com/TestAdmin/TestLauncher/releases/download/v999.0.0/KS-Launcher-Windows-x64.zip", "https://github.com/Other/Repo/releases/download/v999.0.0/KS-Launcher-Windows-x64.zip" }) Throws<InvalidDataException>(() => UpdateTrust.ValidateArchiveUri(fixture.Config, new(uri), UpdateConfiguration.ArchiveAssetName));
    return Task.CompletedTask;
});
await Test("Anulowanie kontroli jest przekazywane wyżej", async () =>
{
    using var fixture = new Fixture(); using var service = fixture.Service(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    await ThrowsAsync<OperationCanceledException>(() => service.CheckAsync(fixture.Config, "0.6.0-alpha", cancellation.Token)); Equal(0, fixture.Handler.Requests);
});
await Test("Weryfikowane pobranie tworzy pełen staging i postęp", async () =>
{
    using var fixture = new Fixture(); using var service = fixture.Service(); var result = await service.CheckAsync(fixture.Config, "0.6.0-alpha");
    var progress = new ImmediateProgress(); var staged = await service.DownloadAsync(result.Update!, progress);
    True(File.Exists(staged.ReceiptPath)); True(File.Exists(Path.Combine(staged.StagingDirectory, "payload", UpdateTrust.ApplicationFileName))); Equal(fixture.Archive.LongLength, progress.Last.BytesReceived);
});
await Test("Podmieniony ZIP o tej samej długości jest odrzucany i usuwany", async () =>
{
    using var fixture = new Fixture(); using var service = fixture.Service(); var result = await service.CheckAsync(fixture.Config, "0.6.0-alpha"); fixture.Archive[5] ^= 1;
    await ThrowsAsync<CryptographicException>(() => service.DownloadAsync(result.Update!)); Equal(0, Directory.GetDirectories(fixture.StageRoot).Length);
});
await Test("Przekroczenie podpisanego rozmiaru jest odrzucane", async () =>
{
    using var fixture = new Fixture(); using var service = fixture.Service(); var result = await service.CheckAsync(fixture.Config, "0.6.0-alpha"); fixture.Archive = [.. fixture.Archive, 0];
    await ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(result.Update!));
});
await Test("Zmieniony deskryptor pobrania zostaje odrzucony", async () =>
{
    using var fixture = new Fixture(); using var service = fixture.Service(); var result = await service.CheckAsync(fixture.Config, "0.6.0-alpha");
    await ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(result.Update! with { Manifest = result.Update!.Manifest with { Version = "998.0.0" } }));
});
foreach (var maliciousPath in new[] { "../outside.txt", "/absolute.txt", "C:/absolute.txt", "folder/../../escape.txt", "folder/name:stream", "folder/CON.txt", "folder/endswithdot.", "folder/endswithspace ", "folder\\..\\escape.txt" })
{
    await Test("ZIP odrzuca ścieżkę: " + maliciousPath, async () =>
    {
        using var fixture = new Fixture(); var archive = fixture.WriteArchive("malicious.zip", Fixture.MakeArchive(fixture.Config, [(maliciousPath, Encoding.UTF8.GetBytes("blocked"))]));
        await ThrowsAsync<InvalidDataException>(() => SafeUpdateArchive.ExtractAsync(archive, Path.Combine(fixture.Root, "extract")));
        True(!File.Exists(Path.Combine(fixture.Root, "outside.txt")));
    });
}
await Test("ZIP odrzuca nazwy różniące się wyłącznie wielkością liter", async () =>
{
    using var fixture = new Fixture(); var archive = fixture.WriteArchive("duplicate.zip", Fixture.MakeArchive(fixture.Config, [("duplicate.txt", [1]), ("DUPLICATE.txt", [2])]));
    await ThrowsAsync<InvalidDataException>(() => SafeUpdateArchive.ExtractAsync(archive, Path.Combine(fixture.Root, "extract")));
});
await Test("ZIP odrzuca dowiązanie symboliczne", async () =>
{
    using var fixture = new Fixture(); using var memory = new MemoryStream();
    using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) { var entry = zip.CreateEntry("link"); entry.ExternalAttributes = unchecked((int)0xA1FF0000); using var writer = new StreamWriter(entry.Open()); writer.Write("../outside"); }
    var path = fixture.WriteArchive("symlink.zip", memory.ToArray()); await ThrowsAsync<InvalidDataException>(() => SafeUpdateArchive.ExtractAsync(path, Path.Combine(fixture.Root, "extract")));
});
await Test("Guard odrzuca katalog będący dowiązaniem i nieistniejące dziecko w nim", () =>
{
    using var fixture = new Fixture();
    var target = Path.Combine(fixture.Root, "real-directory");
    var link = Path.Combine(fixture.Root, "directory-link");
    Directory.CreateDirectory(target);
    try { Directory.CreateSymbolicLink(link, target); }
    catch (UnauthorizedAccessException) { throw new SkipTestException("Windows nie zezwolił na utworzenie dowiązania katalogu bez uprawnień administratora lub trybu dewelopera."); }
    catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xFFFF) is 1314 or 5) { throw new SkipTestException("Windows odmówił uprawnienia do utworzenia dowiązania katalogu (kod Win32 " + (error.HResult & 0xFFFF) + ")."); }
    catch (PlatformNotSupportedException) { throw new SkipTestException("System plików nie obsługuje dowiązań katalogów."); }
    try
    {
        True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
        Throws<InvalidDataException>(() => UpdateTrust.RejectReparsePoint(link));
        Throws<InvalidDataException>(() => UpdateTrust.RejectReparsePoint(Path.Combine(link, "missing", "receipt.json")));
        UpdateTrust.RejectReparsePoint(target);
    }
    finally { Directory.Delete(link); }
    return Task.CompletedTask;
});
await Test("ZIP bez launchera i pomocnika jest odrzucany", async () =>
{
    using var fixture = new Fixture(); using var memory = new MemoryStream(); using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) { using var writer = new StreamWriter(zip.CreateEntry("readme.txt").Open()); writer.Write("missing app"); }
    await ThrowsAsync<InvalidDataException>(() => SafeUpdateArchive.ExtractAsync(fixture.WriteArchive("incomplete.zip", memory.ToArray()), Path.Combine(fixture.Root, "extract")));
});
foreach (var missingFile in new[] { "KrainaSurvivalu.Launcher.Core.dll", "KrainaSurvivalu.Launcher.deps.json", "Updater/KrainaSurvivalu.Launcher.Updater.dll", "SteamWorker/KrainaSurvivalu.SteamWorker.exe", "SteamWorker/coreclr.dll" })
{
    await Test("ZIP odrzuca niepełną publikację bez: " + missingFile, async () =>
    {
        using var fixture = new Fixture();
        var bytes = Fixture.MakeArchive(fixture.Config, [], omitFile: missingFile);
        await ThrowsAsync<InvalidDataException>(() => SafeUpdateArchive.ExtractAsync(fixture.WriteArchive("missing.zip", bytes), Path.Combine(fixture.Root, "extract")));
    });
}
await Test("Wpis katalogu o nazwie EXE nie zastępuje wymaganego pliku", async () =>
{
    using var fixture = new Fixture();
    var bytes = Fixture.MakeArchive(fixture.Config, [], applicationIsDirectory: true);
    await ThrowsAsync<InvalidDataException>(() => SafeUpdateArchive.ExtractAsync(fixture.WriteArchive("directory-exe.zip", bytes), Path.Combine(fixture.Root, "extract")));
});
await Test("ZIP nie może podmieniać pliku blokady aktualizacji", async () =>
{
    using var fixture = new Fixture();
    var bytes = Fixture.MakeArchive(fixture.Config, [(UpdateTrust.InstallLockFileName, [1])]);
    await ThrowsAsync<InvalidDataException>(() => SafeUpdateArchive.ExtractAsync(fixture.WriteArchive("lock-file.zip", bytes), Path.Combine(fixture.Root, "extract")));
});
await Test("ZIP z nadmierną kompresją jest odrzucany", async () =>
{
    using var fixture = new Fixture(); var archive = Fixture.MakeArchive(fixture.Config, [("bomb.bin", new byte[2 * 1024 * 1024])]);
    await ThrowsAsync<InvalidDataException>(() => SafeUpdateArchive.ExtractAsync(fixture.WriteArchive("bomb.zip", archive), Path.Combine(fixture.Root, "extract")));
});
await Test("Anulowanie pobierania nie tworzy instalacji", async () =>
{
    using var fixture = new Fixture(); using var service = fixture.Service(); var check = await service.CheckAsync(fixture.Config, "0.6.0-alpha"); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    await ThrowsAsync<OperationCanceledException>(() => service.DownloadAsync(check.Update!, null, cancelled.Token)); True(!Directory.Exists(fixture.StageRoot));
});
await Test("Apply ponownie weryfikuje ZIP zamiast ufać edytowanemu payloadowi", async () =>
{
    using var fixture = new Fixture(); var staged = await fixture.DownloadAsync(); var install = fixture.CreateInstall();
    await File.WriteAllTextAsync(Path.Combine(staged.StagingDirectory, "payload", UpdateTrust.ApplicationFileName), "tampered staged file");
    await LauncherUpdateInstaller.ApplyForTestingAsync(staged.ReceiptPath, install, fixture.StageRoot, null);
    Equal("new launcher fixture", await File.ReadAllTextAsync(Path.Combine(install, UpdateTrust.ApplicationFileName))); True(File.Exists(Path.Combine(staged.StagingDirectory, "applied.json")));
});
await Test("Apply odrzuca sfałszowany receipt i nie dotyka instalacji", async () =>
{
    using var fixture = new Fixture(); var staged = await fixture.DownloadAsync(); var install = fixture.CreateInstall();
    var receipt = new UpdateStageReceipt(1, fixture.Envelope with { SignatureBase64 = Convert.ToBase64String(new byte[64]) }, "release.zip"); await File.WriteAllTextAsync(staged.ReceiptPath, JsonSerializer.Serialize(receipt, UpdateJson.Options));
    await ThrowsAsync<CryptographicException>(() => LauncherUpdateInstaller.ApplyForTestingAsync(staged.ReceiptPath, install, fixture.StageRoot, null)); Equal("old launcher fixture", await File.ReadAllTextAsync(Path.Combine(install, UpdateTrust.ApplicationFileName)));
});
await Test("Apply odrzuca receipt spoza własnego stagingu", async () =>
{
    using var fixture = new Fixture(); var install = fixture.CreateInstall(); var receipt = Path.Combine(fixture.Root, "receipt.json"); await File.WriteAllTextAsync(receipt, "{}");
    await ThrowsAsync<InvalidDataException>(() => LauncherUpdateInstaller.ApplyForTestingAsync(receipt, install, fixture.StageRoot, null));
});
await Test("Apply blokuje cofnięcie wersji na podstawie zainstalowanego DLL", async () =>
{
    using var fixture = new Fixture("0.0.1"); var staged = await fixture.DownloadAsync("0.0.0"); var install = fixture.CreateInstall();
    await ThrowsAsync<InvalidDataException>(() => LauncherUpdateInstaller.ApplyForTestingAsync(staged.ReceiptPath, install, fixture.StageRoot, null));
});
await Test("Błąd wymiany przywraca pliki i zachowuje kopię", async () =>
{
    using var fixture = new Fixture(); var staged = await fixture.DownloadAsync(); var install = fixture.CreateInstall(); var originalConfig = File.ReadAllText(Path.Combine(install, "config", "updates.json"));
    var copied = 0;
    await ThrowsAsync<IOException>(() => LauncherUpdateInstaller.ApplyForTestingAsync(staged.ReceiptPath, install, fixture.StageRoot, _ => { if (++copied == 2) throw new IOException("Test rollback"); }));
    Equal("old launcher fixture", await File.ReadAllTextAsync(Path.Combine(install, UpdateTrust.ApplicationFileName))); Equal(originalConfig, File.ReadAllText(Path.Combine(install, "config", "updates.json"))); True(Directory.GetDirectories(staged.StagingDirectory, "backup-*").Length == 1); True(!File.Exists(Path.Combine(staged.StagingDirectory, "applied.json")));
});
await Test("Równoległa aktualizacja tej samej instalacji jest blokowana przez uchwyt pliku", async () =>
{
    using var fixture = new Fixture();
    var firstStage = await fixture.DownloadAsync();
    var secondStage = await fixture.DownloadAsync();
    var install = fixture.CreateInstall();
    using var releaseFirst = new ManualResetEventSlim(false);
    var reachedCopy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var copied = 0;
    var firstApply = Task.Run(() => LauncherUpdateInstaller.ApplyForTestingAsync(firstStage.ReceiptPath, install, fixture.StageRoot, _ =>
    {
        if (Interlocked.Increment(ref copied) != 1) return;
        reachedCopy.SetResult();
        if (!releaseFirst.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Test nie zwolnił pierwszej aktualizacji.");
    }));
    try
    {
        await reachedCopy.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await ThrowsAsync<IOException>(() => LauncherUpdateInstaller.ApplyForTestingAsync(secondStage.ReceiptPath, install, fixture.StageRoot, null));
        True(!File.Exists(Path.Combine(secondStage.StagingDirectory, "applied.json")));
    }
    finally { releaseFirst.Set(); }
    await firstApply;
    Equal("new launcher fixture", await File.ReadAllTextAsync(Path.Combine(install, UpdateTrust.ApplicationFileName)));
    await ThrowsAsync<InvalidDataException>(() => LauncherUpdateInstaller.ApplyForTestingAsync(secondStage.ReceiptPath, install, fixture.StageRoot, null));
});
Console.WriteLine($"WYNIK: {passed} PASS / {failed} FAIL / {skipped} SKIP. Testy bez sieci, procesów gry, publikacji i trwałych kluczy prywatnych.");
return failed == 0 ? 0 : 1;

async Task Test(string name, Func<Task> action) { try { await action(); passed++; Console.WriteLine("PASS " + name); } catch (SkipTestException skippedTest) { skipped++; Console.WriteLine($"SKIP {name}: {skippedTest.Message}"); } catch (Exception error) { failed++; Console.WriteLine($"FAIL {name}: {error}"); } }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Oczekiwano {expected}, otrzymano {actual}."); }
static void True(bool condition) { if (!condition) throw new Exception("Warunek nie został spełniony."); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Oczekiwano " + typeof(T).Name); }
static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Oczekiwano " + typeof(T).Name); }

sealed class ImmediateProgress : IProgress<LauncherUpdateProgress>
{
    public LauncherUpdateProgress Last { get; private set; } = new(0, 0);
    public void Report(LauncherUpdateProgress value) => Last = value;
}

sealed class SkipTestException(string message) : Exception(message);

sealed class Fixture : IDisposable
{
    public ECDsa Key { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "KS-Updater-Tests-" + Guid.NewGuid().ToString("N"));
    public string StageRoot => Path.Combine(Root, "stages");
    public UpdateConfiguration Config { get; }
    public byte[] Archive { get; set; }
    public SignedUpdateEnvelope Envelope { get; set; }
    public LauncherUpdateManifest Manifest { get; }
    public string Tag { get; set; }
    public bool MissingArchive { get; set; }
    public bool DuplicateArchive { get; set; }
    public bool Draft { get; set; }
    public bool Prerelease { get; set; }
    public FakeHandler Handler { get; }
    public Fixture(string version = "999.0.0")
    {
        Directory.CreateDirectory(Root);
        Config = new() { GitHubOwner = "TestAdmin", GitHubRepository = "TestLauncher", PublicKeyPem = Key.ExportSubjectPublicKeyInfoPem() };
        Archive = MakeArchive(Config, []);
        Manifest = new(1, version, UpdateConfiguration.ArchiveAssetName, Archive.LongLength, Convert.ToHexString(SHA256.HashData(Archive)).ToLowerInvariant(), DateTimeOffset.UtcNow);
        var payload = JsonSerializer.SerializeToUtf8Bytes(Manifest, UpdateJson.Options);
        Envelope = new(Convert.ToBase64String(payload), Convert.ToBase64String(Key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
        Tag = "v" + version;
        Handler = new(this);
    }
    public GitHubLauncherUpdateService Service() => new(new HttpClient(Handler, false), StageRoot);
    public async Task<StagedLauncherUpdate> DownloadAsync(string current = "0.6.0-alpha") { using var service = Service(); var check = await service.CheckAsync(Config, current); return await service.DownloadAsync(check.Update ?? throw new Exception(check.Message)); }
    public string WriteArchive(string name, byte[] data) { var path = Path.Combine(Root, name); File.WriteAllBytes(path, data); return path; }
    public string CreateInstall()
    {
        var install = Path.Combine(Root, "installed"); Directory.CreateDirectory(Path.Combine(install, "config"));
        File.WriteAllText(Path.Combine(install, UpdateTrust.ApplicationFileName), "old launcher fixture");
        File.Copy(typeof(LauncherVersion).Assembly.Location, Path.Combine(install, "KrainaSurvivalu.Launcher.dll"));
        File.WriteAllText(Path.Combine(install, "config", "updates.json"), JsonSerializer.Serialize(Config, UpdateJson.Options)); return install;
    }
    public static byte[] MakeArchive(UpdateConfiguration config, (string Name, byte[] Bytes)[] extras, string? omitFile = null, bool applicationIsDirectory = false)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [UpdateTrust.ApplicationFileName] = Encoding.UTF8.GetBytes("new launcher fixture"),
                ["KrainaSurvivalu.Launcher.dll"] = File.ReadAllBytes(typeof(Fixture).Assembly.Location),
                ["config/updates.json"] = JsonSerializer.SerializeToUtf8Bytes(config, UpdateJson.Options)
            };
            var entries = SafeUpdateArchive.RequiredFileNames.Where(name => name != omitFile).Select(name => (Name: name, Bytes: contents.GetValueOrDefault(name) ?? Encoding.UTF8.GetBytes("required runtime fixture"))).Concat(extras);
            foreach (var (name, bytes) in entries)
            {
                var directory = applicationIsDirectory && name == UpdateTrust.ApplicationFileName;
                var entry = zip.CreateEntry(directory ? name + "/" : name, CompressionLevel.Optimal);
                if (directory) continue;
                using var output = entry.Open(); output.Write(bytes);
            }
        }
        return stream.ToArray();
    }
    public byte[] ReleaseJson()
    {
        var assets = new List<object> { new { name = UpdateConfiguration.ManifestAssetName, browser_download_url = $"https://github.com/TestAdmin/TestLauncher/releases/download/{Tag}/{UpdateConfiguration.ManifestAssetName}", size = 1024L } };
        var archive = new { name = UpdateConfiguration.ArchiveAssetName, browser_download_url = $"https://github.com/TestAdmin/TestLauncher/releases/download/{Tag}/{UpdateConfiguration.ArchiveAssetName}", size = Manifest.ArchiveSize };
        if (!MissingArchive) assets.Add(archive); if (DuplicateArchive) assets.Add(archive);
        return JsonSerializer.SerializeToUtf8Bytes(new { tag_name = Tag, draft = Draft, prerelease = Prerelease, assets });
    }
    public void Dispose() { Key.Dispose(); Handler.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}

sealed class FakeHandler(Fixture fixture) : HttpMessageHandler
{
    public int Requests { get; private set; }
    public HttpStatusCode ApiStatus { get; set; } = HttpStatusCode.OK;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); Requests++;
        byte[] bytes;
        var uri = request.RequestUri ?? throw new Exception("Brak URI.");
        if (uri.Host == "api.github.com") { if (ApiStatus != HttpStatusCode.OK) return Task.FromResult(new HttpResponseMessage(ApiStatus)); bytes = fixture.ReleaseJson(); }
        else if (uri.Host == "github.com" && uri.AbsolutePath.EndsWith(UpdateConfiguration.ManifestAssetName, StringComparison.Ordinal)) bytes = JsonSerializer.SerializeToUtf8Bytes(fixture.Envelope, UpdateJson.Options);
        else if (uri.Host == "github.com" && uri.AbsolutePath.EndsWith(UpdateConfiguration.ArchiveAssetName, StringComparison.Ordinal)) bytes = fixture.Archive;
        else throw new Exception("Test nigdy nie wykonuje połączenia z siecią.");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
}

using System.Text.Json;
using KrainaSurvivalu.Launcher.Core;

var testRoot = Path.Combine(AppContext.BaseDirectory, "test-artifacts");
var testDirectory = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testDirectory);
var failures = 0;
var passes = 0;
var catalogPath = Path.Combine(testDirectory, "servers.json");
try
{
    await Test("Dokładne endpointy i jawne Query od właściciela; listy modów pozostają nieustalone", async () =>
    {
        await WriteCatalog([
            new("badlands", "Badlands", "145.239.237.112", 27800, 27801),
            new("deer-isle", "Deer Isle", "178.95.158.100", 23000, 9000)
        ]);
        var catalog = await ServerCatalog.LoadAsync(catalogPath);
        Check(catalog.Count == 2 && catalog[0].Host == "145.239.237.112" && catalog[0].GamePort == 27800 && catalog[0].QueryPort == 27801,
            "Zmieniono endpoint Badlands.");
        Check(catalog[1].Host == "178.95.158.100" && catalog[1].GamePort == 23000 && catalog[1].QueryPort == 9000, "Zmieniono endpoint Deer Isle.");
        Check(catalog.All(server => server.RequiredWorkshopIds is null), "Brak list modów został zamieniony w wymyślone wartości.");
    });
    await Test("Znana pusta lista i nieustalona lista modów są odrębne", async () =>
    {
        await WriteCatalog([new("unknown", "Unknown", "example.org", 2302), new("empty", "Empty", "127.0.0.1", 2302, RequiredWorkshopIds: [])]);
        var result = await ServerCatalog.LoadAsync(catalogPath);
        Check(result[0].RequiredWorkshopIds is null && result[1].RequiredWorkshopIds?.Count == 0, "Zgubiono rozróżnienie null i [].");
    });
    await Test("Znane Query, HTTPS i dodatnie ID są zachowane", async () =>
    {
        await WriteCatalog([new("valid", "Valid", "dayz.example.org", 2302, 27016, ["1559212036", "18446744073709551615"], "https://discord.gg/example", "https://example.org/rules", "https://example.org/news")]);
        var result = await ServerCatalog.LoadAsync(catalogPath);
        Check(result[0].QueryPort == 27016 && result[0].RequiredWorkshopIds?.Count == 2, "Zmieniono jawnie podane dane.");
    });
    await Test("Zduplikowane Id są odrzucane również przy innej wielkości liter", async () =>
    {
        await WriteCatalog([Server() with { Id = "same" }, Server() with { Id = "SAME" }]);
        await RejectCatalog();
    });
    await Test("Nieprawidłowe porty gry i Query są odrzucane", async () =>
    {
        foreach (var entry in new[] { Server() with { GamePort = 0 }, Server() with { GamePort = 65536 }, Server() with { QueryPort = -1 }, Server() with { QueryPort = 65536 } })
        {
            await WriteCatalog([entry]);
            await RejectCatalog();
        }
    });
    await Test("Host nie może zawierać argumentów, URL ani białych znaków", async () =>
    {
        foreach (var host in new[] { "54.37.235.206 -mod=evil", "https://example.org", "-connect=example.org", "host;evil", "host\nother", "host:2302", "[::1]", "" })
        {
            await WriteCatalog([Server() with { Host = host }]);
            await RejectCatalog();
        }
    });
    await Test("ID Workshop odrzucają zero, znaki, przepełnienie i duplikaty", async () =>
    {
        foreach (var ids in new string[][] { ["0"], ["-1"], ["1;2"], ["18446744073709551616"], ["1", "01"], [null!] })
        {
            await WriteCatalog([Server() with { RequiredWorkshopIds = ids }]);
            await RejectCatalog();
        }
    });
    await Test("Linki zewnętrzne wymagają HTTPS bez loginu", async () =>
    {
        foreach (var url in new[] { "http://example.org", "javascript:alert(1)", "file:///C:/test", "https://user:secret@example.org/", " https://example.org", "" })
        {
            await WriteCatalog([Server() with { DiscordUrl = url }]);
            await RejectCatalog();
        }
    });
    await Test("Nieprawidłowy schemat, brak listy i nieznane pola są odrzucane", async () =>
    {
        foreach (var json in new[] { "null", "{}", "{\"schemaVersion\":2,\"servers\":[]}", "{\"schemaVersion\":1,\"servers\":[]}", "{\"schemaVersion\":1,\"servers\":[null]}", "{\"schemaVersion\":1,\"servers\":[],\"unexpected\":true}", "{bad" })
        {
            await File.WriteAllTextAsync(catalogPath, json);
            await RejectCatalog();
        }
    });
    await Test("Workshop nie obiecuje synchronizacji dla null ani jawnej listy", () =>
    {
        var service = new WorkshopService();
        Check(!service.GetAvailability(Server()).IsAvailable, "Nieustalona konfiguracja stała się dostępna.");
        Check(!service.GetAvailability(Server() with { RequiredWorkshopIds = ["1"] }).IsAvailable,
            "Sama lista z katalogu nie potwierdza gotowości modów na komputerze.");
        return Task.CompletedTask;
    });
    await Test("Brak ustawień daje pustą konfigurację bez ostrzeżenia", async () =>
    {
        var result = await new SettingsStore(Path.Combine(testDirectory, "missing", "settings.json")).LoadAsync();
        Check(result.Settings == new LauncherSettings() && result.Warning is null, "Brak pliku nie został bezpiecznie obsłużony.");
    });
    await Test("Atomowy zapis i zastąpienie ustawień; brak osieroconych plików tymczasowych", async () =>
    {
        var path = Path.Combine(testDirectory, "settings-roundtrip", "settings.json");
        var store = new SettingsStore(path);
        var first = Settings("first");
        var second = Settings("second");
        await store.SaveAsync(first);
        Check((await store.LoadAsync()).Settings == first, "Pierwszy zapis nie wrócił bez zmian.");
        await store.SaveAsync(second);
        var loaded = await store.LoadAsync();
        Check(loaded.Settings == second && loaded.Warning is null, "Zastąpienie ustawień nie zachowało danych.");
        Check(Directory.GetFiles(Path.GetDirectoryName(path)!).Length == 1, "Pozostały pliki tymczasowe.");
    });
    await Test("Uszkodzony JSON trafia do kopii; odczyt zwraca ostrzeżenie i puste ustawienia", async () =>
    {
        var path = Path.Combine(testDirectory, "corrupt-settings.json");
        const string damaged = "{broken-json";
        await File.WriteAllTextAsync(path, damaged);
        var result = await new SettingsStore(path).LoadAsync();
        Check(result.Settings == new LauncherSettings() && !string.IsNullOrEmpty(result.Warning), "Nie zgłoszono odzyskania ustawień.");
        var backup = Directory.GetFiles(testDirectory, "corrupt-settings.json.invalid-*.bak");
        Check(backup.Length == 1 && await File.ReadAllTextAsync(backup[0]) == damaged && !File.Exists(path),
            "Nie zachowano oryginalnej uszkodzonej zawartości.");
    });
    await Test("Nieprawidłowa wersja, null i względne ścieżki nie stają się aktywnymi ustawieniami", async () =>
    {
        var path = Path.Combine(testDirectory, "invalid-settings.json");
        foreach (var json in new[] { "{\"schemaVersion\":2,\"dayZDirectory\":\"\",\"steamExecutable\":\"\"}", "{\"schemaVersion\":1,\"dayZDirectory\":null,\"steamExecutable\":\"\"}", "{\"schemaVersion\":1,\"dayZDirectory\":\"relative\",\"steamExecutable\":\"\"}" })
        {
            await File.WriteAllTextAsync(path, json);
            var result = await new SettingsStore(path).LoadAsync();
            Check(result.Settings == new LauncherSettings() && result.Warning is not null, "Przyjęto nieprawidłowe ustawienia.");
        }
    });
    await Test("Anulowany zapis nie zmienia poprzednich ustawień", async () =>
    {
        var path = Path.Combine(testDirectory, "canceled-settings.json");
        var store = new SettingsStore(path);
        var first = Settings("cancel-first");
        await store.SaveAsync(first);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Throws<OperationCanceledException>(() => store.SaveAsync(Settings("cancel-second"), cancellation.Token));
        Check((await store.LoadAsync()).Settings == first, "Anulowanie naruszyło istniejące ustawienia.");
    });
    await Test("Po zablokowanym odczycie zapis zachowuje oryginalne bajty w kopii", async () =>
    {
        var path = Path.Combine(testDirectory, "locked-settings.json");
        var originalBytes = new byte[] { 0x7B, 0x00, 0xC3, 0x28, 0xFE, 0xFF, 0x0A };
        await File.WriteAllBytesAsync(path, originalBytes);
        var store = new SettingsStore(path);
        await using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = await store.LoadAsync();
            Check(result.Settings == new LauncherSettings() && result.Warning is not null, "Zablokowany odczyt nie zgłosił ostrzeżenia.");
        }
        var replacement = Settings("recover-after-lock");
        await store.SaveAsync(replacement);
        Check((await store.LoadAsync()).Settings == replacement, "Nie zapisano nowych ustawień po ustaniu blokady.");
        var backups = Directory.GetFiles(testDirectory, "locked-settings.json.recovery-*.bak");
        Check(backups.Length == 1 && (await File.ReadAllBytesAsync(backups[0])).SequenceEqual(originalBytes),
            "Nadpisano nieodczytany oryginał bez zachowania dokładnej kopii.");
    });
    await Test("Nadal zablokowany oryginał wstrzymuje zapis zamiast go nadpisywać", async () =>
    {
        var path = Path.Combine(testDirectory, "still-locked-settings.json");
        var originalBytes = new byte[] { 1, 2, 3, 4, 5 };
        await File.WriteAllBytesAsync(path, originalBytes);
        var store = new SettingsStore(path);
        await using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Check((await store.LoadAsync()).Warning is not null, "Oczekiwano ostrzeżenia o blokadzie.");
            await Throws<IOException>(() => store.SaveAsync(Settings("blocked-save")));
        }
        Check((await File.ReadAllBytesAsync(path)).SequenceEqual(originalBytes), "Nieudany zapis naruszył oryginalne bajty.");
        Check(Directory.GetFiles(testDirectory, ".still-locked-settings.json.*.tmp").Length == 0,
            "Nie posprzątano tymczasowego zapisu po zablokowanej kopii.");
    });
    await Test("Walidacja sprawdza oba pliki gry i konkretny steam.exe", async () =>
    {
        var settings = Settings("path-validation");
        Directory.CreateDirectory(settings.DayZDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(settings.SteamExecutable)!);
        Check(!PathValidation.Validate(settings).IsValid, "Pusty folder został uznany za grę.");
        await File.WriteAllTextAsync(Path.Combine(settings.DayZDirectory, "DayZ_x64.exe"), "fixture only - not an executable");
        await File.WriteAllTextAsync(settings.SteamExecutable, "fixture only - not an executable");
        Check(!PathValidation.Validate(settings).IsValid, "Brak DayZ_BE.exe nie został wykryty.");
        await File.WriteAllTextAsync(Path.Combine(settings.DayZDirectory, "DayZ_BE.exe"), "fixture only - not an executable");
        Check(PathValidation.Validate(settings).IsValid, "Prawidłowe ścieżki zostały odrzucone.");
        var other = Path.Combine(Path.GetDirectoryName(settings.SteamExecutable)!, "other.exe");
        await File.WriteAllTextAsync(other, "fixture only - not an executable");
        Check(!PathValidation.Validate(settings with { SteamExecutable = other }).IsValid, "Dowolny exe został uznany za Steam.");
        Check(!PathValidation.Validate(new()).IsValid, "Puste ustawienia nie powinny umożliwiać gry.");
    });
    await CommunityTests.RunAsync(testDirectory, Test);
    await PlayerNameTests.RunAsync(testDirectory, Test);
    await ServerQueryTests.RunAsync(Test);
    await ServerModsQueryTests.RunAsync(Test);
    await WorkshopPreparationTests.RunAsync(testDirectory, Test);
}
finally
{
    // Only the uniquely created, verified test directory is removed.
    if (Path.GetFullPath(testDirectory).StartsWith(Path.GetFullPath(testRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        Directory.Delete(testDirectory, recursive: true);
}

Console.WriteLine($"Wynik: {passes} PASS, {failures} FAIL. Nie uruchamiano gry, Steam ani połączeń sieciowych.");
return failures == 0 ? 0 : 1;

async Task Test(string name, Func<Task> action)
{
    try { await action(); passes++; Console.WriteLine($"PASS {name}"); }
    catch (Exception exception) { failures++; Console.Error.WriteLine($"FAIL {name}: {exception}"); }
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Oczekiwano wyjątku {typeof(T).Name}.");
}

Task RejectCatalog() => Throws<InvalidDataException>(async () => { _ = await ServerCatalog.LoadAsync(catalogPath); });

Task WriteCatalog(IReadOnlyList<ServerDefinition> entries) => File.WriteAllTextAsync(catalogPath,
    JsonSerializer.Serialize(new { schemaVersion = 1, servers = entries }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

static ServerDefinition Server() => new("test", "Test", "example.org", 2302);

LauncherSettings Settings(string suffix) => new()
{
    DayZDirectory = Path.Combine(testDirectory, suffix, "DayZ"),
    SteamExecutable = Path.Combine(testDirectory, suffix, "Steam", "steam.exe")
};

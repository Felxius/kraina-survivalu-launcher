using System.Diagnostics;
using System.Text.Json;
using KrainaSurvivalu.Launcher.Core;

internal static class PlayerNameTests
{
    public static async Task RunAsync(string testDirectory, Func<string, Func<Task>, Task> test)
    {
        await test("Nick: migracja starych ustawień zachowuje ścieżki i nie dopisuje nazwy gracza", async () =>
        {
            var path = Path.Combine(testDirectory, "old-settings-without-name.json");
            var dayz = Path.Combine(testDirectory, "Old paths", "DayZ");
            var steam = Path.Combine(testDirectory, "Old paths", "Steam", "steam.exe");
            var originalJson = JsonSerializer.Serialize(new { schemaVersion = 1, dayZDirectory = dayz, steamExecutable = steam });
            await File.WriteAllTextAsync(path, originalJson);
            var loaded = await new SettingsStore(path).LoadAsync();
            Check(loaded.Warning is null && loaded.Settings.DayZDirectory == dayz && loaded.Settings.SteamExecutable == steam &&
                loaded.Settings.PlayerName == "", "Migracja zmieniła dotychczasowe ścieżki lub wymyśliła nick.");
            Check(await File.ReadAllTextAsync(path) == originalJson && Directory.GetFiles(testDirectory, "old-settings-without-name.json*.bak").Length == 0,
                "Odczyt zgodnego starszego pliku nie powinien go przenosić ani modyfikować.");
        });
        await test("Nick: Unicode, spacje i dozwolone znaki przechodzą pełny zapis oraz odczyt", async () =>
        {
            var path = Path.Combine(testDirectory, "named-settings.json");
            var store = new SettingsStore(path);
            var settings = new LauncherSettings
            {
                DayZDirectory = Path.Combine(testDirectory, "New paths", "DayZ"),
                SteamExecutable = Path.Combine(testDirectory, "New paths", "Steam", "steam.exe"),
                PlayerName = "[KS] Żółw (PL)_7"
            };
            await store.SaveAsync(settings);
            var result = await store.LoadAsync();
            Check(result.Warning is null && result.Settings == settings, "Zapis nie zachował pełnego nicku i ścieżek.");
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Check(json.RootElement.GetProperty("playerName").GetString() == settings.PlayerName, "Nie zapisano publicznego pola playerName.");
        });
        await test("Nick: pusty nick wolno zapisać jako nieustawiony, ale nie wolno utworzyć argumentu startowego", async () =>
        {
            var store = new SettingsStore(Path.Combine(testDirectory, "unconfigured-name.json"));
            await store.SaveAsync(new LauncherSettings());
            var loaded = await store.LoadAsync();
            Check(loaded.Settings.PlayerName == "" && loaded.Warning is null && !PlayerNameValidation.Validate("").IsValid,
                "Pusty nick nie jest poprawnie rozróżniany od konfiguracji gotowej do startu.");
            Throws<ArgumentException>(() => DayZLaunchArguments.CreatePlayerNameArgument(""));
        });
        await test("Nick: dozwolone litery wielu alfabetów i znaki diakrytyczne pozostają niezmienione", () =>
        {
            foreach (var name in new[] { "Zażółć Gęślą", "玩家 7", "Гравець-2", "[KS] Pilot_(PL)", "Zaz\u0307o\u0301łc\u0301" })
            {
                Check(PlayerNameValidation.Validate(name).IsValid, "Odrzucono bezpieczny nick Unicode.");
                Check(DayZLaunchArguments.CreatePlayerNameArgument(name) == "-name=" + name,
                    "Nie wolno normalizować ani przeformatowywać gotowego nicku w generatorze argumentu.");
            }
            return Task.CompletedTask;
        });
        await test("Nick: limit liczy 32 Unicode runes zamiast połówek par UTF-16", () =>
        {
            var thirtyTwo = string.Concat(Enumerable.Repeat("\U00010400", 32));
            var thirtyThree = thirtyTwo + "\U00010400";
            Check(thirtyTwo.Length == 64 && PlayerNameValidation.Validate(thirtyTwo).IsValid,
                "Poprawne pary surogatów zostały błędnie policzone jako dwa znaki.");
            Check(!PlayerNameValidation.Validate(thirtyThree).IsValid && !PlayerNameValidation.Validate(new string('x', 33)).IsValid,
                "Dopuszczono nick dłuższy niż 32 runes.");
            return Task.CompletedTask;
        });
        await test("Nick: domyślny Survivor, puste wartości i spacje na brzegach są odrzucane", () =>
        {
            foreach (var name in new[] { "Survivor", "SURVIVOR", "sUrViVoR", "", " ", "  ", " Nick", "Nick ", "\tNick", null! })
                Check(!PlayerNameValidation.Validate(name).IsValid, "Przyjęto nieprawidłowy lub domyślny nick.");
            return Task.CompletedTask;
        });
        await test("Nick: cudzysłowy, kontrolne, sekwencje opcji i metaznaki są odrzucane", () =>
        {
            foreach (var name in new[] { "Nick\"", "O'Neil", "Nick\n-mod=evil", "Nick\rNext", "Nick\0", "Nick\tNext", "Nick -mod=evil", "-name=Other", "Nick;Other", "Nick&Other", "Nick|Other", "Nick/Other", "Nick\\Other", "Nick`Other", "Nick$Other", "\uD800" })
            {
                Check(!PlayerNameValidation.Validate(name).IsValid, "Przyjęto nieprawidłowe znaki nicku.");
                Throws<ArgumentException>(() => DayZLaunchArguments.CreatePlayerNameArgument(name));
            }
            return Task.CompletedTask;
        });
        await test("Nick: generator tworzy pojedynczy argument ArgumentList bez ręcznego cytowania ani startu procesu", () =>
        {
            const string name = "[KS] Żółw 7";
            var argument = DayZLaunchArguments.CreatePlayerNameArgument(name);
            var startInfo = new ProcessStartInfo("not-executed-fixture.exe") { UseShellExecute = false };
            startInfo.ArgumentList.Add(argument);
            Check(startInfo.ArgumentList.Count == 1 && startInfo.ArgumentList[0] == "-name=[KS] Żółw 7" && startInfo.Arguments == "",
                "Nick ze spacją został rozbity, ręcznie cytowany albo zamieniony w kompletny wiersz poleceń.");
            return Task.CompletedTask;
        });
        await test("Nick: błędny zapis nie nadpisuje poprzedniej poprawnej konfiguracji", async () =>
        {
            var path = Path.Combine(testDirectory, "invalid-name-save.json");
            var store = new SettingsStore(path);
            var settings = new LauncherSettings { PlayerName = "Żółw 7" };
            await store.SaveAsync(settings);
            var original = await File.ReadAllBytesAsync(path);
            foreach (var name in new[] { "Survivor", "Nick\nOther", " Nick", new string('x', 33), null! })
            {
                await ThrowsAsync<ArgumentException>(() => store.SaveAsync(settings with { PlayerName = name }));
                Check((await File.ReadAllBytesAsync(path)).SequenceEqual(original), "Błędny nick naruszył poprawny plik ustawień.");
            }
        });
        await test("Nick: nieprawidłowe pole z pliku zachowuje oryginał w kopii z ostrzeżeniem", async () =>
        {
            foreach (var name in new[] { "Survivor", "Nick\nOther", null! })
            {
                var path = Path.Combine(testDirectory, $"invalid-loaded-name-{Guid.NewGuid():N}.json");
                var json = JsonSerializer.Serialize(new { schemaVersion = 1, dayZDirectory = "", steamExecutable = "", playerName = name });
                await File.WriteAllTextAsync(path, json);
                var loaded = await new SettingsStore(path).LoadAsync();
                var backups = Directory.GetFiles(testDirectory, Path.GetFileName(path) + ".invalid-*.bak");
                Check(loaded.Settings == new LauncherSettings() && loaded.Warning is not null && backups.Length == 1 &&
                    await File.ReadAllTextAsync(backups[0]) == json, "Nie zachowano bezpiecznej kopii konfiguracji z błędnym nickiem.");
            }
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Oczekiwano wyjątku {typeof(T).Name}.");
    }

    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Oczekiwano wyjątku {typeof(T).Name}.");
    }
}

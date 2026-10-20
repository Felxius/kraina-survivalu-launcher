using KrainaSurvivalu.Launcher.Core;

internal static class DayZPlayTests
{
    public static async Task RunAsync(string testDirectory, Func<string, Func<Task>, Task> test)
    {
        await test("GRAJ: świeża synchronizacja uruchamia BattlEye z nickiem i portem gry", async () =>
        {
            var fixture = Fixture.Create(testDirectory);
            var query = new Query(List("1", "2"), List("1", "2"));
            var client = new Client(fixture); var launcher = new Launcher();
            var result = await Service(query, client, launcher).PlayAsync(Server(), fixture.Settings);
            var plan = launcher.Plans.Single();
            Check(result.Started && result.ProcessId == 4567 && query.Calls == 2 && client.Calls.Count == 1,
                "Gra nie została poprzedzona pełnym uzgodnieniem aktualnej listy.");
            Check(plan.ExecutablePath == Path.Combine(fixture.Settings.DayZDirectory, "DayZ_BE.exe") &&
                plan.WorkingDirectory == fixture.Settings.DayZDirectory && plan.SteamExecutable == fixture.Settings.SteamExecutable,
                "Nie wybrano bootstrapu BattlEye i poprawnego folderu roboczego.");
            Check(plan.Arguments.Take(5).SequenceEqual(new[] { "0", "1", "0", "-exe", "DayZ_x64.exe" }),
                "Zmieniono argumenty bootstrapu względem oficjalnego launchera.");
            Check(plan.Arguments.Contains("-connect=178.95.158.100") && plan.Arguments.Contains("-port=23000") &&
                !plan.Arguments.Contains("-port=9000") && plan.Arguments.Contains("-name=Łowca z Krainy"),
                "Zgubiono nick ze spacjami i Unicode albo użyto portu Query zamiast portu gry.");
            Check(plan.Arguments.Single(argument => argument.StartsWith("-mod=", StringComparison.Ordinal)) ==
                "-mod=" + fixture.ModPath("1") + ";" + fixture.ModPath("2"),
                "Ścieżki modów nie są pojedynczym argumentem zachowującym spacje.");
            Check(!plan.Arguments.Any(argument => argument.Contains("noBattlEye", StringComparison.OrdinalIgnoreCase) ||
                argument.Contains("filePatching", StringComparison.OrdinalIgnoreCase)), "Plan wyłącza zabezpieczenia gry.");
        });

        await test("GRAJ: równoważne ID mają jedną ścieżkę we właściwej kolejności", () =>
        {
            var fixture = Fixture.Create(testDirectory);
            var metadata = List("2", "01", "1", "3");
            var plan = DayZLaunchPlanFactory.Create(fixture.Settings, Server(), Prepared(fixture, metadata));
            Check(plan.Arguments.Single(argument => argument.StartsWith("-mod=", StringComparison.Ordinal)) ==
                "-mod=" + string.Join(';', new[] { fixture.ModPath("2"), fixture.ModPath("1"), fixture.ModPath("3") }),
                "Powielono równoważne ID lub zmieniono kolejność serwera.");
            return Task.CompletedTask;
        });

        await test("GRAJ: akceptuje Workshop w domyślnej bibliotece Steam i oddzielnej bibliotece gry", () =>
        {
            var fixture = Fixture.Create(testDirectory);
            var steamMod = fixture.CreateMod("2", steamLibrary: true);
            var plan = DayZLaunchPlanFactory.Create(fixture.Settings, Server(), Prepared(fixture, List("1", "2"),
                [fixture.Ready("1"), fixture.Ready("2") with { InstalledPath = steamMod }]));
            Check(plan.Arguments.Contains("-mod=" + fixture.ModPath("1") + ";" + steamMod),
                "Prawidłowy mod z drugiej dozwolonej biblioteki został pominięty.");
            return Task.CompletedTask;
        });

        await test("GRAJ: końcowy separator folderu gry zachowuje zewnętrzną bibliotekę Workshop", async () =>
        {
            var fixture = Fixture.Create(testDirectory);
            var settings = fixture.Settings with { DayZDirectory = fixture.Settings.DayZDirectory + Path.DirectorySeparatorChar };
            var launcher = new Launcher();
            var result = await Service(new Query(List("1"), List("1")), new Client(fixture), launcher).PlayAsync(Server(), settings);
            Check(result.Started && launcher.Plans.Single().Arguments.Contains("-mod=" + fixture.ModPath("1")),
                "Końcowy separator usunął prawidłową bibliotekę gry z dozwolonych katalogów Workshop.");
        });

        await test("GRAJ: błędne ścieżki i nick blokują pobieranie przed odpytaniem serwera", async () =>
        {
            var fixture = Fixture.Create(testDirectory);
            foreach (var settings in new[] { new LauncherSettings(), fixture.Settings with { PlayerName = "Survivor" },
                fixture.Settings with { PlayerName = "gracz\" -mod=obcy" }, fixture.Settings with { SteamExecutable = "relative/steam.exe" } })
            {
                var query = new Query(List("1")); var client = new Client(fixture); var launcher = new Launcher();
                var result = await Service(query, client, launcher).PlayAsync(Server(), settings);
                Check(!result.Started && query.Calls == 0 && client.Calls.Count == 0 && launcher.Plans.Count == 0,
                    "Błędne ustawienia dopuściły synchronizację lub start gry.");
            }
        });

        await test("GRAJ: host z argumentami i błędne porty blokują operację przed Steam", async () =>
        {
            var fixture = Fixture.Create(testDirectory);
            foreach (var server in new[] { Server() with { Host = "178.95.158.100 -mod=obcy" },
                Server() with { Host = "https://example.org" }, Server() with { GamePort = 0 }, Server() with { GamePort = 65536 } })
            {
                var query = new Query(List("1")); var client = new Client(fixture); var launcher = new Launcher();
                var result = await Service(query, client, launcher).PlayAsync(server, fixture.Settings);
                Check(!result.Started && query.Calls == 0 && client.Calls.Count == 0 && launcher.Plans.Count == 0,
                    "Nieprawidłowy endpoint dotarł do synchronizacji.");
            }
        });

        await test("GRAJ: brak dostępnego Steam lub uruchomiona gra nie rozpoczyna pobierania", async () =>
        {
            var fixture = Fixture.Create(testDirectory);
            var query = new Query(List("1")); var client = new Client(fixture);
            var launcher = new Launcher { Availability = new(false, "TEST: gra już działa lub Steam nie jest dostępny") };
            var result = await Service(query, client, launcher).PlayAsync(Server(), fixture.Settings);
            Check(!result.Started && result.Message == launcher.Availability.Message && query.Calls == 0 &&
                client.Calls.Count == 0 && launcher.Plans.Count == 0, "Zignorowano brak możliwości bezpiecznego uruchomienia gry.");
        });

        await test("GRAJ: wymagany mod bez ID jest nazwany i blokuje pobieranie całej listy", async () =>
        {
            var fixture = Fixture.Create(testDirectory);
            var metadata = List("1", "0") with { Mods = [new("CF", "1"), new("@craft-nails", "0")] };
            var query = new Query(metadata); var client = new Client(fixture); var launcher = new Launcher();
            var result = await Service(query, client, launcher).PlayAsync(Server(), fixture.Settings);
            Check(!result.Started && result.Message.Contains("@craft-nails", StringComparison.Ordinal) && query.Calls == 1 &&
                client.Calls.Count == 0 && launcher.Plans.Count == 0, "Zgadnięto ID albo rozpoczęto pobieranie nieuruchamialnej listy.");
        });

        await test("GRAJ: dodany podczas synchronizacji mod bez ID wstrzymuje kolejne pobieranie", async () =>
        {
            var fixture = Fixture.Create(testDirectory);
            var metadata = List("1", "0") with { Mods = [new("CF", "1"), new("Nowy prywatny mod", "0")] };
            var query = new Query(List("1"), metadata); var client = new Client(fixture); var launcher = new Launcher();
            var result = await Service(query, client, launcher).PlayAsync(Server(), fixture.Settings);
            Check(!result.Started && result.Message.Contains("Nowy prywatny mod", StringComparison.Ordinal) &&
                client.Calls.Count == 1 && launcher.Plans.Count == 0,
                "Zmiana listy na niekompletną spowodowała dalsze pobieranie lub start gry.");
        });

        await test("GRAJ: nieopisane flagi zachowują ostrzeżenie bez wymyślania brakujących ID", async () =>
        {
            var fixture = Fixture.Create(testDirectory); var metadata = List("1") with { Flags = 8 };
            var launcher = new Launcher();
            var result = await Service(new Query(metadata, metadata), new Client(fixture), launcher).PlayAsync(Server(), fixture.Settings);
            Check(result.Started && launcher.Plans.Single().Warning is not null &&
                result.Message.Contains(launcher.Plans[0].Warning!, StringComparison.Ordinal) &&
                result.Preparation is { AllAdvertisedItemsReady: true, HasCompleteReadyMetadata: false } &&
                result.Preparation.ServerMods!.Flags == 8,
                "Ostrzeżenie o nieopisanych flagach zniknęło albo ogłoszono potwierdzoną kompletność.");
        });

        await test("GRAJ: zmiana modów przed startem uzgadnia nową listę i pomija usunięty mod", async () =>
        {
            var fixture = Fixture.Create(testDirectory);
            var query = new Query(List("1", "2"), List("2", "3"), List("2", "3"));
            var client = new Client(fixture); var launcher = new Launcher();
            var result = await Service(query, client, launcher).PlayAsync(Server(), fixture.Settings);
            Check(result.Started && client.Calls.Count == 2 && query.Calls == 3 &&
                client.Calls[1].SequenceEqual(new[] { "2", "3" }) &&
                launcher.Plans.Single().Arguments.Contains("-mod=" + fixture.ModPath("2") + ";" + fixture.ModPath("3")) &&
                Directory.Exists(fixture.ModPath("1")),
                "Gra użyła starej listy, nie pobrano nowej albo skasowano usuniętą subskrypcję.");
        });

        await test("GRAJ: końcowa kolejność serwera trafia do argumentów bez zbędnego pobierania", async () =>
        {
            var fixture = Fixture.Create(testDirectory); var client = new Client(fixture); var launcher = new Launcher();
            var result = await Service(new Query(List("1", "2"), List("2", "1")), client, launcher).PlayAsync(Server(), fixture.Settings);
            Check(result.Started && client.Calls.Count == 1 &&
                launcher.Plans.Single().Arguments.Contains("-mod=" + fixture.ModPath("2") + ";" + fixture.ModPath("1")),
                "Kolejność planu pochodzi ze starego odczytu.");
        });

        await test("GRAJ: brak odczytu listy przed lub po pobieraniu nie uruchamia gry", async () =>
        {
            var fixture = Fixture.Create(testDirectory);
            var unavailable = new ServerModsQueryResult(ServerModsQueryState.NoResponse, DateTimeOffset.UtcNow);
            foreach (var afterDownload in new[] { false, true })
            {
                var query = afterDownload ? new Query(List("1"), unavailable) : new Query(unavailable);
                var client = new Client(fixture); var launcher = new Launcher();
                var result = await Service(query, client, launcher).PlayAsync(Server(), fixture.Settings);
                Check(!result.Started && launcher.Plans.Count == 0 && client.Calls.Count == (afterDownload ? 1 : 0),
                    "Brak aktualnej odpowiedzi zastąpiono starą lub pustą gotową listą.");
            }
        });

        await test("GRAJ: aktualizacja w kolejce lub błąd Steam blokuje start mimo istniejących plików", async () =>
        {
            var fixture = Fixture.Create(testDirectory);
            foreach (var response in new[] { new WorkshopSyncResult(true,
                [fixture.Ready("1") with { State = WorkshopItemState.Installed | WorkshopItemState.NeedsUpdate }], "TEST: aktualizacja"),
                new(false, [fixture.Ready("1")], "TEST: brak połączenia Steam") })
            {
                var client = new Client(fixture) { Handler = _ => Task.FromResult(response) }; var launcher = new Launcher();
                var result = await Service(new Query(List("1"), List("1")), client, launcher).PlayAsync(Server(), fixture.Settings);
                Check(!result.Started && launcher.Plans.Count == 0, "Folder moda zastąpił potwierdzenie aktualnego stanu Steam.");
            }
        });

        await test("GRAJ: brakujące, dodatkowe i zduplikowane odpowiedzi Steam nie dają startu", async () =>
        {
            var fixture = Fixture.Create(testDirectory);
            foreach (var items in new WorkshopItemSnapshot[][] { [], [fixture.Ready("2")], [fixture.Ready("1"), fixture.Ready("1")] })
            {
                var client = new Client(fixture) { Handler = _ => Task.FromResult(new WorkshopSyncResult(true, items, "TEST")) };
                var launcher = new Launcher();
                var result = await Service(new Query(List("1")), client, launcher).PlayAsync(Server(), fixture.Settings);
                Check(!result.Started && launcher.Plans.Count == 0, "Sprzeczna odpowiedź Steam została użyta do uruchomienia gry.");
            }
        });

        await test("GRAJ: anulowanie przed startem i podczas pobierania nigdy nie uruchamia gry", async () =>
        {
            var fixture = Fixture.Create(testDirectory);
            foreach (var beforeOperation in new[] { true, false })
            {
                using var cancellation = new CancellationTokenSource();
                if (beforeOperation) cancellation.Cancel();
                var query = new Query(List("1")); var launcher = new Launcher();
                var client = new Client(fixture) { Handler = mods => { cancellation.Cancel(); return Task.FromResult(fixture.Success(mods)); } };
                await Canceled(() => Service(query, client, launcher).PlayAsync(Server(), fixture.Settings, cancellationToken: cancellation.Token));
                Check(query.Calls == (beforeOperation ? 0 : 1) && client.Calls.Count == (beforeOperation ? 0 : 1) &&
                    launcher.Plans.Count == 0, "Anulowanie dopuściło dalsze odpytywanie lub uruchomienie.");
            }
        });

        await test("GRAJ: anulowanie po ostatnim odczycie nadal zatrzymuje uruchomienie", async () =>
        {
            var fixture = Fixture.Create(testDirectory); using var cancellation = new CancellationTokenSource();
            var query = new Query(List("1"), List("1")) { OnQuery = call => { if (call == 2) cancellation.Cancel(); } };
            var launcher = new Launcher();
            await Canceled(() => Service(query, new Client(fixture), launcher).PlayAsync(Server(), fixture.Settings, cancellationToken: cancellation.Token));
            Check(query.Calls == 2 && launcher.Plans.Count == 0, "Ostatni odczyt ominął anulowanie.");
        });

        await test("GRAJ: usunięty podczas pobierania plik BattlEye jest sprawdzany ponownie", async () =>
        {
            var fixture = Fixture.Create(testDirectory); var launcher = new Launcher();
            var client = new Client(fixture) { Handler = mods =>
            { File.Delete(Path.Combine(fixture.Settings.DayZDirectory, "DayZ_BE.exe")); return Task.FromResult(fixture.Success(mods)); } };
            var result = await Service(new Query(List("1"), List("1")), client, launcher).PlayAsync(Server(), fixture.Settings);
            Check(!result.Started && launcher.Plans.Count == 0, "Plan nie sprawdził ponownie plików gry po pobieraniu.");
        });

        await test("GRAJ: brak pliku PBO po synchronizacji blokuje start", async () =>
        {
            var fixture = Fixture.Create(testDirectory); var launcher = new Launcher();
            var client = new Client(fixture) { Handler = mods =>
            { File.Delete(Path.Combine(fixture.ModPath("1"), "addons", "fixture.pbo")); return Task.FromResult(fixture.Success(mods)); } };
            var result = await Service(new Query(List("1"), List("1")), client, launcher).PlayAsync(Server(), fixture.Settings);
            Check(!result.Started && launcher.Plans.Count == 0, "Sam pozytywny stan Steam zastąpił obecność plików moda.");
        });

        await test("GRAJ: inna biblioteka, inny folder ID i brakujący folder moda są odrzucane", () =>
        {
            var fixture = Fixture.Create(testDirectory);
            var outside = Path.Combine(fixture.Root, "obca biblioteka", "steamapps", "workshop", "content", "221100", "1");
            Directory.CreateDirectory(Path.Combine(outside, "addons")); File.WriteAllText(Path.Combine(outside, "addons", "fixture.pbo"), "TEST ONLY");
            foreach (var path in new[] { outside, fixture.ModPath("2"), fixture.ModPath("99") })
                Reject(() => DayZLaunchPlanFactory.Create(fixture.Settings, Server(), Prepared(fixture, List("1"),
                    [fixture.Ready("1") with { InstalledPath = path }])));
            return Task.CompletedTask;
        });

        await test("GRAJ: folder dodatku z samym opisem nie staje się modem gry", () =>
        {
            var fixture = Fixture.Create(testDirectory);
            File.Move(Path.Combine(fixture.ModPath("1"), "addons", "fixture.pbo"), Path.Combine(fixture.ModPath("1"), "addons", "readme.txt"));
            Reject(() => DayZLaunchPlanFactory.Create(fixture.Settings, Server(), Prepared(fixture, List("1"))));
            return Task.CompletedTask;
        });

        await test("GRAJ: średnik w nazwie biblioteki nie dokłada drugiego argumentu moda", () =>
        {
            var fixture = Fixture.Create(testDirectory, "biblioteka;drugi-mod");
            Reject(() => DayZLaunchPlanFactory.Create(fixture.Settings, Server(), Prepared(fixture, List("1"))));
            return Task.CompletedTask;
        });

        await test("GRAJ: fabryka odrzuca nieaktualną listę i niezgodne potwierdzenie modów", () =>
        {
            var fixture = Fixture.Create(testDirectory); var good = Prepared(fixture, List("1"));
            foreach (var invalid in new[] { good with { ServerListStable = false }, good with { ServerMods = null },
                good with { Workshop = new(false, [fixture.Ready("1")], "TEST: błąd") },
                Prepared(fixture, List("1"), []), Prepared(fixture, List("1"), [fixture.Ready("1"), fixture.Ready("2")]),
                Prepared(fixture, List("1"), [fixture.Ready("1"), fixture.Ready("1") with { InstalledPath = fixture.ModPath("2") }]) })
                Reject(() => DayZLaunchPlanFactory.Create(fixture.Settings, Server(), invalid));
            return Task.CompletedTask;
        });

        await test("GRAJ: odmowa uruchomienia procesu nie udaje udanego połączenia", async () =>
        {
            var fixture = Fixture.Create(testDirectory);
            var launcher = new Launcher { Result = new(false, "TEST: gra została uruchomiona w trakcie pobierania") };
            var result = await Service(new Query(List("1"), List("1")), new Client(fixture), launcher).PlayAsync(Server(), fixture.Settings);
            Check(!result.Started && result.Message == launcher.Result.Message && result.ProcessId is null && launcher.Plans.Count == 1,
                "Błąd rozpoczęcia procesu został zamieniony na sukces.");
        });

        await test("GRAJ: jawnie pusta aktualna lista uruchamia grę bez pustego -mod i bez Steam UGC", async () =>
        {
            var fixture = Fixture.Create(testDirectory); var client = new Client(fixture); var launcher = new Launcher();
            var query = new Query(List(), List());
            var result = await Service(query, client, launcher).PlayAsync(Server(), fixture.Settings);
            Check(result.Started && query.Calls == 2 && client.Calls.Count == 0 &&
                !launcher.Plans.Single().Arguments.Any(argument => argument.StartsWith("-mod", StringComparison.Ordinal)),
                "Jawnie pusta lista wywołała pobieranie lub włączyła pusty argument moda.");
        });

        await test("GRAJ: drugi równoczesny klik nie tworzy dodatkowej synchronizacji ani gry", async () =>
        {
            var fixture = Fixture.Create(testDirectory);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new Client(fixture) { Handler = async mods => { entered.SetResult(); await release.Task; return fixture.Success(mods); } };
            var query = new Query(List("1"), List("1")); var launcher = new Launcher(); var service = Service(query, client, launcher);
            var first = service.PlayAsync(Server(), fixture.Settings);
            await entered.Task;
            var second = await service.PlayAsync(Server(), fixture.Settings);
            Check(!second.Started && client.Calls.Count == 1 && launcher.Plans.Count == 0, "Drugi klik ominął blokadę wspólnej operacji.");
            release.SetResult();
            Check((await first).Started && launcher.Plans.Count == 1, "Blokada zatrzymała także poprawnie przygotowane pierwsze uruchomienie.");
        });
    }

    private static ServerDefinition Server() => new("deer-isle", "Deer Isle", "178.95.158.100", 23000, 9000);
    private static DayZPlayService Service(Query query, Client client, Launcher launcher) => new(new(query, client), launcher);
    private static ServerModsQueryResult List(params string[] ids) => new(ServerModsQueryState.Available, DateTimeOffset.UtcNow,
        ids.Select(id => new ServerWorkshopMod("TEST " + id, id)).ToArray());
    private static WorkshopPreparationResult Prepared(Fixture fixture, ServerModsQueryResult metadata,
        IReadOnlyList<WorkshopItemSnapshot>? items = null) => new(metadata,
            new(true, items ?? metadata.Mods!.Select(mod => fixture.Ready(mod.WorkshopId)).ToArray(), "TEST: gotowe"), true, "TEST");
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception exception) when (exception is InvalidOperationException or ArgumentException) { return; }
        throw new InvalidOperationException("Nieprawidłowe przygotowanie gry zostało przyjęte.");
    }
    private static async Task Canceled(Func<Task> action)
    {
        try { await action(); } catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Nie przekazano anulowania operacji.");
    }

    private sealed class Fixture(string root, LauncherSettings settings, string librarySteamapps)
    {
        public string Root { get; } = root;
        public LauncherSettings Settings { get; } = settings;
        public string ModPath(string id) => Path.Combine(librarySteamapps, "workshop", "content", "221100", ulong.Parse(id).ToString(System.Globalization.CultureInfo.InvariantCulture));
        public WorkshopItemSnapshot Ready(string id) => new(id, "TEST " + id,
            WorkshopItemState.Installed | WorkshopItemState.Subscribed, ModPath(id));
        public WorkshopSyncResult Success(IReadOnlyList<ServerWorkshopMod> mods) => new(true,
            mods.Select(mod => Ready(mod.WorkshopId)).ToArray(), "TEST: gotowe");
        public string CreateMod(string id, bool steamLibrary = false)
        {
            var path = steamLibrary ? Path.Combine(Path.GetDirectoryName(Settings.SteamExecutable)!, "steamapps", "workshop", "content", "221100", id) : ModPath(id);
            Directory.CreateDirectory(Path.Combine(path, "addons"));
            File.WriteAllText(Path.Combine(path, "addons", "fixture.pbo"), "INERT TEST FILE ONLY; NOT A DAYZ ADDON");
            return path;
        }
        public static Fixture Create(string testDirectory, string libraryName = "Biblioteka gry ze spacjami")
        {
            var root = Path.Combine(testDirectory, "dayz-play", Guid.NewGuid().ToString("N"));
            var steamapps = Path.Combine(root, libraryName, "steamapps");
            var settings = new LauncherSettings
            {
                DayZDirectory = Path.Combine(steamapps, "common", "DayZ"),
                SteamExecutable = Path.Combine(root, "Steam ze spacjami", "steam.exe"),
                PlayerName = "Łowca z Krainy"
            };
            Directory.CreateDirectory(settings.DayZDirectory); Directory.CreateDirectory(Path.GetDirectoryName(settings.SteamExecutable)!);
            foreach (var path in new[] { Path.Combine(settings.DayZDirectory, "DayZ_x64.exe"),
                Path.Combine(settings.DayZDirectory, "DayZ_BE.exe"), settings.SteamExecutable })
                File.WriteAllText(path, "INERT TEST FILE ONLY; NOT AN EXECUTABLE");
            var fixture = new Fixture(root, settings, steamapps);
            foreach (var id in new[] { "1", "2", "3" }) fixture.CreateMod(id);
            return fixture;
        }
    }

    private sealed class Query(params ServerModsQueryResult[] results) : IServerModsQueryService
    {
        public int Calls { get; private set; }
        public Action<int>? OnQuery { get; init; }
        public Task<ServerModsQueryResult> QueryAsync(ServerDefinition server, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Calls >= results.Length) throw new InvalidOperationException("Nieoczekiwane dodatkowe odpytanie w teście GRAJ.");
            var result = results[Calls++]; OnQuery?.Invoke(Calls); return Task.FromResult(result);
        }
    }
    private sealed class Client(Fixture fixture) : IWorkshopClient
    {
        public List<string[]> Calls { get; } = [];
        public Func<IReadOnlyList<ServerWorkshopMod>, Task<WorkshopSyncResult>>? Handler { get; init; }
        public Task<WorkshopSyncResult> InspectAsync(LauncherSettings settings, IReadOnlyList<ServerWorkshopMod> mods,
            IProgress<WorkshopProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("GRAJ nie może zastąpić synchronizacji samym odczytem.");
        public Task<WorkshopSyncResult> SynchronizeAsync(LauncherSettings settings, IReadOnlyList<ServerWorkshopMod> mods,
            IProgress<WorkshopProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls.Add(mods.Select(mod => mod.WorkshopId).ToArray());
            return Handler?.Invoke(mods) ?? Task.FromResult(fixture.Success(mods));
        }
    }
    private sealed class Launcher : IGameProcessLauncher
    {
        public ValidationResult Availability { get; init; } = new(true, "TEST: gotowe");
        public GameLaunchResult Result { get; init; } = new(true, "TEST: rozpoczęto uruchamianie DayZ", 4567);
        public List<DayZLaunchPlan> Plans { get; } = [];
        public ValidationResult CheckAvailability(LauncherSettings settings) => Availability;
        public Task<GameLaunchResult> LaunchAsync(DayZLaunchPlan plan, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Plans.Add(plan); return Task.FromResult(Result); }
    }
}

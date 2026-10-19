using KrainaSurvivalu.Launcher.Core;

internal static class WorkshopPreparationTests
{
    public static async Task RunAsync(string testDirectory, Func<string, Func<Task>, Task> test)
    {
        var directory = Path.Combine(testDirectory, "workshop-pc");
        Directory.CreateDirectory(directory);
        foreach (var name in new[] { "DayZ_x64.exe", "DayZ_BE.exe", "steam.exe" })
            await File.WriteAllTextAsync(Path.Combine(directory, name), "TEST FIXTURE ONLY; NOT EXECUTABLE");
        var settings = new LauncherSettings { DayZDirectory = directory, SteamExecutable = Path.Combine(directory, "steam.exe") };
        var server = new ServerDefinition("test", "TEST", "127.0.0.1", 2302, 27016);

        await test("Workshop PC: błędne ścieżki blokują odczyt i uruchomienie Steam", async () =>
        {
            var query = new Query(List("1")); var client = new Client();
            var result = await new WorkshopPreparationService(query, client).SynchronizeAsync(server, new());
            Check(!result.AllAdvertisedItemsReady && query.Calls == 0 && client.Calls.Count == 0, "Nie sprawdzono ścieżek przed rozpoczęciem operacji.");
        });
        await test("Workshop PC: niedostępna lista nie jest zamieniana na pustą gotową listę", async () =>
        {
            foreach (var metadata in new[] { new ServerModsQueryResult(ServerModsQueryState.NoResponse, DateTimeOffset.UtcNow),
                new(ServerModsQueryState.Available, DateTimeOffset.UtcNow), new(ServerModsQueryState.InvalidResponse, DateTimeOffset.UtcNow) })
            {
                var client = new Client();
                var result = await new WorkshopPreparationService(new Query(metadata), client).SynchronizeAsync(server, settings);
                Check(result.Workshop is null && !result.ServerListStable && client.Calls.Count == 0, "Uruchomiono Steam bez aktualnej listy.");
            }
        });
        await test("Workshop PC: inspekcja jest świeża i nie wywołuje synchronizacji", async () =>
        {
            var query = new Query(List("1", "2")); var client = new Client();
            var result = await new WorkshopPreparationService(query, client).InspectAsync(server, settings);
            Check(query.Calls == 1 && client.Calls.Single().Mode == "inspect" && result.HasCompleteReadyMetadata, "Inspekcja nie zachowała granicy odczytu.");
        });
        await test("Workshop PC: synchronizacja sprawdza listę także po pobieraniu", async () =>
        {
            var query = new Query(List("1"), List("1")); var client = new Client();
            var result = await new WorkshopPreparationService(query, client).SynchronizeAsync(server, settings);
            Check(query.Calls == 2 && client.Calls.Single().Mode == "sync" && result.HasCompleteReadyMetadata, "Brak końcowego odczytu listy serwera.");
        });
        await test("Workshop PC: dodany mod jest pobierany, usunięty pomijany bez kasowania", async () =>
        {
            var query = new Query(List("1", "2"), List("2", "3"), List("2", "3")); var client = new Client();
            var progress = new ProgressSink();
            var result = await new WorkshopPreparationService(query, client).SynchronizeAsync(server, settings, progress);
            Check(result.HasCompleteReadyMetadata && query.Calls == 3 && client.Calls.Count == 2 &&
                client.Calls[1].Ids.SequenceEqual(new[] { "2", "3" }) &&
                result.Workshop!.Items.Select(item => item.WorkshopId).SequenceEqual(new[] { "2", "3" }), "Nie uzgodniono nowej listy.");
            Check(progress.Events.Any(value => value.Message.Contains("zmienił listę")), "Brak informacji o zmianie wymagań.");
        });
        await test("Workshop PC: zmiana kolejności i nazw nie powoduje ponownego pobierania", async () =>
        {
            var latest = List("2", "1") with { Mods = [new("Nowa nazwa 2", "2"), new("Nowa nazwa 1", "1")] };
            var client = new Client();
            var result = await new WorkshopPreparationService(new Query(List("1", "2"), latest), client).SynchronizeAsync(server, settings);
            Check(client.Calls.Count == 1 && result.Workshop!.Items[0].Name == "Nowa nazwa 2" && result.HasCompleteReadyMetadata,
                "Zmiana kolejności nie powinna uruchamiać nowej operacji Steam.");
        });
        await test("Workshop PC: nieznane ID pozostaje błędem, rozpoznane ID jest synchronizowane", async () =>
        {
            var metadata = List("1", "0"); var client = new Client();
            var result = await new WorkshopPreparationService(new Query(metadata, metadata), client).SynchronizeAsync(server, settings);
            Check(client.Calls.Single().Ids.SequenceEqual(new[] { "1" }) && result.Workshop!.Items.Count == 2 &&
                result.Workshop.Items[1].Error is not null && !result.AllAdvertisedItemsReady, "Pominięto nierozpoznany mod lub obiecano kompletność.");
        });
        await test("Workshop PC: same nierozpoznane mody nie uruchamiają pustej synchronizacji", async () =>
        {
            var metadata = List("0"); var client = new Client();
            var result = await new WorkshopPreparationService(new Query(metadata, metadata), client).SynchronizeAsync(server, settings);
            Check(client.Calls.Count == 0 && !result.AllAdvertisedItemsReady && result.Workshop!.Items.Single().Error is not null, "Lista nieznana stała się gotowa.");
        });
        await test("Workshop PC: flaga serwera nie blokuje znanych modów ani nie potwierdza kompletności", async () =>
        {
            var metadata = List("1") with { Flags = 8 }; var client = new Client();
            var result = await new WorkshopPreparationService(new Query(metadata, metadata), client).SynchronizeAsync(server, settings);
            Check(client.Calls.Count == 1 && result.AllAdvertisedItemsReady && !result.HasCompleteReadyMetadata && result.ServerMods!.Flags == 8,
                "Wymyślono znaczenie flag lub zablokowano znany mod.");
        });
        await test("Workshop PC: równoważne ID nie są subskrybowane dwa razy", async () =>
        {
            var metadata = List("1", "01"); var client = new Client();
            var result = await new WorkshopPreparationService(new Query(metadata, metadata), client).SynchronizeAsync(server, settings);
            Check(client.Calls.Single().Ids.SequenceEqual(new[] { "1" }) && result.Workshop!.Items.Count == 2 && result.AllAdvertisedItemsReady, "Powtórzono operację dla tego samego uint64.");
        });
        await test("Workshop PC: niepełna lub nadmiarowa odpowiedź Steam nie daje gotowości", async () =>
        {
            foreach (var items in new WorkshopItemSnapshot[][] { [], [Ready("999")], [Ready("1"), Ready("1")] })
            {
                var client = new Client { Handler = _ => new(true, items, "TEST") };
                var result = await new WorkshopPreparationService(new Query(List("1")), client).InspectAsync(server, settings);
                Check(!result.AllAdvertisedItemsReady && result.Workshop is { Success: false }, "Przyjęto niekompletną lub sprzeczną odpowiedź.");
            }
        });
        await test("Workshop PC: błąd Steam nie potwierdza gotowości istniejących folderów", async () =>
        {
            var client = new Client { Handler = _ => new(false, [Ready("1")], "TEST: brak połączenia") };
            var result = await new WorkshopPreparationService(new Query(List("1")), client).SynchronizeAsync(server, settings);
            Check(!result.AllAdvertisedItemsReady && result.Message.Contains("brak połączenia"), "Zignorowano błąd operacji.");
        });
        await test("Workshop PC: utrata serwera po pobieraniu unieważnia potwierdzenie listy", async () =>
        {
            var result = await new WorkshopPreparationService(new Query(List("1"),
                new(ServerModsQueryState.NoResponse, DateTimeOffset.UtcNow)), new Client()).SynchronizeAsync(server, settings);
            Check(result.Workshop is { AllReady: true } && !result.ServerListStable && !result.AllAdvertisedItemsReady, "Stara lista została uznana za aktualną.");
        });
        await test("Workshop PC: ciągle zmieniana lista ma skończony limit prób", async () =>
        {
            var client = new Client(); var query = new Query(List("1"), List("2"), List("3"));
            var result = await new WorkshopPreparationService(query, client, 1).SynchronizeAsync(server, settings);
            Check(client.Calls.Count == 2 && query.Calls == 3 && !result.ServerListStable && !result.AllAdvertisedItemsReady,
                "Synchronizacja nie zatrzymała zmieniającej się listy.");
        });
        await test("Workshop PC: anulowanie przed operacją nie dotyka Steam", async () =>
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var client = new Client();
            await Canceled(() => new WorkshopPreparationService(new Query(List("1")), client).SynchronizeAsync(server, settings, cancellationToken: cancellation.Token));
            Check(client.Calls.Count == 0, "Rozpoczęto operację po anulowaniu.");
        });
        await test("Workshop PC: anulowanie w kliencie przerywa końcowe odpytywanie", async () =>
        {
            using var cancellation = new CancellationTokenSource();
            var query = new Query(List("1"));
            var client = new Client { Handler = mods => { cancellation.Cancel(); return Success(mods); } };
            await Canceled(() => new WorkshopPreparationService(query, client).SynchronizeAsync(server, settings, cancellationToken: cancellation.Token));
            Check(query.Calls == 1, "Kontynuowano uzgadnianie po anulowaniu.");
        });
        await test("Workshop PC: gotowość wymaga oficjalnego stanu i ścieżki bez błędu", () =>
        {
            var ready = Ready("1"); Check(ready.IsReady, "Poprawny stan nie jest gotowy.");
            foreach (var value in new[] { ready with { InstalledPath = null }, ready with { Error = "TEST" },
                ready with { State = WorkshopItemState.None }, ready with { State = WorkshopItemState.Installed | WorkshopItemState.NeedsUpdate },
                ready with { State = WorkshopItemState.Installed | WorkshopItemState.Downloading },
                ready with { State = WorkshopItemState.Installed | WorkshopItemState.DownloadPending } })
                Check(!value.IsReady, "Niepoprawny stan został uznany za gotowy.");
            return Task.CompletedTask;
        });
        await test("Workshop PC: jawnie pusta lista jest odrębna od braku listy", async () =>
        {
            var metadata = List(); var client = new Client();
            var result = await new WorkshopPreparationService(new Query(metadata, metadata), client).SynchronizeAsync(server, settings);
            Check(result.HasCompleteReadyMetadata && client.Calls.Count == 0, "Jawnie pustej listy nie należy zamieniać na nieznaną.");
        });
    }

    private static ServerModsQueryResult List(params string[] ids) => new(ServerModsQueryState.Available, DateTimeOffset.UtcNow,
        ids.Select(id => new ServerWorkshopMod("TEST " + id, id)).ToArray());
    private static WorkshopItemSnapshot Ready(string id) => new(id, "TEST " + id,
        WorkshopItemState.Installed | WorkshopItemState.Subscribed, Path.Combine(Path.GetTempPath(), "TEST-WORKSHOP-NOT-REAL", id));
    private static WorkshopSyncResult Success(IReadOnlyList<ServerWorkshopMod> mods) => new(true, mods.Select(mod => Ready(mod.WorkshopId)).ToArray(), "TEST: gotowe");
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Canceled(Func<Task> action)
    {
        try { await action(); } catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Brak anulowania.");
    }
    private sealed class Query(params ServerModsQueryResult[] results) : IServerModsQueryService
    {
        public int Calls { get; private set; }
        public Task<ServerModsQueryResult> QueryAsync(ServerDefinition server, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Calls >= results.Length) throw new InvalidOperationException("Nieoczekiwane dodatkowe odpytywanie w teście.");
            return Task.FromResult(results[Calls++]);
        }
    }
    private sealed class Client : IWorkshopClient
    {
        public List<(string Mode, string[] Ids)> Calls { get; } = [];
        public Func<IReadOnlyList<ServerWorkshopMod>, WorkshopSyncResult> Handler { get; init; } = Success;
        public Task<WorkshopSyncResult> InspectAsync(LauncherSettings settings, IReadOnlyList<ServerWorkshopMod> mods, IProgress<WorkshopProgress>? progress = null, CancellationToken cancellationToken = default) => Run("inspect", mods, cancellationToken);
        public Task<WorkshopSyncResult> SynchronizeAsync(LauncherSettings settings, IReadOnlyList<ServerWorkshopMod> mods, IProgress<WorkshopProgress>? progress = null, CancellationToken cancellationToken = default) => Run("sync", mods, cancellationToken);
        private Task<WorkshopSyncResult> Run(string mode, IReadOnlyList<ServerWorkshopMod> mods, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Calls.Add((mode, mods.Select(mod => mod.WorkshopId).ToArray())); return Task.FromResult(Handler(mods)); }
    }
    private sealed class ProgressSink : IProgress<WorkshopProgress>
    { public List<WorkshopProgress> Events { get; } = []; public void Report(WorkshopProgress value) => Events.Add(value); }
}

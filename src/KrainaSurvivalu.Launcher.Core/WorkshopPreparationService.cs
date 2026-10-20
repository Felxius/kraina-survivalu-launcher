using System.Globalization;

namespace KrainaSurvivalu.Launcher.Core;

/// <summary>Reconciles the live server requirements with Steam on this PC without deleting subscriptions.</summary>
public sealed class WorkshopPreparationService
{
    private readonly IServerModsQueryService _query;
    private readonly IWorkshopClient _client;
    private readonly int _maxListChanges;

    public WorkshopPreparationService(IServerModsQueryService query, IWorkshopClient client, int maxListChanges = 3)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (maxListChanges is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(maxListChanges));
        _maxListChanges = maxListChanges;
    }

    public Task<WorkshopPreparationResult> InspectAsync(ServerDefinition server, LauncherSettings settings,
        IProgress<WorkshopProgress>? progress = null, CancellationToken cancellationToken = default) =>
        PrepareAsync(server, settings, false, progress, cancellationToken, false);

    public Task<WorkshopPreparationResult> SynchronizeAsync(ServerDefinition server, LauncherSettings settings,
        IProgress<WorkshopProgress>? progress = null, CancellationToken cancellationToken = default,
        bool requireResolvableIds = false) =>
        PrepareAsync(server, settings, true, progress, cancellationToken, requireResolvableIds);

    private async Task<WorkshopPreparationResult> PrepareAsync(ServerDefinition server, LauncherSettings settings,
        bool synchronize, IProgress<WorkshopProgress>? progress, CancellationToken cancellationToken, bool requireResolvableIds)
    {
        ArgumentNullException.ThrowIfNull(server);
        cancellationToken.ThrowIfCancellationRequested();
        var validation = PathValidation.Validate(settings);
        if (!validation.IsValid) return new(null, null, false, validation.Message);
        progress?.Report(new(null, "Odczytywanie aktualnej listy modów serwera…"));
        var current = await _query.QueryAsync(server, cancellationToken).ConfigureAwait(false);
        if (!HasList(current)) return new(current, null, false, QueryFailure);

        for (var changes = 0; ; changes++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (requireResolvableIds && current.Mods!.Any(mod => !mod.HasWorkshopId))
            {
                var unresolved = current.Mods!.Where(mod => !mod.HasWorkshopId).Select(mod => mod.Name);
                return new(current, null, true,
                    "Nie można uruchomić gry: serwer nie podaje ID Workshop dla: " + string.Join(", ", unresolved) +
                    ". Administracja musi uzupełnić te dane. Nie rozpoczęto pobierania z tej listy.");
            }
            var known = current.Mods!.Where(mod => mod.HasWorkshopId)
                .GroupBy(mod => NumericId(mod.WorkshopId)).Select(group => group.First() with
                { WorkshopId = group.Key.ToString(CultureInfo.InvariantCulture) }).ToArray();
            WorkshopSyncResult result;
            if (known.Length == 0)
                result = new(true, [], "Serwer nie zgłasza modów z rozpoznanym ID Workshop.");
            else
                result = synchronize
                    ? await _client.SynchronizeAsync(settings, known, progress, cancellationToken).ConfigureAwait(false)
                    : await _client.InspectAsync(settings, known, progress, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var merged = Merge(current, known, result);
            if (!result.Success || !merged.Success)
                return new(current, merged, true, merged.Message);
            if (!synchronize) return Finish(current, merged);

            // Downloads can take minutes. Never assume the list from the beginning of a job is still current.
            progress?.Report(new(null, "Ponowne sprawdzanie listy serwera po synchronizacji…"));
            var latest = await _query.QueryAsync(server, cancellationToken).ConfigureAwait(false);
            if (!HasList(latest)) return new(latest, merged, false,
                "Mody zostały sprawdzone, ale nie udało się potwierdzić aktualnej listy serwera. Sprawdź ponownie.");
            if (SameRequirements(current, latest)) return Finish(latest, Merge(latest, known, result));
            if (changes >= _maxListChanges) return new(latest, merged, false,
                "Lista modów serwera zmieniała się podczas synchronizacji. Spróbuj ponownie, gdy serwer zakończy aktualizację.");
            current = latest;
            progress?.Report(new(null, "Serwer zmienił listę modów. Uzupełnianie nowej listy…"));
        }
    }

    private static WorkshopPreparationResult Finish(ServerModsQueryResult metadata, WorkshopSyncResult result)
    {
        var message = !result.AllReady ? "Część modów nie jest jeszcze gotowa. " + result.Message
            : !metadata.HasCompleteWorkshopMetadata ? "Odczytane mody są gotowe. Kompletność listy serwera nadal wymaga potwierdzenia."
            : "Wszystkie mody zgłoszone przez serwer są gotowe na tym komputerze.";
        return new(metadata, result, true, message);
    }

    private static WorkshopSyncResult Merge(ServerModsQueryResult metadata, IReadOnlyList<ServerWorkshopMod> requested,
        WorkshopSyncResult result)
    {
        var expected = requested.Select(mod => NumericId(mod.WorkshopId)).ToHashSet();
        var items = new Dictionary<ulong, WorkshopItemSnapshot>();
        var valid = true;
        foreach (var item in result.Items)
        {
            if (!TryNumericId(item.WorkshopId, out var id) || !expected.Contains(id) || !items.TryAdd(id, item)) valid = false;
        }
        if (items.Count != expected.Count) valid = false;
        var merged = metadata.Mods!.Select(mod =>
        {
            if (!mod.HasWorkshopId) return new WorkshopItemSnapshot(mod.WorkshopId, mod.Name,
                Error: "Brak ID Workshop — potrzebna informacja od administracji serwera.");
            if (!items.TryGetValue(NumericId(mod.WorkshopId), out var item)) return new WorkshopItemSnapshot(mod.WorkshopId, mod.Name,
                Error: "Steam nie potwierdził stanu tego moda.");
            return item with { WorkshopId = mod.WorkshopId, Name = mod.Name };
        }).ToArray();
        return new(result.Success && valid, merged, !valid
            ? "Odpowiedź Steam nie obejmuje dokładnie żądanych modów. Nie można potwierdzić gotowości."
            : merged.Any(item => item.Error is not null && !new ServerWorkshopMod(item.Name, item.WorkshopId).HasWorkshopId)
                ? "Nie wszystkie mody mają ID Workshop. Rozpoznane mody można zsynchronizować, a brakujące ID musi podać administracja."
                : result.Message);
    }

    private static bool SameRequirements(ServerModsQueryResult first, ServerModsQueryResult second)
    {
        static string Key(ServerWorkshopMod mod) => mod.HasWorkshopId
            ? "id:" + NumericId(mod.WorkshopId).ToString(CultureInfo.InvariantCulture)
            : "unknown:" + mod.WorkshopId + ":" + mod.Name;
        return first.Mods!.Select(Key).Order(StringComparer.Ordinal).SequenceEqual(
            second.Mods!.Select(Key).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static bool HasList(ServerModsQueryResult result) => result.State == ServerModsQueryState.Available && result.Mods is not null;
    private static bool TryNumericId(string value, out ulong id) => ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    private static ulong NumericId(string value) => ulong.Parse(value, CultureInfo.InvariantCulture);
    private const string QueryFailure = "Nie udało się odczytać aktualnej listy modów serwera. Synchronizacja nie została rozpoczęta.";
}

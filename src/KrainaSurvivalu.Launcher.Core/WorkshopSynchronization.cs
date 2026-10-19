namespace KrainaSurvivalu.Launcher.Core;

/// <summary>The documented ISteamUGC EItemState bit values, preserved without inventing a version verdict.</summary>
[Flags]
public enum WorkshopItemState : uint
{
    None = 0,
    Subscribed = 1,
    LegacyItem = 2,
    Installed = 4,
    NeedsUpdate = 8,
    Downloading = 16,
    DownloadPending = 32
}

public sealed record WorkshopItemSnapshot(
    string WorkshopId,
    string Name,
    WorkshopItemState State = WorkshopItemState.None,
    string? InstalledPath = null,
    ulong BytesDownloaded = 0,
    ulong BytesTotal = 0,
    string? Error = null)
{
    public bool IsReady => Error is null && !string.IsNullOrWhiteSpace(InstalledPath) &&
        (State & WorkshopItemState.Installed) != 0 &&
        (State & (WorkshopItemState.NeedsUpdate | WorkshopItemState.Downloading | WorkshopItemState.DownloadPending)) == 0;

    public string StatusText => Error is not null ? Error
        : (State & WorkshopItemState.Downloading) != 0 ? "Pobieranie…"
        : (State & WorkshopItemState.DownloadPending) != 0 ? "W kolejce Steam"
        : (State & WorkshopItemState.NeedsUpdate) != 0 ? "Wymaga aktualizacji"
        : IsReady ? "Gotowy"
        : (State & WorkshopItemState.Installed) != 0 ? "Zainstalowany · ścieżka niepotwierdzona"
        : "Brak na komputerze";
}

public sealed record WorkshopProgress(
    string? WorkshopId,
    string Message,
    ulong BytesDownloaded = 0,
    ulong BytesTotal = 0,
    int CompletedItems = 0,
    int TotalItems = 0);

public sealed record WorkshopSyncResult(bool Success, IReadOnlyList<WorkshopItemSnapshot> Items, string Message)
{
    public bool AllReady => Success && Items.All(item => item.IsReady);
}

/// <summary>Runs on the player's PC. Implementations must never remove unrelated Workshop subscriptions or content.</summary>
public interface IWorkshopClient
{
    Task<WorkshopSyncResult> InspectAsync(LauncherSettings settings, IReadOnlyList<ServerWorkshopMod> mods,
        IProgress<WorkshopProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<WorkshopSyncResult> SynchronizeAsync(LauncherSettings settings, IReadOnlyList<ServerWorkshopMod> mods,
        IProgress<WorkshopProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed record WorkshopPreparationResult(
    ServerModsQueryResult? ServerMods,
    WorkshopSyncResult? Workshop,
    bool ServerListStable,
    string Message)
{
    public bool AllAdvertisedItemsReady => ServerListStable && Workshop is { AllReady: true };
    public bool HasCompleteReadyMetadata => AllAdvertisedItemsReady && ServerMods is { HasCompleteWorkshopMetadata: true };
}

using System.Windows.Input;
using KrainaSurvivalu.Launcher.App.Infrastructure;
using KrainaSurvivalu.Launcher.Core;

namespace KrainaSurvivalu.Launcher.App.ViewModels;

public sealed class ServerCardViewModel : ObservableObject
{
    private ServerQueryResult? _queryResult;
    private bool _isChecking;
    private ServerModsQueryResult? _modsQueryResult;
    private bool _isCheckingMods;
    private bool _isWorkshopBusy;
    private bool _isSynchronizingWorkshop;
    private string _workshopMessage = "Mody na tym komputerze nie zostały jeszcze sprawdzone.";
    private double _workshopProgressPercent;
    private bool _workshopProgressIndeterminate;
    private IReadOnlyList<LocalWorkshopItemViewModel> _localWorkshopItems = [];
    private WorkshopPreparationResult? _workshopPreparation;
    private readonly Func<ServerCardViewModel, bool> _canPlay;
    private readonly Func<ServerCardViewModel, string> _playReason;
    private bool _isLaunching;
    private string _launchMessage = "";
    private string _launchWarning = "";
    public ServerDefinition Server { get; }
    public ServerQueryResult? QueryResult => _queryResult;
    public bool IsChecking => _isChecking;
    public string Id => Server.Id;
    public string Name => Server.Name;
    public string Endpoint => $"{Server.Host}:{Server.GamePort}";
    public string Code => Id == "badlands" ? "SEKTOR 01" : "SEKTOR 02";
    public string Description => Id == "badlands" ? "BADLANDS / KRAINA SURVIVALU" : "DEER ISLE / KRAINA SURVIVALU";
    private bool HasOnlineResult => QueryResult?.State == ServerQueryState.Online;
    public bool IsOnline => !IsChecking && HasOnlineResult;
    public string StatusText => IsChecking ? "SPRAWDZANIE…" : QueryResult?.State switch
    {
        ServerQueryState.Online => "SERWER ONLINE",
        ServerQueryState.NoResponse => "BRAK ODPOWIEDZI",
        ServerQueryState.InvalidResponse => "BŁĄD ODPOWIEDZI",
        ServerQueryState.Unconfigured => "STATUS NIEDOSTĘPNY",
        _ => "STATUS NIESPRAWDZONY"
    };
    public string PlayerCount => HasOnlineResult && QueryResult?.Players is { } players && QueryResult?.MaxPlayers is { } maximum
        ? $"{players} / {maximum}" : "— / —";
    public string QueryMessage => Server.QueryPort is null ? "Brak portu Steam Query"
        : IsChecking ? "Oczekiwanie na odpowiedź…"
        : QueryResult?.State switch
        {
            ServerQueryState.Online when QueryResult?.RoundTrip is { } elapsed => $"{Math.Max(1, (int)Math.Round(elapsed.TotalMilliseconds))} ms · Steam Query",
            ServerQueryState.Online => "Steam Query odpowiada",
            ServerQueryState.NoResponse => "Nie udało się potwierdzić statusu",
            ServerQueryState.InvalidResponse => "Nieprawidłowa odpowiedź Query",
            ServerQueryState.Unconfigured => "Brak portu Steam Query",
            _ => "Oczekiwanie na pierwszy pomiar"
        };
    public string MapMessage => HasOnlineResult && !string.IsNullOrWhiteSpace(QueryResult?.Map)
        ? "Mapa: " + QueryResult?.Map : "Mapa: —";
    public string LastCheckedMessage => QueryResult is { } result
        ? "Ostatnie sprawdzenie: " + result.CheckedAtUtc.ToLocalTime().ToString("HH:mm:ss")
        : "Status nie został jeszcze sprawdzony";
    public string QueryDetails
    {
        get
        {
            var details = QueryResult?.Diagnostic ?? (Server.QueryPort is { } port
                ? $"Steam Query: {Server.Host}:{port}" : "Nie podano portu Steam Query.");
            return !string.IsNullOrWhiteSpace(QueryResult?.Name) ? QueryResult?.Name + "\n" + details : details;
        }
    }
    public ServerModsQueryResult? ModsQueryResult => _modsQueryResult;
    public bool IsCheckingMods => _isCheckingMods;
    public IReadOnlyList<ServerWorkshopMod> WorkshopMods => ModsQueryResult is { State: ServerModsQueryState.Available, Mods: { } mods }
        ? mods : [];
    public bool HasModsEntries => WorkshopMods.Count > 0;
    public string ModsStatusText => IsCheckingMods ? "ODCZYT LISTY MODÓW…" : ModsQueryResult?.State switch
    {
        ServerModsQueryState.Available when ModsQueryResult?.HasCompleteWorkshopMetadata == true => "LISTA ODCZYTANA",
        ServerModsQueryState.Available when ModsQueryResult is { Flags: not 0 } => "KOMPLETNOŚĆ WYMAGA POTWIERDZENIA",
        ServerModsQueryState.Available => "NIEPEŁNE DANE WORKSHOP",
        ServerModsQueryState.NoResponse => "BRAK ODPOWIEDZI Z LISTĄ MODÓW",
        ServerModsQueryState.InvalidResponse => "BŁĄD ODCZYTU LISTY MODÓW",
        ServerModsQueryState.Unconfigured => "ODCZYT LISTY NIEDOSTĘPNY",
        _ => "LISTA MODÓW NIESPRAWDZONA"
    };
    public string ModsMessage => IsCheckingMods ? "Sprawdzanie listy modów…" : ModsQueryResult?.State switch
    {
        ServerModsQueryState.Available when ModsQueryResult?.HasCompleteWorkshopMetadata == true => $"Odczytano listę modów: {WorkshopMods.Count}",
        ServerModsQueryState.Available when ModsQueryResult is { Flags: not 0 } => "Lista odczytana · kompletność wymaga potwierdzenia",
        ServerModsQueryState.Available => "Lista odczytana · niepełne dane Workshop",
        ServerModsQueryState.NoResponse => "Nie udało się odczytać listy modów",
        ServerModsQueryState.InvalidResponse => "Nieprawidłowa odpowiedź z listą modów",
        ServerModsQueryState.Unconfigured => "Brak portu Steam Query",
        _ => "Lista modów nie została jeszcze sprawdzona"
    };
    public string ModsCount => ModsQueryResult?.State == ServerModsQueryState.Available
        ? $"Pozycje na liście: {WorkshopMods.Count} · z ID Workshop: {WorkshopMods.Count(mod => mod.HasWorkshopId)}"
        : "Liczba modów: nieustalona";
    public string ModsLastCheckedMessage => ModsQueryResult is { } result
        ? "Ostatnie sprawdzenie: " + result.CheckedAtUtc.ToLocalTime().ToString("HH:mm:ss")
        : "Lista nie została jeszcze sprawdzona";
    public string ModsDetails => ModsQueryResult?.Diagnostic ?? "Odczyt bezpośrednio z serwera przez Steam Query.";
    public string ModsExplanation => IsCheckingMods ? "Oczekiwanie na odpowiedź serwera. Odczyt nie pobiera ani nie instaluje modów."
        : ModsQueryResult?.State switch
        {
            ServerModsQueryState.Available when ModsQueryResult is { Flags: not 0 } => "Serwer zwrócił dodatkowe, niezweryfikowane flagi odpowiedzi. Odczytane pozycje są widoczne poniżej; kompletność listy wymaga potwierdzenia.",
            ServerModsQueryState.Available when ModsQueryResult?.HasCompleteWorkshopMetadata == false => "Co najmniej jedna pozycja nie ma prawidłowego ID Workshop. Możesz pobrać rozpoznane mody; pełny zestaw nadal wymaga potwierdzenia.",
            ServerModsQueryState.Available => "Lista pochodzi z odpowiedzi tego serwera. Odczyt nie potwierdza instalacji ani aktualności modów na tym komputerze.",
            ServerModsQueryState.NoResponse => "Zapytanie o mody nie otrzymało odpowiedzi. To nie oznacza, że serwer jest wyłączony lub nie wymaga modów. Spróbuj odświeżyć listę.",
            ServerModsQueryState.InvalidResponse => "Odpowiedź nie pozwala bezpiecznie odczytać listy. Poprzednia lista została wyczyszczona, aby nie pokazywać jej jako aktualnej.",
            ServerModsQueryState.Unconfigured => "Do odczytania listy potrzebny jest potwierdzony port Steam Query tego serwera.",
            _ => "Otwórz profil lub odśwież listę, aby odczytać wymagane mody bezpośrednio z serwera. Nieznana lista nie oznacza braku modów."
        };
    public string ModsEmptyMessage => ModsQueryResult?.State == ServerModsQueryState.Available
        ? "Serwer nie zgłosił pozycji na liście modów." : "Brak potwierdzonej listy do wyświetlenia.";
    public bool IsWorkshopBusy => _isWorkshopBusy;
    public string WorkshopMessage => _workshopMessage;
    public double WorkshopProgressPercent => _workshopProgressPercent;
    public bool WorkshopProgressIndeterminate => _workshopProgressIndeterminate;
    public IReadOnlyList<LocalWorkshopItemViewModel> LocalWorkshopItems => _localWorkshopItems;
    public bool HasLocalWorkshopItems => LocalWorkshopItems.Count > 0;
    public WorkshopPreparationResult? WorkshopPreparation => _workshopPreparation;
    public string LocalWorkshopSummary => WorkshopPreparation is { HasCompleteReadyMetadata: true }
        ? "Rozpoznana lista jest stabilna, a pliki modów są gotowe."
        : WorkshopPreparation is { AllAdvertisedItemsReady: true }
            ? "Rozpoznane mody są gotowe. Kompletność listy serwera wymaga potwierdzenia."
            : "Stan plików zostanie potwierdzony przez Steam po zakończeniu zadania.";
    public string PlayUnavailableReason => _playReason(this);
    public bool CanPlay => _canPlay(this);
    public bool IsLaunching => _isLaunching;
    public string PlayButtonText => IsLaunching ? "PRZYGOTOWYWANIE…" : "GRAJ";
    public string LaunchMessage => _launchMessage;
    public bool HasLaunchMessage => !string.IsNullOrWhiteSpace(LaunchMessage);
    public string LaunchWarning => _launchWarning;
    public bool HasLaunchWarning => !string.IsNullOrWhiteSpace(LaunchWarning);
    public RelayCommand PlayCommand { get; }
    public RelayCommand CancelPlayCommand { get; }
    public ICommand CopyAddressCommand { get; }
    public ICommand OpenModsCommand { get; }

    public ServerCardViewModel(ServerDefinition server, Action<string> copyAddress, Action<ServerCardViewModel> openMods,
        Action<ServerCardViewModel> play, Func<ServerCardViewModel, bool> canPlay,
        Func<ServerCardViewModel, string> playReason, Action cancelPlay)
    {
        Server = server;
        _canPlay = canPlay;
        _playReason = playReason;
        PlayCommand = new RelayCommand(_ => play(this), _ => CanPlay);
        CancelPlayCommand = new RelayCommand(_ => cancelPlay(), _ => IsLaunching);
        CopyAddressCommand = new RelayCommand(_ => copyAddress(Endpoint));
        OpenModsCommand = new RelayCommand(_ => openMods(this));
    }
    internal void BeginLaunch()
    {
        _isLaunching = true;
        _launchMessage = "Odczytywanie wymagań serwera i przygotowywanie DayZ…";
        _launchWarning = "";
        NotifyLaunchProperties();
    }
    internal void SetLaunchFeedback(string message, string? warning = null)
    {
        _launchMessage = message;
        if (warning is not null) _launchWarning = warning;
        NotifyLaunchProperties();
    }
    internal void EndLaunch()
    {
        _isLaunching = false;
        NotifyLaunchProperties();
    }
    internal void RefreshPlayAvailability()
    {
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(PlayUnavailableReason));
        PlayCommand.Refresh();
        CancelPlayCommand.Refresh();
    }
    private void NotifyLaunchProperties()
    {
        foreach (var property in new[] { nameof(IsLaunching), nameof(PlayButtonText), nameof(LaunchMessage),
            nameof(HasLaunchMessage), nameof(LaunchWarning), nameof(HasLaunchWarning) })
            OnPropertyChanged(property);
        RefreshPlayAvailability();
    }
    internal void BeginQuery()
    {
        _isChecking = true;
        NotifyQueryProperties();
    }
    internal void ApplyQueryResult(ServerQueryResult result)
    {
        _queryResult = result;
        NotifyQueryProperties();
    }
    internal void EndQuery()
    {
        _isChecking = false;
        NotifyQueryProperties();
    }
    internal void BeginModsQuery()
    {
        _isCheckingMods = true;
        NotifyModsProperties();
    }
    internal void ApplyModsQueryResult(ServerModsQueryResult result)
    {
        if (_workshopPreparation?.ServerMods is { } previous &&
            (result.State != ServerModsQueryState.Available || previous.Flags != result.Flags ||
             !WorkshopMods.Select(mod => (mod.WorkshopId, mod.Name)).OrderBy(mod => mod.WorkshopId, StringComparer.Ordinal)
                 .SequenceEqual((result.Mods ?? []).Select(mod => (mod.WorkshopId, mod.Name)).OrderBy(mod => mod.WorkshopId, StringComparer.Ordinal))))
        {
            _workshopPreparation = null;
            _localWorkshopItems = [];
            _workshopMessage = "Lista serwera zmieniła się lub nie została potwierdzona. Sprawdź ponownie mody na komputerze.";
            NotifyWorkshopProperties();
        }
        _modsQueryResult = result;
        NotifyModsProperties();
    }
    internal void EndModsQuery()
    {
        _isCheckingMods = false;
        NotifyModsProperties();
    }
    internal void BeginWorkshopOperation(string message, bool synchronize)
    {
        _isWorkshopBusy = true;
        _isSynchronizingWorkshop = synchronize;
        _workshopMessage = message;
        _workshopProgressPercent = 0;
        _workshopProgressIndeterminate = true;
        _workshopPreparation = null;
        _localWorkshopItems = WorkshopMods.Where(mod => mod.HasWorkshopId)
            .Select(mod => new LocalWorkshopItemViewModel(new(mod.WorkshopId, mod.Name, Error: "Oczekiwanie na sprawdzenie Steam"))).ToArray();
        NotifyWorkshopProperties();
    }
    internal void ApplyWorkshopProgress(WorkshopProgress progress)
    {
        _workshopMessage = progress.Message;
        _workshopProgressIndeterminate = progress.BytesTotal == 0 && progress.TotalItems == 0;
        _workshopProgressPercent = progress.BytesTotal > 0
            ? Math.Clamp(100.0 * progress.BytesDownloaded / progress.BytesTotal, 0, 100)
            : progress.TotalItems > 0 ? Math.Clamp(100.0 * progress.CompletedItems / progress.TotalItems, 0, 100) : 0;
        if (!string.IsNullOrWhiteSpace(progress.WorkshopId))
        {
            var knownName = WorkshopMods.FirstOrDefault(mod => mod.WorkshopId == progress.WorkshopId)?.Name ?? progress.WorkshopId;
            var snapshot = _isSynchronizingWorkshop
                ? new WorkshopItemSnapshot(progress.WorkshopId, knownName,
                    progress.BytesTotal > 0 ? WorkshopItemState.Downloading : WorkshopItemState.DownloadPending,
                    BytesDownloaded: progress.BytesDownloaded, BytesTotal: progress.BytesTotal)
                : new WorkshopItemSnapshot(progress.WorkshopId, knownName, Error: "Sprawdzanie Steam…");
            _localWorkshopItems = _localWorkshopItems.Where(item => item.WorkshopId != progress.WorkshopId)
                .Append(new LocalWorkshopItemViewModel(snapshot)).ToArray();
        }
        NotifyWorkshopProperties();
    }
    internal void ApplyWorkshopPreparation(WorkshopPreparationResult result)
    {
        if (result.ServerMods is { } serverMods) ApplyModsQueryResult(serverMods);
        _workshopPreparation = result;
        _localWorkshopItems = result.Workshop?.Items.Select(snapshot => new LocalWorkshopItemViewModel(snapshot)).ToArray() ?? [];
        _workshopMessage = result.Message;
        _workshopProgressIndeterminate = false;
        _workshopProgressPercent = result.AllAdvertisedItemsReady ? 100 : 0;
        NotifyWorkshopProperties();
    }
    internal void EndWorkshopOperation(string? message = null)
    {
        _isWorkshopBusy = false;
        _workshopProgressIndeterminate = false;
        if (message is not null) _workshopMessage = message;
        NotifyWorkshopProperties();
    }
    private void NotifyWorkshopProperties()
    {
        foreach (var property in new[] { nameof(IsWorkshopBusy), nameof(WorkshopMessage), nameof(WorkshopProgressPercent),
            nameof(WorkshopProgressIndeterminate), nameof(LocalWorkshopItems), nameof(HasLocalWorkshopItems),
            nameof(WorkshopPreparation), nameof(LocalWorkshopSummary) })
            OnPropertyChanged(property);
    }
    private void NotifyModsProperties()
    {
        foreach (var property in new[] { nameof(ModsQueryResult), nameof(IsCheckingMods), nameof(WorkshopMods), nameof(HasModsEntries),
            nameof(ModsStatusText), nameof(ModsMessage), nameof(ModsCount), nameof(ModsLastCheckedMessage), nameof(ModsDetails),
            nameof(ModsExplanation), nameof(ModsEmptyMessage) })
            OnPropertyChanged(property);
        RefreshPlayAvailability();
    }
    private void NotifyQueryProperties()
    {
        foreach (var property in new[] { nameof(QueryResult), nameof(IsChecking), nameof(IsOnline), nameof(StatusText), nameof(PlayerCount), nameof(QueryMessage), nameof(MapMessage), nameof(LastCheckedMessage), nameof(QueryDetails) })
            OnPropertyChanged(property);
    }
}

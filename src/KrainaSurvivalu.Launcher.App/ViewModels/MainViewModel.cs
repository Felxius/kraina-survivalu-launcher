using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using KrainaSurvivalu.Launcher.App.Infrastructure;
using KrainaSurvivalu.Launcher.App.Services;
using KrainaSurvivalu.Launcher.Core;
using KrainaSurvivalu.Launcher.Core.Updates;

namespace KrainaSurvivalu.Launcher.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly CancellationTokenSource _queryCancellation = new();
    private DispatcherTimer? _queryTimer;
    private bool _isRefreshingServerStatus;
    private bool _disposed;
    private string _page = "Home";
    private string _dayZDirectory = "";
    private string _steamExecutable = "";
    private string _playerName = "";
    private string _message = "Wybierz serwer lub skonfiguruj ścieżki do gry.";
    private string _validationMessage = "Ścieżki nie zostały jeszcze sprawdzone.";
    private ServerCardViewModel? _selectedModsServer;
    private readonly WorkshopPreparationService _workshopPreparationService;
    private readonly Action<StagedLauncherUpdate> _applyLauncherUpdate;
    private readonly UpdateConfiguration? _injectedUpdateConfiguration;
    private UpdateConfiguration _updateConfiguration = new();
    private CancellationTokenSource? _workshopCancellation;
    private ServerCardViewModel? _activeWorkshopServer;
    private CancellationTokenSource? _launcherUpdateCancellation;
    private bool _isUpdatingLauncher;
    private string _launcherUpdateMessage = "Repozytorium aktualizacji nie zostało jeszcze skonfigurowane.";
    private double _launcherUpdatePercent;
    private bool _launcherUpdateIndeterminate;
    private StagedLauncherUpdate? _stagedLauncherUpdate;
    public ObservableCollection<ServerCardViewModel> Servers { get; } = [];
    public ICommand NavigateCommand { get; }
    public ICommand BrowseDayZCommand { get; }
    public ICommand BrowseSteamCommand { get; }
    public ICommand DetectPathsCommand { get; }
    public ICommand ValidatePathsCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public RelayCommand RefreshServerStatusCommand { get; }
    public RelayCommand RefreshServerModsCommand { get; }
    public RelayCommand InspectWorkshopCommand { get; }
    public RelayCommand SynchronizeWorkshopCommand { get; }
    public RelayCommand CancelWorkshopCommand { get; }
    public RelayCommand CheckLauncherUpdatesCommand { get; }
    public RelayCommand ApplyLauncherUpdateCommand { get; }
    public RelayCommand CancelLauncherUpdateCommand { get; }
    public string SettingsPath { get; }
    public string CurrentVersion { get; } = ReadCurrentVersion();
    public string Version => CurrentVersion.Split('+', 2)[0].Replace('-', ' ').ToUpperInvariant();
    public CommunityViewModel Community { get; }
    internal IExternalLinkOpener LinkOpener { get; }
    internal IServerQueryService QueryService { get; }
    internal IServerModsQueryService ModsQueryService { get; }
    internal IWorkshopClient WorkshopClient { get; }
    internal ILauncherUpdateService LauncherUpdateService { get; }
    internal Action<StagedLauncherUpdate> LauncherUpdateApplier => _applyLauncherUpdate;
    public bool IsWorkshopBusy => _activeWorkshopServer is not null;
    public bool IsUpdatingLauncher => _isUpdatingLauncher;
    public string LauncherUpdateMessage => _launcherUpdateMessage;
    public double LauncherUpdatePercent => _launcherUpdatePercent;
    public bool LauncherUpdateIndeterminate => _launcherUpdateIndeterminate;
    public bool LauncherUpdatesConfigured => _updateConfiguration.IsConfigured;
    public bool IsLauncherUpdateReady => _stagedLauncherUpdate is not null;
    public bool IsRefreshingServerStatus => _isRefreshingServerStatus;
    public bool IsHomePage => Page == "Home";
    public bool IsModsPage => Page == "Mods";
    public bool IsCommunityPage => Page == "Community";
    public bool IsSettingsPage => Page == "Settings";
    public string Page
    {
        get => _page;
        set
        {
            if (value is not ("Home" or "Mods" or "Community" or "Settings")) return;
            if (!Set(ref _page, value)) return;
            foreach (var name in new[] { nameof(IsHomePage), nameof(IsModsPage), nameof(IsCommunityPage), nameof(IsSettingsPage), nameof(PageTitle), nameof(PageSubtitle) })
                OnPropertyChanged(name);
            if (IsModsPage) _ = RefreshSelectedServerModsSafelyAsync();
        }
    }
    public string PageTitle => Page switch { "Mods" => "Mody serwerów", "Community" => "Społeczność", "Settings" => "Ustawienia", _ => "Baza operacyjna" };
    public string PageSubtitle => Page switch
    {
        "Mods" => "Listy odczytywane bezpośrednio z wybranego serwera.",
        "Community" => "Regulamin i kontakt z Krainą Survivalu.",
        "Settings" => "Nick gracza oraz instalacja DayZ i Steam.",
        _ => "Wybierz swój serwer Krainy Survivalu."
    };
    public string DayZDirectory { get => _dayZDirectory; set => Set(ref _dayZDirectory, value); }
    public string SteamExecutable { get => _steamExecutable; set => Set(ref _steamExecutable, value); }
    public string PlayerName
    {
        get => _playerName;
        set
        {
            if (!Set(ref _playerName, value)) return;
            OnPropertyChanged(nameof(PlayerNameMessage));
        }
    }
    public string PlayerNameMessage => string.IsNullOrWhiteSpace(PlayerName)
        ? "Wpisz własny nick zamiast domyślnego Survivor."
        : PlayerNameValidation.Validate(PlayerName.Trim()).Message;
    public string Message { get => _message; set => Set(ref _message, value); }
    public string ValidationMessage { get => _validationMessage; set => Set(ref _validationMessage, value); }
    public ServerCardViewModel? SelectedModsServer
    {
        get => _selectedModsServer;
        set
        {
            var previous = _selectedModsServer;
            if (!Set(ref _selectedModsServer, value)) return;
            if (previous is not null) previous.PropertyChanged -= SelectedModsServer_PropertyChanged;
            if (value is not null) value.PropertyChanged += SelectedModsServer_PropertyChanged;
            OnPropertyChanged(nameof(SelectedModsMessage));
            OnPropertyChanged(nameof(SelectedWorkshopIds));
            OnPropertyChanged(nameof(SelectedModsCount));
            RefreshServerModsCommand.Refresh();
            RefreshWorkshopCommands();
            if (IsModsPage) _ = RefreshSelectedServerModsSafelyAsync();
        }
    }
    public IEnumerable<string> SelectedWorkshopIds => SelectedModsServer?.WorkshopMods.Where(mod => mod.HasWorkshopId).Select(mod => mod.WorkshopId) ?? [];
    public string SelectedModsCount => SelectedModsServer?.ModsCount ?? "Liczba modów: nieustalona";
    public string SelectedModsMessage => SelectedModsServer?.ModsMessage ?? "Wybierz serwer.";

    public MainViewModel(string settingsPath, IExternalLinkOpener? linkOpener = null, IServerQueryService? queryService = null,
        IServerModsQueryService? modsQueryService = null, IWorkshopClient? workshopClient = null,
        ILauncherUpdateService? launcherUpdateService = null, Action<StagedLauncherUpdate>? applyLauncherUpdate = null,
        UpdateConfiguration? updateConfiguration = null)
    {
        SettingsPath = settingsPath;
        _settingsStore = new SettingsStore(settingsPath);
        LinkOpener = linkOpener ?? new ExternalLinkOpener();
        QueryService = queryService ?? new SteamServerQueryService();
        ModsQueryService = modsQueryService ?? new SteamServerModsQueryService();
        WorkshopClient = workshopClient ?? new SteamWorkshopClient();
        _workshopPreparationService = new WorkshopPreparationService(ModsQueryService, WorkshopClient);
        LauncherUpdateService = launcherUpdateService ?? new GitHubLauncherUpdateService();
        _applyLauncherUpdate = applyLauncherUpdate ?? ApplyLauncherUpdateAndRestart;
        _injectedUpdateConfiguration = updateConfiguration;
        Community = new CommunityViewModel(LinkOpener, message => Message = message);
        NavigateCommand = new RelayCommand(parameter => Page = parameter?.ToString() ?? "Home");
        BrowseDayZCommand = new RelayCommand(_ => BrowseDayZ());
        BrowseSteamCommand = new RelayCommand(_ => BrowseSteam());
        DetectPathsCommand = new AsyncCommand(DetectPathsAsync, ShowError);
        ValidatePathsCommand = new AsyncCommand(ValidatePathsAsync, ShowError);
        SaveSettingsCommand = new AsyncCommand(SaveSettingsAsync, ShowError);
        RefreshServerStatusCommand = new RelayCommand(parameter => { _ = RefreshServerStatusSafelyAsync(); }, _ => !_disposed && !IsRefreshingServerStatus);
        RefreshServerModsCommand = new RelayCommand(parameter => { _ = RefreshSelectedServerModsSafelyAsync(); },
            _ => !_disposed && SelectedModsServer is { IsCheckingMods: false, IsWorkshopBusy: false });
        InspectWorkshopCommand = new RelayCommand(parameter => { _ = PrepareWorkshopSafelyAsync(false); }, _ => CanPrepareWorkshop());
        SynchronizeWorkshopCommand = new RelayCommand(parameter => { _ = PrepareWorkshopSafelyAsync(true); }, _ => CanPrepareWorkshop());
        CancelWorkshopCommand = new RelayCommand(_ => _workshopCancellation?.Cancel(), _ => !_disposed && IsWorkshopBusy);
        CheckLauncherUpdatesCommand = new RelayCommand(parameter => { _ = CheckLauncherUpdatesSafelyAsync(); },
            _ => !_disposed && !IsUpdatingLauncher && !IsLauncherUpdateReady);
        ApplyLauncherUpdateCommand = new RelayCommand(_ => ApplyPreparedLauncherUpdate(),
            _ => !_disposed && IsLauncherUpdateReady && !IsUpdatingLauncher && !IsWorkshopBusy);
        CancelLauncherUpdateCommand = new RelayCommand(_ => _launcherUpdateCancellation?.Cancel(), _ => !_disposed && IsUpdatingLauncher);
    }
    public async Task InitializeAsync(string catalogPath)
    {
        var servers = await ServerCatalog.LoadAsync(catalogPath);
        SelectedModsServer = null;
        Servers.Clear();
        foreach (var server in servers)
            Servers.Add(new ServerCardViewModel(server, CopyAddress, selected => { SelectedModsServer = selected; Page = "Mods"; }));
        SelectedModsServer = Servers.FirstOrDefault();
        var loaded = await _settingsStore.LoadAsync();
        DayZDirectory = loaded.Settings.DayZDirectory;
        SteamExecutable = loaded.Settings.SteamExecutable;
        PlayerName = loaded.Settings.PlayerName;
        if (!string.IsNullOrEmpty(loaded.Warning)) Message = loaded.Warning;
        await Community.InitializeAsync(Path.Combine(Path.GetDirectoryName(catalogPath)!, "community.json"));
        _updateConfiguration = _injectedUpdateConfiguration ?? UpdateConfiguration.Load(Path.Combine(Path.GetDirectoryName(catalogPath)!, "updates.json"));
        OnPropertyChanged(nameof(LauncherUpdatesConfigured));
    }
    public LauncherSettings CurrentSettings => new() { DayZDirectory = DayZDirectory.Trim(), SteamExecutable = SteamExecutable.Trim(), PlayerName = PlayerName.Trim() };
    private void SelectedModsServer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SelectedModsMessage));
        OnPropertyChanged(nameof(SelectedWorkshopIds));
        OnPropertyChanged(nameof(SelectedModsCount));
        RefreshServerModsCommand.Refresh();
        RefreshWorkshopCommands();
    }
    public async Task RefreshServerModsAsync(ServerCardViewModel? server = null, CancellationToken cancellationToken = default)
    {
        server ??= SelectedModsServer;
        if (_disposed || server is null || server.IsCheckingMods || server.IsWorkshopBusy || !Servers.Contains(server)) return;
        cancellationToken.ThrowIfCancellationRequested();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _queryCancellation.Token);
        server.BeginModsQuery();
        RefreshServerModsCommand.Refresh();
        try
        {
            var result = await ModsQueryService.QueryAsync(server.Server, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            server.ApplyModsQueryResult(result);
        }
        finally
        {
            server.EndModsQuery();
            RefreshServerModsCommand.Refresh();
            RefreshWorkshopCommands();
        }
    }
    private bool CanPrepareWorkshop() => !_disposed && !IsWorkshopBusy &&
        SelectedModsServer is { IsCheckingMods: false } server && server.WorkshopMods.Any(mod => mod.HasWorkshopId);
    private void RefreshWorkshopCommands()
    {
        InspectWorkshopCommand.Refresh();
        SynchronizeWorkshopCommand.Refresh();
        CancelWorkshopCommand.Refresh();
        ApplyLauncherUpdateCommand.Refresh();
    }
    public async Task PrepareWorkshopAsync(bool synchronize, CancellationToken cancellationToken = default)
    {
        var server = SelectedModsServer;
        if (!CanPrepareWorkshop() || server is null) return;
        cancellationToken.ThrowIfCancellationRequested();
        var settings = CurrentSettings;
        var validation = PathValidation.Validate(settings);
        ValidationMessage = validation.Message;
        if (!validation.IsValid)
        {
            Message = "Uzupełnij poprawne ścieżki do DayZ i Steam w ustawieniach. " + validation.Message;
            return;
        }
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _queryCancellation.Token);
        _workshopCancellation = operation;
        _activeWorkshopServer = server;
        OnPropertyChanged(nameof(IsWorkshopBusy));
        server.BeginWorkshopOperation(synchronize ? "Sprawdzanie aktualnej listy przed pobieraniem…" : "Sprawdzanie aktualnej listy i plików Steam…", synchronize);
        Message = server.Name + ": " + server.WorkshopMessage;
        RefreshServerModsCommand.Refresh();
        RefreshWorkshopCommands();
        var progress = new Progress<WorkshopProgress>(value =>
        {
            if (!_disposed && ReferenceEquals(_workshopCancellation, operation) && server.IsWorkshopBusy)
                server.ApplyWorkshopProgress(value);
        });
        try
        {
            var result = synchronize
                ? await _workshopPreparationService.SynchronizeAsync(server.Server, settings, progress, operation.Token)
                : await _workshopPreparationService.InspectAsync(server.Server, settings, progress, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            server.ApplyWorkshopPreparation(result);
            Message = server.Name + ": " + result.Message;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            server.EndWorkshopOperation("Anulowano zadanie launchera. Pobieranie już zlecone Steam może być nadal kontynuowane przez Steam.");
            if (!_disposed) Message = server.Name + ": anulowano zadanie modów.";
            throw;
        }
        catch (Exception error)
        {
            server.EndWorkshopOperation("Nie udało się zakończyć zadania: " + error.Message);
            throw;
        }
        finally
        {
            server.EndWorkshopOperation();
            _activeWorkshopServer = null;
            _workshopCancellation = null;
            OnPropertyChanged(nameof(IsWorkshopBusy));
            RefreshServerModsCommand.Refresh();
            RefreshWorkshopCommands();
        }
    }
    private async Task PrepareWorkshopSafelyAsync(bool synchronize)
    {
        try { await PrepareWorkshopAsync(synchronize); }
        catch (OperationCanceledException) { }
        catch (Exception error) { ShowError(error); }
    }
    public void StartLauncherUpdateChecks()
    {
        if (!_disposed) _ = CheckLauncherUpdatesSafelyAsync();
    }
    public async Task CheckLauncherUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || IsUpdatingLauncher || IsLauncherUpdateReady) return;
        cancellationToken.ThrowIfCancellationRequested();
        if (!LauncherUpdatesConfigured)
        {
            _launcherUpdateMessage = "Automatyczne aktualizacje są przygotowane. Właściciel musi jeszcze podać repozytorium GitHub Releases i klucz publikacji.";
            NotifyLauncherUpdateProperties();
            return;
        }
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _queryCancellation.Token);
        _launcherUpdateCancellation = operation;
        _isUpdatingLauncher = true;
        _launcherUpdateMessage = "Sprawdzanie aktualizacji launchera…";
        _launcherUpdatePercent = 0;
        _launcherUpdateIndeterminate = true;
        NotifyLauncherUpdateProperties();
        try
        {
            var result = await LauncherUpdateService.CheckAsync(_updateConfiguration, CurrentVersion, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            _launcherUpdateMessage = result.Message;
            if (result.State == LauncherUpdateState.Available)
            {
                if (result.Update is null) throw new InvalidDataException("Nie otrzymano zweryfikowanych danych aktualizacji.");
                _launcherUpdateMessage = "Pobieranie zweryfikowanej aktualizacji launchera…";
                NotifyLauncherUpdateProperties();
                var progress = new Progress<LauncherUpdateProgress>(value =>
                {
                    if (_disposed || !ReferenceEquals(_launcherUpdateCancellation, operation)) return;
                    _launcherUpdatePercent = Math.Clamp(value.Percent, 0, 100);
                    _launcherUpdateIndeterminate = value.TotalBytes <= 0;
                    _launcherUpdateMessage = $"Pobieranie aktualizacji launchera: {_launcherUpdatePercent:0}%";
                    NotifyLauncherUpdateProperties();
                });
                var staged = await LauncherUpdateService.DownloadAsync(result.Update, progress, operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                _stagedLauncherUpdate = staged;
                _launcherUpdatePercent = 100;
                _launcherUpdateMessage = "Aktualizacja " + _stagedLauncherUpdate.Version + " jest gotowa. Wybierz „Zainstaluj i uruchom ponownie”.";
            }
            NotifyLauncherUpdateProperties();
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            _launcherUpdateMessage = "Anulowano sprawdzanie lub pobieranie aktualizacji launchera.";
            throw;
        }
        catch (Exception error)
        {
            _launcherUpdateMessage = "Nie udało się przygotować aktualizacji launchera: " + error.Message;
            throw;
        }
        finally
        {
            _isUpdatingLauncher = false;
            _launcherUpdateIndeterminate = false;
            _launcherUpdateCancellation = null;
            NotifyLauncherUpdateProperties();
            RefreshWorkshopCommands();
        }
    }
    private async Task CheckLauncherUpdatesSafelyAsync()
    {
        try { await CheckLauncherUpdatesAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception error) { ShowError(error); }
    }
    private void NotifyLauncherUpdateProperties()
    {
        foreach (var name in new[] { nameof(IsUpdatingLauncher), nameof(LauncherUpdateMessage), nameof(LauncherUpdatePercent),
            nameof(LauncherUpdateIndeterminate), nameof(IsLauncherUpdateReady), nameof(LauncherUpdatesConfigured) })
            OnPropertyChanged(name);
        CheckLauncherUpdatesCommand.Refresh();
        CancelLauncherUpdateCommand.Refresh();
        ApplyLauncherUpdateCommand.Refresh();
    }
    private void ApplyPreparedLauncherUpdate()
    {
        if (!ApplyLauncherUpdateCommand.CanExecute(null) || _stagedLauncherUpdate is not { } staged) return;
        try { _applyLauncherUpdate(staged); }
        catch (Exception error)
        {
            _launcherUpdateMessage = "Nie udało się uruchomić instalacji aktualizacji: " + error.Message;
            NotifyLauncherUpdateProperties();
        }
    }
    private static void ApplyLauncherUpdateAndRestart(StagedLauncherUpdate staged)
    {
        using var process = Process.GetCurrentProcess();
        LauncherUpdateInstaller.StartApply(staged, AppContext.BaseDirectory, process.Id, process.StartTime.ToUniversalTime());
        Application.Current.Shutdown();
    }
    private static string ReadCurrentVersion()
    {
        var version = typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? throw new InvalidOperationException("Brak informacji o wersji tego wydania launchera.");
        _ = LauncherVersion.Parse(version);
        return version;
    }
    private async Task RefreshSelectedServerModsSafelyAsync()
    {
        try { await RefreshServerModsAsync(); }
        catch (OperationCanceledException) when (_disposed || _queryCancellation.IsCancellationRequested) { }
        catch (Exception error) { ShowError(error); }
    }
    public async Task RefreshServerStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || IsRefreshingServerStatus) return;
        cancellationToken.ThrowIfCancellationRequested();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _queryCancellation.Token);
        _isRefreshingServerStatus = true;
        OnPropertyChanged(nameof(IsRefreshingServerStatus));
        RefreshServerStatusCommand.Refresh();
        try
        {
            await Task.WhenAll(Servers.Select(async server =>
            {
                server.BeginQuery();
                try
                {
                    var result = await QueryService.QueryAsync(server.Server, linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                    server.ApplyQueryResult(result);
                }
                finally { server.EndQuery(); }
            }));
        }
        finally
        {
            _isRefreshingServerStatus = false;
            OnPropertyChanged(nameof(IsRefreshingServerStatus));
            RefreshServerStatusCommand.Refresh();
        }
    }
    public void StartServerStatusUpdates()
    {
        if (_disposed || _queryTimer is not null) return;
        _queryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _queryTimer.Tick += QueryTimer_Tick;
        _queryTimer.Start();
        _ = RefreshServerStatusSafelyAsync();
    }
    private async void QueryTimer_Tick(object? sender, EventArgs e) => await RefreshServerStatusSafelyAsync();
    private async Task RefreshServerStatusSafelyAsync()
    {
        try { await RefreshServerStatusAsync(); }
        catch (OperationCanceledException) when (_disposed || _queryCancellation.IsCancellationRequested) { }
        catch (Exception error) { ShowError(error); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_queryTimer is not null)
        {
            _queryTimer.Stop();
            _queryTimer.Tick -= QueryTimer_Tick;
        }
        _queryCancellation.Cancel();
        _queryCancellation.Dispose();
        RefreshServerStatusCommand.Refresh();
        RefreshServerModsCommand.Refresh();
        RefreshWorkshopCommands();
        NotifyLauncherUpdateProperties();
        if (SelectedModsServer is not null) SelectedModsServer.PropertyChanged -= SelectedModsServer_PropertyChanged;
    }
    public async Task ValidatePathsAsync()
    {
        var settings = CurrentSettings;
        var result = await Task.Run(() => PathValidation.Validate(settings));
        ValidationMessage = result.Message;
        Message = result.IsValid ? "Pliki gry i Steam zostały znalezione." : "Sprawdź ścieżki w ustawieniach.";
    }
    public async Task SaveSettingsAsync()
    {
        var settings = CurrentSettings;
        await _settingsStore.SaveAsync(settings);
        DayZDirectory = settings.DayZDirectory;
        SteamExecutable = settings.SteamExecutable;
        PlayerName = settings.PlayerName;
        await ValidatePathsAsync();
        Message = string.IsNullOrEmpty(settings.PlayerName)
            ? "Ustawienia zapisane na tym komputerze. Uzupełnij jeszcze nick gracza."
            : $"Zapisano ustawienia i nick: {settings.PlayerName}.";
    }
    public async Task DetectPathsAsync()
    {
        var settings = CurrentSettings;
        var detected = await Task.Run(() => InstallationDetector.Detect(settings));
        DayZDirectory = detected.DayZDirectory;
        SteamExecutable = detected.SteamExecutable;
        await ValidatePathsAsync();
        Message = "Wykrywanie zakończone. Sprawdź ścieżki i wybierz Zapisz ustawienia.";
    }
    private void BrowseDayZ()
    {
        var dialog = new OpenFolderDialog { Title = "Wybierz folder zawierający DayZ_x64.exe", Multiselect = false };
        if (Directory.Exists(DayZDirectory)) dialog.InitialDirectory = DayZDirectory;
        if (dialog.ShowDialog() == true) DayZDirectory = dialog.FolderName;
    }
    private void BrowseSteam()
    {
        var dialog = new OpenFileDialog { Title = "Wybierz steam.exe", Filter = "Steam (steam.exe)|steam.exe", CheckFileExists = true };
        if (File.Exists(SteamExecutable)) dialog.InitialDirectory = Path.GetDirectoryName(SteamExecutable);
        if (dialog.ShowDialog() == true) SteamExecutable = dialog.FileName;
    }
    private void CopyAddress(string address)
    {
        try { Clipboard.SetText(address); Message = $"Skopiowano adres: {address}"; }
        catch (System.Runtime.InteropServices.ExternalException) { Message = "Schowek jest zajęty. Spróbuj skopiować adres ponownie."; }
    }
    private void ShowError(Exception error) => Message = $"Nie udało się wykonać operacji: {error.Message}";
}

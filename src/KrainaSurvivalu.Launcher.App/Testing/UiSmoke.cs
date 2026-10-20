using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KrainaSurvivalu.Launcher.App.ViewModels;
using KrainaSurvivalu.Launcher.App.Services;
using KrainaSurvivalu.Launcher.Core;
using KrainaSurvivalu.Launcher.Core.Updates;

namespace KrainaSurvivalu.Launcher.App.Testing;

// Explicit developer-only mode. Never starts Steam, DayZ, downloads, or an installer.
public static class UiSmoke
{
    private sealed class BindingErrors : TraceListener
    {
        public List<string> Lines { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrEmpty(message)) Lines.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
    public static async Task<bool> RunAsync(MainWindow window, MainViewModel vm, string directory)
    {
        Directory.CreateDirectory(directory);
        var results = new List<object>();
        var failures = 0;
        var recordingOpener = vm.LinkOpener as RecordingLinkOpener;
        var queryService = vm.QueryService as FakeServerQueryService;
        var queryServiceIsFake = queryService is not null;
        var modsQueryService = vm.ModsQueryService as FakeServerModsQueryService;
        var modsQueryServiceIsFake = modsQueryService is not null;
        var workshopClient = vm.WorkshopClient as FakeWorkshopClient;
        var launcherUpdateService = vm.LauncherUpdateService as FakeLauncherUpdateService;
        var launcherUpdateApplier = vm.LauncherUpdateApplier.Target as RecordingLauncherUpdateApplier;
        var gameProcessLauncher = vm.GameProcessLauncher as RecordingGameProcessLauncher;
        var workshopClientIsFake = workshopClient is not null;
        var launcherUpdateServiceIsFake = launcherUpdateService is not null;
        var launcherUpdateApplierIsRecording = launcherUpdateApplier is not null;
        var serverStatusBindingEvidence = new List<object>();
        var serverModsBindingEvidence = new List<object>();
        var workshopBindingEvidence = new List<object>();
        var launcherUpdateBindingEvidence = new List<object>();
        var playBindingEvidence = new List<object>();
        var literalOriginalRules = false;
        var imageResources = new List<object>();
        var embeddedAssetSha256 = new Dictionary<string, string>(StringComparer.Ordinal);
        var noNewsInterface = false;
        var playerNameRoundTripVerified = false;
        var nicknameBindingEvidence = new List<object>();
        int[] decodedIconSizes = [];
        const string playerName = "Łowca Północy";
        string? playerNameArgument = null;
        void Check(bool ok, string name)
        {
            results.Add(new { name, passed = ok });
            if (!ok) failures++;
        }
        var bindingErrors = new BindingErrors();
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        var surface = (FrameworkElement)window.Content;
        async Task Render(string filename, double width = 1360, double height = 920)
        {
            surface.Measure(new Size(width, height));
            surface.Arrange(new Rect(0, 0, width, height));
            surface.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, filename));
            encoder.Save(stream);
        }
        async Task EditText(TextBox box, string value)
        {
            box.SetCurrentValue(TextBox.TextProperty, value);
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
        }
        void RecordEmbeddedHash(string path)
        {
            using var stream = EmbeddedResource(path);
            embeddedAssetSha256[path] = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        async Task WaitForRefreshCompletionAsync()
        {
            var elapsed = Stopwatch.StartNew();
            while (vm.IsRefreshingServerStatus)
            {
                if (elapsed.Elapsed > TimeSpan.FromSeconds(3))
                    throw new TimeoutException("The fake status refresh did not finish within three seconds.");
                await Task.Delay(10);
            }
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        }
        async Task WaitForModsCompletionAsync()
        {
            var elapsed = Stopwatch.StartNew();
            while (vm.Servers.Any(server => server.IsCheckingMods))
            {
                if (elapsed.Elapsed > TimeSpan.FromSeconds(3))
                    throw new TimeoutException("The fake mods refresh did not finish within three seconds.");
                await Task.Delay(10);
            }
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        }
        async Task WaitUntilAsync(Func<bool> ready, string expectation)
        {
            var elapsed = Stopwatch.StartNew();
            while (!ready())
            {
                if (elapsed.Elapsed > TimeSpan.FromSeconds(3))
                    throw new TimeoutException("The controlled fixture did not reach: " + expectation);
                await Task.Delay(10);
            }
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        }
        try
        {
            vm.Page = "Home";
            // An offscreen window has not run its first layout/binding pass yet. Settle it
            // before emulating an edit, just as a user can only type after the page appears.
            await Render("00-initial-layout.png");
            var homeName = window.FindName("HomePlayerNameBox").As<TextBox>();
            var settingsName = window.FindName("PlayerNameBox").As<TextBox>();
            var homeBindingReady = homeName.GetBindingExpression(TextBox.TextProperty)?.Status == BindingStatus.Active;
            var settingsBindingReady = settingsName.GetBindingExpression(TextBox.TextProperty)?.Status == BindingStatus.Active;
            nicknameBindingEvidence.Add(new
            {
                stage = "before-first-edit-after-initial-layout", homeBindingReady, settingsBindingReady,
                homeDataContextMatches = ReferenceEquals(homeName.DataContext, vm),
                settingsDataContextMatches = ReferenceEquals(settingsName.DataContext, vm),
                homeText = homeName.Text, settingsText = settingsName.Text, modelValue = vm.PlayerName
            });
            Check(homeBindingReady && settingsBindingReady && ReferenceEquals(homeName.DataContext, vm) &&
                ReferenceEquals(settingsName.DataContext, vm),
                "Both nickname bindings are active on the actual view model after initial layout and before typing");
            await EditText(homeName, playerName);
            await Render("01-home.png");
            nicknameBindingEvidence.Add(new
            {
                stage = "after-home-edit", homeText = homeName.Text, settingsText = settingsName.Text,
                modelValue = vm.PlayerName, currentSettingsValue = vm.CurrentSettings.PlayerName
            });
            Check(window.FindName("PageHeading").As<TextBlock>().Foreground is SolidColorBrush headingBrush && headingBrush.Color == Color.FromRgb(0xF0, 0xF1, 0xE9), "Derived WPF window applies the readable light foreground from its explicit theme");
            Check(vm.Servers.Count == 2, "Two independent server profiles loaded from distributed configuration");
            var runtimeVersion = typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            Check(!string.IsNullOrWhiteSpace(runtimeVersion) && vm.CurrentVersion == runtimeVersion &&
                LauncherVersion.Parse(vm.CurrentVersion).CompareTo(LauncherVersion.Parse(runtimeVersion)) == 0 &&
                vm.Version == runtimeVersion.Split('+', 2)[0].Replace('-', ' ').ToUpperInvariant(),
                "The current update version and displayed label are derived from the actual assembly metadata rather than a hardcoded release number");
            Check(vm.Servers[0].Endpoint == "145.239.237.112:27800" && vm.Servers[1].Endpoint == "178.95.158.100:23000", "Both game endpoints exactly match the latest owner-supplied values");
            Check(vm.Servers[0].Server.QueryPort == 27801 && vm.Servers[1].Server.QueryPort == 9000 &&
                vm.Servers.All(server => server.Server.RequiredWorkshopIds is null),
                "Confirmed Steam Query ports remain separate from game ports while required mod lists stay unknown");
            var playButtons = Descendants<Button>(surface).Where(b => b.Content as string == "GRAJ").ToArray();
            Check(playButtons.Length == 2 && playButtons.All(b => !b.IsEnabled && !b.Command.CanExecute(null)), "Both actual GRAJ controls reject unconfigured launch");
            Check(vm.PlayerName == playerName && vm.CurrentSettings.PlayerName == playerName && settingsName.Text == playerName,
                "The home nickname binding preserves Unicode and spaces in settings and the settings control");
            await EditText(settingsName, "Zwiadowca");
            Check(vm.PlayerName == "Zwiadowca" && homeName.Text == "Zwiadowca",
                "The settings nickname control edits the same nickname displayed on the home page");
            await EditText(settingsName, playerName);
            RecordEmbeddedHash("Assets/Branding/ks-launcher.ico");
            using (var iconStream = EmbeddedResource("Assets/Branding/ks-launcher.ico"))
            {
                var decoder = BitmapDecoder.Create(iconStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                decodedIconSizes = decoder.Frames.Select(frame => frame.PixelWidth).OrderBy(size => size).ToArray();
                Check(window.Icon is BitmapSource icon && icon.PixelWidth > 0 && icon.PixelHeight > 0 &&
                    decodedIconSizes.SequenceEqual(new[] { 16, 24, 32, 48, 64, 128, 256 }),
                    "Window icon is loaded and all seven sizes decode from the embedded assembly resource");
            }
            var banners = Descendants<Image>(surface).Where(image => image.Name == "ServerBanner").ToArray();
            Check(banners.Length == 2, "Both server cards contain their own actual image control");
            foreach (var server in vm.Servers)
            {
                var banner = banners.Single(image => image.DataContext is ServerCardViewModel model && model.Id == server.Id);
                var filename = server.Id == "badlands" ? "badlands-v2.png" : "deer-isle-v1.png";
                RecordEmbeddedHash("Assets/Servers/" + filename);
                using var embeddedImage = EmbeddedResource("Assets/Servers/" + filename);
                var embeddedBitmap = BitmapFrame.Create(embeddedImage, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var bitmap = banner.Source as BitmapSource;
                var sourceUri = banner.Source is BitmapImage sourceImage ? sourceImage.UriSource?.OriginalString : banner.Source?.ToString();
                Check(bitmap is { PixelWidth: > 1000 } && bitmap.PixelWidth == embeddedBitmap.PixelWidth &&
                    bitmap.PixelHeight == embeddedBitmap.PixelHeight &&
                    sourceUri?.EndsWith("/Assets/Servers/" + filename, StringComparison.OrdinalIgnoreCase) == true,
                    server.Name + " displays its distinct full-resolution banner from the expected embedded resource");
                imageResources.Add(new { server = server.Id, resource = filename, sourceUri, width = bitmap?.PixelWidth, height = bitmap?.PixelHeight });
            }
            noNewsInterface = NoNewsControls(surface);
            Check(noNewsInterface, "No news tab, read-more action, editor or empty-news placeholder exists in the interface");

            Check(queryServiceIsFake && queryService?.QueryCount == 0,
                "UI smoke injects a deterministic query service and performs no automatic or live server queries");
            if (queryService is null)
                throw new InvalidOperationException("UI smoke requires FakeServerQueryService before refreshing any server status.");
            var refreshButton = window.FindName("RefreshServersButton").As<Button>();
            Check(refreshButton.IsEnabled && ReferenceEquals(refreshButton.Command, vm.RefreshServerStatusCommand) &&
                refreshButton.Command?.CanExecute(refreshButton.CommandParameter) == true &&
                refreshButton.Content as string == "ODŚWIEŻ STATUS",
                "The actual home refresh button exposes the implemented asynchronous status command");
            queryService.HoldResponses();
            refreshButton.Command!.Execute(refreshButton.CommandParameter);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Check(vm.IsRefreshingServerStatus && !refreshButton.IsEnabled && !vm.RefreshServerStatusCommand.CanExecute(null) &&
                queryService.QueryCount == 2 && queryService.PendingQueryCount == 2,
                "A pending asynchronous batch checks both profiles and disables the actual refresh control without freezing the dispatcher");
            vm.RefreshServerStatusCommand.Execute(null);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Check(queryService.QueryCount == 2 && queryService.PendingQueryCount == 2,
                "Repeated refresh while the batch is busy does not start duplicate server queries");
            queryService.ReleaseResponses();
            await WaitForRefreshCompletionAsync();
            await Render("11-server-status-online.png");
            Check(!vm.IsRefreshingServerStatus && refreshButton.IsEnabled && queryService.PendingQueryCount == 0 &&
                vm.Servers.All(server => !server.CanPlay && !server.PlayCommand.CanExecute(null)),
                "Completed responses release the batch guard and re-enable refresh while GRAJ remains blocked by unknown mods");
            foreach (var server in vm.Servers)
            {
                var expectedPlayers = server.Id == "badlands" ? 12 : 34;
                var expectedMaximum = server.Id == "badlands" ? 80 : 100;
                var expectedMap = server.Id == "badlands" ? "badlands" : "deerisle";
                var expectedPing = server.Id == "badlands" ? 37 : 61;
                var statusText = ServerText(surface, server.Id, "ServerStatusText").Text;
                var playersText = ServerText(surface, server.Id, "ServerPlayerCountText").Text;
                var mapText = ServerText(surface, server.Id, "ServerMapText").Text;
                var queryText = ServerText(surface, server.Id, "ServerQueryMessageText").Text;
                var checkedText = ServerText(surface, server.Id, "ServerLastCheckedText").Text;
                Check(server.QueryResult is { State: ServerQueryState.Online } result && result.Players == expectedPlayers &&
                    result.MaxPlayers == expectedMaximum && result.Map == expectedMap && result.RoundTrip?.TotalMilliseconds == expectedPing &&
                    statusText == server.StatusText && statusText.Contains("ONLINE", StringComparison.OrdinalIgnoreCase) &&
                    playersText == server.PlayerCount && playersText == $"{expectedPlayers} / {expectedMaximum}" &&
                    mapText.Contains(expectedMap, StringComparison.OrdinalIgnoreCase) && queryText == server.QueryMessage &&
                    queryText.Contains(expectedPing.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(checkedText),
                    server.Name + " renders its own fixture status, count, map, ping and check timestamp through actual text bindings");
                serverStatusBindingEvidence.Add(new { stage = "online-fixture", server = server.Id, statusText, playersText, mapText, queryText, checkedText });
            }
            var badlands = vm.Servers.Single(server => server.Id == "badlands");
            var originalBadlandsResult = badlands.QueryResult!;
            queryService.SetResult("badlands", new(ServerQueryState.NoResponse,
                new DateTimeOffset(2026, 10, 19, 12, 1, 0, TimeSpan.Zero), Diagnostic: "Controlled timeout fixture; no network request."));
            await vm.RefreshServerStatusAsync().WaitAsync(TimeSpan.FromSeconds(3));
            await Render("12-server-status-no-response.png");
            var unavailableStatus = ServerText(surface, "badlands", "ServerStatusText").Text;
            var unavailablePlayers = ServerText(surface, "badlands", "ServerPlayerCountText").Text;
            var unavailableMap = ServerText(surface, "badlands", "ServerMapText").Text;
            var unavailableQuery = ServerText(surface, "badlands", "ServerQueryMessageText").Text;
            Check(badlands.QueryResult is { State: ServerQueryState.NoResponse, Players: null, MaxPlayers: null, Map: null, RoundTrip: null } &&
                unavailableStatus == badlands.StatusText && !unavailableStatus.Contains("OFFLINE", StringComparison.OrdinalIgnoreCase) &&
                unavailableStatus.Contains("BRAK", StringComparison.OrdinalIgnoreCase) && unavailablePlayers == "— / —" &&
                !unavailableMap.Contains("badlands", StringComparison.OrdinalIgnoreCase) &&
                !unavailableQuery.Contains("37 ms", StringComparison.OrdinalIgnoreCase) && !unavailableQuery.Contains("37ms", StringComparison.OrdinalIgnoreCase),
                "NoResponse is honest about missing data and clears the previous count, map and ping without declaring the server offline");
            var deerIsle = vm.Servers.Single(server => server.Id == "deer-isle");
            Check(deerIsle.QueryResult is { State: ServerQueryState.Online, Players: 34, MaxPlayers: 100 } &&
                ServerText(surface, "deer-isle", "ServerPlayerCountText").Text == "34 / 100",
                "One profile timing out does not overwrite the other profile's independent successful status");
            serverStatusBindingEvidence.Add(new { stage = "no-response-fixture", server = badlands.Id,
                statusText = unavailableStatus, playersText = unavailablePlayers, mapText = unavailableMap, queryText = unavailableQuery });
            queryService.SetResult("badlands", originalBadlandsResult);
            await vm.RefreshServerStatusAsync().WaitAsync(TimeSpan.FromSeconds(3));

            var closingQueryService = new FakeServerQueryService();
            closingQueryService.HoldResponses();
            using (var closingViewModel = new MainViewModel(Path.Combine(directory, "sandbox", "query-cancellation", "settings.json"),
                new RecordingLinkOpener(), closingQueryService, new FakeServerModsQueryService(), new FakeWorkshopClient(),
                new FakeLauncherUpdateService(), new RecordingLauncherUpdateApplier().Apply, gameProcessLauncher: new RecordingGameProcessLauncher()))
            {
                await closingViewModel.InitializeAsync(Path.Combine(AppContext.BaseDirectory, "config", "servers.json"));
                var closingRefresh = closingViewModel.RefreshServerStatusAsync();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                closingViewModel.Dispose();
                var cancelled = false;
                try { await closingRefresh.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && closingQueryService.QueryCount == 2 && closingQueryService.CancelledQueryCount == 2 &&
                    closingQueryService.PendingQueryCount == 0 && !closingViewModel.IsRefreshingServerStatus,
                    "Disposing an isolated view model cancels both pending queries and releases asynchronous refresh state");
            }
            Check(!vm.SelectedWorkshopIds.Any() && vm.SelectedModsCount.Contains("nieustalona"), "Unknown mods are shown as undetermined, not zero confirmed mods");
            Check(modsQueryServiceIsFake && modsQueryService?.QueryCount == 0 && vm.Servers.All(server => server.ModsQueryResult is null),
                "UI smoke injects a separate deterministic mods reader; startup performs no mods query or live networking");
            if (modsQueryService is null)
                throw new InvalidOperationException("UI smoke requires FakeServerModsQueryService before opening any mods profile.");
            var modsButton = window.FindName("RefreshModsButton").As<Button>();
            var modsList = window.FindName("WorkshopModsList").As<ItemsControl>();
            var serverSelector = window.FindName("ServerSelector").As<ComboBox>();
            modsQueryService.HoldResponses();
            vm.Servers[1].OpenModsCommand.Execute(null);
            await Render("02-mods-deer-isle.png");
            Check(vm.Page == "Mods" && vm.SelectedModsServer == vm.Servers[1], "Deer Isle MODY command opens its own profile");
            Check(deerIsle.IsCheckingMods && !badlands.IsCheckingMods && modsQueryService.QueryCount == 1 &&
                modsQueryService.PendingQueryCount == 1 && !modsButton.IsEnabled &&
                ReferenceEquals(modsButton.Command, vm.RefreshServerModsCommand) &&
                Named<TextBlock>(surface, "ModsStatusText").Text == deerIsle.ModsStatusText &&
                Named<TextBlock>(surface, "ModsStatusText").Text.Contains("ODCZYT", StringComparison.Ordinal),
                "Opening one mods profile starts only its async query and the actual refresh control shows the busy state");
            vm.RefreshServerModsCommand.Execute(null);
            await vm.RefreshServerModsAsync(deerIsle);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Check(modsQueryService.QueryCount == 1 && modsQueryService.PendingQueryCount == 1,
                "Repeated mods command and direct refresh cannot duplicate a pending query for the selected server");
            vm.Servers[0].OpenModsCommand.Execute(null);
            await Render("03-mods-badlands.png");
            Check(vm.Page == "Mods" && vm.SelectedModsServer == vm.Servers[0], "Badlands MODY command switches to its own profile");
            Check(badlands.IsCheckingMods && deerIsle.IsCheckingMods && modsQueryService.QueryCount == 2 &&
                modsQueryService.RequestedServers.Select(server => server.Id).SequenceEqual(new[] { "deer-isle", "badlands" }),
                "Switching profiles while one is pending starts the second independent query without moving the first response to another server");
            modsQueryService.ReleaseResponses();
            await WaitForModsCompletionAsync();
            await Render("13-mods-partial-metadata-fixture.png");
            var partialNames = Descendants<TextBlock>(modsList).Where(text => text.Name == "WorkshopModNameText").Select(text => text.Text).ToArray();
            var partialIds = Descendants<TextBlock>(modsList).Where(text => text.Name == "WorkshopModIdText").Select(text => text.Text).ToArray();
            Check(badlands.ModsQueryResult is { State: ServerModsQueryState.Available, HasCompleteWorkshopMetadata: false } &&
                badlands.WorkshopMods.Count == 2 && badlands.WorkshopMods[1].WorkshopId == "0" && modsList.Items.Count == 2 &&
                partialNames.SequenceEqual(new[] { "TEST — Mod pustynny", "TEST — Mod bez ID" }) &&
                partialIds.SequenceEqual(new[] { "Workshop ID: 1234567890", "Brak ID Workshop" }) &&
                Named<TextBlock>(surface, "ModsStatusText").Text == "NIEPEŁNE DANE WORKSHOP" &&
                Named<TextBlock>(surface, "ModsCountText").Text.Contains("2 · z ID Workshop: 1", StringComparison.Ordinal),
                "Partial metadata renders literal fixture names and positive IDs while preserving ID zero as Brak ID Workshop");
            Check(Named<Button>(surface, "SynchronizeModsButton").IsEnabled &&
                ReferenceEquals(Named<Button>(surface, "SynchronizeModsButton").Command, vm.SynchronizeWorkshopCommand) && modsButton.IsEnabled &&
                vm.Servers.All(server => !server.CanPlay && !server.PlayCommand.CanExecute(null) && server.Server.RequiredWorkshopIds is null),
                "Reading lists enables explicit synchronization of known IDs but never approves the catalog or allows game launch");
            serverModsBindingEvidence.Add(new { stage = "partial-metadata-fixture", server = badlands.Id,
                statusText = Named<TextBlock>(surface, "ModsStatusText").Text,
                countText = Named<TextBlock>(surface, "ModsCountText").Text, names = partialNames, ids = partialIds });

            serverSelector.SetCurrentValue(Selector.SelectedItemProperty, deerIsle);
            serverSelector.GetBindingExpression(Selector.SelectedItemProperty)?.UpdateSource();
            await WaitForModsCompletionAsync();
            await Render("14-mods-complete-metadata-fixture.png");
            var completeNames = Descendants<TextBlock>(modsList).Where(text => text.Name == "WorkshopModNameText").Select(text => text.Text).ToArray();
            var completeIds = Descendants<TextBlock>(modsList).Where(text => text.Name == "WorkshopModIdText").Select(text => text.Text).ToArray();
            Check(vm.SelectedModsServer == deerIsle && deerIsle.ModsQueryResult is { HasCompleteWorkshopMetadata: true } &&
                modsQueryService.QueryCount == 3 && completeNames.SequenceEqual(new[] { "TEST — Mod wybrzeża", "TEST — Mod lasu" }) &&
                completeIds.SequenceEqual(new[] { "Workshop ID: 2345678901", "Workshop ID: 3456789012" }) &&
                Named<TextBlock>(surface, "ModsStatusText").Text == "LISTA ODCZYTANA" &&
                Named<TextBlock>(surface, "ModsLastCheckedText").Text == deerIsle.ModsLastCheckedMessage &&
                badlands.WorkshopMods[1].WorkshopId == "0",
                "The actual server selector auto-refreshes only its profile and binds its own names, IDs, metadata state and timestamp");
            serverModsBindingEvidence.Add(new { stage = "complete-metadata-fixture", server = deerIsle.Id,
                statusText = Named<TextBlock>(surface, "ModsStatusText").Text, names = completeNames, ids = completeIds,
                checkedText = Named<TextBlock>(surface, "ModsLastCheckedText").Text });
            modsQueryService.HoldResponses();
            modsButton.Command!.Execute(modsButton.CommandParameter);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Check(deerIsle.IsCheckingMods && !modsButton.IsEnabled && modsQueryService.QueryCount == 4 &&
                modsQueryService.PendingQueryCount == 1,
                "The actual manual mods refresh button starts a new read and disables itself for that pending server");
            modsQueryService.ReleaseResponses();
            await WaitForModsCompletionAsync();

            var originalDeerMods = deerIsle.ModsQueryResult!;
            modsQueryService.SetResult(deerIsle.Id, originalDeerMods with { Flags = 8 });
            await vm.RefreshServerModsAsync(deerIsle).WaitAsync(TimeSpan.FromSeconds(3));
            await Render("15-mods-unverified-flags-fixture.png");
            Check(deerIsle.ModsQueryResult is { Flags: 8, HasCompleteWorkshopMetadata: false } && modsList.Items.Count == 2 &&
                Named<TextBlock>(surface, "ModsStatusText").Text == "KOMPLETNOŚĆ WYMAGA POTWIERDZENIA" &&
                Named<TextBlock>(surface, "ModsExplanationText").Text.Contains("niezweryfikowane flagi", StringComparison.Ordinal) &&
                !Named<TextBlock>(surface, "ModsExplanationText").Text.Contains("ucięt", StringComparison.OrdinalIgnoreCase) &&
                Descendants<TextBlock>(modsList).Where(text => text.Name == "WorkshopModIdText").Select(text => text.Text).SequenceEqual(completeIds),
                "Unverified nonzero flags preserve all read entries and explain unknown completeness without inventing truncation or missing IDs");
            serverModsBindingEvidence.Add(new { stage = "unverified-flags-fixture", server = deerIsle.Id,
                flags = deerIsle.ModsQueryResult!.Flags, statusText = Named<TextBlock>(surface, "ModsStatusText").Text,
                explanation = Named<TextBlock>(surface, "ModsExplanationText").Text });

            modsQueryService.SetResult(badlands.Id, new(ServerModsQueryState.NoResponse,
                new DateTimeOffset(2026, 10, 19, 12, 6, 0, TimeSpan.Zero), Diagnostic: "Controlled timeout fixture; no network request."));
            serverSelector.SetCurrentValue(Selector.SelectedItemProperty, badlands);
            serverSelector.GetBindingExpression(Selector.SelectedItemProperty)?.UpdateSource();
            await WaitForModsCompletionAsync();
            await Render("16-mods-no-response-fixture.png");
            Check(badlands.ModsQueryResult is { State: ServerModsQueryState.NoResponse } && modsList.Items.Count == 0 &&
                !badlands.WorkshopMods.Any() && !vm.SelectedWorkshopIds.Any() && vm.SelectedModsCount.Contains("nieustalona", StringComparison.Ordinal) &&
                Named<TextBlock>(surface, "ModsStatusText").Text == "BRAK ODPOWIEDZI Z LISTĄ MODÓW" &&
                Named<TextBlock>(surface, "ModsEmptyText").Visibility == Visibility.Visible &&
                deerIsle.WorkshopMods.Count == 2 && deerIsle.ModsQueryResult.Flags == 8,
                "A timed-out mods read clears stale names and IDs, presents unknown rather than zero mods, and retains the other server's list");
            serverModsBindingEvidence.Add(new { stage = "no-response-fixture", server = badlands.Id,
                statusText = Named<TextBlock>(surface, "ModsStatusText").Text, actualListCount = modsList.Items.Count,
                countText = Named<TextBlock>(surface, "ModsCountText").Text });

            modsQueryService.SetResult(deerIsle.Id, new(ServerModsQueryState.InvalidResponse,
                new DateTimeOffset(2026, 10, 19, 12, 7, 0, TimeSpan.Zero), Diagnostic: "Controlled malformed response fixture; no network request."));
            serverSelector.SetCurrentValue(Selector.SelectedItemProperty, deerIsle);
            serverSelector.GetBindingExpression(Selector.SelectedItemProperty)?.UpdateSource();
            await WaitForModsCompletionAsync();
            await Render("17-mods-invalid-response-fixture.png");
            Check(deerIsle.ModsQueryResult is { State: ServerModsQueryState.InvalidResponse } && modsList.Items.Count == 0 &&
                Named<TextBlock>(surface, "ModsStatusText").Text == "BŁĄD ODCZYTU LISTY MODÓW" &&
                !Named<Button>(surface, "SynchronizeModsButton").IsEnabled && !deerIsle.CanPlay,
                "An invalid mods response clears its prior list and keeps unimplemented synchronization and launch disabled");
            modsQueryService.SetResult(deerIsle.Id, originalDeerMods);
            await vm.RefreshServerModsAsync(deerIsle).WaitAsync(TimeSpan.FromSeconds(3));
            await Render("18-mods-1120x740-fixture.png", 1120, 740);
            Check(surface.ActualWidth == 1120 && surface.ActualHeight == 740 && modsList.ActualWidth > 0 && modsList.Items.Count == 2,
                "The implemented mods page and restored entries lay out at the supported minimum window size");

            var closingModsService = new FakeServerModsQueryService();
            closingModsService.HoldResponses();
            using (var closingModsViewModel = new MainViewModel(Path.Combine(directory, "sandbox", "mods-cancellation", "settings.json"),
                new RecordingLinkOpener(), new FakeServerQueryService(), closingModsService, new FakeWorkshopClient(),
                new FakeLauncherUpdateService(), new RecordingLauncherUpdateApplier().Apply, gameProcessLauncher: new RecordingGameProcessLauncher()))
            {
                await closingModsViewModel.InitializeAsync(Path.Combine(AppContext.BaseDirectory, "config", "servers.json"));
                var pendingMods = Task.WhenAll(closingModsViewModel.Servers.Select(server => closingModsViewModel.RefreshServerModsAsync(server)));
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                closingModsViewModel.Dispose();
                var cancelled = false;
                try { await pendingMods.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && closingModsService.QueryCount == 2 && closingModsService.CancelledQueryCount == 2 &&
                    closingModsService.PendingQueryCount == 0 && closingModsViewModel.Servers.All(server => !server.IsCheckingMods) &&
                    !closingModsViewModel.RefreshServerModsCommand.CanExecute(null),
                    "Disposing an isolated view model cancels both independent mods queries and releases pending state");
            }
            vm.NavigateCommand.Execute("Community");
            await Render("04-community.png");
            Check(vm.IsCommunityPage && vm.Community.IsRulesSection, "Community opens the rules directly without news navigation");
            Check(recordingOpener is not null, "Community smoke uses a recording opener that cannot launch an external browser");
            if (recordingOpener is null)
                throw new InvalidOperationException("UI smoke requires RecordingLinkOpener before executing any external-link command.");
            var community = vm.Community;
            var productionCommunityPath = Path.Combine(AppContext.BaseDirectory, "config", "community.json");
            var productionCommunityBytes = await File.ReadAllBytesAsync(productionCommunityPath);
            var originalContent = await CommunityCatalog.LoadAsync(productionCommunityPath);
            var originalRules = originalContent.Rules?.Sections ?? [];
            Check(!community.HasLoadError && community.RuleCount == 9 && originalRules.Count == 9 && community.HasDiscord &&
                community.DiscordAddress == "https://discord.gg/qEwYQqj7MD", "Production community loads nine original rules chapters and the official Discord invitation");
            Check(originalContent.News.Count == 0 && NoNewsControls(surface), "The production content has no authored news and no news UI is exposed");
            var discordButton = Named<Button>(surface, "DiscordButton");
            Check(discordButton.IsEnabled && discordButton.Command?.CanExecute(discordButton.CommandParameter) == true,
                "The actual Discord control exposes its implemented community command");
            discordButton.Command!.Execute(discordButton.CommandParameter);
            Check(recordingOpener.OpenedUrls.SequenceEqual(new[] { "https://discord.gg/qEwYQqj7MD" }),
                "Discord command passes exactly the official invitation to the recording opener");
            var unsafeLinksRejected = true;
            foreach (var url in new[] { "http://example.invalid", "file:///C:/test.txt", "javascript:alert(1)", "https://user:secret@example.invalid", "https://example.invalid/\n" })
            {
                try { _ = ExternalLinkOpener.ValidateUrl(url); unsafeLinksRejected = false; }
                catch (ArgumentException) { }
            }
            Check(unsafeLinksRejected, "Browser destination validation rejects HTTP, file, JavaScript, credentials and control characters without opening links");

            community.ShowRulesCommand.Execute(null);
            community.SelectedRule = community.VisibleRules.First(section => section.Id == "1");
            await Render("07-rules-section-1.png");
            literalOriginalRules = originalRules.All(original => community.VisibleRules.Any(section =>
                section.Id == original.Id && section.Title == original.Title && section.Body == original.Body));
            Check(community.IsRulesSection && literalOriginalRules && Named<TextBlock>(surface, "RulesBodyText").Text == originalRules[0].Body,
                "Rules viewer renders the exact original section one text with all nine chapters unchanged");
            var firstRule = community.SelectedRule;
            Check(!community.PreviousRuleCommand.CanExecute(null), "Previous chapter command is disabled at the first original chapter");
            community.PreviousRuleCommand.Execute(null);
            Check(community.SelectedRule == firstRule, "Executing previous at the first chapter cannot move outside the rules list");
            for (var index = 0; index < 20 && community.NextRuleCommand.CanExecute(null); index++)
                community.NextRuleCommand.Execute(null);
            var lastRule = community.SelectedRule;
            community.NextRuleCommand.Execute(null);
            Check(lastRule?.Id == "9" && community.SelectedRule == lastRule && !community.NextRuleCommand.CanExecute(null),
                "Chapter navigation reaches section nine and rejects movement beyond the last chapter");

            var rulesSearch = Named<TextBox>(surface, "RulesSearchBox");
            rulesSearch.SetCurrentValue(TextBox.TextProperty, "65 metrów");
            rulesSearch.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
            Check(community.RulesSearch == "65 metrów" && community.VisibleRules.Count == 1 && community.SelectedRule?.Id == "7" &&
                Named<ListBox>(surface, "RulesList").Items.Count == 1,
                "The actual search TextBox binding finds section seven containing the original 65 metre rule");
            rulesSearch.SetCurrentValue(TextBox.TextProperty, "test-nieistniejacego-rozdzialu-ks");
            rulesSearch.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
            Check(community.IsSearchEmpty && community.VisibleRules.Count == 0 && community.SelectedRule is null &&
                Named<ListBox>(surface, "RulesList").Items.Count == 0,
                "An unmatched rules search presents an empty result instead of stale chapter content");
            community.ClearSearchCommand.Execute(null);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
            Check(community.RulesSearch == "" && rulesSearch.Text == "" && community.VisibleRules.Count == 9 && !community.IsSearchEmpty,
                "Clear-search command resets the real search control and restores all nine chapters");
            var rulesList = Named<ListBox>(surface, "RulesList");
            var ruleEight = community.VisibleRules.Single(section => section.Id == "8");
            rulesList.SetCurrentValue(Selector.SelectedItemProperty, ruleEight);
            rulesList.GetBindingExpression(Selector.SelectedItemProperty)?.UpdateSource();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
            await Render("08-rules-section-8.png");
            var originalRuleEight = originalRules.Single(section => section.Id == "8");
            Check(community.SelectedRule?.Id == "8" && Named<TextBlock>(surface, "RulesBodyText").Text == originalRuleEight.Body &&
                originalRuleEight.Body.Contains("1 samochód osobowy", StringComparison.Ordinal),
                "Selecting section eight through the actual contents list renders the original vehicle limits literally");
            await Render("10-rules-1120x740.png", 1120, 740);
            Check(surface.ActualWidth == 1120 && surface.ActualHeight == 740 && Named<TextBlock>(surface, "RulesBodyText").ActualWidth > 0,
                "Rules viewer lays out at the minimum supported 1120 by 740 size");
            noNewsInterface &= NoNewsControls(surface);
            Check(noNewsInterface, "News navigation, editor, article and placeholder are absent from the actual community visual tree");
            await community.ReloadAsync();
            Check(!community.HasLoadError && community.RuleCount == 9 &&
                community.DiscordAddress == "https://discord.gg/qEwYQqj7MD" &&
                (await File.ReadAllBytesAsync(productionCommunityPath)).SequenceEqual(productionCommunityBytes),
                "Refreshing rules retains the supplied content without changing distributed JSON bytes");
            literalOriginalRules &= originalRules.All(original => community.VisibleRules.Any(section =>
                section.Id == original.Id && section.Title == original.Title && section.Body == original.Body));
            var successfulLinksBeforeFailure = recordingOpener.OpenedUrls.Count;
            recordingOpener.FailNextOpen = true;
            community.OpenDiscordCommand.Execute(null);
            Check(recordingOpener.OpenedUrls.Count == successfulLinksBeforeFailure &&
                vm.Message.StartsWith("Nie udało się otworzyć linku.", StringComparison.Ordinal),
                "A controlled link-opener failure produces visible feedback without crashing or opening a browser");
            vm.NavigateCommand.Execute("Settings");
            Check(vm.IsSettingsPage, "Settings navigation changes visible section");
            var sandbox = Path.Combine(directory, "sandbox", "Paths with spaces");
            var dayz = Path.Combine(sandbox, "DayZ");
            var steam = Path.Combine(sandbox, "Steam", "steam.exe");
            Directory.CreateDirectory(dayz);
            Directory.CreateDirectory(Path.GetDirectoryName(steam)!);
            foreach (var file in new[] { Path.Combine(dayz, "DayZ_x64.exe"), Path.Combine(dayz, "DayZ_BE.exe"), steam })
                await File.WriteAllTextAsync(file, "Path validation fixture only; not an executable.");
            window.FindName("DayZPathBox").As<TextBox>().SetCurrentValue(TextBox.TextProperty, dayz);
            window.FindName("SteamPathBox").As<TextBox>().SetCurrentValue(TextBox.TextProperty, steam);
            await EditText(settingsName, playerName);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
            Check(vm.DayZDirectory == dayz && vm.SteamExecutable == steam && vm.PlayerName == playerName && homeName.Text == playerName,
                "Both path controls and the shared Unicode nickname propagate edited values");
            await vm.SaveSettingsAsync();
            using var reloaded = new MainViewModel(vm.SettingsPath, new RecordingLinkOpener(), new FakeServerQueryService(), new FakeServerModsQueryService(),
                new FakeWorkshopClient(), new FakeLauncherUpdateService(), new RecordingLauncherUpdateApplier().Apply,
                gameProcessLauncher: new RecordingGameProcessLauncher());
            await reloaded.InitializeAsync(Path.Combine(AppContext.BaseDirectory, "config", "servers.json"));
            playerNameRoundTripVerified = reloaded.DayZDirectory == dayz && reloaded.SteamExecutable == steam &&
                reloaded.PlayerName == playerName && reloaded.CurrentSettings.PlayerName == playerName &&
                homeName.Text == playerName && settingsName.Text == playerName;
            Check(playerNameRoundTripVerified,
                "Settings persist and reload exact paths plus the Unicode nickname with its embedded space");
            playerNameArgument = DayZLaunchArguments.CreatePlayerNameArgument(reloaded.CurrentSettings.PlayerName);
            var launchPlan = new ProcessStartInfo { FileName = "DayZ_BE.exe", UseShellExecute = false };
            launchPlan.ArgumentList.Add(playerNameArgument);
            Check(launchPlan.ArgumentList.Count == 1 && launchPlan.ArgumentList[0] == "-name=Łowca Północy",
                "The saved nickname forms one exact ArgumentList element without quoting or launching a process");
            Check(!string.IsNullOrWhiteSpace(vm.ValidationMessage), "Path validation returns visible feedback");
            await Render("05-settings.png");
            Check(workshopClientIsFake && launcherUpdateServiceIsFake && launcherUpdateApplierIsRecording &&
                workshopClient?.Calls.Count == 0 && launcherUpdateService is { CheckCount: 0, DownloadCount: 0 } &&
                launcherUpdateApplier?.AppliedUpdates.Count == 0,
                "Every main UI integration uses injected Steam/update/apply fakes; ordinary browsing never starts Steam, HTTP or an updater");
            if (workshopClient is null || launcherUpdateService is null || launcherUpdateApplier is null)
                throw new InvalidOperationException("UI smoke requires FakeWorkshopClient, FakeLauncherUpdateService and RecordingLauncherUpdateApplier before testing preparation or updates.");
            await vm.CheckLauncherUpdatesAsync();
            var pageScroll = window.FindName("MainPageScroll").As<ScrollViewer>();
            pageScroll.ScrollToEnd();
            await Render("19-updater-unconfigured.png");
            Check(!vm.LauncherUpdatesConfigured && launcherUpdateService.CheckCount == 0 && launcherUpdateService.DownloadCount == 0 &&
                Named<TextBlock>(surface, "LauncherUpdateMessageText").Text.Contains("musi jeszcze podać repozytorium", StringComparison.Ordinal) &&
                !Named<Button>(surface, "ApplyLauncherUpdateButton").IsEnabled,
                "An unconfigured release channel explains the missing owner configuration without making any HTTP request or enabling installation");
            launcherUpdateBindingEvidence.Add(new { stage = "unconfigured", configured = vm.LauncherUpdatesConfigured,
                checkCalls = launcherUpdateService.CheckCount, downloadCalls = launcherUpdateService.DownloadCount,
                message = Named<TextBlock>(surface, "LauncherUpdateMessageText").Text });

            vm.Page = "Mods";
            await WaitForModsCompletionAsync();
            pageScroll.ScrollToVerticalOffset(350);
            await Render("20-workshop-before-action-fixture.png");
            var inspectButton = window.FindName("InspectModsButton").As<Button>();
            var synchronizeButton = window.FindName("SynchronizeModsButton").As<Button>();
            var cancelModsButton = window.FindName("CancelModsButton").As<Button>();
            var localModsList = window.FindName("LocalModsList").As<ItemsControl>();
            var previousDayz = vm.DayZDirectory;
            vm.DayZDirectory = "";
            await vm.PrepareWorkshopAsync(false);
            Check(workshopClient.Calls.Count == 0 && !vm.IsWorkshopBusy && vm.Message.Contains("ścieżki", StringComparison.Ordinal),
                "Explicit local inspection rejects missing configured paths before invoking even the fake Steam worker");
            vm.DayZDirectory = previousDayz;
            workshopClient.QueueInspectResult(new(true,
                [new("2345678901", "TEST — Mod wybrzeża", WorkshopItemState.Installed | WorkshopItemState.NeedsUpdate,
                    InstalledPath: "TEST-ONLY/workshop/2345678901"),
                 new("3456789012", "TEST — Mod lasu", WorkshopItemState.None)], "TEST — kontrola plików bez pobierania."));
            inspectButton.Command!.Execute(inspectButton.CommandParameter);
            await WaitUntilAsync(() => !vm.IsWorkshopBusy, "local inspection completion");
            await Render("21-workshop-inspect-fixture.png");
            var inspectedStatuses = Descendants<TextBlock>(localModsList).Where(text => text.Name == "LocalModStatusText").Select(text => text.Text).ToArray();
            Check(workshopClient.InspectCount == 1 && workshopClient.SynchronizeCount == 0 && localModsList.Items.Count == 2 &&
                inspectedStatuses.SequenceEqual(new[] { "Wymaga aktualizacji", "Brak na komputerze" }) &&
                deerIsle.LocalWorkshopItems.All(item => !item.IsReady) && deerIsle.CanPlay && gameProcessLauncher?.Plans.Count == 0,
                "The inspect command displays NeedsUpdate and missing states without downloading or launching; GRAJ is a fresh preparation request, not cached readiness");
            workshopBindingEvidence.Add(new { stage = "inspect-fixture", server = deerIsle.Id, statuses = inspectedStatuses,
                inspectCalls = workshopClient.InspectCount, synchronizeCalls = workshopClient.SynchronizeCount });

            var listBeforeChange = new ServerModsQueryResult(ServerModsQueryState.Available,
                new DateTimeOffset(2026, 10, 19, 13, 0, 0, TimeSpan.Zero),
                [new("TEST — Mod wybrzeża", "2345678901"), new("TEST — Mod lasu", "3456789012")],
                "Controlled nonzero-flags list before synchronization; no network.", Flags: 8);
            modsQueryService.SetResult(deerIsle.Id, listBeforeChange);
            workshopClient.HoldResponses();
            synchronizeButton.Command!.Execute(synchronizeButton.CommandParameter);
            await WaitUntilAsync(() => workshopClient.PendingCount == 1, "fake download pending");
            workshopClient.Report(new("2345678901", "TEST — pobieranie 50%", 50_000_000, 100_000_000, TotalItems: 2));
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await Render("22-workshop-progress-fixture.png");
            Check(vm.IsWorkshopBusy && deerIsle.IsWorkshopBusy && !synchronizeButton.IsEnabled && !inspectButton.IsEnabled &&
                !modsButton.IsEnabled && cancelModsButton.IsEnabled &&
                Named<ProgressBar>(surface, "WorkshopProgressBar").Value == 50 &&
                Named<TextBlock>(surface, "WorkshopMessageText").Text == "TEST — pobieranie 50%" &&
                deerIsle.LocalWorkshopItems.Any(item => item.WorkshopId == "2345678901" && item.Snapshot.BytesDownloaded == 50_000_000 &&
                    item.Snapshot.BytesTotal == 100_000_000 && item.StatusText == "Pobieranie…" && !item.IsReady),
                "A pending explicit synchronization renders worker progress and per-item bytes while disabling conflicting controls and enabling cancellation");
            var callsWhileBusy = workshopClient.SynchronizeCount;
            vm.SynchronizeWorkshopCommand.Execute(null);
            await vm.PrepareWorkshopAsync(true);
            Check(workshopClient.SynchronizeCount == callsWhileBusy && workshopClient.PendingCount == 1,
                "Repeated synchronization clicks and direct calls cannot start a second Steam job while one is pending");
            workshopBindingEvidence.Add(new { stage = "download-progress-fixture", server = deerIsle.Id,
                percent = Named<ProgressBar>(surface, "WorkshopProgressBar").Value,
                message = Named<TextBlock>(surface, "WorkshopMessageText").Text,
                item = deerIsle.LocalWorkshopItems.Single(item => item.WorkshopId == "2345678901").Snapshot });
            var listAfterChange = listBeforeChange with
            {
                CheckedAtUtc = listBeforeChange.CheckedAtUtc.AddMinutes(1),
                Mods = [new("TEST — Mod wybrzeża", "2345678901"), new("TEST — Nowy mod miasta", "4567890123")]
            };
            modsQueryService.SetResult(deerIsle.Id, listAfterChange);
            workshopClient.ReleaseResponses();
            await WaitUntilAsync(() => !vm.IsWorkshopBusy, "changed server list synchronization completion");
            await Render("23-workshop-changed-list-fixture.png");
            var synchronizedCalls = workshopClient.Calls.Where(call => call.Operation == "synchronize").ToArray();
            Check(synchronizedCalls.Length == 2 &&
                synchronizedCalls[0].WorkshopIds.SequenceEqual(new[] { "2345678901", "3456789012" }) &&
                synchronizedCalls[1].WorkshopIds.SequenceEqual(new[] { "2345678901", "4567890123" }) &&
                deerIsle.WorkshopMods.Select(mod => mod.WorkshopId).SequenceEqual(new[] { "2345678901", "4567890123" }) &&
                deerIsle.WorkshopPreparation is { ServerListStable: true, AllAdvertisedItemsReady: true, HasCompleteReadyMetadata: false } &&
                deerIsle.LocalWorkshopItems.All(item => item.IsReady) && localModsList.Items.Count == 2 &&
                Named<TextBlock>(surface, "LocalWorkshopSummaryText").Text.Contains("Kompletność", StringComparison.Ordinal) &&
                badlands.LocalWorkshopItems.Count == 0 && gameProcessLauncher?.Plans.Count == 0,
                "Synchronization rechecks changed server requirements, prepares the new IDs, retains per-server state and never treats unknown flags as complete metadata");
            workshopBindingEvidence.Add(new { stage = "changed-list-ready-fixture", server = deerIsle.Id,
                calls = synchronizedCalls, finalIds = deerIsle.WorkshopMods.Select(mod => mod.WorkshopId).ToArray(),
                listStable = deerIsle.WorkshopPreparation?.ServerListStable,
                advertisedItemsReady = deerIsle.WorkshopPreparation?.AllAdvertisedItemsReady,
                completeMetadataReady = deerIsle.WorkshopPreparation?.HasCompleteReadyMetadata,
                summary = Named<TextBlock>(surface, "LocalWorkshopSummaryText").Text });

            modsQueryService.SetResult(badlands.Id, new(ServerModsQueryState.Available, listBeforeChange.CheckedAtUtc,
                [new("TEST — Mod pustynny", "1234567890"), new("TEST — Mod bez ID", "0")]));
            vm.SelectedModsServer = badlands;
            await WaitForModsCompletionAsync();
            await vm.PrepareWorkshopAsync(true);
            await Render("24-workshop-unknown-id-fixture.png");
            Check(workshopClient.Calls.Last().WorkshopIds.SequenceEqual(new[] { "1234567890" }) &&
                badlands.LocalWorkshopItems.Count == 2 &&
                badlands.LocalWorkshopItems.Any(item => item.WorkshopId == "0" && !item.IsReady && item.StatusText.Contains("Brak ID Workshop", StringComparison.Ordinal)) &&
                badlands.WorkshopPreparation is { HasCompleteReadyMetadata: false, AllAdvertisedItemsReady: false } &&
                deerIsle.LocalWorkshopItems.All(item => item.IsReady),
                "ID zero is never sent to Steam; its blocking local row stays visible while recognized IDs and the other server retain independent results");
            workshopBindingEvidence.Add(new { stage = "unknown-id-blocked-fixture", server = badlands.Id,
                requestedIds = workshopClient.Calls.Last().WorkshopIds,
                items = badlands.LocalWorkshopItems.Select(item => new { item.WorkshopId, item.StatusText, item.IsReady }).ToArray() });

            vm.SelectedModsServer = deerIsle;
            await WaitForModsCompletionAsync();
            workshopClient.HoldResponses();
            synchronizeButton.Command!.Execute(synchronizeButton.CommandParameter);
            await WaitUntilAsync(() => workshopClient.PendingCount == 1, "cancellable fake Steam job");
            cancelModsButton.Command!.Execute(cancelModsButton.CommandParameter);
            await WaitUntilAsync(() => !vm.IsWorkshopBusy, "Steam job cancellation");
            await Render("25-workshop-cancelled-fixture.png");
            Check(workshopClient.CancelledCount == 1 && workshopClient.PendingCount == 0 && !deerIsle.IsWorkshopBusy &&
                deerIsle.WorkshopPreparation is null && !cancelModsButton.IsEnabled && synchronizeButton.IsEnabled &&
                Named<TextBlock>(surface, "WorkshopMessageText").Text.Contains("może być nadal kontynuowane", StringComparison.Ordinal),
                "The actual cancellation control releases the local job without claiming to cancel Steam's already scheduled downloads or confirming readiness");
            workshopClient.ReleaseResponses();
            var closingWorkshopClient = new FakeWorkshopClient();
            closingWorkshopClient.HoldResponses();
            using (var closingWorkshopVm = new MainViewModel(Path.Combine(directory, "sandbox", "workshop-cancellation", "settings.json"),
                new RecordingLinkOpener(), new FakeServerQueryService(), new FakeServerModsQueryService(), closingWorkshopClient,
                new FakeLauncherUpdateService(), new RecordingLauncherUpdateApplier().Apply, gameProcessLauncher: new RecordingGameProcessLauncher()))
            {
                await closingWorkshopVm.InitializeAsync(Path.Combine(AppContext.BaseDirectory, "config", "servers.json"));
                closingWorkshopVm.DayZDirectory = dayz;
                closingWorkshopVm.SteamExecutable = steam;
                closingWorkshopVm.SelectedModsServer = closingWorkshopVm.Servers[1];
                await closingWorkshopVm.RefreshServerModsAsync();
                var closingJob = closingWorkshopVm.PrepareWorkshopAsync(true);
                await WaitUntilAsync(() => closingWorkshopClient.PendingCount == 1, "closing fake Steam job");
                closingWorkshopVm.Dispose();
                var cancelled = false;
                try { await closingJob.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && closingWorkshopClient.CancelledCount == 1 && closingWorkshopClient.PendingCount == 0 &&
                    !closingWorkshopVm.IsWorkshopBusy && closingWorkshopVm.Servers.All(server => !server.IsWorkshopBusy),
                    "Closing the launcher cancels its owned fake Steam job and releases both global and per-server busy state");
            }

            var configuredUpdates = new UpdateConfiguration { GitHubOwner = "TEST-ONLY", GitHubRepository = "TEST-ONLY", PublicKeyPem = "TEST-ONLY-NOT-A-REAL-KEY" };
            var updateDescriptor = new LauncherUpdateDescriptor(configuredUpdates, new("TEST-ONLY", "TEST-ONLY"),
                new(1, "0.7.1-alpha", UpdateConfiguration.ArchiveAssetName, 1000, new string('0', 64), DateTimeOffset.UtcNow),
                new Uri("https://example.invalid/TEST-ONLY.zip"));
            var configuredUpdateService = new FakeLauncherUpdateService
            {
                CheckResult = new(LauncherUpdateState.Available, "TEST — dostępna kontrolowana aktualizacja.", updateDescriptor),
                StagedResult = new(updateDescriptor.Manifest.Version, "TEST-ONLY/receipt.json", "TEST-ONLY/staged-update")
            };
            configuredUpdateService.HoldDownload();
            var configuredApplier = new RecordingLauncherUpdateApplier();
            using (var updateVm = new MainViewModel(Path.Combine(directory, "sandbox", "updates-configured", "settings.json"),
                new RecordingLinkOpener(), new FakeServerQueryService(), new FakeServerModsQueryService(), new FakeWorkshopClient(),
                configuredUpdateService, configuredApplier.Apply, configuredUpdates, new RecordingGameProcessLauncher()))
            {
                await updateVm.InitializeAsync(Path.Combine(AppContext.BaseDirectory, "config", "servers.json"));
                updateVm.Page = "Settings";
                window.DataContext = updateVm;
                updateVm.StartLauncherUpdateChecks();
                await WaitUntilAsync(() => configuredUpdateService.DownloadCount == 1, "automatic fake update download");
                configuredUpdateService.Report(new(400, 1000));
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                pageScroll.ScrollToEnd();
                await Render("26-updater-progress-fixture.png");
                Check(updateVm.IsUpdatingLauncher && configuredUpdateService.CheckCount == 1 && configuredUpdateService.DownloadCount == 1 &&
                    configuredUpdateService.CheckedVersion == runtimeVersion && configuredUpdateService.CheckedVersion == updateVm.CurrentVersion &&
                    Named<ProgressBar>(surface, "LauncherUpdateProgressBar").Value == 40 &&
                    Named<TextBlock>(surface, "LauncherUpdateMessageText").Text.Contains("40%", StringComparison.Ordinal) &&
                    !Named<Button>(surface, "CheckLauncherUpdatesButton").IsEnabled &&
                    !Named<Button>(surface, "ApplyLauncherUpdateButton").IsEnabled && Named<Button>(surface, "CancelLauncherUpdateButton").IsEnabled,
                    "A configured startup check automatically stages an available fixture update and renders progress without launching its installer");
                updateVm.StartLauncherUpdateChecks();
                Check(configuredUpdateService.CheckCount == 1 && configuredUpdateService.DownloadCount == 1,
                    "Repeated startup or check requests cannot duplicate a pending launcher update download");
                launcherUpdateBindingEvidence.Add(new { stage = "download-progress-fixture", configured = updateVm.LauncherUpdatesConfigured,
                    percent = Named<ProgressBar>(surface, "LauncherUpdateProgressBar").Value,
                    message = Named<TextBlock>(surface, "LauncherUpdateMessageText").Text,
                    checkedVersion = configuredUpdateService.CheckedVersion, runtimeVersion,
                    checkCalls = configuredUpdateService.CheckCount, downloadCalls = configuredUpdateService.DownloadCount });
                configuredUpdateService.ReleaseDownload();
                await WaitUntilAsync(() => !updateVm.IsUpdatingLauncher, "staged update ready");
                await Render("27-updater-ready-fixture.png");
                var applyButton = window.FindName("ApplyLauncherUpdateButton").As<Button>();
                Check(updateVm.IsLauncherUpdateReady && applyButton.IsEnabled &&
                    Named<ProgressBar>(surface, "LauncherUpdateProgressBar").Value == 100 && configuredApplier.AppliedUpdates.Count == 0 &&
                    Named<TextBlock>(surface, "LauncherUpdateMessageText").Text.Contains("0.7.1-alpha", StringComparison.Ordinal),
                    "A successfully staged update waits for the actual install-and-restart command instead of replacing files automatically");
                configuredApplier.FailNextApply = true;
                applyButton.Command!.Execute(applyButton.CommandParameter);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                Check(configuredApplier.AppliedUpdates.Count == 0 && updateVm.IsLauncherUpdateReady &&
                    Named<TextBlock>(surface, "LauncherUpdateMessageText").Text.Contains("Nie udało się uruchomić instalacji", StringComparison.Ordinal),
                    "An updater-start failure is reported in the actual view and leaves the staged update available for retry");
                applyButton.Command!.Execute(applyButton.CommandParameter);
                Check(configuredApplier.AppliedUpdates.Count == 1 && configuredApplier.AppliedUpdates[0] == configuredUpdateService.StagedResult,
                    "The actual install command hands exactly the staged fixture receipt to a recording applier without starting a process or shutting down the test");
                launcherUpdateBindingEvidence.Add(new { stage = "staged-update-fixture", ready = updateVm.IsLauncherUpdateReady,
                    stagedVersion = configuredApplier.AppliedUpdates.Single().Version, recordedApplyCalls = configuredApplier.AppliedUpdates.Count });
            }
            var failedUpdateService = new FakeLauncherUpdateService { CheckError = new InvalidDataException("TEST — podpis aktualizacji odrzucony.") };
            using (var failedUpdateVm = new MainViewModel(Path.Combine(directory, "sandbox", "updates-failure", "settings.json"),
                new RecordingLinkOpener(), new FakeServerQueryService(), new FakeServerModsQueryService(), new FakeWorkshopClient(),
                failedUpdateService, new RecordingLauncherUpdateApplier().Apply, configuredUpdates, new RecordingGameProcessLauncher()))
            {
                await failedUpdateVm.InitializeAsync(Path.Combine(AppContext.BaseDirectory, "config", "servers.json"));
                failedUpdateVm.Page = "Settings";
                window.DataContext = failedUpdateVm;
                var failed = false;
                try { await failedUpdateVm.CheckLauncherUpdatesAsync(); }
                catch (InvalidDataException) { failed = true; }
                pageScroll.ScrollToEnd();
                await Render("28-updater-rejected-fixture.png");
                Check(failed && failedUpdateService.CheckCount == 1 && failedUpdateService.DownloadCount == 0 &&
                    !failedUpdateVm.IsUpdatingLauncher && !failedUpdateVm.IsLauncherUpdateReady &&
                    !Named<Button>(surface, "ApplyLauncherUpdateButton").IsEnabled &&
                    Named<TextBlock>(surface, "LauncherUpdateMessageText").Text.Contains("podpis aktualizacji odrzucony", StringComparison.Ordinal),
                    "A controlled update-verification failure surfaces its error, downloads nothing and cannot enable installation");
                launcherUpdateBindingEvidence.Add(new { stage = "verification-rejected-fixture", ready = failedUpdateVm.IsLauncherUpdateReady,
                    downloadCalls = failedUpdateService.DownloadCount, message = Named<TextBlock>(surface, "LauncherUpdateMessageText").Text });
                failedUpdateService.CheckError = new OperationCanceledException("TEST — timeout bez żądania anulowania.");
                var unexpectedCancellation = false;
                try { await failedUpdateVm.CheckLauncherUpdatesAsync(); }
                catch (OperationCanceledException) { unexpectedCancellation = true; }
                await Render("30-updater-timeout-fixture.png");
                Check(unexpectedCancellation && !failedUpdateVm.IsUpdatingLauncher && !failedUpdateVm.IsLauncherUpdateReady &&
                    failedUpdateService.DownloadCount == 0 &&
                    Named<TextBlock>(surface, "LauncherUpdateMessageText").Text.StartsWith("Nie udało się przygotować aktualizacji", StringComparison.Ordinal) &&
                    !Named<TextBlock>(surface, "LauncherUpdateMessageText").Text.StartsWith("Anulowano", StringComparison.Ordinal),
                    "A timeout-style cancellation without a cancelled operation token is reported as a failure rather than invented user cancellation");
            }
            var cancelledUpdateService = new FakeLauncherUpdateService { CheckResult = new(LauncherUpdateState.Available, "TEST", updateDescriptor) };
            cancelledUpdateService.HoldDownload();
            using (var cancelledUpdateVm = new MainViewModel(Path.Combine(directory, "sandbox", "updates-cancellation", "settings.json"),
                new RecordingLinkOpener(), new FakeServerQueryService(), new FakeServerModsQueryService(), new FakeWorkshopClient(),
                cancelledUpdateService, new RecordingLauncherUpdateApplier().Apply, configuredUpdates, new RecordingGameProcessLauncher()))
            {
                await cancelledUpdateVm.InitializeAsync(Path.Combine(AppContext.BaseDirectory, "config", "servers.json"));
                cancelledUpdateVm.Page = "Settings";
                window.DataContext = cancelledUpdateVm;
                cancelledUpdateVm.StartLauncherUpdateChecks();
                await WaitUntilAsync(() => cancelledUpdateService.DownloadCount == 1, "cancellable fake launcher download");
                await Render("29-updater-before-cancel-fixture.png");
                var cancelUpdateButton = window.FindName("CancelLauncherUpdateButton").As<Button>();
                cancelUpdateButton.Command!.Execute(cancelUpdateButton.CommandParameter);
                await WaitUntilAsync(() => !cancelledUpdateVm.IsUpdatingLauncher, "launcher download cancellation");
                Check(cancelledUpdateService.CancelledCount == 1 && !cancelledUpdateVm.IsLauncherUpdateReady &&
                    !Named<Button>(surface, "ApplyLauncherUpdateButton").IsEnabled &&
                    Named<TextBlock>(surface, "LauncherUpdateMessageText").Text.Contains("Anulowano", StringComparison.Ordinal),
                    "The actual updater cancel control releases its pending download without enabling installation");
                cancelledUpdateService.ReleaseDownload();
                cancelledUpdateService.HoldDownload();
                cancelledUpdateVm.StartLauncherUpdateChecks();
                await WaitUntilAsync(() => cancelledUpdateService.DownloadCount == 2, "closing fake launcher download");
                cancelledUpdateVm.Dispose();
                await WaitUntilAsync(() => !cancelledUpdateVm.IsUpdatingLauncher, "launcher download disposal");
                Check(cancelledUpdateService.CancelledCount == 2 && !cancelledUpdateVm.IsLauncherUpdateReady,
                    "Closing the launcher cancels its pending update download without staging or applying incomplete content");
            }
            await PlayUiSmoke.RunAsync(window, directory, Check, Render, playBindingEvidence);
            window.DataContext = vm;
            vm.Page = "Home";
            pageScroll.ScrollToTop();
            await Render("06-home-1120x740.png", 1120, 740);
            Check(surface.ActualWidth == 1120 && surface.ActualHeight == 740 && homeName.Text == playerName,
                "The minimum supported home size lays out the saved nickname and both server cards");
            Check(bindingErrors.Lines.Count == 0, "No WPF binding errors or warnings during navigation and editing");
        }
        catch (Exception error)
        {
            Check(false, error.ToString());
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingErrors); }
        var report = new
        {
            status = failures == 0 ? "PASS" : "FAIL",
            currentVersion = vm.CurrentVersion,
            passed = results.Count - failures, failed = failures, tests = results,
            bindingErrors = bindingErrors.Lines,
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            actualWpfRender = true, offscreenRender = true, gameLaunched = false, networkRequests = false,
            externalBrowserOpened = false, literalOriginalRules, decodedIconSizes, imageResources,
            embeddedAssetSha256, noNewsInterface, playerNameRoundTripVerified, nicknameBindingEvidence,
            queryServiceIsFake, liveNetworkRequests = false, serverStatusBindingEvidence,
            modsQueryServiceIsFake, serverModsBindingEvidence,
            workshopClientIsFake, launcherUpdateServiceIsFake, launcherUpdateApplierIsRecording,
            workshopBindingEvidence, launcherUpdateBindingEvidence,
            gameProcessLauncherIsRecording = gameProcessLauncher is not null, playBindingEvidence,
            recordedMainGamePlans = gameProcessLauncher?.Plans, realGameProcessesStarted = false,
            fakeWorkshopCalls = workshopClient?.Calls, realSteamProcessesStarted = false, realUpdateProcessesStarted = false,
            fakeQueryRequests = queryService?.RequestedServers.Select(server => new { server.Id, server.Host, server.GamePort, server.QueryPort }).ToArray(),
            fakeModsQueryRequests = modsQueryService?.RequestedServers.Select(server => new { server.Id, server.Host, server.GamePort, server.QueryPort }).ToArray(),
            savedPlayerName = playerName, playerNameArgument,
            recordedSuccessfulLinks = recordingOpener?.OpenedUrls.ToArray() ?? [],
            recordedLinkAttempts = recordingOpener?.AttemptedUrls.ToArray() ?? [],
            scope = "Native WPF layout/render and bindings, deterministic fake server status/mod queries and Steam inspection/synchronization/progress/list-change/cancellation, mocked updater check/download/progress/staging/rejection/cancellation and recording apply action, embedded icon/images, original rules, no news UI, shared Unicode nickname and sandbox settings. All status counts, ping, mod names/IDs, file readiness and updater data are explicitly TEST fixtures. No real browser, DayZ, Steam, HTTP/UDP networking, Workshop subscription/download, update file replacement or installer/helper execution."
        };
        await File.WriteAllTextAsync(Path.Combine(directory, "ui-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static T As<T>(this object value) => (T)value;
    private static TextBlock ServerText(DependencyObject root, string serverId, string name) =>
        Descendants<TextBlock>(root).SingleOrDefault(text => text.Name == name &&
            text.DataContext is ServerCardViewModel server && server.Id == serverId)
        ?? throw new InvalidOperationException($"Required server text {serverId}/{name} was not found in the visual tree.");
    private static Stream EmbeddedResource(string path)
    {
        var uri = new Uri("pack://application:,,,/" + path, UriKind.Absolute);
        return Application.GetResourceStream(uri)?.Stream
            ?? throw new InvalidOperationException("Required embedded resource was not found: " + path);
    }
    private static bool NoNewsControls(DependencyObject root) =>
        !Descendants<Button>(root).Any(button => button.Content is string label &&
            (label.Contains("AKTUALNOŚCI", StringComparison.OrdinalIgnoreCase) || label.StartsWith("CZYTAJ DALEJ", StringComparison.OrdinalIgnoreCase))) &&
        !Descendants<FrameworkElement>(root).Any(element => element.Name.Contains("News", StringComparison.OrdinalIgnoreCase)) &&
        !Descendants<TextBlock>(root).Any(text => text.Text.Contains("Nie dodano jeszcze aktualności", StringComparison.OrdinalIgnoreCase) ||
            text.Text.Contains("OCZEKIWANIE NA PIERWSZY WPIS", StringComparison.OrdinalIgnoreCase));
    private static T Named<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants<T>(root).SingleOrDefault(element => element.Name == name)
        ?? throw new InvalidOperationException($"Required UI control {name} ({typeof(T).Name}) was not found in the visual tree.");
}

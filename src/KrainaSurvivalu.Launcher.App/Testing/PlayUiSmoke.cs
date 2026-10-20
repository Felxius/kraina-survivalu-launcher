using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KrainaSurvivalu.Launcher.App.ViewModels;
using KrainaSurvivalu.Launcher.App.Services;
using KrainaSurvivalu.Launcher.Core;
using KrainaSurvivalu.Launcher.Core.Updates;

namespace KrainaSurvivalu.Launcher.App.Testing;

/// <summary>Exercises actual WPF play controls against sandbox files and recording-only launch adapters.</summary>
public static class PlayUiSmoke
{
    public static async Task RunAsync(MainWindow window, string directory, Action<bool, string> check,
        Func<string, double, double, Task> render, List<object> evidence)
    {
        var fixtureRoot = Path.Combine(directory, "sandbox", "play", "Steam");
        var dayz = Path.Combine(fixtureRoot, "steamapps", "common", "DayZ");
        var steam = Path.Combine(fixtureRoot, "steam.exe");
        Directory.CreateDirectory(dayz);
        foreach (var file in new[] { Path.Combine(dayz, "DayZ_x64.exe"), Path.Combine(dayz, "DayZ_BE.exe"), steam })
            await File.WriteAllTextAsync(file, "UI fixture only. This is not an executable.");
        var firstIds = new[] { "2345678901", "3456789012" };
        var finalIds = new[] { "2345678901", "4567890123" };
        string ModPath(string id) => Path.Combine(fixtureRoot, "steamapps", "workshop", "content", "221100", id);
        foreach (var id in firstIds.Concat(finalIds).Distinct())
        {
            var addons = Path.Combine(ModPath(id), "addons");
            Directory.CreateDirectory(addons);
            await File.WriteAllTextAsync(Path.Combine(addons, "test.pbo"), "UI fixture only. No actual game data.");
        }
        ServerModsQueryResult Metadata(IEnumerable<string> ids, byte flags = 0) => new(ServerModsQueryState.Available,
            DateTimeOffset.UtcNow, ids.Select(id => new ServerWorkshopMod("TEST — " + id, id)).ToArray(),
            "Deterministic UI fixture; no network.", flags);
        WorkshopSyncResult Ready(IEnumerable<string> ids) => new(true, ids.Select(id => new WorkshopItemSnapshot(id,
            "TEST — " + id, WorkshopItemState.Subscribed | WorkshopItemState.Installed, ModPath(id))).ToArray(),
            "TEST — sandbox files ready.");
        async Task WaitAsync(Func<bool> completed, string expectation)
        {
            var timer = Stopwatch.StartNew();
            while (!completed())
            {
                if (timer.Elapsed > TimeSpan.FromSeconds(4)) throw new TimeoutException("Play fixture did not reach " + expectation);
                await Task.Delay(10);
            }
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        }
        async Task DrawAsync(string file) => await render(file, 1360, 1050);
        var query = new FakeServerModsQueryService();
        var workshop = new FakeWorkshopClient();
        var process = new RecordingGameProcessLauncher();
        using var vm = new MainViewModel(Path.Combine(directory, "sandbox", "play", "settings.json"), new RecordingLinkOpener(),
            new FakeServerQueryService(), query, workshop, new FakeLauncherUpdateService(), new RecordingLauncherUpdateApplier().Apply,
            new UpdateConfiguration(), process);
        await vm.InitializeAsync(Path.Combine(AppContext.BaseDirectory, "config", "servers.json"));
        window.DataContext = vm;
        vm.Page = "Home";
        window.FindName("MainPageScroll").As<ScrollViewer>().ScrollToTop();
        var deer = vm.Servers.Single(server => server.Id == "deer-isle");
        var badlands = vm.Servers.Single(server => server.Id == "badlands");
        await DrawAsync("31-play-invalid-settings-fixture.png");
        var deerButton = CardControl<Button>(window, deer.Id, "PlayButton");
        var badlandsButton = CardControl<Button>(window, badlands.Id, "PlayButton");
        check(!deerButton.IsEnabled && !badlandsButton.IsEnabled && !deer.PlayCommand.CanExecute(null) && process.Plans.Count == 0,
            "Actual GRAJ controls remain disabled until the player supplies valid paths and a personal nickname");
        vm.DayZDirectory = dayz;
        vm.SteamExecutable = steam;
        vm.PlayerName = "Survivor";
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
        check(!deerButton.IsEnabled && !deer.CanPlay && query.QueryCount == 0 && workshop.Calls.Count == 0,
            "Valid sandbox paths with the forbidden default Survivor nickname cannot query, download or request a game launch");
        vm.PlayerName = "Łowca Północy";
        await DrawAsync("32-play-ready-request-fixture.png");
        check(deerButton.IsEnabled && badlandsButton.IsEnabled && deer.CanPlay && query.QueryCount == 0 && workshop.Calls.Count == 0,
            "GRAJ becomes an explicit preparation request with valid settings without treating an unchecked server list as ready or starting work automatically");
        query.SetResult(deer.Id, Metadata(firstIds));
        query.HoldResponses();
        workshop.HoldResponses();
        workshop.QueueSyncResult(Ready(firstIds));
        workshop.QueueSyncResult(Ready(finalIds));
        deerButton.Command!.Execute(deerButton.CommandParameter);
        await WaitAsync(() => query.PendingQueryCount == 1, "a fresh query before GRAJ");
        await DrawAsync("33-play-query-fixture.png");
        check(vm.IsWorkshopBusy && deer.IsLaunching && deer.IsWorkshopBusy && !badlands.IsLaunching &&
            !deerButton.IsEnabled && !badlandsButton.IsEnabled && query.QueryCount == 1 && workshop.Calls.Count == 0 &&
            CardControl<Button>(window, deer.Id, "CancelPlayButton").IsEnabled,
            "The actual per-server GRAJ control performs a fresh async query and shares its job guard across both cards before touching Steam");
        deer.PlayCommand.Execute(null);
        badlands.PlayCommand.Execute(null);
        await vm.PlayAsync(badlands);
        await vm.PrepareWorkshopAsync(true);
        check(query.QueryCount == 1 && workshop.Calls.Count == 0 && process.Plans.Count == 0,
            "Double-clicks, the other card, direct PlayAsync and an explicit mod task cannot create a duplicate preparation job");
        query.ReleaseResponses();
        await WaitAsync(() => workshop.PendingCount == 1, "a controlled Steam synchronization");
        workshop.Report(new(firstIds[0], "TEST — pobieranie przed uruchomieniem: 50%", 50, 100, TotalItems: 2));
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        await DrawAsync("34-play-download-progress-fixture.png");
        check(CardControl<ProgressBar>(window, deer.Id, "PlayProgressBar").Value == 50 &&
            CardControl<TextBlock>(window, deer.Id, "LaunchMessageText").Text.Contains("50%", StringComparison.Ordinal) &&
            process.Plans.Count == 0 && !deerButton.IsEnabled,
            "The actual home card renders Steam progress and keeps the game unstarted throughout preparation");
        query.SetResult(deer.Id, Metadata(finalIds));
        workshop.ReleaseResponses();
        await WaitAsync(() => !vm.IsWorkshopBusy, "the recorded launch handoff");
        await DrawAsync("35-play-handoff-fixture.png");
        var plan = process.Plans.Single();
        var productionStartInfo = DayZProcessLauncher.CreateStartInfo(plan);
        check(!productionStartInfo.UseShellExecute && productionStartInfo.FileName == plan.ExecutablePath &&
            productionStartInfo.WorkingDirectory == plan.WorkingDirectory &&
            productionStartInfo.ArgumentList.SequenceEqual(plan.Arguments) &&
            productionStartInfo.ArgumentList.Take(5).SequenceEqual(new[] { "0", "1", "0", "-exe", "DayZ_x64.exe" }),
            "The production process adapter constructs the exact BattlEye bootstrap prefix and separate ArgumentList values without using a shell or starting a process");
        var modArgument = plan.Arguments.Single(argument => argument.StartsWith("-mod=", StringComparison.Ordinal));
        check(workshop.SynchronizeCount == 2 && query.QueryCount >= 3 && process.Plans.Count == 1 &&
            workshop.Calls[0].WorkshopIds.SequenceEqual(firstIds) && workshop.Calls[1].WorkshopIds.SequenceEqual(finalIds) &&
            modArgument.Contains(ModPath(finalIds[0]), StringComparison.Ordinal) && modArgument.Contains(ModPath(finalIds[1]), StringComparison.Ordinal) &&
            !modArgument.Contains(ModPath(firstIds[1]), StringComparison.Ordinal),
            "A server list change during downloads is reconciled before exactly one launch plan is recorded; removed requirements are omitted");
        check(plan.ExecutablePath == Path.Combine(dayz, "DayZ_BE.exe") && plan.WorkingDirectory == dayz && plan.SteamExecutable == steam &&
            plan.Arguments.Contains("-connect=178.95.158.100") && plan.Arguments.Contains("-port=23000") &&
            plan.Arguments.Count(argument => argument == "-name=Łowca Północy") == 1 &&
            !deer.IsLaunching && !vm.IsWorkshopBusy && deer.LaunchMessage.Contains("TEST", StringComparison.Ordinal),
            "The recording-only launch plan contains BattlEye, the exact configured game endpoint, sandbox mod folders and one Unicode nickname argument");
        evidence.Add(new { stage = "recording-only-handoff-fixture", server = deer.Id, plan.ExecutablePath, plan.WorkingDirectory,
            plan.Arguments, plansRecorded = process.Plans.Count, message = deer.LaunchMessage, realGameStarted = false });

        query.SetResult(deer.Id, Metadata(finalIds, 8));
        workshop.QueueSyncResult(Ready(finalIds));
        await vm.PlayAsync(deer);
        await DrawAsync("36-play-unverified-flags-warning-fixture.png");
        check(process.Plans.Count == 2 && deer.WorkshopPreparation is { AllAdvertisedItemsReady: true, HasCompleteReadyMetadata: false } &&
            deer.HasLaunchWarning && CardControl<TextBlock>(window, deer.Id, "LaunchWarningText").Visibility == Visibility.Visible &&
            CardControl<TextBlock>(window, deer.Id, "LaunchWarningText").Text.Contains("kompletność", StringComparison.Ordinal),
            "Unknown response flags retain the incomplete-metadata state and a visible warning while the explicit request can use the stable advertised positive IDs");
        evidence.Add(new { stage = "unverified-flags-visible-warning-fixture", server = deer.Id, flags = deer.ModsQueryResult?.Flags,
            completeMetadata = deer.WorkshopPreparation?.HasCompleteReadyMetadata, warning = deer.LaunchWarning });

        var callsBeforeBlocked = workshop.Calls.Count;
        query.SetResult(badlands.Id, Metadata(["1234567890", "0"]));
        await vm.PlayAsync(badlands);
        await DrawAsync("37-play-unknown-id-blocked-fixture.png");
        check(process.Plans.Count == 2 && workshop.Calls.Count == callsBeforeBlocked && badlands.HasLaunchMessage && !badlands.IsLaunching,
            "A fresh server list containing ID zero blocks the game request before any Steam synchronization and leaves visible feedback");
        query.SetResult(badlands.Id, new(ServerModsQueryState.NoResponse, DateTimeOffset.UtcNow));
        await vm.PlayAsync(badlands);
        await DrawAsync("38-play-no-response-fixture.png");
        check(process.Plans.Count == 2 && workshop.Calls.Count == callsBeforeBlocked &&
            badlands.ModsQueryResult?.State == ServerModsQueryState.NoResponse && badlands.LaunchMessage.Length > 0,
            "A server query timeout blocks the game request, clears stale requirements and starts no Steam or game work");

        query.SetResult(deer.Id, Metadata(finalIds));
        workshop.QueueSyncResult(new(true, finalIds.Select(id => new WorkshopItemSnapshot(id, "TEST — " + id,
            WorkshopItemState.Installed | WorkshopItemState.NeedsUpdate, ModPath(id))).ToArray(), "TEST — requires an update."));
        await vm.PlayAsync(deer);
        check(process.Plans.Count == 2 && deer.LocalWorkshopItems.All(item => !item.IsReady) && !vm.IsWorkshopBusy,
            "A successful worker response with NeedsUpdate items still cannot produce a game launch plan");
        workshop.HoldResponses();
        deerButton.Command!.Execute(deerButton.CommandParameter);
        await WaitAsync(() => workshop.PendingCount == 1, "the cancellable play preparation");
        await DrawAsync("39-play-before-cancel-fixture.png");
        var cancel = CardControl<Button>(window, deer.Id, "CancelPlayButton");
        cancel.Command!.Execute(cancel.CommandParameter);
        await WaitAsync(() => !vm.IsWorkshopBusy, "play cancellation");
        await DrawAsync("40-play-cancelled-fixture.png");
        check(process.Plans.Count == 2 && workshop.CancelledCount == 1 && !deer.IsLaunching && !deer.IsWorkshopBusy &&
            deer.CanPlay && deer.LaunchMessage.Contains("Steam może być nadal kontynuowane", StringComparison.Ordinal),
            "The actual card cancellation releases its job without launching a game or claiming to stop Steam's already queued downloads");
        workshop.ReleaseResponses();
        process.SimulateRunningGame = true;
        var queryCountBeforeRunningGame = query.QueryCount;
        var syncCountBeforeRunningGame = workshop.SynchronizeCount;
        await vm.PlayAsync(deer);
        check(process.Plans.Count == 2 && query.QueryCount == queryCountBeforeRunningGame && workshop.SynchronizeCount == syncCountBeforeRunningGame &&
            deer.LaunchMessage.Contains("już", StringComparison.Ordinal),
            "The process adapter's already-running-game result is shown before querying or mutating Steam and prevents a second start");
        process.SimulateRunningGame = false;
        process.LaunchError = new InvalidOperationException("TEST — simulated game start failure.");
        workshop.QueueSyncResult(Ready(finalIds));
        deerButton.Command!.Execute(deerButton.CommandParameter);
        await WaitAsync(() => !vm.IsWorkshopBusy, "the rejected process handoff");
        await DrawAsync("41-play-start-failure-fixture.png");
        check(process.Plans.Count == 2 && !deer.IsLaunching && deer.CanPlay &&
            deer.LaunchMessage.Contains("simulated game start failure", StringComparison.Ordinal),
            "A controlled process-start failure appears on the actual server card and releases the guard for a later retry");
        evidence.Add(new { stage = "blocked-and-cancelled-fixtures", recordedPlans = process.Plans.Count,
            cancelledSteamJobs = workshop.CancelledCount, realGameStarted = false });

        process.LaunchError = null;
        workshop.QueueSyncResult(Ready(finalIds));
        using (var cancellationAfterHandoff = new CancellationTokenSource())
        {
            process.OnLaunchRecorded = cancellationAfterHandoff.Cancel;
            try { await vm.PlayAsync(deer, cancellationAfterHandoff.Token); }
            finally { process.OnLaunchRecorded = null; }
            check(cancellationAfterHandoff.IsCancellationRequested && process.Plans.Count == 3 && !vm.IsWorkshopBusy &&
                deer.LaunchMessage.Contains("przekazano uruchomienie", StringComparison.Ordinal) &&
                !deer.LaunchMessage.StartsWith("Anulowano", StringComparison.Ordinal) &&
                deer.WorkshopMessage == deer.LaunchMessage,
                "Cancellation delivered immediately after a recorded successful handoff cannot claim the game did not start; both card and mods page retain the completed result");
            evidence.Add(new { stage = "cancel-after-started-handoff-fixture", cancellationRequested = cancellationAfterHandoff.IsCancellationRequested,
                recordedPlans = process.Plans.Count, message = deer.LaunchMessage, workshopMessage = deer.WorkshopMessage,
                realGameStarted = false });
        }

        var closingWorkshop = new FakeWorkshopClient();
        closingWorkshop.HoldResponses();
        var closingProcess = new RecordingGameProcessLauncher();
        using var closingVm = new MainViewModel(Path.Combine(directory, "sandbox", "play-closing", "settings.json"),
            new RecordingLinkOpener(), new FakeServerQueryService(), new FakeServerModsQueryService(), closingWorkshop,
            new FakeLauncherUpdateService(), new RecordingLauncherUpdateApplier().Apply, new UpdateConfiguration(), closingProcess);
        await closingVm.InitializeAsync(Path.Combine(AppContext.BaseDirectory, "config", "servers.json"));
        closingVm.DayZDirectory = dayz;
        closingVm.SteamExecutable = steam;
        closingVm.PlayerName = "TEST Gracz";
        var closingTask = closingVm.PlayAsync(closingVm.Servers[1]);
        await WaitAsync(() => closingWorkshop.PendingCount == 1, "play preparation before disposing");
        closingVm.Dispose();
        var cancelledByClosing = false;
        try { await closingTask.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (OperationCanceledException) { cancelledByClosing = true; }
        check(cancelledByClosing && closingProcess.Plans.Count == 0 && closingWorkshop.CancelledCount == 1 &&
            !closingVm.IsWorkshopBusy && closingVm.Servers.All(server => !server.IsLaunching && !server.IsWorkshopBusy),
            "Closing the launcher cancels its pending game preparation and never invokes even the recording game adapter");
    }
    private static T As<T>(this object value) => (T)value;
    private static T CardControl<T>(DependencyObject root, string serverId, string name) where T : FrameworkElement
    {
        if (root is Window { Content: DependencyObject content }) root = content;
        return Descendants<T>(root).Single(control => control.Name == name &&
            control.DataContext is ServerCardViewModel server && server.Id == serverId);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}

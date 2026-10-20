namespace KrainaSurvivalu.Launcher.Core;

public sealed record DayZLaunchPlan(string ExecutablePath, string WorkingDirectory, string SteamExecutable,
    IReadOnlyList<string> Arguments, string? Warning = null);

public sealed record GameLaunchResult(bool Started, string Message, int? ProcessId = null);

public interface IGameProcessLauncher
{
    ValidationResult CheckAvailability(LauncherSettings settings);
    Task<GameLaunchResult> LaunchAsync(DayZLaunchPlan plan, CancellationToken cancellationToken = default);
}

public sealed record DayZPlayResult(bool Started, string Message,
    WorkshopPreparationResult? Preparation = null, int? ProcessId = null);

/// <summary>One explicit Play request: fresh requirements, Steam reconciliation, then the BattlEye bootstrap.</summary>
public sealed class DayZPlayService(WorkshopPreparationService preparation, IGameProcessLauncher processLauncher)
{
    private readonly WorkshopPreparationService _preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));
    private readonly IGameProcessLauncher _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
    private readonly SemaphoreSlim _operation = new(1, 1);

    public async Task<DayZPlayResult> PlayAsync(ServerDefinition server, LauncherSettings settings,
        IProgress<WorkshopProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await _operation.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return new(false, "Przygotowanie gry już trwa. Poczekaj albo je anuluj.");
        WorkshopPreparationResult? prepared = null;
        try
        {
            var validation = DayZLaunchPlanFactory.ValidateSettingsAndServer(settings, server);
            if (!validation.IsValid) return new(false, validation.Message);
            var availability = _processLauncher.CheckAvailability(settings);
            if (!availability.IsValid) return new(false, availability.Message);
            prepared = await _preparation.SynchronizeAsync(server, settings, progress, cancellationToken,
                requireResolvableIds: true).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!prepared.AllAdvertisedItemsReady)
                return new(false, prepared.Message, prepared);
            var plan = DayZLaunchPlanFactory.Create(settings, server, prepared);
            progress?.Report(new(null, "Mody gotowe. Uruchamianie DayZ z BattlEye…"));
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _processLauncher.LaunchAsync(plan, cancellationToken).ConfigureAwait(false);
            var message = result.Message;
            if (result.Started && plan.Warning is not null) message += " " + plan.Warning;
            return new(result.Started, message, prepared, result.ProcessId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return new(false, "Nie udało się przygotować DayZ: " + exception.Message, prepared);
        }
        finally { _operation.Release(); }
    }
}

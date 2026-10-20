using KrainaSurvivalu.Launcher.Core;

namespace KrainaSurvivalu.Launcher.App.Testing;

/// <summary>UI fixtures only: records a validated plan and never inspects or starts a real process.</summary>
public sealed class RecordingGameProcessLauncher : IGameProcessLauncher
{
    public List<DayZLaunchPlan> Plans { get; } = [];
    public List<LauncherSettings> AvailabilityChecks { get; } = [];
    public bool SimulateRunningGame { get; set; }
    public Exception? LaunchError { get; set; }
    public Action? OnLaunchRecorded { get; set; }
    public GameLaunchResult Result { get; set; } = new(true, "TEST — przekazano uruchomienie DayZ. Połączenie z serwerem nie zostało potwierdzone.", 4242);

    public ValidationResult CheckAvailability(LauncherSettings settings)
    {
        AvailabilityChecks.Add(settings);
        return SimulateRunningGame ? new(false, "TEST — DayZ jest już uruchomione.") : new(true, "TEST — brak uruchomionej gry.");
    }

    public async Task<GameLaunchResult> LaunchAsync(DayZLaunchPlan plan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        if (LaunchError is { } error) throw error;
        Plans.Add(plan);
        OnLaunchRecorded?.Invoke();
        return Result;
    }
}

using KrainaSurvivalu.Launcher.Core.Updates;

namespace KrainaSurvivalu.Launcher.App.Testing;

public sealed class RecordingLauncherUpdateApplier
{
    public List<StagedLauncherUpdate> AppliedUpdates { get; } = [];
    public bool FailNextApply { get; set; }
    public void Apply(StagedLauncherUpdate update)
    {
        if (FailNextApply)
        {
            FailNextApply = false;
            throw new InvalidOperationException("Controlled updater launch failure; no process was started.");
        }
        AppliedUpdates.Add(update);
    }
}

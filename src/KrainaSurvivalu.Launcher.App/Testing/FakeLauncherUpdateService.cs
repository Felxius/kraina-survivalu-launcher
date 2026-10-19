using KrainaSurvivalu.Launcher.Core.Updates;

namespace KrainaSurvivalu.Launcher.App.Testing;

/// <summary>Controlled update fixtures only. No HTTP, signatures, ZIP extraction or process launch is performed.</summary>
public sealed class FakeLauncherUpdateService : ILauncherUpdateService
{
    private TaskCompletionSource<bool>? _downloadHold;
    private IProgress<LauncherUpdateProgress>? _progress;
    public int CheckCount { get; private set; }
    public int DownloadCount { get; private set; }
    public int CancelledCount { get; private set; }
    public string? CheckedVersion { get; private set; }
    public LauncherUpdateCheckResult CheckResult { get; set; } = new(LauncherUpdateState.UpToDate, "TEST — brak nowszej wersji.");
    public StagedLauncherUpdate StagedResult { get; set; } = new("0.6.1-alpha", "TEST-ONLY/receipt.json", "TEST-ONLY/staged-update");
    public Exception? CheckError { get; set; }
    public void HoldDownload() => _downloadHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void ReleaseDownload() { var hold = _downloadHold; _downloadHold = null; hold?.TrySetResult(true); }
    public void Report(LauncherUpdateProgress progress) => _progress?.Report(progress);
    public async Task<LauncherUpdateCheckResult> CheckAsync(UpdateConfiguration configuration, string currentVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckCount++;
        CheckedVersion = currentVersion;
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        if (CheckError is not null) throw CheckError;
        return CheckResult;
    }
    public async Task<StagedLauncherUpdate> DownloadAsync(LauncherUpdateDescriptor update,
        IProgress<LauncherUpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DownloadCount++;
        _progress = progress;
        var hold = _downloadHold?.Task;
        try
        {
            await Task.Yield();
            if (hold is not null) await hold.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return StagedResult;
        }
        catch (OperationCanceledException) { CancelledCount++; throw; }
        finally { _progress = null; }
    }
}

using System.Collections.Concurrent;
using KrainaSurvivalu.Launcher.Core;

namespace KrainaSurvivalu.Launcher.App.Testing;

/// <summary>Controlled UI-test worker. It never starts Steam, subscribes, downloads, or reads a real account.</summary>
public sealed class FakeWorkshopClient : IWorkshopClient
{
    public sealed record RecordedCall(string Operation, IReadOnlyList<string> WorkshopIds);
    private readonly ConcurrentQueue<RecordedCall> _calls = new();
    private readonly ConcurrentQueue<WorkshopSyncResult> _inspectResults = new();
    private readonly ConcurrentQueue<WorkshopSyncResult> _syncResults = new();
    private TaskCompletionSource<bool>? _hold;
    private IProgress<WorkshopProgress>? _progress;
    private int _pendingCount;
    private int _cancelledCount;
    public IReadOnlyList<RecordedCall> Calls => _calls.ToArray();
    public int InspectCount => Calls.Count(call => call.Operation == "inspect");
    public int SynchronizeCount => Calls.Count(call => call.Operation == "synchronize");
    public int PendingCount => Volatile.Read(ref _pendingCount);
    public int CancelledCount => Volatile.Read(ref _cancelledCount);
    public void QueueInspectResult(WorkshopSyncResult result) => _inspectResults.Enqueue(result);
    public void QueueSyncResult(WorkshopSyncResult result) => _syncResults.Enqueue(result);
    public void HoldResponses() => _hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void ReleaseResponses() { var hold = _hold; _hold = null; hold?.TrySetResult(true); }
    public void Report(WorkshopProgress progress) => _progress?.Report(progress);
    public Task<WorkshopSyncResult> InspectAsync(LauncherSettings settings, IReadOnlyList<ServerWorkshopMod> mods,
        IProgress<WorkshopProgress>? progress = null, CancellationToken cancellationToken = default) => RunAsync(false, mods, progress, cancellationToken);
    public Task<WorkshopSyncResult> SynchronizeAsync(LauncherSettings settings, IReadOnlyList<ServerWorkshopMod> mods,
        IProgress<WorkshopProgress>? progress = null, CancellationToken cancellationToken = default) => RunAsync(true, mods, progress, cancellationToken);
    private async Task<WorkshopSyncResult> RunAsync(bool synchronize, IReadOnlyList<ServerWorkshopMod> mods,
        IProgress<WorkshopProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Enqueue(new(synchronize ? "synchronize" : "inspect", mods.Select(mod => mod.WorkshopId).ToArray()));
        Interlocked.Increment(ref _pendingCount);
        _progress = progress;
        var hold = _hold?.Task;
        try
        {
            await Task.Yield();
            progress?.Report(new(null, "TEST — kontrolowane zadanie Steam", TotalItems: mods.Count));
            if (hold is not null) await hold.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var results = synchronize ? _syncResults : _inspectResults;
            if (results.TryDequeue(out var result)) return result;
            return new(true, mods.Select(mod => new WorkshopItemSnapshot(mod.WorkshopId, mod.Name,
                WorkshopItemState.Subscribed | WorkshopItemState.Installed,
                InstalledPath: "TEST-ONLY/workshop/" + mod.WorkshopId)).ToArray(), "TEST — pliki potwierdzone przez atrapę Steam.");
        }
        catch (OperationCanceledException) { Interlocked.Increment(ref _cancelledCount); throw; }
        finally { _progress = null; Interlocked.Decrement(ref _pendingCount); }
    }
}

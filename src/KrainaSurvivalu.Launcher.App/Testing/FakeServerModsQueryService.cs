using System.Collections.Concurrent;
using KrainaSurvivalu.Launcher.Core;

namespace KrainaSurvivalu.Launcher.App.Testing;

/// <summary>Explicit UI fixtures only. Names and IDs below are simulated and no networking code is used.</summary>
public sealed class FakeServerModsQueryService : IServerModsQueryService
{
    private readonly ConcurrentDictionary<string, ServerModsQueryResult> _results = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<ServerDefinition> _requestedServers = new();
    private readonly object _holdLock = new();
    private TaskCompletionSource<bool>? _heldResponses;
    private int _queryCount;
    private int _pendingQueryCount;
    private int _cancelledQueryCount;

    public int QueryCount => Volatile.Read(ref _queryCount);
    public int PendingQueryCount => Volatile.Read(ref _pendingQueryCount);
    public int CancelledQueryCount => Volatile.Read(ref _cancelledQueryCount);
    public IReadOnlyList<ServerDefinition> RequestedServers => _requestedServers.ToArray();

    public FakeServerModsQueryService()
    {
        var timestamp = new DateTimeOffset(2026, 10, 19, 12, 5, 0, TimeSpan.Zero);
        SetResult("badlands", new(ServerModsQueryState.Available, timestamp,
            [new("TEST — Mod pustynny", "1234567890"), new("TEST — Mod bez ID", "0")],
            "Controlled partial metadata fixture; no network request."));
        SetResult("deer-isle", new(ServerModsQueryState.Available, timestamp,
            [new("TEST — Mod wybrzeża", "2345678901"), new("TEST — Mod lasu", "3456789012")],
            "Controlled complete metadata fixture; no network request."));
    }

    public void SetResult(string serverId, ServerModsQueryResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        ArgumentNullException.ThrowIfNull(result);
        _results[serverId] = result;
    }

    public void HoldResponses()
    {
        lock (_holdLock)
        {
            if (_heldResponses is not null) throw new InvalidOperationException("Test responses are already held.");
            _heldResponses = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void ReleaseResponses()
    {
        TaskCompletionSource<bool>? pending;
        lock (_holdLock)
        {
            pending = _heldResponses;
            _heldResponses = null;
        }
        pending?.TrySetResult(true);
    }

    public async Task<ServerModsQueryResult> QueryAsync(ServerDefinition server, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        cancellationToken.ThrowIfCancellationRequested();
        _requestedServers.Enqueue(server);
        Interlocked.Increment(ref _queryCount);
        Interlocked.Increment(ref _pendingQueryCount);
        Task? hold;
        lock (_holdLock) hold = _heldResponses?.Task;
        try
        {
            await Task.Yield();
            if (hold is not null) await hold.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return _results.TryGetValue(server.Id, out var result)
                ? result : new(ServerModsQueryState.Unconfigured, DateTimeOffset.UtcNow,
                    Diagnostic: "No deterministic fixture was configured for this server.");
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _cancelledQueryCount);
            throw;
        }
        finally { Interlocked.Decrement(ref _pendingQueryCount); }
    }
}

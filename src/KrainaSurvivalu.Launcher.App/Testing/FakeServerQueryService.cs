using System.Collections.Concurrent;
using KrainaSurvivalu.Launcher.Core;

namespace KrainaSurvivalu.Launcher.App.Testing;

/// <summary>Deterministic developer-test responses only. This class has no networking code.</summary>
public sealed class FakeServerQueryService : IServerQueryService
{
    private readonly ConcurrentDictionary<string, ServerQueryResult> _results = new(StringComparer.Ordinal);
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

    public FakeServerQueryService()
    {
        var timestamp = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        SetResult("badlands", new(ServerQueryState.Online, timestamp, Name: "TEST — Badlands",
            Map: "badlands", Players: 12, MaxPlayers: 80, Bots: 0, RoundTrip: TimeSpan.FromMilliseconds(37)));
        SetResult("deer-isle", new(ServerQueryState.Online, timestamp, Name: "TEST — Deer Isle",
            Map: "deerisle", Players: 34, MaxPlayers: 100, Bots: 1, RoundTrip: TimeSpan.FromMilliseconds(61)));
    }

    public void SetResult(string serverId, ServerQueryResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        ArgumentNullException.ThrowIfNull(result);
        _results[serverId] = result;
    }

    public void HoldResponses()
    {
        lock (_holdLock)
        {
            if (_heldResponses is not null)
                throw new InvalidOperationException("Test responses are already held.");
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

    public async Task<ServerQueryResult> QueryAsync(ServerDefinition server, CancellationToken cancellationToken = default)
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
            // Always yield so that the test observes real asynchronous UI updates.
            await Task.Yield();
            if (hold is not null) await hold.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return _results.TryGetValue(server.Id, out var result)
                ? result
                : new(ServerQueryState.Unconfigured, DateTimeOffset.UtcNow, Diagnostic: "No deterministic fixture was configured for this server.");
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _cancelledQueryCount);
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _pendingQueryCount);
        }
    }
}

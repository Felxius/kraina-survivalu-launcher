using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace KrainaSurvivalu.Launcher.Core;

/// <summary>Transport boundary for deterministic offline tests; production sessions use connected UDP.</summary>
public interface IServerQueryTransport
{
    Task<IServerQuerySession> CreateSessionAsync(string host, int queryPort, CancellationToken cancellationToken);
}

public interface IServerQuerySession : IDisposable
{
    ValueTask SendAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken);
    ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken cancellationToken);
}

public sealed class SteamServerQueryService : IServerQueryService
{
    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromSeconds(5);
    private readonly IServerQueryTransport _transport;
    private readonly TimeSpan _timeout;

    public SteamServerQueryService(IServerQueryTransport? transport = null, TimeSpan? timeout = null)
    {
        _transport = transport ?? new UdpServerQueryTransport();
        _timeout = timeout ?? MaximumTimeout;
        if (_timeout <= TimeSpan.Zero || _timeout > MaximumTimeout)
            throw new ArgumentOutOfRangeException(nameof(timeout), "A2S timeout must be greater than zero and at most five seconds.");
    }

    public async Task<ServerQueryResult> QueryAsync(ServerDefinition server, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        cancellationToken.ThrowIfCancellationRequested();
        if (server.QueryPort is null)
            return Result(ServerQueryState.Unconfigured, "Nie podano portu Steam Query. Nie wysłano zapytania.");
        if (server.QueryPort is < 1 or > 65535 || string.IsNullOrWhiteSpace(server.Host) ||
            server.Host.Any(char.IsWhiteSpace) || Uri.CheckHostName(server.Host) == UriHostNameType.Unknown)
            return Result(ServerQueryState.Unconfigured, "Nieprawidłowy host lub port Steam Query. Nie wysłano zapytania.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        var token = timeout.Token;
        var assembler = new SteamQueryPacketAssembler();
        var receivedBytes = 0;
        var challengeCount = 0;
        try
        {
            using var session = await _transport.CreateSessionAsync(server.Host, server.QueryPort.Value, token).WaitAsync(token);
            var sentAt = Stopwatch.GetTimestamp();
            await session.SendAsync(SteamQueryProtocol.CreateInfoRequest(), token).AsTask().WaitAsync(token);
            for (var count = 0; count < SteamQueryProtocol.MaximumDatagrams; count++)
            {
                var datagram = await session.ReceiveAsync(token).AsTask().WaitAsync(token);
                if (datagram.Length > SteamQueryProtocol.MaximumDataBytes - receivedBytes)
                    return Result(ServerQueryState.InvalidResponse, "Odpowiedzi A2S przekroczyły łączny limit 64 KiB.");
                receivedBytes += datagram.Length;
                var packet = assembler.AddDatagram(datagram.Span);
                if (packet is null) continue;
                if (SteamQueryProtocol.TryReadChallenge(packet, out var challenge))
                {
                    if (++challengeCount > 2)
                        return Result(ServerQueryState.InvalidResponse, "Serwer przekroczył limit dwóch odpowiedzi challenge A2S.");
                    assembler = new SteamQueryPacketAssembler();
                    sentAt = Stopwatch.GetTimestamp();
                    await session.SendAsync(SteamQueryProtocol.CreateInfoRequest(challenge), token).AsTask().WaitAsync(token);
                    continue;
                }
                var info = SteamQueryProtocol.ParseInfo(packet);
                return new(ServerQueryState.Online, DateTimeOffset.UtcNow, info.Name, info.Map,
                    info.Players, info.MaxPlayers, info.Bots, Stopwatch.GetElapsedTime(sentAt));
            }
            return Result(ServerQueryState.InvalidResponse, "Nie złożono odpowiedzi A2S w limicie 16 datagramów.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return assembler.HasPendingFragments
                ? Result(ServerQueryState.InvalidResponse, "Upłynął czas oczekiwania na brakujące części odpowiedzi A2S.")
                : Result(ServerQueryState.NoResponse, "Brak kompletnej odpowiedzi A2S w limicie czasu. Nie potwierdza to wyłączenia serwera.");
        }
        catch (InvalidDataException exception)
        {
            return Result(ServerQueryState.InvalidResponse, exception.Message);
        }
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            return Result(ServerQueryState.NoResponse, $"Nie otrzymano odpowiedzi A2S: {exception.Message}");
        }
    }

    private static ServerQueryResult Result(ServerQueryState state, string diagnostic) =>
        new(state, DateTimeOffset.UtcNow, Diagnostic: diagnostic);
}

internal sealed class UdpServerQueryTransport : IServerQueryTransport
{
    public async Task<IServerQuerySession> CreateSessionAsync(string host, int queryPort, CancellationToken cancellationToken)
    {
        IPAddress[] addresses = IPAddress.TryParse(host, out var address)
            ? [address]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);
        var selected = addresses.FirstOrDefault(value => value.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault(value => value.AddressFamily == AddressFamily.InterNetworkV6)
            ?? throw new SocketException((int)SocketError.HostNotFound);
        cancellationToken.ThrowIfCancellationRequested();
        var client = new UdpClient(selected.AddressFamily);
        try
        {
            client.Connect(selected, queryPort); // Connected UDP restricts incoming packets to this peer.
            return new UdpServerQuerySession(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}

internal sealed class UdpServerQuerySession(UdpClient client) : IServerQuerySession
{
    public async ValueTask SendAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken) =>
        _ = await client.SendAsync(datagram, cancellationToken);

    public async ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken cancellationToken)
    {
        var response = await client.ReceiveAsync(cancellationToken);
        return response.Buffer;
    }

    public void Dispose() => client.Dispose();
}

using System.Net.Sockets;

namespace KrainaSurvivalu.Launcher.Core;

public sealed class SteamServerModsQueryService : IServerModsQueryService
{
    private readonly IServerQueryTransport _transport;
    private readonly TimeSpan _timeout;
    public static readonly TimeSpan InfoToRulesDelay = TimeSpan.FromMilliseconds(300);

    public SteamServerModsQueryService(IServerQueryTransport? transport = null, TimeSpan? timeout = null)
    {
        _transport = transport ?? new UdpServerQueryTransport();
        _timeout = timeout ?? SteamServerQueryService.MaximumTimeout;
        if (_timeout <= TimeSpan.Zero || _timeout > SteamServerQueryService.MaximumTimeout)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Mods query timeout must be positive and at most five seconds.");
    }

    public async Task<ServerModsQueryResult> QueryAsync(ServerDefinition server, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        cancellationToken.ThrowIfCancellationRequested();
        if (server.QueryPort is null)
            return Result(ServerModsQueryState.Unconfigured, "Nie podano portu Steam Query. Lista modów nie została ustalona.");
        if (server.QueryPort is < 1 or > 65535 || string.IsNullOrWhiteSpace(server.Host) ||
            server.Host.Any(char.IsWhiteSpace) || Uri.CheckHostName(server.Host) == UriHostNameType.Unknown)
            return Result(ServerModsQueryState.Unconfigured, "Nieprawidłowy host lub port Steam Query.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        var token = timeout.Token;
        var receivedBytes = 0;
        var datagrams = 0;
        try
        {
            using var session = await _transport.CreateSessionAsync(server.Host, server.QueryPort.Value, token).WaitAsync(token);
            uint? cookie = null;
            await Send(SteamQueryProtocol.CreateInfoRequest());
            var infoChallenges = 0;
            while (true)
            {
                var packet = await ReceivePacket();
                if (!SteamQueryProtocol.TryReadChallenge(packet, out var challenge))
                {
                    _ = SteamQueryProtocol.ParseInfo(packet);
                    break;
                }
                if (++infoChallenges > 2)
                    throw new InvalidDataException("Przekroczono limit dwóch challenge dla INFO.");
                cookie = challenge;
                await Send(SteamQueryProtocol.CreateInfoRequest(challenge));
            }
            // Match the observed successful exchange while remaining inside the same five-second budget.
            await Task.Delay(InfoToRulesDelay, token);
            await Send(DayZServerModsProtocol.CreateRulesRequest(cookie ?? uint.MaxValue));
            var rulesChallenges = 0;
            while (true)
            {
                var packet = await ReceivePacket();
                if (SteamQueryProtocol.TryReadChallenge(packet, out var challenge))
                {
                    if (++rulesChallenges > 2)
                        throw new InvalidDataException("Przekroczono limit dwóch challenge dla RULES.");
                    await Send(DayZServerModsProtocol.CreateRulesRequest(challenge));
                    continue;
                }
                var metadata = DayZServerModsProtocol.ParseRulesPacket(packet);
                string? diagnostic = metadata.Flags != 0
                    ? $"Serwer zwrócił dodatkowe niezweryfikowane flagi (0x{metadata.Flags:X2}); kompletność listy nie została potwierdzona."
                    : metadata.Mods.Any(mod => !mod.HasWorkshopId)
                        ? "Co najmniej jeden mod ma ID 0: lokalny lub niezidentyfikowany mod. Lista nie wystarcza do synchronizacji Workshop."
                        : null;
                return new(ServerModsQueryState.Available, DateTimeOffset.UtcNow, metadata.Mods, diagnostic, metadata.Flags);
            }

            async Task Send(byte[] packet) => await session.SendAsync(packet, token).AsTask().WaitAsync(token);
            async Task<byte[]> ReceivePacket()
            {
                var assembler = new SteamQueryPacketAssembler();
                while (datagrams < SteamQueryProtocol.MaximumDatagrams)
                {
                    var data = await session.ReceiveAsync(token).AsTask().WaitAsync(token);
                    datagrams++;
                    if (data.Length > SteamQueryProtocol.MaximumDataBytes - receivedBytes)
                        throw new InvalidDataException("Cała sekwencja INFO/RULES przekroczyła 64 KiB.");
                    receivedBytes += data.Length;
                    if (assembler.AddDatagram(data.Span) is { } packet) return packet;
                }
                throw new InvalidDataException("Cała sekwencja INFO/RULES przekroczyła limit 16 datagramów.");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result(ServerModsQueryState.NoResponse, "Brak kompletnej odpowiedzi INFO/RULES w limicie czasu. Lista modów pozostaje nieustalona.");
        }
        catch (InvalidDataException exception)
        {
            return Result(ServerModsQueryState.InvalidResponse, exception.Message);
        }
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            return Result(ServerModsQueryState.NoResponse, "Nie otrzymano odpowiedzi RULES: " + exception.Message);
        }
    }

    private static ServerModsQueryResult Result(ServerModsQueryState state, string message) =>
        new(state, DateTimeOffset.UtcNow, Diagnostic: message);
}

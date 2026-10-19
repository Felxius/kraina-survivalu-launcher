namespace KrainaSurvivalu.Launcher.Core;

public enum ServerQueryState { Online, NoResponse, InvalidResponse, Unconfigured }

public sealed record ServerQueryResult(
    ServerQueryState State,
    DateTimeOffset CheckedAtUtc,
    string? Name = null,
    string? Map = null,
    int? Players = null,
    int? MaxPlayers = null,
    int? Bots = null,
    TimeSpan? RoundTrip = null,
    string? Diagnostic = null);

public interface IServerQueryService
{
    Task<ServerQueryResult> QueryAsync(ServerDefinition server, CancellationToken cancellationToken = default);
}

public sealed record SteamServerInfo(string Name, string Map, int Players, int MaxPlayers, int Bots);

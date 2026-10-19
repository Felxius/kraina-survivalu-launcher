using System.Globalization;

namespace KrainaSurvivalu.Launcher.Core;

public enum ServerModsQueryState { Available, NoResponse, InvalidResponse, Unconfigured }

public sealed record ServerWorkshopMod(string Name, string WorkshopId)
{
    public bool HasWorkshopId => !string.IsNullOrEmpty(WorkshopId) && WorkshopId.All(char.IsAsciiDigit) &&
        ulong.TryParse(WorkshopId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0;
}

public sealed record ServerModsQueryResult(
    ServerModsQueryState State,
    DateTimeOffset CheckedAtUtc,
    IReadOnlyList<ServerWorkshopMod>? Mods = null,
    string? Diagnostic = null,
    byte Flags = 0)
{
    public bool HasCompleteWorkshopMetadata => State == ServerModsQueryState.Available &&
        Mods is not null && Flags == 0 && Mods.All(mod => mod.HasWorkshopId);
}

public interface IServerModsQueryService
{
    Task<ServerModsQueryResult> QueryAsync(ServerDefinition server, CancellationToken cancellationToken = default);
}

public sealed record DayZModsMetadata(IReadOnlyList<ServerWorkshopMod> Mods, byte Flags);

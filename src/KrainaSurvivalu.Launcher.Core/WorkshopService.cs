namespace KrainaSurvivalu.Launcher.Core;

public sealed record FeatureAvailability(bool IsAvailable, string Message);

/// <summary>A static catalog alone cannot prove live server requirements or local Steam readiness.</summary>
public sealed class WorkshopService
{
    public FeatureAvailability GetAvailability(ServerDefinition server)
    {
        ArgumentNullException.ThrowIfNull(server);
        return server.RequiredWorkshopIds is null
            ? new(false, "Najpierw odczytaj aktualną listę modów serwera i sprawdź ich stan na tym komputerze.")
            : new(false, "Podana lista modów nie potwierdza instalacji ani aktualności. Sprawdź mody na tym komputerze.");
    }
}

namespace KrainaSurvivalu.Launcher.Core;

public sealed record LauncherSettings
{
    public string DayZDirectory { get; init; } = "";
    public string SteamExecutable { get; init; } = "";
    public string PlayerName { get; init; } = "";
}

public sealed record SettingsLoadResult(LauncherSettings Settings, string? Warning);

public sealed record ValidationResult(bool IsValid, string Message);

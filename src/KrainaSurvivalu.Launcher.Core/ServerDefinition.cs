namespace KrainaSurvivalu.Launcher.Core;

/// <summary>Only explicitly supplied server configuration; null means not yet established.</summary>
public sealed record ServerDefinition(
    string Id,
    string Name,
    string Host,
    int GamePort,
    int? QueryPort = null,
    IReadOnlyList<string>? RequiredWorkshopIds = null,
    string? DiscordUrl = null,
    string? RulesUrl = null,
    string? NewsUrl = null);

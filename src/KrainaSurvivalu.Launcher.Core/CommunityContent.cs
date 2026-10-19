namespace KrainaSurvivalu.Launcher.Core;

public sealed record CommunityContent(
    string? DiscordUrl,
    string? RulesUrl,
    string? NewsUrl,
    RulesDocument? Rules,
    IReadOnlyList<NewsArticle> News);

public sealed record RulesDocument(string Title, IReadOnlyList<RulesSection> Sections);

public sealed record RulesSection(string Id, string Title, string Body);

public sealed record NewsArticle(
    string Id,
    string Title,
    string Summary,
    string Body,
    DateTimeOffset PublishedAt,
    string? SourceUrl = null);

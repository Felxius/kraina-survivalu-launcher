using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace KrainaSurvivalu.Launcher.Core;

/// <summary>Loads local plain text content. No content is executed and no network request is made.</summary>
public static class CommunityCatalog
{
    private const long MaximumCatalogBytes = 1_048_576;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static async Task<CommunityContent> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaximumCatalogBytes)
            throw new InvalidDataException("Plik społeczności przekracza dozwolony rozmiar 1 MB.");

        CommunityDocument? document;
        try
        {
            using var json = await JsonDocument.ParseAsync(stream,
                new JsonDocumentOptions { MaxDepth = 32 }, cancellationToken);
            RejectDuplicateProperties(json.RootElement);
            document = json.RootElement.Deserialize<CommunityDocument>(JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Plik społeczności zawiera nieprawidłowy JSON, datę lub nieznane pola.", exception);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (document?.SchemaVersion != 1)
            throw new InvalidDataException("Plik społeczności musi mieć schemaVersion równy 1.");

        ValidateHttpsLink(document.DiscordUrl, "Discord");
        ValidateHttpsLink(document.RulesUrl, "regulamin");
        ValidateHttpsLink(document.NewsUrl, "aktualności");
        ValidateDiscordLink(document.DiscordUrl);

        RulesDocument? rules = null;
        if (document.Rules is { } rulesEntry)
        {
            ValidateText(rulesEntry.Title, "Tytuł regulaminu", 200, allowLineBreaks: false);
            if (rulesEntry.Sections is null || rulesEntry.Sections.Count is 0 or > 64)
                throw new InvalidDataException("Regulamin musi zawierać od 1 do 64 sekcji.");
            var sections = new List<RulesSection>(rulesEntry.Sections.Count);
            var sectionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var section in rulesEntry.Sections)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (section is null)
                    throw new InvalidDataException("Regulamin zawiera pustą sekcję.");
                ValidateId(section.Id, sectionIds, "sekcji regulaminu");
                ValidateText(section.Title, "Tytuł sekcji regulaminu", 200, allowLineBreaks: false);
                ValidateText(section.Body, "Treść sekcji regulaminu", 32_000, allowLineBreaks: true);
                sections.Add(new(section.Id!, section.Title!, section.Body!));
            }
            rules = new(rulesEntry.Title!, sections.AsReadOnly());
        }

        if (document.News is null || document.News.Count > 100)
            throw new InvalidDataException("Lista aktualności musi być tablicą zawierającą nie więcej niż 100 wpisów.");
        var articles = new List<NewsArticle>(document.News.Count);
        var articleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var article in document.News)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (article is null)
                throw new InvalidDataException("Aktualności zawierają pusty wpis.");
            ValidateId(article.Id, articleIds, "aktualności");
            ValidateText(article.Title, "Tytuł aktualności", 200, allowLineBreaks: false);
            ValidateText(article.Summary, "Skrót aktualności", 1_000, allowLineBreaks: true);
            ValidateText(article.Body, "Treść aktualności", 32_000, allowLineBreaks: true);
            if (article.PublishedAt is null)
                throw new InvalidDataException("Aktualność musi mieć datę publishedAt w formacie ISO 8601 z jawną strefą czasową.");
            ValidateHttpsLink(article.SourceUrl, "źródło aktualności");
            articles.Add(new(article.Id!, article.Title!, article.Summary!, article.Body!,
                article.PublishedAt.Value, article.SourceUrl));
        }

        // Enumerable.OrderByDescending is stable: articles sharing a timestamp retain source order.
        var sortedArticles = Array.AsReadOnly(articles.OrderByDescending(article => article.PublishedAt).ToArray());
        return new(document.DiscordUrl, document.RulesUrl, document.NewsUrl, rules, sortedArticles);
    }

    public static IReadOnlyList<RulesSection> FindRules(RulesDocument? rules, string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (rules is null)
            return Array.Empty<RulesSection>();
        if (string.IsNullOrWhiteSpace(query))
            return Array.AsReadOnly(rules.Sections.ToArray());
        return Array.AsReadOnly(rules.Sections.Where(section =>
            section.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            section.Body.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray());
    }

    private static void ValidateId(string? id, ISet<string> seen, string context)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 80 ||
            id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new InvalidDataException($"Identyfikator {context} może zawierać tylko litery ASCII, cyfry, myślniki i podkreślenia (1–80 znaków).");
        if (!seen.Add(id))
            throw new InvalidDataException($"Identyfikator {context} „{id}” występuje więcej niż raz.");
    }

    private static void ValidateText(string? value, string field, int maximumLength, bool allowLineBreaks)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength ||
            value.Any(character => char.IsControl(character) &&
                !(allowLineBreaks && character is '\r' or '\n' or '\t')))
            throw new InvalidDataException($"{field} wymaga niepustego tekstu (maksymalnie {maximumLength} znaków).");
    }

    private static void ValidateHttpsLink(string? value, string context)
    {
        if (value is null)
            return;
        if (value.Length > 2_048 || value.Any(char.IsControl) || value.Contains('\\') || value != value.Trim() ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) || uri.Port is < 1 or > 65535)
            throw new InvalidDataException($"Link „{context}” musi być pełnym adresem HTTPS bez danych logowania.");
    }

    private static void ValidateDiscordLink(string? value)
    {
        if (value is null)
            return;
        var uri = new Uri(value, UriKind.Absolute);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.None);
        var isShortInvite = uri.IdnHost.Equals("discord.gg", StringComparison.OrdinalIgnoreCase) &&
            segments.Length == 2 && IsInviteCode(segments[1]);
        var isFullInvite = uri.IdnHost.Equals("discord.com", StringComparison.OrdinalIgnoreCase) &&
            segments.Length == 3 && segments[1] == "invite" && IsInviteCode(segments[2]);
        if (value.Contains('%') || !uri.IsDefaultPort || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !(isShortInvite || isFullInvite))
            throw new InvalidDataException("Discord wymaga zaproszenia https://discord.gg/<kod> lub https://discord.com/invite/<kod>.");
    }

    private static bool IsInviteCode(string code) => code.Length is >= 1 and <= 100 &&
        code.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"Plik społeczności zawiera powtórzone pole „{property.Name}”.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                RejectDuplicateProperties(item);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new OffsetDateTimeConverter());
        return options;
    }

    private sealed class OffsetDateTimeConverter : JsonConverter<DateTimeOffset>
    {
        private static readonly Regex DatePattern = new(
            @"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|[+-][0-9]{2}:[0-9]{2})$",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String || reader.GetString() is not { } value ||
                !DatePattern.IsMatch(value) || !reader.TryGetDateTimeOffset(out var date))
                throw new JsonException("Data musi być datą ISO 8601 z jawnym offsetem lub Z.");
            return date;
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }

    private sealed class CommunityDocument
    {
        public int SchemaVersion { get; init; }
        public string? DiscordUrl { get; init; }
        public string? RulesUrl { get; init; }
        public string? NewsUrl { get; init; }
        public RulesEntry? Rules { get; init; }
        public List<ArticleEntry?>? News { get; init; } = [];
    }

    private sealed class RulesEntry
    {
        public string? Title { get; init; }
        public List<SectionEntry?>? Sections { get; init; }
    }

    private sealed class SectionEntry
    {
        public string? Id { get; init; }
        public string? Title { get; init; }
        public string? Body { get; init; }
    }

    private sealed class ArticleEntry
    {
        public string? Id { get; init; }
        public string? Title { get; init; }
        public string? Summary { get; init; }
        public string? Body { get; init; }
        public DateTimeOffset? PublishedAt { get; init; }
        public string? SourceUrl { get; init; }
    }
}

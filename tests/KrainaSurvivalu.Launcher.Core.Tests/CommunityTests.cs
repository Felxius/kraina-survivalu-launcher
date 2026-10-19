using System.Globalization;
using System.Text.Json.Nodes;
using KrainaSurvivalu.Launcher.Core;

internal static class CommunityTests
{
    public static async Task RunAsync(string testDirectory, Func<string, Func<Task>, Task> test)
    {
        var path = Path.Combine(testDirectory, "community.json");
        await test("Społeczność: brak opcjonalnej treści daje null i puste aktualności", async () =>
        {
            await Write(new JsonObject { ["schemaVersion"] = 1 });
            var content = await CommunityCatalog.LoadAsync(path);
            Check(content.DiscordUrl is null && content.RulesUrl is null && content.NewsUrl is null &&
                content.Rules is null && content.News.Count == 0, "Wymyślono brakującą treść lub linki.");
        });
        await test("Społeczność: dziewięć sekcji zachowuje kolejność, polskie znaki i podział na wiersze", async () =>
        {
            var sections = new JsonArray();
            const string body = "Zażółć gęślą jaźń.\r\n\tDrugi wiersz.\nTrzeci wiersz.  ";
            for (var index = 1; index <= 9; index++)
                sections.Add(new JsonObject { ["id"] = $"section-{index}", ["title"] = $"Rozdział {index}", ["body"] = body });
            var document = Document();
            document["rules"]!["sections"] = sections;
            await Write(document);
            var content = await CommunityCatalog.LoadAsync(path);
            Check(content.DiscordUrl == "https://discord.gg/qEwYQqj7MD" && content.Rules?.Sections.Count == 9,
                "Nie zachowano zaproszenia lub liczby rozdziałów.");
            Check(content.Rules!.Sections.Select(section => section.Id).SequenceEqual(Enumerable.Range(1, 9).Select(index => $"section-{index}")),
                "Zmieniono kolejność rozdziałów.");
            Check(content.Rules.Sections.All(section => section.Body == body), "Zmieniono tekst regulaminu.");
        });
        await test("Społeczność: aktualności są sortowane malejąco ze stabilnymi remisami", async () =>
        {
            var document = Document();
            document["news"] = new JsonArray(
                Article("old", "2024-01-01T00:00:00Z"),
                Article("equal-first", "2026-10-01T12:00:00+02:00"),
                Article("newest", "2026-10-02T00:00:00Z"),
                Article("equal-second", "2026-10-01T10:00:00Z"));
            await Write(document);
            var news = (await CommunityCatalog.LoadAsync(path)).News;
            Check(news.Select(article => article.Id).SequenceEqual(new[] { "newest", "equal-first", "equal-second", "old" }),
                "Nieprawidłowa lub niestabilna kolejność aktualności.");
            Check(news[1].PublishedAt.Offset == TimeSpan.FromHours(2), "Zgubiono jawny offset daty.");
        });
        await test("Społeczność: prawidłowe daty nie zależą od bieżącego zegara klienta", async () =>
        {
            var document = Document();
            document["news"] = new JsonArray(Article("future", "9950-01-01T00:00:00.1234567+01:00"));
            await Write(document);
            var news = (await CommunityCatalog.LoadAsync(path)).News;
            Check(news.Count == 1 && news[0].PublishedAt.Year == 9950, "Dodano arbitralną politykę publikacji zależną od zegara.");
        });
        await test("Społeczność: data wymaga ważnego ISO 8601 i jawnego offsetu", async () =>
        {
            foreach (var date in new[] { "2026-01-01T00:00:00", "2026-02-30T00:00:00Z", "2026-01-01", "2026-01-01T00:00:00+25:00", "10000-01-01T00:00:00Z", "2026-01-01T00:00:00Z trailing" })
            {
                var document = Document();
                document["news"] = new JsonArray(Article("invalid", date));
                await Write(document);
                await Reject();
            }
            foreach (var state in new[] { "missing", "null", "number" })
            {
                var article = Article("invalid");
                if (state == "missing") article.Remove("publishedAt");
                else article["publishedAt"] = state == "null" ? null : JsonValue.Create(123);
                var document = Document();
                document["news"] = new JsonArray(article);
                await Write(document);
                await Reject();
            }
        });
        await test("Społeczność: Discord przyjmuje wyłącznie bezpośrednie zaproszenia", async () =>
        {
            foreach (var invite in new[] { "https://discord.gg/qEwYQqj7MD", "https://discord.com/invite/Example-123", "https://discord.gg:443/Example" })
            {
                var document = Document();
                document["discordUrl"] = invite;
                await Write(document);
                Check((await CommunityCatalog.LoadAsync(path)).DiscordUrl == invite, "Odrzucono prawidłowe zaproszenie.");
            }
            foreach (var invite in new[] { "https://example.org/invite/abc", "https://discord.gg.evil.example/abc", "https://discord.com/channels/abc", "https://discord.gg/", "https://discord.gg/a/b", "https://discord.gg/abc?redirect=evil", "https://discord.gg/abc#fragment", "https://discord.gg:444/abc", "https://discord.gg/%61bc" })
            {
                var document = Document();
                document["discordUrl"] = invite;
                await Write(document);
                await Reject();
            }
        });
        await test("Społeczność: wszystkie linki odrzucają schematy nie-HTTPS i dane logowania", async () =>
        {
            foreach (var field in new[] { "discordUrl", "rulesUrl", "newsUrl", "sourceUrl" })
            foreach (var url in new[] { "http://example.org", "javascript:alert(1)", "file:///C:/data", "https://user:secret@example.org", " https://example.org", "https://example.org\n", "https://example.org\\other", "" })
            {
                var document = Document();
                if (field == "sourceUrl")
                {
                    var article = Article("unsafe");
                    article[field] = url;
                    document["news"] = new JsonArray(article);
                }
                else document[field] = url;
                await Write(document);
                await Reject();
            }
        });
        await test("Społeczność: opcjonalne linki HTTPS są zachowane bez dopisywania lokalnych treści", async () =>
        {
            var document = Document();
            document["rules"] = null;
            document["rulesUrl"] = "https://example.org/regulamin?lang=pl#zasady";
            document["newsUrl"] = "https://example.org/aktualnosci";
            var article = Article("linked");
            article["sourceUrl"] = "https://example.org/wpis/1";
            document["news"] = new JsonArray(article);
            await Write(document);
            var content = await CommunityCatalog.LoadAsync(path);
            Check(content.Rules is null && content.RulesUrl == "https://example.org/regulamin?lang=pl#zasady" &&
                content.NewsUrl == "https://example.org/aktualnosci" && content.News[0].SourceUrl == "https://example.org/wpis/1",
                "Zmieniono poprawne linki lub utworzono nieistniejący regulamin.");
        });
        await test("Społeczność: odrębne identyfikatory sekcji i aktualności muszą być unikalne", async () =>
        {
            var rulesDocument = Document();
            rulesDocument["rules"]!["sections"] = new JsonArray(Section("same"), Section("SAME"));
            await Write(rulesDocument);
            await Reject();
            var newsDocument = Document();
            newsDocument["news"] = new JsonArray(Article("same"), Article("SAME"));
            await Write(newsDocument);
            await Reject();
        });
        await test("Społeczność: niepuste tytuły, skróty i treści są wymagane", async () =>
        {
            foreach (var field in new[] { "title", "body" })
            {
                var document = Document();
                document["rules"]!["sections"]![0]![field] = "  \t";
                await Write(document);
                await Reject();
            }
            foreach (var field in new[] { "title", "summary", "body" })
            {
                var document = Document();
                var article = Article("empty");
                article[field] = "\r\n ";
                document["news"] = new JsonArray(article);
                await Write(document);
                await Reject();
            }
            var emptyRulesTitle = Document();
            emptyRulesTitle["rules"]!["title"] = "";
            await Write(emptyRulesTitle);
            await Reject();
        });
        await test("Społeczność: błędne listy i puste wpisy są odrzucane", async () =>
        {
            foreach (var value in new JsonNode?[] { null, new JsonArray(), new JsonArray((JsonNode?)null) })
            {
                var document = Document();
                document["rules"]!["sections"] = value;
                await Write(document);
                await Reject();
            }
            foreach (var value in new JsonNode?[] { null, new JsonArray((JsonNode?)null) })
            {
                var document = Document();
                document["news"] = value;
                await Write(document);
                await Reject();
            }
        });
        await test("Społeczność: limity długości i liczby wpisów są egzekwowane", async () =>
        {
            foreach (var field in new[] { "title", "body" })
            {
                var document = Document();
                document["rules"]!["sections"]![0]![field] = new string('x', field == "title" ? 201 : 32_001);
                await Write(document);
                await Reject();
            }
            var longSummary = Document();
            var article = Article("long");
            article["summary"] = new string('x', 1_001);
            longSummary["news"] = new JsonArray(article);
            await Write(longSummary);
            await Reject();
            var tooManySections = Document();
            tooManySections["rules"]!["sections"] = new JsonArray(Enumerable.Range(0, 65).Select(index => (JsonNode)Section($"section-{index}")).ToArray());
            await Write(tooManySections);
            await Reject();
            var tooManyArticles = Document();
            tooManyArticles["news"] = new JsonArray(Enumerable.Range(0, 101).Select(index => (JsonNode)Article($"news-{index}")).ToArray());
            await Write(tooManyArticles);
            await Reject();
        });
        await test("Społeczność: plik większy niż 1 MiB jest odrzucany przed parsowaniem", async () =>
        {
            await File.WriteAllBytesAsync(path, new byte[1_048_577]);
            await Reject();
        });
        await test("Społeczność: schemat, nieznane pola i powtórzone pola JSON są sprawdzane", async () =>
        {
            foreach (var json in new[] { "{bad", "null", "[]", "{}", "{\"schemaVersion\":2}", "{\"schemaVersion\":1,\"unknown\":true}", "{\"schemaVersion\":1,\"schemaVersion\":1}", "{\"schemaVersion\":1,\"news\":[{\"id\":\"first\",\"id\":\"second\"}]}" })
            {
                await File.WriteAllTextAsync(path, json);
                await Reject();
            }
            var nestedUnknown = Document();
            nestedUnknown["rules"]!["sections"]![0]!["htmlRenderer"] = true;
            await Write(nestedUnknown);
            await Reject();
        });
        await test("Społeczność: anulowanie i brak pliku nie są maskowane fikcyjną treścią", async () =>
        {
            await Write(Document());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Throws<OperationCanceledException>(async () => { _ = await CommunityCatalog.LoadAsync(path, cancellation.Token); });
            await Throws<FileNotFoundException>(async () => { _ = await CommunityCatalog.LoadAsync(path + ".missing"); });
        });
        await test("Społeczność: zwracane kolekcje są niemodyfikowalne", async () =>
        {
            var document = Document();
            document["news"] = new JsonArray(Article("news"));
            await Write(document);
            var content = await CommunityCatalog.LoadAsync(path);
            ThrowsSync<NotSupportedException>(() => ((IList<RulesSection>)content.Rules!.Sections).Add(new("id", "Title", "Body")));
            ThrowsSync<NotSupportedException>(() => ((IList<NewsArticle>)content.News).Clear());
        });
        await test("Społeczność: znaczniki pozostają zwykłym tekstem bez interpretacji HTML", async () =>
        {
            const string literal = "Zażółć\n<script>alert('tekst')</script>\r\n<b>Reguła</b> & <5 >3\t  ";
            var document = Document();
            document["rules"]!["sections"]![0]!["body"] = literal;
            var article = Article("plain-text");
            article["body"] = literal;
            document["news"] = new JsonArray(article);
            await Write(document);
            var content = await CommunityCatalog.LoadAsync(path);
            Check(content.Rules!.Sections[0].Body == literal && content.News[0].Body == literal,
                "Treść została przeformatowana, przycięta albo zinterpretowana jako HTML.");
        });
        await test("Społeczność: wyszukiwanie obejmuje tytuł i treść bez zmiany oryginału", async () =>
        {
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
                var document = Document();
                document["rules"]!["sections"] = new JsonArray(
                    new JsonObject { ["id"] = "first", ["title"] = "Żółć w tytule", ["body"] = "Pierwszy rozdział." },
                    new JsonObject { ["id"] = "second", ["title"] = "Inny tytuł", ["body"] = "Zażółć w treści." });
                await Write(document);
                var rules = (await CommunityCatalog.LoadAsync(path)).Rules;
                Check(CommunityCatalog.FindRules(rules, "ŻÓŁĆ").Count == 2, "Wyszukiwanie nie obejmuje polskiej treści lub tytułu.");
                Check(CommunityCatalog.FindRules(rules, "   ").Count == 2 && CommunityCatalog.FindRules(rules, "brak").Count == 0 &&
                    CommunityCatalog.FindRules(null, "szukaj").Count == 0, "Nieprawidłowe wyniki pustego wyszukiwania.");
                Check(rules!.Sections[0].Title == "Żółć w tytule", "Wyszukiwanie zmieniło źródłową treść.");
            }
            finally { CultureInfo.CurrentCulture = previousCulture; }
        });
        await test("Społeczność: identyfikatory i niedozwolone znaki kontrolne są walidowane", async () =>
        {
            foreach (var id in new[] { "", "has space", "polskie-ż", new string('a', 81) })
            {
                var document = Document();
                document["rules"]!["sections"]![0]!["id"] = id;
                await Write(document);
                await Reject();
            }
            var controls = Document();
            controls["rules"]!["sections"]![0]!["body"] = "Treść\0dalej";
            await Write(controls);
            await Reject();
        });

        Task Write(JsonNode document) => File.WriteAllTextAsync(path, document.ToJsonString());
        Task Reject() => Throws<InvalidDataException>(async () => { _ = await CommunityCatalog.LoadAsync(path); });
    }

    private static JsonObject Document() => new()
    {
        ["schemaVersion"] = 1,
        ["discordUrl"] = "https://discord.gg/qEwYQqj7MD",
        ["rulesUrl"] = null,
        ["newsUrl"] = null,
        ["rules"] = new JsonObject { ["title"] = "Regulamin testowy", ["sections"] = new JsonArray(Section("section-1")) },
        ["news"] = new JsonArray()
    };

    private static JsonObject Section(string id) => new()
    {
        ["id"] = id,
        ["title"] = "Rozdział testowy",
        ["body"] = "Treść testowego rozdziału."
    };

    private static JsonObject Article(string id, string date = "2026-01-01T00:00:00Z") => new()
    {
        ["id"] = id,
        ["title"] = "Wpis testowy",
        ["summary"] = "Skrót testowy.",
        ["body"] = "Pełna treść testowa.",
        ["publishedAt"] = date,
        ["sourceUrl"] = null
    };

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Oczekiwano wyjątku {typeof(T).Name}.");
    }

    private static void ThrowsSync<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Oczekiwano wyjątku {typeof(T).Name}.");
    }
}

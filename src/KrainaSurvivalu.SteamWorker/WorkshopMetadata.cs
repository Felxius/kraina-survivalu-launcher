using System.Globalization;
using System.Text.Json;

namespace KrainaSurvivalu.SteamWorker;

internal static class WorkshopMetadata
{
    // Official public, read-only endpoint. No Web API key, password or Steam login is requested.
    // https://partner.steamgames.com/doc/webapi/ISteamRemoteStorage#GetPublishedFileDetails
    private static readonly Uri Endpoint = new("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/");

    internal static async Task ValidateDayZItemsAsync(IReadOnlyList<ulong> ids)
    {
        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = 8 * 1024 * 1024
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KrainaSurvivaluLauncher/0.6");
        // Validate the whole requested set before the first subscription. Small batches keep
        // response sizes and public Web API requests bounded even for long server mod lists.
        foreach (var batch in ids.Chunk(100))
            await ValidateBatchAsync(client, batch).ConfigureAwait(false);
    }

    private static async Task ValidateBatchAsync(HttpClient client, IReadOnlyList<ulong> ids)
    {
        var fields = new List<KeyValuePair<string, string>> { new("itemcount", ids.Count.ToString(CultureInfo.InvariantCulture)) };
        for (var index = 0; index < ids.Count; index++)
            fields.Add(new($"publishedfileids[{index}]", ids[index].ToString(CultureInfo.InvariantCulture)));
        using var body = new FormUrlEncodedContent(fields);
        using var response = await client.PostAsync(Endpoint, body).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        if (bytes.Length > 8 * 1024 * 1024) throw new InvalidDataException("Odpowiedź Workshop przekroczyła limit danych.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        var details = document.RootElement.GetProperty("response").GetProperty("publishedfiledetails");
        if (details.ValueKind != JsonValueKind.Array || details.GetArrayLength() != ids.Count)
            throw new InvalidDataException("Steam nie zwrócił kompletnej informacji o wymaganych modach.");
        var expected = ids.ToHashSet();
        foreach (var item in details.EnumerateArray())
        {
            if (!item.TryGetProperty("publishedfileid", out var idProperty) ||
                !ulong.TryParse(idProperty.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
                !expected.Remove(id) || !item.TryGetProperty("result", out var result) || result.GetInt32() != 1 ||
                !item.TryGetProperty("consumer_app_id", out var appId) || appId.GetUInt32() != Program.DayZAppId)
                throw new InvalidDataException("Nie można potwierdzić, że wszystkie wymagane pozycje są dostępnymi modami DayZ. Pobieranie zatrzymano.");
        }
        if (expected.Count != 0) throw new InvalidDataException("Lista informacji Workshop jest niekompletna.");
    }
}

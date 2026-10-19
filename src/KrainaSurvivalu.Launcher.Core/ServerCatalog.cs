using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KrainaSurvivalu.Launcher.Core;

public static class ServerCatalog
{
    private const long MaximumCatalogBytes = 1_048_576;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static async Task<IReadOnlyList<ServerDefinition>> LoadAsync(
        string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaximumCatalogBytes)
            throw new InvalidDataException("Katalog serwerów przekracza dozwolony rozmiar 1 MB.");

        CatalogDocument? document;
        try
        {
            document = await JsonSerializer.DeserializeAsync<CatalogDocument>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Katalog serwerów zawiera nieprawidłowy JSON lub nieznane pola.", exception);
        }

        if (document?.SchemaVersion != 1)
            throw new InvalidDataException("Katalog serwerów musi mieć schemaVersion równy 1.");
        if (document.Servers is null || document.Servers.Count is 0 or > 100)
            throw new InvalidDataException("Katalog musi zawierać od 1 do 100 serwerów.");

        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ServerDefinition>(document.Servers.Count);
        foreach (var entry in document.Servers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry is null)
                throw new InvalidDataException("Katalog zawiera pusty wpis serwera.");
            Validate(entry);
            if (!identifiers.Add(entry.Id))
                throw new InvalidDataException($"Identyfikator serwera „{entry.Id}” występuje więcej niż raz.");

            // Freeze the deserialized collections, including the distinction between null and an empty list.
            result.Add(entry with
            {
                RequiredWorkshopIds = entry.RequiredWorkshopIds is null
                    ? null
                    : Array.AsReadOnly(entry.RequiredWorkshopIds.ToArray())
            });
        }

        return result.AsReadOnly();
    }

    private static void Validate(ServerDefinition server)
    {
        if (string.IsNullOrEmpty(server.Id) || server.Id.Length > 64 ||
            server.Id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new InvalidDataException("Id serwera może zawierać tylko litery ASCII, cyfry, myślniki i podkreślenia (1–64 znaki).");
        if (string.IsNullOrWhiteSpace(server.Name) || server.Name.Length > 120 || server.Name.Any(char.IsControl))
            throw new InvalidDataException($"Serwer „{server.Id}” ma nieprawidłową nazwę.");
        if (!IsValidHost(server.Host))
            throw new InvalidDataException($"Serwer „{server.Id}” ma nieprawidłowy adres hosta. Podaj wyłącznie IP albo nazwę DNS.");
        if (server.GamePort is < 1 or > 65535)
            throw new InvalidDataException($"Serwer „{server.Id}” ma nieprawidłowy port gry.");
        if (server.QueryPort is < 1 or > 65535)
            throw new InvalidDataException($"Serwer „{server.Id}” ma nieprawidłowy port Steam Query.");
        if (server.RequiredWorkshopIds is not null)
        {
            if (server.RequiredWorkshopIds.Count > 500)
                throw new InvalidDataException($"Lista modów serwera „{server.Id}” jest zbyt długa.");
            var seen = new HashSet<ulong>();
            foreach (var workshopId in server.RequiredWorkshopIds)
            {
                if (string.IsNullOrEmpty(workshopId) || !workshopId.All(char.IsAsciiDigit) ||
                    !ulong.TryParse(workshopId, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var numericId) || numericId == 0)
                    throw new InvalidDataException($"Serwer „{server.Id}” zawiera nieprawidłowy identyfikator Steam Workshop.");
                if (!seen.Add(numericId))
                    throw new InvalidDataException($"Lista modów serwera „{server.Id}” zawiera powtórzony identyfikator.");
            }
        }

        ValidateLink(server.DiscordUrl, server.Id, "Discord");
        ValidateLink(server.RulesUrl, server.Id, "regulamin");
        ValidateLink(server.NewsUrl, server.Id, "aktualności");
    }

    private static bool IsValidHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253 ||
            host.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not ':'))
            return false;

        if (IPAddress.TryParse(host, out _))
            return true;
        if (host.Contains(':'))
            return false;
        return host.Split('.').All(label => label.Length is >= 1 and <= 63 &&
            char.IsAsciiLetterOrDigit(label[0]) && char.IsAsciiLetterOrDigit(label[^1]) &&
            label.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'));
    }

    private static void ValidateLink(string? value, string serverId, string name)
    {
        if (value is null)
            return;
        if (value.Length > 2048 || value.Any(char.IsControl) || value != value.Trim() ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException($"Serwer „{serverId}”: link „{name}” musi być pełnym adresem HTTPS bez danych logowania.");
    }

    private sealed class CatalogDocument
    {
        public int SchemaVersion { get; init; }
        public List<ServerDefinition?>? Servers { get; init; }
    }
}

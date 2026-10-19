using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace KrainaSurvivalu.SteamWorker;

internal static class Program
{
    internal const uint DayZAppId = 221100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            var request = WorkerRequest.Parse(args);
            // Only the isolated worker gets this game context. Never change launcher/Steam settings
            // or ship steam_appid.txt. Facepunch's SteamClient.Init uses these process-local hints:
            // https://github.com/Facepunch/Facepunch.Steamworks/blob/master/Facepunch.Steamworks/SteamClient.cs
            Environment.SetEnvironmentVariable("SteamAppId", DayZAppId.ToString(CultureInfo.InvariantCulture));
            Environment.SetEnvironmentVariable("SteamGameId", DayZAppId.ToString(CultureInfo.InvariantCulture));
            if (request.Synchronize)
                WorkshopMetadata.ValidateDayZItemsAsync(request.Ids).GetAwaiter().GetResult();
            using var steam = new NativeSteam(request.DayZDirectory, request.SteamExecutable);
            foreach (var id in request.Ids)
            {
                if (request.Synchronize)
                    steam.Synchronize(id, Emit);
                else
                    Emit(steam.Inspect(id));
            }
            Emit(new WorkerEvent { Type = "complete", Success = true, Message = request.Synchronize
                ? "Steam potwierdził gotowość wymaganych modów." : "Sprawdzono stan modów w Steam." });
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // JSON is the only application protocol on stdout. No credentials or account ID are read.
            Emit(new WorkerEvent { Type = "error", Success = false, Message = Limit(exception.Message, 1400) });
            return 1;
        }
    }

    internal static void Emit(WorkerEvent value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
        Console.Out.Flush();
    }

    private static string Limit(string text, int length) => text.Length <= length ? text : text[..length];
}

internal sealed record WorkerRequest(bool Synchronize, string DayZDirectory, string SteamExecutable, ulong[] Ids)
{
    internal static WorkerRequest Parse(string[] args)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new InvalidOperationException("Integracja Steam wymaga systemu Windows x64.");
        if (args.Length != 7 || args[0] is not ("--inspect" or "--sync"))
            throw new ArgumentException("Nieprawidłowe argumenty pomocnika Steam.");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
            if (args[index] is not ("--dayz" or "--steam" or "--ids") || !options.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException("Nieprawidłowe lub powtórzone argumenty pomocnika Steam.");
        var dayz = AbsoluteLocalPath(options["--dayz"]);
        var steam = AbsoluteLocalPath(options["--steam"]);
        if (!File.Exists(Path.Combine(dayz, "DayZ_x64.exe")) || !File.Exists(Path.Combine(dayz, "DayZ_BE.exe")))
            throw new InvalidOperationException("Wskaż poprawny folder instalacji DayZ.");
        if (!File.Exists(steam) || !string.Equals(Path.GetFileName(steam), "steam.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Wskaż poprawny plik steam.exe.");
        var tokens = options["--ids"].Split(',');
        if (tokens.Length is < 1 or > 500)
            throw new ArgumentException("Lista musi zawierać od 1 do 500 identyfikatorów Workshop.");
        var ids = new List<ulong>(tokens.Length);
        var unique = new HashSet<ulong>();
        foreach (var token in tokens)
        {
            if (!ulong.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0)
                throw new ArgumentException("Nieprawidłowy identyfikator Steam Workshop.");
            if (unique.Add(id)) ids.Add(id);
        }
        return new(args[0] == "--sync", dayz, steam, ids.ToArray());
    }

    private static string AbsoluteLocalPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Any(char.IsControl) ||
            !Path.IsPathFullyQualified(value) || value.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Integracja Steam wymaga pełnej ścieżki do lokalnej instalacji.");
        return Path.GetFullPath(value);
    }
}

internal sealed record WorkerEvent
{
    public int SchemaVersion { get; init; } = 1;
    public required string Type { get; init; }
    public ulong? Id { get; init; }
    public uint? State { get; init; }
    public string? InstalledPath { get; init; }
    public ulong? BytesDownloaded { get; init; }
    public ulong? BytesTotal { get; init; }
    public bool? Success { get; init; }
    public string? Message { get; init; }
}

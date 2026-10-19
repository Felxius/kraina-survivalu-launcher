using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KrainaSurvivalu.Launcher.Core;

internal static class ServerModsQueryTests
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> test)
    {
        using var snapshots = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ServerModsSnapshots.json")));
        var info = Snapshot("deerInfo");
        var infoChallenge = Snapshot("deerChallenge");
        var deerRules = Snapshot("deerRules");
        var badlandsRules = Snapshot("badlandsRules");
        await test("Mody: format v2 zachowuje Unicode, uint64, hashe DLC i kolejność modów", () =>
        {
            var metadata = DayZServerModsProtocol.ParseRulesPacket(Rules(Payload([("Zażółć", ulong.MaxValue)], idLength: 8, dlcMask: 0x8001, description: "Opis\nserwera")));
            Check(metadata.Mods.Count == 1 && metadata.Mods[0].Name == "Zażółć" && metadata.Mods[0].WorkshopId == ulong.MaxValue.ToString(), "Zmieniono nazwę lub pełną precyzję ID.");
            Check(Result(metadata).HasCompleteWorkshopMetadata, "Poprawna jawna lista z dodatnim ID nie jest kompletna.");
            Throws<NotSupportedException>(() => ((IList<ServerWorkshopMod>)metadata.Mods).Clear());
            return Task.CompletedTask;
        });
        await test("Mody: sekwencje escape są dekodowane dopiero po połączeniu stron", () =>
        {
            var payload = Payload([("Mod testowy", 0xFF000101)]);
            var encoded = Escape(payload);
            var splitAt = Array.IndexOf(encoded, (byte)1) + 1;
            var packet = RawRules(([2, 2], encoded[splitAt..]), ([1, 2], encoded[..splitAt]), ([1, 2], encoded[..splitAt]));
            var metadata = DayZServerModsProtocol.ParseRulesPacket(packet);
            Check(metadata.Mods[0].WorkshopId == "4278190337", "Escape na granicy strony został uszkodzony.");
            return Task.CompletedTask;
        });
        await test("Mody: brakujące, sprzeczne i niespójne strony nie tworzą pustej listy", () =>
        {
            var encoded = Escape(Payload([("Mod", 123)]));
            Throws<InvalidDataException>(() => DayZServerModsProtocol.ParseRulesPacket(RawRules(([1, 2], encoded))));
            Throws<InvalidDataException>(() => DayZServerModsProtocol.ParseRulesPacket(RawRules(([1, 2], encoded), ([2, 3], [5]))));
            Throws<InvalidDataException>(() => DayZServerModsProtocol.ParseRulesPacket(RawRules(([1, 1], encoded), ([1, 1], [5]))));
            Throws<InvalidDataException>(() => DayZServerModsProtocol.ParseRulesPacket(RawRules(("island"u8.ToArray(), "enoch"u8.ToArray()))));
            return Task.CompletedTask;
        });
        await test("Mody: błędne kodowanie stron i metadane numeracji są odrzucane", () =>
        {
            foreach (var packet in new[] { RawRules(([1, 1], [2, 1])), RawRules(([1, 1], [2, 1, 4])), RawRules(([0, 1], [2])), RawRules(([2, 1], [2])) })
                Throws<InvalidDataException>(() => DayZServerModsProtocol.ParseRulesPacket(packet));
            return Task.CompletedTask;
        });
        await test("Mody: ucięte pola, nieprawidłowy UTF8, ID ponad uint64 i wersje są odrzucane", () =>
        {
            var payload = Payload([("M", 123)]);
            for (var length = 0; length < payload.Length; length++)
                Throws<InvalidDataException>(() => DayZServerModsProtocol.ParsePayload(payload.AsSpan(0, length)));
            var invalidId = (byte[])payload.Clone(); invalidId[9] = 9;
            Throws<InvalidDataException>(() => DayZServerModsProtocol.ParsePayload(invalidId));
            var invalidName = (byte[])payload.Clone(); invalidName[15] = 255;
            Throws<InvalidDataException>(() => DayZServerModsProtocol.ParsePayload(invalidName));
            var version = (byte[])payload.Clone(); version[0] = 3;
            Throws<InvalidDataException>(() => DayZServerModsProtocol.ParsePayload(version));
            Throws<InvalidDataException>(() => DayZServerModsProtocol.ParsePayload([.. payload, 0, 42]));
            Throws<InvalidDataException>(() => DayZServerModsProtocol.ParseRulesPacket(new byte[65_537]));
            return Task.CompletedTask;
        });
        await test("Mody: niezweryfikowane flagi są zachowane bez zgadywania ucięcia listy", () =>
        {
            var metadata = DayZServerModsProtocol.ParsePayload(Payload([("M", 123)], flags: 8));
            var result = Result(metadata);
            Check(result.State == ServerModsQueryState.Available && result.Flags == 8 && result.Mods?.Count == 1 && !result.HasCompleteWorkshopMetadata,
                "Flagi nie powinny usuwać listy ani potwierdzać jej kompletności.");
            return Task.CompletedTask;
        });
        await test("Mody: ID0 pozostaje lokalnym lub nieustalonym modem i blokuje kompletność", () =>
        {
            var metadata = DayZServerModsProtocol.ParsePayload(Payload([("Known", 123), ("@craft-nails", 0)]));
            Check(metadata.Mods.Count == 2 && metadata.Mods[1].WorkshopId == "0" && !metadata.Mods[1].HasWorkshopId && !Result(metadata).HasCompleteWorkshopMetadata,
                "Nie wolno pominąć moda ID0 ani zastąpić go wymyślonym Workshop ID.");
            foreach (var id in new[] { "0", "-1", "1e3", "18446744073709551616", " 1", "" })
                Check(!new ServerWorkshopMod("Test", id).HasWorkshopId, "Przyjęto nieprawidłowe Workshop ID.");
            return Task.CompletedTask;
        });
        await test("Mody: zero modów jest kompletne wyłącznie przy poprawnym jawnym payloadzie v2", () =>
        {
            Check(Result(DayZServerModsProtocol.ParseRulesPacket(Rules(Payload([])))).HasCompleteWorkshopMetadata,
                "Jawna poprawna lista bez modów powinna być odróżniona od braku odpowiedzi.");
            Check(!new ServerModsQueryResult(ServerModsQueryState.NoResponse, DateTimeOffset.UtcNow).HasCompleteWorkshopMetadata,
                "Brak odpowiedzi nie jest pustą poprawną listą.");
            return Task.CompletedTask;
        });
        await test("Mody: przechwycony Deer zawiera dokładnie27 dodatnich ID i niezweryfikowane flagi0x08", () =>
        {
            var metadata = DayZServerModsProtocol.ParseRulesPacket(deerRules);
            Check(metadata.Mods.Count == 27 && metadata.Mods.All(mod => mod.HasWorkshopId) && metadata.Flags == 8 &&
                metadata.Mods.Any(mod => mod.Name == "KS - Mody") && metadata.Mods.Any(mod => mod.Name == "DeerIsle") &&
                !Result(metadata).HasCompleteWorkshopMetadata, "Rzeczywisty snapshot Deer został zmieniony lub niepoprawnie potwierdzony.");
            return Task.CompletedTask;
        });
        await test("Mody: oryginalny snapshot Badlands zachowuje12modów i @craft-nails ID0", () =>
        {
            var metadata = DayZServerModsProtocol.ParseRulesPacket(badlandsRules);
            Check(metadata.Mods.Count == 12 && metadata.Mods.Count(mod => !mod.HasWorkshopId) == 1 &&
                metadata.Mods.Any(mod => mod.Name == "@craft-nails" && mod.WorkshopId == "0") && !Result(metadata).HasCompleteWorkshopMetadata,
                "Rzeczywisty snapshot Badlands zgubił lokalny mod lub kolejność danych.");
            return Task.CompletedTask;
        });
        await test("Mody: INFO i RULES używają jednej sesji, tego samego cookie i przerwy300ms", async () =>
        {
            var session = new Session(infoChallenge, info, deerRules);
            var transport = new Transport(session);
            var result = await new SteamServerModsQueryService(transport).QueryAsync(Server());
            _ = SteamQueryProtocol.TryReadChallenge(infoChallenge, out var cookie);
            Check(result.State == ServerModsQueryState.Available && result.Mods?.Count == 27 && result.Flags == 8 &&
                transport.Calls == 1 && session.Sent.Count == 3 && session.Sent[1].SequenceEqual(SteamQueryProtocol.CreateInfoRequest(cookie)) &&
                session.Sent[2].SequenceEqual(DayZServerModsProtocol.CreateRulesRequest(cookie)) &&
                session.SentTimes[2] - session.SentTimes[1] >= TimeSpan.FromMilliseconds(250) && session.Disposed,
                "Sekwencja odbiega od potwierdzonego INFO/cookie/RULES w tej samej sesji.");
            Check(result.Diagnostic?.Contains("niezweryfikowane flagi", StringComparison.Ordinal) == true &&
                !result.Diagnostic.Contains("uci", StringComparison.OrdinalIgnoreCase), "Nie wolno zgadywać znaczenia flag0x08.");
        });
        await test("Mody: INFO49bez41 korzysta ze standardowego RULES-1 i późniejszego challenge", async () =>
        {
            var session = new Session(info, Challenge(9876), Rules(Payload([("Test", 123)])));
            var result = await new SteamServerModsQueryService(new Transport(session)).QueryAsync(Server());
            Check(result.HasCompleteWorkshopMetadata && session.Sent.Count == 3 &&
                session.Sent[1].SequenceEqual(DayZServerModsProtocol.CreateRulesRequest(uint.MaxValue)) &&
                session.Sent[2].SequenceEqual(DayZServerModsProtocol.CreateRulesRequest(9876)), "Brak INFOcookie powinien użyć standardowego handshake RULES.");
        });
        await test("Mody: bezpośrednia odpowiedź RULES po INFO49 jest również poprawna", async () =>
        {
            var session = new Session(info, Rules(Payload([("Test", 123)])));
            var result = await new SteamServerModsQueryService(new Transport(session)).QueryAsync(Server());
            Check(result.HasCompleteWorkshopMetadata && session.Sent.Count == 2 && session.Disposed, "Odrzucono poprawną bezpośrednią odpowiedź RULES.");
        });
        await test("Mody: brak Query albo zły port nie otwierają transportu", async () =>
        {
            var transport = new Transport(new());
            foreach (var server in new[] { Server() with { QueryPort = null }, Server() with { QueryPort = 0 } })
            {
                var result = await new SteamServerModsQueryService(transport).QueryAsync(server);
                Check(result.State == ServerModsQueryState.Unconfigured && result.Mods is null, "Nieustalona konfiguracja nie może mieć listy modów.");
            }
            Check(transport.Calls == 0, "Nieprawidłowa konfiguracja spowodowała kontakt sieciowy.");
        });
        await test("Mody: timeout INFO lub RULES zachowuje Modsnull i nie udaje pustej listy", async () =>
        {
            var beforeInfo = new Session();
            var noInfo = await new SteamServerModsQueryService(new Transport(beforeInfo), TimeSpan.FromMilliseconds(30)).QueryAsync(Server());
            var afterInfo = new Session(info);
            var noRules = await new SteamServerModsQueryService(new Transport(afterInfo), TimeSpan.FromMilliseconds(350)).QueryAsync(Server());
            Check(noInfo.State == ServerModsQueryState.NoResponse && noInfo.Mods is null && noRules.State == ServerModsQueryState.NoResponse &&
                noRules.Mods is null && !noRules.HasCompleteWorkshopMetadata && afterInfo.Sent.Count == 2 && beforeInfo.Disposed && afterInfo.Disposed,
                "Timeout nie może wymyślać pustych metadanych.");
        });
        await test("Mody: anulowanie propaguje się przed sesją i podczas przerwy INFO→RULES", async () =>
        {
            using var before = new CancellationTokenSource(); before.Cancel();
            var transport = new Transport(new());
            await ThrowsAsync<OperationCanceledException>(() => new SteamServerModsQueryService(transport).QueryAsync(Server(), before.Token));
            Check(transport.Calls == 0, "Anulowanie przed startem utworzyło sesję.");
            using var during = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
            var session = new Session(info);
            await ThrowsAsync<OperationCanceledException>(() => new SteamServerModsQueryService(new Transport(session)).QueryAsync(Server(), during.Token));
            Check(session.Disposed && session.Sent.Count == 1, "Przerwa300ms nie respektuje anulowania.");
        });
        await test("Mody: błędne RULES nie zwraca Available ani pustej listy", async () =>
        {
            var result = await new SteamServerModsQueryService(new Transport(new(info, RawRules()))).QueryAsync(Server());
            Check(result.State == ServerModsQueryState.InvalidResponse && result.Mods is null && !result.HasCompleteWorkshopMetadata,
                "Brak stron nie jest potwierdzeniem braku modów.");
        });
        await test("Mody: ograniczona liczba challenge chroni obie fazy", async () =>
        {
            var infoSession = new Session(Challenge(1), Challenge(2), Challenge(3));
            var badInfo = await new SteamServerModsQueryService(new Transport(infoSession)).QueryAsync(Server());
            var rulesSession = new Session(info, Challenge(1), Challenge(2), Challenge(3));
            var badRules = await new SteamServerModsQueryService(new Transport(rulesSession)).QueryAsync(Server());
            Check(badInfo.State == ServerModsQueryState.InvalidResponse && infoSession.Sent.Count == 3 &&
                badRules.State == ServerModsQueryState.InvalidResponse && rulesSession.Sent.Count == 4,
                "Przekroczono limit dwóch challenge na fazę.");
        });
        await test("Mody: limity16datagramów i64KiB obejmują INFO oraz RULES razem", async () =>
        {
            var fragments = Enumerable.Range(0, 20).Select(_ => Split([5], 2, 0)).Prepend(info).ToArray();
            var many = new Session(fragments);
            var first = await new SteamServerModsQueryService(new Transport(many)).QueryAsync(Server());
            Check(first.State == ServerModsQueryState.InvalidResponse && many.Received == 16, "Limit datagramów został wyzerowany między fazami.");
            var large = new Session(info, Split(new byte[40_000], 2, 0), Split(new byte[40_000], 2, 1));
            var second = await new SteamServerModsQueryService(new Transport(large)).QueryAsync(Server());
            Check(second.State == ServerModsQueryState.InvalidResponse && second.Diagnostic?.Contains("64 KiB", StringComparison.Ordinal) == true,
                "Przekroczono całkowity limit danych.");
        });
        await test("Mody: timeout nie może przekroczyć pięciu sekund", () =>
        {
            Throws<ArgumentOutOfRangeException>(() => new SteamServerModsQueryService(new Transport(new()), TimeSpan.FromSeconds(6)));
            return Task.CompletedTask;
        });

        byte[] Snapshot(string name)
        {
            var entry = snapshots.RootElement.GetProperty(name);
            var bytes = Convert.FromBase64String(entry.GetProperty("base64").GetString()!);
            Check(Convert.ToHexStringLower(SHA256.HashData(bytes)) == entry.GetProperty("sha256").GetString(), "Zmieniono przechwycony fixture.");
            return bytes;
        }
    }

    private static ServerDefinition Server() => new("deer-isle", "Deer Isle", "178.95.158.100", 23000, 9000);
    private static ServerModsQueryResult Result(DayZModsMetadata data) => new(ServerModsQueryState.Available, DateTimeOffset.UtcNow, data.Mods, Flags: data.Flags);
    private static byte[] Payload((string Name, ulong Id)[] mods, byte flags = 0, byte idLength = 4, ushort dlcMask = 0, string? description = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write((byte)2); writer.Write(flags); writer.Write(dlcMask);
        for (var bit = 0; bit < 16; bit++) if ((dlcMask & (1 << bit)) != 0) writer.Write((uint)(bit + 100));
        writer.Write((byte)mods.Length);
        foreach (var mod in mods)
        {
            writer.Write((uint)0xFF000101); writer.Write(idLength);
            for (var index = 0; index < idLength; index++) writer.Write((byte)(mod.Id >> (index * 8)));
            Text(mod.Name);
        }
        writer.Write((byte)0); // No signature names in this synthetic fixture.
        if (description is not null) Text(description);
        return stream.ToArray();
        void Text(string text) { var bytes = Encoding.UTF8.GetBytes(text); writer.Write((byte)bytes.Length); writer.Write(bytes); }
    }
    private static byte[] Escape(byte[] payload) => payload.SelectMany(value => value switch
    {
        0 => new byte[] { 1, 2 }, 1 => new byte[] { 1, 1 }, 255 => new byte[] { 1, 3 }, _ => [value]
    }).ToArray();
    private static byte[] Rules(byte[] payload) => RawRules(([1, 1], Escape(payload)));
    private static byte[] RawRules(params (byte[] Key, byte[] Value)[] entries)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(-1); writer.Write((byte)0x45); writer.Write((ushort)entries.Length);
        foreach (var entry in entries) { writer.Write(entry.Key); writer.Write((byte)0); writer.Write(entry.Value); writer.Write((byte)0); }
        return stream.ToArray();
    }
    private static byte[] Challenge(uint value)
    {
        var packet = new byte[9]; BinaryPrimitives.WriteInt32LittleEndian(packet, -1); packet[4] = 0x41;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(5), value); return packet;
    }
    private static byte[] Split(byte[] body, byte total, byte number)
    {
        var packet = new byte[12 + body.Length]; BinaryPrimitives.WriteInt32LittleEndian(packet, -2);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), 12); packet[8] = total; packet[9] = number;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(10), 60_000); body.CopyTo(packet, 12); return packet;
    }
    private sealed class Transport(Session session) : IServerQueryTransport
    {
        public int Calls { get; private set; }
        public Task<IServerQuerySession> CreateSessionAsync(string host, int queryPort, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Calls++; return Task.FromResult<IServerQuerySession>(session); }
    }
    private sealed class Session(params byte[][] packets) : IServerQuerySession
    {
        private readonly Queue<byte[]> _packets = new(packets);
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        public List<byte[]> Sent { get; } = [];
        public List<TimeSpan> SentTimes { get; } = [];
        public int Received { get; private set; }
        public bool Disposed { get; private set; }
        public ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Sent.Add(packet.ToArray()); SentTimes.Add(_clock.Elapsed); return ValueTask.CompletedTask; }
        public async ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken token)
        {
            if (_packets.Count > 0) { Received++; return _packets.Dequeue(); }
            await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException("Unreachable.");
        }
        public void Dispose() => Disposed = true;
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Oczekiwano {typeof(T).Name}."); }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new InvalidOperationException($"Oczekiwano {typeof(T).Name}."); }
}

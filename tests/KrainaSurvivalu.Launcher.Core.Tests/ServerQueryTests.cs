using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using KrainaSurvivalu.Launcher.Core;

internal static class ServerQueryTests
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> test)
    {
        await test("A2S: współczesna odpowiedź INFO zachowuje nazwę Unicode, mapę i liczby", () =>
        {
            var info = SteamQueryProtocol.ParseInfo(Info());
            Check(info == new SteamServerInfo("Kraina Żółwia — TEST", "enoch", 12, 60, 2), "Zmieniono fakty odebrane w INFO.");
            return Task.CompletedTask;
        });
        await test("A2S: wszystkie opcjonalne pola EDF są bezpiecznie odczytywane", () =>
        {
            var info = SteamQueryProtocol.ParseInfo(Info(edf: 0xF1));
            Check(info.Players == 12 && info.MaxPlayers == 60 && info.Bots == 2, "Pola EDF przesunęły liczby graczy.");
            _ = SteamQueryProtocol.ParseInfo(Info(edf: 0));
            return Task.CompletedTask;
        });
        await test("A2S: każda ucięta podstawowa odpowiedź jest odrzucana", () =>
        {
            var complete = Info();
            for (var length = 0; length < complete.Length; length++)
                Throws<InvalidDataException>(() => SteamQueryProtocol.ParseInfo(complete.AsSpan(0, length)));
            var extended = Info(edf: 0xF1);
            Throws<InvalidDataException>(() => SteamQueryProtocol.ParseInfo(extended.AsSpan(0, extended.Length - 1)));
            return Task.CompletedTask;
        });
        await test("A2S: niepoprawne UTF8, kontrolne i zbyt długie napisy są odrzucane", () =>
        {
            var utf8 = Info();
            utf8[6] = 0xC3;
            utf8[7] = 0x28;
            Throws<InvalidDataException>(() => SteamQueryProtocol.ParseInfo(utf8));
            Throws<InvalidDataException>(() => SteamQueryProtocol.ParseInfo(Info(name: "Zły\nserwer")));
            Throws<InvalidDataException>(() => SteamQueryProtocol.ParseInfo(Info(name: new string('x', 257))));
            Throws<InvalidDataException>(() => SteamQueryProtocol.ParseInfo(Info(name: "")));
            return Task.CompletedTask;
        });
        await test("A2S: nieznane EDF, format legacy i zbędne bajty są odrzucane", () =>
        {
            Throws<InvalidDataException>(() => SteamQueryProtocol.ParseInfo(Info(edf: 2)));
            Throws<InvalidDataException>(() => SteamQueryProtocol.ParseInfo([.. Info(edf: 0), 42]));
            var legacy = Info();
            legacy[4] = 0x6D;
            Throws<InvalidDataException>(() => SteamQueryProtocol.ParseInfo(legacy));
            return Task.CompletedTask;
        });
        await test("A2S: zgłoszone liczniki uint8 są zachowane również ponad liczbą slotów", () =>
        {
            var info = SteamQueryProtocol.ParseInfo(Info(players: 81, maximum: 80, bots: 82));
            Check(info.Players == 81 && info.MaxPlayers == 80 && info.Bots == 82,
                "Parser wymyślił niedokumentowane relacje liczb zamiast zachować zgłoszone bajty.");
            var fullRange = SteamQueryProtocol.ParseInfo(Info(players: 255, maximum: 0, bots: 255));
            Check(fullRange.Players == 255 && fullRange.MaxPlayers == 0 && fullRange.Bots == 255,
                "Nie zachowano pełnego zakresu uint8.");
            return Task.CompletedTask;
        });
        await test("A2S: challenge ma dokładnie cztery bajty i jest dołączany do pełnego zapytania INFO", () =>
        {
            var packet = Challenge(0xFEDCBA98);
            Check(SteamQueryProtocol.TryReadChallenge(packet, out var value) && value == 0xFEDCBA98, "Zmieniono wartość challenge.");
            var initial = SteamQueryProtocol.CreateInfoRequest();
            var response = SteamQueryProtocol.CreateInfoRequest(value);
            Check(initial.Length == 25 && response.Length == 29 && response.AsSpan(0, initial.Length).SequenceEqual(initial) &&
                BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(25)) == value, "Nieprawidłowe zapytanie z challenge.");
            Throws<InvalidDataException>(() => SteamQueryProtocol.TryReadChallenge(packet.AsSpan(0, 8), out _));
            Throws<InvalidDataException>(() => SteamQueryProtocol.TryReadChallenge([.. packet, 1], out _));
            Check(!SteamQueryProtocol.TryReadChallenge(Info(), out _), "INFO błędnie rozpoznano jako challenge.");
            return Task.CompletedTask;
        });
        await test("A2S: części Source są łączone poza kolejnością, również przy przeciętym nagłówku", () =>
        {
            var info = Info(edf: 0xF1);
            foreach (var cut in new[] { 2, info.Length / 2 })
            {
                var assembler = new SteamQueryPacketAssembler();
                Check(assembler.AddDatagram(Split(info[cut..], 2, 1)) is null, "Złożono niepełną odpowiedź.");
                var combined = assembler.AddDatagram(Split(info[..cut], 2, 0));
                Check(combined is not null && combined.SequenceEqual(info), "Źle odtworzono odpowiedź dzieloną.");
                _ = SteamQueryProtocol.ParseInfo(combined);
            }
            var withoutNested = Info()[4..];
            var plainAssembler = new SteamQueryPacketAssembler();
            _ = plainAssembler.AddDatagram(Split(withoutNested[..10], 2, 0));
            _ = SteamQueryProtocol.ParseInfo(plainAssembler.AddDatagram(Split(withoutNested[10..], 2, 1))!);
            return Task.CompletedTask;
        });
        await test("A2S: zgodne duplikaty są ograniczone, a sprzeczne części i ID są odrzucane", () =>
        {
            var assembler = new SteamQueryPacketAssembler();
            var first = Split(Info()[..10], 2, 0);
            _ = assembler.AddDatagram(first);
            Check(assembler.AddDatagram(first) is null, "Duplikat nie powinien kończyć odpowiedzi.");
            var conflict = (byte[])first.Clone();
            conflict[^1] ^= 1;
            Throws<InvalidDataException>(() => assembler.AddDatagram(conflict));
            Throws<InvalidDataException>(() => assembler.AddDatagram(Split([1], 2, 1, id: 2)));
            return Task.CompletedTask;
        });
        await test("A2S: bzip2, nadmierna liczba fragmentów i błędne długości są jawnie odrzucane", () =>
        {
            var compressed = Throws<InvalidDataException>(() => new SteamQueryPacketAssembler().AddDatagram(Split([1], 1, 0, id: 0x80000001)));
            Check(compressed.Message.Contains("bzip2", StringComparison.Ordinal), "Brak uczciwego wyjaśnienia nieobsługiwanej kompresji.");
            Throws<InvalidDataException>(() => new SteamQueryPacketAssembler().AddDatagram(Split([1], 17, 0)));
            Throws<InvalidDataException>(() => new SteamQueryPacketAssembler().AddDatagram(Split([1], 1, 1)));
            Throws<InvalidDataException>(() => new SteamQueryPacketAssembler().AddDatagram(Split([1], 1, 0, size: 0)));
            Throws<InvalidDataException>(() => new SteamQueryPacketAssembler().AddDatagram(new byte[65_537]));
            return Task.CompletedTask;
        });
        await test("A2S: brak Query nie tworzy sesji ani nie zgaduje portu", async () =>
        {
            var transport = new FakeTransport(new());
            var result = await new SteamServerQueryService(transport).QueryAsync(Server() with { QueryPort = null });
            Check(result.State == ServerQueryState.Unconfigured && transport.Calls == 0 && result.Players is null,
                "Brak portu Query spowodował kontakt lub wymyślenie danych.");
        });
        await test("A2S: sukces korzysta wyłącznie z jawnego Query i zwraca UTC oraz zmierzony czas", async () =>
        {
            var session = new FakeSession(Info());
            var transport = new FakeTransport(session);
            var result = await new SteamServerQueryService(transport).QueryAsync(Server());
            Check(result.State == ServerQueryState.Online && result.Name == "Kraina Żółwia — TEST" && result.Map == "enoch" &&
                result.Players == 12 && result.MaxPlayers == 60 && result.Bots == 2 && result.RoundTrip >= TimeSpan.Zero &&
                result.CheckedAtUtc.Offset == TimeSpan.Zero && result.CheckedAtUtc.Year >= 2026 &&
                transport.Host == "145.239.237.112" && transport.Port == 27801 && session.Disposed,
                "Nieprawidłowy status, Query, czas albo zwolnienie sesji.");
        });
        await test("A2S: pełny handshake challenge zachowuje nonce i kończy się informacją", async () =>
        {
            var session = new FakeSession(Challenge(1234), Info(edf: 0xF1));
            var result = await new SteamServerQueryService(new FakeTransport(session)).QueryAsync(Server());
            Check(result.State == ServerQueryState.Online && session.Sent.Count == 2 &&
                session.Sent[1].SequenceEqual(SteamQueryProtocol.CreateInfoRequest(1234)), "Nie wykonano handshake INFO.");
        });
        await test("A2S: trzeci challenge zostaje zatrzymany bez dodatkowego wysłania", async () =>
        {
            var session = new FakeSession(Challenge(1), Challenge(2), Challenge(3));
            var result = await new SteamServerQueryService(new FakeTransport(session)).QueryAsync(Server());
            Check(result.State == ServerQueryState.InvalidResponse && session.Sent.Count == 3 && session.Disposed,
                "Przekroczono limit challenge.");
        });
        await test("A2S: timeout oznacza NoResponse z pustymi liczbami, a nie Offline", async () =>
        {
            var session = new FakeSession();
            var result = await new SteamServerQueryService(new FakeTransport(session), TimeSpan.FromMilliseconds(30)).QueryAsync(Server());
            Check(result.State == ServerQueryState.NoResponse && result.Players is null && result.MaxPlayers is null &&
                result.Name is null && result.RoundTrip is null && result.Diagnostic?.Contains("Nie potwierdza", StringComparison.Ordinal) == true && session.Disposed,
                "Brak odpowiedzi został przedstawiony jako faktyczny stan lub liczba graczy.");
        });
        await test("A2S: błędy sieci i DNS nie są dowodem wyłączenia serwera", async () =>
        {
            var transport = new FakeTransport(new()) { Error = new SocketException((int)SocketError.HostNotFound) };
            var result = await new SteamServerQueryService(transport).QueryAsync(Server());
            Check(result.State == ServerQueryState.NoResponse && result.Players is null && result.Diagnostic is not null,
                "Błąd transportu nie zachował uczciwego statusu NoResponse.");
        });
        await test("A2S: anulowanie klienta jest propagowane przed i podczas oczekiwania", async () =>
        {
            using var before = new CancellationTokenSource();
            before.Cancel();
            var transport = new FakeTransport(new());
            await ThrowsAsync<OperationCanceledException>(() => new SteamServerQueryService(transport).QueryAsync(Server(), before.Token));
            Check(transport.Calls == 0, "Anulowane zapytanie utworzyło sesję.");
            using var during = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
            var session = new FakeSession();
            await ThrowsAsync<OperationCanceledException>(() => new SteamServerQueryService(new FakeTransport(session)).QueryAsync(Server(), during.Token));
            Check(session.Disposed, "Anulowanie nie zwolniło sesji.");
        });
        await test("A2S: błędna odpowiedź i niekompletne części mają InvalidResponse bez liczników", async () =>
        {
            foreach (var packet in new[] { new byte[] { 1, 2, 3, 4, 5 }, Split(Info()[..8], 2, 0) })
            {
                var result = await new SteamServerQueryService(new FakeTransport(new(packet)), TimeSpan.FromMilliseconds(30)).QueryAsync(Server());
                Check(result.State == ServerQueryState.InvalidResponse && result.Players is null && result.Map is null,
                    "Niepełna lub uszkodzona odpowiedź została przyjęta jako poprawne dane.");
            }
        });
        await test("A2S: limity 16 datagramów i 64 KiB obejmują całą operację", async () =>
        {
            var duplicate = Split([1], 2, 0);
            var session = new FakeSession(Enumerable.Range(0, 20).Select(_ => duplicate).ToArray());
            var result = await new SteamServerQueryService(new FakeTransport(session)).QueryAsync(Server());
            Check(result.State == ServerQueryState.InvalidResponse && session.Received == 16 && session.Disposed,
                "Przekroczono całkowity limit datagramów.");
            var large = new FakeSession(Split(new byte[40_000], 2, 0, size: 50_000), Split(new byte[40_000], 2, 1, size: 50_000));
            var oversized = await new SteamServerQueryService(new FakeTransport(large)).QueryAsync(Server());
            Check(oversized.State == ServerQueryState.InvalidResponse && oversized.Diagnostic?.Contains("64 KiB", StringComparison.Ordinal) == true &&
                large.Received == 2 && large.Disposed, "Przekroczono limit danych sieciowych.");
        });
        await test("A2S: usługa obsługuje rzeczywistą reassembly przez atrapę transportu", async () =>
        {
            var packet = Info(edf: 0xF1);
            var session = new FakeSession(Split(packet[20..], 2, 1), Split(packet[..20], 2, 0));
            var result = await new SteamServerQueryService(new FakeTransport(session)).QueryAsync(Server());
            Check(result.State == ServerQueryState.Online && result.Players == 12 && session.Received == 2,
                "Integracja transportu i reassembly nie przyjęła poprawnych części.");
        });
        await test("A2S: konfiguracja nie pozwala przedłużyć limitu ponad pięć sekund", () =>
        {
            Throws<ArgumentOutOfRangeException>(() => new SteamServerQueryService(new FakeTransport(new()), TimeSpan.FromSeconds(6)));
            Throws<ArgumentOutOfRangeException>(() => new SteamServerQueryService(new FakeTransport(new()), TimeSpan.Zero));
            return Task.CompletedTask;
        });
    }

    private static ServerDefinition Server() => new("badlands", "Badlands", "145.239.237.112", 27800, 27801);

    private static byte[] Info(byte? edf = null, byte players = 12, byte bots = 2, string name = "Kraina Żółwia — TEST", byte maximum = 60)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(-1); writer.Write((byte)0x49); writer.Write((byte)17);
        Z(name); Z("enoch"); Z("dayz"); Z("DayZ");
        writer.Write((ushort)(221100 & 65535));
        writer.Write(players); writer.Write(maximum); writer.Write(bots);
        writer.Write((byte)'d'); writer.Write((byte)'w'); writer.Write((byte)0); writer.Write((byte)1);
        Z("1.30.164078");
        if (edf is { } flags)
        {
            writer.Write(flags);
            if ((flags & 0x80) != 0) writer.Write((ushort)27800);
            if ((flags & 0x10) != 0) writer.Write(ulong.MaxValue);
            if ((flags & 0x40) != 0) { writer.Write((ushort)27020); Z("Test SourceTV"); }
            if ((flags & 0x20) != 0) Z("test,offline-fixture");
            if ((flags & 1) != 0) writer.Write((ulong)221100);
        }
        return stream.ToArray();
        void Z(string text) { writer.Write(Encoding.UTF8.GetBytes(text)); writer.Write((byte)0); }
    }

    private static byte[] Challenge(uint value)
    {
        var result = new byte[9];
        BinaryPrimitives.WriteInt32LittleEndian(result, -1);
        result[4] = 0x41;
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(5), value);
        return result;
    }

    private static byte[] Split(byte[] data, byte total, byte number, uint id = 1, ushort size = 1248)
    {
        var result = new byte[12 + data.Length];
        BinaryPrimitives.WriteInt32LittleEndian(result, -2);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), id);
        result[8] = total; result[9] = number;
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10), size);
        data.CopyTo(result, 12);
        return result;
    }

    private sealed class FakeTransport(FakeSession session) : IServerQueryTransport
    {
        public int Calls { get; private set; }
        public string? Host { get; private set; }
        public int? Port { get; private set; }
        public Exception? Error { get; init; }
        public Task<IServerQuerySession> CreateSessionAsync(string host, int queryPort, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++; Host = host; Port = queryPort;
            return Error is null ? Task.FromResult<IServerQuerySession>(session) : Task.FromException<IServerQuerySession>(Error);
        }
    }

    private sealed class FakeSession(params byte[][] packets) : IServerQuerySession
    {
        private readonly Queue<byte[]> _packets = new(packets);
        public List<byte[]> Sent { get; } = [];
        public int Received { get; private set; }
        public bool Disposed { get; private set; }
        public ValueTask SendAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sent.Add(datagram.ToArray());
            return ValueTask.CompletedTask;
        }
        public async ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken cancellationToken)
        {
            if (_packets.Count > 0) { Received++; return _packets.Dequeue(); }
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable timeout fixture.");
        }
        public void Dispose() => Disposed = true;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Oczekiwano {typeof(T).Name}.");
    }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Oczekiwano {typeof(T).Name}.");
    }
}

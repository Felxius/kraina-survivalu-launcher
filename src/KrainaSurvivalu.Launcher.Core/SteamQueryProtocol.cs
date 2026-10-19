using System.Buffers.Binary;
using System.Text;

namespace KrainaSurvivalu.Launcher.Core;

/// <summary>A2S_INFO wire parsing only. Does not infer Workshop lists or game connection ports.</summary>
public static class SteamQueryProtocol
{
    public const int MaximumDataBytes = 65_536;
    public const int MaximumDatagrams = 16;
    private static readonly byte[] RequestPrefix = [255, 255, 255, 255, 0x54, .. "Source Engine Query\0"u8];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] CreateInfoRequest(uint? challenge = null)
    {
        var result = new byte[RequestPrefix.Length + (challenge.HasValue ? 4 : 0)];
        RequestPrefix.CopyTo(result, 0);
        if (challenge.HasValue)
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(RequestPrefix.Length), challenge.Value);
        return result;
    }

    public static bool TryReadChallenge(ReadOnlySpan<byte> packet, out uint challenge)
    {
        challenge = 0;
        if (packet.Length < 5 || BinaryPrimitives.ReadInt32LittleEndian(packet) != -1 || packet[4] != 0x41)
            return false;
        if (packet.Length != 9)
            throw new InvalidDataException("Odpowiedź challenge A2S ma nieprawidłową długość.");
        challenge = BinaryPrimitives.ReadUInt32LittleEndian(packet[5..]);
        return true;
    }

    public static SteamServerInfo ParseInfo(ReadOnlySpan<byte> packet)
    {
        if (packet.Length > MaximumDataBytes)
            throw new InvalidDataException("Odpowiedź A2S przekracza 64 KiB.");
        var reader = new PacketReader(packet);
        if (reader.UInt32() != uint.MaxValue || reader.Byte() != 0x49)
            throw new InvalidDataException("Odpowiedź nie jest współczesnym pakietem A2S_INFO (0x49).");
        if (reader.Byte() == 0)
            throw new InvalidDataException("Odpowiedź A2S ma nieprawidłową wersję protokołu.");
        var name = reader.String(256);
        var map = reader.String(128);
        _ = reader.String(128); // Folder
        _ = reader.String(256); // Game description
        var appId = reader.UInt16();
        var players = reader.Byte();
        var maximum = reader.Byte();
        var bots = reader.Byte();
        var serverType = (char)reader.Byte();
        var environment = (char)reader.Byte();
        if (!"dlpDLP".Contains(serverType) || !"lwmoLWMO".Contains(environment) ||
            reader.Byte() > 1 || reader.Byte() > 1)
            throw new InvalidDataException("Odpowiedź A2S ma nieprawidłowe pola typu serwera, systemu lub zabezpieczeń.");
        if (appId == 2400) // The Ship's documented three extra bytes.
        {
            _ = reader.Byte();
            _ = reader.Byte();
            _ = reader.Byte();
        }
        _ = reader.String(128); // Version
        if (reader.Remaining > 0)
        {
            var flags = reader.Byte();
            if ((flags & ~0xF1) != 0)
                throw new InvalidDataException("Odpowiedź A2S zawiera nieobsługiwane bity EDF.");
            if ((flags & 0x80) != 0) _ = reader.UInt16();
            if ((flags & 0x10) != 0) _ = reader.UInt64();
            if ((flags & 0x40) != 0)
            {
                _ = reader.UInt16();
                _ = reader.String(256);
            }
            if ((flags & 0x20) != 0) _ = reader.String(2048);
            if ((flags & 0x01) != 0) _ = reader.UInt64();
        }
        if (reader.Remaining != 0 || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(map))
            throw new InvalidDataException("Odpowiedź A2S zawiera zbędne bajty albo pustą nazwę lub mapę.");
        return new(name, map, players, maximum, bots);
    }

    private ref struct PacketReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _offset;
        public int Remaining => _data.Length - _offset;
        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || count > Remaining)
                throw new InvalidDataException("Odpowiedź A2S jest ucięta.");
            var result = _data.Slice(_offset, count);
            _offset += count;
            return result;
        }
        public byte Byte() => Take(1)[0];
        public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public ulong UInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
        public string String(int maximumBytes)
        {
            var available = _data[_offset..];
            var terminator = available.IndexOf((byte)0);
            if (terminator < 0 || terminator > maximumBytes)
                throw new InvalidDataException("Pole tekstowe A2S jest zbyt długie lub nie ma terminatora.");
            try
            {
                var value = StrictUtf8.GetString(Take(terminator));
                _ = Take(1);
                if (value.Any(char.IsControl))
                    throw new InvalidDataException("Pole tekstowe A2S zawiera znaki sterujące.");
                return value;
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("Pole tekstowe A2S nie jest poprawnym UTF-8.", exception);
            }
        }
    }
}

/// <summary>One bounded Source split-packet response. Compressed bzip2 and legacy GoldSrc formats are unsupported.</summary>
public sealed class SteamQueryPacketAssembler
{
    private uint? _id;
    private byte[][]? _parts;
    private ushort _splitSize;
    private int _bytes;
    public bool HasPendingFragments => _parts is not null && _parts.Any(part => part is null);

    public byte[]? AddDatagram(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 5 || packet.Length > SteamQueryProtocol.MaximumDataBytes)
            throw new InvalidDataException("Datagram A2S ma nieprawidłową długość.");
        var header = BinaryPrimitives.ReadInt32LittleEndian(packet);
        if (header == -1)
        {
            if (_parts is not null)
                throw new InvalidDataException("Pojedynczy pakiet A2S przerwał niekompletną odpowiedź dzieloną.");
            return packet.ToArray();
        }
        if (header != -2 || packet.Length < 13)
            throw new InvalidDataException("Nieprawidłowy nagłówek datagramu A2S.");
        var id = BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]);
        if ((id & 0x80000000) != 0)
            throw new InvalidDataException("Skompresowane odpowiedzi A2S bzip2 nie są obsługiwane w tej wersji.");
        var total = packet[8];
        var number = packet[9];
        var size = BinaryPrimitives.ReadUInt16LittleEndian(packet[10..]);
        if (total is 0 or > SteamQueryProtocol.MaximumDatagrams || number >= total || size == 0 || packet.Length - 12 > size)
            throw new InvalidDataException("Odpowiedź dzielona A2S ma nieprawidłowy numer, liczbę lub rozmiar części.");
        if (_id is not null && (_id != id || _parts!.Length != total || _splitSize != size))
            throw new InvalidDataException("Części A2S pochodzą z różnych lub niespójnych odpowiedzi.");
        _id ??= id;
        _parts ??= new byte[total][];
        _splitSize = size;
        var body = packet[12..];
        if (_parts[number] is { } previous)
        {
            if (!body.SequenceEqual(previous))
                throw new InvalidDataException("Powtórzona część A2S zawiera inne dane.");
            return null;
        }
        if (_bytes > SteamQueryProtocol.MaximumDataBytes - body.Length)
            throw new InvalidDataException("Odpowiedź dzielona A2S przekracza 64 KiB.");
        _parts[number] = body.ToArray();
        _bytes += body.Length;
        if (HasPendingFragments) return null;
        var result = new byte[_bytes];
        var offset = 0;
        foreach (var part in _parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        if (result.Length >= 4 && BinaryPrimitives.ReadInt32LittleEndian(result) == -1)
            return result;
        if (result.Length > SteamQueryProtocol.MaximumDataBytes - 4)
            throw new InvalidDataException("Złożona odpowiedź A2S przekracza 64 KiB.");
        var withHeader = new byte[result.Length + 4];
        BinaryPrimitives.WriteInt32LittleEndian(withHeader, -1);
        result.CopyTo(withHeader, 4);
        return withHeader;
    }
}

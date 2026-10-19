using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace KrainaSurvivalu.Launcher.Core;

/// <summary>DayZ v2 binary metadata in A2S_RULES. Binary page carriers are never decoded as UTF8 strings.</summary>
public static class DayZServerModsProtocol
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] CreateRulesRequest(uint challenge)
    {
        var packet = new byte[] { 255, 255, 255, 255, 0x56, 0, 0, 0, 0 };
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(5), challenge);
        return packet;
    }

    public static DayZModsMetadata ParseRulesPacket(ReadOnlySpan<byte> packet)
    {
        if (packet.Length > SteamQueryProtocol.MaximumDataBytes)
            throw new InvalidDataException("Odpowiedź RULES przekracza 64 KiB.");
        var reader = new Reader(packet);
        if (reader.UInt32() != uint.MaxValue || reader.Byte() != 0x45)
            throw new InvalidDataException("Odpowiedź nie jest pakietem A2S_RULES (0x45).");
        var count = reader.UInt16();
        if (count > 4096 || count > reader.Remaining / 2)
            throw new InvalidDataException("Liczba reguł przekracza dane odpowiedzi lub limit 4096.");
        var pages = new Dictionary<byte, byte[]>();
        byte total = 0;
        var totalBytes = 0;
        for (var index = 0; index < count; index++)
        {
            var key = reader.NulBytes(256);
            var value = reader.NulBytes(SteamQueryProtocol.MaximumDataBytes);
            if (key.Length != 2) continue; // Ordinary rules are not mod metadata.
            var number = key[0];
            var advertised = key[1];
            if (number == 0 || advertised == 0 || number > advertised || (total != 0 && total != advertised))
                throw new InvalidDataException("Nieprawidłowe lub niespójne numery stron binarnych DayZ.");
            total = advertised;
            if (pages.TryGetValue(number, out var previous))
            {
                if (!value.SequenceEqual(previous))
                    throw new InvalidDataException("Powtórzona strona DayZ zawiera inne dane.");
                continue;
            }
            if (totalBytes > SteamQueryProtocol.MaximumDataBytes - value.Length)
                throw new InvalidDataException("Strony binarne DayZ przekraczają 64 KiB.");
            pages.Add(number, value.ToArray());
            totalBytes += value.Length;
        }
        if (reader.Remaining != 0 || total == 0 || pages.Count != total)
            throw new InvalidDataException("Niepełne strony binarne DayZ albo zbędne dane RULES. Lista modów nie została ustalona.");
        var encoded = new byte[totalBytes];
        var offset = 0;
        for (var page = 1; page <= total; page++)
        {
            if (!pages.TryGetValue((byte)page, out var value))
                throw new InvalidDataException("Brakuje strony binarnej DayZ.");
            value.CopyTo(encoded, offset);
            offset += value.Length;
        }
        // Escape pairs may cross page boundaries. Decode only after all raw pages have been joined.
        var decoded = new byte[encoded.Length];
        var decodedLength = 0;
        for (var position = 0; position < encoded.Length; position++)
        {
            var value = encoded[position];
            if (value == 1)
            {
                if (++position >= encoded.Length)
                    throw new InvalidDataException("Ucięta sekwencja kodowania strony DayZ.");
                value = encoded[position] switch
                {
                    1 => (byte)1,
                    2 => (byte)0,
                    3 => (byte)255,
                    _ => throw new InvalidDataException("Nieprawidłowa sekwencja kodowania strony DayZ.")
                };
            }
            decoded[decodedLength++] = value;
        }
        return ParsePayload(decoded.AsSpan(0, decodedLength));
    }

    public static DayZModsMetadata ParsePayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > SteamQueryProtocol.MaximumDataBytes)
            throw new InvalidDataException("Dane binarne modów przekraczają 64 KiB.");
        var reader = new Reader(payload);
        if (reader.Byte() != 2)
            throw new InvalidDataException("Nieobsługiwana wersja metadanych DayZ. Wymagany format v2.");
        var flags = reader.Byte(); // Undocumented bits: preserve them without inventing truncation semantics.
        var dlcMask = reader.UInt16();
        for (var bit = 0; bit < 16; bit++)
            if ((dlcMask & (1 << bit)) != 0) _ = reader.UInt32();
        var count = reader.Byte();
        var mods = new List<ServerWorkshopMod>(count);
        for (var index = 0; index < count; index++)
        {
            _ = reader.UInt32(); // Short mod hash; never interpreted as a Workshop ID.
            var idLength = reader.Byte();
            if (idLength > 8)
                throw new InvalidDataException("Identyfikator moda przekracza zakres uint64.");
            ulong id = 0;
            for (var part = 0; part < idLength; part++)
                id |= (ulong)reader.Byte() << (8 * part);
            var name = reader.Text();
            mods.Add(new(name, id.ToString(CultureInfo.InvariantCulture)));
        }
        var signatureCount = reader.Byte();
        for (var index = 0; index < signatureCount; index++) _ = reader.Text();
        if (reader.Remaining > 0) _ = reader.Text(allowLineBreaks: true);
        if (reader.Remaining != 0)
            throw new InvalidDataException("Metadane DayZ zawierają zbędne dane po opisie.");
        return new(mods.AsReadOnly(), flags);
    }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _offset;
        public int Remaining => _data.Length - _offset;
        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || count > Remaining)
                throw new InvalidDataException("Ucięte dane RULES lub metadane moda.");
            var result = _data.Slice(_offset, count);
            _offset += count;
            return result;
        }
        public byte Byte() => Take(1)[0];
        public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public ReadOnlySpan<byte> NulBytes(int maximum)
        {
            var terminator = _data[_offset..].IndexOf((byte)0);
            if (terminator < 0 || terminator > maximum)
                throw new InvalidDataException("Pole RULES jest zbyt długie lub nie ma terminatora.");
            var value = Take(terminator);
            _ = Take(1);
            return value;
        }
        public string Text(bool allowLineBreaks = false)
        {
            try
            {
                var text = StrictUtf8.GetString(Take(Byte()));
                if (text.Any(character => char.IsControl(character) && !(allowLineBreaks && character is '\r' or '\n' or '\t')))
                    throw new InvalidDataException("Nazwa lub opis w metadanych moda zawiera znaki sterujące.");
                return text;
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("Nazwa lub opis moda nie jest poprawnym UTF-8.", exception);
            }
        }
    }
}

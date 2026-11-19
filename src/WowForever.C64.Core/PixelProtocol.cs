using System.Buffers.Binary;
using System.Text;

namespace WowForever.C64.Core;

public sealed record PixelFrame(int Width, int Height, byte[] Bgra);

public sealed record BridgeFrame(byte Flags, ushort Sequence, uint Session, ushort SnapshotId,
    ushort ChunkIndex, ushort ChunkCount, ushort TotalLength, uint SnapshotChecksum,
    uint ClockMilliseconds, byte[] Chunk, byte Version = 1)
{
    public BridgeContext? Context { get; init; }
    public int PayloadLength { get; init; } = Chunk.Length;
    public bool InCombat => (Flags & 1) != 0;
    public bool WorldReady => (Flags & 16) != 0;
    public bool AuraSnapshotCurrent => (Flags & 2) != 0 && WorldReady && !InCombat;
    public bool AuraDataAvailable => Version >= 4 ? (Flags & 32) != 0 : AuraSnapshotCurrent;
    public bool Partial => (Flags & 4) != 0;
    public bool Truncated => (Flags & 8) != 0;
}

public sealed record BridgeNotice<T>(uint ClockMilliseconds, T Value);

public sealed record BridgeContext(bool? Dead, BridgeNotice<ushort>? LevelUp,
    BridgeNotice<string>? Quest, BridgeNotice<string>? Zone);

public sealed class PixelProtocol
{
    public const int Columns = 64;
    public const int Rows = 5;
    public const byte CurrentVersion = 5;
    public const int ChunkSize = 252;
    public const int GrayscaleChunkSize = 108;
    private static readonly (bool R, bool G, bool B)[] Markers =
    [
        (true, false, true), (false, true, true), (true, true, true), (false, false, false),
        (true, true, false), (false, false, true), (true, true, true)
    ];
    private (int X, int Y, int Size)? location;
    private int failureStage;
    public string Status { get; private set; } = "Waiting for pixel bridge markers";

    public BridgeFrame? Decode(PixelFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Bgra.Length != frame.Width * frame.Height * 4)
            throw new ArgumentException("Invalid BGRA pixel frame.", nameof(frame));
        failureStage = 0;
        Status = "Pixel bridge markers not found; check addon scale and patch visibility";
        if (location is { } previous && ReadAt(frame, previous.X, previous.Y, previous.Size) is { } known)
            return known;
        location = null;
        for (var y = 0; y <= frame.Height - Rows * 2; y++)
        {
            for (var x = 0; x < Math.Min(320, frame.Width); x++)
            {
                var color = Pixel(frame, x, y);
                if (color.R < 150 || color.G > 100 || color.B < 150)
                    continue;
                for (var size = 2; size <= 6; size++)
                {
                    if (ReadAt(frame, x, y, size) is not { } decoded)
                        continue;
                    location = (x, y, size);
                    return decoded;
                }
            }
        }
        return null;
    }

    private BridgeFrame? ReadAt(PixelFrame frame, int x, int y, int size)
    {
        if (x + Columns * size > frame.Width || y + Rows * size > frame.Height)
            return null;
        (byte R, byte G, byte B) Cell(int index) => Pixel(frame,
            x + index % Columns * size + size / 2, y + index / Columns * size + size / 2);
        for (var i = 0; i < Markers.Length; i++)
        {
            var color = Cell(i);
            var expected = Markers[i];
            if (!Match(color.R, expected.R) || !Match(color.G, expected.G) || !Match(color.B, expected.B))
                return null;
        }
        var encodingMarker = Cell(7);
        var grayscale = Match(encodingMarker.R, false) && Match(encodingMarker.G, true) && Match(encodingMarker.B, false);
        if (!grayscale && !(Match(encodingMarker.R, true) && Match(encodingMarker.G, false) && Match(encodingMarker.B, true)))
            return null;
        var red = new byte[16];
        var green = new byte[16];
        var palette = new (byte R, byte G, byte B)[16];
        for (var i = 0; i < 16; i++)
        {
            var color = Cell(8 + i);
            palette[i] = color;
            red[i] = color.R;
            green[i] = color.G;
            if (i > 0 && (red[i] - red[i - 1] < 3 || green[i] - green[i - 1] < 3))
                return Reject(1, "Pixel bridge brightness levels are clipped or unreadable; reload the latest addon and keep the patch clear");
        }
        var chunkSize = grayscale ? GrayscaleChunkSize : ChunkSize;
        var data = new byte[36 + chunkSize];
        var darkBlue = Cell(8).B;
        for (var i = 0; i < data.Length; i++)
        {
            var color = Cell(32 + i * (grayscale ? 2 : 1));
            if (grayscale)
                data[i] = (byte)(Nearest(color, palette) * 16 + Nearest(Cell(33 + i * 2), palette));
            else
            {
                if (color.B > darkBlue + 40)
                    return Reject(2, "Pixel bridge data is obscured or distorted; check patch visibility and display color settings");
                data[i] = (byte)(Nearest(color.R, red) * 16 + Nearest(color.G, green));
            }
            // A heartbeat has no chunk; unused trailing cells need not be decoded.
            if (i == 35)
            {
                var chunkLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(20));
                if (chunkLength > chunkSize)
                    return Reject(2, "Pixel bridge header is unreadable; reload the latest addon and keep the patch clear");
                Array.Resize(ref data, 36 + chunkLength);
            }
        }
        var version = data[4];
        var contextPacket = version >= 4 && data[34] == 1;
        if (!data.AsSpan(0, 4).SequenceEqual("C64W"u8) || (grayscale ? version < 3 : version is not (1 or 2)))
            return Reject(2, "Pixel bridge header is unreadable; reload the latest addon and keep the patch clear");
        var expectedChecksum = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(26));
        var checksumBytes = new byte[data.Length - 4];
        data.AsSpan(0, 26).CopyTo(checksumBytes);
        data.AsSpan(30).CopyTo(checksumBytes.AsSpan(26));
        if (Adler32(checksumBytes) != expectedChecksum)
            return Reject(3, grayscale ? "Pixel bridge checksum mismatch; keep the patch clear or try /c64 bridge 3"
                : "Legacy color bridge checksum mismatch; update the addon for HDR-compatible grayscale packets");
        if (version > CurrentVersion)
            return Reject(4, $"WoW bridge v{version} requires a newer C64 companion. This build supports up to v{CurrentVersion}.");
        if (data[5] > (version >= 4 ? 63 : 31) || (!contextPacket && data[34] != 0) || data[35] != 0)
            return Reject(4, "Pixel bridge header is invalid; reload the latest addon and keep the patch clear");
        var total = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(18));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(16));
        var index = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14));
        if (total is < 1 or > 8192 || count != (total + chunkSize - 1) / chunkSize
            || index != ushort.MaxValue && (contextPacket || index >= count || data.Length - 36 != Math.Min(chunkSize, total - index * chunkSize))
            || index == ushort.MaxValue && !contextPacket && data.Length != 36)
            return Reject(4, "Pixel bridge packet dimensions are invalid; reload the addon");
        var chunk = data[36..];
        BridgeContext? context = null;
        if (contextPacket)
        {
            try { context = ReadContext(chunk); }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or DecoderFallbackException)
            {
                return Reject(4, "Pixel bridge context packet is invalid; reload the addon");
            }
        }
        Status = "Receiving status stream";
        return new BridgeFrame(data[5], BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(12)), index, count, total,
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(22)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(30)), contextPacket ? [] : chunk, version)
        {
            Context = context, PayloadLength = chunk.Length
        };
    }

    private static BridgeContext ReadContext(byte[] data)
    {
        using var reader = new BinaryReader(new MemoryStream(data), new UTF8Encoding(false, true));
        var flags = reader.ReadByte();
        if (flags > 31 || (flags & 3) == 3) throw new InvalidDataException("Invalid context flags.");
        bool? dead = (flags & 3) switch { 1 => false, 2 => true, _ => null };
        var level = (flags & 4) != 0 ? new BridgeNotice<ushort>(reader.ReadUInt32(), reader.ReadUInt16()) : null;
        var quest = (flags & 8) != 0 ? ReadName(54) : null;
        var zone = (flags & 16) != 0 ? ReadName(36) : null;
        if (reader.BaseStream.Position != reader.BaseStream.Length || level?.Value == 0)
            throw new InvalidDataException("Invalid context payload.");
        return new BridgeContext(dead, level, quest, zone);

        BridgeNotice<string> ReadName(int maxBytes)
        {
            var at = reader.ReadUInt32();
            var length = reader.ReadByte();
            if (length == 0 || length > maxBytes) throw new InvalidDataException("Invalid context name length.");
            var bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return new BridgeNotice<string>(at, new UTF8Encoding(false, true).GetString(bytes));
        }
    }

    private BridgeFrame? Reject(int stage, string message)
    {
        if (stage > failureStage)
        {
            failureStage = stage;
            Status = message;
        }
        return null;
    }

    public static uint Adler32(ReadOnlySpan<byte> data)
    {
        uint a = 1, b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }
        return b * 65536 + a;
    }

    private static bool Match(byte value, bool high) => high ? value > 150 : value < 100;

    private static int Nearest(byte value, byte[] palette)
    {
        var best = 0;
        for (var i = 1; i < palette.Length; i++)
            if (Math.Abs(value - palette[i]) < Math.Abs(value - palette[best])) best = i;
        return best;
    }

    private static int Nearest((byte R, byte G, byte B) color, (byte R, byte G, byte B)[] palette)
    {
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < palette.Length; i++)
        {
            var dr = color.R - palette[i].R;
            var dg = color.G - palette[i].G;
            var db = color.B - palette[i].B;
            var distance = dr * dr + dg * dg + db * db;
            if (distance >= bestDistance) continue;
            best = i;
            bestDistance = distance;
        }
        return best;
    }

    private static (byte R, byte G, byte B) Pixel(PixelFrame frame, int x, int y)
    {
        var offset = (y * frame.Width + x) * 4;
        return (frame.Bgra[offset + 2], frame.Bgra[offset + 1], frame.Bgra[offset]);
    }
}

public sealed class SnapshotAssembler
{
    private (uint Session, ushort Id, uint Checksum, ushort Length, byte Version)? identity;
    private byte[][] chunks = [];
    private StatusSnapshot? complete;

    public StatusSnapshot? Accept(BridgeFrame frame)
    {
        var nextIdentity = (frame.Session, frame.SnapshotId, frame.SnapshotChecksum, frame.TotalLength, frame.Version);
        if (identity != nextIdentity)
        {
            identity = nextIdentity;
            chunks = new byte[frame.ChunkCount][];
            complete = null;
        }
        if (frame.ChunkIndex != ushort.MaxValue)
            chunks[frame.ChunkIndex] = frame.Chunk;
        if (complete is null && chunks.All(chunk => chunk is not null))
        {
            var data = chunks.SelectMany(chunk => chunk).ToArray();
            if (data.Length != frame.TotalLength || PixelProtocol.Adler32(data) != frame.SnapshotChecksum)
                return null;
            try { complete = ReadSnapshot(data, frame.Version); }
            catch (Exception ex) when (ex is InvalidDataException or DecoderFallbackException or ArgumentOutOfRangeException)
            {
                return null;
            }
        }
        if (complete is null) return null;
        return complete with
        {
            InCombat = frame.InCombat, Live = frame.AuraSnapshotCurrent, Partial = frame.Partial, Truncated = frame.Truncated,
            Status = frame.InCombat ? "PAUSED - IN COMBAT" : frame.AuraSnapshotCurrent ? "LIVE" : "PAUSED - DATA UNAVAILABLE"
        };
    }

    private static StatusSnapshot ReadSnapshot(byte[] data, byte version)
    {
        var offset = 0;
        var encoding = new UTF8Encoding(false, true);
        byte ReadByte()
        {
            if (offset >= data.Length) throw new InvalidDataException("Short status packet.");
            return data[offset++];
        }
        uint ReadUInt(int count)
        {
            uint value = 0;
            for (var i = 0; i < count; i++) value |= (uint)ReadByte() << (8 * i);
            return value;
        }
        string ReadText(int length)
        {
            if (length > data.Length - offset) throw new InvalidDataException("Short status text.");
            var text = encoding.GetString(data, offset, length);
            offset += length;
            return text;
        }
        var count = ReadByte();
        if (count > 64) throw new InvalidDataException("Too many effects.");
        var effects = new List<StatusEffect>();
        for (var i = 0; i < count; i++)
        {
            var spellId = ReadUInt(4);
            var instanceId = version >= 4 ? ReadUInt(4) : 0;
            var flags = ReadByte();
            var nameLength = ReadByte();
            var expiry = ReadUInt(4);
            var applications = ReadUInt(2);
            var duration = version >= 2 ? ReadUInt(4) : 0;
            var descriptionCount = ReadByte();
            if (flags > (version >= 2 ? 31 : 15) || nameLength > 160 || descriptionCount > 24) throw new InvalidDataException("Invalid effect.");
            var name = ReadText(nameLength);
            var descriptions = new List<string>();
            for (var j = 0; j < descriptionCount; j++)
            {
                var length = (int)ReadUInt(2);
                if (length > 512) throw new InvalidDataException("Oversized description.");
                descriptions.Add(ReadText(length));
            }
            effects.Add(new StatusEffect { Name = name, SpellId = spellId == 0 ? null : spellId,
                AuraInstanceId = instanceId == 0 ? null : instanceId,
                Harmful = (flags & 1) != 0, Permanent = (flags & 2) != 0,
                Applications = (flags & 8) != 0 ? applications : null,
                Duration = version >= 2 && (flags & 16) != 0 ? duration / 1000d : null,
                ExpiresAtMilliseconds = (flags & 4) != 0 ? expiry : null, Description = descriptions });
        }
        if (offset != data.Length) throw new InvalidDataException("Trailing status bytes.");
        return new StatusSnapshot { Auras = effects };
    }
}

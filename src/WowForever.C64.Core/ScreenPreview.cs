using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace WowForever.C64.Core;

public static class ScreenPreview
{
    // A fixed C64 palette; the physical Ultimate's configured palette can differ.
    private static readonly uint[] Palette =
    [
        0x000000, 0xFFFFFF, 0x813338, 0x75CEC8, 0x8E3C97, 0x56AC4D, 0x2E2C9B, 0xEDF171,
        0x8E5029, 0x553800, 0xC46C71, 0x4A4A4A, 0x7B7B7B, 0xA9FF9F, 0x706DEB, 0xB2B2B2
    ];

    public static void SavePng(string path, TextScreen screen, int scale = 3)
    {
        ArgumentNullException.ThrowIfNull(screen);
        if (scale is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(scale));
        if (screen.BackgroundColor > 15 || screen.BorderColor > 15) throw new ArgumentException("Invalid screen palette.");
        const int border = 16;
        var width = (320 + border * 2) * scale;
        var height = (200 + border * 2) * scale;
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (var x = 0; x < width; x++)
            {
                var px = x / scale - border;
                var py = y / scale - border;
                var color = screen.BorderColor;
                if (px is >= 0 and < 320 && py is >= 0 and < 200)
                {
                    var cell = screen.GetCell(px / 8, py / 8);
                    var bits = screen.CharacterSet.Row(cell.Code, py % 8);
                    color = (bits & (128 >> (px % 8))) != 0 ? cell.Foreground : screen.BackgroundColor;
                }
                var rgb = Palette[color];
                raw.WriteByte((byte)(rgb >> 16));
                raw.WriteByte((byte)(rgb >> 8));
                raw.WriteByte((byte)rgb);
            }
        }
        using var packed = new MemoryStream();
        using (var compressor = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true))
            raw.WriteTo(compressor);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var output = File.Create(fullPath);
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;
        Chunk(output, "IHDR", header);
        Chunk(output, "IDAT", packed.ToArray());
        Chunk(output, "IEND", []);
    }

    public static TextScreen CharsetAtlas(CharacterSet charset)
    {
        var screen = new TextScreen(charset);
        screen.Center(1, "FANTASY CHARSET - HEX CODEPOINTS", 8);
        for (var column = 0; column < 16; column++) screen.Write(4 + column * 2, 3, column.ToString("X"), 8);
        for (var row = 0; row < 16; row++)
        {
            screen.Write(0, 5 + row, $"{row:X}0", 8);
            for (var column = 0; column < 16; column++)
                screen.SetCell(4 + column * 2, 5 + row, new CharacterCell((byte)(row * 16 + column), 15));
        }
        screen.Center(23, "80-FF: INVERSE VERSIONS OF 00-7F", 8);
        return screen;
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        var tag = Encoding.ASCII.GetBytes(type);
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        output.Write(tag);
        output.Write(data);
        uint crc = uint.MaxValue;
        foreach (var value in tag.Concat(data))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        output.Write(number);
    }
}

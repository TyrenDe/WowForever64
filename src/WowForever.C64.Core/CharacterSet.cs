namespace WowForever.C64.Core;

public sealed class CharacterSet
{
    public const int GlyphCount = 256;
    public const int BytesPerGlyph = 8;
    public const int ByteCount = GlyphCount * BytesPerGlyph;
    private readonly byte[] data;

    private CharacterSet(byte[] data) => this.data = data;

    public static CharacterSet FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteCount)
            throw new ArgumentException("A C64 charset must contain exactly 2048 bytes.", nameof(bytes));
        return new CharacterSet(bytes.ToArray());
    }

    public static CharacterSet Load(string path) => FromBytes(File.ReadAllBytes(path));
    public byte[] ToArray() => data.ToArray();

    public byte Row(byte code, int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, BytesPerGlyph);
        return data[code * BytesPerGlyph + row];
    }
}

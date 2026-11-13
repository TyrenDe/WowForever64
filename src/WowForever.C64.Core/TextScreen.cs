namespace WowForever.C64.Core;

public readonly record struct CharacterCell(byte Code, byte Foreground, TileId? Tile = null);

public sealed class TextScreen
{
    public const int Width = 40;
    public const int Height = 25;
    public const int CellCount = Width * Height;
    private readonly CharacterCell[] cells = Enumerable.Repeat(new CharacterCell(32, 15), CellCount).ToArray();

    public TextScreen(CharacterSet? characterSet = null) => CharacterSet = characterSet ?? FantasyCharset.Default;

    public CharacterSet CharacterSet { get; }
    public byte BorderColor { get; init; } = 11;
    public byte BackgroundColor { get; init; } = 0;
    public ReadOnlyMemory<CharacterCell> Cells => cells;

    public CharacterCell GetCell(int column, int row) => cells[Index(column, row)];

    public void SetCell(int column, int row, CharacterCell cell)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cell.Foreground, (byte)15);
        cells[Index(column, row)] = cell;
    }

    public void SetTile(int column, int row, TileId tile, byte foreground) =>
        SetCell(column, row, new CharacterCell((byte)tile, foreground, tile));

    public void Write(int column, int row, string text, byte foreground = 15, bool reverse = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        Index(column, row);
        for (var i = 0; i < Math.Min(text.Length, Width - column); i++)
        {
            var code = ScreenCode.Encode(text[i]);
            var semantic = code < 32 || code >= 96 ? (TileId?)code : null;
            SetCell(column + i, row, new CharacterCell((byte)(code | (reverse ? 128 : 0)), foreground, semantic));
        }
    }

    public void Center(int row, string text, byte foreground = 15, bool reverse = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        Write(Math.Max(0, (Width - text.Length) / 2), row, text, foreground, reverse);
    }

    public byte[] ToScreenCodes() => cells.Select(cell => cell.Code).ToArray();
    public byte[] ToColorRam() => cells.Select(cell => cell.Foreground).ToArray();
    public string ToText() => string.Join(Environment.NewLine, Enumerable.Range(0, Height)
        .Select(row => new string(cells.Skip(row * Width).Take(Width).Select(cell => ScreenCode.Decode(cell.Code)).ToArray())));

    private static int Index(int column, int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Height);
        return row * Width + column;
    }
}

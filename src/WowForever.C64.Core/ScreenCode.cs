namespace WowForever.C64.Core;

public static class ScreenCode
{
    private static readonly Dictionary<char, TileId> Symbols = new()
    {
        ['─'] = TileId.Separator, ['│'] = TileId.PanelLeft,
        ['┌'] = TileId.PanelTopLeft, ['┐'] = TileId.PanelTopRight,
        ['└'] = TileId.PanelBottomLeft, ['┘'] = TileId.PanelBottomRight,
        ['♦'] = TileId.Diamond, ['♥'] = TileId.Heart, ['█'] = TileId.BarFull,
        ['←'] = TileId.ArrowLeft, ['£'] = TileId.Pound
    };

    public static char Normalize(char value)
    {
        if (value is >= 'a' and <= 'z') return (char)(value - 'a' + 'A');
        if (value == '↑') return '^';
        return value is >= ' ' and <= '_' || Symbols.ContainsKey(value) ? value : '?';
    }

    public static byte Encode(char value)
    {
        var normalized = Normalize(value);
        return Symbols.TryGetValue(normalized, out var tile) ? (byte)tile : (byte)normalized;
    }

    public static char Decode(byte code)
    {
        if (code == (byte)TileId.DownArrow) return '↓';
        var plain = (byte)(code & 127);
        if (plain == (byte)TileId.UpArrow) return '↑';
        if (plain is >= 32 and <= 95) return (char)plain;
        return (TileId)plain switch
        {
            TileId.FrameTopLeft or TileId.PanelTopLeft => '┌',
            TileId.FrameTopRight or TileId.PanelTopRight => '┐',
            TileId.FrameBottomLeft or TileId.PanelBottomLeft => '└',
            TileId.FrameBottomRight or TileId.PanelBottomRight => '┘',
            TileId.FrameLeft or TileId.FrameRight or TileId.PanelLeft or TileId.PanelRight => '│',
            TileId.FrameTop or TileId.FrameBottom or TileId.PanelTop or TileId.PanelBottom
                or TileId.Separator or TileId.SeparatorLeft or TileId.SeparatorRight => '─',
            TileId.Diamond or TileId.HollowDiamond or TileId.Live => '♦',
            TileId.Heart => '♥', TileId.Pound => '£', TileId.ArrowLeft => '←',
            >= TileId.BarEmpty and <= TileId.BarFull => '█',
            _ => '·'
        };
    }
}

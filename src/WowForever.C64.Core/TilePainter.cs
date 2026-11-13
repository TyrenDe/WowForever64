namespace WowForever.C64.Core;

public enum LargeIcon { Campfire, Shield, Sword, Skull }

public static class TilePainter
{
    public static void Border(TextScreen screen, int x, int y, int width, int height, byte color = 9, bool panel = false)
    {
        Rectangle(x, y, width, height);
        if (width < 2 || height < 2) throw new ArgumentException("Borders require at least 2 by 2 cells.");
        var topLeft = panel ? TileId.PanelTopLeft : TileId.FrameTopLeft;
        var top = panel ? TileId.PanelTop : TileId.FrameTop;
        var topRight = panel ? TileId.PanelTopRight : TileId.FrameTopRight;
        var left = panel ? TileId.PanelLeft : TileId.FrameLeft;
        var right = panel ? TileId.PanelRight : TileId.FrameRight;
        var bottomLeft = panel ? TileId.PanelBottomLeft : TileId.FrameBottomLeft;
        var bottom = panel ? TileId.PanelBottom : TileId.FrameBottom;
        var bottomRight = panel ? TileId.PanelBottomRight : TileId.FrameBottomRight;
        for (var column = x + 1; column < x + width - 1; column++)
        {
            screen.SetTile(column, y, top, color);
            screen.SetTile(column, y + height - 1, bottom, color);
        }
        for (var row = y + 1; row < y + height - 1; row++)
        {
            screen.SetTile(x, row, left, color);
            screen.SetTile(x + width - 1, row, right, color);
        }
        screen.SetTile(x, y, topLeft, color);
        screen.SetTile(x + width - 1, y, topRight, color);
        screen.SetTile(x, y + height - 1, bottomLeft, color);
        screen.SetTile(x + width - 1, y + height - 1, bottomRight, color);
    }

    public static void Panel(TextScreen screen, int x, int y, int width, int height, byte color = 8)
    {
        Rectangle(x, y, width, height);
        for (var row = y; row < y + height; row++)
            for (var column = x; column < x + width; column++) screen.SetCell(column, row, new CharacterCell(32, 15));
        Border(screen, x, y, width, height, color, panel: true);
    }

    public static void Separator(TextScreen screen, int x, int y, int width, byte color = 9)
    {
        Rectangle(x, y, width, 1);
        if (width < 2) throw new ArgumentException("Separators require two cells.");
        for (var column = x; column < x + width; column++) screen.SetTile(column, y, TileId.Separator, color);
        screen.SetTile(x, y, TileId.SeparatorLeft, color);
        screen.SetTile(x + width - 1, y, TileId.SeparatorRight, color);
    }

    public static void Header(TextScreen screen, int row, string text, byte color = 8)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 32) throw new ArgumentException("Ornamented headers fit 32 characters.");
        var left = (TextScreen.Width - text.Length) / 2;
        screen.Write(left, row, text, color);
        screen.SetTile(left - 2, row, TileId.TitleLeft, color);
        screen.SetTile(left + text.Length + 1, row, TileId.TitleRight, color);
    }

    public static void StatusIndicator(TextScreen screen, int x, int y, bool live, bool combat) =>
        screen.SetTile(x, y, live ? TileId.Live : combat ? TileId.Paused : TileId.Lost, live ? (byte)13 : (byte)10);

    public static void Icon(TextScreen screen, int x, int y, LargeIcon icon, byte color = 8)
    {
        Rectangle(x, y, 2, 2);
        var first = icon switch
        {
            LargeIcon.Campfire => TileId.CampTopLeft, LargeIcon.Shield => TileId.ShieldTopLeft,
            LargeIcon.Sword => TileId.SwordTopLeft, LargeIcon.Skull => TileId.SkullTopLeft,
            _ => throw new ArgumentOutOfRangeException(nameof(icon))
        };
        for (var row = 0; row < 2; row++)
            for (var column = 0; column < 2; column++) screen.SetTile(x + column, y + row, (TileId)((byte)first + row * 2 + column), color);
    }

    public static void ProgressBar(TextScreen screen, int x, int y, int width, double fraction, byte fill = 13, byte outline = 9)
    {
        Rectangle(x, y, width, 1);
        if (width < 3 || !double.IsFinite(fraction)) throw new ArgumentException("Progress bars require three cells and a finite fraction.");
        var pixels = (int)Math.Round(Math.Clamp(fraction, 0, 1) * (width - 2) * 8);
        screen.SetTile(x, y, TileId.BarLeft, outline);
        screen.SetTile(x + width - 1, y, TileId.BarRight, outline);
        for (var i = 0; i < width - 2; i++)
        {
            var amount = Math.Clamp(pixels - i * 8, 0, 8);
            screen.SetTile(x + 1 + i, y, (TileId)((byte)TileId.BarEmpty + amount), amount > 0 ? fill : outline);
        }
    }

    private static void Rectangle(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width < 1 || height < 1 || width > TextScreen.Width - x || height > TextScreen.Height - y)
            throw new ArgumentOutOfRangeException(nameof(width), "Drawing rectangle must fit the 40 by 25 screen.");
    }
}

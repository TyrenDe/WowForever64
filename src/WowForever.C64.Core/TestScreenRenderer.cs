namespace WowForever.C64.Core;

public static class TestScreenRenderer
{
    public const int MessageRows = 10;
    public const int MaximumMessageLength = 400;

    public static TextScreen Render(string message, CharacterSet? charset = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Length > MaximumMessageLength)
            throw new ArgumentException("Message must contain at most 400 characters.", nameof(message));

        var screen = new TextScreen(charset);
        screen.Center(1, "WOW FOREVER STATUS");
        screen.Center(3, "C64 ULTIMATE - NETWORK TEST");
        screen.Write(0, 5, new string('-', TextScreen.Width));

        var row = 7;
        foreach (var line in message.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var chunks = Math.Max(1, (line.Length + TextScreen.Width - 1) / TextScreen.Width);
            for (var chunk = 0; chunk < chunks; chunk++)
            {
                if (row >= 7 + MessageRows)
                    throw new ArgumentException("Message must fit within ten 40-column rows.", nameof(message));
                var start = chunk * TextScreen.Width;
                screen.Write(0, row++, line.Substring(start, Math.Min(TextScreen.Width, line.Length - start)));
            }
        }

        screen.Write(0, 19, new string('-', TextScreen.Width));
        screen.Write(0, 21, "SOURCE: WINDOWS PC / REST API");
        screen.Write(0, 23, "PHASE 1 - NO WOW CONNECTION");
        screen.Write(0, 24, "40 COLUMNS X 25 ROWS");
        return screen;
    }
}

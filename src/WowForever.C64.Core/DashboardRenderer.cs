using System.Globalization;
using System.Text.RegularExpressions;

namespace WowForever.C64.Core;

public static class DashboardRenderer
{
    private const byte Bronze = 8, Iron = 9, Parchment = 15, Red = 10, Green = 13, Silver = 1, Cyan = 3;
    private const int AuraRows = 9, CampRows = 5;

    public static TextScreen Render(StatusSnapshot? snapshot, bool connected = true,
        double elapsedSeconds = 0, int page = 0, uint? clientClockMilliseconds = null, CharacterSet? charset = null)
    {
        var screen = new TextScreen(charset) { BorderColor = 11, BackgroundColor = 0 };
        var live = connected && snapshot?.Live == true && snapshot.InCombat != true;
        var harmful = snapshot?.Auras.Any(effect => effect.Harmful) == true;
        TilePainter.Border(screen, 0, 0, 40, 25, Iron);
        TilePainter.Header(screen, 1, "WORLD OF WARCRAFT", Silver);
        screen.SetTile(9, 1, TileId.TitleLeft, Cyan);
        screen.SetTile(29, 1, TileId.TitleRight, Cyan);
        TilePainter.Icon(screen, 2, 1, LargeIcon.Shield, Bronze);
        TilePainter.Icon(screen, 36, 1, harmful ? LargeIcon.Skull : LargeIcon.Sword, harmful ? Red : Bronze);
        for (var column = 7; column <= 32; column++) screen.SetTile(column, 2, TileId.Chain, Iron);
        screen.SetTile(19, 2, TileId.Rune, Cyan);
        TilePainter.Separator(screen, 0, 3, 40, Iron);

        var status = snapshot is null ? "WAITING FOR WOW"
            : !connected ? "SIGNAL LOST - TIMES FROZEN"
            : snapshot.InCombat == true ? "PAUSED - IN COMBAT"
            : !snapshot.Live ? "PAUSED - DATA UNAVAILABLE"
            : snapshot.Truncated ? "SOME EFFECTS OMITTED"
            : snapshot.Partial ? "PARTIAL DATA" : null;
        if (status is not null)
        {
            screen.Write(6, 2, new string(' ', 28));
            TilePainter.StatusIndicator(screen, 5, 2, false, snapshot?.InCombat == true);
            screen.Center(2, status, Red);
        }

        var camp = snapshot?.Auras.FirstOrDefault(effect => effect.SpellId == 1229741);
        TilePainter.Panel(screen, 1, 4, 38, 10, Bronze);
        TilePainter.Icon(screen, 3, 5, LargeIcon.Campfire, camp is null ? (byte)11 : Bronze);
        if (camp is not null)
        {
            screen.SetTile(4, 5, TileId.CampTopRight, 7);
            screen.SetTile(3, 6, TileId.CampBottomLeft, Iron);
        }
        screen.Write(6, 5, Fit(camp?.Name ?? "CAMP BENEFITS", 21), Silver);
        screen.SetTile(28, 5, TileId.Hourglass, Iron);
        screen.Write(29, 5, Timer(camp, live, elapsedSeconds, clientClockMilliseconds).PadLeft(8), TimerColor(camp, live));
        var remaining = camp is null ? null : Remaining(camp, elapsedSeconds, clientClockMilliseconds);
        var fraction = live && remaining is { } seconds && camp?.Duration is > 0 ? seconds / camp.Duration.Value : 0;
        TilePainter.ProgressBar(screen, 6, 6, 31, fraction, live ? Green : (byte)11, Iron);
        TilePainter.Separator(screen, 2, 7, 36, Iron);

        var details = CampDetails(camp);
        var detailPages = Math.Max(1, (details.Count + CampRows - 1) / CampRows);
        var visibleDetails = details.Skip(PositivePage(page, detailPages) * CampRows).Take(CampRows).ToList();
        for (var i = 0; i < visibleDetails.Count; i++)
        {
            var detail = visibleDetails[i];
            if (detail.Start) screen.SetTile(3, 8 + i, camp?.Harmful == true ? TileId.Skull : TileId.UpArrow, camp?.Harmful == true ? Red : Green);
            screen.Write(5, 8 + i, detail.Text, Parchment);
        }

        TilePainter.Separator(screen, 0, 14, 40, Iron);
        TilePainter.Header(screen, 14, "AURAS & AFFLICTIONS", Silver);
        var effects = snapshot?.Auras.Where(effect => effect != camp).OrderByDescending(effect => effect.Harmful).ToList() ?? [];
        var auraPages = Math.Max(1, (effects.Count + AuraRows - 1) / AuraRows);
        var visible = effects.Skip(PositivePage(page, auraPages) * AuraRows).Take(AuraRows).ToList();
        for (var i = 0; i < visible.Count; i++)
        {
            var effect = visible[i];
            var color = effect.Harmful ? Red : Green;
            var row = 15 + i;
            screen.SetTile(2, row, TileId.Diamond, color);
            screen.SetTile(3, row, effect.Harmful ? TileId.Skull : effect.Permanent ? TileId.Shield : TileId.Sparkle, color);
            var name = effect.Name + (effect.Applications is > 1 ? " X" + effect.Applications.Value : "");
            screen.Write(5, row, Fit(name, 21), effect.Harmful ? Red : Parchment);
            screen.SetTile(27, row, effect.Permanent ? TileId.Live : TileId.Hourglass, Iron);
            screen.Write(29, row, Timer(effect, live, elapsedSeconds, clientClockMilliseconds).PadLeft(8), TimerColor(effect, live));
        }
        if (effects.Count == 0) screen.Write(4, 17, snapshot is null ? "AWAITING STATUS EFFECTS..." : "NO OTHER READABLE EFFECTS", Parchment);
        return screen;
    }

    private static List<(string Text, bool Start)> CampDetails(StatusEffect? camp)
    {
        if (camp is null) return [("NO CAMP BENEFITS", false)];
        var descriptions = camp.Description
            .SelectMany(text => text.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Where(text => !text.EndsWith(':') && !IsTooltipTimer(text)).ToList();
        var labeled = descriptions.Where(text => text.Contains(':')).ToList();
        if (labeled.Count > 0) descriptions = labeled;
        var lines = new List<(string Text, bool Start)>();
        foreach (var text in descriptions)
        {
            var first = true;
            foreach (var wrapped in Wrap(CompactBenefit(text), 31))
            {
                lines.Add((wrapped, first));
                first = false;
            }
        }
        if (lines.Count == 0) lines.Add(("BENEFIT DETAILS UNAVAILABLE", false));
        return lines;
    }

    private static bool IsTooltipTimer(string text)
    {
        var options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        var plain = Regex.Replace(text, @"\|4([^:;]*):([^;]*);", "$2");
        return Regex.IsMatch(plain,
            @"^(?:(?:\d+(?:[.,]\d+)?\s*)?(?:days?|hours?|hrs?|minutes?|mins?|seconds?|secs?);?\s+)+remaining[.!]?$", options);
    }

    private static string CompactBenefit(string text)
    {
        var colon = text.IndexOf(':');
        if (colon >= 0) text = text[(colon + 1)..].Trim();
        const string amount = @"(?<amount>\d+(?:[.,]\d+)?\s*%?)";
        var options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        var match = Regex.Match(text,
            @"^(?<stat>.+?)\s+(?:is\s+)?(?<verb>increased|improved|decreased|reduced)\s+by\s+" + amount + @"\.?$", options);
        if (!match.Success)
            match = Regex.Match(text,
                @"^(?<verb>increases|improves|decreases|reduces)\s+(?<stat>.+?)\s+by\s+" + amount + @"\.?$", options);
        if (!match.Success) return text;
        var verb = match.Groups["verb"].Value;
        var sign = verb.StartsWith("decreas", StringComparison.OrdinalIgnoreCase)
            || verb.StartsWith("reduc", StringComparison.OrdinalIgnoreCase) ? "-" : "+";
        return match.Groups["stat"].Value.Trim() + " " + sign + match.Groups["amount"].Value.Replace(" ", "");
    }

    private static byte TimerColor(StatusEffect? effect, bool live) =>
        effect is null ? Iron : !live ? Silver : effect.Harmful ? Red : effect.Permanent ? Green : Cyan;

    private static double? Remaining(StatusEffect effect, double elapsed, uint? clock) =>
        effect.ExpiresAtMilliseconds is { } expiry && clock is { } current
            ? Math.Max(0, unchecked((int)(expiry - current)) / 1000d - elapsed)
            : effect.Remaining is { } remaining ? Math.Max(0, remaining - elapsed) : null;

    private static string Timer(StatusEffect? effect, bool live, double elapsed, uint? clock)
    {
        if (effect is null) return "--";
        if (!live) return "FROZEN";
        if (effect.Permanent) return "ACTIVE";
        var seconds = Remaining(effect, elapsed, clock);
        if (seconds is null) return "?";
        var time = TimeSpan.FromSeconds(Math.Min(315360000, Math.Floor(seconds.Value)));
        if (time.TotalHours >= 100) return ((int)time.TotalHours).ToString(CultureInfo.InvariantCulture) + "H";
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";
    }

    private static int PositivePage(int page, int count) => (page % count + count) % count;
    private static string Fit(string text, int width)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ');
        return text.Length > width ? text[..(width - 1)] + "." : text;
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ');
        while (text.Length > width)
        {
            var split = text.LastIndexOf(' ', width, width);
            if (split < 1) split = width;
            yield return text[..split];
            text = text[split..].TrimStart();
        }
        if (text.Length > 0) yield return text;
    }
}

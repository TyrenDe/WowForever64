using System.Globalization;
using System.Text.RegularExpressions;

namespace WowForever.C64.Core;

public static class DashboardRenderer
{
    private const byte Bronze = 8, Iron = 9, Parchment = 15, Red = 10, Green = 13, Silver = 1, Cyan = 3,
        Amber = 8, Gold = 7, Gray = 12;
    private const int AuraRows = 9, CampRows = 5;

    public static TextScreen Render(StatusSnapshot? snapshot, bool connected = true,
        double elapsedSeconds = 0, int page = 0, uint? clientClockMilliseconds = null, CharacterSet? charset = null,
        DashboardNotice? diagnostic = null)
    {
        var feed = !connected ? DashboardFeed.Disconnected : snapshot is null ? DashboardFeed.Waiting
            : snapshot.Live && snapshot.InCombat != true ? DashboardFeed.Live : DashboardFeed.Unavailable;
        var effects = snapshot?.Auras.Select(effect =>
        {
            var remaining = Remaining(effect, elapsedSeconds, clientClockMilliseconds);
            return new DashboardAura(effect, remaining, feed != DashboardFeed.Live || remaining == 0);
        }).ToList();
        return RenderView(new DashboardView(effects, feed, snapshot?.InCombat == true,
            snapshot?.Partial == true, snapshot?.Truncated == true, diagnostic), page, charset, diagnostic is not null);
    }

    public static TextScreen RenderView(DashboardView view, int page = 0, CharacterSet? charset = null, bool diagnostic = false)
    {
        var screen = new TextScreen(charset) { BorderColor = 11, BackgroundColor = 0 };
        var harmful = view.Auras?.Any(aura => aura.Effect.Harmful) == true || view.Notice?.Kind == NoticeKind.Death;
        TilePainter.Border(screen, 0, 0, 40, 25, Iron);
        TilePainter.Header(screen, 1, "WORLD OF WARCRAFT", Silver);
        screen.SetTile(9, 1, TileId.TitleLeft, Cyan);
        screen.SetTile(29, 1, TileId.TitleRight, Cyan);
        TilePainter.Icon(screen, 2, 1, LargeIcon.Shield, Bronze);
        TilePainter.Icon(screen, 36, 1, harmful ? LargeIcon.Skull : LargeIcon.Sword, harmful ? Red : Bronze);
        for (var column = 7; column <= 32; column++) screen.SetTile(column, 2, TileId.Chain, Iron);
        screen.SetTile(19, 2, TileId.Rune, Cyan);
        TilePainter.Separator(screen, 0, 3, 40, Iron);

        DrawStatus(screen, view, diagnostic);
        if (view.Notice is { } notice)
        {
            DrawNotice(screen, notice);
            return screen;
        }

        if (view.Auras is { } auras)
            DrawAuras(screen, view, auras, page);
        else
            screen.Center(12, view.Feed == DashboardFeed.Waiting ? "WAITING FOR WOW" : "WAITING FOR AURAS", Parchment);

        return screen;
    }

    private static void DrawStatus(TextScreen screen, DashboardView view, bool diagnostic)
    {
        var (status, color) = view.Feed switch
        {
            _ when diagnostic => ("DEMO", Cyan),
            DashboardFeed.Disconnected => ("SIGNAL LOST", Red),
            _ when view.InCombat => ("IN COMBAT", Red),
            _ when view.AddonUpdateNeeded => ("ADDON UPDATE NEEDED", Amber),
            DashboardFeed.Unavailable => (view.Auras is null ? "DATA UNAVAILABLE" : "AURA DATA STALE", Amber),
            _ when view.Truncated => ("SOME EFFECTS OMITTED", Amber),
            _ when view.Partial => ("PARTIAL DATA", Amber),
            _ => ((string?)null, Silver)
        };
        if (status is not null)
        {
            screen.Write(6, 2, new string(' ', 28), Gray);
            screen.Center(2, status, color);
        }
        if (view.InCombat) screen.SetTile(5, 2, TileId.Sword, Red);
        if (view.Feed == DashboardFeed.Disconnected) screen.SetTile(34, 2, TileId.Lost, Red);
        else if (view.AddonUpdateNeeded || (view.Feed == DashboardFeed.Unavailable && !view.InCombat) || view.Partial || view.Truncated)
            screen.SetTile(34, 2, TileId.HollowDiamond, Amber);
        else if (view.Feed == DashboardFeed.Cached)
            screen.SetTile(34, 2, TileId.HollowDiamond, Gray);
    }

    private static void DrawNotice(TextScreen screen, DashboardNotice notice)
    {
        if (notice.Kind == NoticeKind.Death)
        {
            TilePainter.Panel(screen, 6, 8, 28, 9, Red);
            TilePainter.Icon(screen, 19, 10, LargeIcon.Skull, Red);
            screen.Center(14, notice.Text, Red);
            return;
        }
        if (notice.Kind == NoticeKind.Quest)
        {
            TilePainter.Panel(screen, 2, 8, 36, 9, Green);
            TilePainter.Icon(screen, 19, 9, LargeIcon.Shield, Green);
            TilePainter.Header(screen, 12, "QUEST COMPLETE", Gold);
            screen.Center(14, Fit(notice.Text, 32), Parchment);
            return;
        }
        if (notice.Kind == NoticeKind.Zone)
        {
            TilePainter.Panel(screen, 2, 10, 36, 6, Iron);
            screen.SetTile(19, 11, TileId.Rune, Cyan);
            screen.Center(13, Fit(notice.Text, 32), Gold);
            return;
        }
        TilePainter.Panel(screen, 5, 7, 30, 11, Gold);
        TilePainter.Header(screen, 9, "LEVEL UP!", Gold);
        screen.SetTile(12, 13, TileId.Sparkle, Cyan);
        screen.SetTile(27, 13, TileId.Sparkle, Cyan);
        screen.Center(13, $"  {notice.Text}  ", Gold, reverse: true);
    }

    private static void DrawAuras(TextScreen screen, DashboardView view, IReadOnlyList<DashboardAura> auras, int page)
    {
        var absenceConfirmed = view.Feed == DashboardFeed.Live && !view.Partial && !view.Truncated;
        var camp = auras.FirstOrDefault(aura => aura.Effect.SpellId == 1229741);
        TilePainter.Panel(screen, 1, 4, 38, 10, Bronze);
        TilePainter.Icon(screen, 3, 5, LargeIcon.Campfire, camp is null ? (byte)11 : Bronze);
        if (camp is not null)
        {
            screen.SetTile(4, 5, TileId.CampTopRight, 7);
            screen.SetTile(3, 6, TileId.CampBottomLeft, Iron);
        }
        screen.Write(6, 5, Fit(camp?.Effect.Name ?? "CAMP BENEFITS", 21), Silver);
        screen.SetTile(28, 5, TileId.Hourglass, Iron);
        screen.Write(29, 5, Timer(camp).PadLeft(8), TimerColor(camp));
        var fraction = camp?.RemainingSeconds is { } seconds && camp.Effect.Duration is > 0
            ? Math.Ceiling(seconds) / camp.Effect.Duration.Value : 0;
        TilePainter.ProgressBar(screen, 6, 6, 31, fraction, camp?.Estimated == true ? Amber : Green, Iron);
        TilePainter.Separator(screen, 2, 7, 36, Iron);

        var details = CampDetails(camp?.Effect, absenceConfirmed);
        var detailPages = Math.Max(1, (details.Count + CampRows - 1) / CampRows);
        var visibleDetails = details.Skip(PositivePage(page, detailPages) * CampRows).Take(CampRows).ToList();
        for (var i = 0; i < visibleDetails.Count; i++)
        {
            var detail = visibleDetails[i];
            if (detail.Start && detail.Negative)
            {
                var down = (byte)TileId.DownArrow;
                if (Enumerable.Range(0, CharacterSet.BytesPerGlyph)
                    .All(row => screen.CharacterSet.Row(down, row) == FantasyCharset.Default.Row(down, row)))
                    screen.SetTile(3, 8 + i, TileId.DownArrow, Red);
                else
                    screen.Write(3, 8 + i, "-", Red);
            }
            else if (detail.Start) screen.SetTile(3, 8 + i, TileId.UpArrow, Green);
            screen.Write(5, 8 + i, detail.Text, detail.Negative ? Red : Parchment);
        }

        TilePainter.Separator(screen, 0, 14, 40, Iron);
        TilePainter.Header(screen, 14, "AURAS & AFFLICTIONS", Silver);
        var effects = auras.Where(aura => aura != camp).OrderByDescending(aura => aura.Effect.Harmful).ToList();
        var auraPages = Math.Max(1, (effects.Count + AuraRows - 1) / AuraRows);
        var visible = effects.Skip(PositivePage(page, auraPages) * AuraRows).Take(AuraRows).ToList();
        for (var i = 0; i < visible.Count; i++)
        {
            var aura = visible[i];
            var effect = aura.Effect;
            var color = effect.Harmful ? Red : Green;
            var row = 15 + i;
            screen.SetTile(2, row, TileId.Diamond, color);
            screen.SetTile(3, row, effect.Harmful ? TileId.Skull : effect.Permanent ? TileId.Shield : TileId.Sparkle, color);
            var name = effect.Name + (effect.Applications is > 1 ? " X" + effect.Applications.Value : "");
            screen.Write(5, row, Fit(name, 21), effect.Harmful ? Red : Parchment);
            screen.SetTile(27, row, effect.Permanent ? TileId.Live : TileId.Hourglass, Iron);
            screen.Write(29, row, Timer(aura).PadLeft(8), TimerColor(aura));
        }
        if (effects.Count == 0) screen.Write(4, 17, absenceConfirmed ? "NO OTHER EFFECTS" : "AURAS UNKNOWN",
            absenceConfirmed ? Parchment : Gray);
    }

    private static List<(string Text, bool Start, bool Negative)> CampDetails(StatusEffect? camp, bool absenceConfirmed)
    {
        if (camp is null) return [(absenceConfirmed ? "NO CAMP BENEFITS" : "CAMP BENEFITS UNKNOWN", false, false)];
        var descriptions = camp.Description
            .SelectMany(text => text.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Where(text => !text.EndsWith(':') && !IsTooltipTimer(text)).ToList();
        var labeled = descriptions.Where(text => text.Contains(':')).ToList();
        if (labeled.Count > 0) descriptions = labeled;
        var lines = new List<(string Text, bool Start, bool Negative)>();
        foreach (var text in descriptions)
        {
            var first = true;
            var compact = CompactBenefit(text, out var negative);
            foreach (var wrapped in Wrap(compact, 31))
            {
                lines.Add((wrapped, first, negative || camp.Harmful));
                first = false;
            }
        }
        if (lines.Count == 0) lines.Add(("BENEFIT DETAILS UNAVAILABLE", false, false));
        return lines;
    }

    private static bool IsTooltipTimer(string text)
    {
        var options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        var plain = Regex.Replace(text, @"\|4([^:;]*):([^;]*);", "$2");
        return Regex.IsMatch(plain,
            @"^(?:(?:\d+(?:[.,]\d+)?\s*)?(?:days?|hours?|hrs?|minutes?|mins?|seconds?|secs?);?\s+)+remaining[.!]?$", options);
    }

    private static string CompactBenefit(string text, out bool negative)
    {
        negative = false;
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
        negative = verb.StartsWith("decreas", StringComparison.OrdinalIgnoreCase)
            || verb.StartsWith("reduc", StringComparison.OrdinalIgnoreCase);
        var sign = negative ? "-" : "+";
        return match.Groups["stat"].Value.Trim() + " " + sign + match.Groups["amount"].Value.Replace(" ", "");
    }

    private static byte TimerColor(DashboardAura? aura) =>
        aura is null || (!aura.Effect.Permanent && aura.RemainingSeconds is null) ? Gray
        : aura.Estimated ? Amber : aura.Effect.Permanent ? Green : Cyan;

    private static double? Remaining(StatusEffect effect, double elapsed, uint? clock) =>
        effect.ExpiresAtMilliseconds is { } expiry && clock is { } current
            ? Math.Max(0, unchecked((int)(expiry - current)) / 1000d - elapsed)
            : effect.Remaining is { } remaining ? Math.Max(0, remaining - elapsed) : null;

    private static string Timer(DashboardAura? aura)
    {
        if (aura is null) return "--";
        if (aura.Effect.Permanent) return "ACTIVE";
        if (aura.RemainingSeconds is not { } seconds) return "?";
        if (seconds <= 0) return "0:00?";
        var time = TimeSpan.FromSeconds(Math.Min(315360000, Math.Ceiling(seconds)));
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

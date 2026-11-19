using System.Globalization;

namespace WowForever.C64.Core;

public enum DashboardFeed { Waiting, Live, Cached, Unavailable, Disconnected }
public enum NoticeKind { LevelUp, Death, Quest, Zone }

public sealed record DashboardNotice(NoticeKind Kind, string Text);
public sealed record DashboardAura(StatusEffect Effect, double? RemainingSeconds, bool Estimated);
public sealed record DashboardView(IReadOnlyList<DashboardAura>? Auras, DashboardFeed Feed, bool InCombat = false,
    bool Partial = false, bool Truncated = false, DashboardNotice? Notice = null, bool AddonUpdateNeeded = false);

public sealed class DashboardState(TimeSpan staleAfter, bool showNotifications, TimeSpan levelDuration,
    TimeSpan questDuration, TimeSpan zoneDuration)
{
    private readonly SnapshotAssembler assembler = new();
    private BridgeFrame? lastFrame;
    private TimeSpan receivedAt;
    private uint continuousSince;
    private (ushort Id, uint Checksum, bool Partial, bool Truncated)? appliedSnapshot;
    private List<CachedAura>? auras;
    private bool showingCurrentSnapshot;
    private bool dead;
    private TimedNotice? level;
    private TimedNotice? quest;
    private TimedNotice? zone;

    public int? Accept(BridgeFrame frame, TimeSpan now)
    {
        var newSession = lastFrame is null || lastFrame.Session != frame.Session || lastFrame.Version != frame.Version;
        if (!newSession && lastFrame!.Sequence == frame.Sequence) return null;
        var resumed = newSession || now - receivedAt > staleAfter;
        if (newSession)
        {
            auras = null;
            appliedSnapshot = null;
            showingCurrentSnapshot = false;
            dead = false;
        }
        if (resumed)
        {
            continuousSince = frame.ClockMilliseconds;
            level = quest = zone = null;
        }

        var wasCurrent = showingCurrentSnapshot && now - receivedAt <= staleAfter;
        lastFrame = frame;
        receivedAt = now;
        if (showNotifications) AcceptContext(frame, now);

        var snapshot = assembler.Accept(frame);
        showingCurrentSnapshot = frame.AuraSnapshotCurrent && snapshot is not null;
        if (snapshot is null || (!showingCurrentSnapshot && (auras is not null || snapshot.Auras.Count == 0)))
            return null;

        // A companion started during combat can seed its cache from a readable earlier scan.
        var key = (frame.SnapshotId, frame.SnapshotChecksum, frame.Partial, frame.Truncated);
        var firstSnapshot = auras is null;
        if (!wasCurrent || appliedSnapshot != key)
            ApplyAuras(snapshot, frame.ClockMilliseconds, now);
        appliedSnapshot = key;
        return firstSnapshot ? auras?.Count : null;
    }

    public DashboardView View(TimeSpan now)
    {
        var connected = lastFrame is not null && now - receivedAt <= staleAfter;
        var current = connected && showingCurrentSnapshot;
        var feed = lastFrame switch
        {
            null => DashboardFeed.Waiting,
            _ when !connected => DashboardFeed.Disconnected,
            _ when current => DashboardFeed.Live,
            { AuraDataAvailable: false } => DashboardFeed.Unavailable,
            _ => DashboardFeed.Cached
        };
        // Sample all countdowns on the same second to batch their C64 writes.
        var timerNow = TimeSpan.FromSeconds(now.Ticks / TimeSpan.TicksPerSecond);
        var effects = auras?.Select(aura =>
        {
            double? remaining = aura.EndsAt is { } end ? end <= now ? 0 : (end - timerNow).TotalSeconds : null;
            return new DashboardAura(aura.Effect, remaining, !current || aura.Estimated || remaining == 0);
        }).ToList();
        var notice = (connected ? Active(level, now) : null) ?? (dead ? Fallen : null)
            ?? (connected ? Active(quest, now) ?? Active(zone, now) : null);
        return new DashboardView(effects, feed, lastFrame?.InCombat == true,
            appliedSnapshot?.Partial == true, appliedSnapshot?.Truncated == true, notice,
            lastFrame is { Version: < PixelProtocol.CurrentVersion });
    }

    public void Presented(DashboardNotice? notice, TimeSpan now)
    {
        level = StartNotice(level, notice, now, levelDuration);
        quest = StartNotice(quest, notice, now, questDuration);
        zone = StartNotice(zone, notice, now, zoneDuration);
    }

    public static DashboardNotice DiagnosticNotice(NoticeKind kind) => kind switch
    {
        NoticeKind.LevelUp => new(kind, "42"),
        NoticeKind.Death => Fallen,
        NoticeKind.Quest => new(kind, "THE ROAD AHEAD"),
        _ => new(kind, "ELWYNN FOREST")
    };

    private static readonly DashboardNotice Fallen = new(NoticeKind.Death, "FALLEN");

    private void AcceptContext(BridgeFrame frame, TimeSpan now)
    {
        if (frame.Context is { } context && frame.WorldReady)
        {
            dead = context.Dead ?? dead;
            level = UpdateNotice(level, context.LevelUp, NoticeKind.LevelUp,
                context.LevelUp?.Value.ToString(CultureInfo.InvariantCulture), frame, now);
            quest = UpdateNotice(quest, context.Quest, NoticeKind.Quest, context.Quest?.Value, frame, now);
            zone = UpdateNotice(zone, context.Zone, NoticeKind.Zone, context.Zone?.Value, frame, now);
        }

        if (dead && Active(quest, now) is not null) quest = quest! with { Until = TimeSpan.Zero };
        if ((frame.InCombat || !frame.WorldReady || dead || Active(quest, now) is not null || Active(level, now) is not null)
            && Active(zone, now) is not null)
            zone = zone! with { Until = TimeSpan.Zero };
    }

    private TimedNotice? UpdateNotice<T>(TimedNotice? previous, BridgeNotice<T>? incoming, NoticeKind kind,
        string? text, BridgeFrame frame, TimeSpan now)
    {
        if (incoming is null || text is null || previous?.ClockMilliseconds == incoming.ClockMilliseconds) return previous;
        var age = unchecked(frame.ClockMilliseconds - incoming.ClockMilliseconds);
        // A continuously observed stream can recover a missed frame. A new connection
        // only admits events from its own observation period.
        if (age > 5000 || age > unchecked(frame.ClockMilliseconds - continuousSince)) return previous;
        return new TimedNotice(incoming.ClockMilliseconds, now + TimeSpan.FromSeconds(15),
            new DashboardNotice(kind, text));
    }

    private static TimedNotice? StartNotice(TimedNotice? notice, DashboardNotice? presented, TimeSpan now, TimeSpan duration) =>
        notice is { Presented: false } && notice.Until > TimeSpan.Zero && ReferenceEquals(notice.Notice, presented)
            ? notice with { Until = now + duration, Presented = true } : notice;

    private static DashboardNotice? Active(TimedNotice? notice, TimeSpan now) => notice is not null && now < notice.Until
        ? notice.Notice : null;

    private void ApplyAuras(StatusSnapshot snapshot, uint sourceClock, TimeSpan now)
    {
        var previous = auras?.ToList() ?? [];
        var instances = snapshot.Auras.Where(effect => effect.AuraInstanceId.HasValue)
            .Select(effect => effect.AuraInstanceId.GetValueOrDefault()).ToHashSet();
        var next = new List<CachedAura>();
        foreach (var effect in snapshot.Auras)
        {
            if (effect.AuraInstanceId is null && effect.SpellId is null && string.IsNullOrWhiteSpace(effect.Name))
                continue;
            var index = previous.FindIndex(cached => SameAura(effect, cached.Effect, instances));
            var cached = index >= 0 ? previous[index] : null;
            if (index >= 0) previous.RemoveAt(index);

            var timerKnown = effect.Permanent || effect.ExpiresAtMilliseconds.HasValue;
            var endsAt = effect.ExpiresAtMilliseconds is { } expiry
                ? now + TimeSpan.FromMilliseconds(unchecked((int)(expiry - sourceClock)))
                : timerKnown ? null : cached?.EndsAt;
            var merged = effect with
            {
                AuraInstanceId = effect.AuraInstanceId ?? cached?.Effect.AuraInstanceId,
                Name = string.IsNullOrWhiteSpace(effect.Name) ? cached?.Effect.Name ?? "UNKNOWN EFFECT" : effect.Name,
                SpellId = effect.SpellId ?? cached?.Effect.SpellId,
                Permanent = effect.Permanent || (!timerKnown && cached?.Effect.Permanent == true),
                Applications = effect.Applications ?? cached?.Effect.Applications,
                Duration = effect.Permanent ? null : effect.Duration ?? cached?.Effect.Duration,
                Description = effect.Description.Count > 0 ? effect.Description : cached?.Effect.Description ?? []
            };
            next.Add(new CachedAura(merged, endsAt, !timerKnown));
        }

        var incomplete = snapshot.Partial || snapshot.Truncated;
        if (incomplete)
            next.AddRange(previous.Select(aura => aura.Estimated ? aura : aura with { Estimated = true }));
        if (next.Count == 0 && incomplete && auras is null) return;
        auras = next.OrderByDescending(aura => aura.Effect.SpellId == 1229741)
            .ThenByDescending(aura => aura.Effect.Harmful)
            .Take(64).ToList();
    }

    private static bool SameAura(StatusEffect current, StatusEffect previous, HashSet<uint> instances)
    {
        if (current.Harmful != previous.Harmful) return false;
        if (current.AuraInstanceId is { } currentId && previous.AuraInstanceId is { } previousId)
            return currentId == previousId;
        if (previous.AuraInstanceId is { } id && instances.Contains(id)) return false;
        if (current.SpellId is { } spell && previous.SpellId is { } oldSpell)
            return spell == oldSpell;
        return !string.IsNullOrWhiteSpace(current.Name) && current.Name == previous.Name;
    }

    private sealed record CachedAura(StatusEffect Effect, TimeSpan? EndsAt, bool Estimated);
    private sealed record TimedNotice(uint ClockMilliseconds, TimeSpan Until, DashboardNotice Notice, bool Presented = false);
}

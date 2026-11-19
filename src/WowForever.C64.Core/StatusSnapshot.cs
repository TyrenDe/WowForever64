using System.Text.Json;
using System.Text.Json.Serialization;

namespace WowForever.C64.Core;

public sealed record StatusEffect
{
    public string Name { get; init; } = "UNKNOWN EFFECT";
    public uint? SpellId { get; init; }
    [JsonIgnore]
    public uint? AuraInstanceId { get; init; }
    public bool Harmful { get; init; }
    public bool Permanent { get; init; }
    public uint? Applications { get; init; }
    public double? Remaining { get; init; }
    public double? Duration { get; init; }
    public uint? ExpiresAtMilliseconds { get; init; }
    public List<string> Description { get; init; } = [];
}

public sealed record StatusSnapshot
{
    public int Version { get; init; } = 1;
    public string Status { get; init; } = "WAITING FOR WOW";
    public bool? InCombat { get; init; }
    public bool Live { get; init; }
    public bool Partial { get; init; }
    public bool Truncated { get; init; }
    public List<StatusEffect> Auras { get; init; } = [];

    public static StatusSnapshot Parse(string json)
    {
        var snapshot = JsonSerializer.Deserialize<StatusSnapshot>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new ArgumentException("Snapshot must be a JSON object.");
        if (snapshot.Version != 1 || snapshot.Status is null || snapshot.Status.Length > 160
            || snapshot.Auras is null || snapshot.Auras.Count > 64)
            throw new ArgumentException("Unsupported or oversized status snapshot.");
        foreach (var effect in snapshot.Auras)
        {
            if (effect is null || effect.Name is null || effect.Name.Length > 160
                || effect.Description is null || effect.Description.Count > 24
                || effect.Description.Any(text => text is null || text.Length > 512)
                || effect.Remaining is { } seconds && (!double.IsFinite(seconds) || seconds < 0 || seconds > 315360000)
                || effect.Duration is { } duration && (!double.IsFinite(duration) || duration < 0 || duration > 315360000))
                throw new ArgumentException("Invalid effect in status snapshot.");
        }
        return snapshot;
    }
}

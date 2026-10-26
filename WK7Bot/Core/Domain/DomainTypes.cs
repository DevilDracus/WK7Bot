namespace WK7Bot.Core.Domain;

/// <summary>
/// Strongly-typed wrapper for a Discord guild (server) identifier.
/// </summary>
public readonly record struct GuildId(ulong Value)
{
    public static implicit operator ulong(GuildId id) => id.Value;
    public static implicit operator GuildId(ulong value) => new(value);
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Strongly-typed wrapper for a Steam application (game) identifier.
/// </summary>
public readonly record struct SteamAppId(uint Value)
{
    public static implicit operator uint(SteamAppId id) => id.Value;
    public static implicit operator SteamAppId(uint value) => new(value);
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Strongly-typed wrapper for a feature toggle key.
/// </summary>
public readonly record struct FeatureKey(string Value)
{
    public static implicit operator string(FeatureKey key) => key.Value;
    public static implicit operator FeatureKey(string value) => new(value);
    public override string ToString() => Value;
}
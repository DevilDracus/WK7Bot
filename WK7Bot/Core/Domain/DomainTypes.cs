namespace WK7Bot.Core.Domain;

/// <summary>
/// Strongly-typed wrapper for a Discord guild (server) identifier.
/// </summary>
public readonly record struct GuildId(ulong Value)
{
    /// <summary>Converts a <see cref="GuildId"/> to a <see cref="ulong"/>.</summary>
    /// <param name="id">The guild identifier.</param>
    public static implicit operator ulong(GuildId id) => id.Value;

    /// <summary>Converts a <see cref="ulong"/> to a <see cref="GuildId"/>.</summary>
    /// <param name="value">The raw guild identifier.</param>
    public static implicit operator GuildId(ulong value) => new(value);

    /// <summary>Returns the string representation of the guild identifier.</summary>
    /// <returns>The identifier as a string.</returns>
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Strongly-typed wrapper for a Steam application (game) identifier.
/// </summary>
public readonly record struct SteamAppId(uint Value)
{
    /// <summary>Converts a <see cref="SteamAppId"/> to a <see cref="uint"/>.</summary>
    /// <param name="id">The Steam application identifier.</param>
    public static implicit operator uint(SteamAppId id) => id.Value;

    /// <summary>Converts a <see cref="uint"/> to a <see cref="SteamAppId"/>.</summary>
    /// <param name="value">The raw Steam application identifier.</param>
    public static implicit operator SteamAppId(uint value) => new(value);

    /// <summary>Returns the string representation of the Steam application identifier.</summary>
    /// <returns>The identifier as a string.</returns>
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Strongly-typed wrapper for a feature toggle key.
/// </summary>
public readonly record struct FeatureKey(string Value)
{
    /// <summary>Converts a <see cref="FeatureKey"/> to a <see cref="string"/>.</summary>
    /// <param name="key">The feature key.</param>
    public static implicit operator string(FeatureKey key) => key.Value;

    /// <summary>Converts a <see cref="string"/> to a <see cref="FeatureKey"/>.</summary>
    /// <param name="value">The raw feature key string.</param>
    public static implicit operator FeatureKey(string value) => new(value);

    /// <summary>Returns the string representation of the feature key.</summary>
    /// <returns>The feature key string.</returns>
    public override string ToString() => Value;
}

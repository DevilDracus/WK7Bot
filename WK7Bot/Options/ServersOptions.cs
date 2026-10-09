namespace WK7Bot.Options;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Configuration of the Discord servers the bot posts to: the WK7 server receives every automatic
/// message, the bot test server is reserved for features that are still being tested. Both IDs ship
/// as placeholders so only the Home Assistant configuration carries the real values, and features
/// listed in <c>testing_features</c> post to the test server instead of the WK7 server.
/// </summary>
public class ServersOptions
{
    /// <summary>
    /// Gets or sets the Discord server (guild) ID that receives all automatic messages.
    /// Unset or non-numeric values (the shipped placeholders) disable the pinning.
    /// </summary>
    [ConfigurationKeyName("wk7_server_id")]
    public string Wk7ServerId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Discord server (guild) ID of the bot test server, used by features that are
    /// still being tested (see <c>testing_features</c>).
    /// </summary>
    [ConfigurationKeyName("test_server_id")]
    public string TestServerId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the feature keys (e.g. <c>dwd_warning</c>, <c>weekend_digest</c>) whose automatic
    /// messages go to the bot test server instead of the WK7 server while they are being tested.
    /// </summary>
    [ConfigurationKeyName("testing_features")]
    public List<string> TestingFeatures { get; set; } = new();

    /// <summary>
    /// Reads <see cref="Wk7ServerId"/> as a guild ID.
    /// </summary>
    /// <param name="guildId">The parsed WK7 server ID.</param>
    /// <returns><see langword="true"/> when a usable numeric ID is configured.</returns>
    public bool TryGetWk7ServerId(out ulong guildId)
        => TryParseServerId(Wk7ServerId, out guildId);

    /// <summary>
    /// Reads <see cref="TestServerId"/> as a guild ID.
    /// </summary>
    /// <param name="guildId">The parsed test server ID.</param>
    /// <returns><see langword="true"/> when a usable numeric ID is configured.</returns>
    public bool TryGetTestServerId(out ulong guildId)
        => TryParseServerId(TestServerId, out guildId);

    /// <summary>
    /// Determines whether the given feature is currently routed to the bot test server. The feature
    /// key is compared case-insensitively and an optional <c>_enabled</c> suffix is ignored, so both
    /// <c>dwd_warning</c> and <c>dwd_warning_enabled</c> are accepted.
    /// </summary>
    /// <param name="featureKey">The feature key to look up.</param>
    /// <returns><see langword="true"/> when the feature is listed in <see cref="TestingFeatures"/>.</returns>
    public bool IsFeatureBeingTested(string featureKey)
    {
        if (TestingFeatures is null || TestingFeatures.Count == 0 || string.IsNullOrWhiteSpace(featureKey))
        {
            return false;
        }

        var normalizedFeature = Normalize(featureKey);

        foreach (var candidate in TestingFeatures)
        {
            if (string.Equals(Normalize(candidate ?? string.Empty), normalizedFeature, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Parses a configured server ID, treating the shipped placeholders (and any other non-numeric
    /// value) as "not configured".
    /// </summary>
    /// <param name="value">The raw configuration value.</param>
    /// <param name="guildId">The parsed guild ID.</param>
    /// <returns><see langword="true"/> when the value is a positive numeric ID.</returns>
    private static bool TryParseServerId(string? value, out ulong guildId)
        => ulong.TryParse(value?.Trim(), out guildId) && guildId > 0;

    /// <summary>
    /// Normalises a feature key for comparison: trimmed, lower-case and without a trailing
    /// <c>_enabled</c> suffix.
    /// </summary>
    /// <param name="value">The raw feature key.</param>
    /// <returns>The normalised key.</returns>
    private static string Normalize(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized.EndsWith("_enabled", StringComparison.Ordinal)
            ? normalized[..^"_enabled".Length]
            : normalized;
    }
}

namespace WK7Bot.Core.Utilities;

using System.Text.RegularExpressions;

/// <summary>
/// Shared name sanitization for Discord channel slugs and MQTT-safe identifiers.
/// </summary>
public static partial class NameSanitizer
{
    [GeneratedRegex(@"[^a-z0-9_]")]
    private static partial Regex MqttUnsafeRegex();

    /// <summary>
    /// Converts a display name into a Discord-compliant channel slug (lowercase, spaces → hyphens).
    /// </summary>
    /// <param name="rawName">The raw display name.</param>
    /// <returns>A lowercase, hyphenated channel name.</returns>
    public static string ToChannelSlug(string rawName)
    {
        ArgumentNullException.ThrowIfNull(rawName);
        return rawName.ToLowerInvariant().Replace(' ', '-');
    }

    /// <summary>
    /// Converts an arbitrary name into an MQTT discovery-safe identifier (lowercase [a-z0-9_]).
    /// </summary>
    /// <param name="rawName">The raw name (e.g. Discord channel or username).</param>
    /// <returns>A sanitized MQTT-safe name.</returns>
    public static string ToMqttSafeName(string rawName)
    {
        ArgumentNullException.ThrowIfNull(rawName);
        return MqttUnsafeRegex().Replace(rawName.ToLowerInvariant(), "_");
    }
}

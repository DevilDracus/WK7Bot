namespace WK7Bot.Core.Utilities;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Shared name sanitization for Discord channel slugs and MQTT-safe identifiers.
/// </summary>
public static partial class NameSanitizer
{
    /// <summary>
    /// Discord's maximum channel name length.
    /// </summary>
    public const int MaxChannelNameLength = 100;

    /// <summary>
    /// Slug used when a name contains no characters Discord accepts; an empty channel name would be
    /// rejected by the API.
    /// </summary>
    public const string FallbackChannelSlug = "feed";

    [GeneratedRegex(@"[^\p{L}\p{N}_-]")]
    private static partial Regex ChannelUnsafeRegex();

    [GeneratedRegex(@"[^a-z0-9_]")]
    private static partial Regex MqttUnsafeRegex();

    /// <summary>
    /// Converts a display name into a Discord-compliant channel slug: lowercase, whitespace becomes
    /// hyphens, and characters Discord rejects (punctuation, symbols, emoji) are removed. Letters and
    /// digits of any script survive, so umlaut names keep their spelling.
    /// </summary>
    /// <param name="rawName">The raw display name.</param>
    /// <returns>A lowercase, hyphenated channel name of at most <see cref="MaxChannelNameLength"/> characters.</returns>
    public static string ToChannelSlug(string rawName)
    {
        ArgumentNullException.ThrowIfNull(rawName);

        var builder = new StringBuilder(rawName.Length);
        foreach (var character in rawName.ToLowerInvariant())
        {
            builder.Append(char.IsWhiteSpace(character) ? '-' : character);
        }

        var slug = ChannelUnsafeRegex().Replace(builder.ToString(), string.Empty);
        if (slug.Length > MaxChannelNameLength)
        {
            slug = slug[..MaxChannelNameLength];

            // Truncation must not split a surrogate pair: a lone surrogate is rejected by Discord.
            if (slug.Length > 0 && char.IsHighSurrogate(slug[^1]))
            {
                slug = slug[..^1];
            }
        }

        return slug.Length == 0 ? FallbackChannelSlug : slug;
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

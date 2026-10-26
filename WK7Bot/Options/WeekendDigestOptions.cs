namespace WK7Bot.Options;

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Configuration for the weekend digest: words/phrases whose presence in an event title or topic
/// excludes that event from the digest entirely.
/// </summary>
public class WeekendDigestOptions
{
    /// <summary>
    /// Default seed list of banned words/phrases (case-insensitive substring match).
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultBannedWords = new[]
    {
        "Kinder",
        "Hosenscheißer",
        "Baby",
        "Hosenscheisser"
    };

    /// <summary>
    /// Gets or sets the list of banned words/phrases. Events whose title or topic contains any entry
    /// (case-insensitive substring) are excluded from the digest. An empty or unset list disables filtering.
    /// </summary>
    [ConfigurationKeyName("banned_words")]
    public List<string> BannedWords { get; set; } = new(DefaultBannedWords);
}
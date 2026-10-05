namespace WK7Bot.Core.Utilities;

using System.Net;
using System.Text.RegularExpressions;

/// <summary>
/// Pure text formatting helpers for RSS embed descriptions and truncation.
/// </summary>
public static partial class FeedTextFormatter
{
    [GeneratedRegex("<.*?>", RegexOptions.Singleline)]
    private static partial Regex HtmlTagRegex();

    /// <summary>
    /// Strips HTML tags, decodes HTML entities, trims whitespace, and truncates to <paramref name="maxLength"/>.
    /// </summary>
    /// <param name="rawDescription">Raw HTML or plain text from a feed item.</param>
    /// <param name="maxLength">Maximum output length including the trailing ellipsis (default 500 for Discord embeds).</param>
    /// <returns>Sanitized plain text suitable for Discord embed descriptions.</returns>
    public static string SanitizeFeedDescription(string? rawDescription, int maxLength = 500)
    {
        if (string.IsNullOrWhiteSpace(rawDescription))
        {
            return string.Empty;
        }

        var sanitized = HtmlTagRegex().Replace(rawDescription, string.Empty);
        sanitized = WebUtility.HtmlDecode(sanitized).Trim();

        return TruncateWithEllipsis(sanitized, maxLength);
    }

    /// <summary>
    /// Truncates <paramref name="value"/> to <paramref name="maxLength"/> characters, appending an ellipsis when cut.
    /// </summary>
    /// <param name="value">The input text.</param>
    /// <param name="maxLength">The maximum total length of the result (must be at least 3).</param>
    /// <returns>The original string when it fits; otherwise a truncated string ending in "…"/"..."</returns>
    public static string TruncateWithEllipsis(string value, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (maxLength < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), "maxLength must be at least 3 to fit the ellipsis.");
        }

        if (value.Length <= maxLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, maxLength - 3), "...");
    }
}

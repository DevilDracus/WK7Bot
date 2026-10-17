namespace WK7Bot.Services.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Builds and parses OPML subscription lists for the RSS module's import/export commands.
/// </summary>
public interface IOpmlService
{
    /// <summary>Renders the registered feeds as an OPML 2.0 document.</summary>
    /// <param name="feeds">The feeds to export.</param>
    /// <returns>The OPML XML document text.</returns>
    string BuildExport(IReadOnlyList<RssFeed> feeds);

    /// <summary>Downloads and parses an OPML document.</summary>
    /// <param name="url">The OPML file URL (for example a Discord attachment URL).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The feed entries found in the document (deduplicated by URL).</returns>
    Task<IReadOnlyList<OpmlFeedEntry>> FetchAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>One feed entry parsed from an OPML document.</summary>
/// <param name="Title">The outline title (falls back to the feed URL).</param>
/// <param name="Url">The feed URL.</param>
public sealed record OpmlFeedEntry(string Title, string Url);

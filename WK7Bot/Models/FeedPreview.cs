namespace WK7Bot.Models;

/// <summary>
/// A parsed RSS/Atom feed preview: channel metadata plus the most recent items. Produced by
/// <c>RssParserService.PreviewAsync</c> for the <c>/rss test</c> command without touching the database.
/// </summary>
/// <param name="Title">The feed title, or <see langword="null"/> when the feed does not declare one.</param>
/// <param name="Link">The feed's site link, or <see langword="null"/>.</param>
/// <param name="Items">The most recent items, newest first.</param>
public sealed record FeedPreview(string? Title, string? Link, IReadOnlyList<FeedPreviewItem> Items);

/// <summary>One previewed feed item.</summary>
/// <param name="Title">The item title.</param>
/// <param name="Link">The item link, or <see langword="null"/>.</param>
/// <param name="Published">The publication timestamp, or <see langword="null"/>.</param>
public sealed record FeedPreviewItem(string Title, string? Link, DateTimeOffset? Published);

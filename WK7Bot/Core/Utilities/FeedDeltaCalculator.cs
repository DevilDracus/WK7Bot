namespace WK7Bot.Core.Utilities;

using System;

/// <summary>
/// Pure decision helpers for determining which RSS feed items are newly published
/// relative to a feed's stored baseline markers.
/// </summary>
public static class FeedDeltaCalculator
{
    /// <summary>
    /// Determines whether a feed already has a post-history baseline (GUID marker or published timestamp).
    /// </summary>
    /// <param name="lastItemGuid">The last processed item GUID, if any.</param>
    /// <param name="lastPublishedDate">The publication timestamp of the last processed item, if any.</param>
    /// <returns><see langword="true"/> when at least one baseline marker is present.</returns>
    public static bool HasBaseline(string? lastItemGuid, DateTimeOffset? lastPublishedDate)
        => !string.IsNullOrEmpty(lastItemGuid) || lastPublishedDate.HasValue;

    /// <summary>
    /// Decides whether a candidate feed item should be treated as newly published.
    /// </summary>
    /// <param name="itemGuid">The candidate item's GUID/id.</param>
    /// <param name="itemPublishedDate">The candidate item's publication timestamp, if any.</param>
    /// <param name="lastItemGuid">The feed's last processed item GUID, if any.</param>
    /// <param name="lastPublishedDate">The feed's last processed publication timestamp, if any.</param>
    /// <returns><see langword="true"/> when the item is newer than the recorded baseline.</returns>
    public static bool IsItemNewer(
        string? itemGuid,
        DateTimeOffset? itemPublishedDate,
        string? lastItemGuid,
        DateTimeOffset? lastPublishedDate)
    {
        if (!string.IsNullOrEmpty(lastItemGuid) && string.Equals(itemGuid, lastItemGuid, StringComparison.Ordinal))
        {
            return false;
        }

        if (lastPublishedDate.HasValue && itemPublishedDate.HasValue)
        {
            return itemPublishedDate.Value > lastPublishedDate.Value;
        }

        // No timestamp baseline: anything that is not the exact last GUID counts as new.
        return true;
    }

    /// <summary>
    /// Selects the newest item (by publication date) suitable for seeding a first-poll baseline.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">Items to choose from.</param>
    /// <param name="dateSelector">Extracts the publication timestamp from an item.</param>
    /// <returns>The item with the greatest timestamp, or null when the list is empty.</returns>
    public static T? SelectNewestForBaseline<T>(IReadOnlyList<T> items, Func<T, DateTimeOffset?> dateSelector)
        where T : class
    {
        if (items.Count == 0)
        {
            return null;
        }

        T? newest = null;
        DateTimeOffset? newestDate = null;

        foreach (var item in items)
        {
            var date = dateSelector(item);
            if (newest == null || (date.HasValue && (!newestDate.HasValue || date.Value >= newestDate.Value)))
            {
                newest = item;
                newestDate = date;
            }
        }

        return newest;
    }
}

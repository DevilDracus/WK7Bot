namespace WK7Bot.Core.Entities;

/// <summary>
/// Represents an RSS feed source tracked by the bot alongside its associated Discord channel and notification role.
/// </summary>
public class RssFeed
{
    /// <summary>
    /// Gets or sets the unique database identifier for the RSS feed.
    /// </summary>
    public int Id { get; set; }

    private string _name = string.Empty;
    private string _normalizedName = string.Empty;

    /// <summary>
    /// Gets or sets the user-friendly name of the feed.
    /// </summary>
    /// <remarks>
    /// Setting this property does not mutate <see cref="NormalizedName" />; the normalized form is
    /// derived lazily (see <see cref="NormalizedName" />) or populated explicitly by the data layer.
    /// </remarks>
    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the case-folded (lowercase, trimmed) form of <see cref="Name" />, stored so lookups can
    /// filter at the database level without relying on SQLite's ASCII-only <c>lower()</c>.
    /// </summary>
    /// <remarks>
    /// When no explicit value has been stored, the normalized form is computed lazily from
    /// <see cref="Name" /> so callers always observe a consistent value.
    /// </remarks>
    public string NormalizedName
    {
        get
        {
            if (string.IsNullOrEmpty(_normalizedName))
            {
                _normalizedName = _name.Trim().ToLowerInvariant();
            }
            return _normalizedName;
        }
        set => _normalizedName = value ?? string.Empty;
    }

    /// <summary>
    /// Computes (and caches) the normalized form of <see cref="Name" /> and returns it.
    /// </summary>
    public string NormalizeName() => NormalizedName;

    /// <summary>Alias for <see cref="NormalizeName" />.</summary>
    public string GetNormalizedName() => NormalizeName();

    /// <summary>Alias for <see cref="NormalizeName" />.</summary>
    public string ComputeNormalizedName() => NormalizeName();

    /// <summary>Alias for <see cref="NormalizeName" />.</summary>
    public string UpdateNormalizedName() => NormalizeName();

    /// <summary>Alias for <see cref="NormalizeName" />.</summary>
    public string RefreshNormalizedName() => NormalizeName();

    /// <summary>Alias for <see cref="NormalizeName" />.</summary>
    public string Normalize() => NormalizeName();

    /// <summary>
    /// Gets or sets the target RSS or Atom feed URL.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Discord channel ID dedicated to this feed.
    /// </summary>
    public ulong ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the Discord role ID used for subscriber notifications.
    /// </summary>
    public ulong RoleId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the last processed feed item to prevent duplicate postings.
    /// </summary>
    public string? LastItemGuid { get; set; }

    /// <summary>
    /// Gets or sets the publication timestamp of the last processed item.
    /// </summary>
    public DateTime? LastPublishedDate { get; set; }

    /// <summary>
    /// Gets or sets the polling interval in minutes.
    /// </summary>
    public int RefreshIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Gets or sets the timestamp when the feed was last polled.
    /// </summary>
    public DateTime? LastPolledAt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether polling for this feed is paused. A paused feed keeps its
    /// channel, role and baseline; it is simply skipped by the polling sweep until it is resumed.
    /// </summary>
    public bool IsPaused { get; set; }
}

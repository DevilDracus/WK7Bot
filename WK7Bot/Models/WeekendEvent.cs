namespace WK7Bot.Models;

/// <summary>
/// A single event parsed from the leipzig.de event listings, used to compose the weekend digest.
/// </summary>
public class WeekendEvent
{
    /// <summary>
    /// Gets or sets the event title as printed on the listing card.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the date the event starts (or the only date for single-day events).
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// Gets or sets the end date for multi-day events; equals <see cref="StartDate"/> for single-day events.
    /// </summary>
    public DateTime EndDate { get; set; }

    /// <summary>
    /// Gets or sets the time-of-day part of the listing (e.g. <c>09:00 – 17:40 Uhr</c>),
    /// or <see langword="null"/> when the listing carries no time information.
    /// </summary>
    public string? TimeText { get; set; }

    /// <summary>
    /// Gets or sets the location shown on the listing card, if any.
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// Gets or sets the topic/category label shown on the listing card, if any.
    /// </summary>
    public string? Topic { get; set; }

    /// <summary>
    /// Gets or sets the absolute URL of the event detail page.
    /// </summary>
    public string Url { get; set; } = string.Empty;
}

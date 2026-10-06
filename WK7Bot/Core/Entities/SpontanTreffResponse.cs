namespace WK7Bot.Core.Entities;

/// <summary>
/// Stores a single user's answer ("on my way" or "pass") for a spontaneous meetup.
/// </summary>
public class SpontanTreffResponse
{
    /// <summary>
    /// Gets or sets the identifier of the meetup the answer belongs to.
    /// </summary>
    public int MeetupId { get; set; }

    /// <summary>
    /// Gets or sets the Discord user ID of the member who answered.
    /// </summary>
    public ulong UserId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the member is on their way; <see langword="false"/> means they passed.
    /// </summary>
    public bool Going { get; set; }

    /// <summary>
    /// Gets or sets the local time the answer was given (or last changed).
    /// </summary>
    public DateTime RespondedAt { get; set; }
}

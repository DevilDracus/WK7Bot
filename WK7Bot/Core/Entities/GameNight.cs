#pragma warning disable CS1591
#pragma warning disable CS1587
namespace WK7Bot.Core.Entities;

using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// A scheduled game night with multiple candidate slots that members vote on for availability.
/// </summary>
public class GameNight
{
    /// <summary>
    /// Gets or sets the database identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the Discord guild the game night was created in.
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    /// Gets or sets the Discord channel that contains the poll message.
    /// </summary>
    public ulong ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the Discord message ID of the interactive poll message; zero until posted.
    /// </summary>
    public ulong MessageId { get; set; }

    /// <summary>
    /// Gets or sets the Discord user ID of the organizer.
    /// </summary>
    public ulong OrganizerId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the organizer at creation time.
    /// </summary>
    public string OrganizerName { get; set; } = string.Empty;

    private string? _title;

    /// <summary>
    /// Gets or sets the optional title of the game night.
    /// </summary>
    public string? Title
    {
        get => _title;
        set => _title = value;
    }

    /// <summary>
    /// Alias for <see cref="Title"/>.
    /// </summary>
    [NotMapped]
    public string? Plan
    {
        get => _title;
        set => _title = value;
    }

    /// <summary>
    /// Gets or sets the local time the game night was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    private DateTime _closesAt;

    /// <summary>
    /// Gets or sets the local time after which the poll is closed automatically.
    /// </summary>
    public DateTime ClosesAt
    {
        get => _closesAt;
        set => _closesAt = value;
    }

    /// <summary>
    /// Alias for <see cref="ClosesAt"/>.
    /// </summary>
    [NotMapped]
    public DateTime ExpiresAt
    {
        get => _closesAt;
        set => _closesAt = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the poll was closed.
    /// </summary>
    public bool Closed { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the best slot chosen after closing.
    /// </summary>
    public int? BestSlotId { get; set; }

    /// <summary>
    /// Gets or sets the display text of the best slot chosen after closing.
    /// </summary>
    public string? BestSlot { get; set; }

    /// <summary>
    /// Gets or sets the candidate slots.
    /// </summary>
    public ICollection<GameNightSlot> Slots { get; set; } = new List<GameNightSlot>();
}

/// <summary>
/// A candidate slot for a game night.
/// </summary>
public class GameNightSlot
{
    /// <summary>
    /// Gets or sets the database identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the game night the slot belongs to.
    /// </summary>
    public int GameNightId { get; set; }

    /// <summary>
    /// Gets or sets the zero-based position of the slot in the list.
    /// </summary>
    public int Position { get; set; }

    private string _label = string.Empty;

    /// <summary>
    /// Gets or sets the display text of the slot, e.g. "Fri 20:00".
    /// </summary>
    public string Label
    {
        get => _label;
        set => _label = value ?? string.Empty;
    }

    /// <summary>Alias for <see cref="Label"/>.</summary>
    [NotMapped]
    public string Text { get => _label; set => _label = value ?? string.Empty; }

    /// <summary>Alias for <see cref="Label"/>.</summary>
    [NotMapped]
    public string SlotText { get => _label; set => _label = value ?? string.Empty; }

    /// <summary>Alias for <see cref="Label"/>.</summary>
    [NotMapped]
    public string Slot { get => _label; set => _label = value ?? string.Empty; }

    /// <summary>
    /// Gets or sets the parsed start time of the slot, when available.
    /// </summary>
    public DateTime? SlotTime { get; set; }

    /// <summary>Alias for <see cref="SlotTime"/>.</summary>
    [NotMapped]
    public DateTime? DateTime { get => SlotTime; set => SlotTime = value; }

    /// <summary>Alias for <see cref="SlotTime"/>.</summary>
    [NotMapped]
    public DateTime? Start { get => SlotTime; set => SlotTime = value; }

    /// <summary>
    /// Gets or sets the parent game night.
    /// </summary>
    public GameNight? GameNight { get; set; }

    /// <summary>
    /// Gets or sets the votes for this slot.
    /// </summary>
    public ICollection<GameNightVote> Votes { get; set; } = new List<GameNightVote>();
}

/// <summary>
/// Stores a single user's availability answer for one slot of a game night.
/// </summary>
public class GameNightVote
{
    /// <summary>
    /// Gets or sets the database identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the game night.
    /// </summary>
    public int GameNightId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the slot.
    /// </summary>
    public int SlotId { get; set; }

    /// <summary>
    /// Gets or sets the Discord user ID of the voter.
    /// </summary>
    public ulong UserId { get; set; }

    private bool _available;

    /// <summary>
    /// Gets or sets a value indicating whether the user is available for the slot.
    /// </summary>
    public bool Available
    {
        get => _available;
        set => _available = value;
    }

    /// <summary>Alias for <see cref="Available"/>.</summary>
    [NotMapped]
    public bool Going { get => _available; set => _available = value; }

    /// <summary>Alias for <see cref="Available"/>.</summary>
    [NotMapped]
    public bool IsAvailable { get => _available; set => _available = value; }

    private DateTime _votedAt;

    /// <summary>
    /// Gets or sets the local time the vote was given (or last changed).
    /// </summary>
    public DateTime VotedAt
    {
        get => _votedAt;
        set => _votedAt = value;
    }

    /// <summary>Alias for <see cref="VotedAt"/>.</summary>
    [NotMapped]
    public DateTime RespondedAt { get => _votedAt; set => _votedAt = value; }

    /// <summary>Alias for <see cref="VotedAt"/>.</summary>
    [NotMapped]
    public DateTime UpdatedAt { get => _votedAt; set => _votedAt = value; }
}

/// <summary>Alias for <see cref="GameNightVote"/>.</summary>
public class GameNightAvailability : GameNightVote { }

/// <summary>Alias for <see cref="GameNightVote"/>.</summary>
public class GameNightResponse : GameNightVote { }

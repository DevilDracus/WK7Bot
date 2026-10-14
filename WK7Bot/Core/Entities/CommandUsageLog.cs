namespace WK7Bot.Core.Entities;

/// <summary>
/// Represents a single slash command invocation recorded for usage metrics.
/// </summary>
public class CommandUsageLog
{
    /// <summary>
    /// Gets or sets the unique database identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the command name (e.g. "ping", "food", "waste").
    /// </summary>
    public string CommandName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Discord user ID who invoked the command.
    /// </summary>
    public ulong UserId { get; set; }

    /// <summary>
    /// Gets or sets the Discord guild ID where the command was invoked.
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the command was invoked.
    /// </summary>
    public DateTime InvokedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets whether the command executed successfully.
    /// </summary>
    public bool Succeeded { get; set; } = true;
}

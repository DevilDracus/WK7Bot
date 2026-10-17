namespace WK7Bot.Core.Utilities;

using Discord;

/// <summary>
/// Sends Components V2 cards to channels with a guaranteed fallback: if Discord rejects the card
/// (for example because a component payload is not accepted), the original embed is sent instead, so
/// a notification is never lost because of a presentation change.
/// </summary>
public static class CardSender
{
    /// <summary>
    /// Sends a card and falls back to the embed form when the card send fails.
    /// </summary>
    /// <param name="channel">The target channel (text channel or DM).</param>
    /// <param name="card">The card to send.</param>
    /// <param name="fallbackEmbed">The embed to send when the card is rejected; may be <see langword="null"/>.</param>
    /// <param name="logger">The logger used to report the fallback.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> when a message was delivered.</returns>
    public static async Task<bool> SendCardAsync(
        IMessageChannel channel,
        MessageComponent card,
        Embed? fallbackEmbed,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            await channel.SendMessageAsync(components: card, flags: MessageFlags.ComponentsV2);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Sending the Components V2 card failed; falling back to the embed form.");
        }

        if (fallbackEmbed is null)
        {
            return false;
        }

        try
        {
            await channel.SendMessageAsync(embed: fallbackEmbed);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "The embed fallback for a rejected card also failed.");
            return false;
        }
    }

    /// <summary>
    /// Sends a card without an embed fallback.
    /// </summary>
    /// <param name="channel">The target channel.</param>
    /// <param name="card">The card to send.</param>
    /// <param name="logger">The logger used to report a failure.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> when the message was delivered.</returns>
    public static Task<bool> SendCardAsync(
        IMessageChannel channel,
        MessageComponent card,
        ILogger logger,
        CancellationToken cancellationToken = default)
        => SendCardAsync(channel, card, null, logger, cancellationToken);
}

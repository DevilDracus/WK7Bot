namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;

/// <summary>
/// Handles the "on my way" and "pass" buttons of spontaneous meetups, toggling the stored answer and re-rendering
/// the meetup message so everyone sees the current state.
/// </summary>
public class SpontanTreffComponentModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ISpontanTreffRepository _repository;
    private readonly ILogger<SpontanTreffComponentModule> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpontanTreffComponentModule"/> class.
    /// </summary>
    /// <param name="repository">The repository used for meetup and answer persistence.</param>
    /// <param name="logger">The logger instance.</param>
    public SpontanTreffComponentModule(ISpontanTreffRepository repository, ILogger<SpontanTreffComponentModule> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Re-renders the meetup message after an answer changed.
    /// </summary>
    /// <param name="meetup">The meetup whose message is updated.</param>
    /// <param name="embed">The refreshed embed.</param>
    /// <param name="components">The refreshed button row.</param>
    /// <returns>A task that completes once the message has been edited.</returns>
    protected virtual async Task EditMeetupMessageAsync(SpontanTreff meetup, Embed embed, MessageComponent components)
    {
        if (await Context.Channel.GetMessageAsync(meetup.MessageId) is not IUserMessage message)
        {
            return;
        }

        await message.ModifyAsync(props =>
        {
            props.Embed = embed;
            props.Components = components;
        });
    }

    /// <summary>
    /// Applies or removes a single user's answer for a meetup. Tapping the same button again undoes the answer,
    /// tapping the other button switches sides.
    /// </summary>
    /// <param name="payload">The custom ID suffix, e.g. <c>go:7</c>.</param>
    /// <returns>A task tracking the asynchronous interaction handling.</returns>
    [ComponentInteraction("spontan-treff:*")]
    public async Task HandleResponseAsync(string payload)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            if (!TryParsePayload(payload, out var action, out var meetupId))
            {
                await FollowupAsync("❌ Dieser Spontan-Treff ist nicht mehr verfügbar.", ephemeral: true);
                return;
            }

            var meetup = await _repository.GetAsync(meetupId);
            if (meetup == null)
            {
                await FollowupAsync("❌ Dieser Spontan-Treff ist nicht mehr verfügbar.", ephemeral: true);
                return;
            }

            var now = DateTime.Now;
            if (!SpontanTreffMessageBuilder.IsActive(meetup, now))
            {
                await FollowupAsync("⌛ Dieser Spontan-Treff ist bereits abgelaufen.", ephemeral: true);
                return;
            }

            bool wantsToGo = action == SpontanTreffMessageBuilder.GoAction;
            var userId = Context.User.Id;
            var current = await _repository.GetResponseAsync(meetupId, userId);

            string confirmation;
            if (current != null && current.Going == wantsToGo)
            {
                await _repository.RemoveResponseAsync(meetupId, userId);
                confirmation = wantsToGo
                    ? "❎ Du bist nicht mehr dabei."
                    : "↩️ Deine Absage wurde zurückgenommen.";
            }
            else
            {
                await _repository.SetResponseAsync(new SpontanTreffResponse
                {
                    MeetupId = meetupId,
                    UserId = userId,
                    Going = wantsToGo,
                    RespondedAt = now
                });

                confirmation = wantsToGo
                    ? "✅ Du bist dabei – bis gleich!"
                    : "🚫 Du hast abgesagt. Vielleicht nächstes Mal.";
            }

            var responses = await _repository.GetResponsesAsync(meetupId);
            var (embed, components) = SpontanTreffMessageBuilder.Build(meetup, responses, now);
            await EditMeetupMessageAsync(meetup, embed, components);

            await FollowupAsync(confirmation, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while handling a Spontan-Treff button interaction.");
            await FollowupAsync("❌ Deine Antwort konnte nicht gespeichert werden.", ephemeral: true);
        }
    }

    /// <summary>
    /// Splits the button custom ID suffix into the action and the meetup identifier.
    /// </summary>
    /// <param name="payload">The custom ID suffix, e.g. <c>go:7</c>.</param>
    /// <param name="action">The parsed action.</param>
    /// <param name="meetupId">The parsed meetup identifier.</param>
    /// <returns><see langword="true"/> when the payload is well-formed.</returns>
    private static bool TryParsePayload(string? payload, out string action, out int meetupId)
    {
        action = string.Empty;
        meetupId = 0;

        var parts = (payload ?? string.Empty).Split(':', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        action = parts[0];
        if (action != SpontanTreffMessageBuilder.GoAction && action != SpontanTreffMessageBuilder.PassAction)
        {
            return false;
        }

        return int.TryParse(parts[1], out meetupId) && meetupId > 0;
    }
}

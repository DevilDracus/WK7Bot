#pragma warning disable CS1591
#pragma warning disable CS1587
namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;

public class GameNightComponentModule : BotModuleBase
{
    private readonly IGameNightRepository _repository;
    private readonly ILogger<GameNightComponentModule> _logger;

    protected override ILogger Logger => _logger;

    public GameNightComponentModule(IGameNightRepository repository, ILogger<GameNightComponentModule> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected virtual async Task EditGameNightMessageAsync(GameNight gameNight, Embed embed, MessageComponent components)
    {
        if (await Context.Channel.GetMessageAsync(gameNight.MessageId) is not IUserMessage message)
        {
            return;
        }
        await message.ModifyAsync(props =>
        {
            props.Embed = embed;
            props.Components = components;
        });
    }

    protected virtual Task EditMeetupMessageAsync(GameNight gameNight, Embed embed, MessageComponent components)
        => EditGameNightMessageAsync(gameNight, embed, components);

    public async Task HandleToggleAsync(string payload)
        => await HandleVoteAsync(payload);

    [ComponentInteraction("game-night:*")]
    public async Task HandleVoteAsync(string payload)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        try
        {
            if (!TryParsePayload(payload, out var slotId))
            {
                await FollowupAsync("❌ Dieser Game Night ist nicht mehr verfügbar.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var slot = await _repository.GetSlotAsync(slotId);
            if (slot == null)
            {
                await FollowupAsync("❌ Dieser Game Night ist nicht mehr verfügbar.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var gameNight = await _repository.GetAsync(slot.GameNightId);
            if (gameNight == null)
            {
                await FollowupAsync("❌ Dieser Game Night ist nicht mehr verfügbar.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var now = DateTime.UtcNow;
            if (!GameNightMessageBuilder.IsActive(gameNight, now))
            {
                await FollowupAsync("⌛ Diese Game Night ist bereits beendet.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var userId = Context.User.Id;
            var current = await _repository.GetVoteAsync(gameNight.Id, slot.Id, userId);
            string confirmation;
            if (current != null && current.Available)
            {
                await _repository.RemoveVoteAsync(gameNight.Id, slot.Id, userId).ConfigureAwait(false);
                confirmation = "❎ Du bist nicht mehr verfügbar.";
            }
            else
            {
                await _repository.SetVoteAsync(new GameNightVote
                {
                    GameNightId = gameNight.Id,
                    SlotId = slot.Id,
                    UserId = userId,
                    Available = true,
                    VotedAt = now
                }).ConfigureAwait(false);
                confirmation = "✅ Du bist verfügbar.";
            }

            var slots = await _repository.GetSlotsAsync(gameNight.Id);
            var votes = await _repository.GetVotesAsync(gameNight.Id);
            var (embed, components) = GameNightMessageBuilder.Build(gameNight, slots, votes, now);
            try
            {
                await EditGameNightMessageAsync(gameNight, embed, components).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || ex is TaskCanceledException)
            {
                _logger.LogWarning(ex, "Could not re-render game night message.");
            }

            await FollowupAsync(confirmation, ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ex is TaskCanceledException)
        {
            _logger.LogError(ex, "Error occurred while handling a game night button interaction.");
            await FollowupAsync("❌ Deine Antwort konnte nicht gespeichert werden.", ephemeral: true).ConfigureAwait(false);
        }
    }

    public Task HandleResponseAsync(string payload) => HandleVoteAsync(payload);

    private static bool TryParsePayload(string? payload, out int slotId)
    {
        slotId = 0;
        var parts = (payload ?? string.Empty).Split(':', 2);
        if (parts.Length != 2) return false;
        return int.TryParse(parts[1], out slotId) && slotId > 0;
    }
}

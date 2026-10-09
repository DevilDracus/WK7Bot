namespace WK7Bot.Modules;

using System.Net;
using Discord;
using Discord.Interactions;
using Discord.Net;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;

/// <summary>
/// Provides the <c>/spontan-treff</c> slash command that announces a last-minute hangout with one-click
/// "on my way" / "pass" buttons in the dedicated meetup channel.
/// </summary>
public class SpontanTreffModule : InteractionModuleBase<SocketInteractionContext>
{
    private const string TargetChannelName = "🎉spontan-treff";
    private const string EveryoneMention = "@everyone";
    private const int DefaultOpenMinutes = 30;
    private const int MaxOpenMinutes = 1440;

    private readonly ISpontanTreffRepository _repository;
    private readonly ILogger<SpontanTreffModule> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpontanTreffModule"/> class.
    /// </summary>
    /// <param name="repository">The repository used for meetup and answer persistence.</param>
    /// <param name="logger">The logger instance.</param>
    public SpontanTreffModule(ISpontanTreffRepository repository, ILogger<SpontanTreffModule> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resolves the <c>#🎉spontan-treff</c> channel inside the originating guild and creates it when missing.
    /// </summary>
    /// <returns>The target channel, or <see langword="null"/> when the guild is unavailable or creation is forbidden.</returns>
    protected virtual Task<ITextChannel?> GetOrCreateTreffChannelAsync()
    {
        var guild = Context.Guild;
        return guild is null
            ? Task.FromResult<ITextChannel?>(null)
            : ChannelResolver.TryGetOrCreateFeatureChannelAsync(
                guild,
                Context.Client.CurrentUser.Id,
                TargetChannelName,
                "Kurzfristige Treffen: Auf dem Weg oder Absagen mit einem Klick.",
                _logger);
    }

    /// <summary>
    /// Publishes the interactive meetup message to the target channel.
    /// </summary>
    /// <param name="channel">The channel that receives the meetup.</param>
    /// <param name="text">The message content, usually the @everyone ping.</param>
    /// <param name="embed">The meetup embed.</param>
    /// <param name="components">The button row.</param>
    /// <returns>The posted message.</returns>
    protected virtual Task<IUserMessage> PostToTreffChannelAsync(
        ITextChannel channel,
        string text,
        Embed embed,
        MessageComponent components)
        => channel.SendMessageAsync(text, embed: embed, components: components);

    /// <summary>
    /// Creates a spontaneous meetup, announces it with an @everyone ping in <c>#🎉spontan-treff</c> and opens it
    /// for one-click answers until the requested deadline.
    /// </summary>
    /// <param name="plan">What is happening, e.g. "Kurzer Abendspaziergang".</param>
    /// <param name="ort">Optional meeting point, e.g. "Innenhof".</param>
    /// <param name="minuten">How many minutes the meetup stays open; defaults to 30.</param>
    /// <returns>A task tracking the asynchronous command execution.</returns>
    [SlashCommand("spontan-treff", "Postet einen Spontan-Treff mit Auf-dem-Weg- und Absage-Buttons.")]
    public async Task CreateAsync(
        [Summary("plan", "Worum geht's? z. B. \"Kurzer Abendspaziergang\"")] string plan,
        [Summary("ort", "Optional: Treffpunkt, z. B. \"Innenhof\"")] string? ort = null,
        [Summary("minuten", "Wie lange die Anfrage offen bleibt (1-1440, Standard 30)")] int minuten = DefaultOpenMinutes)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            if (string.IsNullOrWhiteSpace(plan))
            {
                await FollowupAsync("❌ Bitte gib an, worum es geht, z. B. `/spontan-treff plan:Kaffee im Innenhof`.", ephemeral: true);
                return;
            }

            if (minuten < 1 || minuten > MaxOpenMinutes)
            {
                await FollowupAsync($"❌ Die Dauer muss zwischen 1 und {MaxOpenMinutes} Minuten liegen.", ephemeral: true);
                return;
            }

            var targetChannel = await GetOrCreateTreffChannelAsync();
            if (targetChannel == null)
            {
                await FollowupAsync($"❌ Der Kanal `#{TargetChannelName}` konnte auf diesem Server nicht gefunden oder erstellt werden.", ephemeral: true);
                return;
            }

            var meetup = await _repository.AddAsync(new SpontanTreff
            {
                GuildId = Context.Guild?.Id ?? 0,
                ChannelId = targetChannel.Id,
                OrganizerId = Context.User.Id,
                OrganizerName = Context.User.Username,
                Plan = plan.Trim(),
                Location = string.IsNullOrWhiteSpace(ort) ? null : ort.Trim(),
                CreatedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.AddMinutes(minuten)
            });

            var (embed, components) = SpontanTreffMessageBuilder.Build(meetup, new List<SpontanTreffResponse>(), DateTime.Now);

            try
            {
                var message = await PostMeetupAsync(targetChannel, embed, components);
                await _repository.SetMessageAsync(meetup.Id, targetChannel.Id, message.Id);

                await FollowupAsync(
                    $"✅ Spontan-Treff gepostet in {targetChannel.Mention}: [Zum Treff](https://discord.com/channels/{meetup.GuildId}/{targetChannel.Id}/{message.Id})",
                    ephemeral: true);
            }
            catch
            {
                // The meetup was persisted before the post; leaving it behind would strand a row
                // without a message for the expiry sweep to trip over every minute. Cleanup is
                // best effort: a failing cleanup must not replace the original error.
                try
                {
                    await _repository.RemoveAsync(meetup.Id);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx, "Could not remove the orphaned Spontan-Treff {MeetupId} after the post failed.", meetup.Id);
                }

                throw;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error occurred while executing the /spontan-treff slash command.");
            await FollowupAsync("❌ Der Spontan-Treff konnte nicht erstellt werden.", ephemeral: true);
        }
    }

    /// <summary>
    /// Posts the meetup with the @everyone ping, falling back to a plain message when the bot is not allowed to
    /// mention everyone in that channel.
    /// </summary>
    private async Task<IUserMessage> PostMeetupAsync(ITextChannel channel, Embed embed, MessageComponent components)
    {
        try
        {
            return await PostToTreffChannelAsync(channel, EveryoneMention, embed, components);
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            _logger.LogWarning(ex, "Posting the Spontan-Treff without the @everyone ping because mentioning everyone is not allowed.");
            return await PostToTreffChannelAsync(channel, string.Empty, embed, components);
        }
    }
}

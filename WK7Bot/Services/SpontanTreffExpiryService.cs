namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;

/// <summary>
/// Background service that closes spontaneous meetups once their deadline passed: the meetup is marked as closed
/// and its message is re-rendered without buttons so late taps are visibly over.
/// </summary>
public class SpontanTreffExpiryService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DiscordSocketClient _discordClient;
    private readonly FeatureHealthTracker _health;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<SpontanTreffExpiryService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpontanTreffExpiryService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to create database scopes.</param>
    /// <param name="discordClient">The connected Discord socket client instance.</param>
    /// <param name="health">The feature health tracker recording the outcome of each sweep.</param>
    /// <param name="options">The strongly-typed application configuration options.</param>
    /// <param name="logger">The logger instance for background execution diagnostics.</param>
    public SpontanTreffExpiryService(
        IServiceProvider serviceProvider,
        DiscordSocketClient discordClient,
        FeatureHealthTracker health,
        IOptions<Wk7BotOptions> options,
        ILogger<SpontanTreffExpiryService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs the expiry loop, sweeping every minute for meetups whose deadline has passed.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token monitored for background service shutdown.</param>
    /// <returns>A task representing the background execution process.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Features.SpontanTreffEnabled)
        {
            _logger.LogInformation("Spontan-Treff expiry service is disabled via feature options.");
            return;
        }

        _logger.LogInformation("Starting Spontan-Treff expiry scheduler loop.");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                if (await SweepAsync(DateTime.Now, stoppingToken))
                {
                    _health.RecordSuccess(FeatureKeys.SpontanTreff);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while expiring spontaneous meetups.");
                _health.RecordFailure(FeatureKeys.SpontanTreff, ex.Message);
            }
        }
    }

    /// <summary>
    /// Closes every meetup whose deadline has passed. Each meetup is handled independently so a single deleted
    /// Discord message does not block the remaining sweep.
    /// </summary>
    /// <param name="now">The current local date and time.</param>
    /// <param name="stoppingToken">Cancellation token for network and database operations.</param>
    /// <returns><see langword="true"/> when every expired meetup was processed cleanly.</returns>
    protected virtual async Task<bool> SweepAsync(DateTime now, CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISpontanTreffRepository>();

        var expired = await repository.GetExpiredAsync(now, stoppingToken);
        var allClean = true;
        foreach (var meetup in expired)
        {
            stoppingToken.ThrowIfCancellationRequested();

            await repository.CloseAsync(meetup.Id, stoppingToken);
            meetup.Closed = true;

            try
            {
                var responses = await repository.GetResponsesAsync(meetup.Id, stoppingToken);
                var (embed, components) = SpontanTreffMessageBuilder.Build(meetup, responses, now);
                await EditMeetupMessageAsync(meetup, embed, components);
            }
            catch (Exception ex)
            {
                allClean = false;
                _logger.LogWarning(ex, "Could not update the closed Spontan-Treff message {MeetupId}.", meetup.Id);
                _health.RecordFailure(FeatureKeys.SpontanTreff, ex.Message);
            }
        }

        if (expired.Count > 0)
        {
            _logger.LogInformation("Closed {Count} expired Spontan-Treff meetup(s).", expired.Count);
        }

        return allClean;
    }

    /// <summary>
    /// Removes the buttons from an expired meetup message and marks it as over. Virtual for testability.
    /// </summary>
    /// <param name="meetup">The meetup whose message is closed.</param>
    /// <param name="embed">The closed state embed.</param>
    /// <param name="components">The empty component row that removes the buttons.</param>
    /// <returns>A task that completes once the message has been edited.</returns>
    protected virtual async Task EditMeetupMessageAsync(SpontanTreff meetup, Embed embed, MessageComponent components)
    {
        if (_discordClient.GetChannel(meetup.ChannelId) is not IMessageChannel channel)
        {
            return;
        }

        if (await channel.GetMessageAsync(meetup.MessageId) is not IUserMessage message)
        {
            return;
        }

        await message.ModifyAsync(props =>
        {
            props.Embed = embed;
            props.Components = components;
        });
    }
}

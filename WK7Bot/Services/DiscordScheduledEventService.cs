namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Creates native Discord guild scheduled events for Spontan-Treff and the weekend digest, records
/// them in SQLite, and deletes them after their end time. Registered as a singleton because the
/// weekend digest dispatcher is a singleton; the scoped repository is resolved through a scope.
/// </summary>
public class DiscordScheduledEventService : IDiscordScheduledEventService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FeatureGate _featureGate;
    private readonly ILogger<DiscordScheduledEventService> _logger;

    /// <summary>Initializes a new instance of the <see cref="DiscordScheduledEventService"/> class.</summary>
    public DiscordScheduledEventService(
        DiscordSocketClient discordClient,
        IServiceScopeFactory scopeFactory,
        FeatureGate featureGate,
        ILogger<DiscordScheduledEventService> logger)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _featureGate = featureGate ?? throw new ArgumentNullException(nameof(featureGate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ulong?> CreateAsync(
        IGuild guild,
        string name,
        string? description,
        string? location,
        DateTimeOffset start,
        DateTimeOffset end,
        string kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(guild);
        if (!_featureGate.IsEnabled(FeatureKeys.DiscordEvents))
        {
            return null;
        }

        if (end <= start)
        {
            end = start.AddMinutes(5);
        }

        try
        {
            var guildEvent = await guild.CreateEventAsync(
                name,
                start,
                GuildScheduledEventType.External,
                privacyLevel: GuildScheduledEventPrivacyLevel.Private,
                description: description,
                endTime: end,
                location: string.IsNullOrWhiteSpace(location) ? "Discord" : location);

            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IDiscordScheduledEventRepository>();
            await repository.AddAsync(new DiscordScheduledEvent
            {
                GuildId = guild.Id,
                EventId = guildEvent.Id,
                Kind = kind,
                Name = name,
                EndAt = end.UtcDateTime,
                CreatedAt = DateTime.UtcNow
            }, cancellationToken);

            _logger.LogInformation("Created Discord scheduled event '{Name}' ({EventId}) for guild {GuildId}.", name, guildEvent.Id, guild.Id);
            return guildEvent.Id;
        }
        catch (Discord.Net.HttpException ex)
        {
            // The bot may lack MANAGE_EVENTS or the guild may have reached the event limit; a warning
            // keeps this out of the error-notification pipeline because the feature still works.
            _logger.LogWarning(ex, "Could not create the Discord scheduled event '{Name}' for guild {GuildId}.", name, guild.Id);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Unexpected error while creating the Discord scheduled event '{Name}'.", name);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        if (!_featureGate.IsEnabled(FeatureKeys.DiscordEvents))
        {
            return 0;
        }

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDiscordScheduledEventRepository>();
        var expired = await repository.GetExpiredAsync(DateTime.UtcNow, cancellationToken);
        var deleted = 0;

        foreach (var record in expired)
        {
            var guild = _discordClient.GetGuild(record.GuildId);
            var guildEvent = guild?.Events.FirstOrDefault(e => e.Id == record.EventId);
            if (guildEvent is not null)
            {
                try
                {
                    await guildEvent.DeleteAsync();
                    deleted++;
                }
                catch (Discord.Net.HttpException ex)
                {
                    _logger.LogDebug(ex, "Could not delete expired Discord scheduled event {EventId}.", record.EventId);
                }
            }
        }

        // Batch-remove all expired records in one round-trip instead of N.
        if (expired.Count > 0)
        {
            await repository.RemoveRangeAsync(expired.Select(r => r.Id), cancellationToken);
        }

        return deleted;
    }
}

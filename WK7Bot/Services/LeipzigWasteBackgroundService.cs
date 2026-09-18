namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Background service that periodically checks for waste collections that occurred on the previous day
/// using <see cref="ILeipzigWasteService"/> and posts confirmation notifications to a dedicated Discord channel.
/// </summary>
public class LeipzigWasteBackgroundService : BackgroundService
{
    private const string TargetChannelName = "🗑️-leipzig-waste";
    private readonly DiscordSocketClient _discordClient;
    private readonly ILeipzigWasteService _wasteService;
    private readonly ILogger<LeipzigWasteBackgroundService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LeipzigWasteBackgroundService"/> class.
    /// </summary>
    /// <param name="discordClient">The connected Discord socket client instance.</param>
    /// <param name="wasteService">The Leipzig waste schedule parser service.</param>
    /// <param name="logger">The logger instance for background execution diagnostics.</param>
    public LeipzigWasteBackgroundService(
        DiscordSocketClient discordClient,
        ILeipzigWasteService wasteService,
        ILogger<LeipzigWasteBackgroundService> logger)
    {
        _discordClient = discordClient;
        _wasteService = wasteService;
        _logger = logger;
    }

    /// <summary>
    /// Runs the daily schedule loop, checking yesterday's waste collections every morning at 08:00 AM.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token monitored for background service shutdown.</param>
    /// <returns>A task representing the background execution process.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                var nextRunTime = now.Hour >= 8
                    ? now.Date.AddDays(1).AddHours(8)
                    : now.Date.AddHours(8);

                var delay = nextRunTime - now;

                _logger.LogInformation("Waste notification check scheduled for {NextRunTime}", nextRunTime);

                await Task.Delay(delay, stoppingToken);

                await CheckAndSendWasteNotificationsAsync(stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing the Leipzig waste schedule.");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    /// <summary>
    /// Queries yesterday's waste collections via the waste service and sends Discord notification embeds.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for network operations.</param>
    /// <returns>A task representing the asynchronous notification process.</returns>
    private async Task CheckAndSendWasteNotificationsAsync(CancellationToken cancellationToken)
    {
        var yesterday = DateTime.Today.AddDays(-1);
        var collectionsYesterday = await _wasteService.GetWasteTypesForDateAsync(yesterday, cancellationToken);

        if (collectionsYesterday.Count == 0)
        {
            return;
        }

        foreach (var guild in _discordClient.Guilds)
        {
            var channel = await GetOrCreateWasteChannelAsync(guild);
            var embed = BuildWasteNotificationEmbed(collectionsYesterday, yesterday);

            await channel.SendMessageAsync(embed: embed);
        }
    }

    /// <summary>
    /// Constructs a structured Discord Embed confirming the waste pickup from the previous day.
    /// </summary>
    /// <param name="wasteTypes">The list of waste collection types that were picked up yesterday.</param>
    /// <param name="date">The date on which the collection occurred.</param>
    /// <returns>The constructed embed ready for channel broadcast.</returns>
    private Embed BuildWasteNotificationEmbed(List<string> wasteTypes, DateTime date)
    {
        var formattedWasteList = string.Join("\n• ", wasteTypes);

        return new EmbedBuilder()
            .WithTitle("🗑️ Müllabholung Bestätigung")
            .WithDescription($"Gestern (**{date:dd.MM.yyyy}**) wurden folgende Tonnen abgeholt:\n\n• {formattedWasteList}\n\n Die Tonne(n) sollten nun leer sein!")
            .WithColor(Color.DarkGreen)
            .WithCurrentTimestamp()
            .Build();
    }

    /// <summary>
    /// Checks for an existing waste notification channel in the guild or creates a new read-only channel.
    /// </summary>
    /// <param name="guild">The target guild where channel existence is evaluated.</param>
    /// <returns>A task representing the asynchronous channel creation or retrieval operation, returning the text channel instance.</returns>
    private async Task<ITextChannel> GetOrCreateWasteChannelAsync(SocketGuild guild)
    {
        var existingChannel = guild.TextChannels.FirstOrDefault(c => c.Name.Equals(TargetChannelName, StringComparison.OrdinalIgnoreCase));
        if (existingChannel != null)
        {
            return existingChannel;
        }

        return await guild.CreateTextChannelAsync(TargetChannelName, properties =>
        {
            properties.Topic = "Benachrichtigungen und Bestätigungen zur Stadtreinigung Leipzig Müllabholung.";
            properties.PermissionOverwrites = new List<Overwrite>
            {
                new(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    readMessageHistory: PermValue.Allow,
                    sendMessages: PermValue.Deny,
                    addReactions: PermValue.Allow
                )),
                new(_discordClient.CurrentUser.Id, PermissionTarget.User, new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    readMessageHistory: PermValue.Allow,
                    sendMessages: PermValue.Allow,
                    embedLinks: PermValue.Allow
                ))
            };
        });
    }
}
namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Globalization;

/// <summary>
/// Background service that periodically checks for Leipzig waste collections,
/// posting daily confirmation notifications and weekly collection overviews to a dedicated Discord channel.
/// </summary>
public class LeipzigWasteBackgroundService : BackgroundService
{
    private const string TargetChannelName = "🗑️-leipzig-waste";
    private readonly DiscordSocketClient _discordClient;
    private readonly ILeipzigWasteService _wasteService;
    private readonly ILogger<LeipzigWasteBackgroundService> _logger;

    private DateTime _lastDailyNotificationDate = DateTime.MinValue;
    private DateTime _lastWeeklyOverviewDate = DateTime.MinValue;

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
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _wasteService = wasteService ?? throw new ArgumentNullException(nameof(wasteService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs the schedule loop, evaluating daily checks every morning at 08:00 AM and weekly overviews on Mondays at 09:00 AM.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token monitored for background service shutdown.</param>
    /// <returns>A task representing the background execution process.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Leipzig Waste Background Service scheduler loop.");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var now = DateTime.Now;

                if (now.Hour >= 8 && _lastDailyNotificationDate.Date < now.Date)
                {
                    await CheckAndSendWasteNotificationsAsync(stoppingToken);
                    _lastDailyNotificationDate = now.Date;
                }

                if (now.DayOfWeek == DayOfWeek.Monday && now.Hour >= 9 && _lastWeeklyOverviewDate.Date < now.Date)
                {
                    await SendWeeklyWasteOverviewAsync(stoppingToken);
                    _lastWeeklyOverviewDate = now.Date;
                }
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing the Leipzig waste schedule evaluation loop.");
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
    /// Queries collection dates for the upcoming 7 days starting from Monday and sends a weekly overview embed to target Discord channels.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for network operations.</param>
    /// <returns>A task representing the asynchronous weekly overview process.</returns>
    private async Task SendWeeklyWasteOverviewAsync(CancellationToken cancellationToken)
    {
        var monday = DateTime.Today;
        var embed = await BuildWeeklyOverviewEmbedAsync(monday, cancellationToken);

        foreach (var guild in _discordClient.Guilds)
        {
            var channel = await GetOrCreateWasteChannelAsync(guild);
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
    /// Constructs a structured Discord Embed summarizing upcoming waste collections for the next 7 days in the style of the interaction module.
    /// </summary>
    /// <param name="startDate">The starting Monday date for the weekly overview evaluation.</param>
    /// <param name="cancellationToken">Cancellation token for calendar queries.</param>
    /// <returns>A task returning the constructed weekly overview embed.</returns>
    private async Task<Embed> BuildWeeklyOverviewEmbedAsync(DateTime startDate, CancellationToken cancellationToken)
    {
        var germanCulture = new CultureInfo("de-DE");
        var endDate = startDate.AddDays(6);

        var embedBuilder = new EmbedBuilder()
            .WithTitle("🗑️ Stadtreinigung Leipzig — Wochenübersicht")
            .WithDescription($"Anstehende Müllabholungen für die Woche vom **{startDate:dd.MM.yyyy}** bis **{endDate:dd.MM.yyyy}**:")
            .WithColor(Color.Blue)
            .WithCurrentTimestamp();

        var foundAny = false;

        for (var i = 0; i < 7; i++)
        {
            var targetDate = startDate.AddDays(i);
            var collections = await _wasteService.GetWasteTypesForDateAsync(targetDate, cancellationToken);

            if (collections.Count == 0)
            {
                continue;
            }

            foundAny = true;

            var dayLabel = i switch
            {
                0 => "Heute (Montag)",
                _ => targetDate.ToString("dddd", germanCulture)
            };

            var formattedText = string.Join("\n• ", collections);

            embedBuilder.AddField($"{dayLabel} ({targetDate:dd.MM.yyyy})", $"• {formattedText}", inline: false);
        }

        if (!foundAny)
        {
            embedBuilder.WithDescription($"In der Woche vom **{startDate:dd.MM.yyyy}** bis **{endDate:dd.MM.yyyy}** stehen keine Müllabholungen an.");
        }

        return embedBuilder.Build();
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
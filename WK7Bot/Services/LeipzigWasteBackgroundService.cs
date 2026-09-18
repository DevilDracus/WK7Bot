namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Ical.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Background service that periodically fetches the Stadtreinigung Leipzig ICS feed,
/// checks for waste collections that occurred on the previous day, and posts reminder notifications to a dedicated Discord channel.
/// </summary>
public class LeipzigWasteBackgroundService : BackgroundService
{
    private const string TargetChannelName = "🗑️-leipzig-waste";
    private readonly DiscordSocketClient _discordClient;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LeipzigWasteBackgroundService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LeipzigWasteBackgroundService"/> class.
    /// </summary>
    /// <param name="discordClient">The connected Discord socket client instance.</param>
    /// <param name="httpClient">The HTTP client instance for downloading external web resources.</param>
    /// <param name="configuration">The application configuration root containing feed endpoints.</param>
    /// <param name="logger">The logger instance for background execution diagnostics.</param>
    public LeipzigWasteBackgroundService(
        DiscordSocketClient discordClient,
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<LeipzigWasteBackgroundService> logger)
    {
        _discordClient = discordClient;
        _httpClient = httpClient;
        _configuration = configuration;
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
                var nextRunTime = now.Date.AddDays(1).AddHours(8);
                var delay = nextRunTime - now;

                _logger.LogInformation("Waste notification check scheduled for {NextRunTime}", nextRunTime);

                await CheckAndSendWasteNotificationsAsync(stoppingToken);

                await Task.Delay(delay, stoppingToken);
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
    /// Downloads the ICS calendar, identifies events matching yesterday's date, and sends Discord notification embeds.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for network operations.</param>
    /// <returns>A task representing the asynchronous notification process.</returns>
    private async Task CheckAndSendWasteNotificationsAsync(CancellationToken cancellationToken)
    {
        var yesterday = DateTime.Today.AddDays(-1);
        var collectionsYesterday = await GetWasteTypesForDateAsync(yesterday, cancellationToken);

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
    /// Downloads and parses the ICS feed to retrieve waste collection summaries for a specific target date.
    /// </summary>
    /// <param name="targetDate">The target calendar date to evaluate.</param>
    /// <param name="cancellationToken">Cancellation token for network operations.</param>
    /// <returns>A list of friendly waste collection names found on the target date.</returns>
    private async Task<List<string>> GetWasteTypesForDateAsync(DateTime targetDate, CancellationToken cancellationToken)
    {
        var detectedWasteTypes = new List<string>();
        var feedUrl = _configuration["LeipzigWaste:IcsFeedUrl"];

        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            _logger.LogError("Stadtreinigung Leipzig ICS feed URL is not configured in appsettings.json.");
            return detectedWasteTypes;
        }

        try
        {
            using var response = await _httpClient.GetAsync(feedUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var contentString = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!contentString.StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Retrieved payload from Leipzig waste endpoint does not appear to be a valid ICS calendar stream.");
                return detectedWasteTypes;
            }

            var calendar = Calendar.Load(contentString);

            foreach (var calendarEvent in calendar.Events)
            {
                if (calendarEvent.Start.Value.Date == targetDate.Date)
                {
                    var friendlyName = MapWasteSummaryToFriendlyName(calendarEvent.Summary);
                    detectedWasteTypes.Add(friendlyName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download or parse the Stadtreinigung Leipzig ICS feed from {FeedUrl}", feedUrl);
        }

        return detectedWasteTypes;
    }

    /// <summary>
    /// Maps raw ICS event summaries into user-friendly German notification text with corresponding emojis.
    /// </summary>
    /// <param name="rawSummary">The raw summary text extracted from the ICS event.</param>
    /// <returns>A formatted display string describing the waste type.</returns>
    private string MapWasteSummaryToFriendlyName(string rawSummary)
    {
        if (rawSummary.Contains("Restabfall", StringComparison.OrdinalIgnoreCase) || rawSummary.Contains("schwarz", StringComparison.OrdinalIgnoreCase))
        {
            return "⬛ Schwarze Tonne (Restabfall)";
        }
        if (rawSummary.Contains("Papier", StringComparison.OrdinalIgnoreCase) || rawSummary.Contains("blau", StringComparison.OrdinalIgnoreCase))
        {
            return "🟦 Blaue Tonne (Pappe & Papier)";
        }
        if (rawSummary.Contains("Wertstoff", StringComparison.OrdinalIgnoreCase) || rawSummary.Contains("gelb", StringComparison.OrdinalIgnoreCase))
        {
            return "🟨 Gelbe Tonne / Gelber Sack (Wertstoffe)";
        }
        if (rawSummary.Contains("Bio", StringComparison.OrdinalIgnoreCase) || rawSummary.Contains("braun", StringComparison.OrdinalIgnoreCase))
        {
            return "🟫 Braune Tonne (Biogut)";
        }

        return $"🗑️ {rawSummary}";
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
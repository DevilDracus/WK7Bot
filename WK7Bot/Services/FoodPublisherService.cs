namespace WK7Bot.Services;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Background worker service executing scheduled dispatches for monthly seasonal produce lists and weekly renal recipes into Discord.
/// </summary>
public class FoodPublisherService : BackgroundService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly IGeminiFoodService _geminiFoodService;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<FoodPublisherService> _logger;

    private const string TargetChannelName = "🍎food";

    private DateTime? _lastMonthlyPostDate;
    private DateTime? _lastWeeklyRecipePostDate;

    /// <summary>
    /// Initializes a new instance of the <see cref="FoodPublisherService"/> class.
    /// </summary>
    /// <param name="discordClient">The Discord socket client connection instance.</param>
    /// <param name="geminiFoodService">The Gemini AI food and recipe service dependency.</param>
    /// <param name="options">Application options instance.</param>
    /// <param name="logger">Logger instance.</param>
    public FoodPublisherService(
        DiscordSocketClient discordClient,
        IGeminiFoodService geminiFoodService,
        IOptions<Wk7BotOptions> options,
        ILogger<FoodPublisherService> logger)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _geminiFoodService = geminiFoodService ?? throw new ArgumentNullException(nameof(geminiFoodService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Core polling loop continuously checking time anchors for monthly produce and Thursday recipe schedules.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token used to interrupt processing during app shutdown.</param>
    /// <returns>A task tracking execution state.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Features.FoodServiceEnabled)
        {
            _logger.LogInformation("Food publisher background service is disabled in configuration.");
            return;
        }

        _logger.LogInformation("Food publisher background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;

                if (ShouldPublishMonthlyProduce(now))
                {
                    await PublishMonthlyProduceAsync(now, stoppingToken);
                }

                if (ShouldPublishWeeklyRecipe(now))
                {
                    await PublishWeeklyRecipeAsync(now, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error encountered while checking or executing food background jobs.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    /// <summary>
    /// Determines whether the monthly produce post should trigger on the first day of the current month.
    /// </summary>
    private bool ShouldPublishMonthlyProduce(DateTime now)
    {
        if (now.Day != 1) return false;
        if (now.Hour != 9 || now.Minute != 0) return false;

        return !_lastMonthlyPostDate.HasValue || _lastMonthlyPostDate.Value.Month != now.Month;
    }

    /// <summary>
    /// Determines whether the weekly renal recipe should trigger on Thursday at 15:30.
    /// </summary>
    private bool ShouldPublishWeeklyRecipe(DateTime now)
    {
        if (now.DayOfWeek != DayOfWeek.Thursday) return false;
        if (now.Hour != 15 || now.Minute != 30) return false;

        return !_lastWeeklyRecipePostDate.HasValue || _lastWeeklyRecipePostDate.Value.Date != now.Date;
    }

    /// <summary>
    /// Queries seasonal produce data and posts the embedded payload to the target Discord channel.
    /// </summary>
    private async Task PublishMonthlyProduceAsync(DateTime now, CancellationToken cancellationToken)
    {
        var channel = ResolveTargetChannel();
        if (channel == null) return;

        _logger.LogInformation("Generating monthly seasonal produce list for {Month}", now.ToString("MMMM"));

        var produce = await _geminiFoodService.GetSeasonalProduceAsync(now, cancellationToken);
        if (produce == null) return;

        var embed = new EmbedBuilder()
            .WithTitle($"🍎 Seasonal Produce Guide — {produce.Month}")
            .WithDescription("Fresh seasonal fruits, vegetables, herbs, and nuts for this month (Central Europe region):")
            .WithColor(Color.Green)
            .AddField("🍏 Fruits", produce.Fruits.Count > 0 ? string.Join(", ", produce.Fruits) : "None", false)
            .AddField("🥦 Vegetables", produce.Vegetables.Count > 0 ? string.Join(", ", produce.Vegetables) : "None", false)
            .AddField("🌿 Herbs", produce.Herbs.Count > 0 ? string.Join(", ", produce.Herbs) : "None", false)
            .AddField("🥜 Nuts", produce.Nuts.Count > 0 ? string.Join(", ", produce.Nuts) : "None", false)
            .WithFooter("WK7 Bot Seasonal Food Service")
            .WithCurrentTimestamp()
            .Build();

        await channel.SendMessageAsync(embed: embed);
        _lastMonthlyPostDate = now;
    }

    /// <summary>
    /// Queries renal recipe data and posts the embedded payload to the target Discord channel.
    /// </summary>
    private async Task PublishWeeklyRecipeAsync(DateTime now, CancellationToken cancellationToken)
    {
        var channel = ResolveTargetChannel();
        if (channel == null) return;

        _logger.LogInformation("Generating weekly renal/dialysis recipe for {Date}", now.ToShortDateString());

        var recipe = await _geminiFoodService.GetWeeklyRenalRecipeAsync(now, cancellationToken);
        if (recipe == null) return;

        var n = recipe.Nutrition;
        string nutritionSummary = $"• **Calories:** {n.CaloriesKcal} kcal\n" +
                                 $"• **Protein:** {n.ProteinGrams} g | **Carbs:** {n.CarbohydratesGrams} g | **Fat:** {n.FatGrams} g\n" +
                                 $"• **Sodium:** {n.SodiumMg} mg\n" +
                                 $"• **Potassium (Kalium):** {n.PotassiumKaliumMg} mg\n" +
                                 $"• **Sulfate:** {n.SulfateMg} mg\n" +
                                 $"• **Phosphorus:** {n.PhosphorusMg} mg";

        string ingredientsFormatted = string.Join("\n", recipe.Ingredients.Select(i => $"• {i}"));
        string instructionsFormatted = string.Join("\n", recipe.Instructions.Select((inst, idx) => $"{idx + 1}. {inst}"));

        var embed = new EmbedBuilder()
            .WithTitle($"🥗 Recipe of the Week: {recipe.Title}")
            .WithDescription($"{recipe.Description}\n\n⏱️ **Prep Time:** {recipe.PrepTime} | 🍳 **Cook Time:** {recipe.CookTime} | 🍽️ **Servings:** {recipe.Servings}")
            .WithColor(Color.Teal)
            .AddField("🛒 Ingredients", ingredientsFormatted.Length > 1024 ? ingredientsFormatted[..1021] + "..." : ingredientsFormatted, false)
            .AddField("👨‍🍳 Preparation Steps", instructionsFormatted.Length > 1024 ? instructionsFormatted[..1021] + "..." : instructionsFormatted, false)
            .AddField("📊 Nutrition per Serving", nutritionSummary, false)
            .AddField("🛡️ Safety & Renal Notes", recipe.TransplantSafetyNotes, false)
            .WithFooter("Tailored for Dialysis & Kidney Transplant Safety")
            .WithCurrentTimestamp()
            .Build();

        await channel.SendMessageAsync(embed: embed);
        _lastWeeklyRecipePostDate = now;
    }

    /// <summary>
    /// Resolves the socket text channel matching the configured target channel name.
    /// </summary>
    private SocketTextChannel? ResolveTargetChannel()
    {
        foreach (var guild in _discordClient.Guilds)
        {
            var channel = guild.TextChannels.FirstOrDefault(c => string.Equals(c.Name, TargetChannelName, StringComparison.OrdinalIgnoreCase));
            if (channel != null) return channel;
        }

        _logger.LogWarning("Target channel '{ChannelName}' could not be resolved in any available guild.", TargetChannelName);
        return null;
    }
}
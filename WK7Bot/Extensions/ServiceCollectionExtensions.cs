using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using WK7Bot.Options;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;

namespace WK7Bot.Extensions;

/// <summary>
/// Extension methods for configuring application services, data stores, and background workers modularly.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Entity Framework Core SQLite database services.
    /// </summary>
    /// <param name="services">The service collection instance.</param>
    /// <param name="connectionString">The SQLite database connection string.</param>
    /// <returns>The service collection instance for method chaining.</returns>
    public static IServiceCollection AddBotDatabase(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<BotDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IRssRepository, RssRepository>();
        services.AddScoped<IWasteDispatchRepository, WasteDispatchRepository>();
        return services;
    }

    /// <summary>
    /// Registers Discord core socket clients, interaction frameworks, and API clients.
    /// </summary>
    /// <param name="services">The service collection instance.</param>
    /// <returns>The service collection instance for method chaining.</returns>
    public static IServiceCollection AddBotDiscordAndClients(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<IRandomSource, SystemRandomSource>();
        services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent | GatewayIntents.GuildMembers | GatewayIntents.GuildPresences,
            AlwaysDownloadUsers = true
        }));

        services.AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()));
        
        services.AddHttpClient<IHomeAssistantService, HomeAssistantService>();
        services.AddHttpClient<ILeipzigWasteService, LeipzigWasteService>();
        services.AddHttpClient<ISteamService, SteamService>();
        services.AddHttpClient<IGeminiFoodService, GeminiFoodService>();
        services.AddHttpClient<IRecipeSearchService, WebRecipeSearchService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient<AlexaMentionNotificationService>();

        services.AddTransient<RssParserService>();

        services.AddSingleton<IMqttClient>(sp => new MqttClientFactory().CreateMqttClient());

        return services;
    }

    /// <summary>
    /// Conditionally registers application background hosted services, allowing features to be toggled via configuration/MQTT flags.
    /// </summary>
    /// <param name="services">The service collection instance.</param>
    /// <param name="configuration">The configuration provider.</param>
    /// <returns>The service collection instance for method chaining.</returns>
    public static IServiceCollection AddBotHostedServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Core mandatory workers
        services.AddHostedService<InteractionHandlingService>();
        services.AddHostedService<DiscordBotWorker>();

        // Feature flags. Prefer the Wk7Bot section (appsettings.json); fall back to the
        // root level (Home Assistant options.json / config.yaml). Services also re-check
        // bound IOptions at runtime as a second line of defense.
        if (IsFeatureEnabled(configuration, "rss_polling_enabled"))
        {
            services.AddHostedService<RssPollingBackgroundService>();
        }

        if (IsFeatureEnabled(configuration, "home_assistant_notifier_enabled"))
        {
            services.AddHostedService<HomeAssistantNotifierService>();
        }

        if (IsFeatureEnabled(configuration, "leipzig_waste_enabled"))
        {
            services.AddHostedService<LeipzigWasteBackgroundService>();
        }

        if (IsFeatureEnabled(configuration, "alexa_notifications_enabled"))
        {
            services.AddHostedService<AlexaMentionNotificationService>();
        }

        if (IsFeatureEnabled(configuration, "discord_presence_mqtt_enabled"))
        {
            services.AddHostedService<DiscordPresenceMqttService>();
        }

        if (IsFeatureEnabled(configuration, "food_service_enabled"))
        {
            services.AddHostedService<FoodPublisherService>();
        }

        return services;
    }

    /// <summary>
    /// Resolves a feature toggle from the Wk7Bot configuration section when present, otherwise from the root configuration.
    /// </summary>
    /// <param name="configuration">The configuration provider.</param>
    /// <param name="featureKey">The snake_case feature key (e.g. rss_polling_enabled).</param>
    /// <returns><see langword="true"/> when the feature is enabled or unset (defaults to enabled).</returns>
    private static bool IsFeatureEnabled(IConfiguration configuration, string featureKey)
    {
        var wk7Features = configuration.GetSection($"{Wk7BotOptions.SectionName}:features");
        if (wk7Features.Exists())
        {
            return wk7Features.GetValue(featureKey, true);
        }

        var rootFeatures = configuration.GetSection("features");
        if (rootFeatures.Exists())
        {
            return rootFeatures.GetValue(featureKey, true);
        }

        return true;
    }
}
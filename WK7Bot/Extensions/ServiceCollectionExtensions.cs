using System.Net;
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
        services.AddScoped<ISpontanTreffRepository, SpontanTreffRepository>();
        services.AddScoped<IWarningDispatchRepository, WarningDispatchRepository>();
        services.AddScoped<IFoodDispatchRepository, FoodDispatchRepository>();
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
        
services.AddHttpClient<IHomeAssistantService, HomeAssistantService>(client =>
{
    // The Supervisor proxy is a local service: a request still pending after this window is
    // treated as unreachable instead of hanging for the HttpClient default of 100 seconds.
    client.Timeout = TimeSpan.FromSeconds(10);
});
services.AddHttpClient<ILeipzigWasteService, LeipzigWasteService>();
        services.AddHttpClient<ISteamService, SteamService>();
        services.AddHttpClient<IBattleNetService, BattleNetService>();
        services.AddHttpClient<IGeminiFoodService, GeminiFoodService>();
        services.AddHttpClient<IRecipeSearchService, WebRecipeSearchService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient<IWeekendEventSource, LeipzigWeekendEventSource>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient<IDwdWarningService, DwdWarningService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        });
        services.AddHttpClient<AlexaMentionNotificationService>();

        services.AddTransient<RssParserService>();

        services.AddSingleton<IMqttClient>(sp => new MqttClientFactory().CreateMqttClient());

        // Owns all connect/reconnect behavior for the single shared IMqttClient: MQTTnet forbids
        // concurrent ConnectAsync calls, so both MQTT-backed services go through this gate.
        services.AddSingleton<MqttConnectionCoordinator>();

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
            // Reuse the typed-client instance from AddBotDiscordAndClients instead of registering the
            // type a second time: the typed registration is the only one that supplies the HttpClient,
            // and a second instance would never have its StartAsync called if the service were injected
            // anywhere else.
            services.AddHostedService(sp => sp.GetRequiredService<AlexaMentionNotificationService>());
        }

        if (IsFeatureEnabled(configuration, "discord_presence_mqtt_enabled"))
        {
            services.AddHostedService<DiscordPresenceMqttService>();
        }

        if (IsFeatureEnabled(configuration, "food_service_enabled"))
        {
            services.AddHostedService<FoodPublisherService>();
        }

        if (IsFeatureEnabled(configuration, "spontan_treff_enabled"))
        {
            services.AddHostedService<SpontanTreffExpiryService>();
        }

        if (IsFeatureEnabled(configuration, "weekend_digest_enabled"))
        {
            services.AddHostedService<WeekendDigestBackgroundService>();
        }

        if (IsFeatureEnabled(configuration, "dwd_warning_enabled"))
        {
            services.AddHostedService<DwdWarningBackgroundService>();
        }

        if (IsFeatureEnabled(configuration, "error_notifications_enabled"))
        {
            services.AddHostedService<ErrorNotificationDispatcher>();
        }

        return services;
    }

    /// <summary>
    /// Resolves a feature toggle per key: the Wk7Bot section wins when it defines the key, otherwise the
    /// configuration root (Home Assistant options.json / config.yaml) is consulted, otherwise the feature
    /// defaults to enabled. Falling back per key (instead of per section) means a partially configured
    /// section no longer shadows root-level feature flags.
    /// </summary>
    /// <param name="configuration">The configuration provider.</param>
    /// <param name="featureKey">The snake_case feature key (e.g. rss_polling_enabled).</param>
    /// <returns><see langword="true"/> when the feature is enabled or unset (defaults to enabled).</returns>
    private static bool IsFeatureEnabled(IConfiguration configuration, string featureKey)
    {
        var wk7Value = configuration.GetSection($"{Wk7BotOptions.SectionName}:features").GetValue<bool?>(featureKey);
        if (wk7Value.HasValue)
        {
            return wk7Value.Value;
        }

        var rootValue = configuration.GetSection("features").GetValue<bool?>(featureKey);
        if (rootValue.HasValue)
        {
            return rootValue.Value;
        }

        return true;
    }
}
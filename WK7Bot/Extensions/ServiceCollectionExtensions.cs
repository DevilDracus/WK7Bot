using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
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
        return services;
    }

    /// <summary>
    /// Registers Discord core socket clients, interaction frameworks, and API clients.
    /// </summary>
    /// <param name="services">The service collection instance.</param>
    /// <returns>The service collection instance for method chaining.</returns>
    public static IServiceCollection AddBotDiscordAndClients(this IServiceCollection services)
    {
        services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent | GatewayIntents.GuildMembers | GatewayIntents.GuildPresences,
            AlwaysDownloadUsers = true
        }));

        services.AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()));
        
        services.AddHttpClient<IHomeAssistantService, HomeAssistantService>();
        services.AddHttpClient<ILeipzigWasteService, LeipzigWasteService>();
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

        // Feature flags (can be driven by configuration values updated via MQTT/options.json)
        if (configuration.GetValue<bool>("features:rss_polling_enabled", true))
        {
            services.AddHostedService<RssPollingBackgroundService>();
        }

        if (configuration.GetValue<bool>("features:home_assistant_notifier_enabled", true))
        {
            services.AddHostedService<HomeAssistantNotifierService>();
        }

        if (configuration.GetValue<bool>("features:leipzig_waste_enabled", true))
        {
            services.AddHostedService<LeipzigWasteBackgroundService>();
        }

        if (configuration.GetValue<bool>("features:alexa_notifications_enabled", true))
        {
            services.AddHostedService<AlexaMentionNotificationService>();
        }
        
        if (configuration.GetValue<bool>("features:discord_presence_mqtt_enabled", true))
        {
            services.AddHostedService<DiscordPresenceMqttService>();
        }

        return services;
    }
}
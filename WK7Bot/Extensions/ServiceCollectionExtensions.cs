using System.Net;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
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
        services.AddScoped<IWishlistDealRepository, WishlistDealRepository>();
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

        // Single shared instance: the background services record outcomes into it and both the MQTT
        // status sensors and the health endpoint project from it, so they must observe one state.
        services.AddSingleton<FeatureHealthTracker>();

        services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent | GatewayIntents.GuildMembers | GatewayIntents.GuildPresences,
            AlwaysDownloadUsers = true
        }));

        // AutoServiceScopes is pinned explicitly because the interaction handler passes the root
        // provider to ExecuteCommandAsync: the per-command scope is then created inside
        // ExecuteInternalAsync and outlives the handler, which is what keeps scoped module
        // dependencies (the DbContext-backed repositories) alive for the whole detached execution.
        services.AddSingleton(x => new InteractionService(
            x.GetRequiredService<DiscordSocketClient>(),
            new InteractionServiceConfig { AutoServiceScopes = true }));

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

        // Gemini circuit breaker — prevents hammering Gemini when unavailable.
        services.AddSingleton<GeminiCircuitBreaker>();

        // External dependency health checker for /health command.
        services.AddScoped<IExternalDependencyHealthChecker, ExternalDependencyHealthChecker>();
        services.AddHttpClient<IExternalDependencyHealthChecker, ExternalDependencyHealthChecker>(client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient<IRecipeSearchService, WebRecipeSearchService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient<IOpenFoodFactsService, OpenFoodFactsService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHttpClient<IWishlistDealService, WishlistDealService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient<IWeekendEventSource, LeipzigWeekendEventSource>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient<IDwdWarningService, DwdWarningService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        });

        // Shared resilience policy for DWD requests (retry + circuit breaker).
        services.AddSingleton<DwdResiliencePolicy>();
        services.AddSingleton<CommandRateLimiter>();

        // Single source of truth for feature flag decisions.
        services.AddSingleton<FeatureGate>();
        services.AddHttpClient<AlexaMentionNotificationService>(client =>
        {
            // The notification dispatch runs detached from the gateway handler, so bound the wait
            // instead of keeping the request open for the HttpClient default of 100 seconds.
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddTransient<RssParserService>();

        // Command usage metrics repository
        services.AddScoped<CommandUsageRepository>();

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

        // Replicate the same two-step binding that Program.cs uses for Configure<Wk7BotOptions>:
        // first bind from the Wk7Bot section, then from root-level config (which overrides).
        // This ensures the registration layer sees the same merged options as runtime services.
        var wk7Options = new Wk7BotOptions();
        configuration.GetSection(Wk7BotOptions.SectionName).Bind(wk7Options);
        configuration.Bind(wk7Options);
        var featureGate = new FeatureGate(Microsoft.Extensions.Options.Options.Create(wk7Options));

        if (featureGate.IsEnabled(FeatureKeys.RssPolling))
        {
            services.AddHostedService<RssPollingBackgroundService>();
        }

        if (featureGate.IsEnabled(FeatureKeys.HomeAssistantNotifier))
        {
            services.AddHostedService<HomeAssistantNotifierService>();
        }

        if (featureGate.IsEnabled(FeatureKeys.LeipzigWaste))
        {
            services.AddHostedService<LeipzigWasteBackgroundService>();
        }

        if (featureGate.IsEnabled(FeatureKeys.AlexaNotifications))
        {
            // Reuse the typed-client instance from AddBotDiscordAndClients instead of registering the
            // type a second time: the typed registration is the only one that supplies the HttpClient,
            // and a second instance would never have its StartAsync called if the service were injected
            // anywhere else.
            services.AddHostedService(sp => sp.GetRequiredService<AlexaMentionNotificationService>());
        }

        if (featureGate.IsEnabled(FeatureKeys.DiscordPresenceMqtt))
        {
            services.AddHostedService<DiscordPresenceMqttService>();
        }

        if (featureGate.IsEnabled(FeatureKeys.FoodService))
        {
            services.AddHostedService<FoodPublisherService>();
        }

        if (featureGate.IsEnabled(FeatureKeys.SpontanTreff))
        {
            services.AddHostedService<SpontanTreffExpiryService>();
        }

        if (featureGate.IsEnabled(FeatureKeys.WeekendDigest))
        {
            services.AddHostedService<WeekendDigestBackgroundService>();
        }

        if (featureGate.IsEnabled(FeatureKeys.DwdWarning))
        {
            services.AddHostedService<DwdWarningBackgroundService>();
        }

        // Publishes the bot's own availability, uptime and per-feature health to Home Assistant via
        // MQTT discovery. Without this registration the entire status subsystem is dead code.
        if (featureGate.IsEnabled(FeatureKeys.MqttBotStatus))
        {
            services.AddHostedService<BotStatusMqttService>();
        }

        if (featureGate.IsEnabled(FeatureKeys.ErrorNotifications))
        {
            services.AddHostedService<ErrorNotificationDispatcher>();
        }

        if (featureGate.IsEnabled(FeatureKeys.WishlistDealAlerts))
        {
            services.AddHostedService<WishlistDealBackgroundService>();
        }

        // Always-on infrastructure services
        services.AddHostedService<StartupConfigurationValidator>();
        services.AddHostedService<DatabaseMaintenanceService>();

        return services;
    }
}
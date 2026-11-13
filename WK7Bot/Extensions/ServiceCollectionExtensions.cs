using System.Net;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
    /// <summary>Size limit (entry count) applied to the application-wide memory cache.</summary>
    private const int MemoryCacheSizeLimit = 1024;

    /// <summary>Fast HTTP timeout (seconds) for local/proxy calls.</summary>
    private const int HttpTimeoutFastSeconds = 10;

    /// <summary>Medium HTTP timeout (seconds) for moderately slow endpoints.</summary>
    private const int HttpTimeoutMediumSeconds = 15;

    /// <summary>Default HTTP timeout (seconds) for external API calls.</summary>
    private const int HttpTimeoutDefaultSeconds = 30;

    /// <summary>Slow HTTP timeout (seconds) for open-data feeds and large payloads.</summary>
    private const int HttpTimeoutSlowSeconds = 45;

    /// <summary>Extended HTTP timeout (seconds) for Gemini AI generation.</summary>
    private const int HttpTimeoutGeminiSeconds = 60;
    /// <summary>
    /// Registers Entity Framework Core SQLite database services.
    /// </summary>
    /// <param name="services">The service collection instance.</param>
    /// <param name="connectionString">The SQLite database connection string.</param>
    /// <returns>The service collection instance for method chaining.</returns>
    public static IServiceCollection AddBotDatabase(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<BotDbContext>(options =>
            options.UseSqlite(connectionString), ServiceLifetime.Singleton);

        // Factory registered so the data repositories can resolve IDbContextFactory<BotDbContext>
        // and operate on short-lived contexts, preventing context accumulation across requests.
        services.AddDbContextFactory<BotDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IRssRepository, RssRepository>();
        services.AddScoped<IWasteDispatchRepository, WasteDispatchRepository>();
        services.AddScoped<ISpontanTreffRepository, SpontanTreffRepository>();
        services.AddScoped<IGameNightRepository, GameNightRepository>();
        services.AddScoped<IGameNightsRepository, GameNightRepository>();
        services.AddScoped<IGameNightSchedulerRepository, GameNightRepository>();
        services.AddScoped<IWarningDispatchRepository, WarningDispatchRepository>();
        services.AddScoped<IFoodDispatchRepository, FoodDispatchRepository>();
        services.AddScoped<IPollenDispatchRepository, PollenDispatchRepository>();
        services.AddScoped<IWishlistDealRepository, WishlistDealRepository>();
        services.AddScoped<IUserAccountLinkRepository, UserAccountLinkRepository>();
        services.AddScoped<IDealWatchRepository, DealWatchRepository>();
        services.AddScoped<IManualWishlistRepository, ManualWishlistRepository>();
        services.AddScoped<IReleaseDateReminderRepository, ReleaseDateReminderRepository>();
        services.AddScoped<IPriceHistoryRepository, PriceHistoryRepository>();
        services.AddScoped<ISteamAchievementBaselineRepository, SteamAchievementBaselineRepository>();
        services.AddScoped<IAccountLinkResolver, AccountLinkResolver>();
        services.AddScoped<IDiscordScheduledEventRepository, DiscordScheduledEventRepository>();
        services.AddScoped<IReminderRepository, ReminderRepository>();
        services.AddScoped<IBirthdayRepository, BirthdayRepository>();
        services.AddScoped<IVoiceStatsRepository, VoiceStatsRepository>();
        services.AddScoped<ITriviaScoreRepository, TriviaScoreRepository>();
        services.AddScoped<IXpRepository, XpRepository>();
        return services;
    }

    /// <summary>
    /// Registers Discord core socket clients, interaction frameworks, and API clients.
    /// </summary>
    /// <param name="services">The service collection instance.</param>
    /// <returns>The service collection instance for method chaining.</returns>
    public static IServiceCollection AddBotDiscordAndClients(this IServiceCollection services)
    {
        services.AddMemoryCache(options => options.SizeLimit = MemoryCacheSizeLimit);
        services.AddSingleton<IRandomSource, SystemRandomSource>();

        // Single shared instance: the background services record outcomes into it and both the MQTT
        // status sensors and the health endpoint project from it, so they must observe one state.
        services.AddSingleton<IFeatureHealthTracker, FeatureHealthTracker>();
        // MetricsRegistry is registered as a single instance in Program.cs (before AddBotDiscordAndClients)
        // to ensure ErrorNotificationQueue and all other consumers share the same instance.

        services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = (GatewayIntents.AllUnprivileged & ~GatewayIntents.GuildScheduledEvents & ~GatewayIntents.GuildInvites) | GatewayIntents.MessageContent | GatewayIntents.GuildMembers | GatewayIntents.GuildPresences,
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
            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutFastSeconds);
        });
        services.AddHttpClient<ILeipzigWasteService, LeipzigWasteService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutDefaultSeconds);
        });
        services.AddHttpClient<ISteamService, SteamService>(client =>
        {
            // Bound external Steam calls so one slow API response cannot block the presence refresh
            // for the HttpClient default of 100 seconds. 30 s matches the API-grade default ceiling.
            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutDefaultSeconds);
        });
        services.AddHttpClient<IBattleNetService, BattleNetService>(client =>
        {
            // Keep the Battle.net profile refresh from stalling on slow external responses.
            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutDefaultSeconds);
        });
        services.AddHttpClient<IGeminiFoodService, GeminiFoodService>(client => client.Timeout = TimeSpan.FromSeconds(HttpTimeoutGeminiSeconds));

        // Gemini circuit breaker — prevents hammering Gemini when unavailable.
        services.AddSingleton<ICircuitBreaker, GeminiCircuitBreaker>();

        // External dependency health checker for /health command.
        services.AddHttpClient<IExternalDependencyHealthChecker, ExternalDependencyHealthChecker>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutFastSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WK7Bot/1.0");
        });
        services.AddHttpClient<IRecipeSearchService, WebRecipeSearchService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutDefaultSeconds);
        });
        services.AddHttpClient<IOpenFoodFactsService, OpenFoodFactsService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutMediumSeconds);
        });
        services.AddHttpClient<IWishlistDealService, WishlistDealService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutDefaultSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WK7Bot/1.0");
        });
        services.AddHttpClient<ISteamStoreService, SteamStoreService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutDefaultSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WK7Bot/1.0");
        });
        services.AddHttpClient<AddOnOptionsWriter>(client =>
        {
            client.BaseAddress = new Uri("http://supervisor/");
            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutFastSeconds);
        });
        services.AddHttpClient<IAccountLinkService, AccountLinkService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutMediumSeconds);
        });
        services.AddTransient<IWeatherService, WeatherService>();
        services.AddTransient<ILeaderboardService, LeaderboardService>();
        services.AddTransient<IProfileService, ProfileService>();
        services.AddSingleton<IDiscordScheduledEventService, DiscordScheduledEventService>();
        services.AddHttpClient<IDealWatchlistService, DealWatchlistService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutDefaultSeconds);
        });
        services.AddHttpClient<IWeekendEventSource, LeipzigWeekendEventSource>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutDefaultSeconds);
        });
        services.AddHttpClient<IPollenService, PollenService>(client =>
        {
            // Same host as the CAP warnings: a slow open-data response must not hang the caller.
            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutSlowSeconds);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            // Same rationale as DWD: bypass any proxy inherited from the HA container environment.
            UseProxy = false
        });
        services.AddHttpClient<IDwdWarningService, DwdWarningService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutSlowSeconds);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            // The HA add-on container may inherit a broken HTTP_PROXY; bypass it for DWD so the
            // open-data feed is reached directly instead of stalling on a proxy.
            UseProxy = false
        });

        // Shared resilience policy for DWD requests (retry + circuit breaker).
        services.AddSingleton<DwdResiliencePolicy>();
        services.AddSingleton<CommandRateLimiter>();

        // Single source of truth for feature flag decisions.
        services.AddSingleton<FeatureGate>(sp => new FeatureGate(sp.GetRequiredService<IOptions<Wk7BotOptions>>()));
        services.AddHttpClient<AlexaMentionNotificationService>(client =>
        {
            // The notification dispatch runs detached from the gateway handler, so bound the wait
            // instead of keeping the request open for the HttpClient default of 100 seconds.
            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutFastSeconds);
        });

        services.AddTransient<RssParserService>();
        services.AddHttpClient<IOpmlService, OpmlService>(client =>
        {
            // OPML files are small; a stalled download should not hold the interaction for 100 s.
            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutDefaultSeconds);
        });

        // Command usage metrics repository
        services.AddSingleton<ICommandUsageRepository, CommandUsageRepository>();

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
        // Bind options the same way Program.cs does: first from the Wk7Bot section, then from root-level
        // config (which overrides). This ensures the registration layer sees the same merged options
        // as runtime services without building a throwaway service provider.
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

        if (featureGate.IsEnabled(FeatureKeys.GameNight))
        {
            services.AddHostedService<GameNightExpiryService>();
        }

        if (featureGate.IsEnabled(FeatureKeys.WeekendDigest))
        {
            services.AddSingleton<WeekendDigestDispatcher>();
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

        if (featureGate.IsEnabled(FeatureKeys.ReleaseDateReminders))
        {
            services.AddHostedService<ReleaseDateReminderBackgroundService>();
        }

        if (featureGate.IsEnabled(FeatureKeys.Leaderboard))
        {
            services.AddHostedService<LeaderboardBackgroundService>();
        }

        if (featureGate.IsEnabled(FeatureKeys.SteamFriendDigest))
        {
            // The module injects this background service directly, so it must be resolvable via DI
            // (the AddHostedService registration only provides IHostedService, not the concrete type).
            services.AddSingleton<SteamFriendDigestBackgroundService>();
            services.AddHostedService<SteamFriendDigestBackgroundService>();
        }

        if (featureGate.IsEnabled(FeatureKeys.DealWatchlist))
        {
            services.AddHostedService<DealWatchlistBackgroundService>();
        }

        if (featureGate.IsEnabled(FeatureKeys.DiscordEvents))
        {
            services.AddHostedService<DiscordScheduledEventCleanupService>();
        }

        if (featureGate.IsEnabled(FeatureKeys.PollenAlerts))
        {
            services.AddHostedService<PollenBackgroundService>();
        }

        if (featureGate.IsEnabled(FeatureKeys.Reminders))
        {
            services.AddHostedService<ReminderBackgroundService>();
        }

        if (featureGate.IsEnabled(FeatureKeys.Birthdays))
        {
            services.AddHostedService<BirthdayBackgroundService>();
        }

        // Achievement unlock digest (polls Steam achievements for linked users)
        services.AddHostedService<AchievementUnlockDigestBackgroundService>();

        if (featureGate.IsEnabled(FeatureKeys.Xp))
        {
            services.AddHostedService<XpTrackingService>();
        }

        // Always-on infrastructure services
        services.AddHostedService<StartupConfigurationValidator>();
        services.AddHostedService<DatabaseMaintenanceService>();
        services.AddHostedService<VoiceStatsService>();

        return services;
    }
}

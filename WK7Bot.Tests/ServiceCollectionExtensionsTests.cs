using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WK7Bot.Core.Utilities;
using WK7Bot.Extensions;
using Xunit;

namespace WK7Bot.Tests;

public class ServiceCollectionExtensionsTests
{
    private static IEnumerable<Type> HostedServiceTypes(IServiceCollection services)
        => services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)
            .Where(t => t != null)
            .Cast<Type>();

    [Fact]
    public void AddBotHostedServices_RegistersCoreWorkers_Always()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddBotHostedServices(configuration);

        var types = HostedServiceTypes(services).ToList();
        Assert.Contains(typeof(Services.InteractionHandlingService), types);
        Assert.Contains(typeof(Services.DiscordBotWorker), types);
    }

    [Fact]
    public void AddBotHostedServices_DisablesFeatures_FromWk7BotSection()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Wk7Bot:features:rss_polling_enabled"] = "false",
                ["Wk7Bot:features:home_assistant_notifier_enabled"] = "false",
                ["Wk7Bot:features:leipzig_waste_enabled"] = "false",
                ["Wk7Bot:features:alexa_notifications_enabled"] = "false",
                ["Wk7Bot:features:discord_presence_mqtt_enabled"] = "false",
                ["Wk7Bot:features:spontan_treff_enabled"] = "false",
                ["Wk7Bot:features:dwd_warning_enabled"] = "false",
                ["Wk7Bot:features:error_notifications_enabled"] = "false"
            })
            .Build();

        services.AddBotHostedServices(configuration);

        var types = HostedServiceTypes(services).ToList();
        Assert.DoesNotContain(typeof(Services.RssPollingBackgroundService), types);
        Assert.DoesNotContain(typeof(Services.HomeAssistantNotifierService), types);
        Assert.DoesNotContain(typeof(Services.LeipzigWasteBackgroundService), types);
        Assert.DoesNotContain(typeof(Services.AlexaMentionNotificationService), types);
        Assert.DoesNotContain(typeof(Services.DiscordPresenceMqttService), types);
        Assert.DoesNotContain(typeof(Services.SpontanTreffExpiryService), types);
        Assert.DoesNotContain(typeof(Services.DwdWarningBackgroundService), types);
        Assert.DoesNotContain(typeof(Services.ErrorNotificationDispatcher), types);

        // The Alexa feature uses a factory-based descriptor; disabling it must skip that too.
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationFactory != null);
    }

    [Fact]
    public void AddBotHostedServices_DisablesFeatures_FromRootSection_HomeAssistantStyle()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["features:rss_polling_enabled"] = "false",
                ["features:discord_presence_mqtt_enabled"] = "false",
                ["features:spontan_treff_enabled"] = "false",
                ["features:dwd_warning_enabled"] = "false",
                ["features:error_notifications_enabled"] = "false"
            })
            .Build();

        services.AddBotHostedServices(configuration);

        var types = HostedServiceTypes(services).ToList();
        Assert.DoesNotContain(typeof(Services.RssPollingBackgroundService), types);
        Assert.DoesNotContain(typeof(Services.DiscordPresenceMqttService), types);
        Assert.DoesNotContain(typeof(Services.SpontanTreffExpiryService), types);
        Assert.DoesNotContain(typeof(Services.DwdWarningBackgroundService), types);
        Assert.DoesNotContain(typeof(Services.ErrorNotificationDispatcher), types);
        Assert.Contains(typeof(Services.HomeAssistantNotifierService), types);
    }

    [Fact]
    public void AddBotHostedServices_EnablesAllFeatures_ByDefault()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddBotHostedServices(configuration);

        var types = HostedServiceTypes(services).ToList();
        Assert.Contains(typeof(Services.RssPollingBackgroundService), types);
        Assert.Contains(typeof(Services.HomeAssistantNotifierService), types);
        Assert.Contains(typeof(Services.LeipzigWasteBackgroundService), types);
        // Alexa reuses the typed HttpClient registration, so its hosted descriptor is factory-based
        // and carries no ImplementationType. Exactly one such descriptor must exist (Alexa's).
        Assert.Single(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationFactory != null);
        Assert.Contains(typeof(Services.DiscordPresenceMqttService), types);
        Assert.Contains(typeof(Services.BotStatusMqttService), types);
        Assert.Contains(typeof(Services.FoodPublisherService), types);
        Assert.Contains(typeof(Services.SpontanTreffExpiryService), types);
        Assert.Contains(typeof(Services.WeekendDigestBackgroundService), types);
        Assert.Contains(typeof(Services.DwdWarningBackgroundService), types);
        Assert.Contains(typeof(Services.ErrorNotificationDispatcher), types);
    }

    [Fact]
    public void AddBotHostedServices_DisablesMqttBotStatus_FromWk7BotSection()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Wk7Bot:features:mqtt_bot_status_enabled"] = "false"
            })
            .Build();

        services.AddBotHostedServices(configuration);

        Assert.DoesNotContain(typeof(Services.BotStatusMqttService), HostedServiceTypes(services));
    }

    [Fact]
    public void AddBotDiscordAndClients_RegistersFeatureHealthTrackerAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddBotDiscordAndClients();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(FeatureHealthTracker));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddBotHostedServices_DisablesFoodService_FromWk7BotSection()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Wk7Bot:features:food_service_enabled"] = "false"
            })
            .Build();

        services.AddBotHostedServices(configuration);

        var types = HostedServiceTypes(services).ToList();
        Assert.DoesNotContain(typeof(Services.FoodPublisherService), types);
    }

    [Fact]
    public void AddBotHostedServices_DisablesFoodService_FromRootSection_HomeAssistantStyle()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["features:food_service_enabled"] = "false"
            })
            .Build();

        services.AddBotHostedServices(configuration);

        var types = HostedServiceTypes(services).ToList();
        Assert.DoesNotContain(typeof(Services.FoodPublisherService), types);
    }

    [Fact]
    public void AddBotHostedServices_DisablesWeekendDigest_FromWk7BotSection()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Wk7Bot:features:weekend_digest_enabled"] = "false"
            })
            .Build();

        services.AddBotHostedServices(configuration);

        var types = HostedServiceTypes(services).ToList();
        Assert.DoesNotContain(typeof(Services.WeekendDigestBackgroundService), types);
    }

    [Fact]
    public void AddBotHostedServices_DisablesWeekendDigest_FromRootSection_HomeAssistantStyle()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["features:weekend_digest_enabled"] = "false"
            })
            .Build();

        services.AddBotHostedServices(configuration);

        var types = HostedServiceTypes(services).ToList();
        Assert.DoesNotContain(typeof(Services.WeekendDigestBackgroundService), types);
    }

    [Fact]
    public void AddBotDatabase_RegistersDbContextAndRepository()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBotDatabase("Data Source=:memory:");

        Assert.Contains(services, d => d.ServiceType.Name.Contains("BotDbContext"));
        Assert.Contains(services, d => d.ServiceType == typeof(Core.Interfaces.IRssRepository));
        Assert.Contains(services, d => d.ServiceType == typeof(Core.Interfaces.IWasteDispatchRepository));
        Assert.Contains(services, d => d.ServiceType == typeof(Core.Interfaces.ISpontanTreffRepository));
        Assert.Contains(services, d => d.ServiceType == typeof(Core.Interfaces.IWarningDispatchRepository));
    }

    [Fact]
    public void AddBotDiscordAndClients_RegistersWeekendEventSourceClient()
    {
        var services = new ServiceCollection();
        services.AddBotDiscordAndClients();

        Assert.Contains(services, d => d.ServiceType == typeof(Services.Interfaces.IWeekendEventSource));
    }

    [Fact]
    public void AddBotDiscordAndClients_RegistersDwdWarningServiceClient()
    {
        var services = new ServiceCollection();
        services.AddBotDiscordAndClients();

        Assert.Contains(services, d => d.ServiceType == typeof(Services.Interfaces.IDwdWarningService));
    }

    [Fact]
    public void AddBotDiscordAndClients_RegistersBattleNetServiceClient()
    {
        var services = new ServiceCollection();
        services.AddBotDiscordAndClients();

        Assert.Contains(services, d => d.ServiceType == typeof(Services.Interfaces.IBattleNetService));
    }
}

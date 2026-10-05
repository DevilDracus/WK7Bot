using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
                ["Wk7Bot:features:discord_presence_mqtt_enabled"] = "false"
            })
            .Build();

        services.AddBotHostedServices(configuration);

        var types = HostedServiceTypes(services).ToList();
        Assert.DoesNotContain(typeof(Services.RssPollingBackgroundService), types);
        Assert.DoesNotContain(typeof(Services.HomeAssistantNotifierService), types);
        Assert.DoesNotContain(typeof(Services.LeipzigWasteBackgroundService), types);
        Assert.DoesNotContain(typeof(Services.AlexaMentionNotificationService), types);
        Assert.DoesNotContain(typeof(Services.DiscordPresenceMqttService), types);
    }

    [Fact]
    public void AddBotHostedServices_DisablesFeatures_FromRootSection_HomeAssistantStyle()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["features:rss_polling_enabled"] = "false",
                ["features:discord_presence_mqtt_enabled"] = "false"
            })
            .Build();

        services.AddBotHostedServices(configuration);

        var types = HostedServiceTypes(services).ToList();
        Assert.DoesNotContain(typeof(Services.RssPollingBackgroundService), types);
        Assert.DoesNotContain(typeof(Services.DiscordPresenceMqttService), types);
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
        Assert.Contains(typeof(Services.AlexaMentionNotificationService), types);
        Assert.Contains(typeof(Services.DiscordPresenceMqttService), types);
        Assert.Contains(typeof(Services.FoodPublisherService), types);
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
    public void AddBotDatabase_RegistersDbContextAndRepository()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBotDatabase("Data Source=:memory:");

        Assert.Contains(services, d => d.ServiceType.Name.Contains("BotDbContext"));
        Assert.Contains(services, d => d.ServiceType == typeof(Core.Interfaces.IRssRepository));
        Assert.Contains(services, d => d.ServiceType == typeof(Core.Interfaces.IWasteDispatchRepository));
    }
}

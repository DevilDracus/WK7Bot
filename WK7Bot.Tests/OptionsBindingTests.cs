using Microsoft.Extensions.Configuration;
using WK7Bot.Options;
using Xunit;

namespace WK7Bot.Tests;

public class OptionsBindingTests
{
    [Fact]
    public void FeatureOptions_DefaultAllEnabled()
    {
        var features = new FeatureOptions();

        Assert.True(features.RssPollingEnabled);
        Assert.True(features.HomeAssistantNotifierEnabled);
        Assert.True(features.LeipzigWasteEnabled);
        Assert.True(features.AlexaNotificationsEnabled);
        Assert.True(features.DiscordPresenceMqttEnabled);
        Assert.True(features.SteamPresenceEnabled);
        Assert.True(features.FoodServiceEnabled);
    }

    [Fact]
    public void Wk7BotOptions_BindsFromWk7BotSection_WithSnakeCaseKeys()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Wk7Bot:discord_token"] = "token-123",
                ["Wk7Bot:mqtt_host"] = "broker.local",
                ["Wk7Bot:mqtt_port"] = "1884",
                ["Wk7Bot:mqtt_username"] = "user",
                ["Wk7Bot:mqtt_password"] = "pass",
                ["Wk7Bot:steam_api_key"] = "steam-key",
                ["Wk7Bot:gemini_api_key"] = "gemini-key",
                ["Wk7Bot:discord_steam_mappings:0:discord_user_id"] = "111",
                ["Wk7Bot:discord_steam_mappings:0:steam_id"] = "76561198000000001",
                ["Wk7Bot:discord_dm_user_ids:0"] = "222",
                ["Wk7Bot:alexa_notification:target_user_id"] = "333",
                ["Wk7Bot:alexa_notification:api_token"] = "tok",
                ["Wk7Bot:alexa_notification:api_secret"] = "sec",
                ["Wk7Bot:alexa_notification:endpoint_url"] = "https://api.example.com/notify",
                ["Wk7Bot:features:rss_polling_enabled"] = "false",
                ["Wk7Bot:features:steam_presence_enabled"] = "false",
                ["Wk7Bot:features:food_service_enabled"] = "false"
            })
            .Build();

        var options = new Wk7BotOptions();
        config.GetSection(Wk7BotOptions.SectionName).Bind(options);

        Assert.Equal("token-123", options.DiscordToken);
        Assert.Equal("broker.local", options.MqttHost);
        Assert.Equal(1884, options.MqttPort);
        Assert.Equal("user", options.MqttUsername);
        Assert.Equal("pass", options.MqttPassword);
        Assert.Equal("steam-key", options.SteamApiKey);
        Assert.Equal("gemini-key", options.GeminiApiKey);

        var mapping = Assert.Single(options.DiscordSteamMappings);
        Assert.Equal("111", mapping.DiscordUserId);
        Assert.Equal("76561198000000001", mapping.SteamId);

        Assert.Equal("222", Assert.Single(options.DiscordDmUserIds));

        Assert.Equal("333", options.AlexaNotification.TargetUserId);
        Assert.Equal("tok", options.AlexaNotification.ApiToken);
        Assert.Equal("sec", options.AlexaNotification.ApiSecret);
        Assert.Equal("https://api.example.com/notify", options.AlexaNotification.EndpointUrl);

        Assert.False(options.Features.RssPollingEnabled);
        Assert.True(options.Features.HomeAssistantNotifierEnabled);
        Assert.False(options.Features.SteamPresenceEnabled);
        Assert.False(options.Features.FoodServiceEnabled);
    }

    [Fact]
    public void Wk7BotOptions_BindsFromRoot_ForHomeAssistantOptionsJson()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["discord_token"] = "root-token",
                ["mqtt_host"] = "core-mosquitto",
                ["features:leipzig_waste_enabled"] = "false",
                ["features:food_service_enabled"] = "false"
            })
            .Build();

        var options = new Wk7BotOptions();
        config.Bind(options);

        Assert.Equal("root-token", options.DiscordToken);
        Assert.Equal("core-mosquitto", options.MqttHost);
        Assert.False(options.Features.LeipzigWasteEnabled);
        Assert.False(options.Features.FoodServiceEnabled);
    }

    [Fact]
    public void Wk7BotOptions_SectionName_IsWk7Bot()
    {
        Assert.Equal("Wk7Bot", Wk7BotOptions.SectionName);
    }

    [Fact]
    public void Wk7BotOptions_Defaults()
    {
        var options = new Wk7BotOptions();

        Assert.Equal(string.Empty, options.DiscordToken);
        Assert.Equal("core-mosquitto", options.MqttHost);
        Assert.Equal(1883, options.MqttPort);
        Assert.Null(options.MqttUsername);
        Assert.Null(options.MqttPassword);
        Assert.Null(options.SteamApiKey);
        Assert.Null(options.GeminiApiKey);
        Assert.Empty(options.DiscordSteamMappings);
        Assert.Empty(options.DiscordDmUserIds);
        Assert.NotNull(options.AlexaNotification);
        Assert.NotNull(options.Features);
    }

    [Fact]
    public void DiscordSteamMappingOptions_Defaults()
    {
        var mapping = new DiscordSteamMappingOptions();
        Assert.Equal(string.Empty, mapping.DiscordUserId);
        Assert.Equal(string.Empty, mapping.SteamId);
    }

    [Fact]
    public void AlexaNotificationOptions_DefaultsAreNull()
    {
        var options = new AlexaNotificationOptions();
        Assert.Null(options.TargetUserId);
        Assert.Null(options.ApiToken);
        Assert.Null(options.ApiSecret);
        Assert.Null(options.EndpointUrl);
    }
}
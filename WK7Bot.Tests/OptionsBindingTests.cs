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
        Assert.True(features.DwdWarningEnabled);
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
                ["Wk7Bot:features:food_service_enabled"] = "false",
                ["Wk7Bot:features:dwd_warning_enabled"] = "false",
                ["Wk7Bot:dwd_warning:postal_code"] = "04205",
                ["Wk7Bot:dwd_warning:latitude"] = "51.34",
                ["Wk7Bot:dwd_warning:longitude"] = "12.36",
                ["Wk7Bot:dwd_warning:lead_minutes"] = "45",
                ["Wk7Bot:dwd_warning:area_names:0"] = "Stadt Leipzig",
                ["Wk7Bot:dwd_warning:events:0"] = "HAGEL",
                ["Wk7Bot:servers:wk7_server_id"] = "1549409126625575004",
                ["Wk7Bot:servers:test_server_id"] = "551130054776717323",
                ["Wk7Bot:servers:testing_features:0"] = "dwd_warning_enabled"
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
        Assert.False(options.Features.DwdWarningEnabled);

        Assert.Equal("04205", options.DwdWarning.PostalCode);
        Assert.Equal(51.34, options.DwdWarning.Latitude, 3);
        Assert.Equal(12.36, options.DwdWarning.Longitude, 3);
        Assert.Equal(45, options.DwdWarning.LeadMinutes);
        Assert.Equal("Stadt Leipzig", Assert.Single(options.DwdWarning.AreaNames));
        Assert.Equal("HAGEL", Assert.Single(options.DwdWarning.Events));

        Assert.Equal("1549409126625575004", options.Servers.Wk7ServerId);
        Assert.Equal("551130054776717323", options.Servers.TestServerId);
        Assert.Equal("dwd_warning_enabled", Assert.Single(options.Servers.TestingFeatures));
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
                ["features:food_service_enabled"] = "false",
                ["dwd_warning:postal_code"] = "04103",
                ["servers:wk7_server_id"] = "1549409126625575004",
                ["servers:test_server_id"] = "551130054776717323",
                ["servers:testing_features:0"] = "weekend_digest"
            })
            .Build();

        var options = new Wk7BotOptions();
        config.Bind(options);

        Assert.Equal("root-token", options.DiscordToken);
        Assert.Equal("core-mosquitto", options.MqttHost);
        Assert.False(options.Features.LeipzigWasteEnabled);
        Assert.False(options.Features.FoodServiceEnabled);
        Assert.Equal("04103", options.DwdWarning.PostalCode);
        Assert.Equal("1549409126625575004", options.Servers.Wk7ServerId);
        Assert.Equal("551130054776717323", options.Servers.TestServerId);
        Assert.Equal("weekend_digest", Assert.Single(options.Servers.TestingFeatures));
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
        Assert.NotNull(options.DwdWarning);
        Assert.Equal(string.Empty, options.DwdWarning.PostalCode);
        Assert.Equal(0, options.DwdWarning.Latitude);
        Assert.Equal(0, options.DwdWarning.Longitude);
        Assert.Equal(30, options.DwdWarning.LeadMinutes);
        Assert.Empty(options.DwdWarning.AreaNames);
        Assert.Empty(options.DwdWarning.Events);
        Assert.Contains("STARKREGEN", options.DwdWarning.EffectiveEvents());
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

    [Fact]
    public void DwdWarningOptions_Defaults()
    {
        var options = new DwdWarningOptions();

        Assert.Equal(string.Empty, options.PostalCode);
        Assert.False(options.HasPostalCode());
        Assert.Equal(0, options.Latitude);
        Assert.Equal(0, options.Longitude);
        Assert.False(options.HasCoordinates());
        Assert.Equal(30, options.LeadMinutes);
        Assert.Empty(options.AreaNames);
        Assert.Empty(options.Events);
        Assert.Equal(10, options.EffectiveEvents().Count);
        Assert.Contains("STARKREGEN", options.EffectiveEvents());
        Assert.Contains("HEAVY_RAIN", options.EffectiveEvents());
    }

    [Fact]
    public void DwdWarningOptions_HasPostalCode_And_Coordinates_ReflectConfiguredValues()
    {
        var options = new DwdWarningOptions { PostalCode = "04205", Latitude = 51.34 };

        Assert.True(options.HasPostalCode());
        Assert.True(options.HasCoordinates());
    }

    [Fact]
    public void ServersOptions_DefaultsToPlaceholders_AndReportsThemAsUnset()
    {
        var options = new Wk7BotOptions().Servers;

        Assert.Equal(string.Empty, options.Wk7ServerId);
        Assert.Equal(string.Empty, options.TestServerId);
        Assert.Empty(options.TestingFeatures);
        Assert.False(options.TryGetWk7ServerId(out _));
        Assert.False(options.TryGetTestServerId(out _));
        Assert.False(options.IsFeatureBeingTested("dwd_warning"));
    }

    [Fact]
    public void ServersOptions_IgnoresShippedPlaceholders_ButParsesRealIds()
    {
        var placeholder = new ServersOptions
        {
            Wk7ServerId = "YOUR_WK7_SERVER_ID",
            TestServerId = "YOUR_BOT_TEST_SERVER_ID"
        };

        Assert.False(placeholder.TryGetWk7ServerId(out _));
        Assert.False(placeholder.TryGetTestServerId(out _));

        var configured = new ServersOptions
        {
            Wk7ServerId = "1549409126625575004",
            TestServerId = "551130054776717323"
        };

        Assert.True(configured.TryGetWk7ServerId(out var wk7));
        Assert.Equal(1549409126625575004ul, wk7);
        Assert.True(configured.TryGetTestServerId(out var test));
        Assert.Equal(551130054776717323ul, test);
    }

    [Fact]
    public void ServersOptions_IsFeatureBeingTested_MatchesCaseInsensitively_AndToleratesEnabledSuffix()
    {
        var options = new ServersOptions
        {
            TestingFeatures = new List<string> { "DWD_Warning_Enabled", "weekend_digest" }
        };

        Assert.True(options.IsFeatureBeingTested("dwd_warning"));
        Assert.True(options.IsFeatureBeingTested("dwd_warning_enabled"));
        Assert.True(options.IsFeatureBeingTested("DWD_WARNING"));
        Assert.True(options.IsFeatureBeingTested("weekend_digest"));
        Assert.False(options.IsFeatureBeingTested("leipzig_waste"));
    }
}

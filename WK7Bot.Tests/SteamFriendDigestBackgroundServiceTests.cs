using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace WK7Bot.Tests;

public class SteamFriendDigestBackgroundServiceTests
{
    private static SteamFriendDigestBackgroundService CreateService(
        ISteamService? steamService = null,
        DiscordSocketClient? discordClient = null)
    {
        return new SteamFriendDigestBackgroundService(
            discordClient ?? new Mock<DiscordSocketClient>().Object,
            steamService ?? Mock.Of<ISteamService>(),
            Mock.Of<IFeatureHealthTracker>(),
            new FeatureGate(OptionsFactory.Create(new Wk7BotOptions())),
            OptionsFactory.Create(new Wk7BotOptions
            {
                SteamApiKey = "test-key",
                SteamFriendDigestChannelName = "game-deals",
                SteamFriendDigestPostTime = "19:00",
                SteamFriendDigestPostWeekday = "Saturday"
            }),
            NullLogger<SteamFriendDigestBackgroundService>.Instance);
    }

    [Fact]
    public void BuildDigestEmbed_IncludesCurrentGame()
    {
        var activities = new List<SteamUserActivity>
        {
            new()
            {
                SteamId = "100",
                PersonaName = "Alice",
                CurrentGameTitle = "Game One",
                CurrentGameAppId = 123
            }
        };

        var embed = CreateService().BuildDigestEmbed(activities);

        Assert.Contains("Steam-Freunde Aktivit\u00e4t", embed.Title);
        var currentField = embed.Fields.FirstOrDefault(f => f.Name == "Gerade online");
        Assert.NotNull(currentField);
        Assert.Contains("Alice", currentField.Value);
        Assert.Contains("Game One", currentField.Value);
    }

    [Fact]
    public void BuildDigestEmbed_IncludesRecentGames()
    {
        var activities = new List<SteamUserActivity>
        {
            new()
            {
                SteamId = "100",
                PersonaName = "Bob",
                RecentGames = new List<SteamRecentGame>
                {
                    new() { AppId = 1, Name = "Game A", PlaytimeTwoWeeksMinutes = 120 },
                    new() { AppId = 2, Name = "Game B", PlaytimeTwoWeeksMinutes = 45 }
                }
            }
        };

        var embed = CreateService().BuildDigestEmbed(activities);

        var recentField = embed.Fields.FirstOrDefault(f => f.Name.StartsWith("Bob"));
        Assert.NotNull(recentField);
        Assert.Contains("Game A", recentField.Value);
        Assert.Contains("2 Std.", recentField.Value);
        Assert.Contains("Game B", recentField.Value);
        Assert.Contains("45 Min.", recentField.Value);
    }

    [Fact]
    public void BuildDigestEmbed_ShowsNoActivity_WhenNothingToReport()
    {
        var activities = new List<SteamUserActivity>
        {
            new()
            {
                SteamId = "100",
                PersonaName = "Inactive"
            }
        };

        var embed = CreateService().BuildDigestEmbed(activities);

        var noActivityField = embed.Fields.FirstOrDefault(f => f.Name == "Keine Aktivit\u00e4t");
        Assert.NotNull(noActivityField);
    }
}

using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class LeaderboardServiceTests
{
    private static LeaderboardService CreateService(
        IReadOnlyList<UserAccountLink> links,
        ISteamService? steam = null,
        IBattleNetService? battleNet = null)
    {
        var repository = new Mock<IUserAccountLinkRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(links);
        return new LeaderboardService(
            repository.Object,
            steam ?? Mock.Of<ISteamService>(),
            battleNet ?? Mock.Of<IBattleNetService>(),
            new DiscordSocketClient(),
            Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()),
            NullLogger<LeaderboardService>.Instance);
    }

    [Fact]
    public async Task GetAsync_SteamPlaytime_RanksByMinutes()
    {
        var links = new List<UserAccountLink>
        {
            new() { DiscordUserId = 111, Provider = "steam", ExternalId = "76561198000000001" },
            new() { DiscordUserId = 222, Provider = "steam", ExternalId = "76561198000000002" }
        };
        var steam = new Mock<ISteamService>();
        steam.Setup(s => s.GetSteamUserDataAsync("76561198000000001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SteamUserData { SteamId = "76561198000000001", PersonaName = "A", PlaytimeLastTwoWeeksMinutes = 120 });
        steam.Setup(s => s.GetSteamUserDataAsync("76561198000000002", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SteamUserData { SteamId = "76561198000000002", PersonaName = "B", PlaytimeLastTwoWeeksMinutes = 30 });

        var result = await CreateService(links, steam.Object).GetAsync("steam-playtime", 1, 10);

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("2 Std.", result.Entries[0].Value);
        Assert.Equal("111", result.Entries[0].DisplayName);
        Assert.Equal(1, result.Entries[0].Rank);
    }

    [Fact]
    public async Task GetAsync_BattleNetWow_UsesHighestLevel()
    {
        var links = new List<UserAccountLink>
        {
            new() { DiscordUserId = 111, Provider = "battlenet", ExternalId = "A#1", RefreshToken = "token" }
        };
        var battleNet = new Mock<IBattleNetService>();
        battleNet.Setup(b => b.GetBattleNetUserDataAsync("token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BattleNetUserData
            {
                BattleTag = "A#1",
                WowCharacters = new List<BattleNetWowCharacter>
                {
                    new() { Name = "Lyria", CharacterClass = "Magier", Level = 80 },
                    new() { Name = "Alt", CharacterClass = "Krieger", Level = 70 }
                }
            });

        var result = await CreateService(links, battleNet: battleNet.Object).GetAsync("battlenet-wow", 1, 10);

        Assert.Single(result.Entries);
        Assert.Equal("Stufe 80", result.Entries[0].Value);
        Assert.Contains("Lyria", result.Entries[0].Detail);
    }

    [Fact]
    public async Task GetAsync_ReturnsEmpty_WhenNoLinkedUsers()
    {
        var result = await CreateService(new List<UserAccountLink>()).GetAsync("steam-playtime", 1, 10);

        Assert.Empty(result.Entries);
    }
}

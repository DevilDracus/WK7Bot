using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Models;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class ProfileServiceTests
{
    private static ProfileService CreateService(
        ISteamService? steam = null,
        IBattleNetService? battleNet = null,
        IAccountLinkResolver? resolver = null)
    {
        return new ProfileService(
            resolver ?? Mock.Of<IAccountLinkResolver>(),
            steam ?? Mock.Of<ISteamService>(),
            battleNet ?? Mock.Of<IBattleNetService>(),
            new DiscordSocketClient(),
            NullLogger<ProfileService>.Instance);
    }

    [Fact]
    public async Task GetAsync_ReturnsSteamSection_ForLinkedSteamAccount()
    {
        var resolver = new Mock<IAccountLinkResolver>();
        resolver.Setup(r => r.GetSteamIdAsync(111, It.IsAny<CancellationToken>())).ReturnsAsync("76561198000000001");

        var steam = new Mock<ISteamService>();
        steam.Setup(s => s.GetSteamUserDataAsync("76561198000000001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SteamUserData
            {
                PersonaName = "Dracus",
                PersonaState = "Online",
                CurrentGameTitle = "Baldurs Gate 3",
                PlaytimeLastTwoWeeksMinutes = 300,
                SteamAvatarUrl = "https://example.com/avatar.jpg",
                RecentGames = new List<SteamRecentGame>
                {
                    new() { Name = "Baldurs Gate 3", PlaytimeTwoWeeksMinutes = 300, PlaytimeForeverMinutes = 6000, AchievementsUnlocked = 10, AchievementsTotal = 50 },
                    new() { Name = "Factorio", PlaytimeTwoWeeksMinutes = 60, PlaytimeForeverMinutes = 2000 }
                }
            });

        var result = await CreateService(steam.Object, resolver: resolver.Object).GetAsync(111, 999);

        Assert.NotNull(result.Steam);
        Assert.Null(result.BattleNet);
        Assert.Equal("Dracus", result.Steam!.PersonaName);
        Assert.Equal("Baldurs Gate 3", result.Steam.CurrentGameTitle);
        Assert.Equal(10, result.Steam.AchievementsUnlocked);
        Assert.Equal(2, result.Steam.TopRecentGames.Count);
        Assert.Equal("Baldurs Gate 3", result.Steam.TopRecentGames[0].Name);
    }

    [Fact]
    public async Task GetAsync_ReturnsBattleNetSection_WithMainCharacterFirst()
    {
        var resolver = new Mock<IAccountLinkResolver>();
        resolver.Setup(r => r.GetBattleNetLinkAsync(222, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BattleNetAccountLink(222, "refresh-token", "eu", "Dracus#2123"));

        var battleNet = new Mock<IBattleNetService>();
        battleNet.Setup(b => b.GetBattleNetUserDataAsync("refresh-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BattleNetUserData
            {
                BattleTag = "Dracus#2123",
                Region = "eu",
                WowCharacters = new List<BattleNetWowCharacter>
                {
                    new() { Name = "Alt", CharacterClass = "Krieger", Level = 70, AverageItemLevel = 400 },
                    new() { Name = "Lyria", CharacterClass = "Magier", Level = 80, AverageItemLevel = 620, GuildName = "WK7" }
                },
                DiabloHeroes = new List<BattleNetDiabloHero>
                {
                    new() { Name = "Zorn", HeroClass = "barbarian", Level = 70, ParagonLevel = 800, Seasonal = true }
                }
            });

        var result = await CreateService(battleNet: battleNet.Object, resolver: resolver.Object).GetAsync(222, 999);

        Assert.NotNull(result.BattleNet);
        Assert.Null(result.Steam);
        Assert.Equal("Dracus#2123", result.BattleNet!.BattleTag);
        Assert.Equal("Lyria", result.BattleNet.WowCharacters[0].Name);
        Assert.True(result.BattleNet.WowCharacters[0].IsMain);
        Assert.False(result.BattleNet.WowCharacters[1].IsMain);
        Assert.Single(result.BattleNet.DiabloHeroes);
        Assert.Equal(800, result.BattleNet.DiabloHeroes[0].ParagonLevel);
    }

    [Fact]
    public async Task GetAsync_ReturnsEmptySections_WhenNothingIsLinked()
    {
        var result = await CreateService().GetAsync(333, 999);

        Assert.Null(result.Steam);
        Assert.Null(result.BattleNet);
    }

    [Fact]
    public async Task GetAsync_SteamFetchFailure_DoesNotThrow()
    {
        var resolver = new Mock<IAccountLinkResolver>();
        resolver.Setup(r => r.GetSteamIdAsync(111, It.IsAny<CancellationToken>())).ReturnsAsync("76561198000000001");

        var steam = new Mock<ISteamService>();
        steam.Setup(s => s.GetSteamUserDataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("steam down"));

        var result = await CreateService(steam.Object, resolver: resolver.Object).GetAsync(111, 999);

        Assert.Null(result.Steam);
    }
}

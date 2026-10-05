using Moq;
using WK7Bot.Models;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class SteamDataCacheTests
{
    private const string TestSteamId = "76561198012345678";

    [Fact]
    public async Task GetSteamUserDataAsync_CachesResultWithinTtl()
    {
        var data = new SteamUserData { SteamId = TestSteamId, PersonaName = "CachedUser" };
        var mock = new Mock<ISteamService>();
        mock.Setup(s => s.GetSteamUserDataAsync(TestSteamId, It.IsAny<CancellationToken>())).ReturnsAsync(data);

        var cache = new SteamDataCache(mock.Object, TimeSpan.FromMinutes(1));

        var first = await cache.GetSteamUserDataAsync(TestSteamId);
        var second = await cache.GetSteamUserDataAsync(TestSteamId);

        Assert.Same(data, first);
        Assert.Same(data, second);
        mock.Verify(s => s.GetSteamUserDataAsync(TestSteamId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSteamUserDataAsync_RefetchesAfterTtlExpires()
    {
        var data = new SteamUserData { SteamId = TestSteamId };
        var mock = new Mock<ISteamService>();
        mock.Setup(s => s.GetSteamUserDataAsync(TestSteamId, It.IsAny<CancellationToken>())).ReturnsAsync(data);

        var cache = new SteamDataCache(mock.Object, TimeSpan.FromMilliseconds(50));

        await cache.GetSteamUserDataAsync(TestSteamId);
        await Task.Delay(100);
        await cache.GetSteamUserDataAsync(TestSteamId);

        mock.Verify(s => s.GetSteamUserDataAsync(TestSteamId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetSteamUserDataAsync_CachesNullResults()
    {
        var mock = new Mock<ISteamService>();
        mock.Setup(s => s.GetSteamUserDataAsync(TestSteamId, It.IsAny<CancellationToken>())).ReturnsAsync((SteamUserData?)null);

        var cache = new SteamDataCache(mock.Object, TimeSpan.FromMinutes(1));

        var first = await cache.GetSteamUserDataAsync(TestSteamId);
        var second = await cache.GetSteamUserDataAsync(TestSteamId);

        Assert.Null(first);
        Assert.Null(second);
        mock.Verify(s => s.GetSteamUserDataAsync(TestSteamId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSteamUserDataAsync_TracksSteamIdsSeparately()
    {
        const string otherSteamId = "76561198099999999";

        var mock = new Mock<ISteamService>();
        mock.Setup(s => s.GetSteamUserDataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SteamUserData());

        var cache = new SteamDataCache(mock.Object, TimeSpan.FromMinutes(1));

        await cache.GetSteamUserDataAsync(TestSteamId);
        await cache.GetSteamUserDataAsync(otherSteamId);
        await cache.GetSteamUserDataAsync(TestSteamId);

        mock.Verify(s => s.GetSteamUserDataAsync(TestSteamId, It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(s => s.GetSteamUserDataAsync(otherSteamId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Constructor_Throws_WhenSteamServiceIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new SteamDataCache(null!, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Constructor_Throws_WhenTimeToLiveIsNotPositive()
    {
        var mock = new Mock<ISteamService>();

        Assert.Throws<ArgumentOutOfRangeException>(() => new SteamDataCache(mock.Object, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetSteamUserDataAsync_Throws_WhenSteamIdIsNullOrWhiteSpace(string steamId)
    {
        var mock = new Mock<ISteamService>();
        var cache = new SteamDataCache(mock.Object, TimeSpan.FromMinutes(1));

        await Assert.ThrowsAnyAsync<ArgumentException>(() => cache.GetSteamUserDataAsync(steamId));
        mock.Verify(s => s.GetSteamUserDataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

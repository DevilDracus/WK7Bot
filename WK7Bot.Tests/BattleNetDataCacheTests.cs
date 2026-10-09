using Moq;
using WK7Bot.Models;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class BattleNetDataCacheTests
{
    private const string TestRefreshToken = "refresh-token-123";

    [Fact]
    public async Task GetBattleNetUserDataAsync_CachesResultWithinTtl()
    {
        var data = new BattleNetUserData { BattleTag = "Cached#1234" };
        var mock = new Mock<IBattleNetService>();
        mock.Setup(s => s.GetBattleNetUserDataAsync(TestRefreshToken, It.IsAny<CancellationToken>())).ReturnsAsync(data);

        var cache = new BattleNetDataCache(mock.Object, TimeSpan.FromMinutes(1));

        var first = await cache.GetBattleNetUserDataAsync(TestRefreshToken);
        var second = await cache.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.Same(data, first);
        Assert.Same(data, second);
        mock.Verify(s => s.GetBattleNetUserDataAsync(TestRefreshToken, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_RefetchesAfterTtlExpires()
    {
        var data = new BattleNetUserData { BattleTag = "Cached#1234" };
        var mock = new Mock<IBattleNetService>();
        mock.Setup(s => s.GetBattleNetUserDataAsync(TestRefreshToken, It.IsAny<CancellationToken>())).ReturnsAsync(data);

        var cache = new BattleNetDataCache(mock.Object, TimeSpan.FromMilliseconds(50));

        await cache.GetBattleNetUserDataAsync(TestRefreshToken);
        await Task.Delay(100);
        await cache.GetBattleNetUserDataAsync(TestRefreshToken);

        mock.Verify(s => s.GetBattleNetUserDataAsync(TestRefreshToken, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_CachesNullResults()
    {
        var mock = new Mock<IBattleNetService>();
        mock.Setup(s => s.GetBattleNetUserDataAsync(TestRefreshToken, It.IsAny<CancellationToken>())).ReturnsAsync((BattleNetUserData?)null);

        var cache = new BattleNetDataCache(mock.Object, TimeSpan.FromMinutes(1));

        var first = await cache.GetBattleNetUserDataAsync(TestRefreshToken);
        var second = await cache.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.Null(first);
        Assert.Null(second);
        mock.Verify(s => s.GetBattleNetUserDataAsync(TestRefreshToken, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_TracksRefreshTokensSeparately()
    {
        const string otherRefreshToken = "refresh-token-999";

        var mock = new Mock<IBattleNetService>();
        mock.Setup(s => s.GetBattleNetUserDataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BattleNetUserData());

        var cache = new BattleNetDataCache(mock.Object, TimeSpan.FromMinutes(1));

        await cache.GetBattleNetUserDataAsync(TestRefreshToken);
        await cache.GetBattleNetUserDataAsync(otherRefreshToken);
        await cache.GetBattleNetUserDataAsync(TestRefreshToken);

        mock.Verify(s => s.GetBattleNetUserDataAsync(TestRefreshToken, It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(s => s.GetBattleNetUserDataAsync(otherRefreshToken, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Constructor_Throws_WhenBattleNetServiceIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new BattleNetDataCache(null!, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Constructor_Throws_WhenTimeToLiveIsNotPositive()
    {
        var mock = new Mock<IBattleNetService>();

        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleNetDataCache(mock.Object, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetBattleNetUserDataAsync_Throws_WhenRefreshTokenIsNullOrWhiteSpace(string refreshToken)
    {
        var mock = new Mock<IBattleNetService>();
        var cache = new BattleNetDataCache(mock.Object, TimeSpan.FromMinutes(1));

        await Assert.ThrowsAnyAsync<ArgumentException>(() => cache.GetBattleNetUserDataAsync(refreshToken));
        mock.Verify(s => s.GetBattleNetUserDataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

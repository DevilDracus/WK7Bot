using System.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Options;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class DealWatchlistServiceTests
{
    private static HttpClient CreateHttpClient(string json)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        return new HttpClient(handler.Object);
    }

    private static DealWatchlistService CreateService(IDealWatchRepository repository, string searchJson = "{\"items\":[{\"id\":123,\"name\":\"Test Game\"}]}")
        => new(
            repository,
            Mock.Of<IWishlistDealService>(),
            new DiscordSocketClient(),
            Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions { DealWatchlistMinDiscountPercent = 40 }),
            CreateHttpClient(searchJson),
            NullLogger<DealWatchlistService>.Instance);

    [Fact]
    public async Task WatchAsync_AddsResolvedGame()
    {
        var repository = new Mock<IDealWatchRepository>();
        repository.Setup(r => r.GetForUserAsync(111, It.IsAny<CancellationToken>())).ReturnsAsync(new List<DealWatchItem>());
        DealWatchItem? captured = null;
        repository.Setup(r => r.AddAsync(It.IsAny<DealWatchItem>(), It.IsAny<CancellationToken>()))
            .Callback<DealWatchItem, CancellationToken>((item, _) => captured = item)
            .Returns(Task.CompletedTask);

        var result = await CreateService(repository.Object).WatchAsync(111, "test game");

        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.Equal("Test Game", captured!.GameName);
        Assert.Equal("123", captured.ItadId);
        Assert.Equal(40, captured.MinDiscountPercent);
    }

    [Fact]
    public async Task WatchAsync_RejectsDuplicate()
    {
        var repository = new Mock<IDealWatchRepository>();
        repository.Setup(r => r.GetForUserAsync(111, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DealWatchItem> { new() { GameName = "Test Game", ItadId = "123" } });

        var result = await CreateService(repository.Object).WatchAsync(111, "test game");

        Assert.False(result.Success);
        repository.Verify(r => r.AddAsync(It.IsAny<DealWatchItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnwatchAsync_ReturnsResultFromRepository()
    {
        var repository = new Mock<IDealWatchRepository>();
        repository.Setup(r => r.RemoveAsync(111, "Test Game", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateService(repository.Object).UnwatchAsync(111, "Test Game");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task GetWatchlistAsync_ReturnsRepositoryItems()
    {
        var repository = new Mock<IDealWatchRepository>();
        repository.Setup(r => r.GetForUserAsync(111, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DealWatchItem> { new() { GameName = "A" }, new() { GameName = "B" } });

        var result = await CreateService(repository.Object).GetWatchlistAsync(111);

        Assert.Equal(2, result.Count);
    }
}

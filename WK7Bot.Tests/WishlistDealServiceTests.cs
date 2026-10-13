namespace WK7Bot.Tests;

using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services;
using Xunit;

public class WishlistDealServiceTests
{
    private static HttpClient CreateClient(HttpResponseMessage response)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        return new HttpClient(handler.Object)
        {
            BaseAddress = new Uri("https://api.isthereanydeal.com/")
        };
    }

    private static WishlistDealService CreateService(HttpClient client, int minDiscount = 60)
    {
        var options = new Wk7BotOptions
        {
            DealMinDiscountPercent = minDiscount
        };
        return new WishlistDealService(
            client,
            Options.Create(options),
            NullLogger<WishlistDealService>.Instance);
    }

    [Fact]
    public async Task FetchWishlistAsync_ReturnsItems_WhenWishlistIsPublic()
    {
        var json = @"{""123"":{""appid"":123,""name"":""Test Game"",""subs"":[]},""456"":{""appid"":456,""name"":""Other Game"",""subs"":[]}}";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };

        var service = CreateService(CreateClient(response));
        var result = await service.FetchWishlistAsync("123456789");

        Assert.Equal(2, result.Count);
        Assert.Contains(result, i => i.AppId == 123 && i.Name == "Test Game");
        Assert.Contains(result, i => i.AppId == 456 && i.Name == "Other Game");
    }

    [Fact]
    public async Task FetchWishlistAsync_ReturnsEmpty_WhenWishlistIsPrivate()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        var service = CreateService(CreateClient(response));
        var result = await service.FetchWishlistAsync("123456789");

        Assert.Empty(result);
    }

    [Fact]
    public async Task FetchWishlistAsync_ReturnsEmpty_WhenResponseIsEmpty()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]")
        };
        var service = CreateService(CreateClient(response));
        var result = await service.FetchWishlistAsync("123456789");

        Assert.Empty(result);
    }

    [Fact]
    public async Task FetchWishlistAsync_ReturnsEmpty_WhenRequestFails()
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var service = CreateService(CreateClient(response));
        var result = await service.FetchWishlistAsync("123456789");

        Assert.Empty(result);
    }

    [Fact]
    public async Task FetchPricesAsync_ReturnsEmpty_WhenAppIdListIsEmpty()
    {
        var service = CreateService(new HttpClient());
        var result = await service.FetchPricesAsync(Array.Empty<uint>());

        Assert.Empty(result);
    }

    [Fact]
    public void EvaluateAlert_ReturnsNull_WhenPriceDataIsNull()
    {
        var service = CreateService(new HttpClient());
        var result = service.EvaluateAlert(123, "Game", null!, new List<ulong>());

        Assert.Null(result);
    }

    [Fact]
    public void EvaluateAlert_ReturnsNull_WhenDiscountIsBelowThreshold()
    {
        var service = CreateService(new HttpClient(), minDiscount: 60);
        var priceData = new ItadPriceData
        {
            CurrentPrice = 40m,
            RegularPrice = 50m,
            HistoricalLow = 20m
        };

        var result = service.EvaluateAlert(123, "Game", priceData, new List<ulong>());

        Assert.Null(result);
    }

    [Fact]
    public void EvaluateAlert_ReturnsAlert_WhenDiscountMeetsThreshold()
    {
        var service = CreateService(new HttpClient(), minDiscount: 60);
        var priceData = new ItadPriceData
        {
            CurrentPrice = 20m,
            RegularPrice = 50m,
            HistoricalLow = 10m,
            StoreName = "Steam",
            DealUrl = "https://example.com/deal"
        };
        var userIds = new List<ulong> { 111111111111111111 };

        var result = service.EvaluateAlert(123, "Game", priceData, userIds);

        Assert.NotNull(result);
        Assert.Equal("Game", result!.GameTitle);
        Assert.Equal(60, result.DiscountPercent);
        Assert.False(result.IsHistoricalLow);
        Assert.Single(result.NotifiedUserIds);
    }

    [Fact]
    public void EvaluateAlert_ReturnsAlert_WhenHistoricalLow()
    {
        var service = CreateService(new HttpClient(), minDiscount: 60);
        var priceData = new ItadPriceData
        {
            CurrentPrice = 10m,
            RegularPrice = 50m,
            HistoricalLow = 15m,
            StoreName = "Steam",
            DealUrl = "https://example.com/deal"
        };
        var userIds = new List<ulong> { 111111111111111111 };

        var result = service.EvaluateAlert(123, "Game", priceData, userIds);

        Assert.NotNull(result);
        Assert.True(result!.IsHistoricalLow);
        Assert.Equal(80, result.DiscountPercent);
    }

    [Fact]
    public void Constructor_Throws_WhenDependenciesAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => new WishlistDealService(null!, Options.Create(new Wk7BotOptions()), NullLogger<WishlistDealService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new WishlistDealService(new HttpClient(), null!, NullLogger<WishlistDealService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new WishlistDealService(new HttpClient(), Options.Create(new Wk7BotOptions()), null!));
    }
}

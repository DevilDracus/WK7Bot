using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services;
using Xunit;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace WK7Bot.Tests;

public class WishlistDealServiceComparisonTests
{
    private static HttpClient CreateClient(params (string UrlPattern, string Response)[] responses)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                var url = req.RequestUri?.ToString() ?? string.Empty;
                foreach (var (pattern, response) in responses)
                {
                    if (url.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(response)
                        };
                    }
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

        return new HttpClient(handler.Object)
        {
            BaseAddress = new Uri("https://api.isthereanydeal.com/")
        };
    }

    private static WishlistDealService CreateService(HttpClient client)
        => new(client, OptionsFactory.Create(new Wk7BotOptions { ItadApiKey = "test-key" }), NullLogger<WishlistDealService>.Instance);

    [Fact]
    public async Task ComparePricesAsync_ReturnsOffersOrderedByPrice()
    {
        var lookup = @"{ ""found"": true, ""game"": { ""id"":""abc"", ""title"":""Test Game"" }}";
        var prices = @"[{
            ""id"":""abc"",
            ""deals"":[
                { ""shop"":{ ""name"":""Steam"" }, ""price"":{ ""amount"":30 }, ""regular"":{ ""amount"":60 }, ""url"":""https://store.steampowered.com"" },
                { ""shop"":{ ""name"":""GOG"" }, ""price"":{ ""amount"":15 }, ""regular"":{ ""amount"":60 }, ""url"":""https://gog.com"" }
            ]
        }]";

        var service = CreateService(CreateClient(
            ("lookup", lookup),
            ("prices", prices)));

        var result = await service.ComparePricesAsync(123);

        Assert.NotNull(result);
        Assert.Equal("Test Game", result!.Title);
        Assert.Equal(123u, result.AppId);
        Assert.Equal(2, result.Offers.Count);
        Assert.Equal("GOG", result.Offers[0].StoreName);
        Assert.Equal(15m, result.Offers[0].Price);
        Assert.Equal(75, result.Offers[0].DiscountPercent);
    }

    [Fact]
    public async Task ComparePricesAsync_ReturnsNull_WhenLookupFails()
    {
        var lookup = @"{ ""found"": false }";
        var service = CreateService(CreateClient(("lookup", lookup)));

        var result = await service.ComparePricesAsync(123);

        Assert.Null(result);
    }

    [Fact]
    public async Task ComparePricesAsync_ReturnsNull_WhenNoApiKey()
    {
        var service = new WishlistDealService(
            CreateClient(),
            OptionsFactory.Create(new Wk7BotOptions()),
            NullLogger<WishlistDealService>.Instance);

        var result = await service.ComparePricesAsync(123);

        Assert.Null(result);
    }
}

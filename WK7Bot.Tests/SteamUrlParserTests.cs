using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public class SteamUrlParserTests
{
    [Theory]
    [InlineData("76561198000000000")]
    [InlineData("https://steamcommunity.com/profiles/76561198000000000/")]
    [InlineData("https://store.steampowered.com/wishlist/profiles/76561198000000000/")]
    public void TryGetSteamId_ExtractsFromBareIdAndUrls(string input)
    {
        Assert.True(SteamUrlParser.TryGetSteamId(input, out var steamId));
        Assert.Equal("76561198000000000", steamId);
    }

    [Theory]
    [InlineData("https://steamcommunity.com/id/dracus/")]
    [InlineData("https://store.steampowered.com/wishlist/id/dracus/")]
    public void TryGetVanity_ExtractsFromProfileAndWishlistUrls(string input)
    {
        Assert.True(SteamUrlParser.TryGetVanity(input, out var vanity));
        Assert.Equal("dracus", vanity);
    }

    [Fact]
    public void TryGetSteamId_ReturnsFalseForVanity()
    {
        Assert.False(SteamUrlParser.TryGetSteamId("https://steamcommunity.com/id/dracus/", out var steamId));
        Assert.Null(steamId);
    }

    [Fact]
    public void TryGetVanity_ReturnsFalseForSteamIdUrl()
    {
        Assert.False(SteamUrlParser.TryGetVanity("https://steamcommunity.com/profiles/76561198000000000/", out var vanity));
        Assert.Null(vanity);
    }
}

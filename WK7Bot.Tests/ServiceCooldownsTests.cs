using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public class ServiceCooldownsTests
{
    [Fact]
    public void SteamPresenceData_IsPositive()
    {
        Assert.True(ServiceCooldowns.SteamPresenceData > TimeSpan.Zero);
    }

    [Fact]
    public void BattleNetPresenceData_IsPositive()
    {
        Assert.True(ServiceCooldowns.BattleNetPresenceData > TimeSpan.Zero);
    }

    [Fact]
    public void PresenceRefresh_IsPositive()
    {
        Assert.True(ServiceCooldowns.PresenceRefresh > TimeSpan.Zero);
    }

    [Fact]
    public void PrimaryRecipeSearchProvider_IsPositive()
    {
        Assert.True(ServiceCooldowns.PrimaryRecipeSearchProvider > TimeSpan.Zero);
    }

    [Fact]
    public void WishlistDealAlert_IsPositive()
    {
        Assert.True(ServiceCooldowns.WishlistDealAlert > TimeSpan.Zero);
    }
}

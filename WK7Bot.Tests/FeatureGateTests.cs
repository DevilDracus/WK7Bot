using Microsoft.Extensions.Options;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;
using Xunit;

namespace WK7Bot.Tests;

public sealed class FeatureGateTests
{
    [Theory]
    [InlineData(FeatureKeys.RssPolling, true)]
    [InlineData(FeatureKeys.RssPolling, false)]
    [InlineData(FeatureKeys.WishlistDealAlerts, true)]
    [InlineData(FeatureKeys.WishlistDealAlerts, false)]
    [InlineData(FeatureKeys.FoodService, true)]
    [InlineData(FeatureKeys.FoodService, false)]
    public void IsEnabled_RespectsBoundOptions(string featureKey, bool enabled)
    {
        var options = new Wk7BotOptions
        {
            Features = new FeatureOptions
            {
                RssPollingEnabled = enabled,
                WishlistDealAlertsEnabled = enabled,
                FoodServiceEnabled = enabled
            }
        };

        var gate = new FeatureGate(Microsoft.Extensions.Options.Options.Create(options));

        Assert.Equal(enabled, gate.IsEnabled(featureKey));
    }

    [Fact]
    public void IsEnabled_UnknownKey_DefaultsToEnabled()
    {
        var gate = new FeatureGate(Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()));

        Assert.True(gate.IsEnabled("nonexistent_feature_key"));
    }

    [Fact]
    public void IsEnabled_NullKey_DefaultsToEnabled()
    {
        var gate = new FeatureGate(Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()));

        Assert.True(gate.IsEnabled(null!));
    }
}

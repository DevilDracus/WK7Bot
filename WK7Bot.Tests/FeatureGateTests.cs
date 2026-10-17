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

    [Fact]
    public void SetOverride_WinsOverBoundOptions()
    {
        var options = new Wk7BotOptions
        {
            Features = new FeatureOptions { FoodServiceEnabled = true }
        };
        var gate = new FeatureGate(Microsoft.Extensions.Options.Options.Create(options));

        gate.SetOverride(FeatureKeys.FoodService, false);

        Assert.False(gate.IsEnabled(FeatureKeys.FoodService));
        Assert.True(gate.GetOverrides().ContainsKey(FeatureKeys.FoodService));
    }

    [Fact]
    public void ClearOverride_RestoresBoundOption()
    {
        var options = new Wk7BotOptions
        {
            Features = new FeatureOptions { FoodServiceEnabled = true }
        };
        var gate = new FeatureGate(Microsoft.Extensions.Options.Options.Create(options));

        gate.SetOverride(FeatureKeys.FoodService, false);
        var removed = gate.ClearOverride(FeatureKeys.FoodService);

        Assert.True(removed);
        Assert.True(gate.IsEnabled(FeatureKeys.FoodService));
        Assert.Empty(gate.GetOverrides());
    }

    [Fact]
    public void SetOverride_IsCaseInsensitive()
    {
        var gate = new FeatureGate(Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()));

        gate.SetOverride("FOOD_SERVICE_ENABLED", false);

        Assert.False(gate.IsEnabled(FeatureKeys.FoodService));
    }

    [Theory]
    [InlineData(FeatureKeys.FoodService, true)]
    [InlineData(FeatureKeys.RssPolling, true)]
    [InlineData("not_a_feature", false)]
    [InlineData(null, false)]
    public void IsKnownKey_ValidatesAgainstFeatureKeys(string? key, bool expected)
    {
        Assert.Equal(expected, FeatureGate.IsKnownKey(key));
    }

    [Fact]
    public void FeatureKeysAll_ContainsEveryKnownKey()
    {
        Assert.Contains(FeatureKeys.RssPolling, FeatureKeys.All);
        Assert.Contains(FeatureKeys.DiscordEvents, FeatureKeys.All);
        Assert.Contains(FeatureKeys.DealWatchlist, FeatureKeys.All);
        Assert.Equal(FeatureKeys.All.Length, FeatureKeys.All.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}

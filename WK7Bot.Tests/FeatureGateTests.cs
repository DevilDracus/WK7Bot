using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public sealed class FeatureGateTests
{
    public static IEnumerable<object[]> AllFeatureKeyStates() =>
        FeatureKeys.All.SelectMany(key => new[]
        {
            new object[] { key, true },
            new object[] { key, false }
        });

    [Theory]
    [MemberData(nameof(AllFeatureKeyStates))]
    public void IsEnabled_RespectsBoundOptions_AllFeatureKeys(string featureKey, bool enabled)
    {
        var features = new FeatureOptions();
        var property = typeof(FeatureOptions).GetProperties()
            .First(p => string.Equals(
                p.GetCustomAttributes(typeof(ConfigurationKeyNameAttribute), false)
                    .OfType<ConfigurationKeyNameAttribute>()
                    .FirstOrDefault()?.Name,
                featureKey,
                StringComparison.OrdinalIgnoreCase));
        property.SetValue(features, enabled);

        var options = new Wk7BotOptions { Features = features };
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

    [Fact]
    public void FeatureKeys_ConsistentWithFeatureOptions()
    {
        // Every FeatureKeys constant must have a corresponding [ConfigurationKeyName] in FeatureOptions.
        var featureOptionKeys = typeof(FeatureOptions)
            .GetProperties()
            .Select(p => p.GetCustomAttributes(typeof(ConfigurationKeyNameAttribute), false)
                          .OfType<ConfigurationKeyNameAttribute>()
                          .FirstOrDefault()?.Name)
            .Where(n => n is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        var fields = typeof(FeatureKeys)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToList();

        foreach (var key in fields)
        {
            Assert.True(featureOptionKeys.Contains(key),
                $"Feature key '{key}' has no corresponding ConfigurationKeyName in FeatureOptions.");
        }
    }
}

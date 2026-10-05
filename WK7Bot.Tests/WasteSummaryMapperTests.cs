using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public class WasteSummaryMapperTests
{
    [Theory]
    [InlineData("Restabfall", "⬛ Schwarze Tonne (Restabfall)")]
    [InlineData("RESTABFALL Albertusstraße", "⬛ Schwarze Tonne (Restabfall)")]
    [InlineData("schwarz", "⬛ Schwarze Tonne (Restabfall)")]
    [InlineData("Papier", "🟦 Blaue Tonne (Pappe & Papier)")]
    [InlineData("Pappe und Papier", "🟦 Blaue Tonne (Pappe & Papier)")]
    [InlineData("blau", "🟦 Blaue Tonne (Pappe & Papier)")]
    [InlineData("Wertstoffe", "🟨 Gelbe Tonne / Gelber Sack (Wertstoffe)")]
    [InlineData("Wertstoff", "🟨 Gelbe Tonne / Gelber Sack (Wertstoffe)")]
    [InlineData("gelb", "🟨 Gelbe Tonne / Gelber Sack (Wertstoffe)")]
    [InlineData("Biogut", "🟫 Braune Tonne (Biogut)")]
    [InlineData("braun", "🟫 Braune Tonne (Biogut)")]
    public void MapToFriendlyName_KnownCategories(string input, string expected)
    {
        Assert.Equal(expected, WasteSummaryMapper.MapToFriendlyName(input));
    }

    [Fact]
    public void MapToFriendlyName_EmptyOrNull_FallbackLabel()
    {
        Assert.Equal("🗑️ Unbekannte Abfuhr", WasteSummaryMapper.MapToFriendlyName(""));
        Assert.Equal("🗑️ Unbekannte Abfuhr", WasteSummaryMapper.MapToFriendlyName("   "));
        Assert.Equal("🗑️ Unbekannte Abfuhr", WasteSummaryMapper.MapToFriendlyName(null!));
    }

    [Fact]
    public void MapToFriendlyName_UnknownSummary_PrefixedWithTrashEmoji()
    {
        var result = WasteSummaryMapper.MapToFriendlyName("Sperrmüll");
        Assert.Equal("🗑️ Sperrmüll", result);
    }

    [Fact]
    public void MapToFriendlyName_PreferRestabfall_OverColorWhenBothPresent()
    {
        // "schwarz" + "Papier" should resolve via Restabfall precedence first
        var result = WasteSummaryMapper.MapToFriendlyName("Restabfall und Papier");
        Assert.Contains("Schwarze Tonne", result);
    }

    [Fact]
    public void MapToFriendlyName_WertstoffBeforePapier_WhenBothPresent()
    {
        var result = WasteSummaryMapper.MapToFriendlyName("Wertstoffe Papier");
        Assert.Contains("Gelbe Tonne", result);
    }

    [Fact]
    public void MapToFriendlyName_BioKeyword_RequiresBoundaryToAvoidFalsePositive()
    {
        // "Biogut" matches; "Kiosk" must not match bare "bio"
        Assert.Contains("Braune Tonne", WasteSummaryMapper.MapToFriendlyName("Biogut"));
        Assert.Equal("🗑️ Kiosk", WasteSummaryMapper.MapToFriendlyName("Kiosk"));
    }
}

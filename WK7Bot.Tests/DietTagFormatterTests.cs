using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public class DietTagFormatterTests
{
    [Theory]
    [InlineData("low_potassium", "Kaliumarm")]
    [InlineData("low_phosphate", "Phosphatarm")]
    [InlineData("low_sodium", "Natriumarm")]
    [InlineData("low_carb", "Kohlenhydratarm")]
    [InlineData("protein_rich", "Proteinreich")]
    [InlineData("high_fiber", "Ballaststoffreich")]
    public void ToGermanLabel_MapsKnownTags(string tag, string expected)
    {
        Assert.Equal(expected, DietTagFormatter.ToGermanLabel(tag));
    }

    [Fact]
    public void ToGermanLabel_IsCaseInsensitive()
    {
        Assert.Equal("Kaliumarm", DietTagFormatter.ToGermanLabel("LOW_POTASSIUM"));
    }

    [Fact]
    public void ToGermanLabel_FallsBackToRawTag_WhenUnknown()
    {
        Assert.Equal("custom_tag", DietTagFormatter.ToGermanLabel("custom_tag"));
    }

    [Fact]
    public void Format_JoinsLabelsAsBullets()
    {
        var formatted = DietTagFormatter.Format(new[] { "low_potassium", "protein_rich" });

        Assert.Equal("Kaliumarm • Proteinreich", formatted);
    }

    [Fact]
    public void Format_ReturnsPlaceholder_WhenEmptyOrNull()
    {
        Assert.Equal("—", DietTagFormatter.Format(null));
        Assert.Equal("—", DietTagFormatter.Format(new List<string>()));
        Assert.Equal("—", DietTagFormatter.Format(new[] { "", "   " }));
    }

    [Fact]
    public void Format_SkipsBlankEntries()
    {
        Assert.Equal("Kaliumarm", DietTagFormatter.Format(new[] { " ", "low_potassium" }));
    }
}

using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using Xunit;

namespace WK7Bot.Tests;

public class SeasonalTermPickerTests
{
    private static SeasonalFoodData CreateProduce() => new()
    {
        Month = "Juli",
        Vegetables = new List<string> { "Zucchini" },
        Fruits = new List<string> { "Aprikose" },
        Herbs = new List<string> { "Basilikum" },
        Nuts = new List<string> { "Walnuss" }
    };

    [Fact]
    public void Pick_ReturnsEmpty_WhenProduceIsMissing()
    {
        Assert.Empty(SeasonalTermPicker.Pick(null, new StubRandomSource()));
    }

    [Fact]
    public void Pick_ReturnsEmpty_WhenProduceIsEmpty()
    {
        var produce = new SeasonalFoodData();

        Assert.Empty(SeasonalTermPicker.Pick(produce, new StubRandomSource()));
    }

    [Fact]
    public void Pick_ReturnsTwoDistinctTerms_FromTheWholePool()
    {
        var produce = CreateProduce();

        var terms = SeasonalTermPicker.Pick(produce, new StubRandomSource(9));

        // Roll 9 of the 10 total weight points at the nut, the follow-up roll 0 at the heaviest vegetable.
        Assert.Equal(new[] { "Walnuss", "Zucchini" }, terms);
    }

    [Fact]
    public void Pick_WeightsCategories_VegetablesBeforeFruitsBeforeHerbsBeforeNuts()
    {
        var produce = new SeasonalFoodData
        {
            Vegetables = new List<string> { "Zucchini", "Bohnen" },
            Fruits = new List<string> { "Aprikose" },
            Herbs = new List<string> { "Basilikum" },
            Nuts = new List<string> { "Walnuss" }
        };

        // Total weight 4+4+3+2+1 = 14: roll 7 lands in the vegetable bucket, roll 13 in the nut bucket.
        Assert.Equal("Bohnen", SeasonalTermPicker.Pick(produce, new StubRandomSource(7)).First());
        Assert.Equal("Walnuss", SeasonalTermPicker.Pick(produce, new StubRandomSource(13)).First());
        Assert.Equal("Aprikose", SeasonalTermPicker.Pick(produce, new StubRandomSource(10)).First());
        Assert.Equal("Basilikum", SeasonalTermPicker.Pick(produce, new StubRandomSource(12)).First());
    }

    [Fact]
    public void Pick_SkipsHerbsAndNuts_WhenVegetablesAndFruitsRemain()
    {
        var produce = CreateProduce();

        var terms = SeasonalTermPicker.Pick(produce, new StubRandomSource(0, 0));

        Assert.Equal(new[] { "Zucchini", "Aprikose" }, terms);
    }

    [Fact]
    public void Pick_ReturnsEverythingAvailable_WhenPoolIsSmallerThanTwo()
    {
        var produce = new SeasonalFoodData
        {
            Vegetables = new List<string> { "Zucchini" }
        };

        var terms = SeasonalTermPicker.Pick(produce, new StubRandomSource());

        Assert.Equal(new[] { "Zucchini" }, terms);
    }

    [Fact]
    public void Pick_IgnoresBlankAndDuplicateEntries_AcrossCategories()
    {
        var produce = new SeasonalFoodData
        {
            Vegetables = new List<string> { " Zucchini ", "", "zucchini" },
            Fruits = new List<string> { "Zucchini", "Aprikose" },
            Herbs = new List<string> { "  " },
            Nuts = new List<string>()
        };

        var terms = SeasonalTermPicker.Pick(produce, new StubRandomSource());

        Assert.Equal(new[] { "Zucchini", "Aprikose" }, terms);
    }
}

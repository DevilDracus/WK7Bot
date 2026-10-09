using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public class EmbedTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Truncate_NullOrEmpty_ReturnsEmpty(string? value)
    {
        Assert.Equal(string.Empty, EmbedText.Truncate(value, 10));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Truncate_NonPositiveLimit_ReturnsEmpty(int limit)
    {
        Assert.Equal(string.Empty, EmbedText.Truncate("value", limit));
    }

    [Fact]
    public void Truncate_LimitOne_ReturnsEllipsisOnly()
    {
        Assert.Equal("…", EmbedText.Truncate("value", 1));
    }

    [Fact]
    public void Truncate_ValueWithinLimit_IsUnchanged()
    {
        Assert.Equal("kurz", EmbedText.Truncate("kurz", 4));
    }

    [Fact]
    public void Truncate_LongValue_TruncatesToLimitWithEllipsis()
    {
        var result = EmbedText.Truncate(new string('x', 100), 10);

        Assert.Equal(10, result.Length);
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void Title_AppliesTitleLimit()
    {
        var result = EmbedText.Title(new string('x', EmbedText.TitleLimit + 50));

        Assert.Equal(EmbedText.TitleLimit, result.Length);
    }

    [Fact]
    public void Description_AppliesDescriptionLimit()
    {
        var result = EmbedText.Description(new string('x', EmbedText.DescriptionLimit + 50));

        Assert.Equal(EmbedText.DescriptionLimit, result.Length);
    }

    [Fact]
    public void Field_AppliesFieldValueLimit()
    {
        var result = EmbedText.Field(new string('x', EmbedText.FieldValueLimit + 50));

        Assert.Equal(EmbedText.FieldValueLimit, result.Length);
    }
}

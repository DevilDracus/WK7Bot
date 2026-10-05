using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public class FeedTextFormatterTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("Plain text", "Plain text")]
    [InlineData("<p>Hello <b>world</b></p>", "Hello world")]
    [InlineData("<a href=\"https://x.test\">click</a>", "click")]
    [InlineData("A &amp; B", "A & B")]
    [InlineData("Line1<br>Line2", "Line1Line2")]
    public void SanitizeFeedDescription_StripsHtmlAndDecodesEntities(string? input, string expected)
    {
        Assert.Equal(expected, FeedTextFormatter.SanitizeFeedDescription(input));
    }

    [Fact]
    public void SanitizeFeedDescription_TruncatesTo500WithEllipsis()
    {
        var input = "<p>" + new string('x', 800) + "</p>";
        var result = FeedTextFormatter.SanitizeFeedDescription(input);

        Assert.Equal(500, result.Length);
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void SanitizeFeedDescription_CustomMaxLength()
    {
        var result = FeedTextFormatter.SanitizeFeedDescription(new string('y', 50), maxLength: 10);
        Assert.Equal(10, result.Length);
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void SanitizeFeedDescription_DoesNotTruncateWhenAtLimit()
    {
        var input = new string('z', 500);
        var result = FeedTextFormatter.SanitizeFeedDescription(input);
        Assert.Equal(500, result.Length);
        Assert.DoesNotContain("...", result);
    }

    [Fact]
    public void TruncateWithEllipsis_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => FeedTextFormatter.TruncateWithEllipsis(null!, 10));
    }

    [Fact]
    public void TruncateWithEllipsis_ThrowsWhenMaxLengthTooSmall()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FeedTextFormatter.TruncateWithEllipsis("abc", 2));
    }

    [Fact]
    public void TruncateWithEllipsis_ShortInputUnchanged()
    {
        Assert.Equal("hi", FeedTextFormatter.TruncateWithEllipsis("hi", 10));
    }
}

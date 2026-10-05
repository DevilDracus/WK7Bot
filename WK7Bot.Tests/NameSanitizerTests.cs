using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public class NameSanitizerTests
{
    [Theory]
    [InlineData("My Feed", "my-feed")]
    [InlineData("UPPER", "upper")]
    [InlineData("no-spaces", "no-spaces")]
    [InlineData("  padded  ", "--padded--")]
    public void ToChannelSlug_LowercasesAndHyphenates(string input, string expected)
    {
        Assert.Equal(expected, NameSanitizer.ToChannelSlug(input));
    }

    [Fact]
    public void ToChannelSlug_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => NameSanitizer.ToChannelSlug(null!));
    }

    [Theory]
    [InlineData("general", "general")]
    [InlineData("General #1", "general__1")]
    [InlineData("name-with-dash", "name_with_dash")]
    [InlineData("Already_Valid", "already_valid")]
    [InlineData("two  spaces", "two__spaces")]
    public void ToMqttSafeName_ReplacesUnsafeCharacters(string input, string expected)
    {
        Assert.Equal(expected, NameSanitizer.ToMqttSafeName(input));
    }

    [Fact]
    public void ToMqttSafeName_ReplacesEmojiAndSymbols()
    {
        var safe = NameSanitizer.ToMqttSafeName("emoji 🗑️ waste");
        Assert.Matches("^[a-z0-9_]+$", safe);
        Assert.StartsWith("emoji_", safe);
        Assert.EndsWith("_waste", safe);
    }

    [Fact]
    public void ToMqttSafeName_AlwaysMatchesSafePattern()
    {
        var names = new[] { "General #2!", "Ümlaut-Name", "spaces here", "!!!", "" };
        foreach (var name in names)
        {
            var safe = NameSanitizer.ToMqttSafeName(name);
            Assert.Matches("^[a-z0-9_]*$", safe);
        }
    }

    [Fact]
    public void ToMqttSafeName_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => NameSanitizer.ToMqttSafeName(null!));
    }
}

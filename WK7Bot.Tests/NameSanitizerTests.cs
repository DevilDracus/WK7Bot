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

    [Theory]
    [InlineData("PD: Leipzig", "pd-leipzig")]
    [InlineData("General #1", "general-1")]
    [InlineData("Was? Ja!", "was-ja")]
    [InlineData("a/b\\c", "abc")]
    public void ToChannelSlug_RemovesCharactersDiscordRejects(string input, string expected)
    {
        Assert.Equal(expected, NameSanitizer.ToChannelSlug(input));
    }

    [Fact]
    public void ToChannelSlug_KeepsUnicodeLetters()
    {
        Assert.Equal("ümlaut-ä-ö-ü", NameSanitizer.ToChannelSlug("Ümlaut Ä Ö Ü"));
    }

    [Fact]
    public void ToChannelSlug_FallsBackWhenNothingUsableRemains()
    {
        Assert.Equal(NameSanitizer.FallbackChannelSlug, NameSanitizer.ToChannelSlug("!!!"));
        Assert.Equal(NameSanitizer.FallbackChannelSlug, NameSanitizer.ToChannelSlug(":?"));
    }

    [Fact]
    public void ToChannelSlug_WhitespaceOnlyNameBecomesHyphens()
    {
        // Hyphens alone are a valid Discord channel name, so no fallback applies.
        Assert.Equal("---", NameSanitizer.ToChannelSlug("   "));
    }

    [Fact]
    public void ToChannelSlug_CapsAtDiscordLimit()
    {
        var slug = NameSanitizer.ToChannelSlug(new string('a', NameSanitizer.MaxChannelNameLength + 50));

        Assert.Equal(NameSanitizer.MaxChannelNameLength, slug.Length);
    }

    [Fact]
    public void ToChannelSlug_OutputAlwaysMatchesDiscordRules()
    {
        var names = new[] { "PD: Leipzig", "emoji 🗑️ waste", "Ümlaut-Name", "Was? Ja!", "###", "a" };

        foreach (var name in names)
        {
            var slug = NameSanitizer.ToChannelSlug(name);
            Assert.Matches("^[\\p{L}\\p{N}_-]{1,100}$", slug);
        }
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

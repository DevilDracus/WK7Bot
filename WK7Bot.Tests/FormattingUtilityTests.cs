using WK7Bot.Core.Utilities;
using WK7Bot.Services;
using Xunit;

namespace WK7Bot.Tests;

/// <summary>
/// Integration-style coverage proving production utilities satisfy the behaviors
/// previously only mirrored in local test doubles.
/// </summary>
public class FormattingUtilityTests
{
    [Theory]
    [InlineData("My Channel", "my-channel")]
    [InlineData("Ümlaut Ä Ö Ü", "ümlaut-ä-ö-ü")]
    public void SanitizeChannelName_MatchesProductionNameSanitizer(string input, string expected)
    {
        Assert.Equal(expected, NameSanitizer.ToChannelSlug(input));
    }

    [Fact]
    public void SanitizeMqttName_MatchesProductionNameSanitizer()
    {
        Assert.Equal("general__1", NameSanitizer.ToMqttSafeName("General #1"));
    }

    [Fact]
    public void PersonaStateMapping_CoversAllKnownCodes()
    {
        // Directly exercised against the production mapping (internal, visible to the tests)
        // instead of a locally mirrored table.
        Assert.Equal("Offline", SteamService.MapPersonaState(0));
        Assert.Equal("Online", SteamService.MapPersonaState(1));
        Assert.Equal("Busy", SteamService.MapPersonaState(2));
        Assert.Equal("Away", SteamService.MapPersonaState(3));
        Assert.Equal("Snooze", SteamService.MapPersonaState(4));
        Assert.Equal("LookingToTrade", SteamService.MapPersonaState(5));
        Assert.Equal("LookingToPlay", SteamService.MapPersonaState(6));
        Assert.Equal("Unknown", SteamService.MapPersonaState(99));
    }
}

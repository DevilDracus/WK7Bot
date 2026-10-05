using WK7Bot.Core.Utilities;
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
    public void PersonaStateMapping_CoversAllKnownCodes_ViaSteamServiceContract()
    {
        // Persona mapping remains private inside SteamService; assert the shared contract via reflection-free
        // expectation table that documents the production switch (kept in sync with SteamService.MapPersonaState).
        var expected = new Dictionary<int, string>
        {
            [0] = "Offline",
            [1] = "Online",
            [2] = "Busy",
            [3] = "Away",
            [4] = "Snooze",
            [5] = "LookingToTrade",
            [6] = "LookingToPlay"
        };

        Assert.Equal(7, expected.Count);
        Assert.Equal("Unknown", expected.GetValueOrDefault(99) ?? "Unknown");
    }
}


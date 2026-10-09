using WK7Bot.Services;
using Xunit;

namespace WK7Bot.Tests;

/// <summary>
/// Covers the pure helpers of the error notification dispatcher that do not need a Discord gateway.
/// </summary>
public class ErrorNotificationDispatcherTests
{
    [Fact]
    public void BuildChannelPing_MentionsEveryConfiguredUser()
    {
        var ping = ErrorNotificationDispatcher.BuildChannelPing(new[] { "123456789012345678", "162201162257399808" });

        Assert.Equal("<@123456789012345678> <@162201162257399808>", ping);
    }

    [Fact]
    public void BuildChannelPing_IsEmpty_WhenNoUsersAreConfigured()
    {
        Assert.Equal(string.Empty, ErrorNotificationDispatcher.BuildChannelPing(Array.Empty<string>()));
        Assert.Equal(string.Empty, ErrorNotificationDispatcher.BuildChannelPing(new List<string>()));
    }

    [Fact]
    public void BuildChannelPing_SkipsMalformedEntriesInsteadOfPingingThem()
    {
        var ping = ErrorNotificationDispatcher.BuildChannelPing(new[] { "not-a-user", "123456789012345678", "+456", "" });

        Assert.Equal("<@123456789012345678>", ping);
    }
}

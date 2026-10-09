using Microsoft.Extensions.Logging;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;
using Xunit;

namespace WK7Bot.Tests;

public class AutomaticTargetResolverTests
{
    private const ulong Wk7Server = 1549409126625575004;
    private const ulong TestServer = 551130054776717323;
    private const ulong OtherGuild = 42;

    private static readonly ulong[] AvailableGuilds = { OtherGuild, Wk7Server, TestServer };

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static ServersOptions Configured(string? wk7 = null, string? test = null, params string[] testingFeatures)
        => new()
        {
            Wk7ServerId = wk7 ?? string.Empty,
            TestServerId = test ?? string.Empty,
            TestingFeatures = testingFeatures.ToList()
        };

    [Fact]
    public void Resolve_FeatureWithoutTestingEntry_PostsToWk7ServerOnly()
    {
        var servers = Configured(Wk7Server.ToString(), TestServer.ToString());

        var targets = AutomaticTargetResolver.Resolve(servers, "weekend_digest", AvailableGuilds);

        Assert.Equal(new[] { Wk7Server }, targets);
    }

    [Fact]
    public void Resolve_FeatureUnderTest_PostsToTestServerOnly()
    {
        var servers = Configured(Wk7Server.ToString(), TestServer.ToString(), "dwd_warning");

        var targets = AutomaticTargetResolver.Resolve(servers, "dwd_warning", AvailableGuilds);

        Assert.Equal(new[] { TestServer }, targets);
    }

    [Fact]
    public void Resolve_FeatureUnderTest_WithoutTestServerId_FallsBackToWk7Server()
    {
        var servers = Configured(Wk7Server.ToString(), "YOUR_BOT_TEST_SERVER_ID", "dwd_warning");

        var targets = AutomaticTargetResolver.Resolve(servers, "dwd_warning", AvailableGuilds);

        Assert.Equal(new[] { Wk7Server }, targets);
    }

    [Fact]
    public void Resolve_PlaceholderIds_FallBackToAllGuilds_AndLogWarning()
    {
        var servers = Configured("YOUR_WK7_SERVER_ID", "YOUR_BOT_TEST_SERVER_ID");
        var logger = new ListLogger();

        var targets = AutomaticTargetResolver.Resolve(servers, "leipzig_waste", AvailableGuilds, logger);

        Assert.Equal(AvailableGuilds, targets);
        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("leipzig_waste", warning.Message);
    }

    [Fact]
    public void Resolve_ConfiguredServerNotInGuildList_StillTargetsThatServer()
    {
        var servers = Configured(Wk7Server.ToString());

        var targets = AutomaticTargetResolver.Resolve(servers, "food_service", new ulong[] { OtherGuild });

        Assert.Equal(new[] { Wk7Server }, targets);
    }

    [Fact]
    public void Resolve_ConfiguredTarget_DoesNotLogAWarning()
    {
        var servers = Configured(Wk7Server.ToString());
        var logger = new ListLogger();

        AutomaticTargetResolver.Resolve(servers, "food_service", AvailableGuilds, logger);

        Assert.Empty(logger.Entries);
    }
}

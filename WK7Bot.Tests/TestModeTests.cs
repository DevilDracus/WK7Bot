using Microsoft.Extensions.Logging.Abstractions;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;
using Xunit;

namespace WK7Bot.Tests;

/// <summary>
/// Verifies the ambient test-mode scope and the guild routing override it applies to
/// <see cref="AutomaticTargetResolver"/>, so a test-API invocation can never reach the
/// production server.
/// </summary>
public class TestModeTests
{
    private const ulong TestGuild = 551130054776717323ul;
    private const ulong ProductionGuild = 999000111222333444ul;

    private static ServersOptions CreateServers()
    {
        var servers = new ServersOptions
        {
            Wk7ServerId = ProductionGuild.ToString(),
            TestServerId = TestGuild.ToString()
        };
        return servers;
    }

    [Fact]
    public void IsActive_IsFalseOutsideScope()
    {
        Assert.False(TestMode.IsActive);
        Assert.Null(TestMode.GuildOverride);
    }

    [Fact]
    public void Enter_ActivatesScopeAndRestoresOnDispose()
    {
        using (TestMode.Enter(TestGuild))
        {
            Assert.True(TestMode.IsActive);
            Assert.Equal(TestGuild, TestMode.GuildOverride);
        }

        Assert.False(TestMode.IsActive);
    }

    [Fact]
    public void Enter_Nested_RestoresOuterScope()
    {
        using (TestMode.Enter(TestGuild))
        {
            using (TestMode.Enter(null))
            {
                Assert.True(TestMode.IsActive);
                Assert.Null(TestMode.GuildOverride);
            }

            Assert.Equal(TestGuild, TestMode.GuildOverride);
        }

        Assert.False(TestMode.IsActive);
    }

    [Fact]
    public void Resolve_WithActiveScope_TargetsTestGuildInsteadOfProduction()
    {
        var servers = CreateServers();

        using (TestMode.Enter(TestGuild))
        {
            var targets = AutomaticTargetResolver.Resolve(servers, "rss_polling", new[] { ProductionGuild }, NullLogger.Instance);
            Assert.Equal(new[] { TestGuild }, targets);
        }
    }

    [Fact]
    public void ResolveTestServerOnly_WithActiveScope_TargetsTestGuild()
    {
        var servers = CreateServers();

        using (TestMode.Enter(TestGuild))
        {
            var targets = AutomaticTargetResolver.ResolveTestServerOnly(servers, NullLogger.Instance);
            Assert.Equal(new[] { TestGuild }, targets);
        }
    }

    [Fact]
    public void Resolve_WithoutScope_StillUsesConfiguredProductionServer()
    {
        var servers = CreateServers();

        var targets = AutomaticTargetResolver.Resolve(servers, "rss_polling", new[] { ProductionGuild }, NullLogger.Instance);

        Assert.Equal(new[] { ProductionGuild }, targets);
    }
}

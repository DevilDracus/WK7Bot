using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WK7Bot.Testing;
using Xunit;

namespace WK7Bot.Tests;

public class TestCommandRunnerFailureTests
{
    [Fact]
    public async Task RunAsync_UnknownCommand_ReturnsFailureResult_WithoutThrowing()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(new DiscordSocketClient());

        using var provider = services.BuildServiceProvider();

        var result = await TestCommandRunner.RunAsync(
            provider,
            "does-not-exist",
            new TestRunRequest(),
            guildId: 1,
            userId: 2,
            username: "jarvis-test",
            ct: CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("does-not-exist", result.Command);
        Assert.Contains("Unknown test command", result.Error, StringComparison.Ordinal);
    }
}

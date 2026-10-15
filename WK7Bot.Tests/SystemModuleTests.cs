using System.Runtime.CompilerServices;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Core.Utilities;
using WK7Bot.Infrastructure.Data;
using WK7Bot.Modules;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public sealed class SystemModuleTests
{
    private sealed class TestSystemModule : SystemModule
    {
        public List<Embed> RespondedEmbeds { get; } = new();
        public List<string> RespondedTexts { get; } = new();

        public TestSystemModule(
            IHomeAssistantService homeAssistantService,
            CommandUsageRepository commandUsageRepository,
            FeatureHealthTracker healthTracker,
            IExternalDependencyHealthChecker dependencyHealthChecker)
            : base(
                homeAssistantService,
                commandUsageRepository,
                healthTracker,
                dependencyHealthChecker,
                NullLogger<SystemModule>.Instance)
        {
        }

        protected override Task RespondAsync(
            string? text = null,
            Embed[]? embeds = null,
            bool isTTS = false,
            bool ephemeral = false,
            AllowedMentions? allowedMentions = null,
            RequestOptions? options = null,
            MessageComponent? components = null,
            Embed? embed = null,
            PollProperties? poll = null)
        {
            if (embed != null)
                RespondedEmbeds.Add(embed);
            if (text != null)
                RespondedTexts.Add(text);
            return Task.CompletedTask;
        }
    }

    private static TestSystemModule CreateModule(SocketInteractionContext context)
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(databaseName: $"system-tests-{Guid.NewGuid()}")
            .Options;
        var db = new BotDbContext(options);

        var module = new TestSystemModule(
            new Mock<IHomeAssistantService>().Object,
            new CommandUsageRepository(new PooledDbContextFactory<BotDbContext>(options)),
            new FeatureHealthTracker(),
            new Mock<IExternalDependencyHealthChecker>().Object);

        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    [Fact]
    public async Task HelpCommandAsync_DMContext_RespondsWithoutError()
    {
        // Arrange: build a DM interaction context (no guild).
        var context = (SocketInteractionContext)RuntimeHelpers.GetUninitializedObject(typeof(SocketInteractionContext));
        DiscordTestDoubles.SetUser(context, "tester", 123456789ul);

        var module = CreateModule(context);

        // Act & Assert: must not throw — before the fix, .Result on guild lookups threw NRE in DMs.
        await module.HelpCommandAsync();

        Assert.Single(module.RespondedEmbeds);
    }
}

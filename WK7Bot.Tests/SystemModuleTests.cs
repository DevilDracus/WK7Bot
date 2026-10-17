using System.Collections.Generic;
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
        public Mock<IHomeAssistantService> HomeAssistant { get; }

        public Mock<IExternalDependencyHealthChecker> DependencyHealthChecker { get; }

        public List<Embed> RespondedEmbeds { get; } = new();
        public List<MessageComponent> RespondedComponents { get; } = new();
        public List<string> RespondedTexts { get; } = new();

        public int DeferCount { get; private set; }

        public TestSystemModule(
            IHomeAssistantService homeAssistantService,
            IExternalDependencyHealthChecker dependencyHealthChecker,
            CommandUsageRepository commandUsageRepository,
            FeatureHealthTracker healthTracker)
            : base(
                homeAssistantService,
                commandUsageRepository,
                healthTracker,
                dependencyHealthChecker,
                new CommandRateLimiter(),
                NullLogger<SystemModule>.Instance)
        {
            HomeAssistant = (Mock<IHomeAssistantService>)Mock.Get(homeAssistantService);
            DependencyHealthChecker = (Mock<IExternalDependencyHealthChecker>)Mock.Get(dependencyHealthChecker);
        }

        protected override Task DeferAsync(bool ephemeral, RequestOptions options)
        {
            DeferCount++;
            return Task.CompletedTask;
        }

        protected override Task<IUserMessage> FollowupAsync(
            string text,
            Embed[] embeds,
            bool isTTS,
            bool ephemeral,
            AllowedMentions allowedMentions,
            RequestOptions options,
            MessageComponent components,
            Embed embed,
            PollProperties? poll = null,
            MessageFlags flags = MessageFlags.None)
        {
            RespondedTexts.Add(text ?? string.Empty);
            return Task.FromResult<IUserMessage>(null!);
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
            PollProperties? poll = null,
            MessageFlags flags = MessageFlags.None)
        {
            if (embed != null)
                RespondedEmbeds.Add(embed);
            if (components != null)
                RespondedComponents.Add(components);
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
        var homeAssistant = new Mock<IHomeAssistantService>();
        homeAssistant
            .Setup(s => s.GetApiStateAsync("config", It.IsAny<CancellationToken>()))
            .ReturnsAsync("ok");

        var dependencyHealth = new Mock<IExternalDependencyHealthChecker>();
        dependencyHealth
            .Setup(s => s.CheckAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DependencyHealthStatus>());

        var module = new TestSystemModule(
            homeAssistant.Object,
            dependencyHealth.Object,
            new CommandUsageRepository(new PooledDbContextFactory<BotDbContext>(options)),
            new FeatureHealthTracker());

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

        Assert.Single(module.RespondedComponents);
    }

    [Fact]
    public async Task HomeAssistantStatusCommandAsync_ImmediateSecondCall_IsRateLimited()
    {
        var context = (SocketInteractionContext)RuntimeHelpers.GetUninitializedObject(typeof(SocketInteractionContext));
        DiscordTestDoubles.SetUser(context, "tester", 424242ul);
        var module = CreateModule(context);

        await module.HomeAssistantStatusCommandAsync();
        var deferCountAfterFirst = module.DeferCount;

        await module.HomeAssistantStatusCommandAsync();

        Assert.Equal(deferCountAfterFirst, module.DeferCount);
        Assert.Contains("15 Sekunden", string.Join(" ", module.RespondedTexts));
        module.HomeAssistant.Verify(
            s => s.GetApiStateAsync("config", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HealthCommandAsync_ImmediateSecondCall_IsRateLimited()
    {
        var context = (SocketInteractionContext)RuntimeHelpers.GetUninitializedObject(typeof(SocketInteractionContext));
        DiscordTestDoubles.SetUser(context, "tester", 424242ul);
        var module = CreateModule(context);

        await module.HealthCommandAsync();
        var deferCountAfterFirst = module.DeferCount;

        await module.HealthCommandAsync();

        Assert.Equal(deferCountAfterFirst, module.DeferCount);
        Assert.Contains("15 Sekunden", string.Join(" ", module.RespondedTexts));
        module.DependencyHealthChecker.Verify(
            s => s.CheckAllAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

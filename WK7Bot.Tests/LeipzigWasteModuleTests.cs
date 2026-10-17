using System.Reflection;
using System.Runtime.CompilerServices;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Core.Utilities;
using WK7Bot.Modules;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class LeipzigWasteModuleTests
{
    private sealed class TestLeipzigWasteModule : LeipzigWasteModule
    {
        public int DeferCount { get; private set; }

        public int ResponseCount { get; private set; }

        public List<string> Followups { get; } = new();

        public Mock<ILeipzigWasteService> WasteService { get; }

        public TestLeipzigWasteModule(
            Mock<ILeipzigWasteService> wasteService,
            CommandRateLimiter rateLimiter)
            : base(wasteService.Object, rateLimiter, NullLogger<LeipzigWasteModule>.Instance)
        {
            WasteService = wasteService;
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
            ResponseCount++;
            return Task.CompletedTask;
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
            Followups.Add(text ?? string.Empty);
            return Task.FromResult<IUserMessage>(null!);
        }
    }

    private static TestLeipzigWasteModule CreateModule(Mock<ILeipzigWasteService> wasteService)
    {
        var module = new TestLeipzigWasteModule(wasteService, new CommandRateLimiter());
        var context = (SocketInteractionContext)RuntimeHelpers.GetUninitializedObject(typeof(SocketInteractionContext));
        DiscordTestDoubles.SetUser(context, "Tester", 123UL);
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    [Fact]
    public async Task CheckWasteAsync_FirstCall_DefersAndQueriesCalendar()
    {
        var wasteService = new Mock<ILeipzigWasteService>();
        wasteService
            .Setup(s => s.GetWasteTypesForDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Restabfall" });

        var module = CreateModule(wasteService);

        await module.CheckWasteAsync();

        Assert.Equal(1, module.DeferCount);
        Assert.Equal(0, module.ResponseCount);
        Assert.NotEmpty(module.Followups);
        wasteService.Verify(
            s => s.GetWasteTypesForDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task CheckWasteAsync_ImmediateSecondCall_IsRateLimited()
    {
        var wasteService = new Mock<ILeipzigWasteService>();
        wasteService
            .Setup(s => s.GetWasteTypesForDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Restabfall" });

        var module = CreateModule(wasteService);

        await module.CheckWasteAsync();
        wasteService.Invocations.Clear();
        await module.CheckWasteAsync();

        Assert.Equal(1, module.DeferCount);
        Assert.Equal(1, module.ResponseCount);
        wasteService.Verify(
            s => s.GetWasteTypesForDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

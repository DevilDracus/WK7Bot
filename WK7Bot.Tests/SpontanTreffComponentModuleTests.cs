using System.Reflection;
using System.Runtime.CompilerServices;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Infrastructure.Data;
using WK7Bot.Modules;
using Xunit;

namespace WK7Bot.Tests;

public class SpontanTreffComponentModuleTests
{
    private sealed class TestComponentModule : SpontanTreffComponentModule
    {
        private readonly bool _failEdit;

        public int DeferCallCount { get; private set; }
        public List<string> Followups { get; } = new();
        public List<(Embed Embed, MessageComponent Components)> Edits { get; } = new();

        public TestComponentModule(ISpontanTreffRepository repository, bool failEdit = false)
            : base(repository, NullLogger<SpontanTreffComponentModule>.Instance)
        {
            _failEdit = failEdit;
        }

        protected override Task EditMeetupMessageAsync(SpontanTreff meetup, Embed embed, MessageComponent components)
        {
            if (_failEdit)
            {
                throw new InvalidOperationException("The original meetup message was deleted.");
            }

            Edits.Add((embed, components));
            return Task.CompletedTask;
        }

        protected override Task DeferAsync(bool ephemeral, RequestOptions options)
        {
            DeferCallCount++;
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
            PollProperties poll)
        {
            Followups.Add(text ?? string.Empty);
            return Task.FromResult<IUserMessage>(null!);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public BotDbContext Context { get; }
        public ISpontanTreffRepository Repository { get; }

        public Fixture()
        {
            var options = new DbContextOptionsBuilder<BotDbContext>()
                .UseInMemoryDatabase(databaseName: $"spontan-treff-component-tests-{Guid.NewGuid()}")
                .Options;

            Context = new BotDbContext(options);
            Repository = new SpontanTreffRepository(Context);
        }

        public void Dispose()
        {
            Context.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    private static async Task<SpontanTreff> CreateMeetupAsync(
        Fixture fixture,
        bool closed = false,
        DateTime? expiresAt = null)
    {
        var meetup = await fixture.Repository.AddAsync(new SpontanTreff
        {
            GuildId = 1,
            ChannelId = 2,
            OrganizerId = 42,
            OrganizerName = "Tester",
            Plan = "Kaffee im Innenhof?",
            Location = "Innenhof",
            CreatedAt = DateTime.Now.AddMinutes(-3),
            ExpiresAt = expiresAt ?? DateTime.Now.AddMinutes(27),
            Closed = closed
        });

        await fixture.Repository.SetMessageAsync(meetup.Id, 777, 888);

        return (await fixture.Repository.GetAsync(meetup.Id))!;
    }

    private static TestComponentModule CreateModule(Fixture fixture, ulong actingUserId = 99, bool failEdit = false)
    {
        var module = new TestComponentModule(fixture.Repository, failEdit);

        var context = (SocketInteractionContext)RuntimeHelpers.GetUninitializedObject(typeof(SocketInteractionContext));
        SetUser(context, "Gast", actingUserId);
        ((IInteractionModuleBase)module).SetContext(context);

        return module;
    }

    private static void SetUser(SocketInteractionContext context, string username, ulong id)
    {
        var userField = FindField(context.GetType(), "<User>k__BackingField")
            ?? throw new InvalidOperationException("SocketInteractionContext user backing field not found.");
        userField.SetValue(context, CreateUser(username, id));
    }

    private static FieldInfo? FindField(Type type, string fieldName)
    {
        while (type != null)
        {
            var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
            {
                return field;
            }

            type = type.BaseType!;
        }

        return null;
    }

    private static SocketUser CreateUser(string username, ulong id)
    {
        var userType = typeof(SocketUser).Assembly.GetType("Discord.WebSocket.SocketGlobalUser")
            ?? throw new InvalidOperationException("Discord.WebSocket.SocketGlobalUser type not found.");

        var user = (SocketUser)RuntimeHelpers.GetUninitializedObject(userType);
        SetPropertyOrField(user, userType, "Username", username);
        SetPropertyOrField(user, userType, "Id", id);

        return user;
    }

    private static void SetPropertyOrField(object target, Type type, string name, object value)
    {
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.SetMethod != null)
        {
            property.SetValue(target, value);
            return;
        }

        var field = FindField(type, $"<{name}>k__BackingField")
            ?? throw new InvalidOperationException($"{type.Name} {name} backing field not found.");
        field.SetValue(target, value);
    }

    private static List<ButtonComponent> Buttons(MessageComponent component)
        => component.Components
            .SelectMany(row => row.Components)
            .OfType<ButtonComponent>()
            .ToList();

    [Fact]
    public async Task GoButton_AddsUserToGoingList_AndRendersEdit()
    {
        using var fixture = new Fixture();
        await CreateMeetupAsync(fixture);
        var module = CreateModule(fixture);

        await module.HandleResponseAsync("go:1");

        Assert.Equal(1, module.DeferCallCount);

        var response = Assert.Single(fixture.Context.SpontanTreffResponses.AsNoTracking());
        Assert.Equal(99ul, response.UserId);
        Assert.True(response.Going);

        var edit = Assert.Single(module.Edits);
        Assert.Equal(2, Buttons(edit.Components).Count);
        Assert.Contains("<@99>", edit.Embed.Fields.Single(f => f.Name.StartsWith("✅")).Value);
        Assert.StartsWith("✅", Assert.Single(module.Followups));
    }

    [Fact]
    public async Task GoButton_TappedTwice_RemovesTheAnswerAgain()
    {
        using var fixture = new Fixture();
        await CreateMeetupAsync(fixture);
        var module = CreateModule(fixture);

        await module.HandleResponseAsync("go:1");
        await module.HandleResponseAsync("go:1");

        Assert.Empty(fixture.Context.SpontanTreffResponses.AsNoTracking());
        Assert.Equal(2, module.Edits.Count);
        Assert.Equal("—", module.Edits[1].Embed.Fields.Single(f => f.Name.StartsWith("✅")).Value);
    }

    [Fact]
    public async Task PassButton_SwitchesGoingUserToDeclined()
    {
        using var fixture = new Fixture();
        await CreateMeetupAsync(fixture);
        var module = CreateModule(fixture);

        await module.HandleResponseAsync("go:1");
        await module.HandleResponseAsync("pass:1");

        var response = Assert.Single(fixture.Context.SpontanTreffResponses.AsNoTracking());
        Assert.Equal(99ul, response.UserId);
        Assert.False(response.Going);

        Assert.Equal(2, module.Edits.Count);
        var edit = module.Edits[^1];
        Assert.Contains("<@99>", edit.Embed.Fields.Single(f => f.Name.StartsWith("🚫")).Value);
    }

    [Fact]
    public async Task PassButton_TappedTwice_RemovesTheDeclineAgain()
    {
        using var fixture = new Fixture();
        await CreateMeetupAsync(fixture);
        var module = CreateModule(fixture);

        await module.HandleResponseAsync("pass:1");
        await module.HandleResponseAsync("pass:1");

        Assert.Empty(fixture.Context.SpontanTreffResponses.AsNoTracking());
        Assert.Equal("—", module.Edits[1].Embed.Fields.Single(f => f.Name.StartsWith("🚫")).Value);
    }

    [Fact]
    public async Task OtherUsers_AnswerIndependently()
    {
        using var fixture = new Fixture();
        await CreateMeetupAsync(fixture);
        var first = CreateModule(fixture, actingUserId: 99);
        var second = CreateModule(fixture, actingUserId: 100);

        await first.HandleResponseAsync("go:1");
        await second.HandleResponseAsync("pass:1");

        var responses = await fixture.Repository.GetResponsesAsync(1);
        Assert.Equal(2, responses.Count);
        Assert.Single(responses, r => r.UserId == 99 && r.Going);
        Assert.Single(responses, r => r.UserId == 100 && !r.Going);
    }

    [Fact]
    public async Task ExpiredMeetup_RejectsFurtherAnswers()
    {
        using var fixture = new Fixture();
        await CreateMeetupAsync(fixture, expiresAt: DateTime.Now.AddMinutes(-1));
        var module = CreateModule(fixture);

        await module.HandleResponseAsync("go:1");

        Assert.Empty(fixture.Context.SpontanTreffResponses.AsNoTracking());
        Assert.Empty(module.Edits);
        Assert.StartsWith("⌛", Assert.Single(module.Followups));
    }

    [Fact]
    public async Task ClosedMeetup_RejectsFurtherAnswers()
    {
        using var fixture = new Fixture();
        await CreateMeetupAsync(fixture, closed: true);
        var module = CreateModule(fixture);

        await module.HandleResponseAsync("go:1");

        Assert.Empty(fixture.Context.SpontanTreffResponses.AsNoTracking());
        Assert.Empty(module.Edits);
        Assert.StartsWith("⌛", Assert.Single(module.Followups));
    }

    [Theory]
    [InlineData("")]
    [InlineData("go:")]
    [InlineData("go:not-a-number")]
    [InlineData("nope:1")]
    [InlineData("1")]
    public async Task MalformedPayload_IsRejected(string payload)
    {
        using var fixture = new Fixture();
        await CreateMeetupAsync(fixture);
        var module = CreateModule(fixture);

        await module.HandleResponseAsync(payload);

        Assert.Empty(fixture.Context.SpontanTreffResponses.AsNoTracking());
        Assert.Empty(module.Edits);
        Assert.StartsWith("❌", Assert.Single(module.Followups));
    }

    [Fact]
    public async Task EditMeetupMessageFails_StillPersistsAnswerAndConfirms()
    {
        using var fixture = new Fixture();
        await CreateMeetupAsync(fixture);
        var module = CreateModule(fixture, failEdit: true);

        await module.HandleResponseAsync("go:1");

        // The answer is already saved — a failed re-render must not surface a fake "save failed".
        var response = Assert.Single(fixture.Context.SpontanTreffResponses.AsNoTracking());
        Assert.Equal(99ul, response.UserId);
        Assert.True(response.Going);
        Assert.Empty(module.Edits);
        Assert.StartsWith("✅", Assert.Single(module.Followups));
    }

    [Fact]
    public async Task UnknownMeetup_IsRejected()
    {
        using var fixture = new Fixture();
        var module = CreateModule(fixture);

        await module.HandleResponseAsync("go:4711");

        Assert.Empty(module.Edits);
        Assert.StartsWith("❌", Assert.Single(module.Followups));
    }

    [Fact]
    public void CustomIdMatcher_IsRegisteredForThisHandler()
    {
        var attribute = typeof(SpontanTreffComponentModule)
            .GetMethod(nameof(SpontanTreffComponentModule.HandleResponseAsync))!
            .GetCustomAttribute<ComponentInteractionAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("spontan-treff:*", attribute!.CustomId);
    }

    [Fact]
    public async Task InteractionService_MatchesOurButtonCustomIds()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISpontanTreffRepository>(Mock.Of<ISpontanTreffRepository>());
        await using var provider = services.BuildServiceProvider();

        using var interactions = new InteractionService(
            (DiscordSocketClient)RuntimeHelpers.GetUninitializedObject(typeof(DiscordSocketClient)));
        await interactions.AddModuleAsync(typeof(SpontanTreffComponentModule), provider);

        AssertMatches(interactions, SpontanTreffMessageBuilder.BuildCustomId("go", 7), "go:7");
        AssertMatches(interactions, SpontanTreffMessageBuilder.BuildCustomId("pass", 12), "pass:12");
    }

    private static void AssertMatches(
        InteractionService interactions,
        string customId,
        string expectedCapture)
    {
        var data = new Mock<IComponentInteractionData>();
        data.SetupGet(d => d.CustomId).Returns(customId);

        var interaction = new Mock<IComponentInteraction>();
        interaction.SetupGet(i => i.Data).Returns(data.Object);

        var result = interactions.SearchComponentCommand(interaction.Object);

        Assert.True(result.IsSuccess, $"Custom id '{customId}' did not match: {result.ErrorReason}");
        Assert.Equal(nameof(SpontanTreffComponentModule.HandleResponseAsync), result.Command.MethodName);
        Assert.Equal(expectedCapture, Assert.Single(result.RegexCaptureGroups));
    }
}

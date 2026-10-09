using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using WK7Bot.Modules;
using Xunit;

namespace WK7Bot.Tests;

public class SpontanTreffModuleTests
{
    private const string ChannelMention = "<#777>";

    private sealed class TestSpontanTreffModule : SpontanTreffModule
    {
        private readonly ITextChannel? _channel;
        private readonly bool _denyPingOnce;
        private bool _pingDenied;

        public int DeferCallCount { get; private set; }
        public List<string> Followups { get; } = new();
        public List<(string Text, Embed Embed, MessageComponent Components)> Posts { get; } = new();
        public ulong PostedMessageId { get; } = 424242;

        public TestSpontanTreffModule(ISpontanTreffRepository repository, ITextChannel? channel, bool denyPingOnce = false)
            : base(repository, NullLogger<SpontanTreffModule>.Instance)
        {
            _channel = channel;
            _denyPingOnce = denyPingOnce;
        }

        protected override Task<ITextChannel?> GetOrCreateTreffChannelAsync() => Task.FromResult(_channel);

        protected override Task<IUserMessage> PostToTreffChannelAsync(
            ITextChannel channel,
            string text,
            Embed embed,
            MessageComponent components)
        {
            if (_denyPingOnce && !_pingDenied)
            {
                _pingDenied = true;
                throw new HttpException(
                    HttpStatusCode.Forbidden,
                    null!,
                    null,
                    "Missing Permissions",
                    Array.Empty<DiscordJsonError>());
            }

            Posts.Add((text, embed, components));

            var message = new Mock<IUserMessage>();
            message.SetupGet(m => m.Id).Returns(PostedMessageId);
            return Task.FromResult(message.Object);
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
                .UseInMemoryDatabase(databaseName: $"spontan-treff-module-tests-{Guid.NewGuid()}")
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

    private static TestSpontanTreffModule CreateModule(
        Fixture fixture,
        ITextChannel? channel,
        string username = "Tester",
        ulong userId = 42,
        bool denyPingOnce = false)
    {
        var module = new TestSpontanTreffModule(fixture.Repository, channel, denyPingOnce);

        var context = (SocketInteractionContext)RuntimeHelpers.GetUninitializedObject(typeof(SocketInteractionContext));
        DiscordTestDoubles.SetUser(context, username, userId);
        ((IInteractionModuleBase)module).SetContext(context);

        return module;
    }

    private static ITextChannel CreateChannel(ulong id = 777)
        => Mock.Of<ITextChannel>(c => c.Id == id && c.Mention == ChannelMention);

    private static List<ButtonComponent> Buttons(MessageComponent component)
        => component.Components
            .SelectMany(row => row.Components)
            .OfType<ButtonComponent>()
            .ToList();

    [Fact]
    public async Task CreateAsync_PostsPingMessageWithButtons_AndStoresItsLocation()
    {
        using var fixture = new Fixture();
        var module = CreateModule(fixture, CreateChannel());

        await module.CreateAsync("Kurzer Abendspaziergang", "Park");

        Assert.Equal(1, module.DeferCallCount);

        var post = Assert.Single(module.Posts);
        Assert.Equal("@everyone", post.Text);
        Assert.Equal("⚡ Spontan-Treff", post.Embed.Title);
        Assert.Contains("Kurzer Abendspaziergang", post.Embed.Description);
        Assert.Contains("📍 Park", post.Embed.Description);

        var buttons = Buttons(post.Components);
        Assert.Equal(2, buttons.Count);
        Assert.Equal("spontan-treff:go:1", buttons[0].CustomId);
        Assert.Equal("spontan-treff:pass:1", buttons[1].CustomId);

        var meetup = Assert.Single(fixture.Context.SpontanTreffs.AsNoTracking());
        Assert.Equal(777ul, meetup.ChannelId);
        Assert.Equal(module.PostedMessageId, meetup.MessageId);
        Assert.Equal(42ul, meetup.OrganizerId);
        Assert.True(meetup.ExpiresAt > DateTime.Now.AddMinutes(29));
        Assert.True(meetup.ExpiresAt <= DateTime.Now.AddMinutes(31));

        var followup = Assert.Single(module.Followups);
        Assert.StartsWith("✅", followup);
        Assert.Contains(ChannelMention, followup);
    }

    [Fact]
    public async Task CreateAsync_HonoursCustomDuration()
    {
        using var fixture = new Fixture();
        var module = CreateModule(fixture, CreateChannel());

        await module.CreateAsync("Kaffee?", null, 90);

        var meetup = Assert.Single(fixture.Context.SpontanTreffs.AsNoTracking());
        Assert.True(meetup.ExpiresAt > DateTime.Now.AddMinutes(89));
        Assert.True(meetup.ExpiresAt <= DateTime.Now.AddMinutes(91));
        Assert.Null(meetup.Location);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1441)]
    public async Task CreateAsync_RejectsOutOfRangeDuration(int minutes)
    {
        using var fixture = new Fixture();
        var module = CreateModule(fixture, CreateChannel());

        await module.CreateAsync("Kaffee?", null, minutes);

        Assert.Empty(module.Posts);
        Assert.Empty(fixture.Context.SpontanTreffs.AsNoTracking());
        Assert.StartsWith("❌", Assert.Single(module.Followups));
    }

    [Fact]
    public async Task CreateAsync_RejectsBlankPlan()
    {
        using var fixture = new Fixture();
        var module = CreateModule(fixture, CreateChannel());

        await module.CreateAsync("   ");

        Assert.Empty(module.Posts);
        Assert.Empty(fixture.Context.SpontanTreffs.AsNoTracking());
        Assert.StartsWith("❌", Assert.Single(module.Followups));
    }

    [Fact]
    public async Task CreateAsync_ReportsMissingChannel()
    {
        using var fixture = new Fixture();
        var module = CreateModule(fixture, channel: null);

        await module.CreateAsync("Kaffee?");

        Assert.Empty(module.Posts);
        Assert.Empty(fixture.Context.SpontanTreffs.AsNoTracking());
        Assert.Contains("nicht gefunden oder erstellt werden", Assert.Single(module.Followups));
    }

    [Fact]
    public async Task CreateAsync_FallsBackToPlainMessage_WhenEveryonePingIsForbidden()
    {
        using var fixture = new Fixture();
        var module = CreateModule(fixture, CreateChannel(), denyPingOnce: true);

        await module.CreateAsync("Spaziergang?");

        var post = Assert.Single(module.Posts);
        Assert.Equal(string.Empty, post.Text);
        Assert.Equal("⚡ Spontan-Treff", post.Embed.Title);

        // Only one meetup exists: the failed ping attempt must not create a second one.
        Assert.Single(fixture.Context.SpontanTreffs.AsNoTracking());
        Assert.StartsWith("✅", Assert.Single(module.Followups));
    }

    [Fact]
    public async Task CreateAsync_ReportsUnexpectedErrors()
    {
        using var fixture = new Fixture();
        var throwing = new ThrowingModule(fixture.Repository);

        var context = (SocketInteractionContext)RuntimeHelpers.GetUninitializedObject(typeof(SocketInteractionContext));
        DiscordTestDoubles.SetUser(context, "Tester", 42);
        ((IInteractionModuleBase)throwing).SetContext(context);

        await throwing.CreateAsync("Kaffee?");

        Assert.StartsWith("❌", Assert.Single(throwing.Followups));
        Assert.Empty(fixture.Context.SpontanTreffs.AsNoTracking());
    }

    [Fact]
    public void CreateCommand_IsExposedAsSlashCommand()
    {
        var attribute = typeof(SpontanTreffModule).GetMethod(nameof(SpontanTreffModule.CreateAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("spontan-treff", attribute!.Name);
        Assert.False(string.IsNullOrWhiteSpace(attribute.Description));
        Assert.True(attribute.Description.Length <= 100);
    }

    private sealed class ThrowingModule : SpontanTreffModule
    {
        public List<string> Followups { get; } = new();

        public ThrowingModule(ISpontanTreffRepository repository)
            : base(repository, NullLogger<SpontanTreffModule>.Instance)
        {
        }

        protected override Task<ITextChannel?> GetOrCreateTreffChannelAsync()
            => throw new InvalidOperationException("simulated failure");

        protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;

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
}

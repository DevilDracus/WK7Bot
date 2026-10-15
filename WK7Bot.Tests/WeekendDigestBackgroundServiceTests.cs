using System.Runtime.CompilerServices;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class WeekendDigestBackgroundServiceTests
{
    private const string ExpectedTitlePrefix = "📅 Wochenend-Tipps";

    private static readonly DateTime Thursday1415 = new(2026, 10, 8, 14, 15, 0);
    private static readonly DateTime Thursday1431 = new(2026, 10, 8, 14, 31, 0);
    private static readonly DateTime Thursday1400 = new(2026, 10, 8, 14, 0, 0);
    private static readonly DateTime Friday1415 = new(2026, 10, 9, 14, 15, 0);
    private static readonly DateTime Wednesday1415 = new(2026, 10, 7, 14, 15, 0);

    private sealed class TestableDispatcher : WeekendDigestDispatcher
    {
        public List<ulong> ChannelRequests { get; } = new();
        public List<(ulong GuildId, string Title, bool HasPoll)> Posts { get; } = new();
        public HashSet<ulong> FailingGuilds { get; } = new();
        public bool ChannelUnavailable { get; set; }

        public TestableDispatcher()
            : base(
                (DiscordSocketClient)RuntimeHelpers.GetUninitializedObject(typeof(DiscordSocketClient)),
                NullLogger<WeekendDigestDispatcher>.Instance)
        {
        }

        protected override Task<ITextChannel?> GetOrCreateWeekendChannelAsync(ulong guildId)
        {
            ChannelRequests.Add(guildId);

            if (ChannelUnavailable)
            {
                return Task.FromResult<ITextChannel?>(null);
            }

            return Task.FromResult<ITextChannel?>(Mock.Of<ITextChannel>(c => c.Id == guildId));
        }

        protected override Task PostToWeekendChannelAsync(ITextChannel channel, Embed embed, PollProperties? poll)
        {
            if (FailingGuilds.Contains(channel.Id))
            {
                throw new InvalidOperationException($"simulated send failure for guild {channel.Id}");
            }

            Posts.Add((channel.Id, embed.Title ?? string.Empty, poll is not null));
            return Task.CompletedTask;
        }
    }

    private sealed class TestableWeekendDigestService : WeekendDigestBackgroundService
    {
        private readonly IReadOnlyList<ulong> _guildIds;

        public TestableWeekendDigestService(
            IServiceScopeFactory scopeFactory,
            IWeekendEventSource eventSource,
            IReadOnlyList<ulong> guildIds,
            WeekendDigestDispatcher dispatcher)
            : base(
                scopeFactory,
                (DiscordSocketClient)RuntimeHelpers.GetUninitializedObject(typeof(DiscordSocketClient)),
                eventSource,
                new Core.Utilities.FeatureHealthTracker(),
                Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()),
                NullLogger<WeekendDigestBackgroundService>.Instance,
                dispatcher)
            => _guildIds = guildIds;

        public Task EvaluateAsync(DateTime now, CancellationToken cancellationToken = default)
            => EvaluateScheduleAsync(now, cancellationToken);

        protected override IReadOnlyList<ulong> GetTargetGuildIds() => _guildIds;
    }

    private sealed class Fixture : IDisposable
    {
        public ServiceProvider Provider { get; }
        public IServiceScopeFactory ScopeFactory => new ServiceProviderScopeFactory(Provider);

        public Fixture()
        {
            var dbName = $"weekend-digest-tests-{Guid.NewGuid()}";

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: dbName));
            services.AddDbContextFactory<BotDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: dbName));
            services.AddScoped<IWasteDispatchRepository, WasteDispatchRepository>();
            Provider = services.BuildServiceProvider();
        }

        public async Task<bool> HasSentAsync(ulong guildId, DateTime date)
        {
            using var scope = Provider.CreateScope();
            return await scope.ServiceProvider
                .GetRequiredService<IWasteDispatchRepository>()
                .HasSentAsync(WeekendDigestBackgroundService.DispatchKind, guildId, date);
        }

        public void Dispose()
        {
            Provider.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    private static WeekendEvent Market(string title, DateTime date, string location)
        => new()
        {
            Title = title,
            StartDate = date,
            EndDate = date,
            TimeText = "10:00 – 16:00 Uhr",
            Location = location,
            Url = $"https://www.leipzig.de/kultur-und-freizeit/veranstaltungen/eventsingle/event/{title.GetHashCode():x}"
        };

    private static Mock<IWeekendEventSource> EventSourceReturning(params WeekendEvent[] events)
    {
        var mock = new Mock<IWeekendEventSource>();
        mock.Setup(s => s.FetchWeekendEventsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(events.ToList());
        return mock;
    }

    private static Mock<IWeekendEventSource> FailingEventSource()
    {
        var mock = new Mock<IWeekendEventSource>();
        mock.Setup(s => s.FetchWeekendEventsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("simulated fetch failure"));
        return mock;
    }

    [Fact]
    public async Task ThursdayDispatch_PostsDigest_Once_AcrossRestart()
    {
        using var fixture = new Fixture();
        var source = EventSourceReturning(
            Market("Wochenmarkt", new DateTime(2026, 10, 10), "Augustusplatz"),
            Market("Flohmarkt", new DateTime(2026, 10, 11), "Leuthof"));

        var firstDispatcher = new TestableDispatcher();
        var first = new TestableWeekendDigestService(fixture.ScopeFactory, source.Object, new ulong[] { 100 }, firstDispatcher);
        await first.EvaluateAsync(Thursday1415);

        var post = Assert.Single(firstDispatcher.Posts);
        Assert.Equal(100ul, post.GuildId);
        Assert.StartsWith(ExpectedTitlePrefix, post.Title);
        Assert.True(post.HasPoll);

        // Simulates a bot restart: fresh in-memory flags, same persistent database.
        var restartedDispatcher = new TestableDispatcher();
        var restarted = new TestableWeekendDigestService(fixture.ScopeFactory, source.Object, new ulong[] { 100 }, restartedDispatcher);
        await restarted.EvaluateAsync(Thursday1415);

        Assert.Empty(restartedDispatcher.Posts);
        Assert.True(await fixture.HasSentAsync(100, Thursday1415.Date));
    }

    [Fact]
    public async Task Dispatch_NotEvaluated_BeforeThursday1415()
    {
        using var fixture = new Fixture();
        var source = EventSourceReturning(Market("Wochenmarkt", new DateTime(2026, 10, 10), "Augustusplatz"));
        var dispatcher = new TestableDispatcher();
        var service = new TestableWeekendDigestService(fixture.ScopeFactory, source.Object, new ulong[] { 100 }, dispatcher);

        await service.EvaluateAsync(Thursday1400);

        Assert.Empty(dispatcher.Posts);
        source.Verify(
            s => s.FetchWeekendEventsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Dispatch_NotEvaluated_OnOtherDays()
    {
        using var fixture = new Fixture();
        var source = EventSourceReturning(Market("Wochenmarkt", new DateTime(2026, 10, 10), "Augustusplatz"));
        var dispatcher = new TestableDispatcher();
        var service = new TestableWeekendDigestService(fixture.ScopeFactory, source.Object, new ulong[] { 100 }, dispatcher);

        await service.EvaluateAsync(Wednesday1415);
        await service.EvaluateAsync(Friday1415);

        Assert.Empty(dispatcher.Posts);
        Assert.False(await fixture.HasSentAsync(100, Thursday1415.Date));
    }

    [Fact]
    public async Task FailedFetch_IsRetriedOnNextAttempt_AndNotMarked()
    {
        using var fixture = new Fixture();
        var failing = FailingEventSource();

        var firstDispatcher = new TestableDispatcher();
        var first = new TestableWeekendDigestService(fixture.ScopeFactory, failing.Object, new ulong[] { 100 }, firstDispatcher);
        await first.EvaluateAsync(Thursday1415);

        Assert.Empty(firstDispatcher.Posts);
        Assert.False(await fixture.HasSentAsync(100, Thursday1415.Date));

        // 15-minute retry delay has passed; the second attempt succeeds.
        var healthy = EventSourceReturning(Market("Wochenmarkt", new DateTime(2026, 10, 10), "Augustusplatz"));
        var retryDispatcher = new TestableDispatcher();
        var retry = new TestableWeekendDigestService(fixture.ScopeFactory, healthy.Object, new ulong[] { 100 }, retryDispatcher);
        await retry.EvaluateAsync(Thursday1431);

        Assert.Single(retryDispatcher.Posts);
        Assert.True(await fixture.HasSentAsync(100, Thursday1415.Date));
    }

    [Fact]
    public async Task RetryAttempts_AreSuppressed_WithinTheRetryDelay()
    {
        using var fixture = new Fixture();
        var failing = FailingEventSource();

        var service = new TestableWeekendDigestService(fixture.ScopeFactory, failing.Object, new ulong[] { 100 }, new TestableDispatcher());
        await service.EvaluateAsync(Thursday1415);
        await service.EvaluateAsync(Thursday1415.AddMinutes(5));

        failing.Verify(
            s => s.FetchWeekendEventsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FailedFetch_GivesUpAfterMaxAttempts()
    {
        using var fixture = new Fixture();
        var failing = FailingEventSource();

        var service = new TestableWeekendDigestService(fixture.ScopeFactory, failing.Object, new ulong[] { 100 }, new TestableDispatcher());

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var now = Thursday1415.AddMinutes(16 * attempt);
            await service.EvaluateAsync(now);
        }

        // Fifth attempt: the day is exhausted, nothing more happens.
        await service.EvaluateAsync(Thursday1415.AddMinutes(80));

        failing.Verify(
            s => s.FetchWeekendEventsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Exactly(4));
        Assert.False(await fixture.HasSentAsync(100, Thursday1415.Date));
    }

    [Fact]
    public async Task ZeroEvents_MarksDispatched_WithoutPosting()
    {
        using var fixture = new Fixture();
        var source = EventSourceReturning();
        var dispatcher = new TestableDispatcher();
        var service = new TestableWeekendDigestService(fixture.ScopeFactory, source.Object, new ulong[] { 100 }, dispatcher);

        await service.EvaluateAsync(Thursday1415);

        Assert.Empty(dispatcher.Posts);
        Assert.True(await fixture.HasSentAsync(100, Thursday1415.Date));
    }

    [Fact]
    public async Task MissingChannel_SkipsGuild_WithoutMarking()
    {
        using var fixture = new Fixture();
        var source = EventSourceReturning(Market("Wochenmarkt", new DateTime(2026, 10, 10), "Augustusplatz"));
        var dispatcher = new TestableDispatcher { ChannelUnavailable = true };
        var service = new TestableWeekendDigestService(fixture.ScopeFactory, source.Object, new ulong[] { 100 }, dispatcher);

        await service.EvaluateAsync(Thursday1415);

        Assert.Empty(dispatcher.Posts);
        Assert.False(await fixture.HasSentAsync(100, Thursday1415.Date));
    }

    [Fact]
    public async Task PartialFailure_RetriesOnlyFailingGuild_AndReusesBuiltDigest()
    {
        using var fixture = new Fixture();
        var source = EventSourceReturning(Market("Wochenmarkt", new DateTime(2026, 10, 10), "Augustusplatz"));

        var dispatcher = new TestableDispatcher();
        dispatcher.FailingGuilds.Add(2);
        var partial = new TestableWeekendDigestService(fixture.ScopeFactory, source.Object, new ulong[] { 1, 2 }, dispatcher);

        await partial.EvaluateAsync(Thursday1415);

        Assert.Single(dispatcher.Posts);
        Assert.Equal(1ul, dispatcher.Posts[0].GuildId);
        Assert.True(await fixture.HasSentAsync(1, Thursday1415.Date));
        Assert.False(await fixture.HasSentAsync(2, Thursday1415.Date));

        dispatcher.FailingGuilds.Remove(2);
        await partial.EvaluateAsync(Thursday1431);

        Assert.Equal(2, dispatcher.Posts.Count);
        Assert.Equal(2ul, dispatcher.Posts[1].GuildId);
        Assert.True(await fixture.HasSentAsync(2, Thursday1415.Date));

        // Both attempts shared one fetch thanks to the build-once digest cache.
        source.Verify(
            s => s.FetchWeekendEventsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Dispatch_RequestsUpcomingFriday_AsWeekendStart()
    {
        using var fixture = new Fixture();
        var source = EventSourceReturning(Market("Wochenmarkt", new DateTime(2026, 10, 10), "Augustusplatz"));
        var service = new TestableWeekendDigestService(fixture.ScopeFactory, source.Object, new ulong[] { 100 }, new TestableDispatcher());

        await service.EvaluateAsync(Thursday1415);

        source.Verify(
            s => s.FetchWeekendEventsAsync(new DateTime(2026, 10, 9), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

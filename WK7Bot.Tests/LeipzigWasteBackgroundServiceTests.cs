using System.Runtime.CompilerServices;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using WK7Bot.Options;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class LeipzigWasteBackgroundServiceTests
{
    private const string DailyTitle = "🗑️ Müllabholung Bestätigung";
    private const string WeeklyTitle = "🗑️ Stadtreinigung Leipzig — Wochenübersicht";

    private static readonly DateTime Monday0830 = new(2026, 10, 5, 8, 30, 0);
    private static readonly DateTime Monday0930 = new(2026, 10, 5, 9, 30, 0);
    private static readonly DateTime Monday1000 = new(2026, 10, 5, 10, 0, 0);
    private static readonly DateTime Tuesday0755 = new(2026, 10, 6, 7, 55, 0);
    private static readonly DateTime Tuesday0830 = new(2026, 10, 6, 8, 30, 0);
    private static readonly DateTime Tuesday0930 = new(2026, 10, 6, 9, 30, 0);

    private sealed class TestableWasteBackgroundService : LeipzigWasteBackgroundService
    {
        private readonly IReadOnlyList<ulong> _guildIds;

        public List<ulong> ChannelRequests { get; } = new();
        public List<(ulong GuildId, string Title)> Posts { get; } = new();
        public HashSet<ulong> FailingGuilds { get; } = new();
        public bool ChannelUnavailable { get; set; }

        public TestableWasteBackgroundService(
            IServiceProvider serviceProvider,
            ILeipzigWasteService wasteService,
            IReadOnlyList<ulong> guildIds)
            : base(
                serviceProvider,
                (DiscordSocketClient)RuntimeHelpers.GetUninitializedObject(typeof(DiscordSocketClient)),
                wasteService,
                new Core.Utilities.FeatureHealthTracker(),
                Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()),
                NullLogger<LeipzigWasteBackgroundService>.Instance)
            => _guildIds = guildIds;

        public Task EvaluateAsync(DateTime now, CancellationToken cancellationToken = default)
            => EvaluateScheduleAsync(now, cancellationToken);

        protected override IReadOnlyList<ulong> GetTargetGuildIds() => _guildIds;

        protected override Task<ITextChannel?> GetOrCreateWasteChannelAsync(ulong guildId)
        {
            ChannelRequests.Add(guildId);

            if (ChannelUnavailable)
            {
                return Task.FromResult<ITextChannel?>(null);
            }

            return Task.FromResult<ITextChannel?>(Mock.Of<ITextChannel>(c => c.Id == guildId));
        }

        protected override Task PostToWasteChannelAsync(ITextChannel channel, Embed embed)
        {
            if (FailingGuilds.Contains(channel.Id))
            {
                throw new InvalidOperationException($"simulated send failure for guild {channel.Id}");
            }

            Posts.Add((channel.Id, embed.Title ?? string.Empty));
            return Task.CompletedTask;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public ServiceProvider Provider { get; }

        public Fixture()
        {
            var dbName = $"waste-bg-tests-{Guid.NewGuid()}";

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: dbName));
            services.AddScoped<IWasteDispatchRepository, WasteDispatchRepository>();
            Provider = services.BuildServiceProvider();
        }

        public async Task<bool> HasSentAsync(string kind, ulong guildId, DateTime date)
        {
            using var scope = Provider.CreateScope();
            return await scope.ServiceProvider
                .GetRequiredService<IWasteDispatchRepository>()
                .HasSentAsync(kind, guildId, date);
        }

        public void Dispose()
        {
            Provider.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    private static ILeipzigWasteService WasteServiceReturning(params string[] collections)
    {
        var mock = new Mock<ILeipzigWasteService>();
        mock.Setup(w => w.GetWasteTypesForDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(new List<string>(collections)));
        return mock.Object;
    }

    [Fact]
    public async Task DailyConfirmation_SendsOnce_AcrossRestart()
    {
        using var fixture = new Fixture();
        var wasteService = WasteServiceReturning("Schwarze Tonne");

        var first = new TestableWasteBackgroundService(fixture.Provider, wasteService, new ulong[] { 100 });
        await first.EvaluateAsync(Monday0830);

        Assert.Single(first.Posts);
        Assert.Equal(DailyTitle, first.Posts[0].Title);

        // Simulates a bot restart: fresh in-memory flags, same persistent database.
        var restarted = new TestableWasteBackgroundService(fixture.Provider, wasteService, new ulong[] { 100 });
        await restarted.EvaluateAsync(Monday0830);

        Assert.Empty(restarted.Posts);
        Assert.True(await fixture.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 100, Monday0830.Date));
    }

    [Fact]
    public async Task DailyConfirmation_SendsAgain_NextDay()
    {
        using var fixture = new Fixture();
        var wasteService = WasteServiceReturning("Schwarze Tonne");

        var monday = new TestableWasteBackgroundService(fixture.Provider, wasteService, new ulong[] { 100 });
        await monday.EvaluateAsync(Monday0830);
        Assert.Single(monday.Posts);

        var tuesday = new TestableWasteBackgroundService(fixture.Provider, wasteService, new ulong[] { 100 });
        await tuesday.EvaluateAsync(Tuesday0830);

        Assert.Single(tuesday.Posts);
        Assert.Equal(DailyTitle, tuesday.Posts[0].Title);
    }

    [Fact]
    public async Task DailyConfirmation_NotSent_Before8am()
    {
        using var fixture = new Fixture();
        var service = new TestableWasteBackgroundService(fixture.Provider, WasteServiceReturning("Schwarze Tonne"), new ulong[] { 100 });

        await service.EvaluateAsync(Tuesday0755);

        Assert.Empty(service.Posts);
    }

    [Fact]
    public async Task DailyConfirmation_NotSent_WhenNoCollections_AndNotMarked()
    {
        using var fixture = new Fixture();
        var service = new TestableWasteBackgroundService(fixture.Provider, WasteServiceReturning(), new ulong[] { 100 });

        await service.EvaluateAsync(Monday0830);

        Assert.Empty(service.Posts);
        Assert.False(await fixture.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 100, Monday0830.Date));
    }

    [Fact]
    public async Task WeeklyOverview_SentOnMondayAfter9_AndNotReSentAfterRestart()
    {
        using var fixture = new Fixture();
        var wasteService = WasteServiceReturning("Schwarze Tonne");

        var first = new TestableWasteBackgroundService(fixture.Provider, wasteService, new ulong[] { 100 });
        await first.EvaluateAsync(Monday0930);

        Assert.Equal(2, first.Posts.Count);
        Assert.Contains(first.Posts, p => p.Title == DailyTitle);
        Assert.Contains(first.Posts, p => p.Title == WeeklyTitle);

        var restarted = new TestableWasteBackgroundService(fixture.Provider, wasteService, new ulong[] { 100 });
        await restarted.EvaluateAsync(Monday1000);

        Assert.Empty(restarted.Posts);
        Assert.True(await fixture.HasSentAsync(WasteDispatchKinds.WeeklyOverview, 100, Monday0930.Date));
    }

    [Fact]
    public async Task DailyAndWeekly_AreIndependentDispatchKinds()
    {
        using var fixture = new Fixture();
        var wasteService = WasteServiceReturning("Schwarze Tonne");

        var morning = new TestableWasteBackgroundService(fixture.Provider, wasteService, new ulong[] { 100 });
        await morning.EvaluateAsync(Monday0830);

        Assert.Single(morning.Posts);
        Assert.True(await fixture.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 100, Monday0830.Date));
        Assert.False(await fixture.HasSentAsync(WasteDispatchKinds.WeeklyOverview, 100, Monday0830.Date));

        var restarted = new TestableWasteBackgroundService(fixture.Provider, wasteService, new ulong[] { 100 });
        await restarted.EvaluateAsync(Monday0930);

        Assert.Single(restarted.Posts);
        Assert.Equal(WeeklyTitle, restarted.Posts[0].Title);
        Assert.True(await fixture.HasSentAsync(WasteDispatchKinds.WeeklyOverview, 100, Monday0930.Date));
    }

    [Fact]
    public async Task WeeklyOverview_NotSent_OnOtherDays()
    {
        using var fixture = new Fixture();
        var service = new TestableWasteBackgroundService(fixture.Provider, WasteServiceReturning("Schwarze Tonne"), new ulong[] { 100 });

        await service.EvaluateAsync(Tuesday0930);

        Assert.Single(service.Posts);
        Assert.Equal(DailyTitle, service.Posts[0].Title);
        Assert.False(await fixture.HasSentAsync(WasteDispatchKinds.WeeklyOverview, 100, Tuesday0930.Date));
    }

    [Fact]
    public async Task FailedSend_IsRetried_AndNotMarked()
    {
        using var fixture = new Fixture();
        var wasteService = WasteServiceReturning("Schwarze Tonne");

        var failing = new TestableWasteBackgroundService(fixture.Provider, wasteService, new ulong[] { 100 });
        failing.FailingGuilds.Add(100);

        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.EvaluateAsync(Monday0830));

        Assert.Empty(failing.Posts);
        Assert.False(await fixture.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 100, Monday0830.Date));

        var retry = new TestableWasteBackgroundService(fixture.Provider, wasteService, new ulong[] { 100 });
        await retry.EvaluateAsync(Monday0830);

        Assert.Single(retry.Posts);
        Assert.True(await fixture.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 100, Monday0830.Date));

        var later = new TestableWasteBackgroundService(fixture.Provider, wasteService, new ulong[] { 100 });
        await later.EvaluateAsync(Monday0830);

        Assert.Empty(later.Posts);
    }

    [Fact]
    public async Task PartialFailure_MarksOnlySuccessfulGuilds()
    {
        using var fixture = new Fixture();
        var wasteService = WasteServiceReturning("Schwarze Tonne");

        var partial = new TestableWasteBackgroundService(fixture.Provider, wasteService, new ulong[] { 1, 2 });
        partial.FailingGuilds.Add(2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => partial.EvaluateAsync(Monday0830));

        Assert.Single(partial.Posts);
        Assert.Equal(1ul, partial.Posts[0].GuildId);
        Assert.True(await fixture.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 1, Monday0830.Date));
        Assert.False(await fixture.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 2, Monday0830.Date));

        var retry = new TestableWasteBackgroundService(fixture.Provider, wasteService, new ulong[] { 1, 2 });
        await retry.EvaluateAsync(Monday0830);

        Assert.Single(retry.Posts);
        Assert.Equal(2ul, retry.Posts[0].GuildId);
        Assert.True(await fixture.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 2, Monday0830.Date));
    }

    [Fact]
    public async Task MissingChannel_SkipsGuild_WithoutMarking()
    {
        using var fixture = new Fixture();
        var service = new TestableWasteBackgroundService(fixture.Provider, WasteServiceReturning("Schwarze Tonne"), new ulong[] { 100 })
        {
            ChannelUnavailable = true
        };

        await service.EvaluateAsync(Monday0830);

        Assert.Empty(service.Posts);
        Assert.False(await fixture.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 100, Monday0830.Date));
    }
}

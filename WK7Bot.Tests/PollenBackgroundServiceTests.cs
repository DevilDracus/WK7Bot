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
using WK7Bot.Models;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class PollenBackgroundServiceTests
{
    private static readonly DateTime Day0 = new(2026, 10, 5, 10, 0, 0);
    private static readonly DateTime Day1 = new(2026, 10, 6, 10, 0, 0);
    private static readonly DateTime Day3 = new(2026, 10, 8, 10, 0, 0);

    private sealed class TestablePollenBackgroundService : PollenBackgroundService
    {
        private readonly IReadOnlyList<ulong> _guildIds;

        public List<ulong> ChannelRequests { get; } = new();
        public List<(ulong GuildId, string Title)> Posts { get; } = new();
        public bool ChannelUnavailable { get; set; }

        public TestablePollenBackgroundService(
            IServiceScopeFactory scopeFactory,
            IPollenService pollenService,
            IReadOnlyList<ulong> guildIds,
            Wk7BotOptions? options = null,
            TimeProvider? timeProvider = null)
            : base(
                scopeFactory,
                DiscordTestDoubles.CreateClient(),
                pollenService,
                new Core.Utilities.FeatureHealthTracker(),
                Microsoft.Extensions.Options.Options.Create(options ?? new Wk7BotOptions
                {
                    PollenPostTime = "00:00",
                    Servers = new ServersOptions { Wk7ServerId = "100" }
                }),
                NullLogger<PollenBackgroundService>.Instance,
                timeProvider)
            => _guildIds = guildIds;

        public Task EvaluateAsync(DateTime now, CancellationToken cancellationToken = default)
            => EvaluateScheduleAsync(now, cancellationToken);

        protected override IReadOnlyList<ulong> GetTargetGuildIds() => _guildIds;

        protected override Task<ITextChannel?> GetOrCreatePollenChannelAsync(ulong guildId)
        {
            ChannelRequests.Add(guildId);

            if (ChannelUnavailable)
            {
                return Task.FromResult<ITextChannel?>(null);
            }

            return Task.FromResult<ITextChannel?>(Mock.Of<ITextChannel>(c => c.Id == guildId));
        }

        protected override Task PostToPollenChannelAsync(ITextChannel channel, Embed embed, CancellationToken cancellationToken)
        {
            Posts.Add((channel.Id, embed.Title ?? string.Empty));
            return Task.CompletedTask;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public ServiceProvider Provider { get; }
        public IServiceScopeFactory ScopeFactory => new ServiceProviderScopeFactory(Provider);

        public Fixture()
        {
            var dbName = $"pollen-bg-tests-{Guid.NewGuid()}";

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: dbName)
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
            services.AddDbContextFactory<BotDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: dbName)
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
            services.AddScoped<IPollenDispatchRepository, PollenDispatchRepository>();
            Provider = services.BuildServiceProvider();
        }

        public async Task<PollenDispatchState?> GetStateAsync(ulong guildId)
        {
            using var scope = Provider.CreateScope();
            return await scope.ServiceProvider
                .GetRequiredService<IPollenDispatchRepository>()
                .GetLastAsync(guildId);
        }

        public void Dispose()
        {
            Provider.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    private static IPollenService PollenServiceReturning(PollenForecast pollen)
    {
        var mock = new Mock<IPollenService>();
        mock.Setup(s => s.GetPollenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(pollen);
        mock.Setup(s => s.GetUvAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((UvForecast?)null);
        return mock.Object;
    }

    private static PollenForecast MakeForecast(string rawToday = "1", string rawTomorrow = "2", string rawDayAfter = "0")
    {
        return new PollenForecast(
            "Sachsen",
            "Tiefland Sachsen",
            null,
            new List<PollenTypeForecast>
            {
                new("Birke", new PollenLevel(rawToday, 1, "desc"), new PollenLevel(rawTomorrow, 2, "desc"), new PollenLevel(rawDayAfter, 0, "desc"))
            });
    }

    [Fact]
    public async Task FirstPost_PostsAndPersistsState()
    {
        using var fixture = new Fixture();
        var pollen = MakeForecast();
        var service = new TestablePollenBackgroundService(fixture.ScopeFactory, PollenServiceReturning(pollen), new ulong[] { 100 });

        await service.EvaluateAsync(Day0);

        Assert.Single(service.Posts);
        var state = await fixture.GetStateAsync(100);
        Assert.NotNull(state);
        Assert.Equal(Day0.Date, state!.LastPostedDate);
    }

    [Fact]
    public async Task Within3DaysUnchanged_Skips()
    {
        using var fixture = new Fixture();
        var pollen = MakeForecast();
        var service = new TestablePollenBackgroundService(fixture.ScopeFactory, PollenServiceReturning(pollen), new ulong[] { 100 });

        await service.EvaluateAsync(Day0);
        Assert.Single(service.Posts);

        var restarted = new TestablePollenBackgroundService(fixture.ScopeFactory, PollenServiceReturning(MakeForecast("2", "0", "0")), new ulong[] { 100 });
        await restarted.EvaluateAsync(Day1);

        Assert.Empty(restarted.Posts);
    }

    [Fact]
    public async Task Within3DaysChanged_Posts()
    {
        using var fixture = new Fixture();
        var firstPollen = MakeForecast("1", "2", "0");
        var service = new TestablePollenBackgroundService(fixture.ScopeFactory, PollenServiceReturning(firstPollen), new ulong[] { 100 });
        await service.EvaluateAsync(Day0);
        Assert.Single(service.Posts);

        var changedPollen = MakeForecast("2", "2", "0");
        var restarted = new TestablePollenBackgroundService(fixture.ScopeFactory, PollenServiceReturning(changedPollen), new ulong[] { 100 });
        await restarted.EvaluateAsync(Day1);

        Assert.Single(restarted.Posts);
    }

    [Fact]
    public async Task ThreeDaysOrMore_Posts()
    {
        using var fixture = new Fixture();
        var pollen = MakeForecast();
        var service = new TestablePollenBackgroundService(fixture.ScopeFactory, PollenServiceReturning(pollen), new ulong[] { 100 });
        await service.EvaluateAsync(Day0);
        Assert.Single(service.Posts);

        var restarted = new TestablePollenBackgroundService(fixture.ScopeFactory, PollenServiceReturning(pollen), new ulong[] { 100 });
        await restarted.EvaluateAsync(Day3);

        Assert.Single(restarted.Posts);
    }

    [Fact]
    public async Task RestartSameDay_DoesNotRepost()
    {
        using var fixture = new Fixture();
        var pollen = MakeForecast();
        var service = new TestablePollenBackgroundService(fixture.ScopeFactory, PollenServiceReturning(pollen), new ulong[] { 100 });
        await service.EvaluateAsync(Day0);
        Assert.Single(service.Posts);

        var restarted = new TestablePollenBackgroundService(fixture.ScopeFactory, PollenServiceReturning(pollen), new ulong[] { 100 });
        await restarted.EvaluateAsync(Day0);

        Assert.Empty(restarted.Posts);
    }

    [Fact]
    public async Task MissingChannel_SkipsWithoutPersisting()
    {
        using var fixture = new Fixture();
        var pollen = MakeForecast();
        var service = new TestablePollenBackgroundService(fixture.ScopeFactory, PollenServiceReturning(pollen), new ulong[] { 100 })
        {
            ChannelUnavailable = true
        };

        await service.EvaluateAsync(Day0);

        Assert.Empty(service.Posts);
        var state = await fixture.GetStateAsync(100);
        Assert.Null(state);
    }

    [Fact]
    public async Task FeedsUnavailable_Throws()
    {
        using var fixture = new Fixture();
        var mock = new Mock<IPollenService>();
        mock.Setup(s => s.GetPollenAsync(It.IsAny<CancellationToken>())).ReturnsAsync((PollenForecast?)null);
        mock.Setup(s => s.GetUvAsync(It.IsAny<CancellationToken>())).ReturnsAsync((UvForecast?)null);
        var service = new TestablePollenBackgroundService(fixture.ScopeFactory, mock.Object, new ulong[] { 100 });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EvaluateAsync(Day0));
    }
}

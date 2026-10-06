using System.Runtime.CompilerServices;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Infrastructure.Data;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class DwdWarningBackgroundServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 13, 0, 0);

    private sealed class TestableWarningBackgroundService : DwdWarningBackgroundService
    {
        private readonly IReadOnlyList<ulong> _guildIds;

        public List<ulong> ChannelRequests { get; } = new();
        public List<(ulong GuildId, string Title)> Posts { get; } = new();
        public HashSet<ulong> FailingGuilds { get; } = new();
        public bool ChannelUnavailable { get; set; }

        public TestableWarningBackgroundService(
            IServiceProvider serviceProvider,
            IDwdWarningService warningSource,
            IReadOnlyList<ulong> guildIds,
            Wk7BotOptions? options = null)
            : base(
                serviceProvider,
                (DiscordSocketClient)RuntimeHelpers.GetUninitializedObject(typeof(DiscordSocketClient)),
                warningSource,
                Microsoft.Extensions.Options.Options.Create(options ?? new Wk7BotOptions()),
                NullLogger<DwdWarningBackgroundService>.Instance)
            => _guildIds = guildIds;

        public Task RunAsync(DateTime now, CancellationToken cancellationToken = default)
            => EvaluateAsync(now, cancellationToken);

        protected override IReadOnlyList<ulong> GetTargetGuildIds() => _guildIds;

        protected override Task<ITextChannel?> GetOrCreateWarningChannelAsync(ulong guildId)
        {
            ChannelRequests.Add(guildId);

            if (ChannelUnavailable)
            {
                return Task.FromResult<ITextChannel?>(null);
            }

            return Task.FromResult<ITextChannel?>(Mock.Of<ITextChannel>(c => c.Id == guildId));
        }

        protected override Task PostToWarningChannelAsync(ITextChannel channel, Embed embed)
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
            var dbName = $"dwd-warning-tests-{Guid.NewGuid()}";

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: dbName));
            services.AddScoped<IWarningDispatchRepository, WarningDispatchRepository>();
            Provider = services.BuildServiceProvider();
        }

        public async Task<bool> HasSentAsync(ulong guildId, string warningId)
        {
            using var scope = Provider.CreateScope();
            return await scope.ServiceProvider
                .GetRequiredService<IWarningDispatchRepository>()
                .HasSentAsync(warningId, guildId);
        }

        public void Dispose()
        {
            Provider.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    private static Wk7BotOptions WarningOptions()
        => new()
        {
            DwdWarning =
            {
                PostalCode = "04205",
                Latitude = 51.34,
                Longitude = 12.36,
                LeadMinutes = 30,
                AreaNames = new List<string> { "Stadt Leipzig" }
            }
        };

    private static IReadOnlyList<GeoPoint> Square(double latitude, double longitude, double halfSize)
        => new List<GeoPoint>
        {
            new(latitude - halfSize, longitude - halfSize),
            new(latitude - halfSize, longitude + halfSize),
            new(latitude + halfSize, longitude + halfSize),
            new(latitude + halfSize, longitude - halfSize),
            new(latitude - halfSize, longitude - halfSize)
        };

    private static CapWarning RainWarning(DateTime now, string identifier = "warn-rain-1")
        => new()
        {
            Identifier = identifier,
            Event = "STARKREGEN",
            EventGroup = "HEAVY RAIN",
            Severity = "Severe",
            Headline = "Amtliche WARNUNG vor STARKREGEN",
            Description = "Es tritt starker Regen auf.",
            Instruction = "Balkonfenster schließen.",
            Onset = now.AddMinutes(20),
            Expires = now.AddHours(3),
            AreaDescriptions = new List<string> { "polygonal event area", "Stadt Leipzig" },
            Polygons = new List<IReadOnlyList<GeoPoint>> { Square(51.34, 12.36, 0.05) },
            ExcludePolygons = new List<IReadOnlyList<GeoPoint>>()
        };

    private static Mock<IDwdWarningService> SourceReturning(params CapWarning[] warnings)
    {
        var mock = new Mock<IDwdWarningService>();
        mock.Setup(s => s.FetchWarningsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(warnings.ToList());
        return mock;
    }

    private static Mock<IDwdWarningService> FailingSource()
    {
        var mock = new Mock<IDwdWarningService>();
        mock.Setup(s => s.FetchWarningsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("simulated feed failure"));
        return mock;
    }

    [Fact]
    public async Task RelevantWarning_IsPosted_OnceAndMarked()
    {
        using var fixture = new Fixture();
        var source = SourceReturning(RainWarning(Now));
        var service = new TestableWarningBackgroundService(fixture.Provider, source.Object, new ulong[] { 100 }, WarningOptions());

        await service.RunAsync(Now);

        var post = Assert.Single(service.Posts);
        Assert.Equal(100ul, post.GuildId);
        Assert.StartsWith("⚠️ STARKREGEN", post.Title);
        Assert.Contains(100ul, service.ChannelRequests);
        Assert.True(await fixture.HasSentAsync(100, "warn-rain-1"));
    }

    [Fact]
    public async Task Warning_BeyondLeadWindow_IsSkipped()
    {
        using var fixture = new Fixture();
        var warning = RainWarning(Now);
        warning.Onset = Now.AddMinutes(60);
        var source = SourceReturning(warning);
        var service = new TestableWarningBackgroundService(fixture.Provider, source.Object, new ulong[] { 100 }, WarningOptions());

        await service.RunAsync(Now);

        Assert.Empty(service.Posts);
        Assert.False(await fixture.HasSentAsync(100, "warn-rain-1"));
    }

    [Fact]
    public async Task Warning_WithOnsetTooFarInThePast_IsSkipped()
    {
        using var fixture = new Fixture();
        var warning = RainWarning(Now);
        warning.Onset = Now.AddMinutes(-10);
        var source = SourceReturning(warning);
        var service = new TestableWarningBackgroundService(fixture.Provider, source.Object, new ulong[] { 100 }, WarningOptions());

        await service.RunAsync(Now);

        Assert.Empty(service.Posts);
        Assert.False(await fixture.HasSentAsync(100, "warn-rain-1"));
    }

    [Fact]
    public async Task Warning_WithOnsetSlightlyInThePast_IsStillPosted_WithinGrace()
    {
        using var fixture = new Fixture();
        var warning = RainWarning(Now);
        warning.Onset = Now.AddMinutes(-3);
        var source = SourceReturning(warning);
        var service = new TestableWarningBackgroundService(fixture.Provider, source.Object, new ulong[] { 100 }, WarningOptions());

        await service.RunAsync(Now);

        Assert.Single(service.Posts);
        Assert.True(await fixture.HasSentAsync(100, "warn-rain-1"));
    }

    [Fact]
    public async Task ExpiredWarning_IsSkipped()
    {
        using var fixture = new Fixture();
        var warning = RainWarning(Now);
        warning.Expires = Now;
        var source = SourceReturning(warning);
        var service = new TestableWarningBackgroundService(fixture.Provider, source.Object, new ulong[] { 100 }, WarningOptions());

        await service.RunAsync(Now);

        Assert.Empty(service.Posts);
        Assert.False(await fixture.HasSentAsync(100, "warn-rain-1"));
    }

    [Fact]
    public async Task Warning_WithUnrelatedEvent_IsSkipped()
    {
        using var fixture = new Fixture();
        var warning = RainWarning(Now);
        warning.Event = "NEBEL";
        warning.EventGroup = "FOG";
        var source = SourceReturning(warning);
        var service = new TestableWarningBackgroundService(fixture.Provider, source.Object, new ulong[] { 100 }, WarningOptions());

        await service.RunAsync(Now);

        Assert.Empty(service.Posts);
        Assert.False(await fixture.HasSentAsync(100, "warn-rain-1"));
    }

    [Fact]
    public async Task Warning_WhosePolygonMissesTheLocation_IsSkipped()
    {
        using var fixture = new Fixture();
        var warning = RainWarning(Now);
        warning.Polygons = new List<IReadOnlyList<GeoPoint>> { Square(50.0, 8.0, 0.05) };
        var source = SourceReturning(warning);
        var service = new TestableWarningBackgroundService(fixture.Provider, source.Object, new ulong[] { 100 }, WarningOptions());

        await service.RunAsync(Now);

        Assert.Empty(service.Posts);
        Assert.False(await fixture.HasSentAsync(100, "warn-rain-1"));
    }

    [Fact]
    public async Task PostedWarning_IsNotRepeated_AcrossRestart()
    {
        using var fixture = new Fixture();
        var source = SourceReturning(RainWarning(Now));

        var first = new TestableWarningBackgroundService(fixture.Provider, source.Object, new ulong[] { 100 }, WarningOptions());
        await first.RunAsync(Now);
        Assert.Single(first.Posts);

        // Simulates a bot restart: fresh in-memory state, same persistent database.
        var restarted = new TestableWarningBackgroundService(fixture.Provider, source.Object, new ulong[] { 100 }, WarningOptions());
        await restarted.RunAsync(Now);

        Assert.Empty(restarted.Posts);
        Assert.True(await fixture.HasSentAsync(100, "warn-rain-1"));
        source.Verify(s => s.FetchWarningsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SendFailure_IsRetriedOnNextPoll_AndNotMarked()
    {
        using var fixture = new Fixture();
        var source = SourceReturning(RainWarning(Now));
        var service = new TestableWarningBackgroundService(fixture.Provider, source.Object, new ulong[] { 100 }, WarningOptions())
        {
            FailingGuilds = { 100 }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(Now));
        Assert.Empty(service.Posts);
        Assert.False(await fixture.HasSentAsync(100, "warn-rain-1"));

        service.FailingGuilds.Remove(100);
        await service.RunAsync(Now.AddMinutes(2));

        Assert.Single(service.Posts);
        Assert.True(await fixture.HasSentAsync(100, "warn-rain-1"));
    }

    [Fact]
    public async Task MissingChannel_SkipsGuild_WithoutMarking_AndRetriesLater()
    {
        using var fixture = new Fixture();
        var source = SourceReturning(RainWarning(Now));
        var service = new TestableWarningBackgroundService(fixture.Provider, source.Object, new ulong[] { 100 }, WarningOptions())
        {
            ChannelUnavailable = true
        };

        await service.RunAsync(Now);

        Assert.Empty(service.Posts);
        Assert.False(await fixture.HasSentAsync(100, "warn-rain-1"));

        service.ChannelUnavailable = false;
        await service.RunAsync(Now.AddMinutes(2));

        Assert.Single(service.Posts);
        Assert.True(await fixture.HasSentAsync(100, "warn-rain-1"));
    }

    [Fact]
    public async Task EveryGuild_ReceivesTheWarning_WithIndependentDedupState()
    {
        using var fixture = new Fixture();
        var source = SourceReturning(RainWarning(Now));
        var service = new TestableWarningBackgroundService(fixture.Provider, source.Object, new ulong[] { 1, 2 }, WarningOptions());

        await service.RunAsync(Now);

        Assert.Equal(2, service.Posts.Count);
        Assert.Equal(new ulong[] { 1, 2 }, service.Posts.Select(p => p.GuildId).OrderBy(id => id));
        Assert.True(await fixture.HasSentAsync(1, "warn-rain-1"));
        Assert.True(await fixture.HasSentAsync(2, "warn-rain-1"));
    }

    [Fact]
    public async Task MultipleRelevantWarnings_ArePostedIndependently()
    {
        using var fixture = new Fixture();
        var source = SourceReturning(RainWarning(Now, "warn-rain-1"), RainWarning(Now, "warn-rain-2"));
        var service = new TestableWarningBackgroundService(fixture.Provider, source.Object, new ulong[] { 100 }, WarningOptions());

        await service.RunAsync(Now);

        Assert.Equal(2, service.Posts.Count);
        Assert.True(await fixture.HasSentAsync(100, "warn-rain-1"));
        Assert.True(await fixture.HasSentAsync(100, "warn-rain-2"));
    }

    [Fact]
    public async Task FailedFeedFetch_Propagates_AndDoesNotMarkAnything()
    {
        using var fixture = new Fixture();
        var failing = FailingSource();
        var service = new TestableWarningBackgroundService(fixture.Provider, failing.Object, new ulong[] { 100 }, WarningOptions());

        await Assert.ThrowsAsync<HttpRequestException>(() => service.RunAsync(Now));

        Assert.Empty(service.Posts);
        Assert.False(await fixture.HasSentAsync(100, "warn-rain-1"));
    }

    [Fact]
    public async Task LocationWithoutCoordinates_FallsBackToAreaNames()
    {
        using var fixture = new Fixture();
        var options = WarningOptions();
        options.DwdWarning.Latitude = 0;
        options.DwdWarning.Longitude = 0;

        var warning = RainWarning(Now);
        warning.Polygons = new List<IReadOnlyList<GeoPoint>>();
        var source = SourceReturning(warning);
        var service = new TestableWarningBackgroundService(fixture.Provider, source.Object, new ulong[] { 100 }, options);

        await service.RunAsync(Now);

        Assert.Single(service.Posts);
        Assert.True(await fixture.HasSentAsync(100, "warn-rain-1"));
    }
}

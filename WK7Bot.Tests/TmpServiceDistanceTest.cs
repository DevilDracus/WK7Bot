using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Runtime.CompilerServices;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class TmpServiceDistanceTest
{
    private sealed class Testable : DwdWarningBackgroundService
    {
        private readonly IReadOnlyList<ulong> _guildIds;
        public List<(ulong, string)> Posts { get; } = new();
        public Testable(IServiceScopeFactory sf, IDwdWarningService src, IReadOnlyList<ulong> guilds, Wk7BotOptions? opts = null)
            : base(sf, (DiscordSocketClient)RuntimeHelpers.GetUninitializedObject(typeof(DiscordSocketClient)), src, new Core.Utilities.FeatureHealthTracker(), Microsoft.Extensions.Options.Options.Create(opts ?? new Wk7BotOptions()), NullLogger<DwdWarningBackgroundService>.Instance)
            => _guildIds = guilds;
        public Task RunAsync(DateTime now, CancellationToken ct = default) => EvaluateAsync(now, ct);
        protected override IReadOnlyList<ulong> GetTargetGuildIds() => _guildIds;
        protected override Task<ITextChannel?> GetOrCreateWarningChannelAsync(ulong guildId) => Task.FromResult<ITextChannel?>(Mock.Of<ITextChannel>(c => c.Id == guildId));
        protected override Task PostToWarningChannelAsync(ITextChannel channel, Embed embed) { Posts.Add((channel.Id, embed.Title ?? "")); return Task.CompletedTask; }
    }

    private static Wk7BotOptions Opts() => new() { DwdWarning = new DwdWarningOptions { PostalCode="04205", Latitude=51.34, Longitude=12.36, LeadMinutes=30, AreaNames=new List<string>{"Stadt Leipzig"} } };

    [Fact]
    public async Task FarCentroid_IsSkipped()
    {
        using var dbName = new { }; // placeholder
        // Build fixture manually
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<WK7Bot.Infrastructure.Data.BotDbContext>(o => o.UseInMemoryDatabase("tmpsvc"));
        services.AddDbContextFactory<WK7Bot.Infrastructure.Data.BotDbContext>(o => o.UseInMemoryDatabase("tmpsvc"));
        services.AddScoped<WK7Bot.Core.Interfaces.IWarningDispatchRepository, WK7Bot.Infrastructure.Data.WarningDispatchRepository>();
        using var provider = services.BuildServiceProvider();
        var sf = new ServiceProviderScopeFactory(provider);

        var pts = new List<GeoPoint> { new(49.0,11.0), new(49.0,13.0), new(52.0,13.0), new(52.0,11.0), new(49.0,11.0) };
        var warning = new CapWarning { Identifier="w1", Event="STARKREGEN", EventGroup="HEAVY RAIN", Severity="Severe", Headline="h", Onset=DateTime.UtcNow.AddMinutes(20), Expires=DateTime.UtcNow.AddHours(3), AreaDescriptions=new List<string>{"polygonal event area"}, Polygons=new List<IReadOnlyList<GeoPoint>>{pts} };
        var mock = new Mock<IDwdWarningService>();
        mock.Setup(s => s.FetchWarningsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CapWarning>{warning});
        var svc = new Testable(sf, mock.Object, new ulong[]{100}, Opts());
        await svc.RunAsync(DateTime.UtcNow);
        Assert.Empty(svc.Posts);
    }
}

using System.Runtime.CompilerServices;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using WK7Bot.Options;
using WK7Bot.Services;
using Xunit;

namespace WK7Bot.Tests;

public class SpontanTreffExpiryServiceTests
{
    private sealed class TestableExpiryService : SpontanTreffExpiryService
    {
        public List<(Embed Embed, MessageComponent Components)> Edits { get; } = new();
        public bool FailEdits { get; set; }

        public TestableExpiryService(IServiceProvider serviceProvider, bool enabled = true)
            : base(
                serviceProvider,
                (DiscordSocketClient)RuntimeHelpers.GetUninitializedObject(typeof(DiscordSocketClient)),
                new Core.Utilities.FeatureHealthTracker(),
                Microsoft.Extensions.Options.Options.Create(
                    new Wk7BotOptions { Features = new FeatureOptions { SpontanTreffEnabled = enabled } }),
                NullLogger<SpontanTreffExpiryService>.Instance)
        {
        }

        public Task EvaluateAsync(DateTime now, CancellationToken cancellationToken = default)
            => SweepAsync(now, cancellationToken);

        protected override Task EditMeetupMessageAsync(SpontanTreff meetup, Embed embed, MessageComponent components)
        {
            if (FailEdits)
            {
                throw new InvalidOperationException("simulated Discord outage");
            }

            Edits.Add((embed, components));
            return Task.CompletedTask;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public ServiceProvider Provider { get; }

        public Fixture()
        {
            // The database name must be captured once: a Guid inside the AddDbContext lambda would create a fresh,
            // empty in-memory database for every scope.
            var databaseName = $"spontan-treff-expiry-tests-{Guid.NewGuid()}";

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: databaseName));
            services.AddScoped<ISpontanTreffRepository, SpontanTreffRepository>();
            Provider = services.BuildServiceProvider();
        }

        public async Task<SpontanTreff> CreateMeetupAsync(DateTime expiresAt, bool withResponse = false)
        {
            using var scope = Provider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ISpontanTreffRepository>();

            var meetup = await repository.AddAsync(new SpontanTreff
            {
                GuildId = 1,
                ChannelId = 777,
                MessageId = 888,
                OrganizerId = 42,
                OrganizerName = "Tester",
                Plan = "Kaffee?",
                CreatedAt = DateTime.Now,
                ExpiresAt = expiresAt
            });

            if (withResponse)
            {
                await repository.SetResponseAsync(new SpontanTreffResponse
                {
                    MeetupId = meetup.Id,
                    UserId = 99,
                    Going = true,
                    RespondedAt = DateTime.Now
                });
            }

            return meetup;
        }

        public async Task<SpontanTreff?> GetMeetupAsync(int meetupId)
        {
            using var scope = Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ISpontanTreffRepository>().GetAsync(meetupId);
        }

        public async Task<IReadOnlyList<SpontanTreffResponse>> GetResponsesAsync(int meetupId)
        {
            using var scope = Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ISpontanTreffRepository>().GetResponsesAsync(meetupId);
        }

        public async Task<bool> HasExpiredMeetupsAsync(DateTime now)
        {
            using var scope = Provider.CreateScope();
            var expired = await scope.ServiceProvider
                .GetRequiredService<ISpontanTreffRepository>()
                .GetExpiredAsync(now);

            return expired.Count > 0;
        }

        public void Dispose()
        {
            Provider.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    private static List<ButtonComponent> Buttons(MessageComponent component)
        => component.Components
            .SelectMany(row => row.Components)
            .OfType<ButtonComponent>()
            .ToList();

    [Fact]
    public async Task Sweep_ClosesExpiredMeetup_AndRemovesItsButtons()
    {
        using var fixture = new Fixture();
        var meetup = await fixture.CreateMeetupAsync(DateTime.Now.AddMinutes(-1), withResponse: true);
        var service = new TestableExpiryService(fixture.Provider);

        await service.EvaluateAsync(DateTime.Now);

        Assert.True((await fixture.GetMeetupAsync(meetup.Id))!.Closed);

        var edit = Assert.Single(service.Edits);
        Assert.Empty(Buttons(edit.Components));
        Assert.Equal("⚡ Spontan-Treff · vorbei", edit.Embed.Title);

        var responseField = Assert.Single(edit.Embed.Fields, f => f.Name.StartsWith("✅"));
        Assert.Contains("<@99>", responseField.Value);
    }

    [Fact]
    public async Task Sweep_LeavesActiveMeetupsUntouched()
    {
        using var fixture = new Fixture();
        var meetup = await fixture.CreateMeetupAsync(DateTime.Now.AddMinutes(15));
        var service = new TestableExpiryService(fixture.Provider);

        await service.EvaluateAsync(DateTime.Now);

        Assert.False((await fixture.GetMeetupAsync(meetup.Id))!.Closed);
        Assert.Empty(service.Edits);
    }

    [Fact]
    public async Task Sweep_ClosesEveryExpiredMeetup_EvenWhenEditFails()
    {
        using var fixture = new Fixture();
        var first = await fixture.CreateMeetupAsync(DateTime.Now.AddMinutes(-5));
        var second = await fixture.CreateMeetupAsync(DateTime.Now.AddMinutes(-2));
        var service = new TestableExpiryService(fixture.Provider) { FailEdits = true };

        await service.EvaluateAsync(DateTime.Now);

        Assert.True((await fixture.GetMeetupAsync(first.Id))!.Closed);
        Assert.True((await fixture.GetMeetupAsync(second.Id))!.Closed);
        Assert.Empty(service.Edits);
        Assert.False(await fixture.HasExpiredMeetupsAsync(DateTime.Now));
    }

    [Fact]
    public async Task Sweep_IsIdempotent_ASecondRunDoesNotReEdit()
    {
        using var fixture = new Fixture();
        await fixture.CreateMeetupAsync(DateTime.Now.AddMinutes(-1));
        var service = new TestableExpiryService(fixture.Provider);

        await service.EvaluateAsync(DateTime.Now);
        await service.EvaluateAsync(DateTime.Now);

        Assert.Single(service.Edits);
    }

    [Fact]
    public async Task Sweep_WithNothingDue_DoesNotEditMessages()
    {
        using var fixture = new Fixture();
        var service = new TestableExpiryService(fixture.Provider);

        await service.EvaluateAsync(DateTime.Now);

        Assert.Empty(service.Edits);
    }
}

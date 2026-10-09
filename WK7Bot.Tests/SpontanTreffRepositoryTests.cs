using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

public class SpontanTreffRepositoryTests : IDisposable
{
    private readonly BotDbContext _context;
    private readonly ISpontanTreffRepository _repository;

    public SpontanTreffRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(databaseName: $"spontan-treff-tests-{Guid.NewGuid()}")
            .Options;

        _context = new BotDbContext(options);
        _repository = new SpontanTreffRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private static SpontanTreff NewMeetup(DateTime? expiresAt = null) => new()
    {
        GuildId = 1,
        ChannelId = 2,
        OrganizerId = 42,
        OrganizerName = "Tester",
        Plan = "Abendspaziergang",
        Location = "Park",
        CreatedAt = new DateTime(2026, 10, 6, 18, 0, 0),
        ExpiresAt = expiresAt ?? new DateTime(2026, 10, 6, 18, 30, 0)
    };

    [Fact]
    public async Task AddAsync_AssignsId_AndRoundTrips()
    {
        var meetup = await _repository.AddAsync(NewMeetup());

        Assert.True(meetup.Id > 0);

        var loaded = await _repository.GetAsync(meetup.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Abendspaziergang", loaded!.Plan);
        Assert.Equal("Park", loaded.Location);
        Assert.Equal(42ul, loaded.OrganizerId);
        Assert.False(loaded.Closed);
    }

    [Fact]
    public async Task SetMessageAsync_StoresChannelAndMessageLocation()
    {
        var meetup = await _repository.AddAsync(NewMeetup());

        await _repository.SetMessageAsync(meetup.Id, 55, 66);

        var loaded = await _repository.GetAsync(meetup.Id);
        Assert.Equal(55ul, loaded!.ChannelId);
        Assert.Equal(66ul, loaded.MessageId);
    }

    [Fact]
    public async Task SetResponseAsync_InsertsFirstThenUpdatesSameUser()
    {
        var meetup = await _repository.AddAsync(NewMeetup());

        await _repository.SetResponseAsync(new SpontanTreffResponse
        {
            MeetupId = meetup.Id,
            UserId = 11,
            Going = true,
            RespondedAt = new DateTime(2026, 10, 6, 18, 5, 0)
        });

        await _repository.SetResponseAsync(new SpontanTreffResponse
        {
            MeetupId = meetup.Id,
            UserId = 11,
            Going = false,
            RespondedAt = new DateTime(2026, 10, 6, 18, 6, 0)
        });

        var responses = await _repository.GetResponsesAsync(meetup.Id);
        var single = Assert.Single(responses);
        Assert.False(single.Going);
        Assert.Equal(new DateTime(2026, 10, 6, 18, 6, 0), single.RespondedAt);
    }

    [Fact]
    public async Task RemoveResponseAsync_DeletesOnlyThatUser()
    {
        var meetup = await _repository.AddAsync(NewMeetup());
        await _repository.SetResponseAsync(new SpontanTreffResponse { MeetupId = meetup.Id, UserId = 11, Going = true, RespondedAt = DateTime.Now });
        await _repository.SetResponseAsync(new SpontanTreffResponse { MeetupId = meetup.Id, UserId = 12, Going = false, RespondedAt = DateTime.Now });

        await _repository.RemoveResponseAsync(meetup.Id, 11);

        var responses = await _repository.GetResponsesAsync(meetup.Id);
        var remaining = Assert.Single(responses);
        Assert.Equal(12ul, remaining.UserId);
        Assert.Null(await _repository.GetResponseAsync(meetup.Id, 11));
    }

    [Fact]
    public async Task RemoveResponseAsync_UnknownUser_IsNoOp()
    {
        var meetup = await _repository.AddAsync(NewMeetup());

        await _repository.RemoveResponseAsync(meetup.Id, 999);

        Assert.Empty(await _repository.GetResponsesAsync(meetup.Id));
    }

    [Fact]
    public async Task GetResponsesAsync_ReturnsAnswersOrderedByResponseTime()
    {
        var meetup = await _repository.AddAsync(NewMeetup());
        await _repository.SetResponseAsync(new SpontanTreffResponse { MeetupId = meetup.Id, UserId = 11, Going = true, RespondedAt = new DateTime(2026, 10, 6, 18, 10, 0) });
        await _repository.SetResponseAsync(new SpontanTreffResponse { MeetupId = meetup.Id, UserId = 12, Going = true, RespondedAt = new DateTime(2026, 10, 6, 18, 3, 0) });

        var responses = await _repository.GetResponsesAsync(meetup.Id);

        Assert.Equal(new ulong[] { 12, 11 }, responses.Select(r => r.UserId).ToArray());
    }

    [Fact]
    public async Task GetExpiredAsync_OnlyReturnsPendingMeetupsPastTheirDeadline()
    {
        var expired = await _repository.AddAsync(NewMeetup(new DateTime(2026, 10, 6, 18, 0, 0)));
        var pending = await _repository.AddAsync(NewMeetup(new DateTime(2026, 10, 6, 19, 0, 0)));
        await _repository.AddAsync(new SpontanTreff
        {
            GuildId = 1,
            OrganizerId = 42,
            OrganizerName = "Tester",
            Plan = "Bereits abgeschlossen",
            CreatedAt = new DateTime(2026, 10, 6, 17, 0, 0),
            ExpiresAt = new DateTime(2026, 10, 6, 17, 30, 0),
            Closed = true
        });

        var now = new DateTime(2026, 10, 6, 18, 15, 0);
        var result = await _repository.GetExpiredAsync(now);

        var single = Assert.Single(result);
        Assert.Equal(expired.Id, single.Id);
        Assert.DoesNotContain(result, m => m.Id == pending.Id);
    }

    [Fact]
    public async Task CloseAsync_MarksMeetupAndRemovesItFromExpirySweep()
    {
        var meetup = await _repository.AddAsync(NewMeetup(new DateTime(2026, 10, 6, 18, 0, 0)));
        var now = new DateTime(2026, 10, 6, 18, 15, 0);

        await _repository.CloseAsync(meetup.Id);
        await _repository.CloseAsync(meetup.Id);

        Assert.True((await _repository.GetAsync(meetup.Id))!.Closed);
        Assert.Empty(await _repository.GetExpiredAsync(now));
    }

    [Fact]
    public async Task SetMessageAsync_UnknownMeetup_IsNoOp()
    {
        await _repository.SetMessageAsync(4711, 55, 66);

        Assert.Null(await _repository.GetAsync(4711));
    }

    [Fact]
    public async Task SqliteStartupSequence_EnsureCreatedPlusRawSql_RoundTrips()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        await using var context = new BotDbContext(options);
        await context.Database.OpenConnectionAsync();

        // Mirrors the startup sequence in Program.cs for a brand-new database.
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateSpontanTreffSql);

        await AssertRoundTripAsync(context);
    }

    [Fact]
    public async Task SqliteMigrationSequence_RawSql_CreatesMissingTableIdempotently_And_RoundTrips()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        await using var context = new BotDbContext(options);
        await context.Database.OpenConnectionAsync();

        // An existing database predating the feature only receives the raw SQL (EnsureCreated is a no-op for
        // existing files) — it must create both tables and be re-runnable.
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateSpontanTreffSql);
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateSpontanTreffSql);

        await AssertRoundTripAsync(context);
    }

    [Fact]
    public async Task RemoveAsync_DeletesMeetupAndAnswers_AndIgnoresUnknownId()
    {
        var meetup = await _repository.AddAsync(NewMeetup());
        await _repository.SetResponseAsync(new SpontanTreffResponse
        {
            MeetupId = meetup.Id,
            UserId = 42,
            Going = true,
            RespondedAt = new DateTime(2026, 10, 6, 18, 5, 0)
        });

        await _repository.RemoveAsync(meetup.Id);

        Assert.Null(await _repository.GetAsync(meetup.Id));
        Assert.Empty(await _repository.GetResponsesAsync(meetup.Id));

        // An unknown identifier must stay a no-op.
        await _repository.RemoveAsync(meetup.Id + 999);
    }

    private static async Task AssertRoundTripAsync(BotDbContext context)
    {
        var repository = new SpontanTreffRepository(context);
        var expiresAt = new DateTime(2026, 10, 6, 18, 30, 0);

        var meetup = await repository.AddAsync(new SpontanTreff
        {
            GuildId = 1,
            ChannelId = 2,
            OrganizerId = 42,
            OrganizerName = "Tester",
            Plan = "Abendspaziergang",
            Location = "Park",
            CreatedAt = new DateTime(2026, 10, 6, 18, 0, 0),
            ExpiresAt = expiresAt
        });

        Assert.True(meetup.Id > 0);

        await repository.SetMessageAsync(meetup.Id, 55, 66);
        await repository.SetResponseAsync(new SpontanTreffResponse
        {
            MeetupId = meetup.Id,
            UserId = 11,
            Going = true,
            RespondedAt = new DateTime(2026, 10, 6, 18, 5, 0)
        });

        var loaded = await repository.GetAsync(meetup.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Abendspaziergang", loaded!.Plan);
        Assert.Equal(55ul, loaded.ChannelId);
        Assert.Equal(66ul, loaded.MessageId);
        Assert.Equal(expiresAt, loaded.ExpiresAt);

        var response = Assert.Single(await repository.GetResponsesAsync(meetup.Id));
        Assert.True(response.Going);
        Assert.Equal(new DateTime(2026, 10, 6, 18, 5, 0), response.RespondedAt);

        var afterDeadline = expiresAt.AddMinutes(1);
        Assert.Single(await repository.GetExpiredAsync(afterDeadline));

        await repository.CloseAsync(meetup.Id);
        Assert.Empty(await repository.GetExpiredAsync(afterDeadline));
    }
}

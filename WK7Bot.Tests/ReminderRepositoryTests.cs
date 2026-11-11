using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

public class ReminderRepositoryTests : IDisposable
{
    private readonly IDbContextFactory<BotDbContext> _factory;
    private readonly IReminderRepository _repository;

    public ReminderRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(databaseName: $"reminder-tests-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _factory = new PooledDbContextFactory<BotDbContext>(options);
        _repository = new ReminderRepository(_factory);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private static Reminder NewReminder(
        ulong userId = 1,
        DateTime? dueAt = null,
        string text = "Test reminder",
        DateTime? deliveredAt = null) => new()
    {
        DiscordUserId = userId,
        DueAt = dueAt ?? DateTime.UtcNow.AddMinutes(-5),
        Text = text,
        DeliveredAt = deliveredAt
    };

    private async Task<Reminder> AddReminderAsync(Reminder reminder)
    {
        await _repository.AddAsync(reminder);
        return reminder;
    }

    [Fact]
    public async Task AddAsync_AssignsId_AndRoundTrips()
    {
        var reminder = await AddReminderAsync(NewReminder());

        Assert.True(reminder.Id > 0);

        var due = await _repository.GetDueAsync(DateTime.UtcNow);
        Assert.Contains(due, r => r.Id == reminder.Id);
    }

    [Fact]
    public async Task GetDueAsync_ReturnsOnlyUndeliveredPastDueReminders()
    {
        var now = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        var due = await AddReminderAsync(NewReminder(dueAt: now.AddMinutes(-5)));
        await AddReminderAsync(NewReminder(dueAt: now.AddMinutes(5)));
        await AddReminderAsync(NewReminder(dueAt: now.AddMinutes(-10), deliveredAt: now.AddMinutes(-1)));

        var result = await _repository.GetDueAsync(now);

        var single = Assert.Single(result);
        Assert.Equal(due.Id, single.Id);
    }

    [Fact]
    public async Task GetPendingForUserAsync_ReturnsOnlyUndeliveredRemindersForThatUser()
    {
        var userReminder = await AddReminderAsync(NewReminder(userId: 42));
        await AddReminderAsync(NewReminder(userId: 99));
        await AddReminderAsync(NewReminder(userId: 42, deliveredAt: DateTime.UtcNow));

        var result = await _repository.GetPendingForUserAsync(42);

        var single = Assert.Single(result);
        Assert.Equal(userReminder.Id, single.Id);
    }

    [Fact]
    public async Task MarkDeliveredAsync_SetsDeliveredAt()
    {
        var reminder = await AddReminderAsync(NewReminder());

        await _repository.MarkDeliveredAsync(reminder.Id);

        var due = await _repository.GetDueAsync(DateTime.UtcNow);
        Assert.DoesNotContain(due, r => r.Id == reminder.Id);
    }

    [Fact]
    public async Task MarkDeliveredManyAsync_SetsDeliveredAtForMultipleReminders()
    {
        var first = await AddReminderAsync(NewReminder());
        var second = await AddReminderAsync(NewReminder());
        var third = await AddReminderAsync(NewReminder());

        await _repository.MarkDeliveredManyAsync(new[] { first.Id, second.Id });

        var due = await _repository.GetDueAsync(DateTime.UtcNow);
        Assert.DoesNotContain(due, r => r.Id == first.Id);
        Assert.DoesNotContain(due, r => r.Id == second.Id);
        Assert.Contains(due, r => r.Id == third.Id);
    }

    [Fact]
    public async Task MarkDeliveredManyAsync_WithEmptyIds_IsNoOp()
    {
        var reminder = await AddReminderAsync(NewReminder());

        await _repository.MarkDeliveredManyAsync(Array.Empty<int>());

        var due = await _repository.GetDueAsync(DateTime.UtcNow);
        Assert.Contains(due, r => r.Id == reminder.Id);
    }

    [Fact]
    public async Task RescheduleAsync_UpdatesDueAt_AndClearsDeliveredAt()
    {
        var now = DateTime.UtcNow;
        var reminder = await AddReminderAsync(NewReminder(dueAt: now.AddMinutes(-5), deliveredAt: now.AddMinutes(-1)));
        var newDueAt = now.AddHours(1);

        await _repository.RescheduleAsync(reminder.Id, newDueAt);

        var due = await _repository.GetDueAsync(now);
        Assert.DoesNotContain(due, r => r.Id == reminder.Id);

        var pending = await _repository.GetPendingForUserAsync(reminder.DiscordUserId);
        var updated = Assert.Single(pending);
        Assert.Equal(newDueAt, updated.DueAt);
        Assert.Null(updated.DeliveredAt);
    }

    [Fact]
    public async Task RescheduleManyAsync_UpdatesMultipleReminders()
    {
        var now = DateTime.UtcNow;
        var first = await AddReminderAsync(NewReminder(dueAt: now.AddMinutes(-5)));
        var second = await AddReminderAsync(NewReminder(dueAt: now.AddMinutes(-10)));
        var third = await AddReminderAsync(NewReminder(dueAt: now.AddMinutes(-15)));

        await _repository.RescheduleManyAsync(new[]
        {
            (first.Id, now.AddHours(1)),
            (second.Id, now.AddHours(2))
        });

        var due = await _repository.GetDueAsync(now);
        Assert.Single(due);
        Assert.Equal(third.Id, due[0].Id);

        var pending = await _repository.GetPendingForUserAsync(first.DiscordUserId);
        Assert.Equal(3, pending.Count);
        Assert.Contains(pending, r => r.Id == first.Id && r.DueAt == now.AddHours(1));
        Assert.Contains(pending, r => r.Id == second.Id && r.DueAt == now.AddHours(2));
    }

    [Fact]
    public async Task RescheduleManyAsync_WithEmptyUpdates_IsNoOp()
    {
        var reminder = await AddReminderAsync(NewReminder());

        await _repository.RescheduleManyAsync(Array.Empty<(int, DateTime)>());

        var due = await _repository.GetDueAsync(DateTime.UtcNow);
        Assert.Contains(due, r => r.Id == reminder.Id);
    }

    [Fact]
    public async Task RemovePendingAsync_DeletesOnlyMatchingPendingReminder()
    {
        var reminder = await AddReminderAsync(NewReminder());
        var delivered = await AddReminderAsync(NewReminder(deliveredAt: DateTime.UtcNow));

        Assert.True(await _repository.RemovePendingAsync(reminder.Id, reminder.DiscordUserId));
        Assert.False(await _repository.RemovePendingAsync(delivered.Id, delivered.DiscordUserId));
        Assert.False(await _repository.RemovePendingAsync(reminder.Id, 999ul));

        Assert.Empty(await _repository.GetPendingForUserAsync(reminder.DiscordUserId));
    }

    [Fact]
    public async Task PruneDeliveredAsync_RemovesOnlyOldDeliveredReminders()
    {
        var now = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        var oldDelivered = await AddReminderAsync(NewReminder(dueAt: now.AddDays(-2), deliveredAt: now.AddDays(-2)));
        var recentDelivered = await AddReminderAsync(NewReminder(dueAt: now.AddDays(-1), deliveredAt: now.AddMinutes(-30)));
        var pending = await AddReminderAsync(NewReminder(dueAt: now.AddMinutes(-5)));

        var removed = await _repository.PruneDeliveredAsync(now.AddDays(-1));

        Assert.Equal(1, removed);
        var due = await _repository.GetDueAsync(now);
        Assert.DoesNotContain(due, r => r.Id == oldDelivered.Id);
        Assert.Contains(due, r => r.Id == pending.Id);

        using var context = _factory.CreateDbContext();
        Assert.Equal(2, await context.Reminders.CountAsync());
        var storedRecent = await context.Reminders.AsNoTracking().FirstAsync(r => r.Id == recentDelivered.Id);
        Assert.NotNull(storedRecent.DeliveredAt);
    }
}
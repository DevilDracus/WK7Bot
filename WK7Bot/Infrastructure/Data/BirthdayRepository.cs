namespace WK7Bot.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

/// <summary>
/// SQLite-backed repository for member birthdays.
/// </summary>
public class BirthdayRepository : IBirthdayRepository
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;

    /// <summary>Initializes a new instance of the <see cref="BirthdayRepository"/> class.</summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public BirthdayRepository(IDbContextFactory<BotDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    /// <inheritdoc />
    public async Task UpsertAsync(Birthday birthday, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(birthday);

        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var existing = await context.Birthdays
                .FirstOrDefaultAsync(b => b.DiscordUserId == birthday.DiscordUserId, cancellationToken);

            if (existing is null)
            {
                context.Birthdays.Add(birthday);
            }
            else
            {
                existing.Month = birthday.Month;
                existing.Day = birthday.Day;
                existing.GuildId = birthday.GuildId;
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Birthday>> GetDueTodayAsync(int month, int day, int year, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Birthdays
            .AsNoTracking()
            .Where(b => b.Month == month && b.Day == day && b.LastAnnouncedYear != year)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Birthday?> GetForUserAsync(ulong discordUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Birthdays
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.DiscordUserId == discordUserId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkAnnouncedAsync(int id, int year, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var birthday = await context.Birthdays.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (birthday is null)
        {
            return;
        }

        birthday.LastAnnouncedYear = year;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(ulong discordUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var birthday = await context.Birthdays.FirstOrDefaultAsync(b => b.DiscordUserId == discordUserId, cancellationToken);
        if (birthday is null)
        {
            return false;
        }

        context.Birthdays.Remove(birthday);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}

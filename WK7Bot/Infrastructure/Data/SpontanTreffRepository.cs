using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

namespace WK7Bot.Infrastructure.Data;

/// <summary>
/// Entity Framework Core implementation of the spontaneous meetup repository data access contract.
/// </summary>
public class SpontanTreffRepository : ISpontanTreffRepository
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpontanTreffRepository" /> class.
    /// </summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public SpontanTreffRepository(IDbContextFactory<BotDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    /// <inheritdoc/>
    public async Task<SpontanTreff> AddAsync(SpontanTreff meetup, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await context.SpontanTreffs.AddAsync(meetup, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return meetup;
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(int meetupId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var meetup = await context.SpontanTreffs
            .FirstOrDefaultAsync(m => m.Id == meetupId, cancellationToken);

        if (meetup == null)
        {
            return;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // The raw schema has no foreign key cascade, so the answers are removed explicitly.
            var responses = await context.SpontanTreffResponses
                .Where(r => r.MeetupId == meetupId)
                .ToListAsync(cancellationToken);

            context.SpontanTreffResponses.RemoveRange(responses);
            context.SpontanTreffs.Remove(meetup);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task SetMessageAsync(int meetupId, ulong channelId, ulong messageId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var meetup = await context.SpontanTreffs.FindAsync(new object[] { meetupId }, cancellationToken);
        if (meetup == null)
        {
            return;
        }

        meetup.ChannelId = channelId;
        meetup.MessageId = messageId;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SpontanTreff?> GetAsync(int meetupId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SpontanTreffs.AsNoTracking().FirstOrDefaultAsync(m => m.Id == meetupId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<SpontanTreffResponse>> GetResponsesAsync(int meetupId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        // Ordering by the ulong user id would have to happen inside SQLite, which does not support that, so the
        // rows are sorted in memory after materialization.
        var responses = await context.SpontanTreffResponses.AsNoTracking()
            .Where(r => r.MeetupId == meetupId)
            .ToListAsync(cancellationToken);

        return responses
            .OrderBy(r => r.RespondedAt)
            .ThenBy(r => r.UserId)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<SpontanTreffResponse?> GetResponseAsync(int meetupId, ulong userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SpontanTreffResponses.AsNoTracking()
            .FirstOrDefaultAsync(r => r.MeetupId == meetupId && r.UserId == userId, cancellationToken);
    }

    /// <summary>
    /// Inserts or replaces the answer. When two clicks from the same user race, the first writer wins:
    /// the duplicate insert is swallowed and the stored <see cref="SpontanTreffResponse.Going" /> keeps
    /// whichever value was committed first.
    /// </summary>
    /// <param name="response">The answer to persist.</param>
    /// <param name="cancellationToken">Cancellation token for the database operation.</param>
    /// <returns>A task representing the asynchronous write operation.</returns>
    public async Task SetResponseAsync(SpontanTreffResponse response, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.SpontanTreffResponses
            .FirstOrDefaultAsync(r => r.MeetupId == response.MeetupId && r.UserId == response.UserId, cancellationToken);

        if (existing == null)
        {
            await context.SpontanTreffResponses.AddAsync(response, cancellationToken);

            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (DatabaseWriteGuard.IsUniqueConstraintViolation(ex))
            {
                // A concurrent click from the same user inserted the row first; the desired state is
                // identical. Detach the failed insert so the shared context stays usable.
                context.Entry(response).State = EntityState.Detached;
            }

            return;
        }

        existing.Going = response.Going;
        existing.RespondedAt = response.RespondedAt;
        context.SpontanTreffResponses.Update(existing);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task RemoveResponseAsync(int meetupId, ulong userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.SpontanTreffResponses
            .FirstOrDefaultAsync(r => r.MeetupId == meetupId && r.UserId == userId, cancellationToken);

        if (existing == null)
        {
            return;
        }

        context.SpontanTreffResponses.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task CloseAsync(int meetupId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var meetup = await context.SpontanTreffs.FindAsync(new object[] { meetupId }, cancellationToken);
        if (meetup == null || meetup.Closed)
        {
            return;
        }

        meetup.Closed = true;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<SpontanTreff>> GetExpiredAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SpontanTreffs.AsNoTracking()
            .Where(m => !m.Closed && m.ExpiresAt <= now)
            .OrderBy(m => m.ExpiresAt)
            .ToListAsync(cancellationToken);
    }
}
using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

namespace WK7Bot.Infrastructure.Data;

/// <summary>
/// Entity Framework Core implementation of the spontaneous meetup repository data access contract.
/// </summary>
public class SpontanTreffRepository : ISpontanTreffRepository
{
    private readonly BotDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpontanTreffRepository"/> class.
    /// </summary>
    /// <param name="dbContext">The database context instance.</param>
    public SpontanTreffRepository(BotDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public async Task<SpontanTreff> AddAsync(SpontanTreff meetup, CancellationToken cancellationToken = default)
    {
        await _dbContext.SpontanTreffs.AddAsync(meetup, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return meetup;
    }

    /// <inheritdoc/>
    public async Task SetMessageAsync(int meetupId, ulong channelId, ulong messageId, CancellationToken cancellationToken = default)
    {
        var meetup = await _dbContext.SpontanTreffs.FindAsync(new object[] { meetupId }, cancellationToken);
        if (meetup == null)
        {
            return;
        }

        meetup.ChannelId = channelId;
        meetup.MessageId = messageId;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SpontanTreff?> GetAsync(int meetupId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.SpontanTreffs.AsNoTracking().FirstOrDefaultAsync(m => m.Id == meetupId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<SpontanTreffResponse>> GetResponsesAsync(int meetupId, CancellationToken cancellationToken = default)
    {
        // Ordering by the ulong user id would have to happen inside SQLite, which does not support that, so the
        // rows are sorted in memory after materialization.
        var responses = await _dbContext.SpontanTreffResponses.AsNoTracking()
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
        return await _dbContext.SpontanTreffResponses.AsNoTracking()
            .FirstOrDefaultAsync(r => r.MeetupId == meetupId && r.UserId == userId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SetResponseAsync(SpontanTreffResponse response, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.SpontanTreffResponses
            .FirstOrDefaultAsync(r => r.MeetupId == response.MeetupId && r.UserId == response.UserId, cancellationToken);

        if (existing == null)
        {
            await _dbContext.SpontanTreffResponses.AddAsync(response, cancellationToken);
        }
        else
        {
            existing.Going = response.Going;
            existing.RespondedAt = response.RespondedAt;
            _dbContext.SpontanTreffResponses.Update(existing);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task RemoveResponseAsync(int meetupId, ulong userId, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.SpontanTreffResponses
            .FirstOrDefaultAsync(r => r.MeetupId == meetupId && r.UserId == userId, cancellationToken);

        if (existing == null)
        {
            return;
        }

        _dbContext.SpontanTreffResponses.Remove(existing);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task CloseAsync(int meetupId, CancellationToken cancellationToken = default)
    {
        var meetup = await _dbContext.SpontanTreffs.FindAsync(new object[] { meetupId }, cancellationToken);
        if (meetup == null || meetup.Closed)
        {
            return;
        }

        meetup.Closed = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<SpontanTreff>> GetExpiredAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        return await _dbContext.SpontanTreffs.AsNoTracking()
            .Where(m => !m.Closed && m.ExpiresAt <= now)
            .OrderBy(m => m.ExpiresAt)
            .ToListAsync(cancellationToken);
    }
}

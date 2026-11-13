#pragma warning disable CS1591
#pragma warning disable CS1587
using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

namespace WK7Bot.Infrastructure.Data;

public class GameNightRepository : IGameNightRepository, IGameNightsRepository, IGameNightSchedulerRepository
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;

    public GameNightRepository(IDbContextFactory<BotDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<GameNight> AddAsync(GameNight gameNight, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await context.GameNights.AddAsync(gameNight, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return gameNight;
    }

    public async Task RemoveAsync(int gameNightId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var gn = await context.GameNights.FirstOrDefaultAsync(m => m.Id == gameNightId, cancellationToken);
        if (gn == null) return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var slots = await context.GameNightSlots.Where(s => s.GameNightId == gameNightId).ToListAsync(cancellationToken);
            var slotIds = slots.Select(s => s.Id).ToList();
            var votes = await context.GameNightVotes.Where(v => slotIds.Contains(v.SlotId)).ToListAsync(cancellationToken);
            context.GameNightVotes.RemoveRange(votes);
            context.GameNightSlots.RemoveRange(slots);
            context.GameNights.Remove(gn);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task SetMessageAsync(int gameNightId, ulong channelId, ulong messageId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var gn = await context.GameNights.FindAsync(new object[] { gameNightId }, cancellationToken);
        if (gn == null) return;
        gn.ChannelId = channelId;
        gn.MessageId = messageId;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<GameNight?> GetAsync(int gameNightId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.GameNights.AsNoTracking().FirstOrDefaultAsync(m => m.Id == gameNightId, cancellationToken);
    }

    public async Task<IReadOnlyList<GameNight>> GetExpiredAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.GameNights.AsNoTracking()
            .Where(m => !m.Closed && m.ClosesAt <= now)
            .OrderBy(m => m.ClosesAt)
            .ToListAsync(cancellationToken);
    }

    public async Task CloseAsync(int gameNightId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var gn = await context.GameNights.FindAsync(new object[] { gameNightId }, cancellationToken);
        if (gn == null || gn.Closed) return;
        gn.Closed = true;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CloseManyAsync(IEnumerable<int> gameNightIds, CancellationToken cancellationToken = default)
    {
        var idSet = gameNightIds.ToList();
        if (idSet.Count == 0) return;
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var gns = await context.GameNights.Where(m => idSet.Contains(m.Id) && !m.Closed).ToListAsync(cancellationToken);
        foreach (var gn in gns) gn.Closed = true;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<GameNightSlot> AddSlotAsync(GameNightSlot slot, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await context.GameNightSlots.AddAsync(slot, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return slot;
    }

    public async Task<IReadOnlyList<GameNightSlot>> AddSlotsAsync(int gameNightId, IEnumerable<string> slotLabels, CancellationToken cancellationToken = default)
    {
        var labels = slotLabels.ToList();
        List<GameNightSlot> slots = [];
        int pos = 0;
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        foreach (var label in labels)
        {
            var slot = new GameNightSlot { GameNightId = gameNightId, Label = label.Trim(), Position = pos++ };
            await context.GameNightSlots.AddAsync(slot, cancellationToken).ConfigureAwait(false);
            slots.Add(slot);
        }
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return slots;
    }

    public async Task<IReadOnlyList<GameNightSlot>> GetSlotsAsync(int gameNightId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var slots = await context.GameNightSlots.AsNoTracking()
            .Where(s => s.GameNightId == gameNightId)
            .OrderBy(s => s.Position)
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);
        return slots;
    }

    public async Task<GameNightSlot?> GetSlotAsync(int slotId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.GameNightSlots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == slotId, cancellationToken);
    }

    public async Task<GameNightVote?> GetVoteAsync(int gameNightId, int slotId, ulong userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.GameNightVotes.AsNoTracking()
            .FirstOrDefaultAsync(v => v.GameNightId == gameNightId && v.SlotId == slotId && v.UserId == userId, cancellationToken);
    }

    public Task<GameNightVote?> GetResponseAsync(int gameNightId, int slotId, ulong userId, CancellationToken cancellationToken = default)
        => GetVoteAsync(gameNightId, slotId, userId, cancellationToken);

    public async Task<IReadOnlyList<GameNightVote>> GetVotesAsync(int gameNightId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.GameNightVotes.AsNoTracking()
            .Where(v => v.GameNightId == gameNightId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GameNightVote>> GetVotesForSlotAsync(int slotId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.GameNightVotes.AsNoTracking()
            .Where(v => v.SlotId == slotId)
            .ToListAsync(cancellationToken);
    }

    public Task<IReadOnlyList<GameNightVote>> GetResponsesAsync(int gameNightId, CancellationToken cancellationToken = default)
        => GetVotesAsync(gameNightId, cancellationToken);

    public async Task SetVoteAsync(GameNightVote vote, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.GameNightVotes
            .FirstOrDefaultAsync(v => v.SlotId == vote.SlotId && v.UserId == vote.UserId, cancellationToken);
        if (existing == null)
        {
            vote.GameNightId = vote.GameNightId;
            await context.GameNightVotes.AddAsync(vote, cancellationToken).ConfigureAwait(false);
            try { await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false); }
            catch (DbUpdateException) { context.Entry(vote).State = EntityState.Detached; }
            return;
        }
        existing.Available = vote.Available;
        existing.VotedAt = vote.VotedAt;
        context.GameNightVotes.Update(existing);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SetResponseAsync(GameNightVote vote, CancellationToken cancellationToken = default)
        => SetVoteAsync(vote, cancellationToken);

    public async Task RemoveVoteAsync(int gameNightId, int slotId, ulong userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.GameNightVotes
            .FirstOrDefaultAsync(v => v.GameNightId == gameNightId && v.SlotId == slotId && v.UserId == userId, cancellationToken);
        if (existing == null) return;
        context.GameNightVotes.Remove(existing);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task RemoveResponseAsync(int gameNightId, int slotId, ulong userId, CancellationToken cancellationToken = default)
        => RemoveVoteAsync(gameNightId, slotId, userId, cancellationToken);

    public async Task SetBestSlotAsync(int gameNightId, int slotId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var gn = await context.GameNights.FindAsync(new object[] { gameNightId }, cancellationToken);
        if (gn == null) return;
        gn.BestSlotId = slotId;
        var slot = await context.GameNightSlots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == slotId, cancellationToken);
        if (slot != null) gn.BestSlot = slot.Label;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

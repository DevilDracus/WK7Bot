using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Interfaces;

namespace WK7Bot.Infrastructure.Data;

/// <summary>
/// Shared base class for dispatch-state repositories that persist "already sent" markers
/// with idempotent inserts and retention pruning. Concrete subclasses supply the entity-specific
/// predicates and factory delegates.
/// </summary>
/// <typeparam name="TEntity">The dispatch log entity type.</typeparam>
public abstract class DispatchRepositoryBase<TEntity> where TEntity : class, new()
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DispatchRepositoryBase{TEntity}" /> class.
    /// </summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    protected DispatchRepositoryBase(IDbContextFactory<BotDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> for the concrete entity type from the context.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <returns>The entity set.</returns>
    protected abstract DbSet<TEntity> GetDbSet(BotDbContext context);

    /// <summary>
    /// Checks whether a dispatch record matching the given predicate already exists.
    /// </summary>
    /// <param name="predicate">The match predicate.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns><see langword="true"/> when a matching record exists.</returns>
    protected async Task<bool> HasSentAsyncCore(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await GetDbSet(context)
            .AsNoTracking()
            .AnyAsync(predicate, cancellationToken);
    }

    /// <summary>
    /// Inserts a new dispatch record if one does not already exist. Idempotent: a unique-constraint
    /// violation from a concurrent insert is silently swallowed.
    /// </summary>
    /// <param name="existsPredicate">Predicate to check for an existing record.</param>
    /// <param name="entityFactory">Factory that creates the entity to insert.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous storage operation.</returns>
    protected async Task MarkSentAsyncCore(
        Expression<Func<TEntity, bool>> existsPredicate,
        Func<TEntity> entityFactory,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var alreadySent = await GetDbSet(context)
            .AnyAsync(existsPredicate, cancellationToken);

        if (alreadySent)
        {
            return;
        }

        var log = entityFactory();
        await GetDbSet(context).AddAsync(log, cancellationToken);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DatabaseWriteGuard.IsUniqueConstraintViolation(ex))
        {
            context.Entry(log).State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Removes dispatch records matching the given stale predicate.
    /// </summary>
    /// <param name="stalePredicate">Predicate identifying records to remove.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The number of records removed.</returns>
    protected async Task<int> PruneAsyncCore(
        Expression<Func<TEntity, bool>> stalePredicate,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var stale = await GetDbSet(context)
            .Where(stalePredicate)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            return 0;
        }

        GetDbSet(context).RemoveRange(stale);
        await context.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }
}

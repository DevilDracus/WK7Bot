using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace WK7Bot.Infrastructure.Data;

/// <summary>
/// Classifies database write failures so idempotent check-then-insert paths can treat a lost insert
/// race (the row already exists) as success instead of surfacing it as an error.
/// </summary>
internal static class DatabaseWriteGuard
{
    /// <summary>
    /// Determines whether an <see cref="DbUpdateException"/> was caused by a SQLite primary-key or
    /// unique-constraint violation, i.e. another writer inserted the row first.
    /// </summary>
    /// <param name="exception">The update failure to classify.</param>
    /// <returns><see langword="true"/> when the failure is a duplicate-key violation; otherwise <see langword="false"/>.</returns>
    internal static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        if (exception.InnerException is not SqliteException sqlite)
        {
            return false;
        }

        // SQLITE_CONSTRAINT (19) is the primary code for every constraint failure, so the extended
        // codes distinguish primary-key/unique violations from NOT NULL/CHECK/FOREIGN KEY ones. The
        // message fallback covers providers/versions that only report the primary code.
        return sqlite.SqliteExtendedErrorCode is 1555 or 2067
            || sqlite.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
            || sqlite.Message.Contains("PRIMARY KEY must be unique", StringComparison.OrdinalIgnoreCase);
    }
}

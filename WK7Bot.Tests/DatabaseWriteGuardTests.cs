using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

public class DatabaseWriteGuardTests
{
    [Fact]
    public void IsUniqueConstraintViolation_WithoutInnerException_ReturnsFalse()
    {
        Assert.False(DatabaseWriteGuard.IsUniqueConstraintViolation(new DbUpdateException("insert failed")));
    }

    [Fact]
    public void IsUniqueConstraintViolation_NonSqliteInnerException_ReturnsFalse()
    {
        // The message fallback only applies to SqliteException; any other provider must not match.
        var exception = new DbUpdateException("insert failed", new Exception("UNIQUE constraint failed: WasteDispatchLogs.Kind"));

        Assert.False(DatabaseWriteGuard.IsUniqueConstraintViolation(exception));
    }

    [Fact]
    public void IsUniqueConstraintViolation_UnrelatedInnerException_ReturnsFalse()
    {
        var exception = new DbUpdateException("insert failed", new InvalidOperationException("boom"));

        Assert.False(DatabaseWriteGuard.IsUniqueConstraintViolation(exception));
    }

    [Fact]
    public async Task IsUniqueConstraintViolation_SqliteDuplicateKey_ReturnsTrue()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        try
        {
            var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE t (a INTEGER PRIMARY KEY);
                INSERT INTO t VALUES (1);
                INSERT INTO t VALUES (1);
                """;

            var sqliteException = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
            var wrapped = new DbUpdateException("update failed", sqliteException);

            Assert.True(DatabaseWriteGuard.IsUniqueConstraintViolation(wrapped));
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task IsUniqueConstraintViolation_SqliteNotNullViolation_ReturnsFalse()
    {
        // A NOT NULL failure shares the primary SQLITE_CONSTRAINT code but must not be treated as
        // "row already exists" — otherwise a schema bug would silently drop the write.
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        try
        {
            var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE t (a INTEGER NOT NULL);
                INSERT INTO t VALUES (NULL);
                """;

            var sqliteException = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
            var wrapped = new DbUpdateException("update failed", sqliteException);

            Assert.False(DatabaseWriteGuard.IsUniqueConstraintViolation(wrapped));
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }
}

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WK7Bot.Core.Entities;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

/// <summary>
/// Covers <see cref="XpRepository"/> against the real SQLite provider. The in-memory provider used by
/// most repository tests accepts ordering on <c>ulong</c> columns; SQLite does not, so a query that
/// passes in memory can still throw <c>NotSupportedException</c> in production.
/// </summary>
public class XpRepositoryTests : IAsyncLifetime
{
    private const ulong Guild = 551130054776717323ul;
    private const ulong OtherGuild = 999000111222333444ul;

    private SqliteConnection _connection = null!;
    private XpRepository _repository = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite(_connection)
            .Options;

        await using var context = new BotDbContext(options);
        await context.Database.EnsureCreatedAsync();

        _repository = new XpRepository(new PooledDbContextFactory<BotDbContext>(options));
    }

    /// <inheritdoc />
    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private async Task SeedAsync(params XpProfile[] profiles)
    {
        await using var context = new BotDbContext(
            new DbContextOptionsBuilder<BotDbContext>().UseSqlite(_connection).Options);
        context.Set<XpProfile>().AddRange(profiles);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetLeaderboardAsync_UsesSqlite_AndOrdersByXpDescending()
    {
        await SeedAsync(
            new XpProfile { DiscordUserId = 1, GuildId = Guild, Xp = 100 },
            new XpProfile { DiscordUserId = 2, GuildId = Guild, Xp = 900 },
            new XpProfile { DiscordUserId = 3, GuildId = Guild, Xp = 400 });

        var board = await _repository.GetLeaderboardAsync(Guild, 10);

        Assert.Equal(new ulong[] { 2, 3, 1 }, board.Select(p => p.DiscordUserId));
    }

    [Fact]
    public async Task GetLeaderboardAsync_BreaksTiesOnUserId_WithoutThrowingOnSqlite()
    {
        // The tie-break is a ulong column; ordering it server-side throws on SQLite.
        await SeedAsync(
            new XpProfile { DiscordUserId = 900, GuildId = Guild, Xp = 500 },
            new XpProfile { DiscordUserId = 100, GuildId = Guild, Xp = 500 },
            new XpProfile { DiscordUserId = 500, GuildId = Guild, Xp = 500 });

        var board = await _repository.GetLeaderboardAsync(Guild, 10);

        Assert.Equal(new ulong[] { 100, 500, 900 }, board.Select(p => p.DiscordUserId));
    }

    [Fact]
    public async Task GetLeaderboardAsync_RespectsLimitAndGuildIsolation()
    {
        await SeedAsync(
            new XpProfile { DiscordUserId = 1, GuildId = Guild, Xp = 10 },
            new XpProfile { DiscordUserId = 2, GuildId = Guild, Xp = 20 },
            new XpProfile { DiscordUserId = 3, GuildId = Guild, Xp = 30 },
            new XpProfile { DiscordUserId = 4, GuildId = OtherGuild, Xp = 9999 });

        var board = await _repository.GetLeaderboardAsync(Guild, 2);

        Assert.Equal(2, board.Count);
        Assert.Equal(new ulong[] { 3, 2 }, board.Select(p => p.DiscordUserId));
    }

    [Fact]
    public async Task GetLeaderboardAsync_EmptyGuild_ReturnsEmpty()
    {
        var board = await _repository.GetLeaderboardAsync(Guild, 10);

        Assert.Empty(board);
    }

    [Fact]
    public async Task GetVoiceLeaderboardAsync_UsesSqlite_AndOrdersByVoiceMinutes()
    {
        await SeedAsync(
            new XpProfile { DiscordUserId = 1, GuildId = Guild, VoiceMinutes = 30, Xp = 10 },
            new XpProfile { DiscordUserId = 2, GuildId = Guild, VoiceMinutes = 90, Xp = 5 });

        var board = await _repository.GetVoiceLeaderboardAsync(Guild, 10);

        Assert.Equal(new ulong[] { 2, 1 }, board.Select(p => p.DiscordUserId));
    }
}

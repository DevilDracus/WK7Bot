using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using WK7Bot.Services;
using Xunit;

namespace WK7Bot.Tests;

public class DatabaseMaintenanceServiceTests
{
    private sealed class TestableService : DatabaseMaintenanceService
    {
        public TestableService(
            IDbContextFactory<BotDbContext> dbContextFactory,
            ICommandUsageRepository? commandUsageRepository = null)
            : base(
                dbContextFactory,
                commandUsageRepository ?? Mock.Of<ICommandUsageRepository>(),
                Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()),
                NullLogger<DatabaseMaintenanceService>.Instance)
        {
        }

        public Task ExecuteAsyncPublic(CancellationToken cancellationToken) => ExecuteAsync(cancellationToken);

        public Task RunMaintenanceAsyncPublic(CancellationToken cancellationToken)
        {
            var method = typeof(DatabaseMaintenanceService).GetMethod(
                "RunMaintenanceAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            return (Task)method.Invoke(this, new object[] { cancellationToken })!;
        }
    }

    private static TestableService CreateService(
        IDbContextFactory<BotDbContext> dbContextFactory,
        ICommandUsageRepository? commandUsageRepository = null)
        => new(dbContextFactory, commandUsageRepository);

    private static IDbContextFactory<BotDbContext> CreateDbContextFactory()
    {
        var dbName = $"db-maintenance-tests-{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContextFactory<BotDbContext>(options => options.UseInMemoryDatabase(databaseName: dbName));
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IDbContextFactory<BotDbContext>>();
    }

    private static IDbContextFactory<BotDbContext> CreateSqliteFileDbContextFactory(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite($"Data Source={dbPath};Pooling=false")
            .Options;

        var factory = new PooledDbContextFactory<BotDbContext>(options);
        using var context = factory.CreateDbContext();
        context.Database.EnsureCreated();
        return factory;
    }

    [Fact]
    public async Task ExecuteAsync_CancellationDuringInitialDelay_CompletesGracefully()
    {
        var service = CreateService(CreateDbContextFactory());
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await service.ExecuteAsyncPublic(cts.Token);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationDuringLoop_CompletesGracefully()
    {
        var service = CreateService(CreateDbContextFactory());
        using var cts = new CancellationTokenSource();
        var task = service.ExecuteAsyncPublic(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await task;
    }

    [Fact]
    public void Constructor_NullDbContextFactory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DatabaseMaintenanceService(
            null!,
            Mock.Of<ICommandUsageRepository>(),
            Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()),
            NullLogger<DatabaseMaintenanceService>.Instance));
    }

    [Fact]
    public void Constructor_NullCommandUsageRepository_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DatabaseMaintenanceService(
            CreateDbContextFactory(),
            null!,
            Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()),
            NullLogger<DatabaseMaintenanceService>.Instance));
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DatabaseMaintenanceService(
            CreateDbContextFactory(),
            Mock.Of<ICommandUsageRepository>(),
            null!,
            NullLogger<DatabaseMaintenanceService>.Instance));
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DatabaseMaintenanceService(
            CreateDbContextFactory(),
            Mock.Of<ICommandUsageRepository>(),
            Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()),
            null!));
    }

    [Fact]
    public async Task RunMaintenanceAsync_PrunesCommandUsageAndDispatchLogs_AndCreatesBackup()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"wk7bot-maint-{Guid.NewGuid()}.db");
        var backupDir = Path.Combine(Path.GetDirectoryName(dbPath)!, "backups");

        try
        {
            var factory = CreateSqliteFileDbContextFactory(dbPath);
            var repoMock = new Mock<ICommandUsageRepository>();
            repoMock
                .Setup(r => r.PruneOldLogsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(5);

            await SeedDispatchLogsAsync(factory);

            var service = CreateService(factory, repoMock.Object);
            await service.RunMaintenanceAsyncPublic(CancellationToken.None);

            repoMock.Verify(
                r => r.PruneOldLogsAsync(DatabaseMaintenanceService.DefaultCommandUsageRetentionDays, It.IsAny<CancellationToken>()),
                Times.Once);

            Assert.True(Directory.Exists(backupDir));
            Assert.Contains(Directory.EnumerateFiles(backupDir, "wk7bot_backup_*.db.gz"), _ => true);

            await using var context = await factory.CreateDbContextAsync();
            Assert.Empty(context.WasteDispatchLogs.Where(e => e.SentOn < DateTime.UtcNow.AddDays(-30)).ToList());
            Assert.Single(context.WasteDispatchLogs);
            Assert.Empty(context.WarningDispatchLogs.Where(e => e.SentAt < DateTime.UtcNow.AddDays(-30)).ToList());
            Assert.Single(context.WarningDispatchLogs);
            Assert.Empty(context.FoodDispatchLogs.Where(e => e.SentOn < DateTime.UtcNow.AddDays(-30)).ToList());
            Assert.Single(context.FoodDispatchLogs);
        }
        finally
        {
            TryDelete(dbPath);
            TryDelete(backupDir);
        }
    }

    [Fact]
    public async Task RunMaintenanceAsync_CommandUsagePruneFailure_ContinuesDispatchLogPrune()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"wk7bot-maint-{Guid.NewGuid()}.db");

        try
        {
            var factory = CreateSqliteFileDbContextFactory(dbPath);
            var repoMock = new Mock<ICommandUsageRepository>();
            repoMock
                .Setup(r => r.PruneOldLogsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("command usage prune failed"));

            await SeedDispatchLogsAsync(factory);

            var service = CreateService(factory, repoMock.Object);
            await service.RunMaintenanceAsyncPublic(CancellationToken.None);

            await using var context = await factory.CreateDbContextAsync();
            Assert.Empty(context.WasteDispatchLogs.Where(e => e.SentOn < DateTime.UtcNow.AddDays(-30)).ToList());
            Assert.Single(context.WasteDispatchLogs);
        }
        finally
        {
            TryDelete(dbPath);
            var backupDir = Path.Combine(Path.GetDirectoryName(dbPath)!, "backups");
            TryDelete(backupDir);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenMaintenanceThrows_LogsAndContinues()
    {
        var mockFactory = new Mock<IDbContextFactory<BotDbContext>>();
        mockFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        var service = CreateService(mockFactory.Object);

        using var cts = new CancellationTokenSource();
        var task = service.ExecuteAsyncPublic(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        await task;
    }

    private static async Task SeedDispatchLogsAsync(IDbContextFactory<BotDbContext> factory)
    {
        await using var context = await factory.CreateDbContextAsync();
        context.WasteDispatchLogs.AddRange(
            new WasteDispatchLog { Kind = "old", GuildId = 1, SentOn = DateTime.UtcNow.AddDays(-40) },
            new WasteDispatchLog { Kind = "recent", GuildId = 2, SentOn = DateTime.UtcNow.AddDays(-1) });
        context.WarningDispatchLogs.AddRange(
            new WarningDispatchLog { WarningId = "old", GuildId = 1, SentAt = DateTime.UtcNow.AddDays(-40) },
            new WarningDispatchLog { WarningId = "recent", GuildId = 2, SentAt = DateTime.UtcNow.AddDays(-1) });
        context.FoodDispatchLogs.AddRange(
            new FoodDispatchLog { Kind = "old", SentOn = DateTime.UtcNow.AddDays(-40) },
            new FoodDispatchLog { Kind = "recent", SentOn = DateTime.UtcNow.AddDays(-1) });
        await context.SaveChangesAsync();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for transient test files.
        }
    }
}

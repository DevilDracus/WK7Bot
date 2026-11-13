using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
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
}

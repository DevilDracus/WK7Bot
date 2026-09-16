namespace WK7Bot.Infrastructure.Extensions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;

/// <summary>
/// Provides extension methods for registering infrastructure services, EF Core SQLite data access, and repository layers into the application service container.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Configures and registers the SQLite database context, auto-initialization, and repository dependencies.
    /// </summary>
    /// <param name="services">The service collection instance being configured.</param>
    /// <param name="configuration">The application configuration containing database connection settings.</param>
    /// <returns>The modified service collection instance for chained method calls.</returns>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=wk7bot.db";

        services.AddDbContext<BotDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IRssRepository, RssRepository>();

        return services;
    }

    /// <summary>
    /// Ensures the SQLite database file and required schema are created automatically on application startup.
    /// </summary>
    /// <param name="serviceProvider">The root service provider instance used to create a service scope.</param>
    /// <returns>A task representing the asynchronous database initialization operation.</returns>
    public static async Task InitializeDatabaseAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }
}
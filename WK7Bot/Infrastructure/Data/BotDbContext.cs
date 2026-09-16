namespace WK7Bot.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;

/// <summary>
/// Represents the primary Entity Framework Core database context for managing application entities.
/// </summary>
public class BotDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BotDbContext"/> class with the specified configuration options.
    /// </summary>
    /// <param name="options">The context options controlling database connection and provider behavior.</param>
    public BotDbContext(DbContextOptions<BotDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Gets or sets the database set for managing tracked RSS feeds.
    /// </summary>
    public DbSet<RssFeed> RssFeeds => Set<RssFeed>();

    /// <summary>
    /// Gets or sets the database set for managing interactive RSS dashboard location settings.
    /// </summary>
    public DbSet<RssDashboardSetting> RssDashboardSettings => Set<RssDashboardSetting>();

    /// <summary>
    /// Configures entity mappings, database constraints, and table schemas during model construction.
    /// </summary>
    /// <param name="modelBuilder">The builder instance used to define entity structure and keys.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RssFeed>(entity =>
        {
            entity.ToTable("RssFeeds");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Url).IsRequired().HasMaxLength(500);
            entity.Property(e => e.LastItemGuid).HasMaxLength(250);
        });

        modelBuilder.Entity<RssDashboardSetting>(entity =>
        {
            entity.ToTable("RssDashboardSettings");
            entity.HasKey(e => e.Id);
        });
    }
}
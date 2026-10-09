using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;

namespace WK7Bot.Infrastructure.Data;

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
    /// Gets or sets the database set for tracking already dispatched waste notifications.
    /// </summary>
    public DbSet<WasteDispatchLog> WasteDispatchLogs => Set<WasteDispatchLog>();

    /// <summary>
    /// Gets or sets the database set for tracking already dispatched DWD warnings.
    /// </summary>
    public DbSet<WarningDispatchLog> WarningDispatchLogs => Set<WarningDispatchLog>();

    /// <summary>
    /// Gets or sets the database set for tracking already dispatched automatic food publications.
    /// </summary>
    public DbSet<FoodDispatchLog> FoodDispatchLogs => Set<FoodDispatchLog>();

    /// <summary>
    /// Gets or sets the database set for spontaneous meetups ("Spontan-Treff").
    /// </summary>
    public DbSet<SpontanTreff> SpontanTreffs => Set<SpontanTreff>();

    /// <summary>
    /// Gets or sets the database set for the button answers given to spontaneous meetups.
    /// </summary>
    public DbSet<SpontanTreffResponse> SpontanTreffResponses => Set<SpontanTreffResponse>();

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

        modelBuilder.Entity<WasteDispatchLog>(entity =>
        {
            entity.ToTable("WasteDispatchLogs");
            entity.HasKey(e => new { e.Kind, e.GuildId, e.SentOn });
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(40);
        });

        modelBuilder.Entity<WarningDispatchLog>(entity =>
        {
            entity.ToTable("WarningDispatchLogs");
            entity.HasKey(e => new { e.WarningId, e.GuildId });
            entity.Property(e => e.WarningId).IsRequired().HasMaxLength(160);
        });

        modelBuilder.Entity<FoodDispatchLog>(entity =>
        {
            entity.ToTable("FoodDispatchLogs");
            entity.HasKey(e => new { e.Kind, e.SentOn });
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(40);
        });

        modelBuilder.Entity<SpontanTreff>(entity =>
        {
            entity.ToTable("SpontanTreffs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OrganizerName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Plan).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Location).HasMaxLength(200);

            // Backs the every-minute expiry sweep (Closed == false && ExpiresAt <= now).
            entity.HasIndex(e => new { e.Closed, e.ExpiresAt });
        });

        modelBuilder.Entity<SpontanTreffResponse>(entity =>
        {
            entity.ToTable("SpontanTreffResponses");
            entity.HasKey(e => new { e.MeetupId, e.UserId });
        });
    }
}
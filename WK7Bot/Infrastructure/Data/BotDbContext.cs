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
    /// Gets or sets the database set for tracking slash command usage metrics.
    /// </summary>
    public DbSet<CommandUsageLog> CommandUsageLogs => Set<CommandUsageLog>();

    /// <summary>
    /// Gets or sets the database set for tracked wishlist games used by the deal alert service.
    /// </summary>
    public DbSet<WishlistGame> WishlistGames => Set<WishlistGame>();

    /// <summary>
    /// Gets or sets the database set for wishlist deal alert cooldown history.
    /// </summary>
    public DbSet<WishlistAlertHistory> WishlistAlertHistories => Set<WishlistAlertHistory>();

    /// <summary>
    /// Gets or sets the database set for Discord user to external gaming account links.
    /// </summary>
    public DbSet<UserAccountLink> UserAccountLinks => Set<UserAccountLink>();

    /// <summary>
    /// Gets or sets the database set for per-user deal watchlist entries.
    /// </summary>
    public DbSet<DealWatchItem> DealWatchItems => Set<DealWatchItem>();

    /// <summary>
    /// Gets or sets the database set for Discord guild scheduled events created by the bot.
    /// </summary>
    public DbSet<DiscordScheduledEvent> DiscordScheduledEvents => Set<DiscordScheduledEvent>();

    /// <summary>
    /// Gets or sets the database set for personal reminders.
    /// </summary>
    public DbSet<Reminder> Reminders => Set<Reminder>();

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
            entity.Property(e => e.IsPaused).HasDefaultValue(false);
            entity.HasIndex(e => e.Name);
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
            entity.HasIndex(e => e.SentOn);
        });

        modelBuilder.Entity<WarningDispatchLog>(entity =>
        {
            entity.ToTable("WarningDispatchLogs");
            entity.HasKey(e => new { e.WarningId, e.GuildId });
            entity.Property(e => e.WarningId).IsRequired().HasMaxLength(160);
            entity.HasIndex(e => e.SentAt);
        });

        modelBuilder.Entity<FoodDispatchLog>(entity =>
        {
            entity.ToTable("FoodDispatchLogs");
            entity.HasKey(e => new { e.Kind, e.SentOn });
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(40);
            entity.HasIndex(e => e.SentOn);
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
            entity.HasOne<SpontanTreff>()
                .WithMany()
                .HasForeignKey(e => e.MeetupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CommandUsageLog>(entity =>
        {
            entity.ToTable("CommandUsageLogs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CommandName).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.InvokedAt);
            entity.HasIndex(e => e.CommandName);
        });

        modelBuilder.Entity<WishlistGame>(entity =>
        {
            entity.ToTable("WishlistGames");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AppId).IsUnique();
        });

        modelBuilder.Entity<WishlistAlertHistory>(entity =>
        {
            entity.ToTable("WishlistAlertHistories");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.AppId, e.AlertedAt });
            entity.HasIndex(e => e.AlertedAt);
        });

        modelBuilder.Entity<UserAccountLink>(entity =>
        {
            entity.ToTable("UserAccountLinks");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.DiscordUserId, e.Provider }).IsUnique();
            entity.Property(e => e.Provider).IsRequired().HasMaxLength(32);
            entity.Property(e => e.ExternalId).IsRequired().HasMaxLength(200);
            entity.Property(e => e.DisplayName).HasMaxLength(200);
            entity.Property(e => e.Region).HasMaxLength(16);
            entity.Property(e => e.WishlistUrl).HasMaxLength(500);
        });

        modelBuilder.Entity<DealWatchItem>(entity =>
        {
            entity.ToTable("DealWatchItems");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.DiscordUserId, e.GameName }).IsUnique();
            entity.Property(e => e.GameName).IsRequired().HasMaxLength(300);
            entity.Property(e => e.ItadId).HasMaxLength(100);
        });

        modelBuilder.Entity<DiscordScheduledEvent>(entity =>
        {
            entity.ToTable("DiscordScheduledEvents");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.EndAt);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(40);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<Reminder>(entity =>
        {
            entity.ToTable("Reminders");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Text).IsRequired().HasMaxLength(500);

            // Backs the delivery sweep (DeliveredAt IS NULL AND DueAt <= now) and the per-user list.
            entity.HasIndex(e => new { e.DeliveredAt, e.DueAt });
            entity.HasIndex(e => e.DiscordUserId);
        });
    }
}
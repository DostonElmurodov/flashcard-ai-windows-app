using Mavrylo.Models;
using Microsoft.EntityFrameworkCore;

namespace Mavrylo.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AccountSyncEntity> AccountSyncRecords => Set<AccountSyncEntity>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<WordEntity> Words => Set<WordEntity>();
    public DbSet<CategoryEntity> Categories => Set<CategoryEntity>();
    public DbSet<UserSettingsEntity> UserSettings => Set<UserSettingsEntity>();
    public DbSet<TranslationCacheEntity> TranslationCache => Set<TranslationCacheEntity>();
    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();
    public DbSet<SubscriptionEntity> Subscriptions => Set<SubscriptionEntity>();
    public DbSet<AiUsageEntity> AiUsage => Set<AiUsageEntity>();
    public DbSet<ChallengeEntity> Challenges => Set<ChallengeEntity>();
    public DbSet<DeviceWordEntity> DeviceWords => Set<DeviceWordEntity>();
    public DbSet<PublicFlashcardSetEntity> PublicFlashcardSets => Set<PublicFlashcardSetEntity>();

    public DbSet<AccountSessionEntity> AccountSessions => Set<AccountSessionEntity>();
    public DbSet<AccountRefreshTokenEntity> AccountRefreshTokens => Set<AccountRefreshTokenEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountSyncEntity>(e =>
        {
            e.ToTable("account_sync_records");
            e.HasKey(x => new { x.UserId, x.Kind, x.Id });
            e.HasIndex(x => new { x.UserId, x.ChangeRevision });
            e.Property(x => x.Kind).HasMaxLength(8);
            e.Property(x => x.Id).HasMaxLength(128);
            e.Property(x => x.Data).HasColumnType("jsonb");
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique().HasFilter("\"GoogleSub\" IS NULL");
            e.HasIndex(x => x.GoogleSub).IsUnique();
            e.HasIndex(x => x.AppleSub);
        });
        modelBuilder.Entity<AccountSessionEntity>(e =>
        {
            e.ToTable("account_sessions");
            e.HasKey(x => x.Id);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AccountRefreshTokenEntity>(e =>
        {
            e.ToTable("account_refresh_tokens");
            e.HasKey(x => x.Hash);
            e.Property(x => x.Hash).HasMaxLength(64);
            e.HasOne<AccountSessionEntity>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<WordEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.UpdatedAt });
        });
        modelBuilder.Entity<CategoryEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
        });
        modelBuilder.Entity<UserSettingsEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId).IsUnique();
        });
        modelBuilder.Entity<TranslationCacheEntity>(e =>
        {
            e.ToTable("translation_cache");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.NormalizedKey, x.NativeLanguage, x.LearningLanguage, x.CacheKind, x.PromptVersion })
                .IsUnique()
                .HasDatabaseName("IX_translation_cache_lookup");
        });
        modelBuilder.Entity<DeviceEntity>(e =>
        {
            e.ToTable("devices");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.KeyId).IsUnique();
            e.HasIndex(x => x.DeviceUuid);
        });
        modelBuilder.Entity<SubscriptionEntity>(e =>
        {
            e.ToTable("subscriptions");
            e.HasKey(x => x.OriginalTransactionId);
            e.HasIndex(x => x.DeviceUuid);
            e.HasIndex(x => x.OwnerAccountId);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerAccountId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<AiUsageEntity>(e =>
        {
            e.ToTable("ai_usage");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.KeyId, x.Date }).IsUnique();
        });
        modelBuilder.Entity<ChallengeEntity>(e =>
        {
            e.ToTable("challenges");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Kind, x.KeyId, x.RequestPath, x.ExpiresAt });
            e.HasIndex(x => x.ConsumedAt);
        });
        modelBuilder.Entity<DeviceWordEntity>(e =>
        {
            e.ToTable("device_words");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.DeviceUuid, x.NormalizedWord, x.NativeLanguage, x.LearningLanguage }).IsUnique();
            e.HasIndex(x => new { x.DeviceUuid, x.ClientWordId });
            e.HasIndex(x => new { x.DeviceUuid, x.IsActive });
        });
        modelBuilder.Entity<PublicFlashcardSetEntity>(e =>
        {
            e.ToTable("public_flashcard_sets", table =>
                table.HasCheckConstraint(
                    "CK_public_flashcard_sets_status",
                    "\"Status\" IN ('pending', 'approved')"));
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.OwnerDeviceId, x.ClientSetId }).IsUnique();
            e.HasIndex(x => new { x.OwnerAccountId, x.ClientSetId }).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerAccountId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable("public_flashcard_sets", table => table.HasCheckConstraint("CK_public_flashcard_sets_owner", "(\"OwnerDeviceId\" IS NOT NULL) <> (\"OwnerAccountId\" IS NOT NULL)"));
            e.HasIndex(x => new { x.Status, x.UpdatedAt });
            e.HasOne(x => x.OwnerDevice)
                .WithMany()
                .HasForeignKey(x => x.OwnerDeviceId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Title).HasMaxLength(120);
            e.Property(x => x.Description).HasMaxLength(1_000);
            e.Property(x => x.ClientSetId).HasMaxLength(100);
            e.Property(x => x.Status).HasMaxLength(16);
        });
    }
}

using Microsoft.EntityFrameworkCore;
using OTPAuth.API.Entities;

namespace OTPAuth.API.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Email).HasMaxLength(254).IsRequired();
            entity.Property(user => user.NormalizedEmail).HasMaxLength(254).IsRequired();
            entity.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(user => user.FullName).HasMaxLength(100).IsRequired();
            entity.Property(user => user.IsActive).HasDefaultValue(true);
            entity.Property(user => user.CreatedAt).HasPrecision(7).IsRequired();
            entity.Property(user => user.RowVersion).IsRowVersion();
            entity.HasIndex(user => user.NormalizedEmail).IsUnique().HasDatabaseName("UX_Users_NormalizedEmail");
        });

        modelBuilder.Entity<OtpChallenge>(entity =>
        {
            entity.ToTable("OtpChallenges", table =>
            {
                table.HasCheckConstraint("CK_OtpChallenges_Purpose", "[Purpose] = 'LOGIN'");
                table.HasCheckConstraint("CK_OtpChallenges_ExpiresAt", "[ExpiresAt] IS NULL OR ([ExpiresAt] > [CreatedAt] AND [ExpiresAt] <= [FlowExpiresAt])");
                table.HasCheckConstraint("CK_OtpChallenges_Attempts", "[AttemptCount] >= 0 AND [AttemptCount] <= [MaxAttempts] AND [MaxAttempts] BETWEEN 1 AND 5");
                table.HasCheckConstraint("CK_OtpChallenges_ResendCount", "[ResendCount] BETWEEN 0 AND 3");
                table.HasCheckConstraint(
                    "CK_OtpChallenges_OtpState",
                    "([OtpHash] IS NULL AND [ExpiresAt] IS NULL AND [SentAt] IS NULL) OR " +
                    "([OtpHash] IS NOT NULL AND DATALENGTH([OtpHash]) = 32 AND [ExpiresAt] IS NOT NULL AND " +
                    "([SentAt] IS NULL OR ([SentAt] >= [CreatedAt] AND [SentAt] < [ExpiresAt])))");
                table.HasCheckConstraint(
                    "CK_OtpChallenges_ConsumedState",
                    "[ConsumedAt] IS NULL OR ([SentAt] IS NOT NULL AND [ConsumedAt] >= [SentAt] AND [ConsumedAt] < [ExpiresAt])");
            });
            entity.HasKey(challenge => challenge.Id);
            entity.Property(challenge => challenge.OtpHash).HasColumnType("varbinary(32)");
            entity.Property(challenge => challenge.Purpose).HasMaxLength(32).IsUnicode(false).IsRequired();
            entity.Property(challenge => challenge.CreatedAt).HasPrecision(7).IsRequired();
            entity.Property(challenge => challenge.ExpiresAt).HasPrecision(7);
            entity.Property(challenge => challenge.FlowExpiresAt).HasPrecision(7).IsRequired();
            entity.Property(challenge => challenge.SentAt).HasPrecision(7);
            entity.Property(challenge => challenge.ConsumedAt).HasPrecision(7);
            entity.Property(challenge => challenge.MaxAttempts).HasDefaultValue((short)5);
            entity.Property(challenge => challenge.RowVersion).IsRowVersion();
            entity.HasIndex(challenge => new { challenge.UserId, challenge.Purpose, challenge.CreatedAt })
                .IsDescending(false, false, true)
                .HasDatabaseName("IX_OtpChallenges_UserId_Purpose_CreatedAt");
            entity.HasIndex(challenge => new { challenge.AuthenticationFlowId, challenge.CreatedAt })
                .HasDatabaseName("IX_OtpChallenges_AuthenticationFlowId_CreatedAt");
            entity.HasIndex(challenge => new { challenge.UserId, challenge.Purpose })
                .IsUnique()
                .HasFilter("[IsRevoked] = 0 AND [ConsumedAt] IS NULL")
                .HasDatabaseName("UX_OtpChallenges_UserId_Purpose_Open");
            entity.HasOne(challenge => challenge.User)
                .WithMany(user => user.OtpChallenges)
                .HasForeignKey(challenge => challenge.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(log => log.Id);
            entity.Property(log => log.Id).UseIdentityColumn();
            entity.Property(log => log.EventType).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.Property(log => log.ReasonCode).HasMaxLength(64).IsUnicode(false);
            entity.Property(log => log.IpAddress).HasMaxLength(45).IsUnicode(false);
            entity.Property(log => log.UserAgent).HasMaxLength(256);
            entity.Property(log => log.CorrelationId).HasMaxLength(64).IsUnicode(false);
            entity.Property(log => log.CreatedAt).HasPrecision(7).IsRequired();
            entity.HasIndex(log => log.CreatedAt).IsDescending().HasDatabaseName("IX_AuditLogs_CreatedAt");
            entity.HasIndex(log => new { log.UserId, log.CreatedAt }).IsDescending(false, true).HasDatabaseName("IX_AuditLogs_UserId_CreatedAt");
            entity.HasIndex(log => new { log.EventType, log.CreatedAt }).IsDescending(false, true).HasDatabaseName("IX_AuditLogs_EventType_CreatedAt");
            entity.HasOne(log => log.User)
                .WithMany(user => user.AuditLogs)
                .HasForeignKey(log => log.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}

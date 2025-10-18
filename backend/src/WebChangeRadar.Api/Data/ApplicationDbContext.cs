using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace WebChangeRadar.Api.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Monitor> Monitors => Set<Monitor>();
    public DbSet<CheckRun> CheckRuns => Set<CheckRun>();
    public DbSet<Snapshot> Snapshots => Set<Snapshot>();
    public DbSet<ChangeEvent> ChangeEvents => Set<ChangeEvent>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Monitor>(entity =>
        {
            entity.ToTable("Monitors", table =>
            {
                table.HasCheckConstraint("CK_Monitors_IntervalMinutes", "\"IntervalMinutes\" IN (60, 360, 1440)");
                table.HasCheckConstraint("CK_Monitors_Status", "\"Status\" IN ('active', 'paused')");
            });

            entity.HasKey(monitor => monitor.Id);
            entity.Property(monitor => monitor.Name).HasMaxLength(120).IsRequired();
            entity.Property(monitor => monitor.Url).HasMaxLength(2048).IsRequired();
            entity.Property(monitor => monitor.ContentSelector).HasMaxLength(160);
            entity.Property(monitor => monitor.KeywordsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(monitor => monitor.Status).HasMaxLength(16).IsRequired();
            entity.Property(monitor => monitor.UserId).IsRequired();
            entity.Property(monitor => monitor.CreatedAt).IsRequired();
            entity.Property(monitor => monitor.UpdatedAt).IsRequired();

            entity.HasOne(monitor => monitor.User)
                .WithMany(user => user.Monitors)
                .HasForeignKey(monitor => monitor.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(monitor => new { monitor.UserId, monitor.Status });
            entity.HasIndex(monitor => monitor.NextCheckAt);
        });
        builder.Entity<CheckRun>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TriggerType).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(16).IsRequired();
            entity.Property(x => x.ErrorCode).HasMaxLength(64);
            entity.HasOne(x => x.Monitor).WithMany().HasForeignKey(x => x.MonitorId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.MonitorId, x.RequestedAt });
            entity.HasIndex(x => new { x.Status, x.LeaseExpiresAt });
        });
        builder.Entity<Snapshot>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.NormalizedText).IsRequired();
            entity.HasOne(x => x.Monitor).WithMany().HasForeignKey(x => x.MonitorId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.MonitorId, x.CapturedAt });
        });
        builder.Entity<ChangeEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Monitor).WithMany().HasForeignKey(x => x.MonitorId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.OldSnapshot).WithMany().HasForeignKey(x => x.OldSnapshotId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.NewSnapshot).WithMany().HasForeignKey(x => x.NewSnapshotId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.MonitorId, x.DetectedAt });
            entity.HasIndex(x => x.NewSnapshotId).IsUnique();
        });
        builder.Entity<Notification>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ChangeEvent).WithMany().HasForeignKey(x => x.ChangeEventId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.ChangeEventId).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.CreatedAt });
        });
    }
}

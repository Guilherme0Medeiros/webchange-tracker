using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace WebChangeRadar.Api.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Monitor> Monitors => Set<Monitor>();

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
    }
}

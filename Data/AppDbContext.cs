using Microsoft.EntityFrameworkCore;
using ReleaseManager.Models;

namespace ReleaseManager.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<ProgramInfo> Programs => Set<ProgramInfo>();
    public DbSet<Release> Releases => Set<Release>();
    public DbSet<ReleaseItem> ReleaseItems => Set<ReleaseItem>();
    public DbSet<FtpConfig> FtpConfigs => Set<FtpConfig>();
    public DbSet<EmailConfig> EmailConfigs => Set<EmailConfig>();
    public DbSet<WhatsAppConfig> WhatsAppConfigs => Set<WhatsAppConfig>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ProgramInfo>().HasIndex(x => x.Name).IsUnique();
        b.Entity<AppUser>().HasIndex(x => x.Username).IsUnique();
        b.Entity<Release>().HasIndex(x => new { x.ProgramInfoId, x.Version }).IsUnique();
        b.Entity<ProgramInfo>().HasOne(x => x.FtpConfig).WithMany().HasForeignKey(x => x.FtpConfigId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<ProgramInfo>().HasOne(x => x.EmailConfig).WithMany().HasForeignKey(x => x.EmailConfigId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<ProgramInfo>().HasMany(x => x.Releases).WithOne(x => x.ProgramInfo).HasForeignKey(x => x.ProgramInfoId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Release>().HasMany(x => x.Items).WithOne(x => x.Release).HasForeignKey(x => x.ReleaseId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ReleaseItem>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<ReleaseItem>().Property(x => x.Description).HasColumnType("longtext");
        b.Entity<AuditLog>().Property(x => x.Description).HasColumnType("text");
    }
}

using Microsoft.EntityFrameworkCore;
using Shortnr.Data.Entities;

namespace Shortnr.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ShortenedUrl> ShortenedUrls => Set<ShortenedUrl>();
    public DbSet<ClickEvent> ClickEvents => Set<ClickEvent>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Domain> Domains => Set<Domain>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<BioPage> BioPages => Set<BioPage>();
    public DbSet<BioPageLink> BioPageLinks => Set<BioPageLink>();
    public DbSet<AiActivityLog> AiActivityLogs => Set<AiActivityLog>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortenedUrl>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.LongUrl).IsRequired();
            entity.Property(e => e.ShortCode).IsRequired().HasMaxLength(64);
            // Uniqueness is scoped per-domain so different domains can reuse the
            // same slug independently. SQLite treats NULLs as distinct inside
            // unique indexes, so default-domain (DomainId IS NULL) uniqueness is
            // enforced with a filtered index rather than a plain composite one.
            entity.HasIndex(e => new { e.DomainId, e.ShortCode })
                .IsUnique()
                .HasFilter("[DomainId] IS NOT NULL");
            entity.HasIndex(e => e.ShortCode)
                .IsUnique()
                .HasFilter("[DomainId] IS NULL");
            entity.Property(e => e.CreatedAtUtc).HasDefaultValueSql("datetime('now')");

            entity.HasOne(e => e.Owner)
                .WithMany(u => u.ShortenedUrls)
                .HasForeignKey(e => e.OwnerUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Domain)
                .WithMany()
                .HasForeignKey(e => e.DomainId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Workspace)
                .WithMany(w => w.ShortenedUrls)
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Domain>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Hostname).IsRequired().HasMaxLength(255);
            entity.HasIndex(e => e.Hostname).IsUnique();
            entity.Property(e => e.VerificationToken).IsRequired().HasMaxLength(128);
            entity.Property(e => e.CreatedAtUtc).HasDefaultValueSql("datetime('now')");

            entity.HasOne(e => e.Owner)
                .WithMany()
                .HasForeignKey(e => e.OwnerUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Issuer).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Subject).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Email).HasMaxLength(320);
            entity.Property(e => e.Name).HasMaxLength(256);
            entity.HasIndex(e => new { e.Issuer, e.Subject }).IsUnique();
            entity.Property(e => e.CreatedAtUtc).HasDefaultValueSql("datetime('now')");
        });

        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.KeyHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.KeyPrefix).IsRequired().HasMaxLength(16);
            entity.Property(e => e.Label).IsRequired().HasMaxLength(128);
            entity.HasIndex(e => e.KeyHash).IsUnique();
            entity.Property(e => e.CreatedAtUtc).HasDefaultValueSql("datetime('now')");

            entity.HasOne(e => e.Owner)
                .WithMany()
                .HasForeignKey(e => e.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClickEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.IpAddress).HasMaxLength(64);
            entity.Property(e => e.UserAgent).HasMaxLength(512);
            entity.Property(e => e.Referer).HasMaxLength(2048);
            entity.Property(e => e.ClickedAtUtc).HasDefaultValueSql("datetime('now')");

            entity.Property(e => e.CountryCode).HasMaxLength(2);
            entity.Property(e => e.CountryName).HasMaxLength(100);
            entity.Property(e => e.CityName).HasMaxLength(100);
            entity.Property(e => e.PostalCode).HasMaxLength(20);

            entity.Property(e => e.DeviceFamily).HasMaxLength(50);
            entity.Property(e => e.OperatingSystem).HasMaxLength(50);
            entity.Property(e => e.OSVersion).HasMaxLength(50);
            entity.Property(e => e.Browser).HasMaxLength(50);
            entity.Property(e => e.BrowserVersion).HasMaxLength(50);

            entity.HasOne(e => e.ShortenedUrl)
                .WithMany(s => s.ClickEvents)
                .HasForeignKey(e => e.ShortenedUrlId);
        });

        modelBuilder.Entity<BioPage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Slug).IsRequired().HasMaxLength(64);
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(256);
            entity.Property(e => e.AvatarUrl).HasMaxLength(512);
            entity.Property(e => e.BioText).HasMaxLength(2000);
            entity.Property(e => e.Theme).IsRequired().HasMaxLength(32);
            entity.Property(e => e.CreatedAtUtc).HasDefaultValueSql("datetime('now')");

            entity.HasOne(e => e.Owner)
                .WithMany()
                .HasForeignKey(e => e.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BioPageLink>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(256);
            entity.Property(e => e.IconUrl).HasMaxLength(512);
            entity.HasIndex(e => new { e.BioPageId, e.ShortenedUrlId }).IsUnique();

            entity.HasOne(e => e.BioPage)
                .WithMany(b => b.Links)
                .HasForeignKey(e => e.BioPageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ShortenedUrl)
                .WithMany()
                .HasForeignKey(e => e.ShortenedUrlId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AiActivityLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Action).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Summary).IsRequired().HasMaxLength(512);
            entity.Property(e => e.TargetEntityType).HasMaxLength(64);
            entity.HasIndex(e => new { e.OwnerUserId, e.CreatedAtUtc });
            entity.Property(e => e.CreatedAtUtc).HasDefaultValueSql("datetime('now')");

            entity.HasOne(e => e.Owner)
                .WithMany()
                .HasForeignKey(e => e.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ApiKey)
                .WithMany()
                .HasForeignKey(e => e.ApiKeyId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Workspace>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Slug).IsRequired().HasMaxLength(32);
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.Property(e => e.CreatedAtUtc).HasDefaultValueSql("datetime('now')");

            entity.HasOne(e => e.Owner)
                .WithMany()
                .HasForeignKey(e => e.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkspaceMember>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.WorkspaceId, e.UserId }).IsUnique();
            entity.Property(e => e.Role).HasConversion<int>();
            entity.Property(e => e.InviteEmail).HasMaxLength(320);

            entity.HasOne(e => e.Workspace)
                .WithMany(w => w.Members)
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

using Microsoft.EntityFrameworkCore;
using Shortnr.Data.Entities;

namespace Shortnr.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ShortenedUrl> ShortenedUrls => Set<ShortenedUrl>();
    public DbSet<ClickEvent> ClickEvents => Set<ClickEvent>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Domain> Domains => Set<Domain>();

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
    }
}

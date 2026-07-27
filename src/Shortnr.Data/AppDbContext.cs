using Microsoft.EntityFrameworkCore;
using Shortnr.Data.Entities;

namespace Shortnr.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ShortenedUrl> ShortenedUrls => Set<ShortenedUrl>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortenedUrl>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.LongUrl).IsRequired();
            entity.Property(e => e.ShortCode).IsRequired().HasMaxLength(64);
            entity.HasIndex(e => e.ShortCode).IsUnique();
            entity.Property(e => e.CreatedAtUtc).HasDefaultValueSql("datetime('now')");
        });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Shortnr.Data;

public static class DatabaseProviderHelper
{
    public const string ConfigSection = "Database";
    public const string ProviderKey = "Provider";

    public static DatabaseProvider ResolveProvider(IConfiguration configuration)
    {
        var value = configuration[$"{ConfigSection}:{ProviderKey}"];

        if (string.IsNullOrWhiteSpace(value))
            return DatabaseProvider.Sqlite;

        return value.Trim().ToLowerInvariant() switch
        {
            "sqlite" => DatabaseProvider.Sqlite,
            "postgres" or "postgresql" or "npgsql" => DatabaseProvider.Postgres,
            _ => throw new InvalidOperationException(
                $"Unsupported '{ConfigSection}:{ProviderKey}' value '{value}'. " +
                $"Supported values: Sqlite, Postgres, MySql.")
        };
    }

    public static string? ResolveConnectionString(IConfiguration configuration, DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.Sqlite => configuration.GetConnectionString("DefaultConnection")
                ?? "Data Source=shortnr.db",
            _ => configuration.GetConnectionString("DefaultConnection")
        };
    }

    public static DbContextOptionsBuilder UseProvider(
        this DbContextOptionsBuilder builder,
        DatabaseProvider provider,
        string connectionString)
    {
        return provider switch
        {
            DatabaseProvider.Sqlite => builder.UseSqlite(connectionString).UseOpenIddict(),
            // Postgres migrations live in their own assembly (Shortnr.Data.Postgres), not
            // Shortnr.Data's SQLite-flavored ones -- Migrate() replays a migration's frozen
            // Up()/Down() operations verbatim, so the two providers can't share one history.
            DatabaseProvider.Postgres => builder.UseNpgsql(connectionString,
                npgsql => npgsql.MigrationsAssembly("Shortnr.Data.Postgres")).UseOpenIddict(),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
    }

    public static string GetProviderAnnotation(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.Sqlite => "Microsoft.EntityFrameworkCore.Sqlite",
        DatabaseProvider.Postgres => "Npgsql.EntityFrameworkCore.PostgreSQL",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
}

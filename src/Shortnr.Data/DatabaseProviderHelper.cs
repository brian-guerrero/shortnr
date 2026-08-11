using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Shortnr.Data;

public static class DatabaseProviderHelper
{
    public const string ConfigSection = "Database";
    public const string ProviderKey = "Provider";
    public const string ConnectionStringKey = "ConnectionString";

    public static DatabaseProvider ResolveProvider(IConfiguration configuration)
    {
        var value = configuration[$"{ConfigSection}:{ProviderKey}"];

        if (string.IsNullOrWhiteSpace(value))
            return DatabaseProvider.Sqlite;

        return value.Trim().ToLowerInvariant() switch
        {
            "sqlite" => DatabaseProvider.Sqlite,
            "postgres" or "postgresql" or "npgsql" => DatabaseProvider.Postgres,
            "mysql" or "mariadb" or "pomelo" => DatabaseProvider.MySql,
            _ => throw new InvalidOperationException(
                $"Unsupported '{ConfigSection}:{ProviderKey}' value '{value}'. " +
                $"Supported values: Sqlite, Postgres, MySql.")
        };
    }

    public static string? ResolveConnectionString(IConfiguration configuration, DatabaseProvider provider)
    {
        var configured = configuration[$"{ConfigSection}:{ConnectionStringKey}"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

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
            DatabaseProvider.Postgres => builder.UseNpgsql(connectionString).UseOpenIddict(),
            DatabaseProvider.MySql => builder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)).UseOpenIddict(),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
    }

    public static string GetProviderAnnotation(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.Sqlite => "Microsoft.EntityFrameworkCore.Sqlite",
        DatabaseProvider.Postgres => "Npgsql.EntityFrameworkCore.PostgreSQL",
        DatabaseProvider.MySql => "Pomelo.EntityFrameworkCore.MySql",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
}

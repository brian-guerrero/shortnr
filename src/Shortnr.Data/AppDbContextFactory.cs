using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shortnr.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var provider = ResolveProviderFromEnv();
        var connectionString = ResolveConnectionStringFromEnv(provider);

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseProvider(provider, connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }

    private static DatabaseProvider ResolveProviderFromEnv()
    {
        var value = Environment.GetEnvironmentVariable("Database__Provider")
            ?? Environment.GetEnvironmentVariable("DATABASE__PROVIDER");

        if (string.IsNullOrWhiteSpace(value))
            return DatabaseProvider.Sqlite;

        return value.Trim().ToLowerInvariant() switch
        {
            "sqlite" => DatabaseProvider.Sqlite,
            "postgres" or "postgresql" or "npgsql" => DatabaseProvider.Postgres,
            _ => throw new InvalidOperationException(
                $"Unsupported 'Database__Provider' value '{value}'. " +
                $"Supported values: Sqlite, Postgres, MySql.")
        };
    }

    private static string ResolveConnectionStringFromEnv(DatabaseProvider provider)
    {
        var configured = Environment.GetEnvironmentVariable("Database__ConnectionString")
            ?? Environment.GetEnvironmentVariable("DATABASE__CONNECTIONSTRING");

        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var legacy = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__DEFAULTCONNECTION");

        return provider switch
        {
            DatabaseProvider.Sqlite => legacy ?? "Data Source=shortnr.db",
            _ => legacy ?? throw new InvalidOperationException(
                $"No connection string configured for database provider '{provider}'. " +
                $"Set 'Database__ConnectionString' environment variable.")
        };
    }
}

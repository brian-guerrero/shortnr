---
title: Database migration
description: Migrate shortnr data between SQLite, PostgreSQL, and MySQL.
order: 4
---

# Database migration

shortnr supports SQLite, PostgreSQL, and MySQL. This guide covers migrating data between providers.

## When to migrate

**Stay on SQLite if:**
- Single-user or small team deployment
- Low write volume (under ~100 clicks/minute)
- Simplicity is preferred over scalability

**Consider PostgreSQL if:**
- Multiple concurrent users
- Higher write volume (100+ clicks/minute)
- Need for advanced queries or analytics
- Existing Postgres infrastructure

**Consider MySQL if:**
- Existing MySQL/MariaDB infrastructure
- Team familiarity with MySQL
- Migrating from YOURLS or similar MySQL-based tools

## Migration process

shortnr does not support in-place database migration. Instead, export data from the source database and import into the target.

### Step 1: Export from SQLite

Use the SQLite CLI to export data:

```bash
# Export all tables to SQL
sqlite3 shortnr.db .dump > shortnr-export.sql

# Or export specific tables as CSV
sqlite3 -header -csv shortnr.db "SELECT * FROM ShortenedUrls;" > urls.csv
sqlite3 -header -csv shortnr.db "SELECT * FROM ClickEvents;" > clicks.csv
sqlite3 -header -csv shortnr.db "SELECT * FROM Users;" > users.csv
sqlite3 -header -csv shortnr.db "SELECT * FROM Domains;" > domains.csv
```

### Step 2: Set up target database

**PostgreSQL:**

```bash
createdb shortnr
psql shortnr -c "CREATE USER shortnr WITH PASSWORD 'your-password';"
psql shortnr -c "GRANT ALL PRIVILEGES ON DATABASE shortnr TO shortnr;"
```

**MySQL:**

```bash
mysql -e "CREATE DATABASE shortnr;"
mysql -e "CREATE USER 'shortnr'@'%' IDENTIFIED BY 'your-password';"
mysql -e "GRANT ALL PRIVILEGES ON shortnr.* TO 'shortnr'@'%';"
```

### Step 3: Start shortnr with target database

Start a new shortnr instance pointing at the target database. EF Core migrations will create the schema automatically:

```bash
# PostgreSQL
DATABASE__PROVIDER=Postgres \
DATABASE__CONNECTIONSTRING="Host=localhost;Database=shortnr;Username=shortnr;Password=your-password" \
dotnet run --project src/Shortnr.Web

# MySQL
DATABASE__PROVIDER=MySql \
DATABASE__CONNECTIONSTRING="Server=localhost;Database=shortnr;User=shortnr;Password=your-password" \
dotnet run --project src/Shortnr.Web
```

### Step 4: Import data

The schema differs slightly between providers (column types, auto-increment semantics). Use a migration script or tool to transform and import the data.

**Example: Import CSV to PostgreSQL**

```bash
# After starting shortnr to create the schema, import CSV data
psql shortnr -c "\COPY \"ShortenedUrls\" (\"Id\", \"LongUrl\", \"ShortCode\", \"CreatedAtUtc\", \"ClickCount\", \"OwnerUserId\", \"DomainId\", \"WorkspaceId\") FROM 'urls.csv' WITH CSV HEADER"
```

**Important:** Preserve the original `Id` values to maintain referential integrity. Import in dependency order:
1. `Users`
2. `Domains`
3. `Workspaces`
4. `ShortenedUrls`
5. `ClickEvents`
6. Other tables with foreign keys

### Step 5: Verify

After importing, verify the data:

```bash
# Check row counts
psql shortnr -c "SELECT COUNT(*) FROM \"ShortenedUrls\";"
psql shortnr -c "SELECT COUNT(*) FROM \"ClickEvents\";"

# Test a redirect
curl -I http://localhost:8080/your-short-code
```

### Step 6: Switch production traffic

Once verified:
1. Stop the old SQLite instance
2. Update DNS/load balancer to point to the new instance
3. Monitor for errors

## Provider-specific notes

### SQLite → PostgreSQL

- SQLite `TEXT` columns become `text` or `varchar(n)` in Postgres
- SQLite `INTEGER` primary keys become `bigint` in Postgres
- SQLite's `datetime('now')` defaults are handled in C# (provider-agnostic)
- Filtered indexes (`[DomainId] IS NULL`) work identically in Postgres

### SQLite → MySQL

- MySQL does not support filtered indexes. shortnr uses a composite unique index instead
- MySQL `AUTO_INCREMENT` vs SQLite `AUTOINCREMENT` — handled by EF Core
- MySQL requires explicit `CHARSET=utf8mb4` for full Unicode support (set by EF Core)
- Case sensitivity: MySQL collation affects string comparisons. Use `utf8mb4_bin` for case-sensitive lookups

### PostgreSQL ↔ MySQL

- Timestamps: Both use UTC. No conversion needed.
- Boolean: Postgres has native `boolean`; MySQL uses `tinyint(1)`. EF Core handles the mapping.
- JSON: If using JSON columns (future feature), Postgres has `jsonb`; MySQL has `json`.

## Troubleshooting

**"Pending model changes" error on startup**

Ensure migrations are up to date:

```bash
dotnet ef database update --project src/Shortnr.Data
```

**Unique constraint violations on import**

Check for duplicate `ShortCode` values within the same `DomainId`. The uniqueness constraint is per-domain.

**Timestamps are wrong**

All timestamps are stored as UTC. If displayed incorrectly, check the client timezone settings.

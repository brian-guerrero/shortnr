---
title: Database migration
description: Move an existing shortnr instance from SQLite to PostgreSQL.
order: 5
---

# Database migration

shortnr supports SQLite and PostgreSQL. This guide covers moving an existing instance's data from SQLite to Postgres.

For choosing between the two in the first place, see the [database guide](/shortnr/docs/configuration/database/).

## When to migrate

**Stay on SQLite if:**
- Single-instance deployment
- Low write volume (under ~100 clicks/minute)
- Simplicity is preferred over scalability

**Move to PostgreSQL if:**
- You need to run more than one instance behind a load balancer &mdash; the deciding factor, since replicas can't share a SQLite file
- Higher write volume (100+ clicks/minute)
- You already operate Postgres infrastructure

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

```bash
createdb shortnr
psql shortnr -c "CREATE USER shortnr WITH PASSWORD 'your-password';"
psql shortnr -c "GRANT ALL PRIVILEGES ON DATABASE shortnr TO shortnr;"
```

### Step 3: Start shortnr with target database

Start a new shortnr instance pointing at the target database. EF Core migrations will create the schema automatically:

```bash
Database__Provider=Postgres \
Database__ConnectionString="Host=localhost;Database=shortnr;Username=shortnr;Password=your-password" \
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
- Postgres folds unquoted identifiers to lowercase, and shortnr's tables and columns are PascalCase &mdash; quote them (`"ShortenedUrls"`) in every hand-written query
- The two providers keep separate migration histories (`Shortnr.Data` for SQLite, `Shortnr.Data.Postgres` for Postgres); both ship in the published output and the right one is picked from `Database__Provider`

### Reset identity sequences after import

Imported rows carry their original `Id` values, but the sequences backing those columns still start at 1 &mdash; so the first insert after cutover collides with an existing row. Reset each one:

```bash
psql shortnr -c "SELECT setval(pg_get_serial_sequence('\"ShortenedUrls\"', 'Id'), COALESCE(MAX(\"Id\"), 1)) FROM \"ShortenedUrls\";"
```

Repeat for every table you imported.

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

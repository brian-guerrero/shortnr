---
title: Database
description: Choose between SQLite and PostgreSQL, configure the provider and connection string, and migrate an existing SQLite instance to Postgres.
order: 4
---

# Database

shortnr ships with two supported database providers: **SQLite** (the zero-config default) and **PostgreSQL** (for multi-instance deployments). Both run the same schema and the same feature set &mdash; the provider is a deployment choice, not a functional one.

## Which provider should I use?

| | SQLite | PostgreSQL |
|---|---|---|
| Setup | None &mdash; a single file | A server or managed instance |
| Instances | One | Many (horizontal scale) |
| Concurrency | Concurrent reads, serialized writes | MVCC, concurrent writes |
| Backups | `cp` the `.db` file | `pg_dump` / managed snapshots |
| Best for | Personal, single-user, small teams | Multi-user, multi-replica, higher write volume |

**Stay on SQLite** unless you have a concrete reason to move. It handles far more traffic than most self-hosted instances ever see, and click writes are batched through a background channel rather than hitting the database per redirect.

**Move to PostgreSQL when** you need to run more than one instance of shortnr behind a load balancer. This is the deciding factor: SQLite is a local file, so a second replica cannot share it. Higher sustained write volume and existing Postgres infrastructure are secondary reasons.

## Configuration

Two settings control the database. Environment variables use `__` as the hierarchy separator, and keys are case-insensitive &mdash; `DATABASE__PROVIDER` and `Database__Provider` are equivalent.

| Setting | Default | Description |
|---------|---------|-------------|
| `Database__Provider` | `Sqlite` | `Sqlite` or `Postgres`. Also accepts `PostgreSQL` / `Npgsql`. Case-insensitive. |
| `Database__ConnectionString` | *(empty)* | Connection string for the selected provider. |

If `Database__ConnectionString` is unset and the provider is `Postgres`, startup fails with `No connection string configured for database provider 'Postgres'`. SQLite falls back to `Data Source=shortnr.db` in the working directory.

### SQLite

The default. Nothing to configure &mdash; but in Docker you should point it at a mounted volume so the file survives container recreation:

```bash
docker run -p 8080:8080 \
  -v shortnr-data:/data \
  -e Database__ConnectionString="Data Source=/data/shortnr.db" \
  ghcr.io/brian-guerrero/shortnr:latest
```

### PostgreSQL

Set both the provider and the connection string:

```bash
docker run -p 8080:8080 \
  -e Database__Provider=Postgres \
  -e Database__ConnectionString="Host=postgres;Database=shortnr;Username=shortnr;Password=secret" \
  ghcr.io/brian-guerrero/shortnr:latest
```

Or bring up shortnr and Postgres together with the bundled Compose file:

```bash
docker compose -f docker-compose.postgres.yml up -d
```

From source:

```bash
Database__Provider=Postgres \
Database__ConnectionString="Host=localhost;Database=shortnr;Username=shortnr;Password=secret" \
dotnet run --project src/Shortnr.Web
```

For local development, the Aspire AppHost can provision a Postgres container for you:

```bash
dotnet run --project src/Shortnr.AppHost -- db-provider=Postgres
```

## Schema and migrations

The schema is created and updated automatically at startup &mdash; shortnr runs EF Core migrations on boot, so a fresh database needs no manual setup beyond creating an empty database and a user.

Each provider has its own migration history, in its own assembly:

- **SQLite** &mdash; `src/Shortnr.Data/Migrations/`
- **PostgreSQL** &mdash; `src/Shortnr.Data.Postgres/Migrations/`

They are deliberately separate. A migration replays its recorded operations verbatim, so a history scaffolded against SQLite cannot produce correct Postgres-native DDL (column types, identity generation). Both assemblies ship in the published output; the right one is selected at runtime from `Database__Provider`.

## Migrating from SQLite to PostgreSQL

There is no in-place conversion. The process is: stand up the Postgres schema, copy the rows across, verify, then cut traffic over.

Plan for a short write freeze &mdash; links created against SQLite after you export are not copied.

### 1. Create the database and user

```bash
createdb shortnr
psql shortnr -c "CREATE USER shortnr WITH PASSWORD 'your-password';"
psql shortnr -c "GRANT ALL PRIVILEGES ON DATABASE shortnr TO shortnr;"
```

### 2. Create the schema

Start shortnr once against the empty Postgres database. Migrations run on startup and build the full schema:

```bash
Database__Provider=Postgres \
Database__ConnectionString="Host=localhost;Database=shortnr;Username=shortnr;Password=your-password" \
dotnet run --project src/Shortnr.Web
```

Stop it once the app is listening. The database now has empty tables.

### 3. Export from SQLite

Stop the SQLite instance first so the export is a consistent snapshot:

```bash
sqlite3 -header -csv shortnr.db "SELECT * FROM Users;" > users.csv
sqlite3 -header -csv shortnr.db "SELECT * FROM Domains;" > domains.csv
sqlite3 -header -csv shortnr.db "SELECT * FROM Workspaces;" > workspaces.csv
sqlite3 -header -csv shortnr.db "SELECT * FROM ShortenedUrls;" > urls.csv
sqlite3 -header -csv shortnr.db "SELECT * FROM ClickEvents;" > clicks.csv
```

### 4. Import in dependency order

Foreign keys mean order matters. Import parents before children, and preserve the original `Id` values so references stay intact:

1. `Users`
2. `Domains`
3. `Workspaces` / `WorkspaceMembers`
4. `ShortenedUrls`
5. `ClickEvents`
6. Remaining tables (`BioPages`, `ApiKeys`, `Webhooks`, …)

```bash
psql shortnr -c "\COPY \"Users\" FROM 'users.csv' WITH CSV HEADER"
psql shortnr -c "\COPY \"ShortenedUrls\" FROM 'urls.csv' WITH CSV HEADER"
```

Postgres identifiers are case-sensitive when quoted, and shortnr's tables and columns are PascalCase &mdash; the double quotes above are required.

### 5. Reset identity sequences

This step is easy to miss and breaks the first write after cutover. Copied rows carry explicit `Id` values, but the sequences behind those columns still start at 1, so the next insert collides:

```bash
psql shortnr -c "SELECT setval(pg_get_serial_sequence('\"ShortenedUrls\"', 'Id'), COALESCE(MAX(\"Id\"), 1)) FROM \"ShortenedUrls\";"
```

Repeat for each table you imported.

### 6. Verify, then cut over

```bash
psql shortnr -c "SELECT COUNT(*) FROM \"ShortenedUrls\";"
psql shortnr -c "SELECT COUNT(*) FROM \"ClickEvents\";"
```

Compare the counts against the SQLite source, start shortnr against Postgres, and test a real redirect before repointing DNS or your load balancer:

```bash
curl -I http://localhost:8080/your-short-code
```

Keep the SQLite file until you're satisfied &mdash; it's your rollback.

## Troubleshooting

**`Unsupported 'Database:Provider' value`** &mdash; the provider name isn't recognised. Valid values are `Sqlite` and `Postgres` (plus the `PostgreSQL` / `Npgsql` aliases).

**`No connection string configured for database provider 'Postgres'`** &mdash; set `Database__ConnectionString`. Postgres has no default.

**Duplicate key errors on the first write after a migration** &mdash; identity sequences weren't reset. See step 5 above.

**Unique constraint violations while importing** &mdash; short codes are unique *per domain*, not globally. Check for duplicate `ShortCode` values sharing the same `DomainId`.

## Related

- [Configuration reference](/shortnr/docs/configuration/) &mdash; all other settings
- [Self-hosting guide](/shortnr/docs/self-hosting/) &mdash; production deployment and backups

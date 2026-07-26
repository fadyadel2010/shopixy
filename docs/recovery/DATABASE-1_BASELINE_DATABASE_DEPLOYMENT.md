# DATABASE-1 Baseline Database Deployment

## Executive Summary

Deployment target: `PosEcommerceDb_Baseline`

The clean PostgreSQL database was created from the certified canonical baseline path:

`Runtime Model` -> `20260723214642_Baseline_Recovery3` -> `PosEcommerceDb_Baseline`

Final validation confirms:

- EF design-time configuration targets `PosEcommerceDb_Baseline`.
- The baseline migration applied successfully.
- `__EFMigrationsHistory` exists.
- `__EFMigrationsHistory` contains exactly one migration: `20260723214642_Baseline_Recovery3`.
- EF reports no pending model changes.
- Code inventory contains exactly one EF migration and one model snapshot.

Final Certification: `DEPLOYED WITH OBSERVATIONS`

## Connection String Discovery

Active design-time factory:

- File: `PointOfSale.Infrastructure/Data/ApplicationDbContextFactory.cs`
- Configuration load order:
  - `PointOfSale.API/appsettings.json`
  - `PointOfSale.API/appsettings.Development.json`
  - `PointOfSale.API/appsettings.Development.local.json`
  - dotnet user-secrets for `UserSecretsId=bbdf2e74-8790-4753-8874-f20fcfbdbbd5`
  - process environment variable `ConnectionStrings__DefaultConnection`, if present

Active API runtime:

- File: `PointOfSale.API/Program.cs`
- Configuration load order includes the same JSON files, shared user-secrets, and environment variables.

Environment override check:

- `ConnectionStrings__DefaultConnection`: not set for Process/User/Machine.
- `ASPNETCORE_ENVIRONMENT`: not set for Process/User/Machine.
- `DOTNET_ENVIRONMENT`: not set for Process/User/Machine.
- Effective environment fallback: `Development`.

Connection string source:

- Active source: dotnet user-secrets.
- Secret store file: `%APPDATA%/Microsoft/UserSecrets/bbdf2e74-8790-4753-8874-f20fcfbdbbd5/secrets.json`
- Lower-priority local JSON also contains `DefaultConnection`, but user-secrets overrides it in Development.

Discovered current database before update:

- Host: `192.168.1.5`
- Port: `5432`
- Database: `PosEcommerceDb`
- Username: `postgres`
- Password: present, redacted in this report

Initial EF design-time evidence:

```text
Provider name: Npgsql.EntityFrameworkCore.PostgreSQL
Database name: PosEcommerceDb
Data source: tcp://192.168.1.5:5432
```

## Database Creation

Source database metadata:

```text
datname=PosEcommerceDb
owner=postgres
encoding=UTF8
collation=en_US.UTF-8
ctype=en_US.UTF-8
tablespace=pg_default
locale_provider=c
locale=
```

Creation action:

- Created database: `PosEcommerceDb_Baseline`
- Creation method: PostgreSQL `CREATE DATABASE` from `template0`
- Existing database was not deleted or modified.

Target database metadata after creation:

```text
datname=PosEcommerceDb_Baseline
owner=postgres
encoding=UTF8
collation=en_US.UTF-8
ctype=en_US.UTF-8
tablespace=pg_default
locale_provider=c
locale=
```

The target database matches the existing development database for host, port, username, password, owner, encoding, collation, locale provider, and tablespace.

## Connection Validation

Updated only the active connection string source: dotnet user-secrets.

Changed only:

```text
Database=PosEcommerceDb
```

to:

```text
Database=PosEcommerceDb_Baseline
```

EF design-time validation after update:

```text
Provider name: Npgsql.EntityFrameworkCore.PostgreSQL
Database name: PosEcommerceDb_Baseline
Data source: tcp://192.168.1.5:5432
```

## EF Deployment

Command:

```powershell
dotnet ef database update `
  --project .\PointOfSale.Infrastructure.csproj `
  --startup-project ..\PointOfSale.API\PointOfSale.API.csproj `
  --context PointOfSale.Infrastructure.Data.ApplicationDbContext
```

Result:

```text
Build succeeded.
Applying migration '20260723214642_Baseline_Recovery3'.
Done.
```

Observation:

- EF logged a non-terminal failed `SELECT` against `__EFMigrationsHistory` before the table existed.
- The migration command exited successfully and created the history table as part of normal baseline deployment.
- No terminal SQL or PostgreSQL error remained after deployment validation.

## Migration History Validation

Direct database validation:

```text
history_table_exists=True
migration_count=1
migrations=20260723214642_Baseline_Recovery3
```

EF migration inventory:

```text
20260723214642_Baseline_Recovery3
```

Migration files present:

```text
20260723214642_Baseline_Recovery3.cs
20260723214642_Baseline_Recovery3.Designer.cs
ApplicationDbContextModelSnapshot.cs
```

Pending model changes:

```text
No changes have been made to the model since the last migration.
```

## Schema Statistics

Target database: `PosEcommerceDb_Baseline`

```text
tables=257
views=0
sequences=0
indexes=1130
foreign_keys=265
check_constraints=243
```

## Deployment Risks

- `PointOfSale.API/appsettings.Development.local.json` still contains the older `Database=PosEcommerceDb` value. This was intentionally not changed because dotnet user-secrets is the active provider and the mission required changing only the active connection string.
- EF emitted a non-terminal failed history-table probe before baseline creation. Final direct validation proves the history table exists and contains exactly the certified baseline migration.
- The deployment depends on the shared user-secrets entry remaining present for Development design-time/runtime execution.

## Final Certification

`DEPLOYED WITH OBSERVATIONS`

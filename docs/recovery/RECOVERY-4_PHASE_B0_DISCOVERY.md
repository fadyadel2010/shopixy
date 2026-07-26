# RECOVERY-4 Phase B0 Discovery

## Executive Summary

Phase B0 discovered the canonical EF Core command line from repository source and validated it with the permitted EF commands only.

The canonical EF target project is `PointOfSale.Infrastructure`. The canonical startup project is `PointOfSale.API`. Design-time creation uses `ApplicationDbContextFactory`, an explicit `IDesignTimeDbContextFactory<ApplicationDbContext>`.

Validation succeeded after EF was allowed to complete its startup-project build with NuGet access. However, the validation also proved that an active legacy EF migration chain still exists under `PointOfSale.Infrastructure/Data/Migrations`. Therefore the repository is not yet ready for Greenfield Baseline generation. Generating now would not produce an exactly-one-active-migration state.

No migration was generated. No snapshot was generated. No source code, project file, launch settings, runtime model, DbContext, or configuration file was edited.

## DbContext Discovery

Primary EF Core DbContext:

`PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`

Type:

`PointOfSale.Infrastructure.Data.ApplicationDbContext`

Evidence:

- `ApplicationDbContext` inherits `Microsoft.EntityFrameworkCore.DbContext`.
- It is in `PointOfSale.Infrastructure`.
- It loads entity configurations through `modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly)`.
- `PointOfSale.API/Program.cs` registers it with `builder.Services.AddDbContext<ApplicationDbContext>(...)`.
- Runtime provider registration uses `options.UseNpgsql(defaultConnectionString)`.

Multiple DbContext status:

- One EF Core `DbContext` subclass was found: `ApplicationDbContext`.
- `MongoDbContext` exists, but it is not an EF Core `DbContext`; it wraps MongoDB `IMongoDatabase`.

## Startup Project Discovery

Canonical startup project:

`PointOfSale.API/PointOfSale.API.csproj`

Reason:

- It is an SDK web project.
- It references `PointOfSale.Infrastructure`.
- It registers `ApplicationDbContext`.
- It configures Npgsql.
- It contains the application configuration files consumed by the design-time factory.

Multiple startup project status:

- `PointOfSale/PointOfSale.csproj` is also an SDK web project with launch settings, but it does not reference `PointOfSale.Infrastructure` and does not register `ApplicationDbContext`.
- `PointOfSale.API` is therefore the canonical EF startup project.

## Design-Time Factory

Factory exists:

`PointOfSale.Infrastructure/Data/ApplicationDbContextFactory.cs`

Type:

`PointOfSale.Infrastructure.Data.ApplicationDbContextFactory`

Interface:

`IDesignTimeDbContextFactory<ApplicationDbContext>`

Creation path:

- Reads `ASPNETCORE_ENVIRONMENT`, then `DOTNET_ENVIRONMENT`, defaulting to `Development`.
- Builds configuration from `../PointOfSale.API/appsettings.json`, environment appsettings, and optional local appsettings.
- Adds user secrets in Development.
- Reads `ConnectionStrings__DefaultConnection` from environment first, otherwise `DefaultConnection` from configuration.
- Normalizes the connection string with `PostgresConnectionStringPolicy.Normalize`.
- Creates `DbContextOptionsBuilder<ApplicationDbContext>`.
- Calls `UseNpgsql(connectionString)`.
- Returns `new ApplicationDbContext(optionsBuilder.Options)`.

Important command-location implication:

The factory uses `Directory.GetCurrentDirectory()` with `../PointOfSale.API`. The validated command was run from `PointOfSale.Infrastructure`, where that relative path resolves to the API project.

## Project Relationships

Target project:

`PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj`

Target framework:

`net9.0`

Project references:

- `PointOfSale.Core/PointOfSale.Core.csproj`

EF package evidence:

- `Microsoft.EntityFrameworkCore.Design` version `9.0.1`
- `Microsoft.EntityFrameworkCore.Tools` version `9.0.1`
- `Npgsql.EntityFrameworkCore.PostgreSQL` version `9.0.2`

Startup project:

`PointOfSale.API/PointOfSale.API.csproj`

Target framework:

`net9.0`

Project references:

- `PointOfSale.Core/PointOfSale.Core.csproj`
- `PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj`

Tooling:

- `.config/dotnet-tools.json` pins `dotnet-ef` version `9.0.1` with `rollForward: false`.

## EF CLI Commands

Canonical working directory:

`PointOfSale.Infrastructure`

Canonical `dbcontext info` command:

```powershell
dotnet ef dbcontext info --project .\PointOfSale.Infrastructure.csproj --startup-project ..\PointOfSale.API\PointOfSale.API.csproj --context PointOfSale.Infrastructure.Data.ApplicationDbContext
```

Canonical `migrations list` command:

```powershell
dotnet ef migrations list --project .\PointOfSale.Infrastructure.csproj --startup-project ..\PointOfSale.API\PointOfSale.API.csproj --context PointOfSale.Infrastructure.Data.ApplicationDbContext
```

Canonical `migrations add` command for the approved baseline name:

```powershell
dotnet ef migrations add Baseline_Recovery3 --project .\PointOfSale.Infrastructure.csproj --startup-project ..\PointOfSale.API\PointOfSale.API.csproj --context PointOfSale.Infrastructure.Data.ApplicationDbContext --output-dir Migrations
```

Do not run the `migrations add` command until `PointOfSale.Infrastructure/Data/Migrations` is also archived out of the compiled project path and `migrations list` returns no active migrations.

## Validation Results

Command executed from `PointOfSale.Infrastructure`:

```powershell
dotnet ef dbcontext info --project .\PointOfSale.Infrastructure.csproj --startup-project ..\PointOfSale.API\PointOfSale.API.csproj --context PointOfSale.Infrastructure.Data.ApplicationDbContext
```

Initial result:

- Failed during EF's startup-project build.
- Root cause: package restore/build attempted to reach `https://api.nuget.org` and failed with `NU1301` SSL/auth errors inside the sandbox.

Escalated result:

- Passed.
- EF found `ApplicationDbContext`.
- EF used `ApplicationDbContextFactory`.
- Provider: `Npgsql.EntityFrameworkCore.PostgreSQL`.
- Database name: `PosEcommerceDb`.
- Data source: `tcp://192.168.1.5:5432`.

Command executed from `PointOfSale.Infrastructure`:

```powershell
dotnet ef migrations list --project .\PointOfSale.Infrastructure.csproj --startup-project ..\PointOfSale.API\PointOfSale.API.csproj --context PointOfSale.Infrastructure.Data.ApplicationDbContext
```

Result:

- Passed.
- EF listed 38 migrations from `PointOfSale.Infrastructure/Data/Migrations`.
- Latest listed migration: `20260419175541_AddCountryToCheckoutSessions`.
- Latest listed migration status: `Pending`.

## Failure Analysis

The previous Phase B root command failed because the repository root is a solution directory, not a project directory. Running plain `dotnet ef ...` from the root gives:

`No project was found. Change the current working directory or use the --project option.`

The discovered command fixes that by specifying:

- `--project .\PointOfSale.Infrastructure.csproj`
- `--startup-project ..\PointOfSale.API\PointOfSale.API.csproj`
- `--context PointOfSale.Infrastructure.Data.ApplicationDbContext`

The first discovered validation attempt then failed for a different reason: EF invoked a startup-project build and the sandboxed process could not retrieve package metadata from NuGet. With network access approved for the same EF command, the command succeeded.

Critical repository-state finding:

`PointOfSale.Infrastructure/Migrations` is empty, but `PointOfSale.Infrastructure/Data/Migrations` remains inside the compiled infrastructure project. EF discovers that legacy directory and lists 38 migrations. File inventory shows 40 migration class files, 38 designer files, 12 SQL files, and no active model snapshot in that legacy directory. Two class-only migration files are present but are not listed by EF because they lack generated designer metadata with `[Migration(...)]`.

Class-only files not listed by EF:

- `20260117000000_AddPromotionSearchBoostOverride.cs`
- `20260208180000_BlockLegacyCashboxVisaWrites.cs`

## Canonical Invocation

Use this working directory:

`C:\Users\fadya\source\repos\PointOfSale\PointOfSale.Infrastructure`

Use this baseline generation command only after all active old migration sources, including `Data/Migrations`, have been archived:

```powershell
dotnet ef migrations add Baseline_Recovery3 --project .\PointOfSale.Infrastructure.csproj --startup-project ..\PointOfSale.API\PointOfSale.API.csproj --context PointOfSale.Infrastructure.Data.ApplicationDbContext --output-dir Migrations
```

Then validate with:

```powershell
dotnet ef dbcontext info --project .\PointOfSale.Infrastructure.csproj --startup-project ..\PointOfSale.API\PointOfSale.API.csproj --context PointOfSale.Infrastructure.Data.ApplicationDbContext
dotnet ef migrations list --project .\PointOfSale.Infrastructure.csproj --startup-project ..\PointOfSale.API\PointOfSale.API.csproj --context PointOfSale.Infrastructure.Data.ApplicationDbContext
```

## Next Phase

Before Phase B Greenfield Baseline generation can safely resume:

1. Archive `PointOfSale.Infrastructure/Data/Migrations` out of the compiled project path.
2. Preserve its history in the recovery archive.
3. Re-run the canonical `migrations list` command.
4. Proceed only if EF lists no active migrations.

Current state is discovery-complete but generation-blocked.

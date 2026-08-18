# SHOPIXY Runtime Database Schema Drift Forensic Audit

Date: 2026-08-12

Scope: audit only. No source, test, configuration, secret, persistent environment, schema, migration, data, git, frontend, or runtime repair changes were made. The only created artifact is this report.

## 1. Executive Summary

The currently running API is configured to use PostgreSQL database `PosEcommerceDb_Baseline` on host `192.168.1.5`, port `5432`, user `postgres`, schema `public`.

The current EF model expects `public."CarrierConfigurations"."CredentialConfigurationFingerprint"` because the property exists in `CarrierConfiguration`, is configured in `ApplicationDbContext`, and is present in `ApplicationDbContextModelSnapshot`.

The runtime database does not contain that column. The live database also does not contain the sibling Step 2N column `LastValidatedConfigurationFingerprint`, does not contain `Orders.ShippingServiceLevel`, does not contain `CK_ShippingMethods_ActiveRequiresFulfillmentWarehouse`, and still has `CarrierConfigurations.WebhookSecret` as `character varying(256)` rather than the Step 2N `character varying(500)` shape.

The responsible source migration exists:

`20260811215156_SHOPIXY_ShippingClosureRemediationStep2N`

The runtime database `__EFMigrationsHistory` table exists, contains 17 migrations, and its latest migration is:

`20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority`

The fingerprint migration is absent from `__EFMigrationsHistory`.

Classification: CASE 1, normal pending migration.

Source has the fingerprint migration. Runtime DB history does not. Runtime DB schema does not.

## 2. Exact Startup Failure

Observed failure:

`Npgsql.PostgresException 42703: column c.CredentialConfigurationFingerprint does not exist`

Observed call path:

`Program -> CarrierWebhookSecretBackfillHostedService.StartAsync() -> ProtectLegacySecretsAsync() -> ApplicationDbContext CarrierConfiguration query -> PostgreSQL -> missing column`

The startup hosted service is registered in `PointOfSale.API/Program.cs` with:

`AddHostedService<PointOfSale.API.Services.Shipping.CarrierWebhookSecretBackfillHostedService>()`

The service executes `ProtectLegacySecretsAsync()` during `StartAsync()`. That method queries:

`dbContext.Set<CarrierConfiguration>().IgnoreQueryFilters().Where(item => item.WebhookSecret != null && item.WebhookSecret != string.Empty).ToListAsync(...)`

Because the EF entity is materialized, EF queries mapped columns for `CarrierConfiguration`, including `CredentialConfigurationFingerprint`. PostgreSQL rejects the generated SQL because the live table lacks that column.

## 3. Runtime Environment

Runtime environment resolution evidence:

- `ASPNETCORE_ENVIRONMENT=Development`
- `DOTNET_ENVIRONMENT` not set
- `ConnectionStrings__DefaultConnection` not set in the current process
- `ConnectionStrings:DefaultConnection` not set in the current process
- `REQUIRE_REAL_POSTGRES_TESTS` not set
- `launchSettings.json` profiles `http` and `https` both set `ASPNETCORE_ENVIRONMENT=Development`

`Program.cs` configuration load order:

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. `appsettings.{Environment}.local.json`
4. Development-only user-secrets
5. environment variables

`Program.cs` normalizes `ConnectionStrings:DefaultConnection`, writes the normalized value back into configuration, and passes the same `defaultConnectionString` variable to `UseNpgsql(defaultConnectionString)`.

Design-time factory evidence:

`ApplicationDbContextFactory` also resolves `ASPNETCORE_ENVIRONMENT` / `DOTNET_ENVIRONMENT`, loads the same API appsettings family, loads Development user-secrets, lets `ConnectionStrings__DefaultConnection` override JSON configuration, normalizes the connection string, and calls `UseNpgsql(connectionString)`.

## 4. Runtime Database Identity

Safe connection identity from effective Development configuration and user-secrets:

- Host: `192.168.1.5`
- Port: `5432`
- Database: `PosEcommerceDb_Baseline`
- Username: `postgres`
- Password: redacted
- Source: `ConnectionStrings:DefaultConnection` from Development local configuration and Development user-secrets; no current process-level connection-string override was present.

Live PostgreSQL identity using the same runtime connection:

- PostgreSQL version: `PostgreSQL 18.4 (Ubuntu 18.4-1.pgdg22.04+1)`, 64-bit
- `current_database()`: `PosEcommerceDb_Baseline`
- `current_schema()`: `public`
- `current_user`: `postgres`
- `inet_server_addr()`: `192.168.1.5/32`
- `inet_server_port()`: `5432`
- `public` schema exists: true

## 5. CarrierConfiguration Schema

Runtime schema inspection:

- `public."CarrierConfigurations"` exists: true
- `public."CarrierConfiguration"` exists: false
- `CredentialConfigurationFingerprint` exists on `CarrierConfigurations`: false
- `LastValidatedConfigurationFingerprint` exists on `CarrierConfigurations`: false
- Indexes involving `CredentialConfigurationFingerprint`: none
- Constraints involving `CredentialConfigurationFingerprint`: none

Relevant live `CarrierConfigurations` shape:

- Column count: 41
- `WebhookSecret`: `character varying(256)`, nullable
- `CredentialConfigurationFingerprint`: absent
- `LastValidatedConfigurationFingerprint`: absent

The live table matches the pre-Step-2N baseline shape for these fields.

Additional same-migration schema evidence:

- `Orders.ShippingServiceLevel`: absent
- `ShippingMethods.CK_ShippingMethods_ActiveRequiresFulfillmentWarehouse`: absent

## 6. EF Migration History

Runtime `__EFMigrationsHistory` evidence:

- Table exists: true
- Migration count: 17
- Latest migration ID: `20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority`
- Latest `ProductVersion`: `9.0.1`
- Fingerprint migration present: false

Latest 10 runtime DB migrations:

1. `20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority`
2. `20260811150644_SHOPIXY_WarehouseResolutionAuthority`
3. `20260810181420_DOMAIN_Final_ForensicHardening`
4. `20260810114400_SHOPIXY_CheckoutAttachmentFinalization`
5. `20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement`
6. `20260809203807_SHOPIXY_CheckoutAttachments`
7. `20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots`
8. `20260809185514_SHOPIXY_CheckoutFieldDefinitions`
9. `20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze`
10. `20260809090000_DOMAIN_4_3_DomainVerificationEngineDiagnostics`

Targeted history comparison:

- `20260811150644_SHOPIXY_WarehouseResolutionAuthority`: present
- `20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority`: present
- `20260811215156_SHOPIXY_ShippingClosureRemediationStep2N`: absent

## 7. Fingerprint Migration Evidence

Repository migration file:

`PointOfSale.Infrastructure/Migrations/20260811215156_SHOPIXY_ShippingClosureRemediationStep2N.cs`

Migration ID:

`20260811215156_SHOPIXY_ShippingClosureRemediationStep2N`

Exact `Up()` operations relevant to this audit:

- Adds `Orders.ShippingServiceLevel` as `character varying(50)`, nullable.
- Alters `CarrierConfigurations.WebhookSecret` from `character varying(256)` max length 256 to `character varying(500)` max length 500.
- Adds `CarrierConfigurations.CredentialConfigurationFingerprint` as `character varying(64)`, nullable.
- Adds `CarrierConfigurations.LastValidatedConfigurationFingerprint` as `character varying(64)`, nullable.
- Performs shipping method/order backfill SQL.
- Adds `CK_ShippingMethods_ActiveRequiresFulfillmentWarehouse`.

Exact `Down()` operations relevant to this audit:

- Drops `CK_ShippingMethods_ActiveRequiresFulfillmentWarehouse`.
- Drops `Orders.ShippingServiceLevel`.
- Drops `CarrierConfigurations.CredentialConfigurationFingerprint`.
- Drops `CarrierConfigurations.LastValidatedConfigurationFingerprint`.
- Reverts `CarrierConfigurations.WebhookSecret` to `character varying(256)` max length 256.

Source migration exists. Runtime history does not contain it. Runtime schema does not contain its column/constraint changes.

## 8. Current EF Model Evidence

Entity evidence:

- `CarrierConfiguration.CredentialConfigurationFingerprint` exists as `string?`.
- `CarrierConfiguration.LastValidatedConfigurationFingerprint` exists as `string?`.

Model configuration evidence:

- `ApplicationDbContext` configures `CredentialConfigurationFingerprint` with `.HasMaxLength(64)`.
- `ApplicationDbContext` configures `LastValidatedConfigurationFingerprint` with `.HasMaxLength(64)`.
- `ApplicationDbContext` configures `WebhookSecret` with `.HasMaxLength(500)`.

Snapshot evidence:

- `ApplicationDbContextModelSnapshot` contains `CredentialConfigurationFingerprint` with `HasMaxLength(64)` and PostgreSQL type `character varying(64)`.
- `ApplicationDbContextModelSnapshot` contains `LastValidatedConfigurationFingerprint`.

Current EF model expects the column.

## 9. Source-vs-Database Migration Comparison

Source migrations, ordered:

1. `20260723214642_Baseline_Recovery3`
2. `20260726153652_PosCartRuntime`
3. `20260726172030_PlatformCurrencyReferenceSeed`
4. `20260729120000_StorefrontProductUnitDbEnforcement`
5. `20260729150000_ProductAuthoringXminConcurrency`
6. `20260805215917_DOMAIN_2C_0_CreateDomainTables`
7. `20260805215918_DOMAIN_2C_DomainPersistence`
8. `20260809090000_DOMAIN_4_3_DomainVerificationEngineDiagnostics`
9. `20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze`
10. `20260809185514_SHOPIXY_CheckoutFieldDefinitions`
11. `20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots`
12. `20260809203807_SHOPIXY_CheckoutAttachments`
13. `20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement`
14. `20260810114400_SHOPIXY_CheckoutAttachmentFinalization`
15. `20260810181420_DOMAIN_Final_ForensicHardening`
16. `20260811150644_SHOPIXY_WarehouseResolutionAuthority`
17. `20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority`
18. `20260811215156_SHOPIXY_ShippingClosureRemediationStep2N`

Comparison:

| Item | Value |
| --- | --- |
| Source latest migration | `20260811215156_SHOPIXY_ShippingClosureRemediationStep2N` |
| Database latest migration | `20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority` |
| Fingerprint migration | `20260811215156_SHOPIXY_ShippingClosureRemediationStep2N` |
| Database status | Absent from `__EFMigrationsHistory` |
| Fingerprint column | Absent from `public."CarrierConfigurations"` |

## 10. Multiple Database Analysis

Repository/configuration search found the following likely PostgreSQL database names or roles:

- Runtime Development/Baseline DB: `PosEcommerceDb_Baseline`
- Legacy/default DB references: `PosEcommerceDb`
- Staging DB reference: `PosEcommerceDb_Staging`
- Example architecture DB: `PointOfSaleDB`
- Scratch/recovery references in documentation
- No project Docker/Kubernetes database wiring was found in active source; deployment docs describe externally provisioned dependencies.

Checked-in API appsettings state:

- `appsettings.json`: `DefaultConnection` empty
- `appsettings.Development.json`: `DefaultConnection` empty
- `appsettings.Development.local.json`: `Host=192.168.1.5;Port=5432;Database=PosEcommerceDb_Baseline;Username=postgres;Password=<redacted>`
- `appsettings.Test.json`: `DefaultConnection` empty
- `appsettings.Test.local.json`: `Host=192.168.1.5;Port=5432;Database=PosEcommerceDb_Baseline;Username=postgres;Password=<redacted>`
- `appsettings.Staging.json`: `DefaultConnection` empty
- `appsettings.Production.json`: `DefaultConnection` empty

User-secrets contain `ConnectionStrings:DefaultConnection` resolving to:

`Host=192.168.1.5;Port=5432;Database=PosEcommerceDb_Baseline;Username=postgres;Password=<redacted>`

Current process environment did not provide a competing connection string.

Conclusion: the API runtime database is not ambiguous in this shell. It is `PosEcommerceDb_Baseline` on `192.168.1.5:5432`. The failure is not explained by the API connecting to `PosEcommerceDb`, `PosEcommerceDb_Staging`, a scratch DB, Docker DB, or CloudNativePG DB.

## 11. Root Cause

Root cause: the confirmed runtime database is behind the current EF model by one source migration.

Specifically:

- Source: fingerprint migration exists.
- Runtime history: fingerprint migration absent.
- Runtime schema: fingerprint column absent.
- Runtime DB latest migration is immediately before the fingerprint migration.

This is not migration history/schema drift where history says applied but schema is missing. It is not a missing migration artifact. It is not a proven wrong-database condition. It is a normal pending migration state against the exact runtime database.

Primary classification: B / CASE 1, correct database but migration not applied.

Secondary finding: other Step 2N schema changes are also absent, proving the whole Step 2N migration is pending, not only the single fingerprint column.

## 12. Why the API Fails During Startup

Startup sequence:

1. `Program.cs` builds configuration for Development.
2. `Program.cs` resolves and normalizes `ConnectionStrings:DefaultConnection`.
3. `Program.cs` registers `ApplicationDbContext` with `UseNpgsql(defaultConnectionString)`.
4. `Program.cs` registers `CarrierWebhookSecretBackfillHostedService`.
5. Host startup invokes `CarrierWebhookSecretBackfillHostedService.StartAsync()`.
6. `StartAsync()` creates a scope and resolves `ApplicationDbContext`.
7. `ProtectLegacySecretsAsync()` queries `CarrierConfiguration` rows with non-empty `WebhookSecret`.
8. EF materializes the current `CarrierConfiguration` model, including `CredentialConfigurationFingerprint`.
9. PostgreSQL receives SQL referring to column alias `c."CredentialConfigurationFingerprint"`.
10. Runtime table `public."CarrierConfigurations"` does not contain that column.
11. PostgreSQL returns error 42703.
12. The hosted service fails during startup; because this is startup hosted service execution, API startup fails.

This is a migration application issue, not a startup ordering issue. The hosted service is expected to run during startup, but it requires the database schema to match the current EF model.

## 13. Risk Assessment

Risk level: high for startup availability, low ambiguity for diagnosis.

Observed risks:

- API cannot start while current code targets this runtime database without the Step 2N schema.
- Any other runtime path materializing `CarrierConfiguration` can hit the same missing-column failure.
- Step 2N contains additional schema changes beyond the fingerprint column; those are absent too.
- The runtime DB is exactly one migration behind the repository source head, so future code paths may also assume Step 2N objects.

Not observed:

- No evidence that `__EFMigrationsHistory` falsely claims Step 2N is applied.
- No evidence that `CredentialConfigurationFingerprint` exists under another table or schema.
- No evidence of a current process-level connection string overriding the baseline DB.

## 14. Exact Remediation Required

Remediation is outside this audit and was not performed.

The required remediation target is the pending migration relationship:

- Runtime DB: `192.168.1.5:5432 / PosEcommerceDb_Baseline`
- Missing source migration: `20260811215156_SHOPIXY_ShippingClosureRemediationStep2N`
- Required resulting evidence after controlled remediation:
  - `__EFMigrationsHistory` contains `20260811215156_SHOPIXY_ShippingClosureRemediationStep2N`
  - `public."CarrierConfigurations"."CredentialConfigurationFingerprint"` exists as `character varying(64)`, nullable
  - `public."CarrierConfigurations"."LastValidatedConfigurationFingerprint"` exists as `character varying(64)`, nullable
  - `public."CarrierConfigurations"."WebhookSecret"` is `character varying(500)`, nullable
  - `public."Orders"."ShippingServiceLevel"` exists as `character varying(50)`, nullable
  - `public."ShippingMethods"` has `CK_ShippingMethods_ActiveRequiresFulfillmentWarehouse`

No migration was applied during this audit.

## 15. Verification Plan After Remediation

After a separate controlled remediation, verify read-only:

1. Query `__EFMigrationsHistory` for `20260811215156_SHOPIXY_ShippingClosureRemediationStep2N`.
2. Query `information_schema.columns` for both fingerprint columns on `public."CarrierConfigurations"`.
3. Query `information_schema.columns` for `Orders.ShippingServiceLevel`.
4. Query `pg_constraint` for `CK_ShippingMethods_ActiveRequiresFulfillmentWarehouse`.
5. Confirm `CarrierConfigurations.WebhookSecret` max length is 500.
6. Start the API and confirm `CarrierWebhookSecretBackfillHostedService.StartAsync()` no longer fails with PostgreSQL 42703.
7. Confirm no secrets are logged during startup.

## 16. Worktree Safety Evidence

Baseline captured before inspection:

- `git status --short`: no output
- `git diff --stat`: no output

Safety during audit:

- No `dotnet ef database update` was run.
- No migrations were applied.
- No `ALTER TABLE`, `CREATE`, `DROP`, `INSERT`, `UPDATE`, or `DELETE` statements were run.
- Database queries were read-only catalog/history/count inspections.
- No source, test, configuration, secret, persistent environment, schema, frontend, git, commit, or push changes were made.
- A process-scoped `PGPASSWORD` value was set only for the `psql` child command and restored afterward; no persistent environment variable was changed.
- The only intended file modification is this report.

## 17. Final Certification

DIAGNOSIS CONFIRMED — SAFE TO REMEDIATE

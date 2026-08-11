# DOMAIN Final Forensic Hardening Migration Certification

## 1. Root Cause

The reported runtime failure was:

`42703: column d.CertificateOperationFencingToken does not exist`

For the database identified by the API development configuration, the cause is not an unapplied migration. EF Core migration history and direct PostgreSQL schema inspection both show `20260810181420_DOMAIN_Final_ForensicHardening` is already applied and `Domains.CertificateOperationFencingToken` exists.

Most likely remaining explanations are a stale API process, a different runtime connection target than the development target inspected here, or a previously fixed local database state.

## 2. Affected Database

Database: `PosEcommerceDb_Baseline`

Host: `192.168.1.5`

Port: `5432`

Environment: `Development`

Connection source: `PointOfSale.API/appsettings.Development.local.json`, normalized through `Program.cs` via `ConnectionStrings:DefaultConnection`.

Secrets/passwords were not included in this report.

## 3. Pre-Migration State

`dotnet ef migrations list --project PointOfSale.Infrastructure --startup-project PointOfSale.API --context ApplicationDbContext --no-build --json` reported all known migrations as applied.

Latest applied migration:

`20260810181420_DOMAIN_Final_ForensicHardening`

Pending migration status:

`20260810181420_DOMAIN_Final_ForensicHardening` was not pending.

Migrations after target:

None.

Migration history consistency:

Consistent for the migration set returned by EF.

## 4. Migration Inspected

Inspected:

`PointOfSale.Infrastructure/Migrations/20260810181420_DOMAIN_Final_ForensicHardening.cs`

The migration adds:

- `Domains.VerificationTokenIssuedAtUtc`
- `Domains.CertificateOperationFencingToken bigint NOT NULL DEFAULT 0`
- `AK_SslCertificates_TenantId_DomainId_Id`
- `IX_SslCertificates_TenantId_DomainId_ReplacementCertificateId`
- `UQ_DomainVerifications_DomainId_Attempt`
- `IX_Domains_TenantId_RedirectTargetDomainId`
- `CK_Domains_Primary_Type_State_Equivalent`
- `CK_Domains_CertificateOperationFencingToken_NonNegative`
- hardened composite FKs for redirect target, certificate operation certificate reference, and replacement certificate reference

The `Up` path drops and recreates FKs/indexes/check constraints, adds two columns, and runs a non-destructive backfill for `VerificationTokenIssuedAtUtc`.

## 5. Migration Applied

No migration was applied.

Reason:

The mutation gate failed safely because the target migration was already applied according to EF history and direct schema inspection. Running `dotnet ef database update 20260810181420_DOMAIN_Final_ForensicHardening` would not be the requested pending-migration remediation.

## 6. Schema Changes

No schema changes were made during this task.

Existing verified schema state:

- `Domains.CertificateOperationFencingToken`: exists, `bigint`, `NOT NULL`, default `0`
- `Domains.VerificationTokenIssuedAtUtc`: exists, `timestamp with time zone`, nullable
- `AK_SslCertificates_TenantId_DomainId_Id`: exists
- `IX_SslCertificates_TenantId_DomainId_ReplacementCertificateId`: exists
- `UQ_DomainVerifications_DomainId_Attempt`: exists
- `IX_Domains_TenantId_RedirectTargetDomainId`: exists
- `CK_Domains_CertificateOperationFencingToken_NonNegative`: exists
- `CK_Domains_Primary_Type_State_Equivalent`: exists
- `CK_Domains_Redirect_Cascade`: exists
- `FK_Domains_RedirectTargetDomainId`: composite tenant/domain FK exists
- `FK_SslCertificateOperations_CertificateId`: composite tenant/domain/certificate FK exists
- `FK_SslCertificates_ReplacementCertificateId`: composite tenant/domain/replacement FK exists

## 7. Post-Migration Verification

Because no migration was applied, verification was performed against the current database state.

`__EFMigrationsHistory` contains:

`20260810181420_DOMAIN_Final_ForensicHardening`

EF pending model changes:

`dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure --startup-project PointOfSale.API --context ApplicationDbContext --no-build`

Result:

`No changes have been made to the model since the last migration.`

## 8. Data Invariant Results

Read-only invariant probes returned zero violations:

- no duplicate active certificate generations
- no duplicate in-progress certificate operations
- no duplicate certificate operation idempotency keys in scope
- no invalid primary domain state
- no cross-tenant certificate/domain relationships
- no invalid domain type values
- no orphan certificate operations
- no orphan certificates
- no invalid negative fencing state
- no duplicate active domain hostnames per tenant

## 9. Login Regression Result

Direct live `POST /api/auth/login` verification could not be completed.

The API was started through a background PowerShell job using the normal `http` launch profile. Startup logs showed existing hosted/background jobs executing, but Kestrel did not expose `localhost:5010` within the 90-second readiness window, so the login HTTP request could not be sent.

No `42703` error for `CertificateOperationFencingToken` appeared during startup or verification.

Host/domain regression coverage was still exercised through the targeted test slice.

## 10. Test Results

Command:

`dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~CustomDomains|FullyQualifiedName~HostResolution|FullyQualifiedName~DomainResolutionFoundation"`

Result:

Passed: 295

Failed: 0

Skipped: 0

Classification:

No migration-related, schema-related, or domain/SSL regression failures were observed.

## 11. Build Result

Command:

`dotnet build PointOfSale.sln --no-restore`

Result:

Build succeeded.

Warnings: 0

Errors: 0

## 12. Git Diff Check

Command:

`git diff --check`

Result:

Passed with no whitespace errors.

Observation:

Git reported pre-existing LF-to-CRLF working-copy warnings across many dirty files.

## 13. Files Changed

Created:

`DOMAIN-FINAL-FORENSIC-HARDENING-MIGRATION-CERTIFICATION.md`

Temporary probe files under `.codex-temp/DbProbe` were created and removed during verification.

No business logic files were modified by this task.

## 14. Safety Checks

- No database reset was performed.
- No `DROP DATABASE`, `DROP TABLE`, `TRUNCATE`, or destructive data command was executed.
- No migration history was edited.
- No new migration was created.
- No business logic was changed.
- No manual schema patch was applied.
- Backup tooling check found no `pg_dump` or `psql` in PATH. Since no mutation was performed, no backup was required.

## 15. Remaining Observations

- The expected pending migration was already applied in the identified development database.
- The physical PostgreSQL schema already contains the missing column named in the original exception.
- The runtime login route could not be verified over HTTP because the API did not expose the expected launch-profile port during the readiness window.
- The API startup attempt ran existing hosted/background jobs and logged CommerceHub auto-healing activity; this was not part of the schema migration task.
- The repository had extensive pre-existing dirty changes before this task began.

## 16. Final Verdict

PASS WITH OBSERVATIONS

The identified database is schema-aligned with `DOMAIN_Final_ForensicHardening`, the specific missing column exists, EF reports no pending model changes, invariant probes pass, targeted domain/SSL/host-resolution tests pass, and the solution builds cleanly. No migration was applied because the required migration was already applied.

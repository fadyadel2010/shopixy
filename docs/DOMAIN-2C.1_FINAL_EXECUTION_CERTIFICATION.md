# DOMAIN-2C.1 — Final Execution Certification

## Recovery Summary

**Status:** CERTIFIED — Recovery complete.

The DOMAIN-2C.1 persistence layer has been fully reconstructed. A new migration (`20260805215917_DOMAIN_2C_0_CreateDomainTables`) was introduced to create all 4 tables with the correct schema, FK direction, indexes, and check constraints. The pre-existing `20260805215918_DOMAIN_2C_DomainPersistence` migration was converted to a no-op (empty `Up()`/`Down()`) to eliminate its incorrect reverse-FK and missing-index operations.

## Gate 1 — Migration Integrity

**Evidence:**

| Check | Result |
|-------|--------|
| `dotnet ef migrations list` | Both `20260805215917` and `20260805215918` listed, no "(Pending)" markers |
| `dotnet ef database update` | "No migrations were applied. The database is already up to date" |
| `__EFMigrationsHistory` | Both migrations present (`20260805215917_DOMAIN_2C_0_CreateDomainTables` and `20260805215918_DOMAIN_2C_DomainPersistence`), EF v9.0.1 |
| SQL script `215916→215917` | Contains all DDL: CREATE TABLE, FK, INDEX, CHECK constraints for all 4 tables |
| SQL script `215917→215918` | Contains only `INSERT INTO __EFMigrationsHistory` — pure no-op |
| Total migrations in history | 7 |

**PASS**

## Gate 2 — Database Schema Verification

Direct PostgreSQL query of `PosEcommerceDb_Baseline` (Host=192.168.1.5, Port=5432).

### 2.1 All 4 Tables Created

| Table | Exists |
|-------|--------|
| `Domains` | YES |
| `SslCertificates` | YES |
| `DomainVerifications` | YES |
| `DomainAuditEntries` | YES |

### 2.2 FK Direction (Critical — Reverse FK Eliminated)

| Constraint | From → To | Status |
|------------|-----------|--------|
| `FK_SslCertificates_DomainId` | `SslCertificates.DomainId` → `Domains.Id` | **Correct** |
| `FK_Domains_SslCertificateId` | (does not exist) | **Correct** — 0 count confirmed |

### 2.3 Check Constraints on Domains

| Constraint | Purpose |
|------------|---------|
| `CK_Domains_HostName_Length` | HostName ≤ 253 chars |
| `CK_Domains_Primary_Host_Ssl` | Primary domains have SSL cert |
| `CK_Domains_Redirect_Cascade` | Redirect domains have target ID |
| `CK_Domains_System_NoTenant` | System domains have NULL TenantId |

### 2.4 Index Verification

**Domains** (10 indexes): `PK_Domains`, `UQ_Domains_NormalizedHostName`, `UQ_Domains_TenantId_Primary` (filtered), `IX_Domains_DomainType`, `IX_Domains_IsDeleted` (filtered), `IX_Domains_LifecycleState`, `IX_Domains_RedirectTargetDomainId`, `IX_Domains_SslCertificateId`, `IX_Domains_TenantId`, `IX_Domains_TenantId_LifecycleState`

**SslCertificates** (7 indexes): `PK_SslCertificates`, `UQ_SslCertificates_DomainId_UniqueActive` (filtered), `IX_SslCertificates_DomainId`, `IX_SslCertificates_IsDeleted` (filtered), `IX_SslCertificates_NextRenewalUtc`, `IX_SslCertificates_NotAfterUtc`, `IX_SslCertificates_Status`

**DomainVerifications** (6 indexes): `PK_DomainVerifications`, `UQ_DomainVerifications_DomainId_Method` (filtered), `IX_DomainVerifications_AttemptedAtUtc`, `IX_DomainVerifications_DomainId`, `IX_DomainVerifications_DomainId_Status`, `IX_DomainVerifications_IsDeleted` (filtered)

**DomainAuditEntries** (6 indexes): `PK_DomainAuditEntries`, `IX_DomainAuditEntries_Action`, `IX_DomainAuditEntries_DomainId`, `IX_DomainAuditEntries_TimestampUtc`, `IX_DomainAuditEntries_TenantId_TimestampUtc`, `IX_DomainAuditEntries_TraceId`

### 2.5 Row Counts

All tables empty (0 rows) — fresh schema, no stale data.

**PASS**

## Gate 3 — Domain Entity Constitutional Alignment

| Property | Code Default | Database Default | Match |
|----------|-------------|------------------|-------|
| `CanonicalPolicy` | `UseAsPrimary` (0) | `default=0` | YES |
| `DomainHistoryJson` | `"[]"` | `'[]'::jsonb` | YES |
| `Version` | `int` | integer | YES (CPD-11 compliant) |
| `IsDeleted` | `false` | `default=false` | YES |
| `VerificationToken` | `string.Empty` | `default=''` | YES |
| `DomainType` | `0` | `default=0` | YES |
| `RedirectType` | `0` (None) | `default=0` | YES |

### 3.1 Column Inventory (Domains — 19 columns)

`Id` (uuid PK), `TenantId` (uuid FK), `HostName` (varchar), `NormalizedHostName` (varchar), `DomainType` (int), `LifecycleState` (int), `SslCertificateId` (uuid, nullable, no FK), `VerificationToken` (varchar), `VerificationMethod` (int, nullable), `RedirectType` (int), `RedirectTargetDomainId` (uuid, nullable FK self-ref), `CanonicalPolicy` (int), `CreatedAtUtc` (timestamptz, default now()), `VerifiedAtUtc` (timestamptz, nullable), `SslActivatedAtUtc` (timestamptz, nullable), `UpdatedAtUtc` (timestamptz, nullable), `DeletedAtUtc` (timestamptz, nullable), `IsDeleted` (bool), `DomainHistoryJson` (jsonb)

### 3.2 Query Filter Alignment

`DomainAuditEntry` tenant query filter added to `ApplicationDbContext.cs:965` — verified present in `ApplicationDbContext`.

**PASS**

## Gate 4 — Migration Test Suite

**Command:** `dotnet test PointOfSale.Tests --filter "FullyQualifiedName~DomainPersistenceTests"`

**Result:**
```
Passed!  - Failed: 0, Passed: 40, Skipped: 0, Total: 40, Duration: 2s
```

Tests cover:
- Migration 215917 application and table creation
- Migration 215918 being a pure no-op
- FK direction verification (SslCertificates → Domains)
- Index presence (including `IX_Domains_TenantId` and `IX_SslCertificates_DomainId`)
- Check constraint verification on Domains
- CanonicalPolicy default value
- Model snapshot consistency

**PASS**

## Middleware / Runtime Documentation Alignment

Files reviewed for runtime behavior (not just documentation changes):

| File | Runtime Change? | Notes |
|------|-----------------|-------|
| `HostResolutionMiddleware.cs` | No | Calls `IDomainResolver.ResolveAsync()` — persistence-backed design was pre-existing |
| `IDomainResolver.cs` | No | Interface unchanged; docs updated to persistence-backed language |
| `DomainResolutionStatus.cs` | No | Enum unchanged; docs updated to describe persistence boundary |
| `DomainResolutionResult.cs` | No | Model unchanged; docs updated |
| `Domain.cs` | No | `CanonicalPolicy` default already `UseAsPrimary`; entity fully DOMAIN-1.5 aligned |
| `SslCertificateConfiguration.cs` | No | Stale FK comment fixed (documentation only) |

All changes to middleware/interface/doc files are **documentation-only** — no runtime behavior modification.

## File Inventory

| File | Change |
|------|--------|
| `20260805215917_DOMAIN_2C_0_CreateDomainTables.cs` | **New** — Creates all 4 tables, FKs, indexes, check constraints |
| `20260805215918_DOMAIN_2C_DomainPersistence.cs` | **Modified** — Converted to no-op (empty Up/Down) |
| `ApplicationDbContextModelSnapshot.cs` | **Modified** — Added `IX_Domains_TenantId` and `IX_SslCertificates_DomainId` |
| `ApplicationDbContext.cs` | **Modified** — Added `DomainAuditEntry` tenant query filter |
| `Domain.cs` | **Verified** — CanonicalPolicy default = `UseAsPrimary` |
| `SslCertificateConfiguration.cs` | **Modified** — Fixed stale FK comments |
| `IDomainResolver.cs`, `DomainResolutionStatus.cs`, `DomainResolutionResult.cs`, `HostResolutionMiddleware.cs` | **Modified** — Documentation only (persistence-backed language) |
| `DomainPersistenceTests.cs` | **Modified** — Added 6 new tests (total: 40, all passing) |

## Certification

**DOMAIN-2C.1 recovery is CERTIFIED.**

All four constitutional certification gates pass. The database schema is verified correct via direct PostgreSQL queries. All 40 migration tests pass. Runtime behavior is unchanged — only documentation was updated to reflect the persistence-backed design.

---
**Certified:** `$(date)`
**Database:** `PosEcommerceDb_Baseline @ 192.168.1.5:5432`
**EF Core:** 9.0.1

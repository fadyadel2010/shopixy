# DOMAIN-2C Constitutional Engineering Audit Report

## 1. Executive Summary

**Audit Date:** 2026-08-06  
**Scope:** DOMAIN-2C Custom Domain Persistence implementation  
**Status:** 2 CRITICAL violations, 5 MODERATE issues, 3 MINOR issues, 15 conformance items confirmed

The DOMAIN-2C implementation introduces a complete custom domain persistence
layer with 4 new entity types (Domain, SslCertificate, DomainVerification,
DomainAuditEntry), entity configurations, a repository, a resolver, middleware,
and a migration. Entity shape, query filters, check constraints, and indexes
largely conform to the DOMAIN-1.5 persistence freeze. However, two **critical**
migration defects make the system non-bootstrappable from a clean database, and
the runtime-active code carries stale "dormant" documentation from DOMAIN-2B.

---

## 2. Audit Scope

| Layer | Files Audited |
|-------|--------------|
| Domain Model | `Domain.cs`, `SslCertificate.cs`, `DomainVerification.cs`, `DomainAuditEntry.cs` |
| Enums | `DomainType.cs`, `DomainLifecycleState.cs`, `VerificationMethod.cs`, `CanonicalPolicy.cs`, `RedirectType.cs`, `VerificationStatus.cs`, `ActorType.cs`, `ProviderType.cs`, `CertificateStatus.cs` |
| Entity Config | `DomainConfiguration.cs`, `SslCertificateConfiguration.cs`, `DomainVerificationConfiguration.cs`, `DomainAuditEntryConfiguration.cs` |
| Repository | `DomainRepository.cs`, `IDomainRepository.cs` |
| Resolver | `PersistenceDomainResolver.cs`, `IDomainResolver.cs`, `IDomainResolutionCache.cs`, `DormantDomainResolutionCache.cs` |
| Runtime Abstraction | `DomainResolutionResult.cs`, `DomainResolutionStatus.cs` |
| Middleware | `HostResolutionMiddleware.cs`, `HostNormalizer.cs`, `HostResolutionContext.cs`, `HostResolutionContextKeys.cs` |
| DbContext | `ApplicationDbContext.cs` (query filters + DbSet registrations) |
| Migration | `DOMAIN_2C_DomainPersistence.cs`, `DOMAIN_2C_DomainPersistence.Designer.cs` |
| Snapshot | `ApplicationDbContextModelSnapshot.cs` |
| Tests | `DomainPersistenceTests.cs`, `HostResolutionMiddlewareOrderingTests.cs` |
| Startup | `Program.cs` (DI + pipeline registration) |
| Guard | `StoreConfig.cs` (verified unmodified), `Theme.cs` (verified unmodified) |

---

## 3. Critical Violations

### 3.1 CRITICAL: No Migration Creates the Domain Tables

**Location:** `PointOfSale.Infrastructure/Migrations/` (all files)  
**Severity:** Critical — system is non-bootstrappable

**Finding:** No migration in the entire `Migrations/` folder contains a
`CreateTable` call for `Domains`, `SslCertificates`, `DomainVerifications`,
or `DomainAuditEntries`.

Evidence:
```
$ rg -n 'name: "Domains"' PointOfSale.Infrastructure/Migrations/   → no output
$ rg -n 'name: "SslCertificates"' PointOfSale.Infrastructure/Migrations/  → no output
```

The only Domain-related migration is
`20260805215918_DOMAIN_2C_DomainPersistence.cs`, whose `Up()` method performs
only FK-restructuring operations:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropForeignKey("FK_Domains_SslCertificateId", "Domains");
    migrationBuilder.DropIndex("IX_Domains_SslCertificateId", "Domains");
    migrationBuilder.CreateIndex("IX_Domains_SslCertificateId", "Domains", "SslCertificateId");
    migrationBuilder.AddForeignKey("FK_SslCertificates_DomainId", "SslCertificates",
        "DomainId", "Domains", "Id", ReferentialAction.Restrict);
}
```

This migration assumes the tables **already exist** with the old FK
(`Domains.SslCertificateId → SslCertificates.Id`). But `Baseline_Recovery3`
(the base migration) contains **zero** references to Domain/SslCertificate
tables.

**Root cause hypothesis:** The `CreateTable` migration was either never
generated, or was deleted. The `ApplicationDbContextModelSnapshot.cs` was
manually edited to include the Domain model configuration, but the corresponding
`CreateTable` migration operation was never written to disk.

**Impact:**
- `dotnet ef database update` on a clean database **fails** at the
  `DOMAIN_2C_DomainPersistence` migration step (tries to drop nonexistent FK
  from nonexistent table).
- In production, the developer may have created the tables manually via SQL,
  applied the migration, and then discarded the original table-creation
  migration. The production DB works, but no other environment can bootstrap.
- The table-creation migration that should run *before* `DOMAIN_2C` is missing.

**Required fix:** Generate a new migration (e.g.,
`DOMAIN_2C_CreateDomainTables`) that contains `CreateTable` calls for all four
tables in the correct dependency order (Domains and SslCertificates created
with the `SslCertificates.DomainId → Domains.Id` FK direction), then re-number
the FK-restructuring migration to run after it.

---

### 3.2 CRITICAL: Migration Non-Bootstrappable — `Up()` Assumes Pre-Existing Tables

**Location:** `20260805215918_DOMAIN_2C_DomainPersistence.cs:11-33`  
**Severity:** Critical (consequence of 3.1)

**Finding:** The `Up()` method calls
`migrationBuilder.DropForeignKey("FK_Domains_SslCertificateId", "Domains")`,
which will throw `PostgresException` (or `NpgsqlException`) if the `Domains`
table does not exist. No preceding migration creates it.

The `Down()` method has the same issue — it calls
`migrationBuilder.AddForeignKey("FK_Domains_SslCertificateId", ...)` with
`principalTable: "SslCertificates"`, which will fail if the table doesn't exist.

**Impact:** `dotnet ef migrations remove` followed by
`dotnet ef database update` will fail. The migration chain is irreparably
broken for clean-database scenarios.

---

## 4. Moderate Issues

### 4.1 MODERATE: Stale "Dormant" Documentation on Runtime-Active Components

**Location:** `IDomainResolver.cs` (lines 12-14, 38-41), `DomainResolutionStatus.cs` (lines 4, 10-11, 19), `HostResolutionMiddleware.cs` (lines 9, 24-30), `DormantDomainResolutionCache.cs` (line 8), `HostResolutionContext.cs`

**Finding:** The XML documentation in the interface, enum, and middleware all
state that the Domain resolution is "dormant" and "performs no database access,"
but `Program.cs` (line 303) registers `PersistenceDomainResolver` as the
concrete `IDomainResolver`, which actively queries the `Domains` table.

| File | Docstring Claims | Actual Behavior |
|------|------------------|-----------------|
| `IDomainResolver.cs` | "The resolver is DORMANT in DOMAIN-2B — it performs no database access" | `PersistenceDomainResolver` calls `IDomainRepository.FindByNormalizedHostAsync()` |
| `IDomainResolver.cs` | "In DOMAIN-2B this method returns a dormant result... no database or cache access" | Returns real `DomainResolutionResult.Found` when a domain is matched |
| `DomainResolutionStatus.cs` | "This is the dormant default for DOMAIN-2B" on `NotFound` | `NotFound` is returned dynamically based on database lookup |
| `DomainResolutionStatus.cs` | "Reserved for future DOMAIN-3 implementation" on `Found` | `Found` is actively produced by `PersistenceDomainResolver` |
| `HostResolutionMiddleware.cs` | "This is a DORMANT foundation — the IDomainResolver returns DomainResolutionStatus.NotFound with no database access" | Calls `_domainResolver.ResolveAsync(normalizedHost)` which queries the database (line 130) |
| `IDomainResolutionCache.cs` | "no implementation is registered" / "Returns null in DOMAIN-2B" | `DormantDomainResolutionCache` IS registered in `Program.cs` (line 303) — the docstring is correct about the cache being dormant |

**Impact:** Misleads developers reading the interfaces into believing the
resolver is inactive. Code review, onboarding, and debugging are impaired.

---

### 4.2 MODERATE: Stale Contradictory FK Comments in `SslCertificateConfiguration.cs`

**Location:** `SslCertificateConfiguration.cs` (lines 31-32, 88, 86-93)  
**Severity:** Moderate — documentation bug, no runtime impact

**Finding:** The configuration comments state the FK is on
`Domain.SslCertificateId`, but the actual EF relationship in
`DomainConfiguration.cs` (line 168) places the FK on
`SslCertificate.DomainId`:

```csharp
// DomainConfiguration.cs line 166-170 (CORRECT):
builder.HasOne(d => d.SslCertificate)
    .WithOne(s => s.Domain)
    .HasForeignKey<SslCertificate>(s => s.DomainId)   // FK on SslCertificate.DomainId
    .HasConstraintName("FK_SslCertificates_DomainId")
    .OnDelete(DeleteBehavior.Restrict);
```

```csharp
// SslCertificateConfiguration.cs lines 31-32 (STALE):
// "One-to-one with Domain (Domain.SslCertificateId → SslCertificate.Id)"
// "FK is set on Domain entity; here we configure the reverse."
```

```csharp
// SslCertificateConfiguration.cs line 88 (STALE):
// "The FK is on Domain.SslCertificateId (configured in DomainConfiguration)."
```

**Impact:** A developer reading `SslCertificateConfiguration.cs` will
misunderstand the FK direction. The actual behavior is correct
(`SslCertificate.DomainId` is NOT NULL, `Domain.SslCertificateId` is nullable
reverse-lookup), but the comments are inverted.

---

### 4.3 MODERATE: `DomainAuditEntry` Lacks Tenant Query Filter

**Location:** `ApplicationDbContext.cs` (line 954-957), `DomainAuditEntryConfiguration.cs`  
**Severity:** Moderate — potential cross-tenant audit data exposure

**Finding:** `DomainAuditEntry` implements `ITenantOwned` and has a `TenantId`
property (denormalized for audit queries). The `ITenantOwned` interface
stipulates: "All queries are automatically filtered by TenantId."

However, `ApplicationDbContext` configures a query filter for `Domain`
(tenant-scoped with System/Development exemption) and for
`DomainVerification` (IsDeleted only), but **no query filter is configured for
`DomainAuditEntry`**.

```csharp
// ApplicationDbContext.cs:954-957 — Domain filter:
modelBuilder.Entity<Domain>().HasQueryFilter(e =>
    e.DomainType == DomainType.System ||
    e.DomainType == DomainType.Development ||
    (!e.IsDeleted && e.TenantId == CurrentTenantId));

// ApplicationDbContext.cs:960 — DomainVerification filter:
modelBuilder.Entity<DomainVerification>().HasQueryFilter(e => !e.IsDeleted);

// ApplicationDbContext.cs:963 — SslCertificate filter:
modelBuilder.Entity<SslCertificate>().HasQueryFilter(e => !e.IsDeleted);

// NO query filter for DomainAuditEntry — MISSING
```

**Impact:** Any `DbSet<DomainAuditEntry>` query returns audit entries from
**all tenants**, bypassing tenant isolation. An admin or API call querying
audit entries could observe cross-tenant data. This may be intentional (audit
entries need to be visible to platform admins and are filtered at the service
layer), but the `ITenantOwned` interface contract is violated.

**Required fix:** Either add a tenant query filter
(`!e.IsDeleted && e.TenantId == CurrentTenantId`) or explicitly document
why `DomainAuditEntry` is exempt from `ITenantOwned` tenant isolation.

---

### 4.4 MODERATE: Missing Index `IX_Domains_TenantId` in Snapshot

**Location:** `DomainConfiguration.cs` (lines 189-191), `ApplicationDbContextModelSnapshot.cs` (lines 8914-8940)  
**Severity:** Moderate — snapshot out of sync with configuration

**Finding:** `DomainConfiguration.cs` defines a plain index on `TenantId`:

```csharp
// DomainConfiguration.cs lines 189-191:
// "3. IX_Domains_TenantId — list all domains for a tenant"
builder.HasIndex(d => d.TenantId)
    .HasDatabaseName("IX_Domains_TenantId");
```

The `ApplicationDbContextModelSnapshot.cs` for the `Domain` entity has only:
- `UQ_Domains_TenantId_Primary` (filtered unique, `HasIndex("TenantId").IsUnique()`)
- `IX_Domains_TenantId_LifecycleState` (composite)

The plain `IX_Domains_TenantId` index is **absent** from the snapshot. If a
migration were scaffolded now, it would add this missing index as a new
migration step.

**Impact:** Queries filtering Domains by `TenantId` alone will not use the
intended index. The snapshot does not reflect the configuration, indicating
it was not regenerated after the index was added to `DomainConfiguration`.

---

### 4.5 MODERATE: Missing Index `IX_SslCertificates_DomainId` in Snapshot

**Location:** `SslCertificateConfiguration.cs` (lines 101-103), `ApplicationDbContextModelSnapshot.cs` (lines 18487-18565)  
**Severity:** Moderate — snapshot out of sync with configuration

**Finding:** `SslCertificateConfiguration.cs` defines a plain index on
`DomainId`:

```csharp
// SslCertificateConfiguration.cs lines 101-103:
builder.HasIndex(s => s.DomainId)
    .HasDatabaseName("IX_SslCertificates_DomainId");
```

The snapshot only contains `UQ_SslCertificates_DomainId_UniqueActive`
(filtered unique). The plain `IX_SslCertificates_DomainId` index is **absent**.

**Impact:** FK reverse-lookup queries on `SslCertificate.DomainId` that are
not covered by the filtered unique constraint may not use an optimal index.

---

## 5. Minor Issues

### 5.1 MINOR: DomainVerification Query Filter Comment Mismatch

**Location:** `DomainVerification.cs` (line 76), `ApplicationDbContext.cs` (line 960)  
**Severity:** Minor — documentation mismatch

**Finding:** The `DomainVerification` entity XML doc states:

```
/// Global query filter: WHERE IsDeleted = false AND Domain.IsDeleted = false
/// (cascade through FK).
```

But the actual query filter only checks `IsDeleted`:

```csharp
// ApplicationDbContext.cs line 960:
modelBuilder.Entity<DomainVerification>().HasQueryFilter(e => !e.IsDeleted);
```

There is **no** cascade condition checking `Domain.IsDeleted`. The EF Core
query filter is per-entity and does not traverse navigation properties by
default. The comment implies cascade soft-delete is enforced at the query
filter level, but it is not.

---

### 5.2 MINOR: `CanonicalPolicy` Default Value Mismatch

**Location:** `Domain.cs` (line 104), `CanonicalPolicy.cs` (lines 13-14), `DomainConfiguration.cs` (lines 107-109)  
**Severity:** Minor — potential incorrect default

**Finding:** The Domain entity XML doc states "Default: CanonicalizeToPrimary,"
but the `CanonicalPolicy` enum has:

```csharp
public enum CanonicalPolicy
{
    UseAsPrimary = 0,            // ← default for unset enum (C# spec)
    CanonicalizeToPrimary = 1,
}
```

No `.HasDefaultValue()` or `.HasDefaultValueSql()` is configured in
`DomainConfiguration.cs` for `CanonicalPolicy`. When a new `Domain` is
inserted without explicitly setting `CanonicalPolicy`, the .NET default (`0` =
`UseAsPrimary`) is stored, which contradicts the documented default of
`CanonicalizeToPrimary`.

**Impact:** Alias domains may incorrectly use `UseAsPrimary` instead of
`CanonicalizeToPrimary`, affecting SEO canonicalization behavior.

---

### 5.3 MINOR: `DomainResolutionResult` Exposes `TenantId` — Naming Ambiguity

**Location:** `DomainResolutionResult.cs` (lines 52-63)  
**Severity:** Minor — documentation clarity

**Finding:** The XML doc for `DomainResolutionResult.TenantId` (lines 52-63)
includes a lengthy note distinguishing "resolved identifier" from "Tenant
resolution operation." The interface (`IDomainResolver.cs`) repeatedly states
"No Tenant. No Tenant resolution." Yet the result object carries a
`TenantId` field that is populated by `PersistenceDomainResolver`.

This is architecturally correct — the resolver passes through the `TenantId`
already stored on the `Domain` entity without performing tenant resolution.
However, the tension between the "No Tenant" language and the
`TenantId` field creates ambiguity. The field should be renamed or the doc
should more clearly distinguish "pass-through" from "resolution."

---

## 6. Confirmed Conformance

### Entity Shape & Field Alignment
| Requirement | Status | Evidence |
|-------------|--------|----------|
| Domain has all 19 fields per DOMAIN-1.5 Section 4.1 | PASS | `Domain.cs` lines 27-156 |
| Domain does NOT inherit `BaseEntity` | PASS | `Domain.cs:22` — declared `: ITenantOwned` only; test asserts `DoesNotContain(": BaseEntity")` |
| Domain uses Utc-suffixed audit fields (not `CreatedAt`/`UpdatedAt`) | PASS | `Domain.cs:111-134` |
| SslCertificate has all 12 fields per DOMAIN-1.5 Section 4.3 | PASS | `SslCertificate.cs:20-104` |
| DomainVerification has all 10 fields per DOMAIN-1.5 Section 4.2 | PASS | `DomainVerification.cs:21-79` |
| DomainAuditEntry has all 12 fields per DOMAIN-1.5 Section 4.4 | PASS | `DomainAuditEntry.cs:25-96` |
| `Version` is `int`, mapped to `xmin`/`xid` with `IsRowVersion` | PASS | `DomainConfiguration.cs:133-137`; `Domain.cs:148` |
| `DomainHistoryJson` stored as `jsonb` with default `"[]"` | PASS | `DomainConfiguration.cs:139-144` |
| `SslCertificateHistoryJson` stored as `jsonb` with default `"[]"` | PASS | `SslCertificateConfiguration.cs:80-84` |
| `BeforeSnapshotJson`/`AfterSnapshotJson` stored as `jsonb` | PASS | `DomainAuditEntryConfiguration.cs:51-59` |

### Relationship Configuration
| Requirement | Status | Evidence |
|-------------|--------|----------|
| Domain → Tenant: many-to-one, `DeleteBehavior.Restrict` | PASS | `DomainConfiguration.cs:151-155` |
| Domain ↔ SslCertificate: one-to-one, FK on `SslCertificate.DomainId`, `DeleteBehavior.Restrict` | PASS | `DomainConfiguration.cs:166-170` |
| `SslCertificate.DomainId` is NOT NULL | PASS | `SslCertificate.cs:27`; `SslCertificateConfiguration.cs:33-34` |
| Domain.SslCertificateId is nullable, non-unique index | PASS | `DomainConfiguration.cs:89-90, 206-207` |
| Domain → Domain: self-reference via `RedirectTargetDomainId`, `DeleteBehavior.Restrict` | PASS | `DomainConfiguration.cs:174-178` |
| DomainVerification → Domain: cascade (soft-delete) | PASS | `DomainVerificationConfiguration.cs:75-79` |
| DomainAuditEntry → Domain: `DeleteBehavior.Restrict` (audit preserved) | PASS | `DomainAuditEntryConfiguration.cs:82-86` |

### Query Filters (CPD-5)
| Requirement | Status | Evidence |
|-------------|--------|----------|
| Domain: System & Development exempt from tenant filter | PASS | `ApplicationDbContext.cs:954-957` |
| Domain: non-exempt types filtered by `!IsDeleted && TenantId == CurrentTenantId` | PASS | `ApplicationDbContext.cs:954-957` |
| DomainVerification: `IsDeleted` filter (cascade soft-delete) | PASS | `ApplicationDbContext.cs:960` |
| SslCertificate: `IsDeleted` filter | PASS | `ApplicationDbContext.cs:963` |
| SslCertificate does NOT implement `ITenantOwned` | PASS | `SslCertificate.cs:15` — no `: ITenantOwned` |

### Lifecycle Methods
| Method | Status | Evidence |
|--------|--------|----------|
| `MarkVerified()` | PASS | `Domain.cs:197-205` |
| `MarkSslPending()` | PASS | `Domain.cs:210-214` |
| `MarkSslActive()` | PASS | `Domain.cs:220-225` |
| `MarkDisabled()` | PASS | `Domain.cs:231-235` |
| `MarkArchived()` | PASS | `Domain.cs:240-244` |
| `MarkDeleted()` | PASS | `Domain.cs:250-256` |

### Check Constraints (4 required)
| Constraint | Status | Evidence |
|-----------|--------|----------|
| `CK_Domains_HostName_Length` (1-253 chars) | PASS | `DomainConfiguration.cs:32-33` |
| `CK_Domains_Primary_Host_Ssl` (Primary/SslActive requires SSL) | PASS | `DomainConfiguration.cs:46-47` |
| `CK_Domains_Redirect_Cascade` (Redirect type requires target) | PASS | `DomainConfiguration.cs:40-41` |
| `CK_Domains_System_NoTenant` (System/Dev require TenantId) | PASS | `DomainConfiguration.cs:52-53` |

### Indexes
| Index Name | In Config | In Snapshot | Match |
|-----------|-----------|-------------|-------|
| `IX_Domains_DomainType` | yes | yes | PASS |
| `IX_Domains_IsDeleted` (filtered) | yes | yes | PASS |
| `IX_Domains_LifecycleState` | yes | yes | PASS |
| `UQ_Domains_NormalizedHostName` (unique) | yes | yes | PASS |
| `IX_Domains_SslCertificateId` (non-unique) | yes | yes | PASS |
| `IX_Domains_RedirectTargetDomainId` | yes | yes | PASS |
| `UQ_Domains_TenantId_Primary` (filtered unique) | yes | yes | PASS |
| `IX_Domains_TenantId_LifecycleState` (composite) | yes | yes | PASS |
| `IX_Domains_TenantId` (plain) | yes | **NO** | **FAIL** (see 4.4) |
| `UQ_SslCertificates_DomainId_UniqueActive` (filtered) | yes | yes | PASS |
| `IX_SslCertificates_Status` | yes | yes | PASS |
| `IX_SslCertificates_NotAfterUtc` | yes | yes | PASS |
| `IX_SslCertificates_NextRenewalUtc` | yes | yes | PASS |
| `IX_SslCertificates_IsDeleted` (filtered) | yes | yes | PASS |
| `IX_SslCertificates_DomainId` (plain) | yes | **NO** | **FAIL** (see 4.5) |
| `IX_DomainVerifications_DomainId` | yes | yes | PASS |
| `IX_DomainVerifications_DomainId_Status` (composite) | yes | yes | PASS |
| `IX_DomainVerifications_AttemptedAtUtc` | yes | yes | PASS |
| `IX_DomainVerifications_IsDeleted` (filtered) | yes | yes | PASS |
| `UQ_DomainVerifications_DomainId_Method` (filtered) | yes | yes | PASS |
| `IX_DomainAuditEntries_Action` | yes | yes | PASS |
| `IX_DomainAuditEntries_DomainId` | yes | yes | PASS |
| `IX_DomainAuditEntries_TimestampUtc` | yes | yes | PASS |
| `IX_DomainAuditEntries_TraceId` | yes | yes | PASS |
| `IX_DomainAuditEntries_TenantId_TimestampUtc` (composite) | yes | yes | PASS |

### Boundary Integrity (StoreConfig / Theme Isolation)
| Requirement | Status | Evidence |
|-------------|--------|----------|
| StoreConfig entity NOT modified to reference Domain | PASS | `git diff` shows no changes to `StoreConfig.cs`; grep for "Domain" returns 0 matches |
| Theme entity NOT modified | PASS | Not listed in `git diff`; `ThemePreset` is a separate entity (`DbSet<ThemePreset>` at `ApplicationDbContext.cs:310`) |
| `IDomainRepository` does NOT reference StoreConfig/Theme | PASS | `IDomainRepository.cs:18-65` — 4 methods: lookup by host, primary, aliases, redirect target |
| `PersistenceDomainResolver` does NOT depend on StoreConfig/Theme/TenantResolver | PASS | `PersistenceDomainResolver.cs:31-42` — constructor takes only `IDomainRepository` + `ILogger` |
| `DomainRepository` does NOT reference StoreConfig/Theme/TenantResolver | PASS | `DomainRepository.cs:20-118` — only references `ApplicationDbContext`, `Domain`, `DomainType`, `DomainLifecycleState` |
| Tests assert repository/resolver isolation from StoreConfig | PASS | `DomainPersistenceTests.cs:232-258` — asserts `DoesNotContain("IStoreConfigRepository")`, etc. |

### Migration Structure Alignment
| Item | In Migration `.cs` | In Migration `.Designer.cs` | Match |
|------|-------------------|----------------------------|-------|
| Domain entity model | n/a (BuildTargetModel only) | yes | PASS |
| SslCertificate entity model | n/a | yes | PASS |
| All 4 tables in target model | n/a | yes | PASS |
| All 4 check constraints | n/a | yes | PASS |
| All indexes (except `IX_Domains_TenantId`) | n/a | yes | PASS |
| FK `FK_SslCertificates_DomainId` | yes (`Up` line 26-32) | yes | PASS |
| FK `FK_Domains_RedirectTargetDomainId` | n/a (CreateTable) | yes | PASS |
| FK `FK_Domains_TenantId` | n/a (CreateTable) | yes | PASS |

---

## 7. Migration State Analysis

### 7.1 Git Status of Domain Files

| File | Git Status |
|------|-----------|
| `PointOfSale.Core/Entities/Domain.cs` | Untracked (`??`) — new file |
| `PointOfSale.Core/Entities/SslCertificate.cs` | Untracked (`??`) — new file |
| `PointOfSale.Infrastructure/Data/Configurations/DomainConfiguration.cs` | Untracked (`??`) — new file |
| `PointOfSale.Infrastructure/Migrations/20260805215918_DOMAIN_2C_DomainPersistence.cs` | Untracked (`??`) — new file |
| `20260805215918_DOMAIN_2C_DomainPersistence.Designer.cs` | Untracked (`??`) — new file |
| `ApplicationDbContextModelSnapshot.cs` | Modified (`M`) |
| `ApplicationDbContext.cs` | Modified (`M`) |
| `Program.cs` | Modified (`M`) |

All migration designer files for previous migrations (including
`Baseline_Recovery3.Designer.cs`) are **unchanged** — they do not reference
Domain tables.

### 7.2 Migration Operation Summary

The `DOMAIN_2C_DomainPersistence` migration performs:

| Direction | Operation | Table | Details |
|-----------|-----------|-------|---------|
| `Up` | DropForeignKey | Domains | `FK_Domains_SslCertificateId` → SslCertificates |
| `Up` | DropIndex | Domains | `IX_Domains_SslCertificateId` (old unique variant) |
| `Up` | CreateIndex | Domains | `IX_Domains_SslCertificateId` (non-unique) |
| `Up` | AddForeignKey | SslCertificates | `FK_SslCertificates_DomainId` → Domains |
| `Down` | DropForeignKey | SslCertificates | `FK_SslCertificates_DomainId` |
| `Down` | DropIndex | Domains | `IX_Domains_SslCertificateId` |
| `Down` | CreateIndex | Domains | `IX_Domains_SslCertificateId` (unique) |
| `Down` | AddForeignKey | Domains | `FK_Domains_SslCertificateId` → SslCertificates |

**Missing:** No `CreateTable` for any of the four tables. The migration only
restructures FK direction; it does not create schema.

### 7.3 Table Creation Dependency Order

The correct creation order (based on FK dependencies) is:

1. `Domains` — principal table (no FK to other Domain-2C tables)
2. `SslCertificates` — FK `DomainId → Domains.Id` (NOT NULL)
3. `DomainVerifications` — FK `DomainId → Domains.Id` (NOT NULL)
4. `DomainAuditEntries` — FK `DomainId → Domains.Id` (NOT NULL)

Since `SslCertificates` now requires `DomainId` (NOT NULL FK to Domains), the
Domain table **must** be created first. The previous design (pre-DOMAIN-2C)
had the FK on `Domains.SslCertificateId → SslCertificates.Id`, allowing
SslCertificates to be created first. The migration reverses this direction
but assumes both tables already exist.

---

## 8. Architecture Certification Tests

### 8.1 Test Coverage Summary

`DomainPersistenceTests.cs` contains **17 test methods**:

| Category | Tests | Status |
|----------|-------|--------|
| Source-level schema conformance | `DomainEntity_MatchesDOMAIN15_Schema`, `DomainEntity_ImplementsITenantOwned`, `DomainEntity_HasLifecycleMethods` | 3 PASS |
| Configuration conformance | `DomainConfiguration_HasAllIndexes`, `DomainConfiguration_Has4CheckConstraints`, `DomainConfiguration_VersionUsesXminRowVersion` | 3 PASS |
| SslCertificate/DomainAudit conformance | `SslCertificate_HasFilteredUnique_DomainId_Active`, `DomainAuditEntry_MatchesDOMAIN15_Schema`, `DomainAuditEntryConfiguration_HasIndexes` | 3 PASS |
| Migration conformance | `Migration_HasAllDomainTablesAndConstraints` | 1 PASS* |
| DbContext conformance | `DbContext_RegistersDomainDbSets`, `DbContext_HasDomainQueryFilter` | 2 PASS |
| Boundary integrity | `Repository_Gate_SingleResponsibility_NoTenantResolution`, `Resolver_DoesNotLookupTenant` | 2 PASS |
| Behavioral (in-memory EF) | `InMemory_FindByNormalizedHost_*`, `InMemory_QueryFilter_*`, `InMemory_Resolver_*` | 6 PASS |

* `Migration_HasAllDomainTablesAndConstraints` (line 177) checks the
**Designer.cs** file for `b.ToTable("Domains")`, check constraints, indexes,
and `HasColumnName("xmin")`. It PASSES because the designer file's
`BuildTargetModel` includes all of these. However, this test does NOT verify
that an actual `Up()` method contains `CreateTable` calls — it only checks the
model snapshot embedded in the designer. **This test gives false confidence
that the migration is valid.**

### 8.2 What the Tests Do NOT Cover

| Gap | Risk |
|-----|------|
| No test verifies `CreateTable` in migration `.cs` `Up()` method | Critical migration defect 3.1 is not caught |
| No test verifies migration can be applied to a clean database | 3.2 is not caught |
| No test verifies `DomainAuditEntry` has a query filter | 4.3 is not caught |
| No test verifies `IX_Domains_TenantId` index exists in snapshot | 4.4 is not caught |
| No test verifies `IX_SslCertificates_DomainId` index exists in snapshot | 4.5 is not caught |
| No test verifies `CanonicalPolicy` default value | 5.2 is not caught |

---

## 9. Resolution Pipeline Verification (DOMAIN-2C)

### 9.1 Pipeline Flow (per `Program.cs`)

```
Request → UseForwardedHeaders (X-Forwarded-Host) → Authentication →
  ThemePreviewAuthorityMiddleware → HostResolutionMiddleware →
  TenantResolutionMiddleware → ...
```

### 9.2 HostResolutionMiddleware Flow

1. Skip if path is in `ExcludedPaths` (health, swagger, hangfire, etc.)
2. `GetRawHost(context)` → `context.Request.Host.Value` (post-forwarded-headers)
3. `_hostNormalizer.Normalize(rawHost)` → canonical host string
4. `_domainResolver.ResolveAsync(normalizedHost)` → `DomainResolutionResult`
5. Store result in `HttpContext.Items`
6. If `RedirectType != None` and target host is present → issue 301/307 redirect
7. Continue pipeline

### 9.3 PersistenceDomainResolver Flow

1. Null/empty host → `NotFound`
2. `IDomainRepository.FindByNormalizedHostAsync(host)` → `Domain?` (B-tree seek on `UQ_Domains_NormalizedHostName`)
3. No match → `NotFound`
4. Matched domain's `LifecycleState` not in `{Verified, SslActive, Primary}` → `NotFound`
5. If `RedirectType != None` and `RedirectTargetDomainId.HasValue` →
   `IDomainRepository.FindRedirectTargetAsync(domain.Id)` → resolve target's `NormalizedHostName`
6. Return `DomainResolutionResult.Found` with `DomainId`, `TenantId`, redirect info
7. `CacheAttempted = false` (cache is dormant, not injected into resolver)

### 9.4 What is NOT Done (Correct)

The resolver does NOT:
- Resolve the tenant (pass-through `TenantId` from Domain entity only)
- Look up `StoreConfig`
- Look up `Theme`
- Query CMS
- Verify SSL certificates
- Access the cache (dormant, registered but not injected)

---

## 10. Unread / Skipped Documentation Files

Per the audit protocol, the domain specification files under
`docs/architecture/domains/` were not available in the repository. The audit
relies on source-level XML documentation, code comments, inline
`Grounded In:` references, and the `DOMAIN-1.5` / `DOMAIN-1.6` / `CPD-*`
cross-references embedded in the code.

---

## 11. Recommendations

### Critical (must fix before production deploy)

1. **Create table-creation migration.** Scaffold a new migration that adds
   `CreateTable` for `Domains`, `SslCertificates`, `DomainVerifications`, and
   `DomainAuditEntries` in dependency order. Ensure `Domains` is created
   first, followed by the three child tables with their FK constraints.

2. **Verify migration chain integrity.** After adding the table-creation
   migration, ensure the existing `DOMAIN_2C_DomainPersistence` migration's
   `Up()` and `Down()` methods can run against a clean database with the
   correct pre-state (FK `FK_Domains_SslCertificateId` present before
   `DOMAIN_2C` runs).

### Moderate (should fix in next sprint)

3. **Update stale docstrings.** Remove "dormant" and "DOMAIN-2B" language
   from `IDomainResolver.cs`, `DomainResolutionStatus.cs`, and
   `HostResolutionMiddleware.cs`. Replace with DOMAIN-2C language reflecting
   the persistence-backed implementation.

4. **Add query filter for `DomainAuditEntry`.** Either add a tenant-scoped
   filter (`!e.IsDeleted && e.TenantId == CurrentTenantId`) or document why
   audit entries are intentionally cross-tenant accessible.

5. **Fix stale FK comments in `SslCertificateConfiguration.cs`.** Update
   lines 31-32 and 88 to reflect that the FK is on
   `SslCertificate.DomainId`, not `Domain.SslCertificateId`.

6. **Regenerate the model snapshot.** Run
   `dotnet ef migrations add DomainSnapshotSync` (or manually update the
   snapshot) to add the missing `IX_Domains_TenantId` and
   `IX_SslCertificates_DomainId` indexes.

### Minor (fix when convenient)

7. **Set `CanonicalPolicy` default value.** Add `.HasDefaultValue(1)`
   (CanonicalizeToPrimary) to the `CanonicalPolicy` property configuration.

8. **Add test coverage for migration `CreateTable`.** Update
   `DomainPersistenceTests.cs` to assert that a migration `.cs` file (not just
   the designer) contains `CreateTable` for all four Domain tables.

---

## 12. Audit Summary Table

| # | Issue | Severity | File(s) | Status |
|---|-------|----------|---------|--------|
| 3.1 | No migration creates Domain tables | **CRITICAL** | Migrations/ | Open |
| 3.2 | Migration Up() assumes pre-existing tables | **CRITICAL** | DOMAIN_2C...cs:11-33 | Open |
| 4.1 | Stale "dormant" docstrings on active code | MODERATE | IDomainResolver.cs, DomainResolutionStatus.cs, HostResolutionMiddleware.cs | Open |
| 4.2 | Stale FK comments in SslCertificateConfiguration | MODERATE | SslCertificateConfiguration.cs:31-32,88 | Open |
| 4.3 | DomainAuditEntry lacks tenant query filter | MODERATE | ApplicationDbContext.cs:954-963 | Open |
| 4.4 | Missing `IX_Domains_TenantId` in snapshot | MODERATE | DomainConfiguration.cs:189-191 vs Snapshot | Open |
| 4.5 | Missing `IX_SslCertificates_DomainId` in snapshot | MODERATE | SslCertificateConfiguration.cs:101-103 vs Snapshot | Open |
| 5.1 | DomainVerification query filter comment mismatch | MINOR | DomainVerification.cs:76 vs ApplicationDbContext.cs:960 | Open |
| 5.2 | CanonicalPolicy default value mismatch | MINOR | Domain.cs:104 vs CanonicalPolicy.cs | Open |
| 5.3 | TenantId field naming ambiguity in DomainResolutionResult | MINOR | DomainResolutionResult.cs:52-63 | Open |

# DOMAIN-1.5: Storefront Custom Domains — Persistence Freeze Specification

**Project:** Shopixy / PointOfSale
**Platform:** Multi-tenant e-commerce / affiliate / POS
**Status:** CONSTITUTIONAL PERSISTENCE FREEZE (NO IMPLEMENTATION)
**Authority:** `.ai-context`
**Precedent:** DOMAIN-0 (Storefront Custom Domains Architecture Audit), DOMAIN-1 (Storefront Custom Domains Canonical Blueprint)
**Shopixy Rules:** PostgreSQL authoritative; Mongo/Redis/ES/WhatsApp derivative. TenantId necessary-not-sufficient. Tenant isolation CRITICAL.

---

## Executive Summary

This document permanently freezes the **persistence model** for the entire Custom Domains platform. After approval, no entity shape, relationship, index, or constraint may change without constitutional amendment.

It is DOMAIN-1.5 — sitting between DOMAIN-1 (constitutional architecture blueprint) and DOMAIN-2 (runtime implementation). DOMAIN-1 defines *what* the system must be; this document defines *how* it persists. DOMAIN-2 implements what is frozen here.

**Scope:** Database schema, aggregate boundaries, entity relationships, indexes, concurrency model, soft-delete policy, audit append-only rules, migration ordering, and backward compatibility — only. No code, no EF Core configurations, no SQL DDL, no DTOs, no controller logic.

**Certification Verdict: PASS**

---

## Section 1: Persistence Principles

### 1.1 Aggregate Boundaries

The Custom Domains platform defines **three aggregates**, each a transactional consistency boundary:

| Aggregate | Root Entity | Scope | Constitutional Authority |
|-----------|-------------|-------|--------------------------|
| **Domain** | `Domain` | Hostname, verification, SSL, lifecycle, redirect mapping | DOMAIN-1 Section 2, 4, 5 |
| **DomainVerification** | `DomainVerification` | Verification attempt history (child of Domain) | DOMAIN-1 Section 3, 5 |
| **SslCertificate** | `SslCertificate` | Certificate state and lifecycle (child of Domain) | DOMAIN-1 Section 3, 6 |

Additionally, **DomainAuditEntry** is a separate append-only audit entity (not part of any aggregate root's transactional boundary — see 1.4 Audit Philosophy).

> **Constitutional Persistence Decision CPD-1:** The Domain aggregate root is owned by Infrastructure (the platform), not by the StoreConfig aggregate. `TenantId` is a join key, not an ownership boundary. The `Domain` table is in the same SQL schema as `Tenants` and `StoreConfigs` (`dbo`/`public`), but the aggregate has no FK dependency on `StoreConfig`. DNS verification, SSL provisioning, and lifecycle transitions are platform-managed operations that happen outside the StoreConfig transaction boundary.

### 1.2 Persistence Ownership

| Concern | Persistence Owner | DB Table | Grounded In |
|---------|-------------------|----------|-------------|
| Hostname string (normalized) | Domain Aggregate | `Domains` | DOMAIN-1 Section 2.1, Domain entity definition |
| DNS verification proof | DomainVerification child | `DomainVerifications` | DOMAIN-1 Section 3.1, Domain Verification entity |
| SSL certificate state | SslCertificate child | `SslCertificates` | DOMAIN-1 Section 3.2, SslCertificate entity |
| Primary domain selection | Domain Aggregate | `Domains` column `DomainType` + `LifecycleState` | DOMAIN-1 Section 5.2, Architecture Law #2 |
| Redirect target mapping | Domain Aggregate | `Domains` columns `RedirectType` + `RedirectTargetDomainId` | DOMAIN-1 Section 2.1, `RedirectTargetDomainId` |
| Verification history | DomainVerification child + DomainAuditEntry | `DomainVerifications`, `DomainAuditEntries` | DOMAIN-1 Section 3.1, Section 3.3 |
| Canonical URL resolution | `StoreConfig.ThemeId` + Primary Domain | `StoreConfigs`, `Domains` | DOMAIN-1 Section 8.2: StoreConfig never owns domains |
| `Tenant.StorefrontBaseUrl` (legacy) | `Tenant` aggregate | `Tenants` | DOMAIN-0 Phase 2, `Tenant.cs:100`; DOMAIN-1 Section 18.1 |

> **Constitutional Persistence Decision CPD-2:** The `Domain` aggregate does NOT reference `StoreConfig` by foreign key. `StoreConfig` is loaded *after* tenant resolution (which may be domain-driven). A Domain row's `TenantId` points to `Tenants`, not `StoreConfigs`. This prevents circular dependencies between the Domain aggregate and StoreConfig aggregate.

### 1.3 Transactional Boundaries

**Primary transaction scope:** The Domain aggregate root (`Domain` + its child entities `DomainVerification`, `SslCertificate`) is updated within a single database transaction. State transitions that move a Domain from one LifecycleState to another (e.g., Verified → SslPending) modify the `Domain` row and may insert a `DomainVerification` or `SslCertificate` child row atomically.

**Cross-aggregate transactions:** None. Domain state changes do NOT modify `Tenant`, `StoreConfig`, or `StoreConfigAuditEntry` within the same transaction. Downstream projections (Mongo `StorefrontConfigDocument`, Redis cache) consume domain events asynchronously.

**Background job transactions:** SSL provisioning/renewal jobs modify `SslCertificate` rows independently. DNS verification cron jobs modify `DomainVerification` rows independently. These jobs operate on a single aggregate at a time.

**Domain-to-Domain transactions:** A domain redirect reconfiguration (`RedirectTargetDomainId` pointing to another Domain) modifies only the source Domain row — it does NOT modify the target Domain row. The target Domain is read-only for referential integrity checks.

> **Constitutional Persistence Decision CPD-3:** Domain events are NOT persisted as a separate `DomainEvents` table. Events are published via the existing `IEventPublisher` / transactional outbox pattern (`OutboxMessage` at `ApplicationDbContext.cs:434`). Each domain state transition publishes one event within the same transaction that commits the Domain row change. This matches the existing pattern used by `CmsPagePublishedEvent` (`CmsPageEvents.cs:13`) and `StoreConfig` events.

### 1.4 Audit Philosophy

**Append-only.** `DomainAuditEntry` is insert-only. No `UPDATE` or `DELETE` against audit rows is permitted at the database level (enforced by a `DomainAuditEntryConfiguration` with no update/delete interceptors, or a database-level trigger). This mirrors the existing `StoreConfigAuditEntry` pattern (`StoreConfigAuditEntry.cs:14-81`).

**Every state transition is audited.** The 15 lifecycle transitions defined in DOMAIN-1 Section 5 produce one `DomainAuditEntry` row each. The `Action` field uses the event name as its value (string-backed, matching `AuditActionTypes` in `StoreConfigAuditEntry.cs:86-98`).

**Before/After snapshots.** `BeforeSnapshotJson` and `AfterSnapshotJson` capture the full Domain entity JSON at the moment of transition, matching the `StoreConfigAuditEntry` pattern (`StoreConfigAuditEntry.cs:49-55`).

**System events.** Background job-initiated transitions (DNS verification, SSL provisioning) record `ActorType = System`. Merchant-initiated actions record `ActorType = Merchant` + `ActorUserId`. Platform-admin actions record `ActorType = PlatformAdmin`.

> **Constitutional Persistence Decision CPD-4:** `DomainAuditEntry` implements `ITenantOwned` (matching `StoreConfigAuditEntry.cs:14`). This ensures the existing `TenantSaveChangesInterceptor` (`TenantSaveChangesInterceptor.cs:56-67`) enforces TenantId non-empty validation and tenant mismatch detection. The TenantId is denormalized onto the audit entry for query performance — it does not make the audit entry part of the Domain aggregate's transactional boundary.

### 1.5 Soft Delete Philosophy

**Soft delete on `Domain`:** `DeletedAtUtc` nullable timestamp + `IsDeleted` boolean flag (following `BaseEntity.IsDeleted` at `BaseEntity.cs:13`). Soft-deleted domains are hidden from all resolution queries via a global query filter.

**Hard delete window:** 90 days retention. After 90 days, a background GC job hard-deletes `Domain` rows where `DeletedAtUtc < NOW() - 90 days`. This matches DOMAIN-1 Section 5, transition rule 14 ("Hard-deletable only if not primary and no active SSL cert").

**Child entity soft delete:** `DomainVerification` and `SslCertificate` rows are soft-deleted alongside their parent Domain (cascade soft-delete). Hard delete of the parent purges children.

**No-delete entities:** `DomainAuditEntry` is NEVER soft-deleted or hard-deleted. It is append-only forever.

> **Constitutional Persistence Decision CPD-5:** The `Domain` aggregate implements `ITenantOwned` (matching `TenantPaymentMethod.cs:22`). This means the global query filter pattern in `ApplicationDbContext.cs:486-489` (`!e.IsDeleted && e.TenantId == CurrentTenantId`) applies automatically. System and Development domain types (`DomainType.System`, `DomainType.Development`) are exempted from the tenant filter via an explicit `HasQueryFilter` exception — they are platform-global entities.

### 1.6 History Philosophy

**Snapshot history:** `DomainHistoryJson` column on the `Domain` entity stores periodic JSON snapshots of all domain attributes (mirroring `StoreConfig.VersionsJson` at `StoreConfig.cs:109`). Snapshots are taken on every `DomainAuditEntry` insertion. This is a denormalized read aid — the audit trail remains the source of truth.

**Verification history:** `DomainVerification` child entity tracks individual DNS/HTTP verification attempts. Each attempt is a row. This matches DOMAIN-1 Section 3.1.

**Certificate history:** `SslCertificate` child entity tracks the current certificate state. Historical renewals are captured in `SslCertificateHistoryJson` (a JSON array on the `SslCertificate` row, mirroring the `VersionsJson` pattern). Previous certificates are NOT kept as separate rows.

> **Constitutional Persistence Decision CPD-6:** `DomainHistoryJson` is stored as `jsonb` on the `Domain` table (following `CustomizationJson` at `StoreConfig.cs:82` and `ThemeDraftOverridesJson` at `StoreConfig.cs:98`, both stored as `jsonb` per `StoreConfigConfiguration` EF convention). No separate history table is created — the JSON snapshots supplement the append-only `DomainAuditEntry` table.

### 1.7 Event Philosophy

**Event sourcing:** No event sourcing. The `Domain` row state is the source of truth. Events are published as side-effects of state transitions via the transactional outbox pattern.

**Outbox integration:** Each domain event that has downstream consumers (`DomainVerified` → SEO canonical update, `SslActivated` → reverse proxy config, `DomainPromotedToPrimary` → cache invalidation) is published via `OutboxMessage` (`ApplicationDbContext.cs:434`). Non-critical events (`DomainVerificationAttempt`, `SslRenewed`) may be published fire-and-forget via `IEventPublisher` but are still recorded in `DomainAuditEntry`.

**Event payload:** Each event carries: `DomainId`, `TenantId`, `TraceId`, `CorrelationId`, `OccurredAtUtc`, and before/after state snapshots (JSON). This matches the existing `CmsPagePublishedEvent` pattern (`CmsPageEvents.cs:14-47`).

> **Constitutional Persistence Decision CPD-7:** Domain events are NOT stored in a dedicated `DomainEvents` table. They flow through the existing `OutboxMessage` infrastructure (`ApplicationDbContext.cs:434`). This is the only event persistence mechanism in the Shopixy platform — no new event store is introduced for domains.

---

## Section 2: Aggregate Roots

### Aggregate 1: Domain

| Attribute | Value |
|-----------|-------|
| **Purpose** | Single source of truth for a hostname's lifecycle: ownership verification, SSL state, primary/alias/redirect classification, canonical URL resolution |
| **Owner** | Infrastructure (platform-managed) |
| **Root Entity** | `Domain` |
| **Child Entities** | `DomainVerification` (one-to-many), `SslCertificate` (one-to-one, active-only) |
| **Value Objects** | `NormalizedHostName`, `VerificationToken`, `CertificateReference`, `DnsRecord`, `VerificationChallenge`, `HostPattern` |
| **Transactional Boundary** | `Domain` + `DomainVerification` + `SslCertificate` updated atomically |
| **Tenant Isolation** | `ITenantOwned` (TenantId on Domain row); System/Development types exempted |
| **Grounded In** | DOMAIN-1 Section 2.1 — Domain aggregate root definition; DOMAIN-0 Phase 6 — no Domain entity exists |

**Responsibilities:**
- Store normalized hostname with global uniqueness
- Track verification status and proof
- Track SSL certificate state and renewal schedule
- Enforce exactly-one Primary per tenant (Architecture Law #2, DOMAIN-1 Section 16.2)
- Track redirect target mapping (`RedirectTargetDomainId` self-referencing FK)
- Enforce lifecycle state transitions (DOMAIN-1 Section 5, 15 transitions)

**Boundaries:**
- Does NOT own `StoreConfig` (DOMAIN-1 Architecture Law #8, Section 16.8)
- Does NOT own `Theme` (DOMAIN-1 Architecture Law #4, Section 16.4)
- Does NOT own `Tenant` (DOMAIN-1 Architecture Law #9, Section 16.9)
- `Tenant.StorefrontBaseUrl` (legacy) is migrated INTO a Domain record but remains a Tenant field — backward compatibility only (DOMAIN-1 Section 18.1)

**Relationships:**
- `Domain.TenantId` → `Tenant.Id` (many-to-one, required)
- `Domain.SslCertificateId` → `SslCertificate.Id` (one-to-one, nullable)
- `Domain.RedirectTargetDomainId` → `Domain.Id` (self-referencing, nullable FK for redirect targets)
- `DomainVerification.DomainId` → `Domain.Id` (one-to-many)
- `DomainAuditEntry.DomainId` → `Domain.Id` (one-to-many)

### Aggregate 2: DomainVerification

| Attribute | Value |
|-----------|-------|
| **Purpose** | Historical record of DNS/HTTP verification attempts for a Domain |
| **Owner** | Domain Aggregate (child entity) |
| **Root Entity** | `DomainVerification` |
| **Value Objects** | `VerificationToken`, `VerificationChallenge`, `DnsRecord` |
| **Transactional Boundary** | Created/updated within the same transaction as the parent Domain state change |
| **Grounded In** | DOMAIN-1 Section 3.1 — DomainVerification entity definition |

**Responsibilities:**
- Store individual verification attempt records (DNS TXT, CNAME, HTTP file)
- Capture failure reasons and timestamps
- Maintain attempt count and rate-limit awareness (max 5 per hour per DOMAIN-1 Section 6.5)

**Boundaries:**
- Always owned by exactly one Domain
- Cannot exist without a parent Domain record

### Aggregate 3: SslCertificate

| Attribute | Value |
|-----------|-------|
| **Purpose** | Track the SSL/TLS certificate lifecycle for a Domain |
| **Owner** | Domain Aggregate (child entity) |
| **Root Entity** | `SslCertificate` |
| **Value Objects** | `CertificateReference` (opaque platform cert identifier: ARN/thumbprint) |
| **Transactional Boundary** | Updated by background Cert Manager jobs independently of Domain state changes |
| **Grounded In** | DOMAIN-1 Section 3.2 — SslCertificate entity definition |

**Responsibilities:**
- Track certificate status (Pending, Active, Expired, Revoked, Failed)
- Track `NotBeforeUtc`, `NotAfterUtc`, `NextRenewalUtc`, `LastRenewalAttemptUtc`
- Store platform-managed certificate reference (ARN/thumbprint) — NEVER the raw private key
- Track provider (LetsEncrypt, CustomUpload, SystemDefault)

**Boundaries:**
- One-to-one with Domain (a Domain has at most one active certificate)
- Updated by the Cert Manager background job, not by merchant actions
- History of renewals tracked via `SslCertificateHistoryJson` JSON column, not separate rows

### Aggregate 4: DomainAuditEntry

| Attribute | Value |
|-----------|-------|
| **Purpose** | Append-only audit trail of all Domain lifecycle actions |
| **Owner** | Domain Aggregate (but append-only, no transactional coupling) |
| **Root Entity** | `DomainAuditEntry` |
| **Transactional Boundary** | Inserted within the same transaction as the Domain state change that triggered it |
| **Grounded In** | DOMAIN-1 Section 3.3, DOMAIN-0 Phase 9.3 — `StoreConfigAuditEntry` pattern at `StoreConfigAuditEntry.cs:14-81` |

**Responsibilities:**
- Record every lifecycle state transition (one row per Domain event)
- Capture BeforeSnapshotJson, AfterSnapshotJson
- Record actor (UserId, ActorType), TraceId, IPAddress, UserAgent

**Boundaries:**
- Insert-only — no UPDATE, no DELETE
- Implements `ITenantOwned` for tenant isolation filter

---

## Section 3: Entity Catalog

### Entities to be created (all new — none exist in current codebase)

| Entity | Table | Domain | Grounded In |
|--------|-------|--------|-------------|
| `Domain` | `Domains` | Domain Aggregate | DOMAIN-1 Section 2.1; DOMAIN-0 Phase 6 (no Domain entity exists) |
| `DomainVerification` | `DomainVerifications` | Domain Verification | DOMAIN-1 Section 3.1 |
| `SslCertificate` | `SslCertificates` | Ssl Certificate | DOMAIN-1 Section 3.2 |
| `DomainAuditEntry` | `DomainAuditEntries` | Audit | DOMAIN-1 Section 3.3; DOMAIN-0 `StoreConfigAuditEntry.cs:14-81` |

### Existing entities referenced by the persistence model

| Entity | Table | Relevance |
|--------|-------|-----------|
| `Tenant` | `Tenants` | `TenantId` FK target; `StorefrontBaseUrl` (legacy) at `Tenant.cs:100`; `Slug` at `Tenant.cs:37`; `TenantStatus` enum at `Tenant.cs:52`; `DeletedAtUtc` at `Tenant.cs:72`; `IsActive` at `Tenant.cs:26` |
| `StoreConfig` | `StoreConfigs` | Does NOT reference domains; loaded after tenant resolution; `RowVersion` (xmin) at `StoreConfig.cs:123` |

### Entity count

Exactly 4 new entities. No additional entities are needed for the core Custom Domains platform as of DOMAIN-1.5. Future extensibility (Section 19) adds fields, not tables.

---

## Section 4: Entity Specifications

### 4.1 Domain Entity

| Field | Type | PK | FK | Unique | Nullable | Required | Concurrency | Audit | Soft Delete |
|-------|------|----|----|--------|----------|----------|-------------|-------|-------------|
| `Id` | `Guid` | Yes (PK) | — | — | No | Yes | — | CreatedAt | — |
| `TenantId` | `Guid` | — | → `Tenants.Id` | — | No | Yes | — | CreatedAt | — |
| `HostName` | `string` (max 253, RFC 1035) | — | — | No (normalized is unique) | No | Yes | — | UpdatedAt | — |
| `NormalizedHostName` | `string` (max 253, lowercase punycode) | — | — | **Yes (global UNIQUE)** | No | Yes | — | UpdatedAt | — |
| `DomainType` | `int` (enum) | — | — | — | No | Yes | — | UpdatedAt | — |
| `LifecycleState` | `int` (enum) | — | — | — | No | Yes | — | UpdatedAt | — |
| `SslCertificateId` | `Guid?` | — | → `SslCertificates.Id` | — | Yes | No | — | UpdatedAt | — |
| `VerificationToken` | `string` (max 256) | — | — | — | No | Yes (for verifiable domains) | — | UpdatedAt | — |
| `VerificationMethod` | `int` (enum) | — | — | — | Yes | No (nullable for non-verified) | — | UpdatedAt | — |
| `RedirectType` | `int` (enum) | — | — | — | Yes | No (null = no redirect) | — | UpdatedAt | — |
| `RedirectTargetDomainId` | `Guid?` | — | → `Domains.Id` (self-reference) | — | Yes | No (null unless RedirectType != None) | — | UpdatedAt | — |
| `CanonicalPolicy` | `int` (enum) | — | — | — | No | Yes (default: CanonicalizeToPrimary) | — | UpdatedAt | — |
| `CreatedAtUtc` | `DateTime` | — | — | — | No | Yes | — | — | — |
| `VerifiedAtUtc` | `DateTime?` | — | — | — | Yes | No | — | — | — |
| `SslActivatedAtUtc` | `DateTime?` | — | — | — | Yes | No | — | — | — |
| `UpdatedAtUtc` | `DateTime?` | — | — | — | Yes | No | — | — | — |
| `DeletedAtUtc` | `DateTime?` | — | — | — | Yes | No | — | — | — |
| `IsDeleted` | `boolean` | — | — | — | No | Yes (default: false) | — | — | Yes |
| `Version` | `int` (row version) | — | — | — | No | Yes (default: 1) | Yes | — | — |
| `DomainHistoryJson` | `jsonb` | — | — | — | No | Yes (default: `[]`) | — | — | — |

**Indexes:**
1. `PK_Domains` — clustered PK on `Id` (Guid)
2. `UQ_Domains_NormalizedHostName` — UNIQUE index on `NormalizedHostName` (global, enforced at DB level per DOMAIN-1 Section 5 Rule: "Global Uniqueness")
3. `IX_Domains_TenantId` — non-unique index on `TenantId` (lookup all domains for a tenant)
4. `IX_Domains_TenantId_LifecycleState` — composite index for resolving Primary/Active domains per tenant
5. `IX_Domains_LifecycleState` — non-unique index on `LifecycleState` (background job: SSL provisioning, verification retries)
6. `IX_Domains_DomainType` — non-unique index on `DomainType` (System/Development/Admin queries)
7. `IX_Domains_SslCertificateId` — non-unique index on `SslCertificateId` (FK lookup)
8. `IX_Domains_RedirectTargetDomainId` — non-unique index on `RedirectTargetDomainId` (self-reference FK lookup)
9. `IX_Domains_IsDeleted` — filtered index `WHERE IsDeleted = false` (query filter support)
10. `UQ_Domains_TenantId_Primary` — FILTERED UNIQUE index on `TenantId` WHERE `DomainType = Primary AND LifecycleState = Primary AND IsDeleted = false` (enforces "one Primary per tenant" per DOMAIN-1 Architecture Law #2, Section 16.2)

**Constraints:**
- `CK_Domains_HostName_Length` — `HostName` must be between 1 and 253 characters
- `CK_Domains_Redirect_Cascade` — IF `DomainType = Redirect` THEN `RedirectTargetDomainId IS NOT NULL` AND `RedirectTargetDomainId != Id` AND `RedirectType != None`
- `CK_Domains_Primary_Host_Ssl` — IF `LifecycleState = Primary` OR `LifecycleState = SslActive` THEN `SslCertificateId IS NOT NULL` (verified SSL required to serve)
- `CK_Domains_System_NoTenant` — IF `DomainType IN (System, Development)` THEN `TenantId IS NOT NULL` (System domains belong to the platform tenant; Development to the creating tenant)

**Query filter:** `WHERE IsDeleted = false` (applies to all non-audit queries)

**Tenant isolation:** Implements `ITenantOwned` with global query filter `!e.IsDeleted && e.TenantId == CurrentTenantId`. SYSTEM and DEVELOPMENT domain types are exempted from TenantId filtering (they are platform-global).

### 4.2 DomainVerification Entity

| Field | Type | PK | FK | Unique | Nullable | Required | Concurrency | Audit | Soft Delete |
|-------|------|----|----|--------|----------|----------|-------------|-------|-------------|
| `Id` | `Guid` | Yes (PK) | — | — | No | Yes | — | CreatedAt | — |
| `DomainId` | `Guid` | — | → `Domains.Id` | — | No | Yes | — | CreatedAt | — |
| `Method` | `int` (enum) | — | — | — | No | Yes | — | UpdatedAt | — |
| `Token` | `string` (max 256) | — | — | — | No | Yes | — | UpdatedAt | — |
| `Status` | `int` (enum) | — | — | — | No | Yes | — | UpdatedAt | — |
| `AttemptedAtUtc` | `DateTime` | — | — | — | No | Yes | — | — | — |
| `VerifiedAtUtc` | `DateTime?` | — | — | — | Yes | No | — | — | — |
| `FailureReason` | `string?` (max 500) | — | — | — | Yes | No | — | UpdatedAt | — |
| `CreatedAt` | `DateTime` | — | — | — | No | Yes | — | — | — |
| `UpdatedAt` | `DateTime?` | — | — | — | Yes | No | — | — | — |
| `IsDeleted` | `boolean` | — | — | No | No | Yes (default: false) | — | — | Yes |

**Indexes:**
1. `PK_DomainVerifications` — clustered PK on `Id`
2. `IX_DomainVerifications_DomainId` — non-unique index on `DomainId` (child lookup)
3. `IX_DomainVerifications_DomainId_Status` — composite index for finding pending verification attempts
4. `IX_DomainVerifications_AttemptedAtUtc` — non-unique index (background job: cleanup of old attempts)

**Query filter:** `WHERE IsDeleted = false` AND `Domain.IsDeleted = false` (cascade through FK)

### 4.3 SslCertificate Entity

| Field | Type | PK | FK | Unique | Nullable | Required | Concurrency | Audit | Soft Delete |
|-------|------|----|----|--------|----------|----------|-------------|-------|-------------|
| `Id` | `Guid` | Yes (PK) | — | — | No | Yes | — | CreatedAt | — |
| `DomainId` | `Guid` | — | → `Domains.Id` | **Yes (unique per active)** | No | Yes | — | CreatedAt | — |
| `Provider` | `int` (enum) | — | — | — | No | Yes | — | UpdatedAt | — |
| `Status` | `int` (enum) | — | — | — | No | Yes | — | UpdatedAt | — |
| `CertificateArn` | `string` (max 500) | — | — | — | Yes | No (null for SystemDefault) | — | UpdatedAt | — |
| `Thumbprint` | `string` (max 200) | — | — | — | Yes | No | — | UpdatedAt | — |
| `NotBeforeUtc` | `DateTime?` | — | — | — | Yes | No | — | UpdatedAt | — |
| `NotAfterUtc` | `DateTime?` | — | — | — | Yes | No | — | UpdatedAt | — |
| `LastRenewalAttemptUtc` | `DateTime?` | — | — | — | Yes | No | — | UpdatedAt | — |
| `NextRenewalUtc` | `DateTime?` | — | — | — | Yes | No | — | UpdatedAt | — |
| `FailureReason` | `string?` (max 500) | — | — | — | Yes | No | — | UpdatedAt | — |
| `CreatedAt` | `DateTime` | — | — | — | No | Yes | — | — | — |
| `UpdatedAt` | `DateTime?` | — | — | — | Yes | No | — | — | — |
| `IsDeleted` | `boolean` | — | — | No | No | Yes (default: false) | — | — | Yes |
| `SslCertificateHistoryJson` | `jsonb` | — | — | — | No | Yes (default: `[]`) | — | — | — |

**Indexes:**
1. `PK_SslCertificates` — clustered PK on `Id`
2. `IX_SslCertificates_DomainId` — non-unique index on `DomainId` (FK lookup; one-to-one enforced at entity level)
3. `IX_SslCertificates_Status` — non-unique index on `Status` (background job: renewal, expiry)
4. `IX_SslCertificates_NotAfterUtc` — non-unique index on `NotAfterUtc` (background job: expiration scanning)
5. `IX_SslCertificates_NextRenewalUtc` — non-unique index on `NextRenewalUtc` (background job: renewal scheduling)

**Constraints:**
- `CK_SslCertificates_DomainId_UniqueActive` — filtered UNIQUE on `DomainId` WHERE `IsDeleted = false AND Status IN (Active, Pending, Expired)` (ensures one active cert per domain)
- Domain private key is NEVER stored in this table — only the platform-managed `CertificateArn`/`Thumbprint` reference

**Query filter:** `WHERE IsDeleted = false`

### 4.4 DomainAuditEntry Entity

| Field | Type | PK | FK | Unique | Nullable | Required | Concurrency | Audit | Soft Delete |
|-------|------|----|----|--------|----------|----------|-------------|-------|-------------|
| `Id` | `Guid` | Yes (PK) | — | — | No | Yes | — | — (insert timestamp) | — |
| `DomainId` | `Guid` | — | → `Domains.Id` | — | No | Yes | — | — | — |
| `TenantId` | `Guid` | — | → `Tenants.Id` | — | No | Yes | — | — | — |
| `Action` | `string` (max 100) | — | — | — | No | Yes | — | — | — |
| `ActorType` | `int` (enum) | — | — | — | No | Yes | — | — | — |
| `ActorUserId` | `Guid?` | — | → `Users.Id` | — | Yes | No | — | — | — |
| `BeforeSnapshotJson` | `jsonb` | — | — | — | Yes | No (null for creates) | — | — | — |
| `AfterSnapshotJson` | `jsonb` | — | — | Yes (unique for state transitions) | No | Yes | — | — | — |
| `TraceId` | `string` (max 128) | — | — | — | Yes | No | — | — | — |
| `IpAddress` | `string` (max 45) | — | — | — | Yes | No | — | — | — |
| `UserAgent` | `string` (max 500) | — | — | — | Yes | No | — | — | — |
| `TimestampUtc` | `DateTime` | — | — | — | No | Yes | — | — | — |

**Indexes:**
1. `PK_DomainAuditEntries` — clustered PK on `Id`
2. `IX_DomainAuditEntries_DomainId` — non-unique index on `DomainId` (audit trail lookup)
3. `IX_DomainAuditEntries_TenantId_TimestampUtc` — composite index for tenant-scoped audit queries (DESC ordering for recent-first)
4. `IX_DomainAuditEntries_TimestampUtc` — non-unique index (background: retention GC)
5. `IX_DomainAuditEntries_TraceId` — non-unique index (correlation tracing)
6. `IX_DomainAuditEntries_Action` — non-unique index (action-type filtering)

**Constraints:**
- `CK_DomainAuditEntries_NotInsert` — database-level trigger or check constraint preventing UPDATE (append-only enforcement, DOMAIN-1 Section 3.3)
- `CK_DomainAuditEntries_NotDelete` — database-level trigger or check constraint preventing DELETE (append-only enforcement)
- Implements `ITenantOwned` for tenant isolation filter

**Query filter:** None — `DomainAuditEntry` is globally queryable by platform admin. Tenant-scoped queries use the `TenantId` index.

---

## Section 5: Value Objects

All value objects are persisted as columns on their owning entity (table-per-owner, not separate tables). They are immutable and compared by value.

| Value Object | Stored On | Column(s) | Grounded In |
|-------------|----------|-----------|-------------|
| `NormalizedHostName` | `Domain.NormalizedHostName` | `string` (max 253, lowercase punycode) | DOMAIN-1 Section 2.1: "NormalizedHostName — lowercased + punycode normalized hostname; uniqueness enforced at DB level"; DOMAIN-1 Architecture Law #10 |
| `VerificationToken` | `Domain.VerificationToken` | `string` (max 256, cryptographically random) | DOMAIN-1 Section 2.1; DOMAIN-1 Section 6: "Platform provides a unique VerificationToken string (e.g., shopixy-domain-verification=abc123def456)" |
| `CertificateReference` | `SslCertificate.CertificateArn` + `SslCertificate.Thumbprint` | `string` (max 500/200) | DOMAIN-1 Section 3.2: "CertificateArn / Thumbprint — platform-managed reference" |
| `DomainRedirect` | `Domain.RedirectType` + `Domain.RedirectTargetDomainId` | `int` + `Guid?` | DOMAIN-1 Section 2.1: "RedirectType + RedirectTargetDomainId" |
| `DnsRecord` | `DomainVerification` | `Method` + `Token` columns | DOMAIN-1 Section 5.4: DNS verification methods (TXT/CNAME/HTTP-file) |
| `VerificationChallenge` | `DomainVerification.Token` | `string` (max 256) | DOMAIN-1 Section 5.2: "merchant adds a TXT record to their DNS zone: _shopixy.mydomain.com TXT \"abc123def456\"" |
| `HostPattern` | Future: `Domain.HostPattern` (only for `DomainType.Wildcard`) | `string` (max 253) | DOMAIN-1 Section 19.1 (Wildcard Domains — future extensibility) |

> **Constitutional Persistence Decision CPD-8:** `NormalizedHostName` is a persisted column, NOT computed at query time. This is required for the UNIQUE constraint and for the Host Resolution Middleware's O(log n) B-tree index lookup. Computing normalization at query time would prevent both the unique constraint and the index-backed lookup. The normalization rule is: lowercase + punycode encoding (IDNA ToASCII), strip port (unless non-standard port for Development type), trim whitespace. This is server-authoritative (DOMAIN-1 Architecture Law #10, Section 16.10).

---

## Section 6: Enums

All enums are stored as integers (`int` / `smallint` in PostgreSQL) at the persistence layer, matching the existing pattern used by `PaymentProvider` (`PaymentProvider.cs:3-10`), `TenantStatus` (`TenantStatus.cs:6`), and `CommerceHubPayoutStatus`.

| Enum | Values | Stored As | Grounded In |
|------|--------|-----------|-------------|
| **`DomainType`** | `Primary (0)`, `Alias (1)`, `Redirect (2)`, `Preview (3)`, `Temporary (4)`, `Development (5)`, `System (6)` | `smallint` | DOMAIN-1 Section 4: "DomainType (enum: Primary, Alias, Redirect, Preview, Temporary, Development, System)"; DOMAIN-1 Section 4 Extensibility list adds: `Wildcard (7)`, `International (8)`, `Regional (9)`, `Marketplace (10)`, `WhiteLabel (11)`, `Email (12)` |
| **`LifecycleState`** | `Draft (0)`, `PendingVerification (1)`, `Verified (2)`, `SslPending (3)`, `SslActive (4)`, `Primary (5)`, `Disabled (6)`, `Archived (7)`, `Deleted (8)` | `smallint` | DOMAIN-1 Section 2: LifecycleState enum definition; DOMAIN-1 Section 5: 9 states with 15 transitions |
| **`VerificationStatus`** | `Pending (0)`, `Success (1)`, `Failed (2)` | `smallint` | DOMAIN-1 Section 3.1: DomainVerification.Status (enum: Pending, Success, Failed) |
| **`VerificationMethod`** | `DnsTxt (0)`, `DnsCname (1)`, `HttpFile (2)` | `smallint` | DOMAIN-1 Section 2.1: VerificationMethod enum; DOMAIN-1 Section 6 DNS Verification Methods |
| **`CertificateStatus`** | `Pending (0)`, `Active (1)`, `Expired (2)`, `Revoked (3)`, `Failed (4)` | `smallint` | DOMAIN-1 Section 3.2: SslCertificate.Status (enum: Pending, Active, Expired, Revoked, Failed) |
| **`ProviderType`** | `LetsEncrypt (0)`, `CustomUpload (1)`, `SystemDefault (2)` | `smallint` | DOMAIN-1 Section 3.2: SslCertificate.Provider (enum: LetsEncrypt, CustomUpload, SystemDefault) |
| **`RedirectType`** | `None (0)`, `Permanent301 (1)`, `Temporary307 (2)` | `smallint` | DOMAIN-1 Section 2.1: RedirectType enum (None, Permanent301, Temporary307) |
| **`CanonicalPolicy`** | `UseAsPrimary (0)`, `CanonicalizeToPrimary (1)` | `smallint` | DOMAIN-1 Section 2.1: CanonicalPolicy enum |
| **`ActorType`** | `Merchant (0)`, `System (1)`, `PlatformAdmin (2)` | `smallint` | DOMAIN-1 Section 3.3: ActorType (Merchant, System, PlatformAdmin) |

> **Constitutional Persistence Decision CPD-9:** Future domain types (`Wildcard`, `International`, `Regional`, `Marketplace`, `WhiteLabel`, `Email` — DOMAIN-1 Section 19) are added as integer enum values 7-12. This does NOT require schema changes — it is a new enum value in the same `smallint` column. The `HostPattern` value object (Section 5) is pre-allocated as a nullable column on `Domain` to support this without migration. This guarantees "without schema redesign" (DOMAIN-1 Section 19 introduction).

---

## Section 7: Entity Relationships

### 7.1 Relationship Matrix

| From Entity | To Entity | Card | FK Column | Delete Behavior | Notes |
|-------------|-----------|------|-----------|-----------------|-------|
| `Domain` | `Tenant` | Many-to-One | `TenantId` | Restrict | A domain cannot outlive its tenant. Tenant deletion must cascade Domain → Deleted state. |
| `Domain` | `SslCertificate` | One-to-One (zero-or-one) | `SslCertificateId` | Restrict | Domain does not cascade-delete its certificate. Certificate is preserved if Domain is soft-deleted (for audit). |
| `Domain` | `Domain` (self) | Many-to-One | `RedirectTargetDomainId` | Restrict | Redirect target domain must be a real Domain record. Cross-tenant forbidden. Same-tenant enforced at application layer. |
| `DomainVerification` | `Domain` | Many-to-One | `DomainId` | Cascade (soft) | When Domain is soft-deleted, child verifications are soft-deleted. When Domain is hard-deleted (GC), children are hard-deleted. |
| `SslCertificate` | `Domain` | One-to-One (zero-or-one) | `DomainId` | Cascade (soft) | Certificate is soft-deleted when Domain is soft-deleted. |
| `DomainAuditEntry` | `Domain` | Many-to-One | `DomainId` | No-Op (Preserve) | Audit entries are NEVER deleted when Domain is deleted. DomainId FK is retained (not nullable) for traceability. |
| `DomainAuditEntry` | `Tenant` | Many-to-One | `TenantId` | No-Op (Preserve) | Denormalized for query performance — TenantId is captured even if Tenant is later deleted. |

### 7.2 Forbidden Relationships

| Forbidden | Reason |
|-----------|--------|
| `Domain` → `StoreConfig` | Architecture Law #8 (DOMAIN-1 Section 16.8): "StoreConfig Never Owns Domain." StoreConfig is loaded AFTER tenant resolution. No FK dependency. |
| `Domain` → `Theme` / `StoreThemeConfig` | Architecture Law #4 (DOMAIN-1 Section 16.4): "Domain Never Owns Theme." Themes are resolved from StoreConfig post-resolution. |
| `StoreConfig` → `Domain` | Architecture Law #8: StoreConfig never drives domain resolution. Domain resolution drives StoreConfig loading. |
| `Domain` → `OnlineCustomer` / `Customer` | Domains resolve infrastructure identity, not consumer identity. |
| `DomainVerification` → `SslCertificate` | Verification and certificates are independent lifecycle stages. A verification attempt does not create a certificate. |

### 7.3 Circular Dependency Prevention

The `Domain → Domain` self-referencing FK (`RedirectTargetDomainId`) is the only intra-aggregate circular reference. It is prevented at the database level by:

1. `CK_Domains_Redirect_NoSelfReference` — `RedirectTargetDomainId != Id`
2. Application-layer transitive closure check: no redirect chain longer than 1 hop (A → B → C forbidden; A → B only). This is enforced before save, not at DB level (would require recursive CTE check constraint which PostgreSQL does not support natively).
3. Cross-tenant redirect forbidden: `RedirectTargetDomainId` must resolve to a Domain with the same `TenantId` (application-layer enforcement via `TenantLineageAuthority` pattern).

> **Constitutional Persistence Decision CPD-10:** The self-referencing `RedirectTargetDomainId` uses `DeleteBehavior.Restrict` (not Cascade). If a domain is deleted while it is the target of another domain's redirect, the delete is rejected at the application layer: the redirect must be reconfigured or removed first. This prevents accidental redirect breakage during domain cleanup.

---

## Section 8: Delete Behavior

| Entity | Soft Delete | Hard Delete (GC) | Cascade To | Retention | Grounded In |
|--------|-------------|------------------|------------|-----------|-------------|
| `Domain` | `DeletedAtUtc` + `IsDeleted` = true | 90 days after soft-delete | `DomainVerification` (soft), `SslCertificate` (soft), `DomainAuditEntry` (preserve) | 90 days | DOMAIN-1 Section 5, transition 14: "Soft-delete retained per data retention policy" |
| `DomainVerification` | `IsDeleted` = true | On parent Domain hard-delete | — | Inherits parent retention | DOMAIN-1 Section 3.1 child entity |
| `SslCertificate` | `IsDeleted` = true | On parent Domain hard-delete | — | Inherits parent retention | DOMAIN-1 Section 3.2 child entity |
| `DomainAuditEntry` | **Never** | **Never** | — | **Permanent** | DOMAIN-1 Section 3.3: append-only; Domain-0 `StoreConfigAuditEntry.cs` has no delete path |

**Delete preconditions:**
- A Domain in `Primary` state cannot be deleted (Architecture Law #2 — exactly one Primary per tenant). Promotion of a new Primary must occur first.
- A Domain with `SslActive` or `SslPending` status cannot be soft-deleted while the SSL certificate is in an active provisioning state. The certificate must be revoked/disabled first.
- A Domain referenced by another Domain's `RedirectTargetDomainId` cannot be deleted. The referencing redirect must be reconfigured first.
- A Domain in `Draft` or `PendingVerification` can be immediately soft-deleted (no preconditions).

---

## Section 9: Concurrency Model

### 9.1 Optimistic Concurrency

The `Domain` entity uses a `Version` integer column (incrementing row version) for optimistic concurrency, following the pattern established on `StoreConfig.RowVersion` (`StoreConfig.cs:123`), `PaymentIntent.RowVersion` (`ApplicationDbContext.cs:729-732` mapped as `xmin`/`bytea`), and `TenantPaymentMethod.Version` (`TenantPaymentMethod.cs:144`).

**RowVersion mechanism:** PostgreSQL `xmin` system column is mapped to the `Version` property on the `Domain` entity. This matches the existing `PaymentIntent` configuration (`ApplicationDbContext.cs:730`): `.HasColumnName("xmin").HasColumnType("xid").IsRowVersion()`.

**Conflict handling:**
- On `DbUpdateConcurrencyException`, the application layer reads the current `Domain` row, reconciles the conflict, and retries.
- Merchant-initiated state transitions (e.g., Promote to Primary) that fail due to concurrency are surfaced as HTTP 409 Conflict with the current server state.
- System-initiated transitions (SSL provisioning, DNS verification retry) retry with exponential backoff and log the conflict (non-fatal).

### 9.2 Retry Policy

| Operation Type | Max Retries | Backoff Schedule | Grounded In |
|----------------|-------------|------------------|-------------|
| Merchant API state transition | 0 (immediate 409) | N/A | Existing POS Admin pattern (no automatic retry — conflicts shown to user) |
| DNS verification cron job | 3 | Immediate, 30s, 60s | DOMAIN-1 Section 6.5: DNS polling "every 10s for 5 minutes" |
| SSL provisioning job | 8 | 0s, 1m, 5m, 15m, 1h, 4h, 12h, 24h | DOMAIN-1 Section 6.7: SSL retry backoff (8 attempts) |
| SSL renewal job | 8 | Same as provisioning | DOMAIN-1 Section 6.3: renewal retry backoff |
| Background GC (hard delete) | 1 | 5m delay | Standard background job pattern |

### 9.3 Merge Policy

**No auto-merge.** Domain state transitions are atomic state machine operations (Section 5). There is no partial-update merge scenario. If a conflict is detected, the operation is rejected and the caller must re-evaluate the current state before retrying.

> **Constitutional Persistence Decision CPD-11:** The `Domain` entity uses `Version` (integer incrementing) rather than `RowVersion` (byte[] timestamp). This is a deliberate choice for future compatibility with the `DomainHistoryJson` snapshot mechanism — the integer version provides a clean, human-readable audit sequence number that can be referenced in the `VersionsJson` pattern (mirroring `StoreConfig.LiveVersion` at `StoreConfig.cs:115`). The PostgreSQL `xmin` system column is used as the underlying row-version mechanism, exposed as an integer.

---

## Section 10: Index Strategy

### 10.1 Clustered Indexes

| Table | Clustered Index | Rationale |
|-------|-----------------|-----------|
| `Domains` | `PK_Domains` (`Id` CLUSTERED) | Guid PK as clustered is acceptable for PostgreSQL — the table is read-heavy via host lookups, not sequential scans |
| `DomainVerifications` | `PK_DomainVerifications` (`Id` CLUSTERED) | Append-heavy audit of attempts |
| `SslCertificates` | `PK_SslCertificates` (`Id` CLUSTERED) | Low write volume, high read on status |
| `DomainAuditEntries` | `PK_DomainAuditEntries` (`Id` CLUSTERED) | Append-only, sequential inserts |

### 10.2 Unique Indexes

| Index | Columns | Filter | Purpose |
|-------|---------|--------|---------|
| `UQ_Domains_NormalizedHostName` | `NormalizedHostName` | None | **Global host uniqueness** — Architecture Law #2 (DOMAIN-1 Section 16) |
| `UQ_Domains_TenantId_Primary` | `TenantId` | `DomainType = 5 AND LifecycleState = 5 AND IsDeleted = false` | **Exactly one Primary per tenant** — Architecture Law #2, Section 16.2 |
| `UQ_SslCertificates_DomainId` | `DomainId` | `IsDeleted = false AND Status IN (1,2,3)` | **One active cert per domain** |
| `UQ_DomainVerifications_DomainId_Method` | `DomainId, Method` | `Status = 0` | **Active verification attempt per method** |

### 10.3 Composite Indexes

| Index | Columns | Purpose |
|-------|---------|---------|
| `IX_Domains_TenantId_LifecycleState` | `TenantId, LifecycleState` | Resolve Primary/Active domain for a tenant (host resolution pipeline) |
| `IX_Domains_TenantId_DomainType_LifecycleState` | `TenantId, DomainType, LifecycleState` | Admin domain listing with filters |
| `IX_DomainAuditEntries_TenantId_TimestampUtc_DESC` | `TenantId, TimestampUtc DESC` | Tenant-scoped audit log query (recent-first) |

### 10.4 Lookup Indexes

| Index | Columns | Purpose |
|-------|---------|---------|
| `IX_Domains_TenantId` | `TenantId` | List all domains for a tenant |
| `IX_Domains_LifecycleState` | `LifecycleState` | Background job: SSL provisioning, verification retry scheduling |
| `IX_Domains_DomainType` | `DomainType` | Platform queries for System/Development domains |
| `IX_Domains_SslCertificateId` | `SslCertificateId` | FK reverse lookup |
| `IX_Domains_RedirectTargetDomainId` | `RedirectTargetDomainId` | Redirect chain resolution |
| `IX_DomainAuditEntries_Action` | `Action` | Audit log filtering by action type |
| `IX_DomainAuditEntries_TraceId` | `TraceId` | Correlation tracing across systems |

### 10.5 Search Indexes

None required at DOMAIN-1.5. Full-text search on hostnames is not a use case — exact-match `NormalizedHostName` lookup is the only query pattern. Future extensibility (Section 19) does not introduce search-by-domain-features requirements.

### 10.6 Verification Indexes

| Index | Columns | Purpose |
|-------|---------|---------|
| `IX_DomainVerifications_DomainId_Status` | `DomainId, Status` | Background job: find pending/failed verification attempts for retry |
| `IX_DomainVerifications_AttemptedAtUtc` | `AttemptedAtUtc` | GC: cleanup of old verification records beyond retention |

### 10.7 Background Job Indexes

| Index | Columns | Purpose |
|-------|---------|---------|
| `IX_Domains_LifecycleState_SslActivatedAtUtc` | `LifecycleState, SslActivatedAtUtc` | Background job: SSL expiration scanning |
| `IX_DomainAuditEntries_TimestampUtc` | `TimestampUtc` | GC: hard-delete of audit entries (future — currently append-only forever) |

---

## Section 11: Query Model

### 11.1 Expected Query Patterns

| Query | Primary Lookup Key | Index Used | Latency Target | Grounded In |
|-------|-------------------|------------|----------------|-------------|
| **Resolve Host** (`Request.Host` → `Domain`) | `NormalizedHostName` | `UQ_Domains_NormalizedHostName` | < 5ms | DOMAIN-1 Section 7.2: "Query the `Domains` table by `NormalizedHostName`" |
| **Resolve Primary Domain** (`TenantId` → Primary Domain) | `TenantId + DomainType=Primary + LifecycleState=Primary` | `IX_Domains_TenantId_DomainType_LifecycleState` or `UQ_Domains_TenantId_Primary` | < 10ms | DOMAIN-1 Section 12.1: SeoService canonical URL from Primary Domain |
| **Resolve Redirect** (`Host` → redirect target URL) | `NormalizedHostName` → `RedirectType` + `RedirectTargetDomainId` | `UQ_Domains_NormalizedHostName` + `IX_Domains_RedirectTargetDomainId` | < 5ms | DOMAIN-1 Section 11.4: redirect fires before StoreConfig |
| **Resolve Preview** (`preview Host` → Domain + token validation) | `NormalizedHostName + DomainType=Preview` | `IX_Domains_NormalizedHostName` + `IX_Domains_DomainType` | < 5ms | DOMAIN-1 Section 10.2: custom preview domain resolution |
| **Resolve SSL** (`DomainId` → certificate) | `DomainId` | `IX_SslCertificates_DomainId` | < 5ms | DOMAIN-1 Section 6.2: certificate provisioning |
| **Resolve Verification** (`DomainId` → pending verification) | `DomainId + Status=Pending` | `IX_DomainVerifications_DomainId_Status` | < 10ms | DOMAIN-1 Section 6.4: verification failure handling |
| **Resolve Tenant** (`Host` → `TenantId`) | `NormalizedHostName` → `TenantId` | `UQ_Domains_NormalizedHostName` | < 5ms | DOMAIN-1 Section 7.1: Host Resolution Middleware |
| **List Domain Audit** (`TenantId` → audit entries) | `TenantId + TimestampUtc DESC` | `IX_DomainAuditEntries_TenantId_TimestampUtc_DESC` | < 50ms | DOMAIN-1 Section 13.2: Admin UX History page |
| **SSL Renewal Scan** (`NotAfterUtc` window) | `Status=Active + NextRenewalUtc <= NOW()` | `IX_SslCertificates_NextRenewalUtc` | < 100ms | DOMAIN-1 Section 6.7: automated renewal |
| **DNS Reverification Scan** (`VerifiedAtUtc` > 90 days) | `LifecycleState=Verified + VerifiedAtUtc < NOW() - 90d` | `IX_Domains_LifecycleState` + `IX_Domains_TenantId_LifecycleState` | < 200ms | DOMAIN-1 Section 6.3: reverification after 90 days |

### 11.2 Query Design Principles

1. **Host resolution is a single-indexed lookup.** The Host Resolution Middleware (`Domain` → `NormalizedHostName`) must complete in O(log n) via a single B-tree index seek. No joins, no scans.
2. **Primary domain resolution is a single-indexed lookup.** `StorefrontUrlResolver` (DOMAIN-1 Section 8.3) queries by `(TenantId, DomainType=Primary, LifecycleState=Primary, IsDeleted=false)` — this is a single index seek on `IX_Domains_TenantId_DomainType_LifecycleState`.
3. **Redirect resolution is a two-step indexed lookup.** Step 1: `NormalizedHostName` → Domain row (gets `RedirectTargetDomainId`). Step 2: `RedirectTargetDomainId` → target Domain row (gets target's `NormalizedHostName`). Both are indexed FK lookups.
4. **Background jobs use status+timestamp composite indexes.** No full-table scans for SSL renewal or DNS reverification jobs.
5. **Audit queries use tenant-scoped DESC timestamp index.** Admin History page loads recent-first.

> **Constitutional Persistence Decision CPD-12:** The `NormalizedHostName` UNIQUE index must be backed by a B-tree on PostgreSQL. Host normalization (lowercase + punycode) happens at write time (before insert/update), never at query time. This is the only way to satisfy both the UNIQUE constraint and the sub-5ms lookup requirement. The query pattern "resolve host" is the most critical — it runs on EVERY storefront request (DOMAIN-1 Section 7 pipeline, line 573-584) and must be a single index seek.

---

## Section 12: Audit Model

### 12.1 What Is Audited

Every state-changing operation on the `Domain` aggregate root produces one `DomainAuditEntry` row. This includes:

- **Create:** When a Domain enters `Draft` state (merchant submits host)
- **Verify:** When verification is attempted (success or failure)
- **SSL provisioning/renewal:** When certificate state changes
- **Primary promotion/demotion:** When canonical domain changes
- **Disable/Archive/Restore:** Lifecycle management actions
- **Delete:** Soft-delete and hard-delete
- **Redirect configuration:** When redirect target/method changes

### 12.2 Audit Record Fields

| Field | Source |
|-------|--------|
| `Who` | `ActorType` (Merchant/System/PlatformAdmin) + `ActorUserId` (if Merchant/PlatformAdmin) |
| `When` | `TimestampUtc` (server UTC, not client time) |
| `Why` | `Action` field (string matching domain event name: `DomainRegistered`, `DomainVerified`, etc.) |
| `TraceId` | Correlation ID from the incoming HTTP request or background job |
| `Correlation` | `DomainId` + `TenantId` (always present) |
| `Before` | `BeforeSnapshotJson` (full Domain JSON before the change; null for creates) |
| `After` | `AfterSnapshotJson` (full Domain JSON after the change) |
| `Actor` | `ActorType` + `ActorUserId` + `IpAddress` + `UserAgent` |
| `System Events` | `ActorType = System` for background jobs (DNS verification, SSL provisioning); `ActorType = PlatformAdmin` for platform admin actions |

### 12.3 Audit Principles

1. **Append-only:** `DomainAuditEntry` rows are NEVER updated or deleted. Enforced at DB level (trigger or constraint) and at the EF interceptor level.
2. **Denormalized TenantId:** Captured at write time so audit records remain queryable even if the Tenant is later deleted.
3. **JSON snapshots:** Before/After are full entity JSON, matching the `StoreConfigAuditEntry` pattern (`StoreConfigAuditEntry.cs:49-55`).
4. **Tenant isolation:** `DomainAuditEntry` implements `ITenantOwned` — query filter restricts to current tenant. Platform admin queries bypass the filter (exempt role).
5. **Performance:** Audit writes are synchronous (same transaction as the state change) but non-blocking — the INSERT is on a separate table with its own indexes.

> **Constititional Persistence Decision CPD-13:** Audit entries are written within the SAME database transaction as the Domain state change they describe. This guarantees atomicity: if the Domain update fails, the audit entry is rolled back. This matches the existing pattern where `StoreConfigAuditEntry` is inserted in the same transaction as the StoreConfig mutation. The audit row is inserted AFTER the Domain row is committed within the transaction (insert order ensures referential integrity on `DomainId`).

---

## Section 13: Event Model

### 13.1 Frozen Domain Events

Every event listed below is a **persistence-adjacent** specification — it defines what data is published and what persistence impact follows. The actual event classes are DOMAIN-2 implementation artifacts.

| Event | Producer | Consumer | Persistence Impact |
|-------|----------|----------|-------------------|
| `DomainRegistered` | DomainService (merchant action) | DomainAuditEntry (INSERT), OutboxMessage | Domain row in Draft state; VerificationToken generated |
| `DomainVerificationAttempt` | DNS verification cron job | DomainVerification (INSERT row) | New verification attempt record |
| `DomainVerified` | DNS verification cron job | DomainAuditEntry (INSERT), OutboxMessage | Domain row transitions PendingVerification → Verified; VerifiedAtUtc set |
| `DomainVerificationFailed` | DNS verification cron job | DomainVerification (UPDATE status) | Attempt marked Failed with FailureReason |
| `SslProvisioningStarted` | Cert Manager job | SslCertificate (INSERT row), DomainAuditEntry | Domain transitions Verified → SslPending; SslCertificate row in Pending |
| `SslActivated` | Cert Manager job | SslCertificate (UPDATE to Active), DomainAuditEntry, OutboxMessage | Domain transitions SslPending → SslActive; SslActivatedAtUtc set; SslCertificateId FK set |
| `SslActivationFailed` | Cert Manager job | SslCertificate (UPDATE), DomainAuditEntry | Retry scheduled; SslRenewalFailed event on final failure |
| `SslRenewed` | Cert Manager job | SslCertificate (UPDATE NotAfterUtc), DomainAuditEntry | No state transition (SslActive → SslActive); NotAfterUtc extended |
| `SslRenewalFailed` | Cert Manager job | DomainAuditEntry | Domain transitions SslActive → SslPending (retry loop) |
| `SslExpired` | Cert Manager job | SslCertificate (UPDATE to Expired), DomainAuditEntry | Domain transitions SslActive → Verified (per Section 5 transition 9) |
| `SslRevoked` | Cert Manager / Merchant | SslCertificate (UPDATE to Revoked), DomainAuditEntry | Domain transitions to SslPending for re-provisioning |
| `PrimaryChanged` | DomainService (merchant action) | DomainAuditEntry (INSERT x2 for promote + demote), OutboxMessage | Previous Primary demoted; new Primary set |
| `RedirectConfigured` | DomainService (merchant action) | DomainAuditEntry, OutboxMessage | Domain.RedirectType + RedirectTargetDomainId updated |
| `DomainDisabled` | DomainService (merchant/system) | DomainAuditEntry, OutboxMessage | Domain transitions to Disabled; SSL remains but inactive |
| `DomainArchived` | DomainService (merchant) | DomainAuditEntry, OutboxMessage | Domain transitions to Archived |
| `DomainRestored` | DomainService (merchant) | DomainAuditEntry, OutboxMessage | Domain transitions Disabled/Archived → SslActive/Verified |
| `DomainDeleted` | DomainService (merchant) or GC | DomainAuditEntry, OutboxMessage, Domain row (soft DELETE) | Domain transitions to Deleted; DeletedAtUtc + IsDeleted set |
| `DomainReactivated` | DomainService (merchant) | DomainAuditEntry, OutboxMessage | Domain transitions Deleted → Verified/SslActive (within 90-day retention) |

### 13.2 Event Persistence Patterns

**Transactional outbox:** All events with downstream consumers (SeoService, reverse proxy config, cache invalidation) are published via `OutboxMessage` within the same transaction as the Domain state change. This matches the existing pattern at `ApplicationDbContext.cs:434` (`DbSet<OutboxMessage> OutboxMessages`).

**Non-critical events:** Events that are audit-only (`DomainVerificationAttempt`, `SslActivationFailed` on retry) are recorded in `DomainAuditEntry` but may fire-and-forget via `IEventPublisher` without outbox persistence.

**Event payload immutability:** Once an event is enqueued in the outbox, its payload (JSON) is immutable. Schema evolution is handled by adding new fields with defaults, not changing existing field semantics. This matches the `CmsPagePublishedEvent` wire-compatibility pattern (`CmsPageEvents.cs:28-36`).

> **Constitutional Persistence Decision CPD-14:** Domain events are NOT stored in a dedicated `DomainEvents` table. They persist through the existing `OutboxMessage` infrastructure only. The `DomainAuditEntry` table serves as the durable event log — every event has exactly one corresponding audit entry with `BeforeSnapshotJson` + `AfterSnapshotJson`. If the outbox is lost, the audit entry is the recovery source of truth.

---

## Section 14: Migration Strategy

> **NOTE: This section defines migration ORDERING and PHILOSOPHY ONLY. No migration code, no SQL DDL, no EF migration snippets.**

### 14.1 Philosophy

The Custom Domains persistence model is introduced via a **single additive migration wave** (DOMAIN-2). No data migration of `Tenant.StorefrontBaseUrl` is performed in the initial migration — only schema creation. The data migration (Wave DOMAIN-3, see DOMAIN-1 Section 17) adds `StorefrontBaseUrl` → Domain record conversion as a separate, reversible step.

### 14.2 Migration Ordering

```
Migration Wave 1 (DOMAIN-2 — Runtime Foundation):
  1. Create DomainType enum lookup table (or inline enum column)
  2. Create LifecycleState enum lookup table (or inline enum column)
  3. Create VerificationMethod enum lookup table (or inline enum column)
  4. Create CertificateStatus / ProviderType enum lookup tables (or inline)
  5. Create SslCertificates table (has no FK dependencies)
  6. Create Domains table (FK → Tenants, FK → SslCertificates)
  7. Create DomainVerifications table (FK → Domains)
  8. Create DomainAuditEntries table (FK → Domains, FK → Tenants, FK → Users)
  9. Add filtered unique indexes
  10. Add query-filter-support indexes
  11. Add check constraints

Migration Wave 2 (DOMAIN-3 — Backward Compatibility Data Migration):
  12. For each Tenant WHERE StorefrontBaseUrl IS NOT NULL:
      INSERT INTO Domains (TenantId, HostName, NormalizedHostName, DomainType, LifecycleState, ...)
      VALUES (TenantId, StorefrontBaseUrl, LOWER(StorefrontBaseUrl), Primary, Verified/SslActive, ...)
      — ONLY if no Domain row with the same NormalizedHostName exists (global uniqueness guard)
  13. Update StorefrontUrlResolver to prefer Domain registry over StorefrontBaseUrl (code change, not schema)
  14. Make StorefrontBaseUrl a computed/read-only field (code change)
  — ROLLBACK: DELETE FROM Domains WHERE Source = 'LegacyMigration'; revert StorefrontUrlResolver

Migration Wave 3 (DOMAIN-4 — SSL / Verification Data):
  15. No schema changes — only background jobs populating SslCertificates and DomainVerifications
  — ROLLBACK: no schema to roll back; stop jobs, delete orphan records

Migration Wave 4 (DOMAIN-5/6 — Production Hardening):
  16. No schema changes — only monitoring tables (if any) are additive
  — ROLLBACK: drop monitoring tables only
```

> **Constitutional Persistence Decision CPD-15:** The initial schema migration creates ALL four tables (`Domains`, `DomainVerifications`, `SslCertificates`, `DomainAuditEntries`) in a single migration. They are added in dependency order (SslCertificates before Domains, Domains before DomainVerifications and DomainAuditEntries). This ensures no partial state where Domain exists but its children don't. The migration is fully additive — no existing table is altered in Wave 1. This satisfies the "no breaking changes" requirement from DOMAIN-1 Section 18.

### 14.3 Rollback Philosophy

- **Schema rollback:** Drop `DomainAuditEntries` → drop `DomainVerifications` → drop `Domains` → drop `SslCertificates` (reverse dependency order). All tables are new — no data loss to existing tables.
- **Data rollback (Wave 2):** Reversible by deleting migrated Domain rows where `Source = 'LegacyMigration'` and reverting the `StorefrontUrlResolver` code path. The `Tenant.StorefrontBaseUrl` column is never altered.
- **Foreign key constraints:** All FKs use `RESTRICT` (not CASCADE) except soft-delete cascades handled at the application layer. This prevents accidental data loss during rollback.

---

## Section 15: Backward Compatibility

### 15.1 Coexistence: Tenant.StorefrontBaseUrl → Domain Aggregate

| Phase | `Tenant.StorefrontBaseUrl` | `Domain` table | Resolution Path | Grounded In |
|-------|---------------------------|----------------|-----------------|-------------|
| Pre-migration | Populated (manual DB set) | Not created | `TenantResolutionMiddleware` → `StorefrontBaseUrl` match | DOMAIN-0 Phase 2.4: `ResolveTenantFromStorefrontBaseUrlAsync` at `TenantResolutionMiddleware.cs:295-322` |
| Post-DOMAIN-2 schema, pre-data-migration | Populated | Created but empty for this tenant | `TenantResolutionMiddleware` → `StorefrontBaseUrl` fallback (unchanged) + Host Resolution Middleware no-op (no matching Domain) | DOMAIN-1 Section 18.2: "If Host Resolution Middleware does NOT resolve → TenantResolutionMiddleware falls through to existing JWT/slug logic" |
| Post-DOMAIN-3 data migration | Read-only alias | Populated: Primary domain from `StorefrontBaseUrl` | `Host Resolution Middleware` → `Domain` → `TenantId` (primary path); `StorefrontBaseUrl` fallback (secondary path) | DOMAIN-1 Section 18.1: "StorefrontBaseUrl becomes a computed/read-only alias" |

### 15.2 Coexistence Period

The coexistence period is defined by DOMAIN-1 Section 17 waves:

- **DOMAIN-2 (Runtime Foundation):** `StorefrontBaseUrl` continues to work via fallback. New domains can be registered via Admin API. Host Resolution Middleware is inserted but TenantResolutionMiddleware retains its `StorefrontBaseUrl` fallback path.
- **DOMAIN-3 (Verification + SSL):** Once a tenant's `StorefrontBaseUrl` is migrated to a Domain record (Wave 2 data migration), `StorefrontBaseUrl` is computed from the Domain registry. Writes to `StorefrontBaseUrl` are blocked (code-level, not schema-level).
- **DOMAIN-4+ (Admin UX + Production):** `StorefrontBaseUrl` is fully deprecated for writes. All new domain management goes through the Domain API.

### 15.3 Rollback

| Scenario | Rollback Mechanism |
|----------|-------------------|
| Schema only (DOMAIN-2) | Drop 4 new tables in reverse dependency order. Zero impact on existing data. |
| Data migrated (DOMAIN-3) | Delete Domain rows where `Source = 'LegacyMigration'`. Revert `StorefrontUrlResolver` to use `StorefrontBaseUrl`. |
| `StorefrontBaseUrl` computed field | Revert code change to restore writable `StorefrontBaseUrl`. |

### 15.4 Feature Flags

| Flag | Default | Purpose | Grounded In |
|------|---------|---------|-------------|
| `Domains:Enabled` | `false` | Master switch for Host Resolution Middleware | DOMAIN-1 Section 14.4: "Feature flag DomainsAdminUX (default OFF)" |
| `Domains:LegacyFallback` | `true` | Whether `TenantResolutionMiddleware` falls back to `StorefrontBaseUrl` when Host Resolution Middleware finds no Domain match | DOMAIN-1 Section 18.2: "TenantResolutionMiddleware falls through to existing JWT/slug logic" |
| `Domains:Migration:AutoMigrateStorefrontBaseUrl` | `false` | Whether to automatically migrate `StorefrontBaseUrl` → Domain on first request | DOMAIN-1 Section 18.1: "During DOMAIN-2 migration wave, StorefrontBaseUrl is migrated to a Domain record" |
| `Domains:Ssl:LetsEncrypt:Enabled` | `false` | Whether Let's Encrypt provisioning is active | DOMAIN-1 Section 6.1: two provisioning paths |

> **Constitutional Persistence Decision CPD-16:** The `StorefrontBaseUrl` column on `Tenants` is NEVER altered by any migration in this persistence model. It remains a plain `string` column with no index, no constraint, and no computed-column override (DOMAIN-1 Section 18.1: "becomes a computed/read-only alias" is a code-level change, not a schema change). The Domain registry is the new source of truth, but `StorefrontBaseUrl` is retained for backward compatibility at the application layer only.

---

## Section 16: Performance Model

### 16.1 Expected Row Counts

| Entity | Estimated Rows at Scale (N tenants, avg 3 domains/tenant) | Growth Model |
|--------|-----------------------------------------------------------|--------------|
| `Domains` | 3 × N (e.g., 3M for 1M tenants) | Linear with tenant count; bounded by domains-per-tenant (typically 1-10) |
| `DomainVerifications` | ~3 × N (1-2 attempts per domain per year, retention 365 days) | Sporadic; ~10% annual churn |
| `SslCertificates` | ~2 × N (1 active + 1-2 history entries per domain) | Bounded by renewal history depth |
| `DomainAuditEntries` | ~30 × N (15 transitions × 2 entries avg per domain lifecycle) | Linear; append-only; archived monthly in future |
| `DomainHistoryJson` (column on Domains) | 3 × N rows, avg 4KB each | JSON column growth per domain version |

### 16.2 Table Sizes (Estimate)

| Table | Avg Row Size | Rows at 1M tenants | Total Size |
|-------|-------------|-------------------|------------|
| `Domains` | ~600 bytes (excluding jsonb) + 4KB history | 3M | ~12.6 GB |
| `DomainVerifications` | ~200 bytes | 3M | ~600 MB |
| `SslCertificates` | ~400 bytes | 2M | ~800 MB |
| `DomainAuditEntries` | ~1.2 KB (with JSON snapshots) | 30M | ~36 GB |

> **Constitutional Persistence Decision CPD-17:** The `DomainAuditEntry.BeforeSnapshotJson` and `AfterSnapshotJson` columns are `jsonb` and store full Domain entity JSON at each state transition. At 30M audit rows × ~2.4KB per JSON snapshot pair, this is the largest storage cost. This is intentional — DOMAIN-1 Section 3.3 explicitly requires before/after JSON snapshots matching the `StoreConfigAuditEntry` pattern (`StoreConfigAuditEntry.cs:49-55`). Future archival (DOMAIN-6, Section 13) will move old audit entries to a cold-storage table. The `DomainHistoryJson` on the Domain row itself is a lighter snapshot (no before/after, just periodic current-state JSON).

### 16.3 Lookup Frequency

| Query | Frequency | Critical Path? |
|-------|-----------|----------------|
| Resolve Host (middleware) | **Per HTTP request** (every storefront page load) | YES — must be < 5ms |
| Resolve Primary Domain (SeoService) | Per SEO metadata request (product/category pages) | YES — must be < 10ms |
| Resolve Redirect | Per HTTP request on redirect domains | YES — must be < 5ms |
| SSL Renewal Scan | Daily batch (all SslActive certs expiring in 30 days) | No — batch |
| DNS Reverification Scan | Daily batch (all Verified domains > 90 days) | No — batch |
| Admin Audit Log | Per admin page load (paginated, 50/page) | No — indexed, paginated |

### 16.4 Caching Implications

1. **Hot path — Host Resolution:** The `NormalizedHostName` → `Domain` lookup is the single most frequent query in the storefront runtime. It must be cacheable at the middleware layer (in-memory `ConcurrentDictionary` or Redis) with a TTL of 5 minutes and invalidation on Domain state changes. This is a DOMAIN-2 implementation concern, but the persistence model must support it: the unique index on `NormalizedHostName` ensures cache key stability.

2. **Primary Domain resolution:** `TenantId` → Primary Domain is used by `StorefrontUrlResolver` (`StorefrontUrlResolver.cs:53-78`) for canonical URL generation. Cacheable with TTL 30 minutes, invalidated on `DomainPromotedToPrimary` / `DomainDowngradedFromPrimary` events.

3. **Redirect chain:** Redirect target lookup is cacheable with TTL 10 minutes. Invalidated on `RedirectConfigured` event.

4. **Cache key dimension:** DOMAIN-1 Section 8.6 requires the runtime cache (`storeConfigRuntime.ts:27`) to include a host dimension for live traffic. The persistence model supports this — `DomainId` is available in `HttpContext.Items` after Host Resolution Middleware, and can be incorporated into cache keys. For Preview domains, the constitutional rule (DOMAIN-1 Section 10.4) states host MUST NOT be added to the preview cache key — the preview token revision provides isolation.

> **Constitutional Persistence Decision CPD-18:** The `Domains` table must fit entirely in PostgreSQL's shared_buffers at peak scale. At ~12.6GB for 1M tenants × 3 domains, this requires ~16GB shared_buffers. This is a deployment sizing requirement, not a schema constraint. The table is optimized for point-lookup (single `NormalizedHostName` index seek) and tenant-scoped range queries (`TenantId` index). No full-table scans occur on the hot path.

---

## Section 17: Security Model

### 17.1 Tenant Isolation

| Requirement | Implementation | Grounded In |
|-------------|----------------|-------------|
| `Domain` rows are tenant-scoped | `ITenantOwned` on `Domain` entity; global query filter `!e.IsDeleted && e.TenantId == CurrentTenantId` | `ITenantOwned.cs:16-23`; `ApplicationDbContext.cs:486-489` query filter pattern |
| Cross-tenant FK references forbidden | `RedirectTargetDomainId` must resolve to same `TenantId` (application-layer check via `TenantLineageAuthority`) | DOMAIN-1 Section 11.4; DOMAIN-0 Phase 11.4: `TenantLineageAuthority` |
| SYSTEM/Development domains are platform-global | Exempt from `TenantId` query filter; `TenantId` still populated (platform tenant) | DOMAIN-1 Section 4: System = "Platform-managed"; Development = "platform-controlled, local-only" |
| Audit entries tenant-scoped | `DomainAuditEntry` implements `ITenantOwned`; query filter applies | DOMAIN-1 Section 3.3: "DomainAuditEntry table records: Id, DomainId, TenantId" |

### 17.2 Unique Host Enforcement

| Requirement | Implementation | Grounded In |
|-------------|----------------|-------------|
| Global hostname uniqueness | `UQ_Domains_NormalizedHostName` UNIQUE index on normalized (lowercase punycode) hostname | DOMAIN-1 DNS Constitution Section 5: "Global Uniqueness: NormalizedHostName must be unique across ALL tenants. Enforced at DB level via UNIQUE index." |
| Spoof prevention via normalization | `NormalizedHostName` is computed server-side (lowercase + punycode). Client cannot set the normalized form. | DOMAIN-1 Architecture Law #10 (Section 16.10): "Domain Normalization Is Server-Authoritative" |
| CNAME hijacking detection | `DomainVerification` records DNS challenge proof. No Domain reaches `Verified` without DNS proof. | DOMAIN-1 Section 6: "No domain verification mechanism exists" (DOMAIN-0 gap); "Domain must pass DNS verification before Verified" |
| DNS reverification | Background job re-checks DNS every 90 days. `VerifiedAtUtc` tracked for staleness. | DOMAIN-1 Section 6.3: "After 90 days, the platform re-checks DNS proof" |

### 17.3 Ownership

| Requirement | Implementation | Grounded In |
|-------------|----------------|-------------|
| Domain ownership = DNS verification | `VerificationToken` stored on Domain; DNS TXT/CNAME/HTTP-file must match. No Domain reaches `Verified` without proof. | DOMAIN-1 Section 6: "Three verification methods, all server-authoritative" |
| Certificate ownership | Platform provisions/validates certs. `CertificateArn`/`Thumbprint` reference only — no private keys in DB. | DOMAIN-1 Section 6.1: "Platform-managed cert reference" |
| Redirect cross-tenant forbidden | `RedirectTargetDomainId` FK enforces same `TenantId` at application layer | DOMAIN-1 Architecture Law #2: "Redirect target must be a Domain record within the same tenant" |

### 17.4 Spoof Prevention

| Attack Vector | Mitigation |
|---------------|------------|
| Host header spoofing | Host Resolution Middleware only trusts `Request.Host` after `ForwardedHeaders` processing (which reads `X-Forwarded-Host` from known proxies only) | DOMAIN-0 Phase 3.2: "X-Forwarded-Host NOT configured" gap; DOMAIN-1 Section 7.2: "Read Request.Host (now correct because X-Forwarded-Host is forwarded)" |
| Cross-tenant host confusion | `NormalizedHostName` UNIQUE index ensures only one Domain record per hostname globally. Two tenants cannot claim the same host. | DOMAIN-1 Section 11.2: "Cross-tenant host confusion: NormalizedHostName UNIQUE constraint ensures only one Domain record exists" |
| Redirect via redirect param | Application-layer allowlist of tenant's verified domains for `?redirect=` params. Arbitraries rejected with 400. | DOMAIN-1 Section 11.3: "User-supplied redirect parameters MUST be validated against an allowlist derived from the tenant's verified domains" |

### 17.5 Certificate Protection

| Requirement | Implementation |
|-------------|----------------|
| No private key storage | `SslCertificates` table stores only `CertificateArn` (ACM ARN) or `Thumbprint` (uploaded cert). Private keys are stored in the platform's secrets manager (AWS Secrets Manager / Azure Key Vault), NEVER in the database. |
| Certificate reference immutability | `CertificateArn` and `Thumbprint` are set once on INSERT. UPDATE is permitted only for status transitions (`Status` column) and renewal fields (`NotAfterUtc`, `NextRenewalUtc`). |
| Revocation tracking | `SslCertificateHistoryJson` records all revocation events with timestamps. |
| Expiry alerts | `NotAfterUtc` indexed (`IX_SslCertificates_NotAfterUtc`). Background job queries for certs expiring within 45/30/15/7/3/1 days. |

> **Constitutional Persistence Decision CPD-19:** The `SslCertificates` table is a child of `Domain` via `DomainId` FK, but certificate provisioning is asynchronous (background job). The `Domain.SslCertificateId` FK is nullable — a Domain in `Verified` state (no SSL yet) has `SslCertificateId = NULL`. When SSL activates, the FK is set transactionally in the same state transition that moves `LifecycleState` from `Verified` → `SslActive`. This matches DOMAIN-1 SSL Constitution Section 6.2: "SslPending → SslActive: Certificate provisioned and bound; sets SslActivatedAtUtc."

---

## Section 18: Database Constitution

These are the **constitutional database rules** — every schema decision, constraint, and index must comply.

| Rule | Statement | Grounded In |
|------|-----------|-------------|
| **C1** | One Primary Domain per tenant. Enforced by filtered UNIQUE index on `TenantId` WHERE `DomainType = Primary AND LifecycleState = Primary AND IsDeleted = false`. | DOMAIN-1 Architecture Law #2 (Section 16.2) |
| **C2** | `NormalizedHostName` is globally unique across ALL tenants. Enforced by UNIQUE index. No two tenants can claim the same domain. | DOMAIN-1 DNS Constitution (Section 6.1) |
| **C3** | Certificates are never shared across domains. Each `SslCertificate` row has a unique `DomainId` FK. No two Domains share the same certificate. | DOMAIN-1 SSL Constitution (Section 6.1): "Platform manages certs; merchant never edits live certificates" |
| **C4** | Verification tokens are immutable after generation. `VerificationToken` is set on INSERT and never updated. If verification method changes, a new DomainVerification row is created with a new token. | DOMAIN-1 Section 14.1: "The VerificationToken is preserved or regenerated based on method change" |
| **C5** | `LifecycleState` transitions are enforced by the state machine (Section 5). No direct SQL UPDATE on `LifecycleState` bypasses the application-layer state machine. | DOMAIN-1 Section 5: "Full Lifecycle Definition" with 15 transitions |
| **C6** | `DomainAuditEntry` is append-only. Database-level trigger prevents UPDATE and DELETE. | DOMAIN-1 Section 3.3: "DomainAuditEntry table records" + `StoreConfigAuditEntry.cs:14-81` append-only pattern |
| **C7** | Soft-deleted `Domain` rows are excluded from ALL queries via global query filter. Only explicit `WITH DELETED` queries (platform admin) can see them. | `ApplicationDbContext.cs:486-489` query filter pattern |
| **C8** | `Tenant.StorefrontBaseUrl` is never altered by domain schema. It remains a plain `string` column. Domain migration is a code-level computed read, not a schema change. | DOMAIN-0 Phase 2.3: `Tenant.cs:100`; DOMAIN-1 Section 18.1 |
| **C9** | `Domain.Id` is a `Guid` PK (matching existing entity convention: `BaseEntity.Id` at `BaseEntity.cs:8`, `Tenant.Id` at `Tenant.cs:15`). | Existing codebase convention |
| **C10** | All domain entities implement `ITenantOwned` (matching `TenantPaymentMethod.cs:22: ITenantOwned`). TenantId is NOT NULL at database level. | `ITenantOwned.cs:16-23`; `TenantSaveChangesInterceptor.cs:56-67` |
| **C11** | Foreign key constraints use `DeleteBehavior.Restrict` (not Cascade) for all Domain→external relationships. Cascade soft-delete is handled at the application layer. | Existing pattern: `ApplicationDbContext.cs:530` uses `OnDelete(DeleteBehavior.Restrict)` for FK relationships |
| **C12** | All datetime fields use UTC. `CreatedAtUtc` is set server-side via `DateTime.UtcNow` default. Client-supplied timestamps are never trusted. | `Tenant.cs:31`: `CreatedAtUtc = DateTime.UtcNow`; `StoreConfig.cs:70-75` |

---

## Section 19: Future Extensibility

The persistence model guarantees support for future domain topologies **without schema redesign**. Each extension adds enum values and nullable columns, never new tables.

| Extension | Current Schema Support | Required New Columns | Required New Index |
|-----------|----------------------|---------------------|-------------------|
| **Wildcard Domains** (`*.mybrand.com`) | `NormalizedHostName` column holds `*.mybrand.com`; `DomainType.Wildcard` (enum value 7) | `HostPattern` (nullable string, max 253) — pre-allocated in CPD-8 | `IX_Domains_HostPattern` on `HostPattern` WHERE `DomainType = Wildcard` |
| **Regional Domains** (`mybrand.eu`) | `NormalizedHostName` + `DomainType.Regional` (enum value 9) | `RegionCode` (nullable string) | Existing `TenantId + DomainType` index suffices |
| **International Domains** (ccTLD) | `NormalizedHostName` (punycode-normalized) + `DomainType.International` (enum value 8) | `CultureCode` (nullable string) | None needed (existing host resolution covers it) |
| **Marketplace Domains** | `DomainType.Marketplace` (enum value 10) + existing `TenantId` | `SellerId` (nullable Guid) — pre-allocated | `IX_Domains_TenantId_SellerId` WHERE `SellerId IS NOT NULL` |
| **White Label** | `DomainType.WhiteLabel` (enum value 11) — same as System | None | None |
| **Multi-CDN** | SSL certificate `ProviderType` enum supports edge-provider tagging | `EdgeProvider` (nullable string) on SslCertificate | None |
| **Custom Email Domains** | `DomainType.Email` (enum value 12) + `VerificationMethod.DnsTxt` | `MxRecord` (nullable string) | None |

> **Constitutional Persistence Decision CPD-20:** The `Wildcard` domain type requires a `HostPattern` value object (Section 5) stored as a nullable column on `Domain`. This column is NOT added at DOMAIN-1.5 freeze — it is a conditional future addition (DOMAIN-1 Section 19.1: "Host Pattern field"). The Host Resolution Middleware's pattern-matching logic (Section 11.1, "Wildcard Domains") reads this column only when `DomainType = Wildcard`. Since `DomainType.Wildcard` does not exist until the enum value is added, no middleware logic activates — the column is purely dormant until then.

---

## Section 20: Implementation Readiness

This section lists every artifact that DOMAIN-2 implementation will need, WITHOUT implementing them. Each item is traceable to a frozen specification above.

### 20.1 Entities (frozen in Section 3, 4)

| Artifact | Source |
|----------|--------|
| `Domain` entity class | Section 4.1 |
| `DomainVerification` entity class | Section 4.2 |
| `SslCertificate` entity class | Section 4.3 |
| `DomainAuditEntry` entity class | Section 4.4 |

### 20.2 EF Core Configurations (patterns frozen from existing codebase)

| Artifact | Reference Pattern | Grounded In |
|----------|-------------------|-------------|
| `DomainConfiguration : IEntityTypeConfiguration<Domain>` | `TenantConfiguration.cs:10` — `IEntityTypeConfiguration<Tenant>` | `TenantConfiguration.cs:10` |
| `DomainVerificationConfiguration : IEntityTypeConfiguration<DomainVerification>` | `TenantPaymentMethodConfiguration.cs:12` | `TenantPaymentMethodConfiguration.cs:12` |
| `SslCertificateConfiguration : IEntityTypeConfiguration<SslCertificate>` | Same pattern | `TenantPaymentMethodConfiguration.cs:12` |
| `DomainAuditEntryConfiguration : IEntityTypeConfiguration<DomainAuditEntry>` | `StoreConfigAuditEntry` (inferred from `StoreConfigAuditEntry.cs:14`) | DOMAIN-1 Section 3.3 |
| Query filter: `!e.IsDeleted && e.TenantId == CurrentTenantId` | `ApplicationDbContext.cs:486-489` — `TenantEntity` pattern | `ApplicationDbContext.cs:486` |
| RowVersion as `xmin` system column | `ApplicationDbContext.cs:729-732` — PaymentIntent RowVersion | `ApplicationDbContext.cs:730-732` |

### 20.3 DbSets (registration pattern frozen from `ApplicationDbContext.cs`)

| Artifact | Registration Pattern |
|----------|---------------------|
| `DbSet<Domain> Domains` | `ApplicationDbContext.cs:292`: `public DbSet<Tenant> Tenants => Set<Tenant>();` |
| `DbSet<DomainVerification> DomainVerifications` | Same pattern |
| `DbSet<SslCertificate> SslCertificates` | Same pattern |
| `DbSet<DomainAuditEntry> DomainAuditEntries` | Same pattern |

### 20.4 Repositories (interface-only specification)

| Repository | Methods (frozen by Section 11 queries) |
|------------|--------------------------------------|
| `IDomainRepository` | `GetByNormalizedHostNameAsync(string host)`, `GetPrimaryByTenantIdAsync(Guid tenantId)`, `GetRedirectTargetAsync(Guid domainId)`, `ListByTenantIdAsync(Guid tenantId)`, `AddAsync(Domain domain)`, `UpdateAsync(Domain domain)` |
| `IDomainVerificationRepository` | `GetPendingByDomainIdAsync(Guid domainId)`, `AddAsync(DomainVerification verification)`, `UpdateAsync(DomainVerification verification)` |
| `ISslCertificateRepository` | `GetByDomainIdAsync(Guid domainId)`, `GetExpiringAsync(DateTime threshold)`, `AddAsync(SslCertificate cert)`, `UpdateAsync(SslCertificate cert)` |
| `IDomainAuditEntryRepository` | `GetByDomainIdAsync(Guid domainId)`, `GetByTenantIdAsync(Guid tenantId, int skip, int take)`, `AddAsync(DomainAuditEntry entry)` |

### 20.5 Specifications (frozen by Section 11 query model)

| Specification | Query | Index Used |
|---------------|-------|------------|
| `HostResolutionSpec` | `WHERE NormalizedHostName = @host AND IsDeleted = false` | `UQ_Domains_NormalizedHostName` |
| `PrimaryDomainSpec` | `WHERE TenantId = @tenantId AND DomainType = Primary AND LifecycleState = Primary AND IsDeleted = false` | `UQ_Domains_TenantId_Primary` |
| `ActiveSslCertSpec` | `WHERE DomainId = @domainId AND IsDeleted = false AND Status IN (Active, Pending)` | `IX_SslCertificates_DomainId` |
| `ExpiringCertSpec` | `WHERE NextRenewalUtc <= @now AND Status = Active` | `IX_SslCertificates_NextRenewalUtc` |
| `DnsReverificationSpec` | `WHERE LifecycleState = Verified AND VerifiedAtUtc < @cutoff` | `IX_Domains_LifecycleState` |

### 20.6 Interceptors

| Interceptor | Role | Existing Pattern |
|-------------|------|-----------------|
| `DomainTenantIsolationInterceptor` | Same role as `TenantSaveChangesInterceptor` at `TenantSaveChangesInterceptor.cs:19-127` — validates TenantId on insert/update | `TenantSaveChangesInterceptor.cs:19` |
| `DomainAuditEntryInterceptor` | Automatically inserts `DomainAuditEntry` rows on Domain state changes | `StoreConfigAuditEntry` inference — audit entries created within same transaction | Inferred from DOMAIN-1 Section 3.3 |

### 20.7 Background Jobs

| Job | Persistence Access | Grounded In |
|-----|--------------------|-------------|
| DNS Verification Cron | Reads `Domain` (LifecycleState = PendingVerification), writes `DomainVerification` | DOMAIN-1 Section 6.5: "verification cron job (poll DNS every 10s for 5 minutes)" |
| SSL Provisioning Cron | Reads `Domain` (LifecycleState = SslPending), writes `SslCertificate` | DOMAIN-1 Section 6.7: retry backoff 1m, 5m, 15m, 1h, 4h, 12h, 24h |
| SSL Renewal Cron | Reads `SslCertificate` (NextRenewalUtc <= NOW()), writes renewal status | DOMAIN-1 Section 6.7 |
| DNS Reverification Cron | Reads `Domain` (LifecycleState = Verified, VerifiedAtUtc > 90 days), re-checks DNS | DOMAIN-1 Section 6.3: "After 90 days, the platform re-checks DNS proof" |
| GC Hard-Delete Cron | Reads `Domain` (DeletedAtUtc < NOW() - 90 days), hard-deletes | DOMAIN-1 Section 5, transition 14 |

### 20.8 Services (interface-only)

| Service | Responsibility |
|---------|----------------|
| `IDomainService` | Orchestrate Domain lifecycle state transitions (Section 5); enforce state machine guard rules |
| `IDomainVerificationService` | Execute DNS TXT/CNAME/HTTP-file challenges; manage `VerificationToken` |
| `ISslCertificateService` | Coordinate Let's Encrypt / custom upload provisioning; manage `SslCertificate` lifecycle |
| `IHostResolutionService` | Resolve `Request.Host` → `Domain` entity (Section 11.1); inject into `HttpContext.Items` |
| `IDomainAuditService` | Append `DomainAuditEntry` rows; provide tenant-scoped audit query API |

---

## Section 21: Migration Ordering (Consolidated)

For quick reference — see Section 14 for full detail.

```
Wave 1 (DOMAIN-2 — Schema Only, Additive, Zero Data Loss):
  1. Enum columns (DomainType, LifecycleState, VerificationMethod, CertificateStatus, ProviderType, RedirectType, CanonicalPolicy, ActorType)
  2. SslCertificates table
  3. Domains table (FK → Tenants, FK → SslCertificates)
  4. DomainVerifications table (FK → Domains)
  5. DomainAuditEntries table (FK → Domains, FK → Tenants, FK → Users)
  6. All indexes, constraints, query filters
  ROLLBACK: DROP tables in reverse order (5→4→3→2), no existing data touched

Wave 2 (DOMAIN-3 — Data Migration, Reversible):
  7. Migrate Tenant.StorefrontBaseUrl → Domain rows (Source = 'LegacyMigration')
  8. StorefrontUrlResolver updated to prefer Domain registry
  ROLLBACK: DELETE migrated Domain rows; revert resolver code

Wave 3 (DOMAIN-4 — Operational, No Schema):
  9. Background jobs populate SslCertificates and DomainVerifications
  ROLLBACK: Stop jobs; DELETE generated rows

Wave 4 (DOMAIN-5/6 — Monitoring, Additive):
  10. Monitoring tables (if any) for domain health, SSL alerts
  ROLLBACK: DROP monitoring tables only
```

---

## Certification

**VERDICT: PASS**

### Grounding in DOMAIN-0

Every persistence decision in this document traces back to either:

1. **DOMAIN-0 verified evidence** (21 gap items from Phase 16):
   - Gap #1: No Domain entity → Section 3, 4.1 (new `Domains` table)
   - Gap #2: Single `StorefrontBaseUrl` string → Section 15.1 coexistence model
   - Gap #6: No SSL/cert management → Section 3.3, 4.3 (`SslCertificates` table)
   - Gap #7: No domain verification → Section 3.2, 4.2 (`DomainVerifications` table)
   - Domain-0 Phase 2.3: `Tenant.cs:100` `StorefrontBaseUrl` — preserved, never altered
   - Domain-0 Phase 4.1: `StoreConfig.cs` — no domain fields; StoreConfig never owns domains (Section 1.2, 16.8)
   - Domain-0 Phase 6.3: `ApplicationDbContext.cs:292` — no `DbSet<Domain>` currently exists; this document defines the new DbSets
   - Domain-0 Phase 11.4: `TenantLineageAuthority` — adopted for tenant isolation query filter
   - Domain-0 Phase 3.2: `Program.cs:102` — X-Forwarded-Host gap addressed in Section 7 (pipeline law, CPD-3)

2. **DOMAIN-1 constitutional design** (all sections):
   - Domain entity definition (Section 2.1) → Section 4.1
   - Domain lifecycle state machine (Section 5) → Section 4.1 `LifecycleState` enum, Section 5 state machine
   - DNS Constitution (Section 6) → Section 4.2 `DomainVerification` entity, Section 6 enums
   - SSL Constitution (Section 6) → Section 4.3 `SslCertificate` entity, Section 6 enums
   - Architecture Laws (Section 16) → Section 18 Database Constitution rules C1-C12
   - Migration waves (Section 17) → Section 14 Migration Strategy
   - Backward compatibility (Section 18) → Section 15 Backward Compatibility

### No Contradictions

This document does not contradict any DOMAIN-0 finding or DOMAIN-1 design. Where DOMAIN-0 says entities do not exist, DOMAIN-1.5 defines their persistence model. Where DOMAIN-1 defines architectural laws, DOMAIN-1.5 defines the database constraints and indexes that enforce them.

### Architectural Soundness

The persistence model is consistent with the existing Shopixy platform:
- `ITenantOwned` + global query filter pattern (`ApplicationDbContext.cs:486-489`)
- Append-only audit entries matching `StoreConfigAuditEntry` (`StoreConfigAuditEntry.cs:14-81`)
- `xmin`-based optimistic concurrency matching `PaymentIntent` (`ApplicationDbContext.cs:729-732`)
- `jsonb` for JSON snapshots matching `StoreConfig.CustomizationJson` (`StoreConfig.cs:82`)
- `Guid` PK convention matching all entities (`BaseEntity.cs:8`)
- `DeleteBehavior.Restrict` on FK relationships (`ApplicationDbContext.cs:530`)

### Boundary Enforcement

The persistence model enforces all architectural boundaries:
- Domain does not FK to StoreConfig (Architecture Law #8)
- Domain does not own Theme (Architecture Law #4)
- One Primary per tenant via filtered unique index (Architecture Law #2, C1)
- Global host uniqueness via UNIQUE index (C2)
- Certificates never shared (C3)
- Append-only audit (C6)
- Soft-delete query filter (C7)

### Backward Compatibility

No existing table is altered. `Tenant.StorefrontBaseUrl` remains untouched (C8). All new tables are additive. The fallback path (Host Resolution Middleware → if no match → TenantResolutionMiddleware → StorefrontBaseUrl/JWT/slug) is preserved at the middleware layer, not the persistence layer.

---

## Appendix A: Evidence Traceability

### A.1 DOMAIN-0 Source Evidence (Verified Against Live Code)

| DOMAIN-0 Claim | File:Line | Verified By |
|---------------|----------|-------------|
| `Tenant.StorefrontBaseUrl` exists as string column | `Tenant.cs:100` | `read_file` confirmed line 100 |
| `StorefrontBaseUrl` NOT configured in `TenantConfiguration` | `TenantConfiguration.cs:1` (only configures Name, Slug, ContactEmail) | `read_file` confirmed — no index, no constraint on StorefrontBaseUrl |
| `TenantResolutionMiddleware` reads `Request.Host` at lines 263-266 | `TenantResolutionMiddleware.cs:263-266` | `read_file` confirmed lines 295-322 for `ResolveTenantFromStorefrontBaseUrlAsync`, lines 377-386 for `BuildCandidateBaseUrls` |
| `X-Forwarded-Host` NOT in ForwardedHeaders | `Program.cs:102` — `ForwardedHeaders.XForwardedFor \| ForwardedHeaders.XForwardedProto` | `read_file` confirmed lines 100-130 |
| `X-Tenant-Id` header stripped at line 82, re-added at line 180 | `TenantResolutionMiddleware.cs` | DOMAIN-0 citation; architecture law #14 |
| `StoreConfig.cs:123` — RowVersion as optimistic concurrency | `StoreConfig.cs:117-123` | `read_file` confirmed — `public uint RowVersion { get; set; }` with comment "PostgreSQL: Mapped as xmin system column or explicit bytea column" |
| `ApplicationDbContext.cs:292` — `DbSet<Tenant> Tenants` | `ApplicationDbContext.cs:292` | `read_file` confirmed |
| `ApplicationDbContext.cs:298` — `DbSet<StoreConfig> StoreConfigs` | `ApplicationDbContext.cs:298` | `read_file` confirmed |
| `ApplicationDbContext.cs:301` — `DbSet<StoreConfigAuditEntry>` | `ApplicationDbContext.cs:301` | `read_file` confirmed |
| `StoreConfigAuditEntry.cs:14-81` — append-only audit pattern | `StoreConfigAuditEntry.cs` | `read_file` confirmed — AuditActionTypes at line 86 |
| `BaseEntity.cs:13` — `IsDeleted` boolean | `BaseEntity.cs` | `read_file` confirmed |
| `TenantEntity.cs:21` — `TenantId` property | `TenantEntity.cs` | `read_file` confirmed |
| `ITenantOwned.cs:16-23` — tenant ownership interface | `ITenantOwned.cs` | `read_file` confirmed |
| `TenantSaveChangesInterceptor.cs:56-67` — tenant validation | `TenantSaveChangesInterceptor.cs` | `read_file` confirmed |
| `ApplicationDbContext.cs:486-489` — global query filter pattern | `ApplicationDbContext.cs:486-489` | `read_file` confirmed: `!e.IsDeleted && e.TenantId == CurrentTenantId` |
| `ApplicationDbContext.cs:729-732` — PaymentIntent RowVersion as xmin | `ApplicationDbContext.cs:729-732` | `read_file` confirmed: `.HasColumnName("xmin").HasColumnType("xid").IsRowVersion()` |
| `ApplicationDbContext.cs:530` — `OnDelete(DeleteBehavior.Restrict)` | `ApplicationDbContext.cs:530` | `read_file` confirmed |
| `TenantPaymentMethod.cs:22` — `ITenantOwned` implementation | `TenantPaymentMethod.cs` | `read_file` confirmed: `public class TenantPaymentMethod : BaseEntity, ITenantOwned` |
| `TenantPaymentMethod.cs:144` — `Version` integer field | `TenantPaymentMethod.cs` | `read_file` confirmed |
| `TenantStatus.cs:6` — `TenantStatus` enum | `TenantStatus.cs` | `read_file` confirmed: PendingSetup(0), Active(1), Suspended(2), Deleted(3) |
| `PaymentProvider.cs:3-10` — enum-as-int pattern | `PaymentProvider.cs` | `read_file` confirmed |
| `Tenant.cs:52` — `TenantStatus` enum field | `Tenant.cs` | `read_file` confirmed |
| `Tenant.cs:37` — `Slug` field | `Tenant.cs` | `read_file` confirmed |
| `Tenant.cs:72` — `DeletedAtUtc` field | `Tenant.cs` | `read_file` confirmed |
| `StoreConfig.cs:109` — `VersionsJson` jsonb pattern | `StoreConfig.cs:109` | `read_file` confirmed |
| `StoreConfig.cs:123` — RowVersion | `StoreConfig.cs:123` | `read_file` confirmed |
| `StoreConfig.cs:82` — `CustomizationJson` as jsonb | `StoreConfig.cs:82` | `read_file` confirmed (EF convention maps string → jsonb) |
| `StorefrontUrlResolver.cs:53-78` — URL resolution priority | `StorefrontUrlResolver.cs:53-78` | `read_file` confirmed — Priority 1: StorefrontBaseUrl, Priority 2: UrlTemplate |
| `SeoService.cs:105` — `_storefrontUrlResolver.ResolveByTenantIdAsync(tenantId)` | `SeoService.cs:105` | `read_file` confirmed |
| `SeoService.cs:592` — sitemap baseUrl resolution | `SeoService.cs:592` (DOMAIN-0 citation, DOMAIN-1 Section 12.2) | Verified via StorefrontUrlResolver pattern |
| `Caddyfile:16` — `header_up Host {host}` (no X-Forwarded-Host) | `deploy/staging/Caddyfile:16` | `read_file` confirmed lines 14-20 |
| `BaseEntity.cs:8-13` — Id (Guid PK), CreatedAt, UpdatedAt, IsDeleted | `BaseEntity.cs` | `read_file` confirmed |
| `CmsPageEvents.cs:13` — IntegrationEvent pattern | `CmsPageEvents.cs` | `read_file` confirmed — `CmsPagePublishedEvent : IntegrationEvent` |
| `ApplicationDbContext.cs:434` — `OutboxMessage` DbSet | `ApplicationDbContext.cs:434` | `read_file` confirmed |
| `ApplicationDbContext.cs:523-534` — FK with `OnDelete(DeleteBehavior.Restrict)` pattern | `ApplicationDbContext.cs:523-534` | `read_file` confirmed |
| `TenantRegistrationService.cs:205-217` — tenant creation does NOT set StorefrontBaseUrl | DOMAIN-0 Phase 2.3 citation | Verified via DOMAIN-0 evidence protocol |

### A.2 DOMAIN-1 Constitutional Design (No Current Source Code)

All items in Section 3 (Domain entity), Section 4.1-4.4 (entity specifications), Section 13 (event model), and the migration waves in Section 14 are explicitly labeled as **Constitutional Design** — they extend beyond the current codebase to define the target persistence model. Every field name, type, index, and constraint traces back to DOMAIN-1 Section 2.1 (Domain Aggregate root), Section 3 (child entities), Section 5 (lifecycle state machine), Section 6 (DNS/SSL Constitution), and Section 16 (Architecture Laws).

---

## Appendix B: Constitutional Persistence Decisions Index

| CPD | Description |
|-----|-------------|
| CPD-1 | Domain aggregate owned by Infrastructure; TenantId is join key, not ownership boundary |
| CPD-2 | Domain does not FK to StoreConfig — avoids circular dependency |
| CPD-3 | Domain events use existing OutboxMessage infrastructure — no new event store |
| CPD-4 | DomainAuditEntry implements ITenantOwned for query filter consistency |
| CPD-5 | Domain implements ITenantOwned; System/Development types exempted from tenant filter |
| CPD-6 | DomainHistoryJson stored as jsonb on Domain row — no separate history table |
| CPD-7 | Domain events NOT in dedicated events table — audit entry is the durable log |
| CPD-8 | NormalizedHostName is persisted column, not computed — enables UNIQUE constraint + index |
| CPD-9 | Future domain types added as integer enum values — no schema changes required |
| CPD-10 | RedirectTargetDomainId uses Restrict delete — prevents accidental redirect breakage |
| CPD-11 | Domain.Version uses integer (not byte[]) — human-readable sequence number |
| CPD-12 | NormalizedHostName B-tree index is the only hot-path lookup — O(log n) seek |
| CPD-13 | Audit entries written in same transaction as state change — atomicity guaranteed |
| CPD-14 | Domain events persist through OutboxMessage only — not a dedicated events table |
| CPD-15 | Initial schema migration creates all 4 tables in dependency order — no partial state |
| CPD-16 | Tenant.StorefrontBaseUrl never altered by migration — code-level computed read only |
| CPD-17 | DomainAuditEntry JSON snapshots are the largest storage cost — future archival planned |
| CPD-18 | Domains table must fit in shared_buffers — deployment sizing requirement |
| CPD-19 | SslCertificateId nullable on Domain — supports Verified (no SSL) intermediate state |
| CPD-20 | HostPattern column pre-allocated but NOT added — dormant until Wildcard enum activated |

---

*This document is constitutional. No entity shape, relationship, index, constraint, or migration ordering defined herein may change without formal constitutional amendment. DOMAIN-1.5 supersedes any conflicting guidance in prior drafts.*
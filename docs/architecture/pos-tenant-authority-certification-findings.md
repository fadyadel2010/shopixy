# PASS P1-B POS Tenant Authority Certification

Date: 2026-06-07

## Verdict

The inspected POS tenant-authority runtime is production-certifiable after P1-B hardening. Tenant authority is derived from current SQL user, tenant, session-binding, and governance truth. JWT claims, routes, queries, headers, DTOs, caches, and projections remain evidence only.

## 1. Tenant Authority Findings

- `TenantLineageAuthority` and `TenantService` now define an authoritative tenant as present in SQL, `IsActive`, `TenantStatus.Active`, and not deleted.
- `TenantResolutionMiddleware` now applies the same lifecycle rule to authenticated, slug, and custom-domain resolution.
- `ApplicationDbContext` tenant filters fail closed when trusted tenant context is absent.
- Stale token tenant claims are rejected when they differ from the current SQL user's tenant.

## 2. Cross-Tenant Read Findings

- One runtime defect existed: tenant-admin CommerceHub analytics counted global marketplace search queries as tenant searches and did not explicitly bind conversions to the tenant suborder.
- The query now derives searches from tenant-owned search impressions and requires `SubOrder.StoreId == tenantId` for conversions.
- POS settings use `RequiredTenantId`; reports establish `RequiredTenantId` before every action and retain EF tenant filters.
- Guessed resource IDs remain constrained by global filters or explicit tenant ownership predicates.

## 3. Cross-Tenant Write Findings

- Create/update/delete paths use trusted tenant context, tenant-scoped SQL predicates, and EF global filters.
- `TenantSaveChangesInterceptor` now validates `Deleted` entries in addition to added and modified tenant-owned entities.
- DTO tenant IDs, route IDs, and query IDs cannot transfer ownership or authorize a foreign-tenant mutation.

## 4. Session-Tenant Binding Findings

- `PosSessionAuthority` re-reads the current SQL user and rejects missing, inactive, deleted, reassigned, role-stale, expired, rotated, or revoked sessions.
- Access requires the JWT tenant claim to equal the current SQL user's tenant.
- Refresh authority remains single-session bound and is cleared when user, tenant, or vendor governance authority is reduced.

## 5. Governance Findings

- Suspended, inactive, soft-deleted, or status-deleted tenants cannot resolve request tenant authority.
- Vendor governance `Suspended` and `Banned` states deny access and revoke persisted POS refresh authority.
- Deleted tenants fail current SQL lineage checks even if an old JWT or refresh credential still exists.

## 6. Adversarial Findings

- Route parameter manipulation: foreign IDs resolve to no tenant-visible row.
- Query parameter manipulation: filters can narrow current-tenant data but cannot select another tenant.
- Header manipulation: authenticated tenant/header mismatches are rejected; middleware replaces raw tenant headers with trusted resolved output.
- DTO tampering: caller-supplied tenant ownership is not authoritative.
- Direct API invocation and session reuse: authorization still executes SQL-current POS session and tenant validation.

## 7. Runtime Hardening Performed

- Unified tenant lifecycle resolution around `IsActive`, active status, and non-deletion.
- Repaired tenant-admin CommerceHub analytics isolation.
- Extended write interception to hard deletes.
- Hardened legacy settings and reports to require trusted tenant context.

## 8. Exact Runtime Files Modified

- `PointOfSale.API/Controllers/AdminCommerceHubController.cs`
- `PointOfSale.API/Controllers/ReportsController.cs`
- `PointOfSale.API/Controllers/SettingsController.cs`
- `PointOfSale.API/Middleware/TenantResolutionMiddleware.cs`
- `PointOfSale.Infrastructure/Data/TenantSaveChangesInterceptor.cs`
- `PointOfSale.Infrastructure/Services/TenantService.cs`

## 9. Regression Freezes Added

- `PointOfSale.Tests/Architecture/PosTenantAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/TenantIsolationCertificationFreezeTests.cs`
- `PointOfSale.Tests/Security/ProductionSecurityHardeningTests.cs`
- `PointOfSale.Tests/Security/TenantSaveChangesInterceptorTests.cs`

## 10. Architecture Updates

- Tenant is the root POS business boundary.
- Current SQL tenant lifecycle truth is required at request/session authority boundaries.
- Tenant-owned EF reads fail closed through trusted context and global filters.
- Explicit filter bypasses must immediately reapply tenant ownership.
- Tenant lifecycle and vendor governance reductions invalidate POS refresh authority.

## 11. Remaining Debt

- PostgreSQL row-level security is not the primary application boundary; isolation still depends on application SQL predicates, filters, and interceptors.
- A lifecycle change racing an already-running transaction is bounded by request-time authority and transaction controls, not a database-wide tenant lease.
- Dedicated production alerting and chaos evidence for cross-tenant probes and lifecycle races remains operational work.
- Branch and warehouse authority require separate future certifications and were not expanded in P1-B.

## Required Answers

1. Can Tenant A read Tenant B data? **No, after P1-B hardening.**
2. Can Tenant A modify Tenant B data? **No.**
3. Can route parameters bypass tenant isolation? **No.**
4. Can query parameters bypass tenant isolation? **No.**
5. Can stale tenant authority survive? **No across subsequent access or refresh validation; current SQL authority is rechecked.**
6. Can suspended tenants retain authority? **No.**
7. Can deleted tenants retain authority? **No.**
8. Can governance-restricted tenants retain authority? **No.**
9. Were runtime code changes required? **Yes.**
10. Is POS Tenant Authority production-certifiable? **Yes, for the inspected runtime scope.**

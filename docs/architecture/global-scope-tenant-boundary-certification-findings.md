# POS-ADMIN-CERTIFICATION-01 PASS 11.2

# Global Scope & Tenant Boundary Certification

Date: 2026-05-29

## Executive Result

The reviewed platform has two explicit authority domains:

- Global / platform authority: current SQL super-admin actor authority plus current governance state, enforced by `RequireSuperAdmin` policy and `SuperAdminPlatformAuthorityMiddleware`.
- Tenant-scoped authority: current trusted tenant context plus current SQL tenant actor and active tenant lineage, enforced by `AdminTenantAuthorityMiddleware`, tenant services, tenant filters, and SQL recomposition in repair/rebuild paths.

No new business feature was introduced. This pass added regression-freeze coverage and documentation for scope governance. Existing runtime hardening from PASS 11.1, tenant isolation, marketplace governance, recovery/rebuild, convergence, and chaos passes was certified as the active enforcement layer.

## 1. Scope Inventory Findings

Global operations:
- Super-admin controllers under `api/super-admin/*`.
- Platform tenant administration in `TenantsController`.
- Maintenance and development/platform-only operational controllers guarded by `SuperAdminSecurityConstants.PolicyName`.
- Legacy marketplace service global reindex at `api/marketplace/admin/reindex/global`.
- CommerceHub global reindex and Elasticsearch drift jobs where global SQL authority is declared by `SqlTruthRebuildGuard.RequireGlobalSqlAuthority`.

Tenant operations:
- `api/admin/*` POS/admin routes using `Owner`, `Admin`, `Manager`, `Cashier`, `Warehouse`, or `Accountant` role policies.
- Tenant product/category/CMS/search repair and reindex endpoints using `ITenantContext.RequiredTenantId`.
- Tenant marketplace service repair at `api/marketplace/admin/reindex`.
- Tenant diagnostics and reporting endpoints that resolve one trusted tenant.

Hybrid operations:
- Marketplace and CommerceHub read/search/governance surfaces. They may cross tenants for public marketplace discovery, but writes and recovery mutation must use SQL listing/product/vendor/category authority and tenant-scoped document identity.
- Affiliate identity is global, but product economics, discovery visibility, and reindex are tenant/store-scoped.

Recovery, replay, repair, reindex, and diagnostics operations:
- Recovery discovers candidates from SQL, MongoDB, Redis, Elasticsearch, provider evidence, or outboxes, but final mutation must revalidate current SQL authority and tenant scope.
- Reindex is either explicitly global under platform authority or explicitly tenant-scoped from trusted tenant context.
- Diagnostics are tenant-scoped unless a route is explicitly super-admin/platform-owned.

## 2. RequireSuperAdmin Findings

`RequireSuperAdmin` remains the reviewed platform authority boundary. `SuperAdminBaseController` applies `SuperAdminSecurityConstants.PolicyName`, and `SuperAdminPlatformAuthorityMiddleware` reloads current SQL super-admin authority, current governance state, and token version before execution.

The legacy marketplace global reindex is intentionally global and uses `[Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]`. Tenant-admin marketplace reindex remains tenant-scoped and does not delete or recreate the global index.

No reviewed platform route relies on tenant role strings as global authority.

## 3. Tenant-Boundary Findings

Tenant-admin authority is not just JWT evidence. `AdminTenantAuthorityMiddleware` extracts tenant role metadata, skips super-admin policy endpoints, and validates the authenticated actor against current SQL user authority, trusted tenant context, active tenant lineage, and current role.

Reviewed tenant routes use `RequiredTenantId` or middleware-validated tenant claims. Search, repair, diagnostics, reports, payment methods, categories, coupons, dashboard, financial reports, WhatsApp, storefront footer, and affiliate admin paths remain tenant-bound.

Tenant operations cannot silently fall back to global behavior. Reviewed tenant search controls return `GLOBAL_SEARCH_CONTROL_FORBIDDEN` or `GLOBAL_SEARCH_INDEX_UNAVAILABLE` rather than performing global index mutation.

## 4. Marketplace Governance Findings

Global marketplace actions are intentionally global only when platform authority is present. Marketplace homepage, listing, discovery, recommendation, review, and search projection paths remain derivative and SQL-filtered by current listing/product/category/vendor/tenant authority.

Tenant marketplace service reindex filters by `TenantId`; global service reindex is a separate super-admin route. CommerceHub listing identity and recovery identity include tenant scope where tenant evidence exists.

## 5. Replay And Recovery Findings

Replay/recovery remains convergence tooling, not authority. `ProjectionEventFence`, `ProjectionConsumerEventFence`, processed-event receipts, claim leases, SQL authority overlays, and stale-delete/tombstone behavior collapse duplicate and stale work before derivative mutation.

CommerceHub auto-healing keeps `{ TenantId, ListingId }` in recovery identity, and search analytics recovery still calls `CommerceHubSearchSqlAuthorityOverlay` before reindex or delete. Affiliate reindex uses `ReindexTenantAsync(Guid tenantId)`, `TenantScopedDerivativeKey`, active tenant checks, and SQL-first rebuild declarations.

## 6. Reindex Findings

Tenant reindex:
- Storefront product search reindex uses trusted tenant context and `ProductSearchSqlAuthorityOverlay`.
- Affiliate AF16E reindex is tenant-scoped.
- Tenant marketplace service reindex filters Elasticsearch scans and SQL services by tenant.

Global reindex:
- Legacy service-marketplace global reindex is guarded by super-admin policy.
- CommerceHub global reindex uses SQL-visible listing count verification before alias promotion.
- Drift reconciliation declares global SQL authority and reloads current SQL service/listing authority before search mutation.

## 7. Diagnostics Findings

Tenant diagnostics remain tenant-scoped on reviewed legacy marketplace diagnostics and admin reporting routes. Marketplace diagnostics count active SQL services and Elasticsearch documents for the resolved tenant only.

Global diagnostics require a platform route or runbook. This pass does not claim a universal global diagnostic dashboard.

## 8. Service-Layer Findings

The reviewed service layer preserves scope after controller entry:
- `AdminTenantAuthority` revalidates actor, tenant, and role against SQL.
- `SuperAdminPlatformAuthority` revalidates platform actor, governance state, and token version against SQL.
- Rebuild/reindex services call SQL authority overlays before derivative mutation.
- Event publishing keeps tenant scope explicit through authoritative event validation.

## 9. Tenant-Context Findings

Tenant context is required for tenant-admin operations. Missing trusted tenant context fails closed through `AdminTenantAuthority` or `ITenantContext.RequiredTenantId`.

Tenant context is not used to constrain platform operations accidentally. Platform routes use super-admin authority and are allowed to cross tenant boundaries only where explicitly global.

## 10. Auditability Findings

Platform mutations through super-admin routes are audited by `SuperAdminOperationalAuditMiddleware` with actor id, actor email, token version, method, path, endpoint, status, and request reference.

Tenant authority rejection is observable through structured logs and bounded `OperationalConvergenceTelemetry.RecordAuthorityFailure` emissions for admin tenant scope, actor-scope rejection, tenant inactivity, and related failures.

## 11. Chaos Findings

Simulated chaos reviewed by source:
- Replay during tenant migration collapses through SQL tenant lineage and tenant-scoped derivative keys.
- Recovery during tenant disablement fails closed through active tenant checks and SQL overlays.
- Reindex during governance changes remains explicit: global reindex requires platform authority, tenant reindex remains tenant-bound.
- Startup/background CommerceHub recovery keeps tenant-scoped recovery identity.
- Global repair while tenant changes occur revalidates current SQL authority before derivative mutation.

## 12. Runtime Hardening Performed

No new production business feature or broad runtime rewrite was added in PASS 11.2. The hardening performed in this pass is regression hardening:

- Added `GlobalScopeTenantBoundaryCertificationTests`.
- Certified the existing platform/tenant middleware split.
- Certified tenant-admin global-search denial markers.
- Certified tenant-scoped reindex, recovery, diagnostics, and derivative identity markers.
- Added this certification document and `.ai-context` governance entries.

## 13. Monitoring Improvements

This pass did not add a new meter. It certified existing monitoring surfaces:

- `SuperAdminOperationalAuditMiddleware` for platform operation audit trails.
- `OperationalConvergenceTelemetry.RecordAuthorityFailure` for tenant-scope failures.
- Existing convergence/readiness telemetry for replay, reindex, and derivative drift.

Future global-operation dashboards should aggregate these existing signals rather than reintroduce raw tenant ids or unbounded labels.

## 14. Governance Updates

PASS 11.2 updates `.ai-context` and freezes:

- Global operations must use explicit platform authority.
- Tenant operations must require trusted tenant context and current SQL tenant actor authority.
- Hybrid marketplace/recovery/reindex paths must document whether they are global, tenant-scoped, or tenant-filtered global operations.
- New diagnostics/reporting/support/global-script paths must declare intended scope, authority owner, enforcement owner, observability, and tenant-boundary behavior.

## 15. Honest Remaining Debt

- No enterprise-wide scope registry or generated route inventory exists.
- No universal parity scanner proves every diagnostics/reporting/support path tenant-by-tenant.
- No immutable SIEM or centralized global-operation dashboard was introduced.
- Some legacy marketplace/service endpoints still use local tenant claim helpers, but reviewed tenant role routes are covered by tenant authority middleware.
- Future feed/export/CDN purge/support/global-script/reporting/search-analytics paths need path-specific scope certification before they are treated as safe global or tenant tooling.

## Guarantees Now Certified

- Global authority is explicit and guarded by current SQL platform authority.
- Tenant authority remains tenant-bound and validated against current SQL tenant actor and tenant lineage.
- Tenant routes cannot silently invoke global search/index operations on reviewed paths.
- Global routes do not inherit tenant filters accidentally where global scope is intended and documented.
- Replay, recovery, repair, reindex, and diagnostics preserve scope through SQL authority overlays, tenant-scoped identity, active tenant checks, fences, and audit/telemetry.

# PASS SF-PROMO.LIFECYCLE Admin Promotion Lifecycle Authority Certification Findings

Scope: PointOfSale.Web Promotion Management lifecycle runtime only.

Status: Admin Promotion Lifecycle is production-certifiable after RowVersion/If-Match hardening.

## Pages Reviewed

- `PointOfSale.Web/src/pages/Promotions.jsx`
- `PointOfSale.Web/src/services/adminPromotionsApi.js`
- `PointOfSale.Web/src/auth/adminPromotionReadAuthority.js`

## Backend Endpoints Reviewed

- `GET /api/promotions`
- `GET /api/promotions/{id}`
- `POST /api/promotions`
- `PUT /api/promotions/{id}`
- `PATCH /api/promotions/{id}/toggle`
- `DELETE /api/promotions/{id}`
- `GET /api/promotions/stats`
- `POST /api/promotions/sync-to-mongo`

## Backend Services Reviewed

- `PromotionsController`
- `ApplicationDbContext` Promotion mapping and save guards
- SQL `Promotions` / `PromotionUsages`
- Promotion integration event publication for derivative projections
- `IDiscountReadModelRepairService.RebuildPromotionsAsync` as SQL-first derivative repair

## Lifecycle Findings

Promotion Lifecycle is backend-owned. Create, edit, activate, deactivate, archive, validity window, priority, business validation, accepted status, and accepted lifecycle DTOs begin only after trusted tenant/session authority, Owner/Admin/Manager authorization, SQL promotion admission, backend validation, SQL persistence, and accepted `PromotionDto` emission. Frontend `Promotions.jsx` owns lifecycle intent, editor state, navigation, presentation, refresh, and retry only.

Archive is modeled through backend delete semantics: used promotions are soft-deleted and deactivated; unused promotions may be hard-deleted. Restore is not supported in the inspected runtime.

## Concurrency Findings

Promotion lifecycle mutations now require backend `RowVersion` evidence and mandatory `If-Match`. Existing-promotion update, toggle, and archive load current tenant-owned non-deleted SQL rows through `LoadPromotionForLifecycleMutationAsync`; PostgreSQL paths use `SELECT xmin, * ... FOR UPDATE`. Missing, malformed, stale, replayed, or modified RowVersion evidence fails closed before persistence, and EF concurrency conflicts return `409 Conflict`.

## Recovery Findings

Refresh, reconnect, application restart, Redis flush, Mongo rebuild, and projection rebuild converge to SQL truth. Admin lifecycle presentation refetches no-store backend DTOs. Projection repair is SQL-first and derivative only. Stale frontend/editor state cannot survive recovery as accepted lifecycle.

## Cache Findings

React state, React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, and Mongo projections are not Promotion Lifecycle Authority. Frontend mutation wrappers require RowVersion from an admitted backend DTO and send `If-Match`; cached or fabricated DTOs without current admitted RowVersion are rejected.

## Cross-System Findings

Coupons, Checkout, Pricing, Orders, Smart Offers, Affiliate, Marketplace, Builder, CMS, and Analytics do not own Promotion Lifecycle. Those systems may consume backend-admitted promotion evidence where already certified, but cannot activate, deactivate, archive, prioritize, persist, recover, or validate Promotion lifecycle state.

## Lifecycle Pipeline

Promotion Candidate -> Mutation DTO -> backend tenant/session/role admission -> backend validation -> RowVersion / If-Match check -> SQL persistence -> accepted DTO -> frontend admission -> presentation.

Lifecycle begins in `Promotions.jsx` as administrator intent. Authority begins in `PromotionsController` after trusted tenant/session/role admission and SQL row ownership. Validation begins in backend discount/date/entity validation. Concurrency begins at backend RowVersion / If-Match validation. SQL persistence begins inside `ApplicationDbContext.SaveChangesAsync`. Presentation begins only after accepted backend `PromotionDto` admission.

## Boundaries

Lifecycle Boundary: backend SQL `Promotions`. Authority owner: backend. Recovery source: SQL. Failure behavior: 400/404/409/428 or withheld presentation.

Concurrency Boundary: RowVersion / If-Match and SQL row lock. Authority owner: backend. Recovery source: fresh no-store read. Failure behavior: stale mutations fail closed.

Recovery Boundary: SQL-backed reads and SQL-first projection repair. Authority owner: backend. Recovery source: SQL. Failure behavior: derivative stores rebuild or remain stale without authority.

Presentation Boundary: `adminPromotionReadAuthority` plus `Promotions.jsx`. Authority owner: backend DTO admission. Recovery source: backend refetch. Failure behavior: loading/error state only.

Audit Boundary: backend logs/outbox/event evidence around SQL mutations. Authority owner: backend. Recovery source: SQL/outbox. Failure behavior: mutation rejection or convergence through backend events.

## Runtime Hardening Performed

- Added backend-owned Promotion `RowVersion` mapped to PostgreSQL `xmin`.
- Added non-PostgreSQL RowVersion increment support for Promotion tests/providers.
- Added accepted `PromotionDto.RowVersion` evidence.
- Required `If-Match` for existing Promotion update, toggle, and archive mutations.
- Replaced tenantless `FindAsync(id)` lifecycle loads with tenant-scoped non-deleted mutation loading and PostgreSQL `FOR UPDATE`.
- Scoped duplicate-name lifecycle checks to current tenant and non-deleted promotions.
- Scoped archive usage checks to current tenant and non-deleted usage rows.
- Added frontend Promotion lifecycle mutation wrappers that require admitted RowVersion evidence and submit `If-Match`.
- Updated `Promotions.jsx` create/update/toggle/archive to use admitted lifecycle wrappers.
- Extended frontend Promotion DTO admission to reject missing or invalid RowVersion evidence.

## Exact Runtime Files Modified

- `PointOfSale.Core/Entities/Promotion.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.API/Controllers/PromotionsController.cs`
- `PointOfSale.Web/src/auth/adminPromotionReadAuthority.js`
- `PointOfSale.Web/src/services/adminPromotionsApi.js`
- `PointOfSale.Web/src/pages/Promotions.jsx`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/AdminPromotionLifecycleAuthorityCertificationTests.cs`

## Architecture Documentation Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/admin-promotion-lifecycle-authority-certification-findings.md`

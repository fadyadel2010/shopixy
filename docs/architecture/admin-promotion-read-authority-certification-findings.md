# PASS SF-PROMO.READ Admin Promotion Read Authority Certification Findings

Scope: PointOfSale.Web Promotion Management read runtime only.

Status: Admin Promotion Read is production-certifiable after tenant/read DTO admission hardening.

## Read findings

Promotion Read is backend-owned. `PromotionsController.GetPromotions`, `GetPromotion`, and `GetStats` now enter trusted tenant/session authority, require Owner/Admin/Manager authorization, read current SQL `Promotions` and `PromotionUsages` with tenant scope and `AsNoTracking`, resolve current SQL tenant currency, execute search/filter/sort/pagination on the backend, and emit no-store accepted `PromotionDto` or `PromotionStatsDto` evidence.

## DTO findings

Accepted Promotion DTOs exist only after backend admission. DTOs now carry backend `TenantId`, `Currency`, `Status`, `EligibilitySummary`, `CreatedAt`, and `UpdatedAt` evidence in addition to identity, name, discount metadata, scope/channel assignment, validity window, priority, usage, and active/scheduled/expired flags. `adminPromotionReadAuthority` rejects foreign tenant evidence, route-mismatched details, duplicate list identities, malformed status/scope/discount/currency evidence, missing eligibility summaries, and invalid date/number fields before presentation.

## Recovery findings

Refresh, reconnect, restart, Redis flush, Mongo rebuild, and projection rebuild converge to backend SQL truth because Promotion Management list/detail/stats re-enter SQL-backed no-store APIs. Frontend state is cleared or withheld when current user tenant evidence is unavailable or DTO admission fails.

## Cache findings

React and page state hold admitted presentation only. `adminPromotionsApi` sends `Cache-Control: no-store` and `Pragma: no-cache` for list/detail/stats reads, and backend read endpoints use no-store response metadata. React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, Mongo projections, and stale DTO replay are not Promotion Read Authority.

## Cross-system findings

Promotion Read does not consume Coupon, Checkout, Pricing, Orders, Smart Offers, Affiliate, Marketplace, Builder, CMS, or Analytics authority. Cross-system domains may observe backend-admitted promotion evidence only and cannot overwrite Promotion Identity, Status, Priority, Validity, Type, Eligibility Summary, Financial Metadata, Tenant Scope, Persistence, or Recovery.

## Promotion pipeline

SQL Promotions -> backend read admission in `PromotionsController` -> accepted `PromotionDto`/`PromotionStatsDto` -> frontend DTO admission in `adminPromotionReadAuthority` -> presentation in `Promotions.jsx`.

Promotion begins in SQL `Promotions`. Authority begins when trusted backend tenant/session/role authority reads tenant-owned SQL rows. DTO admission begins in `PromotionsController` DTO mapping and continues in frontend `adminPromotionReadAuthority`. SQL persistence begins only in backend promotion write transactions. Presentation begins only after frontend admission accepts backend DTO evidence.

## Boundaries

Promotion Boundary: backend SQL `Promotions`, `PromotionUsages`, tenant/session/role authority. Recovery source: SQL. Failure behavior: fail closed with no accepted presentation.

DTO Boundary: backend `PromotionDto`/`PromotionStatsDto` plus frontend `adminPromotionReadAuthority`. Recovery source: no-store backend refetch. Failure behavior: reject malformed, stale, foreign, duplicate, or route-mismatched evidence.

Recovery Boundary: refresh/reconnect/restart/cache/projection recovery re-enters backend reads. Recovery source: SQL. Failure behavior: withhold stale presentation.

Presentation Boundary: `Promotions.jsx` list/details/stats rendering. Recovery source: admitted DTOs only. Failure behavior: loading/error state, not fabricated promotion state.

Tenant Boundary: `ITenantContext.RequiredTenantId`, role authorization, SQL `TenantId`, and frontend current-user tenant admission. Recovery source: backend session/SQL. Failure behavior: 401/403/404 or frontend admission error.

## Runtime hardening performed

- Added Owner/Admin/Manager role authorization and no-store response semantics to admin promotion list/detail/stats reads.
- Scoped backend list/detail/stats reads to current `ITenantContext.RequiredTenantId` and non-deleted SQL promotions.
- Added backend-executed search length validation, sorting, pagination, tenant currency evidence, status evidence, eligibility summary evidence, and tenant-scoped stats.
- Added no-store frontend Promotion read API wrappers.
- Added frontend Promotion DTO and stats admission before rendering.
- Added read-only details reread from `GET /api/promotions/{id}`.
- Rendered fixed promotion money from backend DTO currency evidence.
- Added frontend sort and pagination intent controls backed by backend execution.

## Exact runtime files modified

- `PointOfSale.API/Controllers/PromotionsController.cs`
- `PointOfSale.Web/src/pages/Promotions.jsx`
- `PointOfSale.Web/src/auth/adminPromotionReadAuthority.js`
- `PointOfSale.Web/src/services/adminPromotionsApi.js`

## Regression freezes added

- `PointOfSale.Tests/Architecture/AdminPromotionReadAuthorityCertificationTests.cs`

## Architecture updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/admin-promotion-read-authority-certification-findings.md`

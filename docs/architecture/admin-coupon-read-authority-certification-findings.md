# Admin Coupon Read Authority Certification Findings

Date: 2026-07-01

## Scope

Reviewed only the PointOfSale.Web Coupon Management runtime for coupon listing, search, filter, sort, pagination, and details presentation, plus backend APIs and SQL/Mongo read-model services directly relevant to coupon read authority.

## Finding

Coupon Read is production-certifiable after runtime hardening. Backend SQL coupon authority now emits tenant, code, name, discount, currency, usage, status, validity, and eligibility DTO evidence for admin list and details reads. Frontend Coupon Management admits backend DTOs before presentation and keeps search, filter, sort, pagination, selection, refresh, and navigation as request/presentation intent only.

## Closed Gaps

- Admin coupon reads were tenant-filtered SQL reads, but they were not explicitly no-store at the backend or frontend read boundary.
- The frontend stored raw coupon DTO arrays directly in React state without a coupon-specific admission boundary.
- Fixed-amount coupon presentation formatted money with frontend settings instead of backend coupon read DTO currency evidence.
- Coupon details presentation reused edit state rather than a read-only details DTO re-read boundary.

## Hardening

- `CouponsController.GetCoupons` and `CouponsController.GetCoupon` are no-store, tenant-scoped, SQL-backed, `AsNoTracking` reads.
- `CouponDto` now carries backend-owned `TenantId`, `Name`, `Currency`, `Status`, and `EligibilitySummary`.
- Admin coupon list search, active filter, sort, and pagination are accepted as bounded query intent and executed by the backend before DTO emission.
- `adminCouponsApi` sends no-store read headers and admits list/details DTOs before returning them to the page.
- `adminCouponReadAuthority` rejects malformed, foreign-tenant, duplicate, incomplete, or route-mismatched coupon DTO evidence.
- `Coupons.jsx` renders fixed coupon money from `coupon.currency`, renders status/eligibility from backend DTO evidence, and opens read-only details through `GET /api/coupons/{id}`.

## Authority Rule

Coupon Identity, Coupon Code, Coupon Name, Coupon Type, Discount Type, Discount Value, Currency, Status, Usage Limits, Usage Counts, Eligibility Summary, Validity Window, Tenant Ownership, Persistence, and Recovery are backend-owned. Frontend Coupon Management may search, filter, sort, paginate, select, navigate, refresh, retry, format, and render admitted DTOs only. React, React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, Mongo projections, stale DTO replay, modified DTOs, injected DTOs, refresh, reconnect, application restart, Redis flush, Mongo rebuild, and projection rebuild must never create, preserve, recover, overwrite, or distribute Coupon Read Authority.

## Residual Operational Risk

Residual proof remains deployed browser/API/database/projection chaos for concurrent administrators, rapid refresh/retry, timeout-after-read, backend restart, Redis flush, Mongo rebuild, projection rebuild lag, BFCache restore, malformed gateway payloads, and direct browser state tampering. These paths are invalidation/refetch/fail-closed concerns only and do not establish frontend coupon authority.

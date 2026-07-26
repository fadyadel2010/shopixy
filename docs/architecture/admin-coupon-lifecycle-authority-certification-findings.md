# Admin Coupon Lifecycle Authority Certification Findings

Date: 2026-07-01

## Scope

Reviewed only the PointOfSale.Web Coupon Management runtime for create, edit, activate, deactivate, validity changes, archive, refresh/reconnect, and accepted coupon lifecycle presentation, plus the directly consumed backend coupon controller, DTO, entity, SQL configuration, and coupon lifecycle mutation boundaries.

## Finding

Coupon Lifecycle is production-certifiable after RowVersion/If-Match hardening. Backend SQL coupon authority owns Coupon Identity, Code, Status, Activation, Deactivation, Validity Window, Start Date, End Date, Archive State, lifecycle rules, business validation, concurrency, audit evidence, persistence, recovery, and projection distribution. Frontend Coupon Management submits lifecycle candidates only and presents accepted backend DTOs only after admission.

## Closed Gap

- Existing coupon write endpoints were tenant-scoped SQL mutations with backend business validation and projection outbox events, but update, toggle, and archive did not require accepted backend RowVersion evidence or a mandatory `If-Match` precondition. A stale administrator or replayed DTO could therefore reach backend mutation validation without an explicit certifiable concurrency boundary.

## Hardening

- `CouponDto` now carries backend-owned `RowVersion` evidence from SQL `xmin`.
- `ApplicationDbContext` configures coupon `xmin` as the EF optimistic concurrency token and increments the shadow value for non-PostgreSQL providers.
- `CouponsController.UpdateCoupon`, `ToggleCouponStatus`, and `DeleteCoupon` now load the current tenant-owned SQL coupon through the lifecycle mutation boundary, require `If-Match`, reject malformed/missing/stale RowVersion evidence, bind EF original RowVersion, catch concurrency failures, and emit accepted backend DTOs after update/toggle.
- `adminCouponReadAuthority` admits coupon DTOs only when RowVersion evidence is present and positive.
- `adminCouponsApi` routes create/update/toggle/archive through one coupon lifecycle boundary. Existing-coupon mutations refuse to submit without admitted backend RowVersion evidence and send it in `If-Match`.
- `Coupons.jsx` no longer calls raw coupon mutation endpoints directly; it submits candidates through the lifecycle API wrapper and refetches backend truth after accepted mutations.
- Added `AdminCouponLifecycleAuthorityCertificationTests`.

## Authority Rule

Coupon Lifecycle begins as frontend lifecycle intent only. Authority begins at backend trusted tenant/session authority, Owner/Admin/Manager authorization, current SQL `Coupons`, backend business validation, RowVersion/`If-Match` concurrency validation, SQL persistence, outbox-backed lifecycle projection events, and accepted DTO emission. Presentation begins only after frontend DTO admission.

React, React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, Mongo projections, stale DTO replay, modified status/validity/archive/code evidence, missing or modified RowVersion, removed `If-Match`, duplicate lifecycle requests, refresh, reconnect, application restart, Redis flush, Mongo rebuild, and projection rebuild may invalidate, refetch, or fail closed only. They must never create, preserve, recover, overwrite, or distribute accepted Coupon Lifecycle Authority.

## Residual Operational Risk

Residual proof remains deployed browser/API/database/projection chaos for two administrators, duplicate clicks, timeout-after-commit, stale RowVersion, malformed gateway DTOs, BFCache restore, Redis flush, Mongo rebuild, projection lag, and direct browser state tampering. These are retry/refetch/fail-closed concerns only and do not establish frontend coupon lifecycle authority.

# PASS SF-PRM.RECOVERY-AUDIT Promotion Recovery and Audit Authority Certification

Date: 2026-07-02

## Verdict

Promotion Recovery and Audit are production-certifiable for the inspected runtime paths. No runtime behavior change was required.

Backend SQL authority owns Promotion Reservation, Usage, Confirmation, Release-by-failure-state, Replay Protection, Idempotency, Audit Trail, Audit Events, Outbox Events, Projection Repair, Projection Rebuild, Recovery Source, Persistence, and convergence. Frontend requests recovery and audit only, then renders backend DTOs.

## Pages Reviewed

- `PointOfSale.Web/src/pages/AuditLog.jsx`
- Prior certified evidence: `docs/architecture/storefront-promotions-authority-certification-findings.md`
- Prior certified evidence: `docs/architecture/storefront-promotion-financial-authority-certification-findings.md`
- Prior certified evidence: `docs/architecture/coupon-recovery-audit-authority-certification-findings.md`

## Backend Endpoints Reviewed

- `GET /api/storefront/cart/recover/{token}`
- `GET /api/audit-logs`
- `GET /api/audit-logs/export`
- `POST /api/storefront/checkout`
- Payment lifecycle success/recovery entry through `PaymentLifecycleService`
- `POST /api/promotions/sync-to-mongo` recovery boundary where directly relevant to promotion projection repair

## Backend Services Reviewed

- `StorefrontCartRecoveryController`
- `CheckoutService`
- `PaymentLifecycleService`
- `PromotionProjectionConsumer`
- `DiscountReadModelRepairService`
- `AuditLogsController`
- `ApplicationDbContext`
- `PromotionUsage`
- `PromotionEvents`

## Recovery Findings

- Recovery begins only when a backend recovery request validates the signed recovery token, resolves current SQL abandoned-cart ownership, and recomposes a live backend cart through `ICartService.GetCartAsync`.
- Stored abandoned-cart snapshots are not Promotion Recovery authority. If a live backend-admitted cart cannot be resolved, recovery fails closed with `live_cart_unavailable`.
- Accepted recovery state exists only after backend SQL/cart authority. React, Zustand, localStorage, sessionStorage, browser cache, Redis, Mongo, stale DTOs, and route tokens cannot restore accepted Promotion Usage or discount authority.
- Promotion Reservation begins only in checkout after backend pricing/order authority is revalidated inside the serializable SQL checkout transaction.
- Promotion Usage begins only after payment success re-locks current SQL promotion authority, checks lifecycle/minimum/usage capacity, recomputes current discount against the persisted order, and inserts `PromotionUsage`.

## Audit Findings

- Promotion Audit is backend-owned. `AuditLogsController` reads current tenant SQL `AuditLogs` under `Owner,Admin` authorization, uses `AsNoTracking`, orders by backend timestamp, and emits no-store responses.
- Audit export is the same backend SQL read boundary with tenant filter and no-store response semantics.
- `AuditLog.jsx` sends no-store/no-cache GET requests and has no audit mutation, localStorage, or sessionStorage authority.
- SQL audit evidence is append-only in `ApplicationDbContext`; updates/deletes fail closed through `GuardAppendOnlyEvidence<AuditLog>()`.

## Projection Findings

- Projection begins only after backend promotion events or explicit repair. Mongo promotion documents, product promotion overlays, category promotion overlays, Redis invalidation, and search sync are derivative visibility evidence.
- `PromotionProjectionConsumer` uses projection event fences, SQL authority reload, exact SQL version validation, freshness filters, and SQL-derived current usage. Event payload usage counts do not become authority.
- `DiscountReadModelRepairService.RebuildPromotionsAsync` uses `SqlTruthRebuildGuard`, reads SQL `Promotions`, deletes stale Mongo promotion documents absent from SQL truth, rebuilds product/category promotion decorations from SQL winners, and invalidates Redis cache.
- Mongo Projection cannot become Promotion Authority or Recovery Authority.

## Concurrency Findings

- Duplicate checkout and payment retry re-enter backend checkout/payment authority.
- Duplicate payment confirmation first checks existing `PromotionUsage`, then relies on the unique `(TenantId, PromotionId, OrderId)` SQL index and duplicate-key collapse.
- Duplicate events and consumer restarts collapse through `ProjectionConsumerEventFence` and freshness filters.
- Replay cannot fabricate Promotion Usage because usage writes occur only in `PaymentLifecycleService` after SQL promotion/order validation; consumers update derivative Mongo fields only.

## Cache Findings

- React, React Query, Zustand, LocalStorage, SessionStorage, and browser cache/BFCache own refresh, retry, presentation, navigation, and rendering only.
- Redis is invalidation/cache evidence only and cannot become Recovery Source.
- Mongo is projection evidence only and cannot become Recovery Source.
- Cache loss, Redis flush, Mongo rebuild, restart, reconnect, and BFCache restore re-enter backend SQL/cart/order/payment/projection authority or fail closed.

## Cross-System Findings

- Checkout may reserve promotion capacity only after backend pricing/order authority.
- Orders persist promotion identity/discount snapshots as backend SQL evidence.
- Payments confirm usage only after success and current promotion revalidation.
- Coupons remain a separate backend-owned discount authority and enter promotion conflict resolution only as backend-prevalidated evidence.
- Inventory, Smart Offers, Affiliate, Marketplace, Builder, CMS, and Analytics cannot overwrite Promotion Recovery, Usage, Audit, or Projection authority.

## Recovery Pipeline

Promotion Reservation -> Checkout -> Payment -> Promotion Usage -> Audit -> Outbox -> Projection -> Recovery -> Presentation.

- Recovery begins: backend `StorefrontCartRecoveryController` after token, SQL cart, ownership, and live-cart authority admission.
- Usage begins: `PaymentLifecycleService.ConfirmOfferPromotionUsageAsync` after payment success and current SQL promotion revalidation.
- Audit begins: backend SQL audit append/read authority.
- Projection begins: `PromotionProjectionConsumer` event handling or `DiscountReadModelRepairService.RebuildPromotionsAsync` after SQL authority reload.
- SQL persistence begins: checkout SQL order transaction for reservation evidence and payment lifecycle SQL `PromotionUsage` insert for usage.
- Presentation begins: frontend recovery/audit views after accepted backend DTOs.

## Boundary Review

- Recovery Boundary: backend-owned; source is live backend cart plus SQL cart/order/payment/promotion truth; failure behavior is fail closed.
- Usage Boundary: backend-owned; source is SQL promotion/order/payment success; failure behavior is no usage insert or duplicate collapse.
- Audit Boundary: backend-owned; source is append-only SQL `AuditLogs`; failure behavior is no-store read/export failure.
- Projection Boundary: backend-owned derivative repair/consumer; source is SQL promotion truth; failure behavior is stale/no side effects, rebuild, or retry.
- Presentation Boundary: frontend-owned rendering only; source is accepted backend DTO; failure behavior is loading/error/empty presentation.

## Runtime Hardening Performed

- None. Existing runtime already enforced backend-owned recovery, usage, audit, projection repair, event replay collapse, and cache non-authority for inspected paths.

## Exact Runtime Files Modified

- None.

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/PromotionRecoveryAuditAuthorityCertificationTests.cs`

## Architecture Documentation Updates

- Added this findings document.
- Updated all five `.ai-context` authority files with `PASS SF-PRM.RECOVERY-AUDIT`.

## Explicit Certification Answers

- Did you review ONLY the directly involved runtimes? Yes.
- Which frontend files were modified? None.
- Which backend files were modified? None.
- Who owns Promotion Recovery? Backend SQL/cart/order/payment/promotion authority.
- Who owns Promotion Reservation? Backend checkout SQL transaction authority.
- Who owns Promotion Usage? Backend payment lifecycle SQL authority.
- Who owns Promotion Usage Confirmation? Backend payment lifecycle SQL authority.
- Who owns Promotion Release? Backend order/payment failure and expiry state; promotion reservation evidence is released by backend failure/expiry exclusion from active reservation counts.
- Who owns Replay Protection? Backend SQL uniqueness, idempotency, event fences, and freshness filters.
- Who owns Idempotency? Backend checkout/payment services and SQL constraints.
- Who owns Audit Trail? Backend append-only SQL `AuditLogs`.
- Who owns Audit Events? Backend audit/event/outbox services.
- Who owns Outbox Events? Backend services after SQL authority admission.
- Who owns Projection Rebuild? Backend SQL-derived repair service.
- Who owns Recovery Source? Backend SQL truth plus live backend cart/order/payment/promotion authority.
- Can frontend fabricate Recovery? No.
- Can frontend fabricate Audit? No.
- Can replay create duplicate Promotion Usage? No.
- Can duplicate consumers change Promotion state? No; they can only attempt derivative projection updates and are fenced/freshness-checked.
- Can Redis become Recovery Authority? No.
- Can Mongo Projection become Recovery Authority? No.
- Can React become Recovery Authority? No.
- Can LocalStorage become Recovery Authority? No.
- Can Recovery diverge from SQL Truth? No inspected path allows divergence; recovery re-enters SQL/live backend authority or fails closed.
- Where does Recovery begin? Backend recovery request after token, SQL cart, ownership, and live-cart authority admission.
- Where does Audit begin? Backend SQL audit append/read authority.
- Where does Projection begin? Backend event consumer or SQL repair service after SQL authority reload.
- Where does SQL persistence begin? Checkout SQL transaction and payment lifecycle SQL `PromotionUsage` insert.
- Where does Presentation begin? Frontend after accepted backend DTOs.
- Were runtime changes required? No.
- Is Promotion Recovery & Audit production-certifiable? Yes.

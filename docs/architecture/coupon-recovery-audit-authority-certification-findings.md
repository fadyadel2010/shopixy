# 2026-07-01 PASS SF-CPN6 Coupon Recovery and Audit Authority Certification Findings

Scope reviewed: Coupon Recovery and Audit runtime responsible for coupon recovery, audit integrity, projection recovery, cache recovery, replay protection, recovery convergence, and directly consumed checkout/order/payment reservation and usage recovery paths. Coupon Read, Lifecycle, Eligibility, Redemption, Financial, Promotion, Smart Offers, Affiliate, Marketplace, Builder, CMS, and Analytics authorities were treated as prior architectural truth and were not re-certified.

Pages and frontend runtime reviewed:
- `PointOfSale.Storefront/src/components/cart/CouponInput.tsx`
- `PointOfSale.Storefront/src/store/cartStore.ts`
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`
- `PointOfSale.Web/src/pages/AuditLog.jsx`

Backend endpoints reviewed:
- `POST /api/storefront/cart/apply-coupon`
- `POST /api/storefront/cart/remove-coupon`
- `GET /api/storefront/cart` where it revalidates coupon presentation before response
- checkout order creation path where coupon reservation is directly consumed
- payment lifecycle success/failure paths where coupon usage confirmation/release is directly consumed
- `GET /api/audit-logs`
- `GET /api/audit-logs/export`
- `POST /api/coupons/sync-to-mongo`

Backend services reviewed:
- `StorefrontCartController`
- `StorefrontCouponService`
- `CouponValidationService`
- `CheckoutService`
- `CheckoutPaymentIntentRecoveryService`
- `PaymentLifecycleService`
- `InventoryReservationCleanupJob`
- `CouponProjectionConsumer`
- `DiscountReadModelRepairService`
- `AuditLogsController`
- `CouponReservation`
- `CouponUsage`
- MassTransit outbox event publishing used by coupon events

Recovery findings:
- Coupon Recovery remains backend-owned. Recovery begins from SQL `Coupons`, `Orders`, `CouponReservations`, `CouponUsages`, payment lifecycle state, and SQL-backed coupon rules.
- Checkout retry, timeout-after-commit, application restart, Redis flush, Mongo rebuild, background consumer restart, and outbox replay re-enter backend SQL authority or fail closed.
- Stale coupon state cannot survive as authority because cart presentation is revalidated by backend cart/coupon services, checkout revalidates in a serializable SQL transaction, confirmation uses SQL order/reservation/usage state, and projections rebuild from SQL.

Audit findings:
- Audit Trail and Audit Events remain backend-owned. Admin audit history is read from SQL `AuditLogs` under current tenant and Owner/Admin authorization.
- Frontend audit presentation has no audit mutation path and cannot fabricate audit evidence; it can only request no-store backend history/export and render returned rows.
- Runtime hardening added no-store response/request semantics for audit-log reads and exports so browser cache cannot become audit presentation authority.

Replay findings:
- Replay cannot create duplicate Coupon Usage. SQL uniqueness on `(TenantId, OrderId)`, transactional `ConfirmCouponUsageAsync`, duplicate exception collapse, and idempotent payment lifecycle replay reuse one `CouponUsage` per order.
- Duplicate reservation replay is collapsed by `(TenantId, OrderId)` reservation uniqueness; active customer reservation uniqueness prevents parallel pending reservations for the same coupon/customer where required.
- Outbox/consumer replay is derivative only because `CouponProjectionConsumer` uses event fences, freshness filters, SQL coupon reloads, SQL usage counts, and tombstones.

Projection findings:
- Projection begins only after backend SQL coupon events or explicit admin repair. Mongo coupon documents are derivative presentation/read models.
- `CouponProjectionConsumer` maps coupon documents from current SQL coupon rows and derives usage from SQL `CouponUsages`, not event payload usage counts.
- `DiscountReadModelRepairService.RebuildCouponsAsync` reads tenant SQL coupons, upserts Mongo documents, and deletes stale documents absent from SQL truth.

Concurrency findings:
- Reservation, confirmation, and release require an open SQL transaction.
- Coupon capacity mutations lock the SQL coupon row for supported relational providers.
- Checkout creates orders and coupon reservations inside a serializable SQL transaction; payment success/failure confirms or releases coupon state inside payment lifecycle SQL transactions.
- Parallel recovery or multiple consumers cannot diverge accepted coupon state because accepted state is SQL-owned and projections are replay-collapsed derivatives.

Cache findings:
- React, Zustand, browser cache, LocalStorage, SessionStorage, Redis cart state, and Mongo coupon projections are not Recovery Authority.
- Storefront cart recovery presentation stores only accepted backend DTOs, resets `skipNextFetch`, and must re-enter backend cart APIs for authority.
- Redis flush removes volatile cart/idempotency cache only; SQL order/reservation/usage truth remains decisive. Mongo rebuild recomposes derivative visibility from SQL.

Cross-system findings:
- Recovery begins at backend checkout/order/payment SQL truth, not frontend refresh.
- Audit becomes authoritative at backend SQL/audit logging and SQL audit reads.
- Inventory/payment/order services may trigger coupon release/confirmation, but they do not transfer coupon authority to frontend or projections.
- Promotion, Smart Offers, Affiliate, Marketplace, Builder, CMS, and Analytics were not reviewed beyond confirming they are not Coupon Recovery or Audit authority in this pass.

Recovery pipeline:
- SQL Truth: `Coupons`, `Orders`, `CouponReservations`, `CouponUsages`, payment/order state.
- Reservation: `CheckoutService` calls `ReserveCouponAsync` inside serializable SQL checkout transaction.
- Usage: `CouponUsage` is created only in `ConfirmCouponUsageAsync` after payment success.
- Payment Confirmation: `PaymentLifecycleService` confirms usage or releases reservations inside backend SQL transactions.
- Audit Events: backend SQL audit rows and domain/outbox events are backend-owned evidence.
- Outbox: coupon events are staged through backend event publishing/outbox with SQL state.
- Projection: `CouponProjectionConsumer` and repair rebuild Mongo from SQL authority.
- Presentation: Storefront cart/checkout and Web audit log render backend DTOs only.

Boundary review:
- Recovery Boundary: backend SQL/services own recovery; source is SQL; failures refetch, replay-collapse, release, rebuild, or fail closed.
- Audit Boundary: backend SQL `AuditLogs` owns audit trail; source is SQL; failures withhold presentation.
- Reservation Boundary: `CouponValidationService.ReserveCouponAsync` owns reservation; source is SQL coupon/order/customer state; duplicates collapse or fail closed.
- Usage Boundary: `ConfirmCouponUsageAsync` owns usage; source is SQL order/reservation/coupon state; duplicate usage collapses to existing SQL row.
- Projection Boundary: `CouponProjectionConsumer`/repair own projection writes; source is SQL; stale/replayed events are fenced or tombstoned.
- Replay Boundary: SQL uniqueness, idempotency, transaction, event fence, and freshness filter own replay protection; failures collapse or fail closed.
- Presentation Boundary: Storefront/Web own rendering, refresh, retry, navigation, and recovery intent only; failures clear/withhold/refetch presentation.

Runtime hardening performed:
- Added no-store backend response semantics to `AuditLogsController.GetAuditLogs` and `ExportAuditLogs`.
- Added no-store frontend request headers to `PointOfSale.Web/src/pages/AuditLog.jsx` audit history and export reads.

Exact runtime files modified:
- `PointOfSale.API/Controllers/AuditLogsController.cs`
- `PointOfSale.Web/src/pages/AuditLog.jsx`

Regression freezes added:
- `PointOfSale.Tests/Architecture/CouponRecoveryAuditAuthorityCertificationTests.cs`

Architecture documentation updates:
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

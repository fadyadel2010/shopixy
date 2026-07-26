# 2026-07-01 PASS SF-CPN.FINAL Complete Coupon End-to-End Authority Certification

Overall findings:
- Complete Coupon runtime across PointOfSale.Storefront, PointOfSale.Web, and directly consumed backend APIs/services remains production-certifiable after source-documentation hardening. Coupon begins in backend SQL `Coupons`; accepted Coupon Authority begins only after backend tenant/session/role/customer admission reaches SQL-backed coupon services/controllers and accepted DTO emission. Frontend owns candidate entry, presentation, navigation, rendering, retry, and recovery intent only.

Read findings:
- Admin coupon reads use no-store SQL-backed `CouponsController.GetCoupons` and `GetCoupon`, resolve tenant currency from SQL, and require frontend DTO admission before presentation.

Lifecycle findings:
- Create/update/toggle/archive remain backend-owned through Owner/Admin/Manager authorization, normalized-code uniqueness, SQL `Coupons`, `RowVersion`/`If-Match` for existing mutations, and outbox projection events.

Eligibility findings:
- Minimum order, maximum discount, usage restrictions, product/category scope, and exclusions are canonicalized and validated against current tenant-owned SQL catalog rows before persistence.

Redemption findings:
- Storefront coupon entry submits code/token candidates only. Cart apply validates authenticated customer/tenant authority through `StorefrontCouponService` and SQL-backed `CouponValidationService`, persists only accepted cart coupon presentation state, and revalidates before response.

Financial findings:
- Checkout pricing preview and final checkout recalculate backend pricing, revalidate coupon identity/code/discount/current rules, compare expected pricing, persist SQL order snapshots, and reserve coupon capacity inside the serializable order transaction.

Recovery findings:
- Reservation, usage confirmation, release, outbox replay, consumer replay, Redis flush, Mongo rebuild, projection rebuild, restart, and timeout-after-commit converge to SQL `Coupons`, `Orders`, `CouponReservations`, `CouponUsages`, payment/order state, and current coupon rules.

Audit findings:
- Audit presentation is backend SQL-owned and no-store. Coupon audit evidence is emitted by backend mutation/payment/recovery boundaries; PointOfSale.Web audit history can render only current tenant-owned backend audit DTOs.

Cross-system findings:
- Checkout, Orders, Payments, Pricing, Inventory, Promotions, Smart Offers, Affiliate, Marketplace, Builder, CMS, and Analytics cannot overwrite Coupon Authority. Coupon can participate through validated candidates, snapshots, events, and derivative presentation, but cannot overwrite another domain's authority.

End-to-end pipeline:
- Coupon Creation: admin intent enters `CouponsController.CreateCoupon`; SQL `Coupons` begins identity/persistence.
- Lifecycle: backend status/validity/archive rules and RowVersion preconditions own accepted state.
- Eligibility: backend validates and canonicalizes SQL catalog scope before persistence.
- Coupon Candidate: Storefront code/token input begins presentation intent only.
- Backend Validation: `StorefrontCartController`, `StorefrontCouponService`, and `CouponValidationService` validate SQL coupon rules.
- Pricing: `CheckoutService` and `StorefrontPricingService` calculate canonical totals.
- Reservation: `ReserveCouponAsync` begins inside checkout SQL transaction.
- Checkout: final transaction revalidates coupon/pricing/currency/shipping/payment.
- Order Snapshot: SQL `Orders` persists coupon identity/code/discount snapshot.
- Payment: payment lifecycle confirms success or releases on failure/cancel/expiry.
- Usage Confirmation: `ConfirmCouponUsageAsync` creates or reuses one SQL `CouponUsage` per order.
- Audit: backend mutation/payment/recovery boundaries emit SQL audit/event evidence.
- Outbox: backend events commit with SQL changes.
- Projection: consumers reload SQL and write derivative Mongo documents with fences/freshness/tombstones.
- Presentation: Storefront/Web render accepted DTOs only.

Authority map:
- Coupon Identity, Code, Lifecycle, Eligibility, Redemption, Financial Authority, Pricing, Reservation, Usage, Audit, Recovery, Projection Source, SQL Truth, Persistence: backend SQL/services/controllers.
- Candidate Entry, Presentation, Navigation, Rendering, Retry, Recovery Intent: frontend.
- Redis, Mongo, React, React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache: derivative or transient only.

Runtime hardening performed:
- Corrected stale coupon CQRS comments in `CouponsController`, `CouponProjectionConsumer`, `CouponReadRepository`, `ICouponReadRepository`, and `Program` so Mongo is documented as derivative visibility/read-model evidence only and SQL-backed validation remains accepted Coupon Authority.

Regression freezes added:
- `PointOfSale.Tests/Architecture/CouponEndToEndAuthorityCertificationTests.cs`.

Architecture documentation updated:
- `.ai-context/PROJECT_CONTEXT.md`.
- `.ai-context/ARCHITECTURE_TRUTH.md`.
- `.ai-context/KNOWN_RISKS.md`.
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- `.ai-context/CODEX_RULES.md`.

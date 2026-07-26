# PASS SF-ORD5 Storefront Order Lineage Admin Presentation Authority Certification

Scope reviewed: PointOfSale.Web Order Management runtime responsible for displaying Storefront-origin orders, plus directly consumed backend admin order DTO/API/service and Storefront checkout/read lineage boundaries. Affiliate order runtime, CommerceHub marketplace runtime internals, checkout implementation beyond Storefront order lineage, payment, shipping, coupons, promotions, Builder, CMS, analytics, and previously certified SF-ORD1 through SF-ORD4 authorities were not re-reviewed as independent runtimes.

Pages reviewed:
- `PointOfSale.Web/src/pages/Orders.jsx`

Backend endpoints reviewed:
- `GET /api/admin/orders`
- `GET /api/admin/orders/{orderId}`
- `PUT /api/admin/orders/{orderId}/status` only as a certified lifecycle re-entry boundary
- Storefront read repair boundary in `StorefrontOrdersReadController` only where it proves SQL `OrderSource.Ecommerce` before projection presentation

Backend services reviewed:
- `AdminOrderService`
- `CheckoutService` only where it persists canonical Storefront order lineage
- `StorefrontOrdersReadController` projection repair/read authority helpers

Storefront lineage findings:
- Storefront Order Origin begins in backend checkout order creation when SQL `Orders.Source` is set to `OrderSource.Ecommerce`.
- Backend SQL `Orders` owns Source Type, Channel Identity, Canonical Order, Order Identity, Online Customer Identity, Storefront cart lineage, tenant ownership, persistence, and recovery.
- `AdminOrderService` maps `Order.Source` and `SourceOrderId` from SQL into admin DTOs. The frontend now rejects missing, malformed, contradictory, or non-Storefront source evidence before rendering Storefront lineage.

Canonicalization findings:
- Canonicalization begins in backend checkout and SQL order creation, not React. Storefront checkout evidence is admitted into a canonical SQL `Order` with tenant, online customer, cart, payment, totals, item, and source lineage.
- Admin presentation consumes `AdminOrderListItemDto` and `AdminOrderDetailsDto` after backend SQL mapping and frontend admission. The frontend can format/render but cannot modify canonical lineage.

Recovery findings:
- Refresh, reconnect, app restart, Redis flush, Mongo rebuild, and projection rebuild converge through backend SQL reads or guarded Storefront read projection repair.
- Storefront lineage cannot change after recovery because SQL `Orders.Source`, `TenantId`, `OnlineCustomerId`, `CartId`, and canonical order identity remain the recovery source.

Cache findings:
- React, React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, Mongo, stale DTOs, and projection rebuilds are not lineage authority.
- Runtime hardening removed frontend Storefront source fallback; source presentation now requires backend-admitted `Ecommerce` or `Marketplace` evidence.

Cross-system findings:
- Storefront checkout creates canonical Storefront orders; Orders admin displays admitted SQL DTOs; Inventory, Payments, Fulfillment, Shipping, and projection paths observe or re-enter existing backend authorities. None can overwrite Storefront Origin.

Storefront lineage pipeline:
- Storefront Checkout begins as submission intent in the Storefront frontend.
- Backend Order Creation begins in `CheckoutController`/`CheckoutService`.
- Canonicalization begins when `CheckoutService.CreateOrderAsync` constructs SQL `Order` with `Source = OrderSource.Ecommerce`.
- Authority begins after trusted tenant/customer/session context, SQL StoreConfig availability, cart ownership, pricing, inventory, payment method, idempotency, serializable transaction, and SQL persistence admission.
- SQL persistence begins when the canonical `Order`, `OrderItems`, idempotency, payment/outbox/reservation evidence commit.
- AdminOrderDto mapping begins in `AdminOrderService`.
- Frontend admission begins in `adminOrdersApi` and `commerceHubVendorIdentityAuthority`.
- Presentation begins only after admitted DTO source evidence is rendered in `Orders.jsx`.

Boundaries:
- Storefront Boundary: backend checkout/session/cart authority. Recovery source: SQL checkout/order/cart records. Failure behavior: reject or fail closed.
- Canonical Boundary: SQL `Orders` creation with `OrderSource.Ecommerce`. Recovery source: SQL `Orders`. Failure behavior: no canonical order without backend admission.
- Order Boundary: admin order APIs/services reading SQL DTOs. Recovery source: SQL `Orders`, `CommerceHubSubOrders`, payment and shipment SQL evidence. Failure behavior: not found or fail closed.
- Recovery Boundary: SQL re-entry and projection repair. Recovery source: SQL. Failure behavior: projection unavailable rather than projection authority.
- Presentation Boundary: frontend DTO admission. Recovery source: backend refetch. Failure behavior: reject missing/contradictory source evidence.

Runtime hardening performed:
- Added `requireBackendOrderSourceAuthority` to reject missing, malformed, non-Storefront, or contradictory admin order source evidence before presentation.
- Updated `Orders.jsx` to render source labels only from admitted backend source evidence and removed Storefront fallback presentation.

Exact runtime files modified:
- `PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.js`
- `PointOfSale.Web/src/pages/Orders.jsx`

Regression freezes added:
- `PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.test.ts`
- `PointOfSale.Tests/Architecture/StorefrontOrderLineageAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/AdminOrderReadAuthorityCertificationTests.cs`

Architecture documentation updates:
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/storefront-order-lineage-admin-presentation-authority-certification-findings.md`

Certification: Storefront Order Lineage is production-certifiable after frontend source-admission hardening.

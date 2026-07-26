# Admin Order Read Authority Certification Findings

Date: 2026-06-30

Scope reviewed: PointOfSale.Web Order Management runtime for listing, searching, filtering, sorting, pagination, details viewing, refresh/reconnect/restart recovery, and the directly consumed backend admin order read APIs/services.

Pages reviewed: `PointOfSale.Web/src/pages/Orders.jsx` and route admission in `PointOfSale.Web/src/App.jsx`.

Backend endpoints reviewed: `GET /api/admin/orders` and `GET /api/admin/orders/{orderId}` in `PointOfSale.API/Controllers/AdminOrdersController.cs`.

Backend services/DTOs reviewed: `IAdminOrderService`, `AdminOrderService`, and `AdminOrderDtos`.

Findings: production-certifiable after runtime hardening. Backend owns accepted order data, identity, number, status, customer evidence, totals, currency, line items, payment/shipping summaries where returned, pagination, filtering, sorting, persistence, recovery source, and SQL read admission. Frontend owns only search/filter/sort/page intent, selected details drawer, navigation, refresh/retry, loading/error UI, formatting, and rendering.

Runtime hardening performed: added backend currency evidence to list DTOs; added backend search/sort query validation and execution; marked admin order reads no-store at request and response boundaries; changed frontend list totals to render backend currency instead of hardcoded `EGP`; added the architecture regression freeze.

Pipeline: SQL `Orders`/`CommerceHubSubOrders` authority -> tenant ownership and backend query validation -> `AdminOrderService` SQL read -> `AdminOrderListItemDto`/`AdminOrderDetailsDto` -> frontend lifecycle/tenant/route DTO admission -> presentation.

Recovery: refresh, reconnect, browser restart, app restart, Redis flush, and Mongo projection rebuild re-enter the backend admin order SQL read APIs or fail closed. React, React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, Mongo projection, stale DTOs, and replay cannot become order authority.

Regression freeze: `PointOfSale.Tests/Architecture/AdminOrderReadAuthorityCertificationTests.cs`.

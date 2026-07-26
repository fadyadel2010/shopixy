# 2026-06-30 PASS SF-ADM3.2 Admin Order Lifecycle Authority Certification Findings

Scope reviewed: PointOfSale.Web Orders runtime and directly consumed backend admin order lifecycle mutation APIs/services only.

Finding: production-certifiable after hardening. Frontend submits requested status and optional reason candidates only. Backend owns lifecycle validation, business rules, concurrency, RowVersion, If-Match validation, audit note/timestamp, persistence, recovery, and accepted DTO emission.

Runtime hardening performed:
- Added backend RowVersion evidence to admin order details and status update responses.
- Required `If-Match` for `PUT /api/admin/orders/{orderId}/status`.
- Compared expected RowVersion with current SQL `Orders` RowVersion under write lock and rejected stale replay with 409.
- Bound EF original RowVersion before persistence and kept `OrderStatusSaveChangesInterceptor` as the authorized status write boundary.
- Added Web details status candidate submission with RowVersion `If-Match`, no browser storage authority, and backend reread after admission.
- Added architecture regression freeze `PointOfSale.Tests/Architecture/AdminOrderLifecycleAuthorityCertificationTests.cs`.

State machine result:
- Generic admin status update legal path: same status no-op and `OnHold` where backend validation allows it.
- Generic admin status update illegal paths: direct `Cancelled`, `Shipped`, `Delivered`, `Completed`, `Refunded`, direct operational `Confirmed`/`Processing`, reopening `Cancelled`/`Refunded`, and mutating terminal `Delivered`/`Completed`.
- Operational progression remains owned by dedicated backend payment/fulfillment lifecycle boundaries, not by the Web status control.

Recovery result: refresh, reconnect, restart, Redis flush, Mongo rebuild, projection rebuild, stale React Query data, and replay converge to SQL `Orders` through backend reads/mutations or fail closed.

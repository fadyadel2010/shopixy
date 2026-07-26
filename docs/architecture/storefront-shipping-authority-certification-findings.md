# PASS P2-F Storefront Shipping Authority Certification Findings

Date: 2026-06-08

## Verdict

PASS P2-F is production-certifiable for inspected storefront shipping authority after runtime tracking-spoof hardening.

Tenant-owned PostgreSQL `Shipments`, `ShippingMethods`, `CarrierConfigurations`, `ShippingWebhookLogs`, external-registration evidence, shipment lifecycle timestamps, and current SQL `Orders` own shipment identity, lifecycle, carrier registration, tracking, webhook, delivery, reconciliation, and shipment/order visibility truth.

Shipping may advance fulfillment status only through `IOrderFulfillmentService.SynchronizeFromShipmentAsync(...)`. Shipping may not create payment, treasury, inventory, refund, promotion, or reporting truth. COD payment recognition remains owned by `IPaymentLifecycleService.MarkCodDeliveredAsync(...)` after fulfillment delivery authority is proven.

## Findings

- Shipment creation: SQL serializable creation proves same-tenant order, active shipping method, order financial readiness, one active shipment per order, server-generated shipment identity, and post-commit carrier registration intent. Clients cannot provide shipment identity, carrier registration evidence, or tracking truth.
- Shipment ownership: reads and mutations are tenant-scoped; webhook tenant resolution ignores ambient tenant headers and resolves against current SQL shipment/carrier/tracking evidence.
- State machine: shipment transitions are forward-only, terminal states cannot resurrect, outbound progression requires order financial authority, and delivered/order-facing transitions synchronize through fulfillment authority.
- Carrier authority: carrier-managed shipment registration uses durable `CarrierRequestId`, external registration state, unique carrier request/reference indexes, adapter registry validation, and reconciliation-required states when external success cannot be safely persisted.
- Tracking authority: carrier-managed tracking now comes only from carrier registration, tracking sync, or verified webhooks. Admin/system DTO tracking mutation is blocked for carrier-managed shipments.
- Webhook authority: carrier webhooks require supported carrier resolution, tenant/shipment resolution, signature validation, payload-hash event idempotency, carrier/shipment matching, duplicate collapse, and current SQL status progression checks.
- Delivery authority: delivery transitions synchronize with fulfillment lifecycle authority and publish shipment/order events atomically with SQL mutation. Customers cannot mark delivered.
- COD authority: shipment COD collection events are evidence only. Payment truth is created through `PaymentLifecycleService`, not by shipment services or carrier payloads.
- Order integration: order tracking/carrier/delivery snapshots and storefront projections recompose from current SQL shipment/order lineage through `OrderFulfillmentLineageAuthority`.
- Fulfillment integration: shipment progress that maps to packed/out-for-delivery/delivered calls `IOrderFulfillmentService.SynchronizeFromShipmentAsync(...)`; failed synchronization aborts shipment mutation.
- Tenant isolation: admin, storefront, webhook, carrier sync, and projection paths reapply tenant/shipment/order predicates or fail closed on ambiguous tracking matches.
- Concurrency: serializable mutations, SQL uniqueness, durable carrier request identity, webhook idempotency logs, and replay-safe event keys prevent duplicate shipments, duplicate carrier registration, duplicate delivery, and stale webhook overwrite.
- Historical consistency: shipment lifecycle history remains append/event backed; terminal and stale state changes collapse to no-op, rejection, or reconciliation-required behavior.
- Adversarial review: tracking spoofing, carrier spoofing, webhook spoofing, delivery spoofing, shipment replay, tenant guessing, DTO tampering, carrier mismatch, tracking collision ambiguity, fulfillment bypass, and duplicate carrier registration are blocked or routed to explicit reconciliation.

## Runtime Hardening Performed

- Blocked admin tracking and carrier-code mutation for carrier-managed shipments in `AdminShipmentService`.
- Blocked generic shipment-status DTO tracking injection for carrier-managed shipments in `ShipmentService`.
- Added conflict responses for carrier tracking authority violations in shipment controllers.

## Files Modified

- `PointOfSale.API/Services/Admin/AdminShipmentService.cs`
- `PointOfSale.API/Services/Admin/IAdminShipmentService.cs`
- `PointOfSale.API/Controllers/AdminShipmentsController.cs`
- `PointOfSale.API/Services/Shipping/ShipmentService.cs`
- `PointOfSale.API/Services/Shipping/IShipmentService.cs`
- `PointOfSale.API/Controllers/ShipmentController.cs`
- `PointOfSale.Tests/Architecture/StorefrontShippingAuthorityCertificationTests.cs`
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Answers

- Shipping authority is owned by tenant-owned PostgreSQL shipment/shipping/carrier/webhook state plus carrier execution evidence, with fulfillment and payment handoffs delegated to their certified lifecycle services.
- Shipments cannot be forged on inspected paths because creation is privileged, order-bound, tenant-bound, serializable, and SQL-unique.
- Tracking cannot be spoofed for carrier-managed shipments; DTO tracking rewrites now fail closed.
- Carrier callbacks cannot be spoofed on inspected paths because supported-carrier, tenant resolution, signature validation, idempotency, and current SQL progression are required.
- Delivery cannot be forged by customers or stale payloads; delivery must pass shipment state rules and fulfillment synchronization.
- COD collection cannot bypass payment authority; shipping does not set payment truth.
- Shipment states cannot regress on inspected paths.
- Shipment truth cannot diverge from order truth on inspected paths without recovery detecting and repairing through SQL lineage.
- Tenant leakage is blocked by tenant-scoped SQL predicates and webhook ambiguity rejection.
- Replay attacks cannot duplicate deliveries or carrier registrations on inspected paths.
- Runtime code changes were required.
- Storefront Shipping Authority is production-certifiable for the inspected runtime scope.

# PASS SF-SHP4 Carrier Integration Authority Certification

Date: 2026-07-03

## Scope

Reviewed only the Carrier Integration runtime: carrier adapters, carrier registry/resolution, carrier configuration, carrier authentication/credentials, shipment creation handoff, carrier responses, label admission, tracking-number admission, carrier tracking sync, shipping webhooks/callbacks, provider mapping, cache/projection boundaries, and direct carrier APIs.

Excluded shipping fee calculation, shipping pricing, shipment fulfillment execution, warehouse execution, inventory reservation, split shipment execution, tracking lifecycle beyond carrier admission, returns, audit, and recovery internals except where they directly guard carrier replay/admission.

## Implemented Providers

- `manual` via `ManualCarrierDefinition`
- `bosta` via `BostaAdapter`
- `aramex` via `AramexAdapter`
- `mylerz` via `MylerzAdapter`
- `dhl` via `DhlAdapter`

No implemented ShipBlu, FedEx, UPS, or custom provider adapter was found.

## Authority Findings

Carrier Integration remains backend-owned. Carrier begins when backend code resolves an already selected order shipping method and tenant carrier configuration through `ShippingCarrierRegistry`. Authority begins only after trusted tenant context, SQL order/shipping-method lineage, tenant carrier configuration, backend credentials, stable carrier request idempotency, adapter execution, carrier response admission, lifecycle policy, and SQL persistence agree.

Shipment begins when backend SQL creates or reloads the tenant/order-bound `Shipment` row. Tracking begins only after a successful carrier result passes backend admission and is persisted to SQL, or a verified carrier webhook/tracking sync resolves an existing tenant/carrier shipment. SQL persistence begins in `Shipments`, `CarrierConfigurations`, `ShippingWebhookLogs`, and order tracking snapshots. Presentation begins only after backend DTO emission and frontend rendering.

Closed runtime risk: successful adapter results were trusted too directly at the SQL persistence boundary. `CarrierShipmentService` now rejects successful carrier results missing a durable carrier shipment id, missing/malformed tracking number, malformed tracking URL, malformed label URL/data/format, malformed provider metadata JSON, or invalid initial status mapping before persisting carrier truth.

## Certification Result

Production-certifiable after carrier response DTO admission hardening. Frontend owns only shipment request candidates, presentation, retry, refresh, and rendering. Backend owns Carrier Identity, Carrier Adapter, Carrier Authentication, Carrier Credentials, Carrier Session, Carrier Request, Carrier Response, Shipment Request, Shipment Identity, Carrier Shipment Id, Tracking Number, Tracking URL, Label, Label URL, Label PDF/data, Shipment Status Mapping, Webhook Validation, Callback Validation, Provider Mapping, and SQL Truth.

React, React Query, Zustand, LocalStorage, Redis, Mongo projections, stale DTO replay, duplicate carrier create retries, duplicate labels, duplicate callbacks, duplicate webhooks, Checkout, Orders, Payments, Inventory, Warehouse, Affiliate, CommerceHub, and Marketplace cannot fabricate, preserve, recover, overwrite, or distribute accepted Carrier Integration Authority.

Regression freeze: `PointOfSale.Tests/Architecture/CarrierIntegrationAuthorityCertificationTests.cs`.

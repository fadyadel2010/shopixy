# PASS M7-F Shipment Tracking Authority Certification Findings

Shipment Tracking Authority is production-certifiable for the inspected runtime scope. Backend owns Tracking Authority: tracking identity, source, event admission, normalized timeline state, replay handling, recovery, governance, and shipment-lifecycle synchronization. The frontend only renders backend-issued shipment and order timeline evidence.

- Tracking identity: `Shipment.Id`, tenant/order lineage, carrier code, tracking number, AWB, external reference, and carrier request ID are persisted backend fields. Carrier registration applies provider results to an existing shipment; client tracking values cannot create a shipment or carrier-managed tracking identity.
- Events and timeline: the runtime has no independent tracking-event or timeline write model. Carrier sync and verified webhooks are evidence inputs that update the existing SQL shipment only after canonical lifecycle progression checks. UI timelines are display projections, not state writers.
- Carrier and replay: webhook processing requires an existing unambiguous `{tenant, carrier, tracking}` shipment, enabled carrier configuration, provider-trust validation, and durable event/payload-hash idempotency. Tampered, duplicate, concurrently processing, malformed, missing, ambiguous, manual-carrier, and lifecycle-regressing callbacks cannot change tracking truth.
- Recovery and projections: recovery reconciles existing current-SQL shipment authority through guarded services and does not directly assign shipment status. Shipment/order projections, dashboards, exports, and frontend timelines are read-only derivatives; none can restore tracking truth.
- Cache and frontend: tracking hooks retain component-local response candidates with request-sequence fences only. The reviewed tracking components use neither local/session storage nor a persisted React Query cache, and expose no tracking mutation route. Refresh, reconnect, browser history, BFCache, and stale responses can trigger a backend reread only.
- Tenant and failures: sync queries use `{TenantId, ShipmentId}`; webhooks resolve tenant server-side and reject no-match/ambiguous targets. 401/403/404/409/422-style failures, forged IDs, forged callbacks, malformed DTOs, timeline tampering, and cross-tenant references fail closed and grant no tracking authority.

Regression freeze: `PointOfSale.Tests/Architecture/ShipmentTrackingAuthorityCertificationTests.cs`.

## Explicit certification answers

- Who owns Tracking Authority? Backend SQL shipment authority plus server-side carrier trust, lifecycle, tenant, and fulfillment guards.
- Can frontend fabricate, emerge, replay, or restore tracking authority? No.
- Can tracking events, carrier callbacks, or timelines become authority? No; verified carrier evidence is only a backend-governed input to an existing shipment.
- Can tracking override shipment lifecycle, or shipment override tracking truth? No; canonical backend lifecycle policy governs accepted changes, while shipment/order synchronization is downstream from the guarded shipment write.
- Can recovery, replay, projections, or cache fabricate or restore tracking truth? No; they re-enter current backend authority or remain read-only derivatives.
- Can tenant leakage occur? Not on the inspected runtime paths; tenant-scoped queries and ambiguous webhook rejection hold isolation.
- Were runtime changes required? No. Existing runtime controls satisfied the authority model; this pass adds regression and architecture certification freezes.
- Is Shipment Tracking Authority production-certifiable? Yes, for the inspected scope.

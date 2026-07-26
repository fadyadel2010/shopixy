# ARCH-6 CommerceHub Final Production Architecture Certification

Date: 2026-06-25  
Status: PASS - enterprise architecture complete, production architecture complete, production deployable  
Runtime hardening required: No

## Executive Summary

CommerceHub is production-certifiable as one coherent architecture. Runtime inspection confirms every inspected business truth has exactly one owner, every authority flow is evidence-forward and acyclic, infrastructure remains derivative, and frontend, events, projections, cache, replay, recovery, and rebuild paths cannot create, restore, widen, or mutate business authority.

The final runtime graph is:

POS Product -> Publication -> Catalog -> Search -> Cart -> Shipping Method -> Shipping Cost -> Checkout -> Split Orders -> Marketplace Orders -> Payment -> Shipment -> Tracking -> Refund -> Return -> Vendor Earnings -> Settlement -> Payout Eligibility -> Payout Execution -> Reconciliation.

No unresolved architectural blocker, authority blocker, tenant blocker, lifecycle blocker, or currency blocker remains in the inspected runtime.

## Complete Architecture Findings

Product authority remains POS SQL product, variant, stock, media, price, inventory, and tenant lineage. Product projections and CommerceHub listing/search documents are derivative.

Publication authority is the backend CommerceHub publication boundary. `PublishToCommerceHubHandler` revalidates current tenant, vendor governance, category lineage, shipping readiness, currency policy, product visibility, variant lineage, attributes, and price authority before writing publication intent.

Catalog authority is derivative discovery only. Catalog reads consume current SQL listing/product/variant/category/vendor evidence and cannot mutate product or publication truth.

Search authority is derivative discovery only. `CommerceHubSearchSqlAuthorityOverlay` requires current SQL listing, tenant, product, variant, price, stock, media, category, currency, and tombstone authority before an Elasticsearch document can remain visible.

Cart authority is customer intent only. `CommerceHubCartService` validates current public lineage and pricing evidence; it cannot mutate product, publication, shipping, checkout, order, or payment authority.

Shipping Method authority is backend tenant-owned SQL plus shipping services. Storefront cache and CommerceHub request selections are candidates only; final checkout bypasses candidate cache and recomputes from current SQL.

Shipping Cost authority is backend calculation over SQL shipping configuration, product subtotal, weight, tenant/store scope, and deterministic method selection. Posted totals, carrier estimates, snapshots, cache, and frontend calculations are stale-detection evidence only.

Checkout authority is the cart-to-order transition. `CommerceHubCheckoutService` uses order, shipping-assignment, financial-workflow, and payment-lifecycle mutation scopes, locks, serializable persistence, request hashes, current SQL revalidation, and idempotency replay.

Split Orders and Marketplace Orders own immutable order, vendor, item, shipping, payment, financial, customer, and lifecycle evidence after checkout. Downstream domains consume this evidence without taking upstream ownership.

Payment authority is `CommerceHubPaymentService` plus SQL payment rows, provider verification, amount/currency checks, payment locks, unresolved-webhook suspense, lifecycle guards, and compensation/reconciliation paths.

Shipment authority is backend SQL shipment/order lineage, trusted tenant context, actor authorization, carrier trust, idempotency, and `ShipmentLifecycleTransitionPolicy`.

Tracking authority is existing backend shipment plus verified carrier/tracking evidence after tenant, carrier, idempotency, and lifecycle validation.

Refund authority is backend refund SQL, payment/order lineage, current order-item evidence, financial reversal workflow, idempotency, locks, and refund lifecycle policy.

Return authority is backend return/refund/order lineage, tenant/actor authorization, return workflow, idempotency, and current mutation re-entry.

Vendor Earnings authority is backend earnings SQL, ledger lineage, order/payment/refund/return evidence, lifecycle workflow, payout reservation, tenant/actor authorization, EGP guards, locks, and idempotency.

Settlement authority is CommerceHub-owned EGP-only ledger-backed settlement truth. Settlement visibility, calculation, identity, projections, exports, and reports must prove EGP and current ledger evidence.

Payout authority is CommerceHub-owned payout identity, membership, eligibility, reservation, execution, provider evidence recognition, EGP currency, idempotency, locks, and reconciliation-required handling.

Reconciliation authority owns mismatch detection, anomaly recording, and recovery orchestration only. It can re-enter owning payment/order/payout boundaries, but cannot become payment, order, settlement, or payout authority itself.

Super Admin authority is platform governance, diagnostics, and backend-authorized operations only. Super Admin presentation, selected rows, monitoring screens, repair controls, and exports are not business truth owners.

Storefront and POS Integration are backend-bound presentation and intent surfaces. Browser storage, route state, React Query, DTOs, mutation responses, BFCache, realtime, and cross-tab events validate, withhold, clear, invalidate, refetch, or submit intent only.

Read Models, Events, Recovery, Replay, Projection, Cache, Search, Hosted Services, and Background Workers restore availability or derivative state only. They never own business authority.

## Complete Authority Findings

Every business truth has exactly one owner:

- Product: POS product SQL and product services.
- Publication: CommerceHub listing/publication SQL and publication handler.
- Catalog/Search: derivative read/discovery state over current SQL authority.
- Cart: customer cart intent service.
- Shipping Method/Cost: backend SQL shipping configuration and calculation service.
- Checkout: checkout service cart-to-order transition.
- Split Orders/Marketplace Orders: CommerceHub order SQL and order lifecycle boundary.
- Payment: CommerceHub payment SQL and payment service.
- Shipment/Tracking: shipment SQL, shipment services, lifecycle policy, and verified carrier trust.
- Refund: refund SQL and refund service.
- Return: return/refund/order SQL and return workflow.
- Vendor Earnings: earnings SQL and financial workflow.
- Settlement: EGP ledger-backed settlement services.
- Payout: payout SQL, payout service, execution workflow.
- Reconciliation: anomaly/recovery orchestration records only.

Writers are the owning backend services under exact mutation scopes or domain policies. Readers are downstream services, read APIs, projections, dashboards, storefront/POS/Super Admin presentation, and workers. Consumers must never mutate upstream truth; they may re-enter the owning backend service or fail closed. No dual authority, hidden writer, circular ownership, or authority leakage was found in the inspected runtime.

## Complete Domain Findings

Cross-domain authority flows one way. Product feeds Publication; Publication feeds Catalog/Search; Search feeds discovery; Cart captures customer intent; Shipping resolves backend method/cost; Checkout persists immutable order evidence; Orders feed Payment, Shipment, Refund, Return, Earnings, Settlement, Payout, and Reconciliation as evidence.

No backward authority, loop, fork, shortcut, hidden mutation, or orchestration authority was found. Merges are evidence merges only, such as checkout aggregate totals, split-order aggregation, payout membership, settlement aggregation, and reconciliation anomaly grouping. Each merge is owned by its local backend domain and cannot rewrite upstream truth.

## Infrastructure Findings

SQL is the only durable business truth store. Outbox owns sequencing and delivery state only. RabbitMQ owns transport only. Mongo owns read documents only. Redis owns cache envelopes only. Elasticsearch owns search documents only. Frontend owns presentation only. Hosted services and background workers own scheduling/orchestration only.

Infrastructure cannot own business truth, recreate business truth, or mutate business truth except by re-entering the exact owning SQL/service boundary under guards, locks, idempotency, and tenant checks.

## Event Findings

Events are evidence and delivery records. Outbox publication preserves aggregate order and delivery state; consumers stage projection work and use idempotency fences. Events cannot transfer, create, restore, reorder, repair, or mutate business authority.

## Projection Findings

Projections are derivative read models. Mongo projection writes are source-version/checksum/freshness guarded. Projection metadata is mutation-scope guarded. Projection repair and reconciliation rebuild from SQL and fail closed when projection state cannot prove SQL authority.

## Recovery Findings

Recovery, replay, retry, repair, rebuild, startup, cache warmup, search reindex, payment recovery, shipment/tracking recovery, refund recovery, payout recovery, and financial reconciliation re-enter current SQL/service authority, rebuild derivative state, no-op, quarantine, tombstone, invalidate, mark reconciliation-required, or fail closed. Recovery never owns authority.

## Tenant Findings

POS, Storefront, CommerceHub, Vendor, Customer, Carrier, and Super Admin scopes remain separated by trusted backend tenant context, authenticated actor claims, SQL lineage, endpoint authorization, role/capability checks, row-level tenant predicates, cache keys, and frontend scoped purge/revalidation.

Authority cannot cross tenants, widen scope, leak identity, leak lifecycle, or leak currency in the inspected runtime.

## Failure Findings

400, 401, 403, 404, 409, 422, 500, timeout, retry, replay, crash, restart, and recovery paths do not create business truth. Failures reject, withhold, retry delivery, reload SQL, invalidate stale derivatives, record anomalies, mark reconciliation-required, or preserve existing SQL state.

Failures cannot recreate authority, widen tenant scope, advance lifecycle, or change currency truth.

## Operational Findings

Concurrency, idempotency, outbox, dead letter, retry, recovery, compensation, projection, cache, and rebuild paths restore availability or converge derivative state. They do not change authority ownership.

Locks, serializable transactions, unique constraints, request hashes, provider idempotency keys, processed-event fences, projection freshness, tombstones, cache invalidation, and SQL re-entry prevent operational mechanics from becoming authority.

## Remaining Risks

- Architecture Blocker: None.
- Operational Risk: Deployed chaos evidence remains required for broker outages, retry storms, duplicate/late/missing events, provider timeout-after-external-success, lock infrastructure failure, concurrent hosted workers, projection rebuild races, Mongo/Redis/Elasticsearch tampering, cache poisoning, BFCache restoration, multi-tab tenant or actor replacement, carrier callback tampering, payout provider ambiguity, and recovery after partial external success.
- Infrastructure Risk: External dependencies can be unavailable or ambiguous, including Redis, Mongo, Elasticsearch, RabbitMQ, payment providers, payout providers, carriers, and distributed locks. Runtime must continue to fail closed, retry, quarantine, invalidate, or mark reconciliation-required.
- Monitoring Gap: Production monitoring must continue to cover outbox lag, projection lag, dead letters, cache corruption, reconciliation anomalies, provider suspense, payout reconciliation-required rows, shipment recovery failures, and cross-tenant/foreign-currency rejection signals.
- Future Enhancement: Future split-fulfillment shipment models must add explicit CommerceHub sub-order shipment lineage instead of reusing generic order shipment authority. Future return/logistics/import/repair surfaces must preserve backend re-entry and add focused certification before rollout.

No invented runtime risk is added; these are the residual operational/monitoring/future risks already carried by prior certifications.

## Final Certification Questions

Is Product production-certifiable? Yes.  
Is Publication production-certifiable? Yes.  
Is Catalog production-certifiable? Yes.  
Is Search production-certifiable? Yes.  
Is Cart production-certifiable? Yes.  
Is Shipping production-certifiable? Yes.  
Is Checkout production-certifiable? Yes.  
Is Orders production-certifiable? Yes.  
Is Payment production-certifiable? Yes.  
Is Shipment production-certifiable? Yes.  
Is Tracking production-certifiable? Yes.  
Is Refund production-certifiable? Yes.  
Is Return production-certifiable? Yes.  
Is Vendor Earnings production-certifiable? Yes.  
Is Settlement production-certifiable? Yes.  
Is Payout production-certifiable? Yes.  
Is Reconciliation production-certifiable? Yes.  
Is Event Architecture production-certifiable? Yes.  
Is Recovery Architecture production-certifiable? Yes.  
Is CommerceHub production-certifiable as one coherent architecture? Yes.  
Are there any unresolved architectural blockers? No.  
Are there any unresolved authority blockers? No.  
Are there any unresolved tenant blockers? No.  
Are there any unresolved lifecycle blockers? No.  
Are there any unresolved currency blockers? No.  
Were runtime changes required? No.  
Is CommerceHub Enterprise Architecture Certification PASSED? Yes.

## Runtime Hardening Performed

None. Final runtime inspection found no new architecture or authority gap requiring code hardening.

## Exact Runtime Files Modified

None.

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/CommerceHubFinalProductionArchitectureCertificationTests.cs`

## Architecture Updates

- Added this ARCH-6 findings document.
- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

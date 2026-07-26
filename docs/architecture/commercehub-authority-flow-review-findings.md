# ARCH-3 CommerceHub Authority Flow Review

Date: 2026-06-25

## Scope

Runtime-first authority-flow review from POS Product through Reconciliation. This review follows ARCH-1 Business Flow Review, ARCH-2 Domain Boundary Review, and the focused CommerceHub authority certifications, but reviews the stricter question: whether business truth flows through CommerceHub in exactly one authoritative direction without forking, looping, duplicating, merging, transferring ownership, or being recreated by events, replay, recovery, projections, cache, lifecycle, orchestration, concurrency, or tenant boundaries.

Verdict: CommerceHub Authority Flow is production-certifiable for the inspected runtime. No runtime hardening was required.

## Authority Graph Findings

Runtime authority graph:

Product -> Publication -> Catalog -> Search -> Cart -> Shipping Method -> Shipping Cost -> Checkout -> Split Orders -> Marketplace Orders -> Payment -> Shipment -> Tracking -> Refund -> Return -> Vendor Earnings -> Settlement -> Payout Eligibility -> Payout Execution -> Reconciliation.

The reviewed runtime proves this graph is evidence-forward and authority-local. Each edge passes identity, lineage, state, scope, money, or lifecycle evidence to the next owner. It does not transfer upstream ownership. Downstream domains consume current SQL/service authority and then write only their own truth through the named owning service, lifecycle policy, lock, idempotency record, tenant predicate, and `CommerceHubBoundaryMutationScope` where applicable.

No inspected edge forks authority, creates a second owner, merges two owners into one truth, moves authority backward, or permits downstream evidence to become upstream authority.

## Product Flow Findings

Owner: POS Product SQL and product/variant/stock/price authority.

Leaves: product identity, variant identity, category, media, price, stock, and online visibility evidence. Enters Publication as prerequisites.

Finding: ownership is not transferred. Publication, Catalog, Search, Cart, Checkout, Shipment, Refund, Return, Earnings, Settlement, Payout, Reconciliation, projections, cache, replay, and recovery cannot mutate Product truth. Product cannot receive authority from Catalog; Catalog can only expose derivative visibility.

## Publication Flow Findings

Owner: CommerceHub publication/listing authority through `PublishToCommerceHubHandler`, update/unpublish handlers, listing SQL, and governance/category/shipping/currency/product-lineage checks.

Leaves: listing intent and current listing lineage. Enters Catalog/Search as projection source and Cart/Checkout as visibility evidence.

Finding: Publication owns listing intent only. It does not own Product truth. Catalog/Search cannot push authority back into Publication, and projections can only copy or tombstone derivative listing evidence.

## Catalog Flow Findings

Owner: derivative catalog read model from SQL listing/product authority.

Leaves: catalog visibility evidence. Enters Search and product-detail/customer surfaces.

Finding: Catalog is a consumer, not upstream owner. It cannot mutate Product, Publication, Cart, Checkout, or Orders. Catalog cannot receive authority from Search; Search provides query/discovery candidates only.

## Search Flow Findings

Owner: derivative discovery/ranking/index authority in Elasticsearch plus SQL authority overlay.

Leaves: candidate listing/product identities. Enters Cart as customer selection candidates.

Finding: Search cannot create Cart, Product, Publication, price, stock, shipping, or checkout authority. Cart and Checkout re-enter SQL authority; Search cannot receive authority from Cart and cannot push authority upstream to Catalog.

## Cart Flow Findings

Owner: `CommerceHubCartService`, customer/cart SQL, backend-validated line intent, and customer ownership predicates.

Leaves: customer intent, line quantity intent, cart version, and backend-validated candidate snapshot. Enters Shipping Resolution and Checkout.

Finding: Cart owns intent only. It cannot mutate Product, Publication, Search, Shipping, Checkout, Orders, Payment, or finance truth. Checkout consumes Cart evidence but does not transfer Checkout ownership to Cart.

## Shipping Flow Findings

Owner: SQL `ShippingMethods`, `CommerceHubShippingService`, `ShippingCalculationService`, and split-order shipping assignment boundary.

Leaves: backend-selected method and backend-calculated per-store cost evidence. Enters Checkout and Split Orders.

Finding: Shipping owns method/cost/assignment truth only. Request IDs, prices, cache rows, projections, carrier quotes, and browser state are candidates or evidence. Shipping cannot mutate Cart, Product, Payment, Refund, Earnings, Settlement, or Payout.

## Checkout Flow Findings

Owner: `CommerceHubCheckoutService` and the cart-to-order transition under serializable transaction, customer/idempotency/cart locks, SQL revalidation, request-hash-bound idempotency, order lifecycle scope, shipping assignment scope, financial workflow scope, and payment lifecycle scope.

Leaves: marketplace order identity, split-order identity, immutable item/financial/shipping snapshots, reservation evidence, and order-created events. Enters Split Orders, Marketplace Orders, and Payment.

Finding: Checkout owns the transition only. It does not transfer upstream Product/Cart ownership or downstream Payment/Shipment ownership. Checkout cannot receive authority from Shipment, and Shipment cannot push authority back into Checkout.

## Split-Order Flow Findings

Owner: checkout-created `CommerceHubSubOrders`, sub-order items, immutable vendor/store/item/financial/shipping evidence, and guarded lifecycle/financial/shipping mutation scopes.

Leaves: vendor partition, item snapshot, financial snapshot, shipping assignment, and order lineage evidence. Enters Marketplace Orders, Payment, Shipment, Refund, Return, Earnings, Settlement, and Payout eligibility.

Finding: split-order evidence can fan out as evidence to many consumers, but authority does not fork. Each downstream domain writes only its own truth and cannot rebind sub-order vendor, item, shipping, or financial snapshot ownership.

## Order Flow Findings

Owner: `CommerceHubMarketplaceOrders`, order lifecycle services, payment/fulfillment/refund guarded lifecycle synchronization, and SQL order lineage.

Leaves: order identity, customer ownership, aggregate lifecycle, totals, payment status evidence, and fulfillment evidence. Enters Payment, Shipment, Refund, Return, Earnings, Settlement, Payout, and Reconciliation.

Finding: Orders remain SQL-owned. Shipment and Tracking may synchronize fulfillment only through certified order lifecycle authority; they cannot own order identity or financial truth.

## Payment Flow Findings

Owner: `CommerceHubPaymentService`, payment lifecycle authority, provider recognition, locks, provider uniqueness, unresolved webhook suspense, and EGP amount/currency checks.

Leaves: payment identity, provider session evidence, paid/failed/refunded lifecycle evidence, and verified provider outcome evidence. Enters Shipment readiness, Refund, Vendor Earnings, Settlement, and Reconciliation.

Finding: Payment requires existing order lineage. Provider callbacks, duplicate webhooks, retries, and compensation evidence cannot create a second payment truth. Refund can update refund-specific payment lifecycle fields through guarded boundaries only; it cannot rewrite payment financial identity.

## Shipment Flow Findings

Owner: `ShipmentService`, `AdminShipmentService`, `CarrierShipmentService`, shipment SQL, trusted tenant context, role authorization, shipping-method/order lineage, carrier trust, idempotency, and `ShipmentLifecycleTransitionPolicy`.

Leaves: shipment identity, carrier registration, shipment lifecycle, and logistics evidence. Enters Tracking and Return logistics.

Finding: Shipment consumes order/shipping/payment readiness evidence but does not own Orders, Payment, Refund, Earnings, Settlement, or Payout. Shipment cannot receive authority from Tracking; Tracking must resolve an existing shipment.

## Tracking Flow Findings

Owner: existing tenant-scoped shipment plus verified carrier/tracking pipeline and idempotency.

Leaves: normalized tracking event/timeline evidence. Enters Shipment presentation and Return logistics evidence.

Finding: Tracking cannot mint shipment identity, rewrite shipment ownership, or mutate upstream checkout/order/payment truth. Carrier payloads are evidence only until bound to exactly one trusted current shipment.

## Refund Flow Findings

Owner: refund services, refund SQL, payment/order/item/customer/vendor lineage, refund lifecycle policy, execution services, locks, idempotency, payout effects, and ledger effects.

Leaves: refund identity, refund lifecycle, refund amount/currency, execution evidence, and reversal evidence. Enters Return, Vendor Earnings reversal, Settlement, Payout readiness, and Reconciliation.

Finding: Refund owns refund truth only. It cannot recreate Payment authority, mutate Payment financial identity, own Return logistics, or receive authority from Settlement.

## Return Flow Findings

Owner: `ReturnManagementService`, return SQL/logistics state, refund/order/item/customer/vendor scope, action grants, and lifecycle policy.

Leaves: return identity, logistics lifecycle, receipt/failure/evidence, and backend-derived financial impact evidence. Enters Refund display, Shipment/logistics context, Earnings/Settlement reversal evidence, and Reconciliation.

Finding: Return is refund/order-bound. It cannot mint Refund, Payment, Order, Settlement, or Payout truth.

## Earnings Flow Findings

Owner: `CommerceHubFinancialWorkflowService`, `CommerceHubEarningsService`, SQL earnings, ledger lineage, vendor/order/payment/refund/return lineage, locks, idempotency, payout reservation fields, and EGP guards.

Leaves: earning identity, earning lifecycle, available/in-payout/paid/reversed state, vendor payable evidence, and settlement/payout eligibility evidence.

Finding: Earnings are created from paid or delivered sub-order evidence through financial workflow. Settlement consumes earnings/ledger evidence but cannot rewrite earning identity. Payout can reserve/release payout fields only through payout authority.

## Settlement Flow Findings

Owner: settlement/ledger financial workflow, `CommerceHubVendorSettlements`, financial transaction rows, append-only EGP settlement records, read/report services, and EGP guards.

Leaves: settlement identity, ledger-backed balances, available/payable evidence, and settlement-to-payout eligibility evidence.

Finding: Settlement owns settlement/ledger truth only. It cannot mutate Earnings identity or Payout identity. Payout consumes settlement evidence without receiving Settlement ownership.

## Payout Flow Findings

Owner: payout eligibility/readiness services, `CommerceHubPayoutService`, `SuperAdminPayoutsService`, `CommerceHubPayouts`, `CommerceHubPayoutItems`, earning locks, serializable transactions, idempotency, external transfer idempotency, provider/bank evidence, and EGP guards.

Leaves: eligibility/readiness evidence to execution, payout identity, payout item membership, execution evidence, completed/failed/reconciliation-required lifecycle, and external reference evidence. Enters Reconciliation and finance presentation.

Finding: Payout owns payout truth only. It cannot mutate Settlement truth. Reconciliation cannot push authority back into Payout except by re-entering the payout service or recording anomaly/reconciliation-required evidence.

## Reconciliation Flow Findings

Owner: reconciliation jobs/read services, unresolved webhook rows, anomaly reports, recovery orchestration, compensation orchestration, and append-only reconciliation evidence.

Leaves: anomaly, mismatch, recovery-needed, compensation-needed, and diagnostic evidence to operations and owning services.

Finding: Reconciliation is terminal diagnostic/recovery orchestration. It cannot recreate Product, Cart, Checkout, Order, Payment, Shipment, Refund, Return, Earnings, Settlement, or Payout authority. It may re-enter owning authority services or mark anomalies; it cannot manufacture healthy truth.

## Event Authority Findings

SQL is authority. Outbox, RabbitMQ, consumers, and event messages carry sequencing evidence and projection/retry/recovery triggers only.

Events do not transfer ownership. Event replay cannot move authority backward or create a business transition. Consumers either write derivative projections under projection scope or re-enter the owning SQL/service authority.

## Projection Authority Findings

Mongo and Elasticsearch projections are derivative read models. Projection consumers use freshness fences, SQL recomposition, checksums, tombstones, tenant document identity, and `BeginProjection` metadata scope.

Projections cannot own authority, restore deleted authority, repair upstream truth, or become a source for checkout/payment/payout mutations.

## Cache Authority Findings

Redis, React Query, browser state, local/session storage, route/history/BFCache, and cached DTOs are presentation or candidate evidence only. Tenant/actor scoped cache admission, current-session revalidation, cache purge on authority loss, and backend refetch convergence prevent cache restoration.

Cache cannot own authority, transfer tenant scope, recreate lifecycle state, or preserve stale action/eligibility grants.

## Replay Authority Findings

Checkout, payout, settlement, vendor-submission, projection, payment, and event replay paths are idempotent replay or projection rebuild only. Runtime request hashes, original replay matching, idempotency keys, external idempotency keys, unique provider references, SQL lineage, and stale replay blocking prevent recreation.

Replay cannot create authority. It can return an already persisted matching result, no-op, tombstone, wait, block, or re-enter owning authority.

## Recovery Authority Findings

Hosted recovery services and jobs recover by re-entering owning services and current SQL lineage. Payment recovery, refund recovery, payout reconciliation, projection repair, order projection repair, finance reconciliation, and inventory consistency workers cannot skip lifecycle gates.

Recovery cannot create new upstream truth, transfer ownership, or bypass authority. Partial external success becomes suspense, compensation, or reconciliation-required evidence until the owner accepts it.

## Lifecycle Authority Findings

Lifecycle state is owned by the current domain service/policy: checkout order creation, order lifecycle, payment lifecycle, shipment lifecycle, refund lifecycle, return lifecycle, earning lifecycle, settlement records, and payout lifecycle. Lifecycle transitions do not transfer ownership to consumers.

Lifecycle cannot bypass authority, recreate authority, or move backward except where the owning policy explicitly records a reversal/failure/reconciliation-required state as its own truth.

## Concurrency Authority Findings

Parallel checkout is constrained by customer/idempotency/cart locks, request-hash-bound idempotency, cart version checks, serializable transactions, and unique idempotency records. Duplicate payment is constrained by payment locks, provider identity uniqueness, unresolved-webhook fences, duplicate webhook handling, and compensation evidence. Duplicate shipment, refund, and payout paths are constrained by existing-shipment/order lineage, lifecycle policy, idempotency, row locks, unique indexes, and serializable transactions.

Concurrency can collapse, block, retry, or mark reconciliation-required. It cannot duplicate authority, fork ownership, merge incompatible truths, or transfer ownership.

## Tenant Authority Findings

POS, Vendor, Storefront, CommerceHub customer, Carrier, and Super Admin boundaries remain separated by trusted backend tenant context, authenticated actor/session authority, endpoint authorization, row-level SQL lineage, carrier trust, and DTO/cache admission.

Authority cannot cross tenants, stores, or vendors through client-selected IDs, headers, routes, cache, projections, events, provider payloads, or Super Admin UI state. Super Admin governs through explicit platform authority and domain services; Super Admin does not own domain business truth.

## Cross-Domain Authority Findings

No domain owns another domain's truth. Shared EF entities, DTOs, events, orchestration services, dashboards, and read models are not ownership. Runtime guards prevent Product/Catalog/Search/Cart/Checkout/Shipment/Tracking/Refund/Return/Earnings/Settlement/Payout/Reconciliation from writing each other's guarded business truth outside exact authority scopes.

Authority can be referenced by multiple consumers as evidence, but it cannot exist simultaneously as owned truth in multiple domains.

## Failure Authority Findings

400 and 422 reject malformed/stale intent. 401 and 403 withhold/purge/revalidate. 404 hides missing or foreign lineage. 409 handles stale cart/price/shipping, lock contention, lifecycle conflicts, duplicate provider evidence, idempotency mismatch, and concurrency conflicts. 500, timeout, and partial failure paths use idempotency, suspense, compensation, recovery re-entry, or reconciliation-required state.

Failure cannot transfer authority. Retry cannot duplicate authority. Recovery cannot bypass authority.

## Architectural Risks

- Residual operational proof remains required for live chaos: provider delays, duplicate webhooks, lock infrastructure failures, checkout/payment/payout concurrency, timeout-after-external-success, Redis/Mongo/Elasticsearch tampering, projection rebuild races, outbox retry storms, BFCache, multi-tab actor/tenant replacement, carrier callback tampering, and payout provider ambiguity.
- Future split-fulfillment must add explicit sub-order shipment authority rather than letting generic shipment infer split-order ownership.
- Future repair/import/migration/bulk SQL tooling must retain exact owner re-entry and never write upstream truth from projection/cache/export/provider evidence.
- Future broad orchestration helpers must remain sequencing-only and must not acquire cross-domain mutation authority.

## Final Authority Questions

- Who owns CommerceHub Authority Flow? The backend CommerceHub authority graph owns flow composition; each domain owner owns only its own truth, with POS Product SQL as upstream product owner and CommerceHub backend services owning downstream transitions.
- Is authority transferred or only referenced? Only referenced. Evidence moves forward; ownership stays with the original domain.
- Can authority duplicate? No.
- Can authority fork? No.
- Can authority merge? No inspected path merges incompatible owners into one truth.
- Can authority move backwards? No.
- Can downstream domains become upstream authority? No.
- Can projections own authority? No.
- Can events own authority? No.
- Can replay recreate authority? No.
- Can recovery recreate authority? No.
- Can cache own authority? No.
- Can lifecycle transfer authority? No.
- Can orchestration own authority? No.
- Can concurrency duplicate authority? No.
- Can tenant boundaries transfer authority? No.
- Were runtime changes required? No.
- Is CommerceHub Authority Flow production-certifiable? Yes, subject to the ARCH-3 freeze and continued operational chaos evidence.

## Runtime Hardening Performed

None. Runtime implementation already enforces one-way, evidence-forward authority flow through SQL/service owners, exact mutation scopes, immutable records, lifecycle policies, locks, idempotency, tenant lineage, projection fences, cache admission, replay matching, and recovery re-entry.

## Exact Runtime Files Modified

None.

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/CommerceHubAuthorityFlowArchitectureCertificationTests.cs`
- `PASS ARCH-3 CommerceHub Authority Flow Architecture Freeze` in `.ai-context/CODEX_RULES.md`

## Architecture Updates

- Added this findings document.
- Added ARCH-3 entries to `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, and `.ai-context/CODEX_RULES.md`.

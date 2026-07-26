# ARCH-2 CommerceHub Domain Boundary Review

Date: 2026-06-25

## Scope

Runtime-first architecture boundary review from POS Product through Reconciliation. This review follows ARCH-1 and the focused CommerceHub authority certifications, but reviews a different question: whether every domain owns exactly its own truth and whether downstream domains can mutate upstream business authority.

Verdict: CommerceHub Domain Boundary Architecture is production-certifiable for the inspected runtime. No runtime hardening was required.

## Product Boundary Findings

Owner/truth: POS Product Authority owns product identity, variant identity, SKU/barcode ownership, stock, base price, online visibility, product lifecycle, and product media lineage in PostgreSQL.

Tables: `Products`, `ProductVariants`, product media/attribute/unit/category/stock/price authority tables, and product publication lineage rows. Services: `ProductsController`, `ProductVariantsController`, product authority overlays, inventory/price authorities. Allowed callers: POS/admin product services, publication as a candidate consumer, catalog/search/cart/checkout as read/validation consumers. Consumers: Publication, Catalog, Search, Cart, Checkout, Shipment/fulfillment only through order lineage. Never mutate: Publication, Catalog, Search, Cart, Checkout, Payment, Shipment, Tracking, Refund, Return, Earnings, Settlement, Payout, Reconciliation, projections, cache, repair, rebuild, replay.

Finding: Catalog, Search, Cart, Checkout, and all downstream domains consume product SQL lineage only. They cannot mutate Product truth.

## Publication Boundary Findings

Owner/truth: Publication Authority owns merchant intent to expose a POS product as a CommerceHub listing after current tenant, vendor governance, live category, active shipping, EGP currency, attribute, product, variant, visibility, and price checks.

Tables: `CommerceHubListings`, listing publication audit/version data, category lineage tables. Services: `PublishToCommerceHubHandler`, `UpdateCommerceHubListingHandler`, `UnpublishFromCommerceHubHandler`. Allowed callers: tenant vendor/admin publication endpoints and governed Super Admin/platform controls where explicitly certified. Consumers: Catalog, Search, Cart, Checkout. Never mutate: Product SQL truth, Payment, Orders, Shipment, Refund, Return, Earnings, Settlement, Payout, Reconciliation.

Finding: Publication can create/update/unpublish listing intent only; it cannot rewrite POS product truth.

## Catalog Boundary Findings

Owner/truth: Catalog owns derivative CommerceHub listing/product read visibility, not product or listing business truth.

Tables/stores: Mongo CommerceHub/marketplace read models and catalog projection documents; source truth remains PostgreSQL `CommerceHubListings` plus product lineage. Services: `CommerceHubProjectionConsumer`, marketplace read-model projection services, SQL authority overlays. Allowed callers: transactional outbox/projection workers and SQL-first rebuild/repair paths. Consumers: Search, Storefront, Cart, Checkout. Never mutate: Product, Publication, Cart, Orders, Payment, Shipment, Refund, Return, Earnings, Settlement, Payout, Reconciliation.

Finding: Catalog projections are SQL-recomposed, freshness-fenced, and derivative. They cannot mutate Product or Publication truth.

## Search Boundary Findings

Owner/truth: Search owns derivative discovery, ranking, autocomplete, indexing, and query presentation only.

Tables/stores: Elasticsearch indexes, search projection metadata, search-filter/intelligence rows where certified as derivative. Services: `CommerceHubSearchIndexService`, `CommerceHubSearchSqlAuthorityOverlay`, reindex and drift services. Allowed callers: projection/reindex workers and public search controllers. Consumers: Product detail, Cart candidate selection, customer discovery. Never mutate: Product, Publication, Catalog, Cart, Checkout, Orders, Payment, Shipment, Refund, Return, Earnings, Settlement, Payout, Reconciliation.

Finding: Search can return candidates only. Cart and Checkout re-enter current SQL authority before pricing, quantity, shipping, or order mutation.

## Cart Boundary Findings

Owner/truth: Cart owns customer cart identity, ownership, selected line intent, quantity intent, and backend-validated cart snapshots before checkout.

Tables/stores: CommerceHub cart persistence, cart items, cart idempotency/owner rows, Redis/React caches as derivative only. Services: `CommerceHubCartService`, `CommerceHubCartController`. Allowed callers: authenticated customer/cart endpoints and checkout as consumer. Consumers: Shipping, Checkout. Never mutate: Product, Publication, Catalog, Search, Orders after checkout, Payment, Shipment, Refund, Return, Earnings, Settlement, Payout, Reconciliation.

Finding: Cart cannot mutate Product. Checkout may consume cart intent, but it does not let Cart become order or payment authority.

## Shipping Boundary Findings

Owner/truth: Shipping Method owns method identity/scope/eligibility; Shipping Cost owns backend-calculated cost; split-order shipping assignment owns immutable per-store shipping evidence.

Tables: `ShippingMethods`, carrier configuration tables, split-order shipping snapshot fields on `CommerceHubSubOrders`. Services: `ShippingMethodsController`, Storefront shipping method service, `CommerceHubShippingService`, `ShippingCalculationService`, `CommerceHubCheckoutService`. Allowed callers: POS vendor/admin config paths, Storefront candidate reads, Checkout final calculation, Shipment creation as method-bound consumer. Consumers: Checkout, Split Orders, Shipment. Never mutate: Product, Publication, Cart, Payment, Refund, Return, Earnings, Settlement, Payout, Reconciliation.

Finding: Shipping computes and assigns only shipping truth. It cannot mutate upstream product/catalog/cart truth or downstream payment/finance truth.

## Checkout Boundary Findings

Owner/truth: Checkout owns the cart-to-marketplace-order transition and final validation of product/listing/price/stock/shipping/payment-method candidates.

Tables: checkout idempotency, checkout locks, `CommerceHubMarketplaceOrders`, `CommerceHubSubOrders`, `CommerceHubSubOrderItems`, reservation rows, event/outbox rows. Services: `CommerceHubCheckoutService`, checkout session/security services. Allowed callers: authenticated customer checkout endpoints. Consumers: Orders, Payment, Shipment, Earnings workflow after payment. Never mutate: Product, Publication, Catalog, Search, settled Payment truth, Shipment truth, Refund, Return, Settlement, Payout, Reconciliation.

Finding: Checkout consumes Cart, but Cart cannot mutate Checkout and Checkout cannot mutate Product. Checkout does not mutate Shipment; it creates order/shipping evidence that Shipment later consumes.

## Split-Order Boundary Findings

Owner/truth: Split Order owns vendor/store partitioning, immutable item snapshots, immutable financial snapshots, and immutable per-store shipping assignment created at checkout.

Tables: `CommerceHubSubOrders`, `CommerceHubSubOrderItems`, shipping/financial snapshot fields. Services: `CommerceHubCheckoutService`, `CommerceHubFinancialWorkflowService` for finance-only workflow fields, guarded by `CommerceHubBoundaryMutationScope`. Allowed callers: Checkout for creation, financial workflow for earning/commission fields, order lifecycle for lifecycle fields. Consumers: Payment, Shipment, Refund, Return, Earnings, Settlement, Payout, Reconciliation. Never mutate: Product, Publication, Catalog, Search, Cart, Payment financial identity, Shipment identity, Refund identity, Payout identity.

Finding: Split-order business evidence is immutable outside its specific guards. Shipment cannot rebind split-order shipping or vendor truth.

## Order Boundary Findings

Owner/truth: Marketplace Order owns order identity, customer/order lineage, aggregate lifecycle, payment status fields, fulfillment state, and order-level totals created by checkout.

Tables: `CommerceHubMarketplaceOrders`, `CommerceHubSubOrders`, order lifecycle/event/outbox tables. Services: `CommerceHubCheckoutService`, `CommerceHubOrderManagementService`, payment/refund/fulfillment lifecycle services under order lifecycle scope. Allowed callers: checkout, payment lifecycle, refund/return/fulfillment services as bounded lifecycle consumers. Consumers: Payment, Shipment, Refund, Return, Earnings, Settlement, Payout, Reconciliation. Never mutate: Product, Publication, Catalog, Search, Cart. Downstream Shipment and Tracking may not mutate order identity or financial truth.

Finding: Shipment cannot mutate Orders except through certified fulfillment/order synchronization under order lifecycle authority; generic shipment/tracking evidence cannot rewrite order truth.

## Payment Boundary Findings

Owner/truth: Payment owns payment initiation, provider session evidence, payment lifecycle, webhook recognition, amount/currency/provider identity, and payment logs/items.

Tables: `CommerceHubPayments`, `CommerceHubPaymentItems`, `CommerceHubPaymentAttempts`, `CommerceHubPaymentLogs`, unresolved payment webhook rows, payment lifecycle/order payment status fields where guarded. Services: `CommerceHubPaymentService`, payment lifecycle authority/recovery services, provider webhook pipelines. Allowed callers: checkout/order payment endpoints, provider webhooks through verified pipelines, recovery through payment authority. Consumers: Refund, Earnings, Settlement, Reconciliation. Never mutate: Product, Catalog, Cart, Shipment, Return, Settlement/Payout identity. Refund may adjust payment refund fields only through refund/payment lifecycle guards; it cannot mutate payment financial identity.

Finding: Refund cannot mutate Payment authority except bounded refunded-amount/lifecycle fields through `BeginRefund`/payment lifecycle authority. Return cannot mutate Payment directly.

## Shipment Boundary Findings

Owner/truth: Shipment owns shipment identity, shipment lifecycle, carrier registration, carrier trust, and carrier-bound tracking state for existing order/shipping lineage.

Tables: `Shipments`, shipment tracking/events, carrier request/idempotency tables, order fulfillment synchronization fields guarded by lifecycle boundaries. Services: `ShipmentService`, `AdminShipmentService`, `CarrierShipmentService`, shipping webhook pipeline, `OrderShipmentOrchestrationConsumer`. Allowed callers: order shipment orchestration, admin/vendor shipment endpoints, verified carrier callbacks, recovery re-entry. Consumers: Tracking, Return logistics, customer/vendor/admin reads. Never mutate: Product, Catalog, Cart, Payment, Refund, Earnings, Settlement, Payout. Order mutation is limited to certified fulfillment synchronization.

Finding: Checkout does not mutate Shipment. Shipment does not own Orders; it consumes existing order lineage and may synchronize fulfillment only through order lifecycle policy.

## Tracking Boundary Findings

Owner/truth: Tracking owns tracking event/timeline evidence only after resolving one current tenant-scoped shipment and trusted carrier.

Tables/stores: shipment tracking fields/events, carrier webhook idempotency rows, tracking projections. Services: carrier sync/webhook pipeline, shipment tracking lifecycle policy. Allowed callers: verified carrier callbacks, shipment sync/recovery, admin views. Consumers: Customer/vendor/admin shipment timeline, return logistics. Never mutate: Product, Cart, Checkout, Orders financial truth, Payment, Refund, Return, Earnings, Settlement, Payout, Reconciliation.

Finding: Tracking cannot mutate Shipment identity. Carrier cannot mutate Tracking until it is verified, idempotency-checked, tenant-scoped, and bound to an existing shipment.

## Refund Boundary Findings

Owner/truth: Refund owns refund request identity, refund lifecycle, refund amount/currency, customer/vendor/payment/order/item lineage, execution evidence, payout effect, and refund financial ledger effects.

Tables: `CommerceHubRefunds`, refund request/evidence/execution rows, refund ledger/outbox rows, payment refunded fields where guarded. Services: `CommerceHubRefundService`, `CustomerRefundService`, `RefundExecutionService`, `CommerceHubRefundRecoveryHostedService`, Super Admin refund services. Allowed callers: customer refund endpoints, Super Admin refund workflow, recovery through refund authority. Consumers: Return, Earnings reversal, Settlement, Payout readiness, Reconciliation. Never mutate: Product, Catalog, Cart, Checkout, Shipment, Payment financial identity, Payout identity.

Finding: Refund cannot mutate Payment financial identity; it can update refund-specific payment state only through refund/payment lifecycle boundaries.

## Return Boundary Findings

Owner/truth: Return owns return identity, logistics lifecycle, action grants, return/refund/order/item/customer/vendor scope, and return financial impact as backend-derived evidence.

Tables: return records, return tracking/logistics rows, refund-bound return evidence. Services: `ReturnManagementService`, customer/vendor/Super Admin return services, stale return escalation. Allowed callers: refund-bound customer/vendor/admin return workflows. Consumers: Refund display, Shipment/logistics, Earnings/Settlement reversal evidence. Never mutate: Product, Cart, Checkout, Payment, Refund financial identity, Settlement, Payout.

Finding: Return cannot mutate Refund authority. It stays refund/order-bound and may feed evidence through certified refund/financial workflows only.

## Earnings Boundary Findings

Owner/truth: Vendor Earnings owns earning identity, lifecycle, availability, payout reservation, reversal, vendor/order/payment/refund/return/settlement lineage, and EGP financial truth.

Tables: `CommerceHubVendorEarnings`, payout item links, financial transaction/ledger rows, statement/readiness projections as derivative. Services: `CommerceHubFinancialWorkflowService`, `CommerceHubEarningsService`, finance/readiness services. Allowed callers: payment success financial workflow, refund/return reversal workflow, payout reservation workflow. Consumers: Settlement, Payout, Reconciliation, vendor finance presentation. Never mutate: Product, Orders identity, Payment identity, Refund identity. Settlement cannot mutate earnings financial identity; Payout can reserve/release only payout fields under payout boundary.

Finding: Settlement cannot mutate Earnings except through certified financial workflow facts; payout reservation is guarded by the Payout boundary.

## Settlement Boundary Findings

Owner/truth: Settlement owns EGP-only settlement identity, settlement calculation, append-only ledger/financial records, settlement visibility, and settlement-to-payout eligibility evidence.

Tables: `CommerceHubVendorSettlements`, CommerceHub financial ledger/transaction rows, settlement batch/readiness records, daily aggregate projection rows as derivative. Services: financial workflow, settlement/read services, dashboard/report services. Allowed callers: financial workflow and Super Admin read/report paths. Consumers: Payout, Reconciliation, finance dashboards. Never mutate: Earnings identity, Payout identity, Payment/refund/order identity.

Finding: Payout consumes settlement/earning eligibility but cannot mutate Settlement truth. Settlement cannot rewrite Earnings identity.

## Payout Boundary Findings

Owner/truth: Payout owns payout identity, payout item membership, reserved earning allocation, payout lifecycle, execution evidence, external reference uniqueness, and EGP provider/bank recognition.

Tables: `CommerceHubPayouts`, `CommerceHubPayoutItems`, payout method/destination snapshots, payout ledger/financial records. Services: `CommerceHubPayoutService`, `SuperAdminPayoutsService`, payout reconciliation hosted service. Allowed callers: vendor payout-method intent for destination only, Super Admin payout execution workflow, recovery/reconciliation through payout authority. Consumers: Reconciliation, vendor finance presentation, settlement reports. Never mutate: Product, Orders, Payment identity, Refund identity, Settlement identity. Reconciliation may flag or re-enter payout authority; it cannot mutate payout identity from its own evidence.

Finding: Reconciliation cannot mutate Payout authority directly. Payout cannot mutate Settlement truth.

## Reconciliation Boundary Findings

Owner/truth: Reconciliation owns mismatch detection, anomaly classification, recovery orchestration, compensation orchestration, reconciliation-required state, and reconciliation history.

Tables: reconciliation reports/history/anomaly rows, unresolved webhook rows, projection checksum/drift rows, recovery outbox rows. Services: `FinancialReconciliationService`, `CommerceHubFinanceReconciliationJob`, `CommerceHubFinanceReconciliationReadService`, payout reconciliation hosted service, order projection repair services. Allowed callers: hosted jobs, Super Admin read/diagnostic tools, recovery workers. Consumers: Operations, finance dashboards, payout/payment/refund recovery. Never mutate: Product, Catalog, Cart, Checkout, Orders, Payment, Shipment, Tracking, Refund, Return, Earnings, Settlement, Payout business truth except by re-entering the owning authority service.

Finding: Reconciliation is diagnostic/recovery authority only. It cannot recreate healthy upstream truth.

## Database Boundary Findings

PostgreSQL is the business authority boundary. Domain-owned tables are guarded by current SQL predicates, tenant lineage, lifecycle policies, idempotency, uniqueness constraints, immutable entity guards, and `CommerceHubBoundaryMutationScope`.

DbContext bypass: blocked for certified CommerceHub order/payment/refund/payout/earning/shipping assignment fields by `ApplicationDbContext` guards and immutable entity checks. Foreign services cannot write guarded tables unless the exact boundary scope is active. Background workers, repair jobs, imports, migrations, and rebuilds remain architecture-sensitive; certified runtime paths re-enter SQL authority, use fences, or no-op/tombstone on missing current authority. Migrations may create or tighten constraints but must not merge domain ownership.

## Service Boundary Findings

Controllers submit intent and enforce actor/tenant/session authorization. Application services own mutation when they are the named domain authority. Domain services/policies own lifecycle validation and calculations. Hosted services, consumers, and workers orchestrate, rebuild, retry, or re-enter authority only.

Orchestration cannot become authority because certified write paths either call the owning service or fail under DbContext guards. Examples: checkout creates orders; payment owns payment mutation; refund owns refund mutation; payout owns payout mutation; projection scope can mark projection metadata but cannot rewrite business truth.

## Event Boundary Findings

Outbox, RabbitMQ, consumers, Mongo, Redis, and Elasticsearch are derivative or sequencing infrastructure. Events can trigger projection, invalidation, retry, compensation, or owning-service re-entry. Consumers cannot mutate foreign SQL authority unless they enter the matching boundary and pass current SQL lineage checks. Projections, rebuilds, caches, and replay cannot create or restore business truth.

## Authority Graph Findings

Runtime authority graph:

Product -> Publication -> Catalog -> Search -> Cart -> Shipping Method -> Shipping Cost -> Checkout -> Split Orders -> Marketplace Order -> Payment -> Shipment -> Tracking -> Refund -> Return -> Vendor Earnings -> Settlement -> Payout -> Reconciliation.

The graph is acyclic for business truth. Downstream domains consume upstream identity/lineage as evidence and may only write their own truth. No inspected shortcut lets Search create Cart, Cart create Product, Checkout create Shipment, Shipment create Payment/Refund, Tracking create Shipment identity, Refund rewrite Payment identity, Return rewrite Refund identity, Settlement rewrite Earnings identity, Payout rewrite Settlement identity, or Reconciliation rewrite Payout identity.

## Coupling Findings

Compile-time coupling exists through shared EF entities, DTOs, and services, but runtime mutation coupling is constrained by authority scopes, SQL lineage, tenant context, idempotency, and lifecycle policies. Shared DbContext access is not sufficient to write guarded truth. Shared helpers are acceptable only where they preserve SQL recomposition and do not become broad mutation authority.

Architectural smell to watch: future broad orchestration/helper layers, generic repair jobs, direct `ExecuteUpdate`/`ExecuteDelete`, or all-domain background workers could erode the boundary if they bypass existing guards. Current inspected runtime does not show that erosion.

## Tenant Boundary Findings

POS, Storefront, CommerceHub customer, vendor, Super Admin, customer, and carrier boundaries stay separated by trusted tenant context, current backend actor/session authority, endpoint authorization, row-level SQL lineage, and DTO/cache admission checks.

One tenant cannot mutate another tenant's authority on inspected paths. Super Admin can operate only through explicit platform authority, not by bypassing domain ownership. Carrier callbacks are trusted-provider evidence and must resolve one existing tenant-scoped shipment before mutation.

## Failure Findings

400/422 failures reject malformed/stale intent. 401/403 failures purge/withhold and revalidate. 404 hides missing or foreign lineage. 409 handles idempotency, concurrency, stale price/shipping/cart, duplicate provider evidence, and lifecycle conflicts. 500/timeout/retry paths use idempotency, locks, outbox, compensation, reconciliation-required status, or owning-service recovery.

Failures cannot cross boundaries as authority. Retry, replay, and recovery re-enter current SQL/service authority or fail closed; they cannot mutate a foreign domain or recreate upstream truth.

## Architectural Risks

- Residual operational proof remains required for live chaos: provider delay/duplicate webhooks, Redis/Mongo/Elasticsearch tampering, outbox retry storms, rebuild races, timeout-after-external-success, BFCache, and multi-tab tenant/actor replacement.
- Future split-fulfillment must add explicit sub-order shipment authority instead of letting Shipment infer split-order ownership.
- Future repair/import/migration tooling must retain exact domain ownership and never use projections/cache/export/provider evidence as mutation authority.
- Future broad DbContext helpers or direct SQL bulk updates must remain reviewed authority-sensitive changes.

## Final Questions

- Who owns every domain? Product: POS Product SQL. Publication: `PublishToCommerceHubHandler`/listing SQL. Catalog/Search: derivative read models from SQL listing/product authority. Cart: `CommerceHubCartService`. Shipping Method/Cost: SQL shipping methods plus `CommerceHubShippingService`/`ShippingCalculationService`. Checkout/Split Orders/Orders: `CommerceHubCheckoutService` and order lifecycle boundary. Payment: `CommerceHubPaymentService` and payment lifecycle authority. Shipment/Tracking: shipment services, lifecycle policy, and verified carrier trust. Refund: refund services. Return: return management services. Earnings/Settlement: financial workflow, ledger, settlement services. Payout: payout services and Super Admin execution workflow. Reconciliation: reconciliation services/jobs as diagnostic recovery authority.
- Can Product mutate Payment? No.
- Can Catalog mutate Orders? No.
- Can Search mutate Checkout? No.
- Can Cart mutate Product? No.
- Can Checkout mutate Shipment? No.
- Can Shipment mutate Orders? Only certified fulfillment synchronization through order lifecycle authority; it cannot rewrite order identity or financial truth.
- Can Tracking mutate Shipment? Only tracking/lifecycle evidence on an existing tenant-scoped shipment after carrier trust/idempotency/lifecycle checks; it cannot create shipment identity.
- Can Refund mutate Payment? Only refund-specific payment fields through refund/payment lifecycle authority; it cannot mutate payment financial identity.
- Can Return mutate Refund? No direct mutation of refund authority; it remains refund/order-bound evidence and must re-enter refund/financial authority.
- Can Settlement mutate Earnings? No identity mutation; settlement consumes ledger/earning evidence.
- Can Payout mutate Settlement? No.
- Can Reconciliation mutate Payout? No direct mutation; it may re-enter payout authority or mark reconciliation/anomaly state.
- Can any downstream domain mutate upstream truth? No inspected runtime path permits it.
- Can projections mutate authority? No.
- Can cache mutate authority? No.
- Can replay mutate authority? No.
- Can recovery mutate authority? Only by re-entering the owning backend authority; recovery itself is not authority.
- Can orchestration become authority? No inspected path; orchestration sequences calls to authority services and remains guarded by DbContext scopes.
- Can hidden writers exist? No inspected certified writer is hidden; future direct SQL/bulk/import/repair paths remain architecture-sensitive.
- Can circular ownership exist? No cycle was found in the runtime authority graph.
- Can tenant leakage cross boundaries? No inspected path permits it.
- Were runtime changes required? No.
- Is CommerceHub Domain Boundary Architecture production-certifiable? Yes, subject to continued operational chaos evidence and the ARCH-2 freeze.

## Runtime Hardening Performed

None. Runtime implementation already contained the necessary SQL authority, mutation scopes, immutable entity guards, projection fences, cache admission, idempotency, tenant lineage, and lifecycle policies.

## Exact Runtime Files Modified

None.

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/CommerceHubDomainBoundaryArchitectureCertificationTests.cs`
- `PASS ARCH-2 CommerceHub Domain Boundary Architecture Freeze` in `.ai-context/CODEX_RULES.md`

## Architecture Updates

- Added this findings document.
- Added ARCH-2 entries to `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, and `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.

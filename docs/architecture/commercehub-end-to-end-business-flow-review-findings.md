# ARCH-1 CommerceHub End-to-End Business Flow Review

Date: 2026-06-25

## Scope

Runtime-first architecture review of the complete CommerceHub journey from POS product publication through catalog/search/cart/checkout/split orders/order/payment/shipment/refund/return/earnings/settlement/payout/reconciliation/vendor paid.

Reviewed required context files and completed CommerceHub certifications M6-A through M6-H, C1 through C7-G, M7-A through M7-H, and M8-A through M8-H before inspecting runtime services. This review does not replace the focused authority certifications; it verifies that they compose as one coherent business lifecycle.

## Runtime Flow Proven

1. POS Product Creation -> Storefront Product
   Owner: POS Product Authority.
   Runtime: `ProductsController`, `ProductVariantsController`, product/variant SQL entities, product visibility lineage, price authority, stock authority.
   Finding: CommerceHub cannot mint product truth. Publication and checkout both re-enter current product/variant SQL authority and require active, online-visible, published lineage.

2. Storefront Product -> CommerceHub Publication
   Owner: CommerceHub Publication Authority with POS product/tenant/category prerequisites.
   Runtime: `PublishToCommerceHubHandler`.
   Finding: Publication validates trusted tenant scope, vendor governance, live category, active shipping, EGP store currency, product/variant lineage, attribute schema, and price authority before creating/updating `CommerceHubListing`. Frontend and DTOs remain intent only.

3. Publication -> Catalog Projection -> Search
   Owner: SQL listing/product authority; projections/search are read models.
   Runtime: transactional event publisher, `MarketplaceProjectionOutboxProcessor`, Mongo read models, `CommerceHubSearchIndexService`, reindex services.
   Finding: Events and projections can display, index, retry, or rebuild only. Completion fences, event ordering, projection checksums, tenant-aware document identity, and SQL authority overlays prevent projection/search from becoming business truth.

4. Search/Product Details -> Customer Cart
   Owner: Cart Authority with product/listing/price validation.
   Runtime: `CommerceHubCartService`, cart persistence, customer/cart owner context.
   Finding: Search and product details can propose candidates only. Cart pricing and checkout revalidate current SQL product/listing/price visibility and cannot rely on stale search/index evidence.

5. Cart -> Shipping Resolution -> Checkout
   Owner: Checkout Authority and Shipping Method/Cost Authority.
   Runtime: `CommerceHubCheckoutService`, `CommerceHubShippingService`, `ShippingCalculationService`.
   Finding: Checkout locks customer and idempotency scope, locks cart, verifies cart version and requested lines, reloads listing/product/variant/category/vendor/payment-method authority from SQL, recomputes shipping per store with cache bypass, rejects stale prices/shipping, and persists only backend-calculated split-order evidence.

6. Checkout -> Split Orders -> Order Creation
   Owner: Marketplace Order Authority and Split Order Authority.
   Runtime: `CommerceHubCheckoutService`, `CommerceHubMarketplaceOrder`, `CommerceHubSubOrder`, `CommerceHubBoundaryMutationScope`.
   Finding: One serializable checkout transaction creates the marketplace order, sub-orders, immutable item/financial/shipping snapshots, inventory reservations, idempotency response, and order-created event. No frontend, cache, replay, or projection path can create a valid order outside this boundary.

7. Order -> Payment
   Owner: Payment Authority.
   Runtime: `CommerceHubPaymentService`, provider adapters, payment webhooks, payment recovery authority.
   Finding: Payment initiation requires an existing order, customer/token access, EGP currency, active reservations, current provider eligibility, payment locks, unresolved-webhook checks, duplicate-paid guards, and provider identity uniqueness. Payment cannot exist authoritatively without order lineage.

8. Payment -> Vendor Earnings
   Owner: Financial Workflow and Vendor Earnings Authority.
   Runtime: `CommerceHubPaymentService.ApplyWebhookOutcomeAsync`, `CommerceHubFinancialWorkflowService.ApplyOnlinePaymentSuccessAsync`.
   Finding: Verified provider success locks the payment, reloads payment/order SQL, checks lifecycle and identity, marks payment collected, applies order lifecycle, records ledger/audit, and creates earnings from sub-order snapshots. Provider events cannot directly create earnings.

9. Shipment/Tracking
   Owner: Shipment Authority.
   Runtime: `OrderShipmentOrchestrationConsumer`, `ShipmentService`, `CarrierShipmentService`, shipment lifecycle policy.
   Finding: Shipment creation consumes existing order/payment/shipping-method authority and resolves tenant/order/shipping lineage. Carrier callbacks/tracking evidence may update only existing tenant-scoped shipments through trust, idempotency, and lifecycle policy.

10. Refund -> Return
    Owner: Refund Authority and Return Authority.
    Runtime: `CommerceHubRefundService`, `CustomerRefundService`, `RefundExecutionService`, `ReturnManagementService`.
    Finding: Refunds and returns re-enter current order/payment/item/customer/vendor authority, preserve idempotency, and feed ledger/earnings reversal paths. Return logistics cannot create refund/payment/order truth.

11. Earnings -> Settlement -> Payout Eligibility
    Owner: Vendor Earnings, Settlement, Ledger, and Payout Eligibility Authority.
    Runtime: `CommerceHubFinancialWorkflowService`, `CommerceHubFinancialLedgerService`, finance read/readiness services.
    Finding: Available earnings are created from paid/delivered sub-orders and ledger-confirmed financial snapshots. Settlement and payout eligibility require EGP-only ledger and earning evidence; read models and dashboards cannot grant eligibility.

12. Payout Execution -> Reconciliation -> Vendor Paid
    Owner: Payout Execution and Reconciliation Authority.
    Runtime: `CommerceHubPayoutService`, `SuperAdminPayoutsService`, payout reconciliation hosted services, finance reconciliation read/job services.
    Finding: Payout creation locks selected available earnings in a serializable transaction, nets eligible negative adjustments, proves EGP, confirms ledger evidence, reserves earnings in payout, and enforces idempotency. Completion requires external/bank evidence, unique provider references, ledger posting, and reconciliation. Reconciliation can detect/repair through authority re-entry only; it cannot recreate truth.

## Domain Findings

- Product findings: POS product/variant/stock/price SQL remains upstream truth. CommerceHub publication, cart, checkout, search, and projections cannot mutate product truth.
- Publication findings: `PublishToCommerceHubHandler` is the publication transition owner. It validates tenant, governance, shipping readiness, category, currency, attributes, product lineage, and price before listing mutation.
- Catalog findings: catalog projections derive from listing/product SQL and transactional events; projection writes do not become upstream authority.
- Search findings: Elasticsearch is a read model with tenant/listing identity, alias/rebuild controls, stale deletion, and SQL authority overlay. Search can discover candidates only.
- Cart findings: cart stores customer intent and backend-validated snapshots; checkout is the final authority and rejects stale cart, price, quantity, and shipping evidence.
- Shipping findings: shipping method, eligibility, cost, and split-order assignment remain backend SQL/calculation authority; checkout bypasses cache and recomputes per store.
- Checkout findings: checkout is the single creator of marketplace orders from cart intent. It owns the cart-to-order transition and stages events transactionally.
- Split-order findings: split orders are persisted once with immutable vendor, item, financial, and shipping evidence. Generic shipment and projection paths cannot rebind split-order business truth.
- Order findings: order lifecycle is SQL-owned and updated through checkout/payment/fulfillment/refund/return guarded services only.
- Payment findings: payment initiation and webhooks require existing order/payment lineage, locks, provider identity uniqueness, amount/currency checks, and active reservations. Duplicate provider success is compensation evidence, not a second payment truth.
- Refund findings: refund identity/lifecycle/action/financial/scope are backend-owned and tied to order/payment/item/customer/vendor lineage.
- Return findings: return state is backend-owned and refund/order-bound; return logistics cannot mint refund/payment/order truth.
- Earnings findings: earnings are created through financial workflow from paid or delivered sub-orders and immutable financial snapshots; frontend and finance DTOs are display only.
- Settlement findings: settlement is EGP-only, ledger-backed, append-only, and cannot be created from frontend/projection/cache evidence.
- Payout findings: payout requires available earning locks, ledger confirmation, EGP proof, serializable transaction, idempotency, and provider/bank evidence for completion.
- Reconciliation findings: reconciliation is diagnostic/recovery authority only. It re-enters SQL/payment/payout/ledger boundaries and records anomalies rather than manufacturing healthy truth.
- Event-flow findings: SQL is authority; outbox/RabbitMQ/consumers/Mongo/Elasticsearch/Redis are derivative. Processed-event fences, leases, retries, checksums, and tombstones prevent duplicate/out-of-order projection writes from becoming business state.
- Projection findings: projections rebuild from SQL, carry source version/checksum evidence, and fail closed when stale or contradictory.
- Cache findings: Redis and React Query cache are tenant/actor scoped and admission guarded; cache can withhold/refetch/display but cannot restore authority.
- Recovery findings: recovery workers use backend services and SQL lineage. They cannot skip mandatory checkout/payment/earning/payout stages.
- Replay findings: idempotency replay returns the same persisted response only when request hash and SQL order/payout/payment identity agree. Event replay is projection replay, not business-flow recreation.
- Concurrency findings: checkout locks customer/idempotency/cart plus serializable SQL; payment locks order/provider and payment processing; payout locks earnings and idempotency; webhook/provider identities have unique constraints. Duplicate execution collapses or becomes compensation/reconciliation evidence.
- Tenant findings: tenant/store/vendor/customer scope is derived from trusted backend context and SQL lineage. Storefront/CommerceHub/Super Admin/POS scopes stay separated at runtime.
- Cross-domain findings: downstream domains consume upstream SQL lineage but cannot write upstream truth. Orchestration services call authority services and are not authority by themselves.
- Business orchestration findings: checkout, split-order creation, shipping, payment, settlement, payout, and reconciliation are orchestrated through backend services with explicit mutation scopes. Orchestration remains sequencing, not business truth.
- Failure findings: 400/401/403/404/409/422 reject or withhold. 500/timeout/retry paths use idempotency, locks, outbox, recovery, or compensation. Partial failures do not advance mandatory lifecycle stages without authoritative SQL mutation.

## Final Business Questions

- Overall owner: backend CommerceHub business authority graph, with POS product authority upstream and CommerceHub checkout/payment/finance/shipment/refund/return/payout services owning their respective transitions.
- Can frontend create business truth? No.
- Can any domain bypass mandatory stages? No inspected runtime path permits it.
- Can payment exist without order? No authoritative payment initiation/webhook mutation requires order/payment lineage; unresolved provider evidence is suspense/reconciliation only.
- Can shipment exist without split order/order lineage? No; shipment creation is order-bound and shipping-method-bound. Generic shipment currently models canonical order shipment, not a separate CommerceHub split-fulfillment authority.
- Can settlement exist without earnings? No inspected settlement/payout path requires ledger/earning evidence.
- Can payout exist without settlement/earning eligibility? No; payout creation requires available earnings, ledger confirmation, EGP proof, locks, and idempotency.
- Can reconciliation recreate business truth? No; it can re-enter authority or flag anomalies only.
- Can replay recreate business flow? No; replay can collapse duplicate idempotent responses or rebuild projections from SQL only.
- Can recovery skip business stages? No inspected recovery path skips authority gates; unresolved or partial evidence remains blocked, suspense, compensation, or reconciliation-required.
- Can projections become business truth? No.
- Can cache become business truth? No.
- Can business lifecycle diverge? No inspected production-certification blocker remains; lifecycle changes are guarded by the owning backend services and policies.
- Can authority diverge? No inspected cross-domain authority emergence remains.
- Can tenant leakage occur? No inspected path permits Vendor A/Store A to influence Vendor B/Store B business flow; residual operational chaos proof remains required.

## Architectural Risks

- Residual operational proof remains necessary for deployed chaos scenarios: provider delays, duplicate webhooks, multi-tab actor replacement, BFCache, Redis/Mongo/Elasticsearch tampering, outbox retry storms, projection rebuild races, payout provider ambiguity, and recovery after partial external success.
- Future CommerceHub split-fulfillment models must add explicit sub-order shipment lineage rather than reusing generic order shipment authority.
- Future admin/import/repair tooling must retain backend authority re-entry and must not write upstream truth from projection, cache, export, or provider evidence.

## Runtime Hardening Performed

No runtime hardening was required in ARCH-1. The inspected runtime already composes the certified authority boundaries into a coherent end-to-end business journey.

## Files Modified

- `docs/architecture/commercehub-end-to-end-business-flow-review-findings.md`
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Regression Freezes Added

An ARCH-1 architecture freeze was added to `CODEX_RULES.md`. No executable test freeze was added because no runtime behavior changed.

## Certification Decision

The complete CommerceHub business journey is production-certifiable as a single end-to-end architecture, subject to continued deployed operational chaos evidence and the ARCH-1 freeze for future changes.

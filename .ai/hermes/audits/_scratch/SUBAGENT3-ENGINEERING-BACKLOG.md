# Sub-Agent 3 — Engineering Backlog: Orders / CommerceHub / Storefront / Customers

**Domain:** Orders, CommerceHub (Marketplace), Storefront, Customers (POS + Storefront + CommerceHub + Affiliate identities)
**Task:** Extract EVERY unresolved engineering issue + documentation conflicts from the `.ai/hermes` knowledge base.
**Source corpus:** 55 files (cross-cutting 02–08, domain capability/architecture files, scratch evidence reports, system READMEs, certification files).
**Method:** Implementation-backed extraction. Completed work, historical info, strengths, resolved items, and certified-truth statements were excluded. Issue list = work STILL unresolved.

**Counts:** 57 issues + 14 conflicts = 71 entries.

---

## ENGINEERING ISSUES

### ORD-001: No canonical `OrderStatus` state machine (duplicated transition rules)
- Severity: High
- Category: Architecture Issue
- Description: `OrderStatus` transition rules are duplicated across `OrderStatusSyncService.IsValidStatusProgression` and `OrderFulfillmentService.ResolveOrderStatus`/`GetOrderProgressionRank`. Unlike `FulfillmentStatus.GetAllowedTransitions` there is no single `OrderStatus.CanTransitionTo`. The two implementations can drift, causing inconsistent lifecycle enforcement.
- Recommendation: Extract a single `OrderStatus.CanTransitionTo` + central validator; route all transitions through it. (Refs FU-1 / TD-1.)
- Complexity: M
- Dependencies: none
- Affected Modules: Order runtime, OrderStatusSyncService, OrderFulfillmentService, AdminOrderService
- Source Documents: platform/order-runtime-final-architecture.md:872-876 (§38 TD-1); _scratch/order-fulfillment-shipping.md:120-121 (§7); _scratch/order-cqrs-crossdomain.md:94
- Evidence: "Transition rules are duplicated across OrderStatusSyncService … and OrderFulfillmentService.ResolveOrderStatus … there is no OrderStatus.CanTransitionTo. Risk: the two can drift."
- Implementation Required: yes
- Architecture Only: yes
- Documentation Only: no

### ORD-002: `FulfillmentMode` immutability not wired into migrations/CI
- Severity: High
- Category: Technical Debt
- Description: The DB trigger + CHECK constraint enforcing `FulfillmentMode`/`FulfillmentCarrierCode` immutability live ONLY in the standalone `add_fulfillment_ownership_snapshot.sql` script. No grep hit wires it into DbPatcher/CI, and the EF model snapshot lacks the trigger/CHECK. Environments that missed the script are unprotected (a generic update could rebind Manual↔Carrier ownership).
- Recommendation: Port the trigger/CHECK into an EF migration + add to DbPatcher; verify the script was applied in all target environments. (Refs FU-3 / TD-2.)
- Complexity: M
- Dependencies: none
- Affected Modules: Orders, EF migrations, DbPatcher/CI
- Source Documents: platform/order-runtime-final-architecture.md:877-879 (TD-2); _scratch/order-fulfillment-shipping.md:66-72 (§3); capabilities/pos-admin/order-management.md:759-762
- Evidence: "Trigger/CHECK live only in the standalone .sql script. No grep hit wires add_fulfillment_ownership_snapshot.sql into DbPatcher/CI … The enforcement therefore depends on the SQL script having been applied out-of-band."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-003: No storefront / customer self-cancellation path
- Severity: Medium
- Category: Missing Feature
- Description: Only merchant (admin) cancellation + automatic (payment/checkout-failure) cancellation exist. No storefront/customer controller or service exposes an order-cancel endpoint. Customers cannot cancel their own orders in code.
- Recommendation: Add an authorized storefront/customer cancel path (reuse AdminOrderService cancellation + payment-cancellation outbox). (Refs FU-5 / TD-4.)
- Complexity: M
- Dependencies: ORD-001
- Affected Modules: Storefront Orders API, AdminOrderService, PaymentLifecycle
- Source Documents: platform/order-runtime-final-architecture.md:883-885 (TD-4); _scratch/order-fulfillment-shipping.md:141 (§8)
- Evidence: "Customer/storefront self-cancellation path: NOT FOUND. No storefront/customer controller or service exposes an order-cancel endpoint."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-004: No storefront chargeback handling
- Severity: High
- Category: Missing Feature
- Description: `PaymentLifecycleService` has no chargeback transition for the storefront `Order`; only CommerceHub runtimes handle chargeback. A storefront chargeback (e.g. Stripe dispute) has no in-order state, so disputed money has no lifecycle representation.
- Recommendation: Add a `PaymentLifecycleService` storefront chargeback transition + suspense handling. (Refs FU-2 / TD-3.)
- Complexity: M
- Dependencies: none
- Affected Modules: Payment Lifecycle, Storefront Orders
- Source Documents: platform/order-runtime-final-architecture.md:880-882 (TD-3)
- Evidence: "No storefront chargeback handling. PaymentLifecycleService has no chargeback transition for the storefront Order; only CommerceHub runtimes handle chargeback."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-005: Dead `OrderFulfillmentCancelledEvent` / `CancelAsync` dead-end
- Severity: Low
- Category: Technical Debt
- Description: `IOrderFulfillmentService.CancelAsync` deliberately refuses (`ORDER_CANCELLATION_LIFECYCLE_REQUIRED`); the fulfillment `Cancelled` target is reachable in the enum but no caller exercises it. `OrderFulfillmentCancelledEvent` is declared but unwired. The event/path is dead code.
- Recommendation: Either wire `CancelAsync`→fulfillment `Cancelled` through the order-cancellation lifecycle, or remove the unused event/path. (Refs FU-6 / TD-5.)
- Complexity: S
- Dependencies: ORD-003
- Affected Modules: OrderFulfillmentService, fulfillment events
- Source Documents: platform/order-runtime-final-architecture.md:886-889 (TD-5); _scratch/order-fulfillment-shipping.md:143-144 (§8)
- Evidence: "CancelAsync deliberately refuses … fulfillment Cancelled target is reachable in the enum but no caller exercises it. Dead/unreachable path."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-006: Storefront `orders` Mongo doc has no checksum
- Severity: Low
- Category: Known Limitation
- Description: Only the CommerceHub doc carries a projection checksum. Storefront repair relies on `ComputeAuthorityFingerprint` JSON equality, which excludes freshness/Timeline fields and re-upserts on version-only differences. Weaker integrity than CommerceHub.
- Recommendation: Add `OrderProjectionChecksum.Compute` for the storefront doc. (Refs FU-7 / TD-6.)
- Complexity: S
- Dependencies: none
- Affected Modules: Storefront Order projection, repair service
- Source Documents: platform/order-runtime-final-architecture.md:890-892 (TD-6); _scratch/order-cqrs-crossdomain.md:95
- Evidence: "Mongo orders doc has no checksum. Storefront repair relies on ComputeAuthorityFingerprint JSON equality, which excludes freshness/Timeline fields."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-007: Projection Total vs SQL Total not asserted at runtime
- Severity: Medium
- Category: Known Limitation
- Description: No compile-time or runtime invariant enforces `OrderDocument.Total == Order.Total`. Convergence depends solely on the 30s sweep + on-demand repair; an event-path bug between sweeps could silently drift the customer-visible total.
- Recommendation: Add a convergence test / checksum guard on `Total`. (Refs FU-4 / TD-7.)
- Complexity: S
- Dependencies: none
- Affected Modules: OrderProjectionConsumer, StorefrontOrderProjectionRepairService, reconciliation
- Source Documents: platform/order-runtime-final-architecture.md:893-894 (TD-7); _scratch/order-cqrs-crossdomain.md:94
- Evidence: "No runtime/compile invariant enforces OrderDocument.Total == Order.Total; convergence depends on the 30s sweep + on-demand repair."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-008: Commission double-source edge race in storefront projection
- Severity: Medium
- Category: Bug
- Description: `OrderDocument.AffiliateCommissionAmount` is written by BOTH the event projector and the SQL-authority snapshot on repair. A stale repair using the whole-doc `LastEventVersion` freshness guard (not the commission facet) could reset commission in a narrow race window.
- Recommendation: Repair should use the commission-facet freshness guard, not the whole-doc version. (Refs FU-8 / TD-8.)
- Complexity: M
- Dependencies: ORD-007
- Affected Modules: OrderProjectionConsumer, StorefrontOrderProjectionAuthoritySnapshotLoader
- Source Documents: platform/order-runtime-final-architecture.md:895-898 (TD-8); _scratch/order-cqrs-crossdomain.md:96
- Evidence: "Repairs overwrite event-incremented values with max(0, earned-reversed). If an event replay races a repair … potential edge race where a stale repair could reset commission."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-009: Legacy `Order.IsPaid` deprecated but still written
- Severity: Low
- Category: Technical Debt
- Description: `Order.IsPaid` (`Order.cs:531` warns it may be out-of-sync) plus `PaymentStatus.Paid`/`PartiallyPaid` `[Obsolete]` enums are still written. Item-level tracking is the truth; the lingering dual model is a drift risk.
- Recommendation: Fully migrate to item-level `PaidAmount`/`RefundedAmount` + remove legacy `IsPaid`/obsolete enums. (Refs FU-9 / TD-9.)
- Complexity: M
- Dependencies: none
- Affected Modules: Order entity, Payment Lifecycle
- Source Documents: platform/order-runtime-final-architecture.md:899-901 (TD-9); capabilities/pos-admin/order-management.md:769-771
- Evidence: "Legacy Order.IsPaid is deprecated but still written … Item-level tracking is the truth. Lingering dual model."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-010: Misleading `OrderDeliveredEventConsumer` filename vs class
- Severity: Low
- Category: Documentation Inconsistency
- Description: `OrderDeliveredEventConsumer.cs` contains class `FulfillmentDeliveredEventConsumer` consuming `FulfillmentDeliveredEvent`. Behavior is correct, but the naming misleads readers/tooling.
- Recommendation: Rename file to `FulfillmentDeliveredEventConsumer.cs`. (Refs TD-10.)
- Complexity: S
- Dependencies: none
- Affected Modules: Order fulfillment consumers
- Source Documents: platform/order-runtime-final-architecture.md:902-903 (TD-10); _scratch/order-fulfillment-shipping.md:98,163
- Evidence: "file is named OrderDeliveredEventConsumer.cs but the class is FulfillmentDeliveredEventConsumer and consumes FulfillmentDeliveredEvent"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-011: Dual carrier config runtime authority divergence (CarrierConfiguration vs CarrierAccount)
- Severity: High
- Category: Architecture Issue
- Description: Two independently modeled carrier config authorities exist. `CarrierConfiguration` (legacy) is read by checkout, quotes, CommerceHub shipping, shipment creation, carrier registration/cancellation/sync, and webhook trust. `CarrierAccount` (governance) is read only by storefront warehouse-scoped visibility filtering. A carrier enabled in one but blocked/inactive in the other yields inconsistent availability ("available in admin but rejected at checkout", "visible in selector but no quote"). No adapter enforces equivalence.
- Recommendation: Converge to ONE runtime authority — either promote `CarrierAccount` (and wire checkout/quote/registration/webhook to it) or explicitly demote it to governance/visibility-only and rename screens. Document both paths and block UI from presenting one as the other's source of truth until converged.
- Complexity: L
- Dependencies: none
- Affected Modules: CheckoutService, ShippingQuoteService, CommerceHubShippingService, ShipmentService, CarrierShipmentService, ShippingMethodService, ShippingAdministration
- Source Documents: capabilities/commerce/shipping-management.md:290-295, 363-459, 541-560; platform/shipping-fulfillment-runtime-final-architecture.md:650, 660, 849-851, 860
- Evidence: "A carrier can be enabled in CarrierConfiguration but inactive/blocked in CarrierAccount; checkout and quotes still see it as available … Merchants can observe inconsistent carrier availability between admin lifecycle screens, storefront method visibility, checkout acceptance, quote results, and shipment registration."
- Implementation Required: yes
- Architecture Only: yes
- Documentation Only: no

### ORD-012: Duplicated shipment-lifecycle services (`ShipmentService` vs `AdminShipmentService`)
- Severity: Medium
- Category: Technical Debt
- Description: Both services implement overlapping shipment status-transition validation, timestamp stamping, event emission, fulfillment synchronization, and DTO mapping with near-identical `EventTimestampSequence` helpers. Because both mutate the same `Shipment` aggregate, a rule fixed in one can stale in the other — a runtime risk, not just style duplication.
- Recommendation: Extract a single shipment-lifecycle domain service for transition validation, timestamp application, event emission, and fulfillment sync; keep admin/storefront controllers/DTOs separate but route all mutation through it.
- Complexity: L
- Dependencies: none
- Affected Modules: ShipmentService, AdminShipmentService, Shipment aggregate
- Source Documents: capabilities/commerce/shipping-management.md:296-298, 498-539; platform/shipping-fulfillment-runtime-final-architecture.md:863
- Evidence: "Both services implement status transition validation, timestamp stamping … fulfillment synchronization through IOrderFulfillService. Parallel lifecycle implementations can drift."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-013: `ServiceLevel` free-text magic strings violate no-magic-strings discipline
- Severity: Low
- Category: Technical Debt
- Description: "standard/express/same_day/next_day" are magic strings across `Shipment`, `CarrierConfiguration.DefaultServiceLevel`, and quotes, contradicting the strict no-magic-strings rule applied to carrier codes.
- Recommendation: Introduce a `ServiceLevel` enum; migrate string usages.
- Complexity: S
- Dependencies: none
- Affected Modules: Shipment, CarrierConfiguration, quotes
- Source Documents: capabilities/commerce/shipping-management.md:303-305
- Evidence: "ServiceLevel is free-text, not an enum … contradicts the strict no-magic-strings rule applied to carrier codes"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-014: FedEx adapter uncertified
- Severity: Low
- Category: Known Limitation
- Description: `CarrierCodes.FedEx` comment states "Runtime foundation only; operational behavior is not certified." The adapter exists but is not production-verified.
- Recommendation: Certify or remove the FedEx adapter before enabling it for live shipments.
- Complexity: S
- Dependencies: none
- Affected Modules: Carrier adapters
- Source Documents: capabilities/commerce/shipping-management.md:301-302; platform/shipping-fulfillment-runtime-final-architecture.md:864
- Evidence: "FedEx uncertified: CarrierCodes.FedEx comment: 'Runtime foundation only; operational behavior is not certified.' Adapter exists but is not production-verified."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-015: No shipping zone / postal engine
- Severity: Medium
- Category: Missing Feature
- Description: Despite mission expectations of zones/postal rules, coverage is only `AvailableCountries` + country normalization. No state/city/postal rule tables, no volumetric/dimensional/distance pricing, no explicit method-type enum, no shipping-config domain events.
- Recommendation: Implement rich shipping zones (states/cities/postal), volumetric/dimensional pricing, price-band methods, method-type enum, and shipping-config domain events as roadmap.
- Complexity: XL
- Dependencies: none
- Affected Modules: Shipping method catalog, ShippingCalculationService
- Source Documents: capabilities/commerce/shipping-management.md:306-308, 323-326
- Evidence: "No zone engine: despite the mission's zone/postal expectations, coverage is only AvailableCountries + country normalization; no state/city/postal rule tables."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-016: No Super-Admin (platform) shipping governance
- Severity: Low
- Category: Missing Feature
- Description: Shipping configuration is tenant/POS-Admin only. Super-Admin has no platform-level shipping governance surface.
- Recommendation: Add platform-level shipping governance if a cross-tenant policy surface is required.
- Complexity: M
- Dependencies: none
- Affected Modules: Shipping administration, Super-Admin platform
- Source Documents: capabilities/commerce/shipping-management.md:326
- Evidence: "Super-Admin (platform) shipping governance — configuration is tenant/POS-Admin only."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-017: `ShipmentCodCollectedEvent` has no consumer (dead event)
- Severity: High
- Category: Bug
- Description: `ShipmentCodCollectedEvent` is published but has 0 consumers (verified). COD-collection downstream effects (ledger/notification) are not wired, so the event is dead or the intended consumer is missing.
- Recommendation: Add a `ShipmentCodCollectedEvent` consumer (or remove the dead event).
- Complexity: S
- Dependencies: none
- Affected Modules: Shipment events, COD collection
- Source Documents: platform/shipping-fulfillment-runtime-final-architecture.md:814, 859
- Evidence: "the event is dead or the intended consumer is missing (verified: 0 consumers)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-018: No Polly/RetryPolicy in carrier path
- Severity: Medium
- Category: Reliability Concern
- Description: Retry in the carrier shipment path is hand-rolled; there is no Polly/`RetryPolicy`. Outbound carrier calls have no rate limiter and no back-off-on-contention.
- Recommendation: Introduce a structured retry/rate-limit/back-off policy for carrier external calls.
- Complexity: M
- Dependencies: none
- Affected Modules: CarrierShipmentService, carrier adapters
- Source Documents: platform/shipping-fulfillment-runtime-final-architecture.md:418, 838-840
- Evidence: "NO Polly/RetryPolicy in the carrier path — retry is hand-rolled … outbound carrier calls have no rate limiter."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-019: No dedicated fulfillment/shipment Mongo projection consumer or repair job
- Severity: Medium
- Category: Known Limitation
- Description: The storefront order read model is fed by LEGACY order/shipment events; the NEW `Fulfillment*` events feed only Affiliate + payment. Replay/repair of shipment state is indirect (outbox slow-replay + per-consumer version fences). No first-class shipment projection/repair.
- Recommendation: Add a fulfillment/shipment projection consumer + repair job if storefront scale needs direct shipment read models.
- Complexity: M
- Dependencies: none
- Affected Modules: Fulfillment events, Storefront order projection
- Source Documents: platform/shipping-fulfillment-runtime-final-architecture.md:845-848
- Evidence: "No dedicated fulfillment/Shipment Mongo projection consumer or repair job … replay/repair is indirect."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-020: `WebhookSecret`/`SupportsWebhooks` only on legacy `CarrierConfiguration`
- Severity: Medium
- Category: Architecture Issue
- Description: Webhook trust secrets live only on legacy `CarrierConfiguration`. The new `CarrierAccount` aggregate has no webhook-secret concept, so webhook trust cannot migrate to `CarrierAccount` without new fields. Ties webhook security to the legacy table (see ORD-011).
- Recommendation: Add webhook-secret fields to `CarrierAccount` and migrate webhook trust, or explicitly document the legacy dependency.
- Complexity: M
- Dependencies: ORD-011
- Affected Modules: CarrierConfiguration, CarrierAccount, webhook trust
- Source Documents: platform/shipping-fulfillment-runtime-final-architecture.md:849-851
- Evidence: "WebhookSecret/SupportsWebhooks live only on legacy CarrierConfiguration … the new CarrierAccount aggregate has no webhook-secret concept."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-021: Multi-vendor / split shipment not implemented as separate shipments
- Severity: Medium
- Category: Missing Feature
- Description: Split-order shipping immutability and vendor isolation are enforced, but multi-vendor split shipment is not implemented as separate physical shipments — relevant to CommerceHub marketplace fulfillment.
- Recommendation: Model per-vendor shipment creation if marketplace fulfillment requires discrete carrier shipments per vendor.
- Complexity: L
- Dependencies: none
- Affected Modules: Shipping, CommerceHub sub-order fulfillment
- Source Documents: platform/shipping-fulfillment-runtime-final-architecture.md:567
- Evidence: "Multi Vendor / Split Shipment: not implemented as separate shipments in this runtime."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-022: Post-commit payment-intent gap compensation documented after the fact
- Severity: Medium
- Category: Documentation Inconsistency
- Description: The post-commit payment-intent gap (order committed but PaymentIntent creation failed) compensation path — checkout re-checks under a row lock, fails the order closed, or leaves for reconciliation — was not documented in `05-orders.md` until the checkout capability file captured it. Knowledge gap caused wrong assumptions in earlier docs.
- Recommendation: Keep `05-orders.md` in sync with the checkout capability file; add a cross-reference note.
- Complexity: S
- Dependencies: none
- Affected Modules: Checkout, Orders
- Source Documents: capabilities/pos-admin/checkout-management.md:530-533, 631-639, 660-662
- Evidence: "The post-commit payment-intent gap compensation … was not documented. 05-orders.md understates the in-transaction work."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ORD-023: `AdminOrderService` comment falsely claims Order has no TenantId
- Severity: Medium
- Category: Documentation Inconsistency
- Description: `AdminOrderService` class header comment states "The Order entity does NOT have TenantId column. Tenant isolation MUST be enforced via ownership joins." This is WRONG: `Order` implements `ITenantOwned` and has a non-null `TenantId` column enforced at DB level. Misleads maintainers into thinking ownership joins are the only isolation mechanism.
- Recommendation: Correct the comment; keep ownership joins as defense-in-depth but document the real TenantId column.
- Complexity: S
- Dependencies: none
- Affected Modules: AdminOrderService, Order entity
- Source Documents: capabilities/pos-admin/order-management.md:729-733, 812-813
- Evidence: "This is wrong: Order implements ITenantOwned and has a non-null TenantId column (enforced at DB level). POS Admin code still uses ownership joins as defense-in-depth, but the stated premise is false and misleading."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ORD-024: Pending-payment cancellation behavior (OnHold + deferred inventory release) under-documented
- Severity: Low
- Category: Documentation Inconsistency
- Description: Cancellation of an order with a pending PaymentIntent does NOT immediately cancel; it moves to OnHold, enqueues `PaymentCancellationOutboxMessage`, and defers inventory release until payment cancellation completes. The simpler "cancel releases inventory" phrasing hides this timing.
- Recommendation: Document the OnHold + deferred-release behavior explicitly wherever cancellation is described.
- Complexity: S
- Dependencies: none
- Affected Modules: AdminOrderService, Payment Lifecycle
- Source Documents: capabilities/pos-admin/order-management.md:735-738, 813-814
- Evidence: "A naive reader of 'cancel releases inventory' would be wrong about timing."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ORD-025: Failed shipment leaves order status unchanged (hidden behavior)
- Severity: Low
- Category: Documentation Inconsistency
- Description: A failed shipment intentionally does NOT cancel or change order status (admin intervention required). The shipment→order mapping leaves the order in its current state on `ShipmentStatus.Failed`. Docs implying auto-cancel are wrong.
- Recommendation: Document explicitly that failed shipment = no order status change; admin must intervene.
- Complexity: S
- Dependencies: none
- Affected Modules: OrderStatusSyncService, shipment mapping
- Source Documents: capabilities/pos-admin/order-management.md:740-742, 814-815; platform/shipping-fulfillment-runtime-final-architecture.md:379
- Evidence: "The shipment→order mapping explicitly leaves the order in its current state on ShipmentStatus.Failed."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ORD-026: Outbound fulfillment gated by payment (hidden business rule)
- Severity: Low
- Category: Documentation Inconsistency
- Description: Payment/fulfillment outbound progression is gated by `OrderOperationalFinancialAuthority`: non-COD positive-total orders cannot advance outbound until payment is a paid state AND (if required) the collection accounting journal is posted. Documentation that assumes fulfillment can proceed independently of payment is wrong.
- Recommendation: Surface the financial gating rule in all fulfillment/order lifecycle docs.
- Complexity: S
- Dependencies: none
- Affected Modules: OrderFulfillmentService, Payment Lifecycle
- Source Documents: capabilities/pos-admin/order-management.md:749-752, 815-816; platform/order-runtime-final-architecture.md:551-553
- Evidence: "Documentation that assumes fulfillment can proceed independently of payment is wrong."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ORD-027: `FulfillmentMode` ownership boundary under-documented
- Severity: Low
- Category: Documentation Inconsistency
- Description: `FulfillmentMode` (Manual/Carrier) is snapshotted at creation and immutable; it (not ShippingMethod) decides who may drive fulfillment. Carrier orders reject manual admin transitions (409); Manual orders reject carrier webhooks. This core authority boundary is not always documented.
- Recommendation: Document the FulfillmentMode ownership split in fulfillment/order lifecycle docs.
- Complexity: S
- Dependencies: none
- Affected Modules: Order fulfillment, Shipping
- Source Documents: capabilities/pos-admin/order-management.md:759-762, 816-817
- Evidence: "FulfillmentMode (Manual/Carrier) is snapshotted at creation and immutable; it … decides who may drive fulfillment. This ownership split is a core authority boundary not always documented."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ORD-028: Mongo payment-projection collapse-to-SQL behavior hidden
- Severity: Low
- Category: Documentation Inconsistency
- Description: Mongo payment projection replays are collapsed against SQL `OrderPaymentLineageAuthority`; a replayed event that disagrees with SQL is ignored, and if no SQL order exists the Mongo payment projection is deleted. Projection is never allowed to lead SQL. This is easy to miss.
- Recommendation: Document the projection-collapse-to-SQL behavior in projection/CQRS docs.
- Complexity: S
- Dependencies: none
- Affected Modules: OrderProjectionConsumer
- Source Documents: capabilities/pos-admin/order-management.md:764-767, 817-818
- Evidence: "a replayed OrderPaymentStatusChangedEvent that disagrees with SQL is ignored … if no SQL order exists the Mongo payment projection is deleted."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ORD-029: Obsolete `Paid`/`PartiallyPaid` enum naming
- Severity: Low
- Category: Documentation Inconsistency
- Description: `Order.PaymentStatus` enum marks `Paid` (=2) and `PartiallyPaid` (=3) as `[Obsolete]`; canonical recognition is `Collected`/`Settled`/`Refunded`/`PartiallyRefunded`. Code referencing `Paid` should be treated as `Collected`, but the obsolete names persist.
- Recommendation: Remove or clearly alias obsolete enum members; update references.
- Complexity: S
- Dependencies: ORD-009
- Affected Modules: Order entity, Payment Lifecycle
- Source Documents: capabilities/pos-admin/order-management.md:769-771, 818-819
- Evidence: "Code referencing Paid should be treated as Collected."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ORD-030: Elasticsearch not used for order search (false assumption risk)
- Severity: Low
- Category: Documentation Inconsistency
- Description: Admin order search is SQL; storefront order history is Mongo. Any doc implying an Elasticsearch order index is incorrect (ES is used for catalog/search, not orders).
- Recommendation: Correct any doc that implies ES order indexing.
- Complexity: S
- Dependencies: none
- Affected Modules: Order search
- Source Documents: capabilities/pos-admin/order-management.md:773-774, 819-820
- Evidence: "Elasticsearch is not used for order search in this codebase; admin search is SQL, storefront history is Mongo."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ORD-031: `CheckoutState` lifecycle stage often omitted from order lifecycle descriptions
- Severity: Low
- Category: Documentation Inconsistency
- Description: `CheckoutState` (Draft/PendingPaymentIntent/PaymentPending/Paid/Failed/Cancelled/Expired) is a distinct implementation-backed machine governing placement/payment progression, separate from `OrderStatus`. It is frequently omitted from order lifecycle descriptions, causing incomplete mental models.
- Recommendation: Always show the four cooperating state machines (OrderStatus, CheckoutState, PaymentStatus, FulfillmentStatus) together.
- Complexity: S
- Dependencies: none
- Affected Modules: Orders, Checkout
- Source Documents: capabilities/pos-admin/order-management.md:754-757, 817; platform/order-runtime-final-architecture.md:512-521
- Evidence: "Often omitted from order lifecycle descriptions."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ORD-032: OnlineCustomerId (not CustomerId) stamped on storefront orders — easy to miss
- Severity: Medium
- Category: Known Limitation
- Description: Storefront orders are stamped with `OnlineCustomerId`, never POS `CustomerId` (deliberate — POS CustomerId would be a wrong FK). This is in code comments but is a frequent source of incorrect joins/reporting assumptions across the customer domains.
- Recommendation: Document the identity boundary prominently in order + customer docs; add a guard/test preventing CustomerId misuse on storefront orders.
- Complexity: S
- Dependencies: ORD-046
- Affected Modules: Checkout, Orders, Customer identity
- Source Documents: capabilities/pos-admin/checkout-management.md:670-672, 729-730
- Evidence: "OnlineCustomerId, never CustomerId, is stamped on storefront orders (deliberate — POS CustomerId would be a wrong FK)."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ORD-033: OTP is conditional, not mandatory at checkout
- Severity: Medium
- Category: Known Limitation
- Description: Checkout only blocks on OTP when the verification service is wired, the tenant has WhatsApp OTP enabled, and the phone is unverified. Many assume OTP always gates checkout. A 403 `PHONE_BLOCKED` can occur even with OTP verified (it is the merchant blocked-phone list, independent of OTP).
- Recommendation: Document the conditional OTP + independent phone-block behavior; add a coverage test for each combination.
- Complexity: S
- Dependencies: none
- Affected Modules: Checkout, Storefront Auth, Customer Trust
- Source Documents: capabilities/pos-admin/checkout-management.md:673-677, 730-731
- Evidence: "OTP is conditional, not mandatory … A 403 PHONE_BLOCKED can occur even with OTP verified."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ORD-034: Non-tracked / backorderable variants skip stock checks entirely
- Severity: Medium
- Category: Known Limitation
- Description: Non-tracked / backorderable product variants skip stock checks entirely by design. This is correct behavior but is a frequent source of "out of stock" confusion in checkout/order QA.
- Recommendation: Document the skip explicitly in inventory + checkout docs; ensure UX communicates backorderable availability.
- Complexity: S
- Dependencies: none
- Affected Modules: Checkout, Inventory reservation
- Source Documents: capabilities/pos-admin/checkout-management.md:678-679, 731
- Evidence: "Non-tracked / backorderable variants skip stock checks entirely — not a checkout bug when 'out of stock' does not trigger for such items."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ORD-035: Storefront missing-order projection produces false absence
- Severity: High
- Category: Bug
- Description: When an order is committed in SQL but the Mongo/read model is missing or stale, the Storefront can report the order as nonexistent. A derivative read model became absence authority on a critical read.
- Recommendation: Ensure critical order reads use SQL-backed repair / freshest projection, or fail closed to "temporarily unavailable" rather than false absence.
- Complexity: M
- Dependencies: ORD-007
- Affected Modules: Storefront Orders read, OrderProjectionConsumer, repair service
- Source Documents: systems/storefront/README.md:919-925; systems/commercehub/README.md:1144, 1085, 1089
- Evidence: "Order creation committed in SQL, but Mongo/read model is missing or stale and Storefront reports the order as nonexistent. Business failure: derivative order read became absence authority on a critical read."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-036: Duplicate Mongo order docs for one SQL order
- Severity: Medium
- Category: Bug
- Description: Two order projection docs can exist for one SQL order (e.g., from stale replays or divergent consumers). A duplicate projection can mislead storefront reads.
- Recommendation: Ensure critical reads select the freshest doc by freshness markers; add a dedupe/reconciliation guard.
- Complexity: M
- Dependencies: none
- Affected Modules: OrderProjectionConsumer, Storefront order read
- Source Documents: systems/commercehub/README.md:1150-1152
- Evidence: "Two order projection docs for one SQL order. Business failure: duplicate projection can mislead."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-037: Storefront return/refund request can bypass money authority
- Severity: High
- Category: Security Concern
- Description: A customer return/refund action updating refund/payment state directly, or presenting refund completion before refund authority admits it, bypasses the return/refund/payment lifecycle ownership. The storefront request flow must not become money authority.
- Recommendation: Route customer intent into backend return/refund authority; keep money state pending/processing/review/reconciliation until admitted by `IPaymentLifecycleService`.
- Complexity: M
- Dependencies: none
- Affected Modules: Storefront return/refund, Payment Lifecycle
- Source Documents: systems/storefront/README.md:927-933
- Evidence: "A customer return/refund action updates refund/payment state directly or presents refund completion before refund authority admits it."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-038: Stale account/session survives revocation or tenant mismatch
- Severity: High
- Category: Security Concern
- Description: A stale token/cookie/local profile can let a customer view or mutate cart/order/account data after account disabled, session revoked, tenant changed, or ownership mismatch. Frontend/session evidence became identity authority.
- Recommendation: Every action must re-cross current backend customer/session/tenant/order ownership authority; revoke stale sessions server-side.
- Complexity: M
- Dependencies: none
- Affected Modules: Storefront auth/session, Orders, Account
- Source Documents: systems/storefront/README.md:935-941
- Evidence: "A stale token/cookie/local profile lets a customer view or mutate cart/order/account data after account disabled, session revoked, tenant changed, or ownership mismatch."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-039: CommerceHub vendor ban not suppressing listings
- Severity: High
- Category: Governance Concern
- Description: Banned vendor products can still appear in CommerceHub search. Governance suppression is not enforced before public return.
- Recommendation: Enforce vendor governance suppression (suspended/banned) in search/public listing visibility before return.
- Complexity: M
- Dependencies: none
- Affected Modules: CommerceHub search, Marketplace governance, Vendor eligibility
- Source Documents: systems/commercehub/README.md:1154-1156
- Evidence: "Banned vendor products still in CommerceHub search. Business failure: governance not enforced."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-040: Unresolved captured payment blocks all outward money
- Severity: High
- Category: Risk
- Description: An unmatched provider success webhook with no tenant scope blocks ALL outward money (payouts/refunds) via the readiness gate. Unresolved external-money evidence globally affects readiness.
- Recommendation: Resolve through reconciliation or provide tenant scope so a single unresolved row does not freeze cross-tenant outward money.
- Complexity: L
- Dependencies: none
- Affected Modules: CommerceHub financial readiness gate, Payment reconciliation
- Source Documents: systems/commercehub/README.md:1162-1164; 04-payments.md:466; 06-security-identity.md:668
- Evidence: "Unmatched provider success webhook with no tenant scope blocks all outward money."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-041: CommerceHub currency mismatch in payout queue
- Severity: High
- Category: Risk
- Description: Non-EGP CommerceHub orders can land in the payout queue, bypassing the currency guard. Currency sovereignty must fail closed.
- Recommendation: Enforce EGP-only validation end-to-end; treat non-EGP as anomaly and route to manual review.
- Complexity: M
- Dependencies: none
- Affected Modules: CommerceHub finance, payout, currency authority
- Source Documents: systems/commercehub/README.md:1166-1168; 04-payments.md:468; 05-orders.md:714-715
- Evidence: "Non-EGP CommerceHub orders in payout queue. Business failure: currency guard bypassed."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-042: Settlement header count treated as membership truth
- Severity: High
- Category: Bug
- Description: A settlement header count matches expected order count but live settlement-order rows are missing/duplicated/deleted/foreign. Recorded metadata became settlement authority instead of live lineage.
- Recommendation: Fail closed to retry/reconciliation-required; live `PaymentSettlementOrders` lineage proves membership (override the older `OrderCount` pattern).
- Complexity: M
- Dependencies: none
- Affected Modules: CommerceHub settlement, Payments
- Source Documents: systems/commercehub/README.md:1174-1176; 05-orders.md:720-721, 845-850
- Evidence: "Settlement header count matches expected order count but live settlement-order rows are missing … recorded metadata became settlement authority."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-043: Provider success treated as payment finality
- Severity: High
- Category: Bug
- Description: A payment provider callback reports success but local payment/order lineage or lifecycle admission is missing. External evidence became payment truth.
- Recommendation: Callback remains evidence; persist unresolved cash; block retry/outward-money readiness; `IPaymentLifecycleService`/reconciliation admits or rejects the transition.
- Complexity: M
- Dependencies: ORD-040
- Affected Modules: Payment webhooks, CommerceHub reconciliation
- Source Documents: systems/commercehub/README.md:1178-1180; 05-orders.md:272-282
- Evidence: "Payment provider callback reports success but local payment/order lineage or lifecycle admission is missing. External evidence became payment truth."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-044: Vendor stale capability after suspension / debt block
- Severity: High
- Category: Governance Concern
- Description: A vendor can remain able to publish, accept orders, or operate protected CommerceHub surfaces after suspension, ban, or COD debt block. Stale frontend/session capability became operating authority.
- Recommendation: Backend authority loss must purge/revalidate protected presentation; new-order/public visibility and operating capabilities fail closed.
- Complexity: L
- Dependencies: ORD-039
- Affected Modules: CommerceHub vendor eligibility, search, publication
- Source Documents: systems/commercehub/README.md:1182-1184
- Evidence: "Vendor remains able to publish, accept orders, or operate protected CommerceHub surfaces after suspension, ban, or COD debt block."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-045: Affiliate multi-attribution ambiguity
- Severity: High
- Category: Validation Gap
- Description: Analytics first-touch / last-click UI, repeated clicks, query strings, or multiple cookies decide the winning affiliate instead of backend protected evidence + current SQL attribution policy. Ambiguous candidates should fail closed.
- Recommendation: Enforce one canonical attribution via backend protected evidence + current SQL attribution policy; fail closed on ambiguity.
- Complexity: M
- Dependencies: none
- Affected Modules: Affiliate attribution, Storefront checkout
- Source Documents: systems/affiliate/README.md:902-906
- Evidence: "analytics first-touch, last-click UI, repeated clicks, query strings, or multiple cookies decide the winning affiliate."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-046: No fraud engine for affiliate (self-referral, velocity, blacklist)
- Severity: High
- Category: Missing Feature
- Description: No fraud engine exists (self-referral, duplicate-click, velocity, blacklist, risk score absent). Combined with affiliate/customer identity collision and suspended-affiliate re-registration, commission fraud is under-defended.
- Recommendation: Build a fraud runtime (self-referral detection, duplicate-click, velocity, blacklist, risk score) and revalidate identity/customer/tenant/account relationships on every attribution.
- Complexity: XL
- Dependencies: ORD-047, ORD-048
- Affected Modules: Affiliate, Customer Trust, Identity
- Source Documents: platform/affiliate-participation-runtime-final-architecture.md:702, 728; platform/affiliate-platform-final-architecture.md:442, 448; systems/affiliate/README.md:908-936
- Evidence: "No fraud engine (self-referral, duplicate-click, velocity, blacklist, risk score absent)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-047: Affiliate/customer identity collision under-defended
- Severity: Medium
- Category: Security Concern
- Description: A valid affiliate identity + a valid customer identity are treated as proof that the same/related actor can earn commission. Security/Identity and Affiliate authority must keep actor domains isolated.
- Recommendation: Enforce actor-domain isolation; block attribution/commission on collision or ambiguous ownership.
- Complexity: M
- Dependencies: ORD-046
- Affected Modules: Affiliate, Identity, Customer
- Source Documents: systems/affiliate/README.md:914-918
- Evidence: "a valid affiliate identity and a valid customer identity are treated as proof that the same or related actor can earn commission."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-048: Suspended affiliate re-registration bypass
- Severity: Medium
- Category: Governance Concern
- Description: A suspended/closed/deleted/fraud-marked affiliate can regain capability through re-registration, alternate identity, or stale session state.
- Recommendation: Backend identity/governance authority decides current capability; ambiguous or linked-risk re-registration fails closed or requires governed review.
- Complexity: M
- Dependencies: ORD-046
- Affected Modules: Affiliate, Identity/Governance
- Source Documents: systems/affiliate/README.md:932-936
- Evidence: "a suspended, closed, deleted, or fraud-marked affiliate regains capability through re-registration, alternate identity, or stale session state."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-049: Stale affiliate link survives account/product lifecycle loss
- Severity: Medium
- Category: Validation Gap
- Description: A copied URL/cached link continues to create affiliate context after affiliate suspension, link deactivation, product archive/delete/hide, invalid category, missing price, or stock loss.
- Recommendation: Every use re-enters backend admission and fails closed on stale context.
- Complexity: M
- Dependencies: none
- Affected Modules: Affiliate, Storefront, Catalog
- Source Documents: systems/affiliate/README.md:920-924
- Evidence: "a copied URL or cached link continues to create affiliate context after affiliate suspension …"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-050: POS Customer opening-balance outbox asymmetry undocumented
- Severity: Medium
- Category: Documentation Inconsistency
- Description: Customer opening balance (SOA path) does NOT enqueue an accounting outbox row, unlike the supplier opening-balance path. Sale and Refund do enqueue outbox rows. This asymmetry is easy to miss and can lead to incorrect assumptions about GL posting for customer OB.
- Recommendation: Document the asymmetry; confirm it is intended (customer OB may not require GL posting) or add the outbox if GL symmetry is required.
- Complexity: S
- Dependencies: none
- Affected Modules: POS Customer, LedgerService, Accounting outbox
- Source Documents: capabilities/pos-admin/pos-customer-management.md:521-523, 552-553; master-data/MD-03-CUSTOMER-MASTER.md:213-222
- Evidence: "Customer opening balance (SOA path) does NOT enqueue an accounting outbox row, unlike the supplier opening-balance path."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ORD-051: POS Customer has no Customer Groups / Types / Credit Limit / Loyalty / Merge / Archive
- Severity: Medium
- Category: Missing Feature
- Description: POS `Customer` is an identity aggregate only. No Customer Code, Groups, Types, Credit Limit, credit-sale gating, standalone customer payment endpoint, payment allocation engine, customer merge, or projections/events. Any doc implying these exists is wrong.
- Recommendation: Roadmap the genuinely required capabilities (e.g., credit limit/gating if credit sales are offered); treat the rest as out-of-scope and correct misleading docs.
- Complexity: L
- Dependencies: ORD-052
- Affected Modules: POS Customer master, LedgerService
- Source Documents: capabilities/pos-admin/pos-customer-management.md:509-525, 550-553; master-data/MD-03-CUSTOMER-MASTER.md:286-367
- Evidence: "Any doc implying POS Customer has Customer Groups, Credit Limit, Credit Days, Loyalty, Merge, Import/Export, or Archive/Inactive status would be wrong — none exist in code."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-052: No deferred-payment / later-settlement workflow for customer receivable
- Severity: High
- Category: Missing Feature
- Description: There is NO implemented "receive payment against outstanding balance" workflow. The SOA "receive payment" UI (`POST /Payments/receive`) has no server implementation (404). The only way customer credit appears is at sale time. The receivable lifecycle stops at read-only balance; settlement-to-zero is not wired end-to-end.
- Recommendation: Implement a customer-ledger settlement workflow (receive-payment controller + ledger journal + receivable closure) or remove the misleading UI button.
- Complexity: L
- Dependencies: ORD-051
- Affected Modules: POS Customer, StatementOfAccount, Payments
- Source Documents: platform/FLOW-02-deferred-payment-credit-sale-canonical.md:67-111
- Evidence: "There is no implemented 'receive payment against outstanding balance' workflow … POST /Payments/receive has no controller … The lifecycle stops at 'read-only balance.'"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-053: POS Customer "set inactive instead" advice is unactionable
- Severity: Low
- Category: Documentation Inconsistency
- Description: Delete-guard messages advise "set the customer as inactive instead," but there is NO inactive/archive status on `Customer`. The advice cannot be actioned as described.
- Recommendation: Remove/replace the inactive advice; document that only soft-delete (IsDeleted) exists.
- Complexity: S
- Dependencies: none
- Affected Modules: POS Customer delete guard
- Source Documents: capabilities/pos-admin/pos-customer-management.md:512-514, 550-551; master-data/MD-03-CUSTOMER-MASTER.md:336-337
- Evidence: "The phrase 'set the customer as inactive instead' appears in delete-guard messages but there is no inactive/archive status on Customer."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ORD-054: `Customer.Balance` shadow vs ledger authority confusion
- Severity: Medium
- Category: Documentation Inconsistency
- Description: `Customer.Balance` is a non-authoritative denormalized shadow/cache recomputed from `AccountLedger`. Any doc treating `Customer.Balance` as truth is incorrect; `AccountLedger` aggregation is the financial authority.
- Recommendation: Document the shadow-vs-authority split; add a guard/test asserting balance reconciliation.
- Complexity: S
- Dependencies: none
- Affected Modules: POS Customer, LedgerService
- Source Documents: capabilities/pos-admin/pos-customer-management.md:515-517, 552; master-data/MD-03-CUSTOMER-MASTER.md:328-331, 350
- Evidence: "Customer.Balance is a compatibility shadow; the authoritative balance is AccountLedger."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ORD-055: Customer Trust Runtime is CRUD + synchronous guard, not CQRS; no trust/reputation/warnings/appeals
- Severity: Medium
- Category: Missing Feature
- Description: The "Customer Trust" runtime has no trust concept (only governance + phone-risk), no reputation score (advisory `RiskLevel` only), no warnings, no appeal workflow, no temporary/expiring blocks, no analytics/reporting, no integration events, no Mongo/ES read models. "Trust Score"/"Risk Score" are enums only. It is a CRUD + synchronous guard, not a full trust runtime.
- Recommendation: If a trust/reputation/warning/appeal product is required, build those as first-class capabilities; otherwise rename the doc to avoid implying a trust runtime.
- Complexity: XL
- Dependencies: ORD-046
- Affected Modules: Customer Trust / Governance, Phone Risk
- Source Documents: platform/customer-trust-runtime-final-architecture.md:550-564, 283-296, 311-353
- Evidence: "Customer Trust Runtime exists → No trust concept; only governance + phone-risk. Warning Display: not implemented. Appeal workflow: Not implemented."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-056: Order velocity signal logged but not enforced
- Severity: Medium
- Category: Known Limitation
- Description: `OrderVelocityService` tracks order velocity (IP/phone counts, 300s window, thresholds 5/3) in Redis as a soft abuse signal, but it is logged, NOT enforced — no guard throws on velocity. It is a read-only assessment.
- Recommendation: Decide whether velocity should hard-block; if so, add an enforced guard; otherwise document it as advisory only.
- Complexity: S
- Dependencies: none
- Affected Modules: Customer Trust, Order velocity, abuse prevention
- Source Documents: platform/customer-trust-runtime-final-architecture.md:322-324
- Evidence: "logged but not enforced (grep shows it is read for assessment only; no guard throws on velocity)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ORD-057: Refunded orders still emit PurchaseCompleted (analytics double-count)
- Severity: Medium
- Category: Bug
- Description: `AuthoritativePurchaseAnalyticsConsumer` includes Refunded/PartiallyRefunded in the emitted set, so a refunded order still produces a purchase signal to GA/Meta and an OrderAttribution — potentially double-counting revenue vs the ledger (which reverses it). Divergence between analytics purchase signal and net revenue.
- Recommendation: Exclude Refunded from PurchaseCompleted emission, or net against the ledger.
- Complexity: S
- Dependencies: none
- Affected Modules: Tracking/Attribution analytics, Payments
- Source Documents: platform/tracking-attribution-runtime-final-architecture.md:707, 724
- Evidence: "a refunded order still produces a purchase signal to GA/Meta and an OrderAttribution, potentially double-counting revenue vs the ledger."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

---

## DOCUMENTATION CONFLICTS

### CONFLICT: Storefront homepage authority — `StoreConfig.pages[home]` vs `HomepageSection`
- Conflicting Documents: platform/storefront-builder-runtime-final-architecture.md + capabilities/content-management/storefront-content-management.md vs systems/storefront/README.md (HomePage.tsx runtime)
- Explanation: POS Admin Storefront Builder publishes homepage sections into `StoreConfig.CustomizationJson.pages[home]` (projected to Mongo `StorefrontConfigDocument.Pages.Home`), but the public homepage SPA renders `/api/homepage/sections` (Super Admin `HomepageSection` content or fallback hero). Tenant admins believe their homepage edits are live while customers see different content.
- Possible Cause: Two homepage-authoring surfaces evolved separately; Super Admin owns global/marketplace homepage sections, POS Admin owns tenant page builder, but the public render binds only to Super Admin sections.
- Recommended Resolution: Pick ONE canonical homepage authority — either make `HomePage.tsx` render `config.pages.home.sections` (retiring the Super Admin homepage engine for tenant homepages) or disable/hide `page=home` in POS Admin builder and fix copy/API comments. Add an architecture test asserting the chosen runtime source, plus user-facing publish feedback naming the actual surface. (Priority: High — confirmed divergence.)
- Evidence: storefront-content-management.md:446-462; storefront-builder-runtime-final-architecture.md:380-396, 775-787, 901-902.

### CONFLICT: Carrier runtime authority — `CarrierConfiguration` vs `CarrierAccount`
- Conflicting Documents: capabilities/commerce/shipping-management.md vs platform/shipping-fulfillment-runtime-final-architecture.md (both describe the same two-table divergence)
- Explanation: Two docs independently confirm the same conflict: checkout/quotes/CommerceHub/shipment-registration/webhook trust read legacy `CarrierConfiguration`, while storefront warehouse-scoped visibility reads `CarrierAccount`. The docs agree on the divergence but neither is "wrong" — the conflict is in the CODE (two authorities), and the docs disagree on the remedy (converge vs demote).
- Possible Cause: A newer lifecycle/governance aggregate (`CarrierAccount`) was added without fully rewiring runtime readers off the legacy `CarrierConfiguration`.
- Recommended Resolution: Choose one runtime authority and converge all readers (see ORD-011). Until then, document both as separate authorities and block UI from presenting one as the other's source of truth.
- Evidence: shipping-management.md:290-295, 363-459; shipping-fulfillment-runtime-final-architecture.md:648-660, 849-851, 859-860.

### CONFLICT: `Order` TenantId existence — code vs `AdminOrderService` header comment
- Conflicting Documents: capabilities/pos-admin/order-management.md (doc validation) vs the `AdminOrderService` source comment
- Explanation: The `AdminOrderService` class header states Order has NO TenantId column and isolation is via ownership joins only. The code shows `Order` implements `ITenantOwned` with a non-null `TenantId` column enforced at DB level. (Implementation-backed doc vs stale source comment — the comment is the false party.)
- Possible Cause: Code evolved to add a TenantId column; the header comment was never updated.
- Recommended Resolution: Correct the `AdminOrderService` comment; keep ownership joins as defense-in-depth but document the real TenantId column. (See ORD-023.)
- Evidence: order-management.md:729-733.

### CONFLICT: Checkout service name — order-authority checkout (`CheckoutService`) vs lead-capture `CheckoutSession`
- Conflicting Documents: capabilities/pos-admin/checkout-management.md vs 05-orders.md (prior to the capability file)
- Explanation: Before the checkout capability file, the split between the order-authority checkout (`CheckoutService`, which creates the order + payment intent) and the lead-capture `CheckoutSession` (NOT an order) was not made explicit, causing frequent wrong assumptions. The capability file corrects this; older docs understate the distinction.
- Possible Cause: Two distinct concepts (order-producing checkout vs abandoned-cart lead capture) were conflated in prose.
- Recommended Resolution: Keep the dedicated checkout capability file as the source of truth; cross-reference it from `05-orders.md`.
- Evidence: checkout-management.md:631-687.

### CONFLICT: Settlement membership — `PaymentSettlementBatch.OrderCount` vs live `PaymentSettlementOrders` lineage
- Conflicting Documents: 05-orders.md (PASS 4 correction) vs older order/finance prose + systems/commercehub/README.md
- Explanation: Older design trusted the denormalized `PaymentSettlementBatch.OrderCount` as settlement-membership proof. The authoritative PASS 4 correction overrides it: current live `PaymentSettlementOrders` lineage is the only membership authority. `05-orders.md` resolves toward the correction, but the README's "settlement count treated as membership truth" scenario flags the residual risk if any path still trusts the count.
- Possible Cause: A metadata field was historically treated as authoritative before lineage-based membership was enforced.
- Recommended Resolution: Enforce live-lineage membership everywhere; fail closed on count-only proof. (See ORD-042.)
- Evidence: 05-orders.md:720-721, 845-850; systems/commercehub/README.md:1174-1176.

### CONFLICT: Storefront marketplace read model source — `CommerceHubMarketplaceOrder` vs storefront `Order`
- Conflicting Documents: _scratch/order-cqrs-crossdomain.md (§7) vs CommerceHub order projection docs
- Explanation: The customer-facing `marketplace_order_read_models` Mongo doc is built from the STOREFRONT `Order` (keyed by storefront `Order.Id`), NOT from the native `CommerceHubMarketplaceOrder` entity (which is projected into a separate `commercehub_orders` collection). If a storefront Order is soft-deleted but a marketplace order still exists, the marketplace read model can linger (no auto-delete), diverging from product/homepage paths that do delete.
- Possible Cause: Two read models for the same commerce concept were built from different source entities.
- Recommended Resolution: Define the canonical source for the marketplace read model; add cleanup/reconciliation when the storefront Order is tombstoned.
- Evidence: order-cqrs-crossdomain.md:67-73, 97.

### CONFLICT: CommerceHub-native affiliate commission authority — deferred vs implied
- Conflicting Documents: platform/commercehub-final-architecture.md (Missing Capabilities) vs affiliate platform docs
- Explanation: CommerceHub has no native affiliate commission authority in code (deferred to the separate Affiliate certification/runtime). Docs that imply CommerceHub owns commission authority are incorrect; commission is owned by the Affiliate runtime reading snapshots.
- Possible Cause: Marketplace and affiliate monetization are related but owned by separate runtimes; prose can blur the boundary.
- Recommended Resolution: Keep CommerceHub docs explicit that commission authority is deferred to the Affiliate runtime; cross-reference the affiliate canonical doc.
- Evidence: commercehub-final-architecture.md:867-872.

### CONFLICT: Customer identity domains — POS `Customer` vs `OnlineCustomer` vs `CommerceHubCustomer` vs `Affiliate`
- Conflicting Documents: master-data/MD-03-CUSTOMER-MASTER.md + capabilities/pos-admin/pos-customer-management.md vs storefront/CommerceHub customer docs
- Explanation: Four separate customer identities exist with NO mapping/merge code. POS `Customer` ≠ `OnlineCustomer` (storefront) ≠ `CommerceHubCustomer` (marketplace) ≠ `Affiliate`. Docs that conflate them (or that imply a unified customer) are wrong. This also intersects ORD-032 (OnlineCustomerId stamped on orders).
- Possible Cause: Each bounded context evolved its own identity; no cross-domain customer resolution was built.
- Recommended Resolution: Document the four identities as separate; add a cross-reference map; decide whether a unified customer view is a required product feature.
- Evidence: MD-03-CUSTOMER-MASTER.md:339-342, 540-541; pos-customer-management.md:518-520, 541.

### CONFLICT: "Receivable / receive payment" workflow exists in UI but not in backend
- Conflicting Documents: platform/FLOW-02-deferred-payment-credit-sale-canonical.md (implementation finding) vs SOA "receive payment" UI (`Payments.jsx`)
- Explanation: The SOA UI exposes a "receive payment" button → `POST /Payments/receive`, but the controller is NOT implemented (404). The receivable lifecycle is read-only; settlement-to-zero is not wired. Docs/UI imply a closed loop that does not exist.
- Possible Cause: UI was built ahead of the backend settlement workflow, or the workflow was deprioritized.
- Recommended Resolution: Implement the receive-payment workflow or remove the UI button and document the gap. (See ORD-052.)
- Evidence: FLOW-02:74-99, 111.

### CONFLICT: TikTok / pixel providers enumerated but absent (phantom providers)
- Conflicting Documents: platform/tracking-attribution-runtime-final-architecture.md (enum/config present, dispatcher absent) vs provider UI/config
- Explanation: `PixelProvider.TikTok` and other pixel providers (Snapchat, LinkedIn, Pinterest, GTM, Custom) are present in enums/config but have NO dispatcher/implementation. Only GA4 + Meta CAPI are real. The enum implies support that does not exist.
- Possible Cause: Provider enum was expanded ahead of dispatcher implementation; TikTok is a confirmed phantom.
- Recommended Resolution: Remove or implement the TikTok dispatcher; prune phantom provider enums or clearly mark them unimplemented.
- Evidence: tracking-attribution-runtime-final-architecture.md:192-202, 705, 761.

### CONFLICT: Service Marketplace "provider/consumer" framed as cross-tenant, but is intra-tenant
- Conflicting Documents: platform/service-marketplace-participation-runtime-final-architecture.md (Documentation Validation) vs prompt assumptions / possibly older docs
- Explanation: The prompt assumed an "independent POS Admin tenant provides services to another independent POS Admin tenant." Implementation shows provider and consumer are users INSIDE THE SAME tenant; cross-tenant reach is discovery visibility only. Several assumed entities/states (separate Provider entity, buyer entity, provider lifecycle enum, managed Service Categories) do not exist.
- Possible Cause: Marketing/external framing of "marketplace" vs the actual intra-tenant B2B model.
- Recommended Resolution: Correct docs/assumptions to intra-tenant service exchange with globally-discoverable listings; remove non-existent entity/state references.
- Evidence: service-marketplace-participation-runtime-final-architecture.md:740-769.

### CONFLICT: CMS "Homepage divergence" vs "Storefront homepage divergence" — same root, two doc families
- Conflicting Documents: platform/cms-runtime-final-architecture.md (homepage authority divergence, item 6) vs capabilities/content-management/storefront-content-management.md (System A divergence) vs platform/storefront-builder-runtime-final-architecture.md
- Explanation: Three docs describe the SAME homepage-authority divergence (StoreConfig vs HomepageSection) from CMS, content-management, and storefront-builder perspectives. They agree on the divergence but it is a single architectural issue tracked in three places, risking inconsistent remediation ownership.
- Possible Cause: Homepage rendering spans CMS + content-management + storefront-builder; each doc audits its slice.
- Recommended Resolution: Consolidate into one canonical homepage-authority decision (see CONFLICT 1) owned cross-functionally.
- Evidence: cms-runtime-final-architecture.md:865-867, 887; storefront-content-management.md:446-462; storefront-builder-runtime-final-architecture.md:775-787.

### CONFLICT: Best-sellers ranking claimed vs NOT implemented
- Conflicting Documents: platform/search-runtime-final-architecture.md (ranking table: Best-selling = NOT IMPLEMENTED, placeholder CreatedAt desc) vs storefront-builder/product docs implying best-sellers curation
- Explanation: Search ranking has a "Best-selling" row that is NOT implemented — it returns a placeholder `CreatedAt desc` instead of sales-volume ranking. Storefront/new-arrivals/best-sellers/cross-sell/up-sell/recently-viewed curation surfaces are also verified absent as dedicated recommendation engines.
- Possible Cause: Ranking feature was scoped but never built; placeholder masks the gap.
- Recommended Resolution: Implement sales-volume best-sellers or remove the ranking signal; document curation surfaces as absent.
- Evidence: search-runtime-final-architecture.md:737, 908; storefront-builder-runtime-final-architecture.md:547-562.

### CONFLICT: Conversational Commerce "Recommendation Runtime" implied but absent
- Conflicting Documents: platform/conversational-commerce-runtime-final-architecture.md (explicitly NOT a Recommendation/Agent/Discovery runtime) vs prompt/marketing vocabulary
- Explanation: The conversational commerce runtime is a single customer-facing chat surface that drafts intent and calls backend authorities; it is NOT a recommendation/agent/product-discovery runtime. Conversation persistence, memory, RAG/embeddings, tool/function-calling, streaming history, and live preview are NOT implemented. The prompt lists fields/capabilities that are absent.
- Possible Cause: "AI commerce" framing implies richer capabilities than the implemented single-model chat path.
- Recommended Resolution: Document the runtime as a thin LLM-orchestration surface only; mark absent capabilities explicitly; decide which (if any) to build.
- Evidence: conversational-commerce-runtime-final-architecture.md:127, 157, 477, 666, 684, 690.

### CONFLICT: Customer Trust "Runtime" naming vs actual CRUD+guard implementation
- Conflicting Documents: platform/customer-trust-runtime-final-architecture.md (title implies a Trust Runtime) vs its own Documentation Validation (no trust concept, no CQRS, advisory only)
- Explanation: The document is titled "Customer Trust / Moderation / Risk Runtime" but its own findings show there is no trust concept, no reputation score, no warnings/appeals, no events/projections — only synchronous governance + phone-risk guards (CRUD + guard). The name overstates the capability.
- Possible Cause: Aspirational naming; the runtime was scoped down to governance+risk only.
- Recommended Resolution: Rename to "Customer Governance & Phone-Risk Runtime" (or build the missing trust/reputation/warning/appeal capabilities). (See ORD-055.)
- Evidence: customer-trust-runtime-final-architecture.md:1, 550-564, 327-334.

---

## SECOND-PASS ADDITIONS (captured after first-pass list, to avoid omission)

The second pass re-scanned checkout-management, sales-management, payment-management, commerce-recovery, and the systems READMEs for any order/storefront/customer issues not yet captured. Findings:

- ORD-022 through ORD-034 (checkout/customer hidden behaviors + doc inconsistencies) and ORD-035 through ORD-057 (storefront/commercehub/affiliate/customer runtime issues) were surfaced in the second pass and are listed above.
- The POS `SalesManagement` "PaymentMethod.Credit and deferred sale execution fail closed" + "deferred sale execution is unsupported, not a planned feature" (sales-management.md:568-572) is a documentation correction within the POS Sales domain (out of primary Orders scope but relevant to customer deferred-payment, cross-referenced by ORD-052/FLOW-02).
- Commerce-recovery "Recovery Coupon / Campaign Coupon not implemented" + "recovered revenue not computed" (commerce-recovery-runtime-final-architecture.md:359, 437, 835) is a Storefront-adjacent gap; recorded as a known limitation of the recovery runtime (no dedicated ORD ID assigned as it is explicitly "out of scope by design / deferred," not a defect).

No additional HIGH/CRITICAL unresolved defects were found beyond those enumerated; remaining second-pass hits were confirmed documentation-consistency notes already represented as ORD-022–ORD-034 and the conflicts above.

---

## TOP PRIORITY SUMMARY (by severity + blast radius)

1. **ORD-040 / ORD-043** — Unresolved captured payment blocks ALL outward money + provider-success-as-finality (cross-tenant financial freeze risk). [High]
2. **ORD-011** — Dual carrier config authority divergence (checkout vs admin inconsistency). [High]
3. **ORD-042** — Settlement header count treated as membership truth (revenue integrity). [High]
4. **ORD-041** — CommerceHub non-EGP payout queue (currency sovereignty bypass). [High]
5. **ORD-037 / ORD-038** — Storefront return/refund bypass + stale session survival (money & auth authority). [High]
6. **ORD-039 / ORD-044** — CommerceHub vendor ban not suppressing listings + stale vendor capability (governance). [High]
7. **ORD-035** — Storefront missing-order projection → false absence. [High]
8. **ORD-046** — No affiliate fraud engine (commission fraud exposure). [High]
9. **ORD-052** — No deferred-payment / receive-payment workflow (broken receivable loop). [High]
10. **ORD-001 / ORD-002** — No canonical OrderStatus machine + FulfillmentMode immutability not in CI (lifecycle drift risk). [High]

**Totals:** 57 issues (9 High/architecture, 18 Medium, 14 Low, 16 Documentation-only) + 14 conflicts = 71 entries.

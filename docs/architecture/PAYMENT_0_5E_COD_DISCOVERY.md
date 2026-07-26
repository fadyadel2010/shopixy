# PAYMENT-0.5E COD Discovery

Cash On Delivery (COD) Workflow Discovery and Architecture Compatibility Certification

Date: 2026-07-10

Status: Discovery Only

Runtime changes: None

Certified references:
- `PAYMENT_0_PLATFORM_ARCHITECTURE_CERTIFICATION`
- `PAYMENT_0_5A_STRIPE_DISCOVERY`
- `PAYMENT_0_5B_PAYMOB_DISCOVERY`
- `PAYMENT_0_5C_FAWRY_DISCOVERY`
- `PAYMENT_0_5D_MANUAL_PAYMENT_DISCOVERY`
- `SHIPPING_PLATFORM_ARCHITECTURE_CERTIFICATION_FREEZE`
- `WAREHOUSE_FINAL_END_TO_END_DEVELOPMENT_CERTIFICATION_FREEZE`

## Executive Summary

COD can operate as a first-class provider inside the provider-neutral Payment Platform, but only when it is modeled as a fulfillment-driven payment provider, not as an online gateway and not as a manual payment workflow.

The current project already follows several PAYMENT-0-compatible principles:

- COD does not use payment webhooks.
- COD does not mark orders paid at checkout.
- Payment lifecycle confirmation is backend-owned.
- COD collection is blocked until fulfillment reaches `Delivered`.
- Shipping adapters can carry COD amounts and parse COD collection evidence.
- Settlement is represented as a payment-owned clearing process after collection.

However, the current implementation is not enterprise-complete for COD. The largest gap is that delivery confirmation and cash collection confirmation are collapsed in important paths. A delivered fulfillment event can trigger payment collection recognition for COD, while the separate shipping-side `ShipmentCodCollectedEvent` evidence path is not the apparent driver of payment confirmation.

This is acceptable for a simple happy-path COD model only if the business definition of `Delivered` means "delivered and cash collected." It is not sufficient for enterprise COD, where delivered, refused, not collected, partially collected, returned, and settled are separate operational and financial facts.

Certification decision: Compatible in concept, partially compatible in implementation, not fully certified as enterprise COD without workflow and authority migration.

## Current Implementation

### Payment Lifecycle

The current payment lifecycle contains COD-specific handling in `PaymentLifecycleService`.

Observed behavior:

- COD orders remain unpaid while pending delivery.
- `HandlePaymentResultAsync` admits COD success only through COD delivery handling.
- COD collection is rejected unless the order is a COD order.
- COD collection is rejected unless fulfillment status is `Delivered`.
- Successful COD collection sets the order payment status to `Collected`.
- The order becomes paid after collection, not at checkout.
- COD accounting and event publication are emitted after successful collection.
- Existing collected, settled, or paid COD orders are handled idempotently.

This is directionally compatible with PAYMENT-0 because Payment owns the payment lifecycle and final payment state. It does not require an online gateway.

### Checkout and Storefront

Checkout normalizes COD-like payment method names to `COD` and treats COD as an offline payment method. Current checkout comments and service paths indicate that COD orders are not automatically paid at order placement.

Storefront payment method APIs expose COD as an available payment method when enabled and include basic fields such as:

- Display name
- Description
- Minimum order amount
- Maximum order amount
- Fee
- Fee type
- Offline support

The storefront currently supports COD selection, but does not appear to provide a complete customer-facing COD lifecycle for awaiting delivery, cash collection, refusal, retry, return-to-origin, or post-delivery settlement states.

### Administration

Administration can enable and configure COD as a tenant payment method. COD requires no gateway configuration.

The payment lifecycle controller exposes:

- A COD delivered/collected action for an order.
- COD settlement recording.
- Allowed action metadata showing COD orders can be marked delivered.

Shipping administration surfaces COD information on shipments, including COD-only filtering and COD collected/pending display. This is useful operationally, but it is not the same as a complete payment-owned COD administration workflow.

### Shipping and Carrier Integration

Shipping supports COD at the carrier request level.

Observed behavior:

- Carrier configuration includes `SupportsCod`.
- Shipping quotes can filter carriers based on COD support.
- Shipment creation can include `IsCashOnDelivery` and `CodAmount`.
- Adapters such as Bosta, Aramex, and Mylerz serialize COD values for carrier requests.
- Carrier tracking or webhook parsing can detect COD collection.
- Shipments include COD fields such as `CodCollected` and `CodCollectedAtUtc`.
- A `ShipmentCodCollectedEvent` exists and can be published.

The missing connection is payment authority admission from dedicated COD collection evidence. Current payment collection appears to rely primarily on fulfillment delivered events, not on the shipping COD collection event.

### Warehouse and Fulfillment

Warehouse and fulfillment boundaries are mostly preserved.

Observed behavior:

- Warehouse owns fulfillment readiness, reservation, allocation, picking, and packing.
- Shipping owns shipment registration, carrier assignment, and tracking evidence.
- Fulfillment service owns fulfillment status transitions.
- Payment lifecycle consumes fulfillment evidence and owns payment state.
- Fulfillment return handling explicitly does not create refund authority.

This aligns with the certified warehouse and shipping architecture freezes.

## Workflow Analysis

### Expected Enterprise COD Workflow

The enterprise COD workflow should be:

1. Customer selects COD.
2. Payment creates a COD payment session or COD payment obligation.
3. Inventory is reserved.
4. Order is placed with payment pending collection.
5. Warehouse processes the order.
6. Shipment is created with COD amount and collection requirement.
7. Shipment is assigned to carrier or driver.
8. Shipment goes out for delivery.
9. Customer receives shipment and pays cash.
10. Driver or carrier records cash collection evidence.
11. Shipping records delivery and collection evidence.
12. Payment admits collection evidence and marks payment collected.
13. Cash is reconciled and settled.
14. Payment records settlement.
15. Accounting recognizes the correct clearing, fee, and settlement entries.

### Current Workflow Fit

| Workflow Step | Current Support | Assessment |
|---|---:|---|
| Create COD session | Partial | COD payment method/order intent exists, but no explicit COD session identity is consistently modeled. |
| Reserve inventory | Supported by checkout/order flow | Not COD-specific; compatible if reservation remains order/inventory-owned. |
| Place order | Supported | COD order remains unpaid/pending. |
| Warehouse processing | Supported by warehouse architecture | Payment does not own warehouse decisions. |
| Shipment creation | Supported | COD amount can be sent to carriers. |
| Shipment assignment | Supported by shipping platform | Shipping owns carrier/assignment authority. |
| Out for delivery | Supported by shipping/fulfillment states | Payment remains pending. |
| Delivered | Supported | Fulfillment can mark delivered. |
| Cash collection | Partial | Collection is inferred from delivery in key paths; dedicated collection evidence is not the payment driver. |
| Payment confirmation | Partial | Payment owns final state, but source evidence is too broad when delivery equals collection. |
| Settlement | Supported | COD settlement batch has validation and idempotency. |
| Cancellation | Partial | Order/fulfillment cancellation exists; COD-specific refused/RTO cancellation path is incomplete. |
| Refund | Partial | Payment refunds exist, and fulfillment does not own refund authority; COD-specific collected-vs-not-collected refund rules need design. |
| Audit | Partial | Events/outbox exist; complete immutable COD chain-of-custody audit is missing. |

## Architecture Compatibility

COD is compatible with PAYMENT-0 as a provider-neutral payment provider when represented as:

- A backend-owned payment method.
- A backend-owned payment session or obligation.
- A payment lifecycle whose completion depends on fulfillment and collection evidence.
- A provider without online gateway redirects or payment webhooks.
- A settlement process that clears collected cash.

The current project satisfies the broad architecture shape but has workflow gaps that prevent full certification.

Compatible areas:

- Payment owns payment state.
- Shipping owns shipment evidence.
- Warehouse owns fulfillment preparation.
- COD webhooks are blocked.
- COD is not treated as Stripe, Paymob, Fawry, or manual wallet.
- Settlement is payment-owned.

Incompatible or incomplete areas:

- Delivery confirmation and collection confirmation are not separated enough.
- `ShipmentCodCollectedEvent` is not clearly admitted by Payment as the canonical collection confirmation.
- COD-specific lifecycle states are incomplete.
- Administration lacks a complete COD collection and exception workflow.
- Eligibility rules are minimal.
- Cash handling fraud controls are incomplete.

## Authority Analysis

### Payment Authority

Payment should own:

- COD payment session or obligation.
- Payment pending state.
- Collection admission.
- Collected, settled, refunded, cancelled, expired, and rejected payment states.
- COD payment audit.
- Settlement batches.
- Accounting outbox for COD collection and settlement.
- Refund authority for already-collected COD payments.

Current implementation mostly follows this. Payment owns the final collected and settled states.

### Shipping Authority

Shipping should own:

- Shipment creation.
- Carrier selection and registration.
- Tracking events.
- Delivery evidence.
- Carrier/driver collection evidence as evidence.
- Delivery failure, refusal, and return-to-origin evidence.

Shipping must not own final payment status. Current implementation mostly respects this, but the architecture should ensure shipping evidence is admitted by Payment instead of shipping directly implying financial finality.

### Order Authority

Order should own:

- Order identity.
- Order lifecycle.
- Order cancellation.
- Customer/order-level state presentation.

Order must not own collection finality or cash settlement.

### Warehouse Authority

Warehouse should own:

- Reservation coordination.
- Allocation.
- Picking.
- Packing.
- Handoff readiness.

Warehouse must not own shipping execution, payment state, or settlement.

### Driver or Carrier Authority

Driver or carrier should own only physical-world evidence:

- Delivery attempt result.
- Cash received evidence.
- Refusal evidence.
- Partial collection evidence, if allowed.
- Return-to-origin evidence.

Driver or carrier must not be the final authority for platform payment state.

### Administration Authority

Administration should own controlled review actions through backend APIs:

- Confirm collection when trusted evidence is available.
- Reject collection.
- Record failure reason.
- Reconcile settlement.
- Resolve disputes.

Administration should not mutate raw payment, shipping, or warehouse state outside the owning services.

## Shipping Integration

The shipping integration is partially compatible.

Strengths:

- Carriers can be marked as COD-capable.
- Shipping quote flow can account for COD support.
- Carrier requests include COD amount and COD flags.
- Adapters understand COD-specific carrier fields.
- Tracking parse results can include COD collection evidence.
- Shipment entities preserve COD collection fields.

Gaps:

- Payment confirmation is not clearly driven by `ShipmentCodCollectedEvent`.
- Fulfillment delivered events can trigger COD payment collection.
- Delivered, collected, refused, failed delivery, and return-to-origin need separate canonical facts.
- Carrier collection evidence should be admitted by Payment through a provider-neutral collection evidence pipeline.
- Shipping should remain evidence producer, not payment authority.

Authority-safe target model:

1. Shipping records delivery and collection evidence.
2. Shipping publishes immutable evidence events.
3. Payment validates the evidence against COD payment obligation, order total, currency, tenant, shipment, and current lifecycle state.
4. Payment marks collected only after successful admission.
5. Settlement later clears collected cash.

## Capability Matrix

| Capability | Current Support | Notes |
|---|---:|---|
| Supports cash collection | Partial | COD amount can be sent to carriers; collection confirmation is not independently modeled enough. |
| Supports delivery confirmation | Yes | Fulfillment and shipping lifecycle support delivered state. |
| Supports collection confirmation | Partial | Payment can mark collected, but delivered event is too strong as collection proof. |
| Supports return | Partial | Fulfillment return exists; COD-specific financial handling needs design. |
| Supports customer refusal | Partial | Failed delivery/return paths exist, but COD refusal is not first-class payment state. |
| Supports COD fee | Yes | Tenant payment method has fee and fee type. |
| Supports settlement | Yes | COD settlement batch exists with validation. |
| Supports audit | Partial | Events and outbox exist; chain-of-custody audit is incomplete. |
| Supports notifications | Partial | General order/payment/shipping notifications may exist; COD-specific state messaging is incomplete. |
| Supports retry | Partial | Failed delivery can indicate retry; payment COD retry semantics are incomplete. |
| Supports partial collection | No | No first-class amount-specific COD collection lifecycle. |
| Supports regional restrictions | Minimal | Carrier COD support and payment method enablement exist; region/country/product/customer rules are incomplete. |
| Supports driver reconciliation | No | No complete cash custody or driver deposit workflow observed. |

## Provider Constraints

COD-specific constraints:

- Requires physical delivery.
- Requires cash collection at destination.
- Requires successful fulfillment before payment completion.
- Requires shipment, carrier, or driver evidence.
- Requires collection reconciliation.
- Allows customer refusal.
- Allows failed delivery.
- May require return-to-origin.
- May require partial collection handling in some markets.
- Has elevated fraud and cash custody risk.
- Has region, carrier, product, customer, and amount restrictions.
- Has no online gateway redirect.
- Has no payment webhook.

Current implementation recognizes several of these constraints, especially the lack of payment webhook and the fulfillment dependency. It does not yet model the full exception and cash custody surface.

## Canonical Runtime Mapping

| PAYMENT-0 Canonical Concept | COD Mapping | Current Project Mapping |
|---|---|---|
| Payment Method | Cash On Delivery | Tenant payment method code `COD`. |
| Payment Provider | Fulfillment-driven COD provider | Implemented as COD lifecycle behavior, not separate gateway adapter. |
| Payment Session | COD payment session or payment obligation | Order-bound payment intent/session concept; explicit COD session identity is incomplete. |
| Provider Session ID | COD obligation ID, shipment COD obligation, tracking-linked collection instruction | No consistent first-class COD session ID observed. |
| Customer Action | Receive shipment and pay cash | Storefront places COD order; customer lifecycle messaging incomplete. |
| Provider Confirmation | Cash collected evidence | Currently often represented by delivered fulfillment event plus admin action; target should be dedicated collection evidence. |
| Async Event | Delivery and collection evidence | Fulfillment delivered event exists; shipment COD collected event exists but is not canonical payment trigger. |
| Payment Success | Collection admitted by Payment | `PaymentStatus.Collected`. |
| Settlement | Cash settlement batch | `RecordCodSettlementAsync` and settlement batch. |
| Refund | Return refund or reversal after collected cash | Generic refund lifecycle exists; COD-specific return/refund rules incomplete. |
| Cancellation | Cancel before collection or after failed/refused delivery | Generic order/payment cancellation exists; COD-specific cancellation states incomplete. |
| Webhook | None for Payment | COD payment webhooks are blocked. Shipping webhooks are evidence only. |

## Current Gaps

### Critical and High Gaps

1. Delivery and cash collection are collapsed.

   A COD payment can be collected from a fulfillment delivered event. Enterprise COD requires delivery evidence and cash collection evidence to be separately represented and admitted.

2. Shipping COD collection evidence is not the canonical payment confirmation path.

   Shipment COD fields and `ShipmentCodCollectedEvent` exist, but payment lifecycle confirmation appears to be driven by fulfillment delivered handling rather than dedicated collection evidence.

3. COD lifecycle states are incomplete.

   Required enterprise states include awaiting shipment, awaiting delivery, awaiting collection, collected, customer refused, not collected, partial collection, return-to-origin, returned, cancelled, refunded, and settled. Current payment state coverage is too generic and inconsistent between `Pending` and `CashOnDelivery`.

4. Partial collection is unsupported.

   Settlement validation assumes full order total collection. There is no amount-specific collection confirmation model.

5. COD administration is incomplete.

   Current administration has actions and settlement APIs, plus shipping displays, but not a full COD operations console for pending collection, not collected, refused, RTO, discrepancy, settlement, and audit review.

### Medium Gaps

6. Collection confirmation authority is broad.

   The COD delivered endpoint is available at controller role level including Manager. Since the action recognizes payment collection, this should be reviewed against financial authority policy.

7. Allowed action visibility is too loose.

   `CanMarkCodDelivered` appears to depend on COD and pending payment status, while the actual mutation rejects if fulfillment is not delivered. This can expose an action before it is valid.

8. Eligibility rules are minimal.

   COD availability supports enablement, amount limits, fees, and carrier COD support. Country, region, shipping method, product, merchant, customer risk, and address restrictions are not complete.

9. Customer experience is incomplete.

   Storefront can select COD, but customer-facing status and exception handling for awaiting collection, refusal, retry, RTO, and cancellation are incomplete.

10. Comments and older payment service paths contain stale COD language.

   Some comments describe COD as confirming order immediately or skipping payment in ways that should be reconciled with the certified architecture during a later cleanup phase.

## Security Review

Current positive controls:

- COD payment webhooks are blocked.
- COD cannot be collected before delivered status in payment lifecycle.
- Non-COD orders cannot be marked collected through COD flow.
- COD settlement validates order list, currency, amount, fee bounds, duplicate order IDs, tenant consistency, and accounting readiness.
- Settlement has idempotency/content checks.
- Fulfillment return flow does not create refund authority.

Security risks:

- Delivery evidence can become payment collection evidence without an independent cash collection confirmation.
- Collection confirmation endpoint may be available to roles that should not have final financial authority.
- No complete immutable cash custody chain was observed.
- No dedicated evidence hash, driver identity, collection receipt, or handoff reconciliation model was observed.
- No first-class discrepancy state for collected amount mismatch.
- No partial collection controls.
- No duplicate collection prevention beyond payment idempotency.
- No complete exception audit for refusal, no-cash delivery, RTO, or driver cash shortage.

Security alignment is partial, not complete.

## Fraud Review

Primary fraud risks:

- Driver reports delivery without cash collection.
- Carrier reports delivered while customer refused payment.
- Cash is collected but not handed over.
- Partial cash is collected but full order is marked collected.
- Duplicate collection attempt after payment state changes.
- Manager or operator confirms collection without sufficient evidence.
- Customer claims cash paid while carrier reports not collected.
- Carrier webhook or tracking event is trusted without reconciliation.
- Settlement batch includes incorrect order set.
- COD amount differs from order total due to discount, shipping fee, or fee drift.

Current mitigation:

- COD collection requires delivered fulfillment status.
- Settlement validates order amount, currency, duplicate IDs, and collected state.
- Accounting outbox and events create some audit trail.

Missing mitigation:

- Dedicated collection evidence model.
- Driver/carrier custody trail.
- Refusal and not-collected states.
- Partial collection model.
- Discrepancy resolution workflow.
- Role-specific collection approval policy.
- Immutable audit chain for cash movement.

## Administration Review

Current administration support:

- Enable or disable COD payment method.
- Configure COD display information.
- Configure minimum and maximum order amount.
- Configure fee and fee type.
- View shipping-side COD status on shipment screens.
- Mark COD delivered/collected through payment lifecycle.
- Record COD settlement batches.

Missing administration support:

- Dedicated COD orders queue.
- Pending collection filter.
- Awaiting delivery filter.
- Not collected filter.
- Collected but unsettled filter.
- Settled filter.
- Customer refused filter.
- Return-to-origin filter.
- Partial collection filter.
- Collection failure reason.
- Driver or carrier collection reconciliation.
- Cash handoff/deposit workflow.
- Discrepancy review.
- Evidence viewer.
- Immutable audit timeline.
- COD-specific reporting.

Administration is not complete for enterprise COD.

## Eligibility Review

Current eligibility support:

- Tenant-level COD enablement.
- Minimum order amount.
- Maximum order amount.
- COD fee.
- Carrier `SupportsCod` filtering for shipping quotes.

Missing eligibility dimensions:

- Country restrictions.
- Region/city/zone restrictions.
- Shipping method restrictions beyond carrier-level support.
- Product restrictions.
- Merchant restrictions.
- Customer risk restrictions.
- Address quality restrictions.
- Historical refusal restrictions.
- Cash amount thresholds by region/carrier.
- COD fee per carrier, region, or amount band.

Eligibility is functional but not enterprise-complete.

## Migration Opportunities

Recommended future migration, without changing current runtime in this discovery:

1. Introduce a first-class COD payment obligation/session.

   Store COD session identity, amount, currency, order, shipment relationship, collection requirement, and current COD lifecycle state.

2. Separate delivery confirmation from collection confirmation.

   Delivery should not automatically mean collected unless the source event explicitly carries admitted collection evidence.

3. Admit `ShipmentCodCollectedEvent` through Payment.

   Shipping should publish evidence. Payment should validate and decide whether to mark collected.

4. Add COD-specific lifecycle states.

   Suggested states: `AwaitingShipment`, `AwaitingDelivery`, `AwaitingCollection`, `Collected`, `PartiallyCollected`, `CustomerRefused`, `NotCollected`, `ReturnToOrigin`, `Returned`, `Cancelled`, `Refunded`, `Settled`, `Expired`.

5. Build COD administration.

   Add queues, filters, evidence review, failure reasons, settlement reconciliation, and audit views.

6. Add eligibility policy.

   Centralize country, region, shipping method, product, merchant, customer, amount, and carrier restrictions.

7. Add cash custody audit.

   Track driver/carrier collection, handoff, deposit, discrepancy, approval, and settlement.

8. Tighten collection authority.

   Review roles allowed to confirm collection and separate operational delivery confirmation from financial collection approval.

## Runtime Impact

Current runtime impact from this discovery: None.

No code, configuration, database, or runtime behavior was changed.

Immediate runtime update required for current happy-path COD: No.

Runtime update required before claiming enterprise-complete first-class COD certification: Yes.

The current implementation can support a basic COD happy path when `Delivered` operationally means "delivered and cash collected." It should not be considered complete for enterprise COD workflows where delivery, cash collection, refusal, partial collection, return, and settlement are distinct facts.

## Compatibility Scores

These are separate compatibility scores. No overall score is calculated.

| Category | Score | Rationale |
|---|---:|---|
| Architecture Compatibility | 74/100 | Backend payment authority, blocked COD webhooks, unpaid checkout, and settlement are aligned; delivery/collection collapse prevents full compatibility. |
| Workflow Completeness | 48/100 | Happy path exists, but refusal, partial collection, not collected, RTO, retry, customer messaging, and COD-specific states are incomplete. |
| Shipping Integration | 58/100 | Shipping carries COD data and parses collection evidence; payment does not clearly use dedicated shipping collection evidence as canonical confirmation. |
| Security Alignment | 62/100 | Good lifecycle gating and settlement validation; cash custody, evidence, role, and fraud controls are incomplete. |
| Future Runtime Readiness | 70/100 | The provider-neutral architecture can absorb COD cleanly, but needs a dedicated COD workflow layer and state model. |

## Recommendations

1. Certify COD as provider-compatible in concept, not enterprise-complete in current implementation.

2. Do not treat COD as an online payment gateway.

3. Do not merge COD with manual payment.

4. Introduce a COD-specific collection evidence model.

5. Require Payment to admit collection evidence before marking COD collected.

6. Keep Shipping as delivery and collection evidence producer only.

7. Keep Warehouse out of payment and shipping authority.

8. Add COD exception states before high-volume or multi-carrier COD operation.

9. Add COD administration before production-scale operations.

10. Add fraud and cash custody controls before enterprise certification.

Recommended next phase:

`PAYMENT-0.6 COD Fulfillment-Driven Collection Authority and Administration Design`

## Certification Decision

Decision: Partially compatible. Not fully certified.

COD can be a first-class provider inside the PAYMENT-0 platform after a COD-specific workflow migration. The current implementation is compatible with a basic fulfillment-driven COD happy path, but it is not complete enough for enterprise COD certification because it does not fully separate delivery confirmation, cash collection confirmation, collection authority, exception handling, and cash custody audit.

Certification label:

`FIRST-CLASS PROVIDER-COMPATIBLE IN CONCEPT; CURRENT IMPLEMENTATION PARTIALLY COMPATIBLE WITH REQUIRED COD WORKFLOW MIGRATION`

## Success Answers

Reviewed current implementation? Yes.

Workflow architecture compatible? Partially. The basic happy path is compatible, but enterprise COD needs a separate collection evidence and exception workflow.

Authority boundaries preserved? Mostly. Payment owns final payment state, Shipping owns shipment evidence, Warehouse owns fulfillment preparation, and Order owns order lifecycle. The main boundary issue is that delivered evidence can imply cash collection too directly.

Shipping integration compatible? Partially. COD data reaches carriers and collection evidence can be parsed, but dedicated shipping collection evidence is not the apparent canonical payment confirmation source.

Security issues found? Yes. Main issues are delivery-as-collection, broad collection confirmation authority, incomplete cash custody audit, and missing discrepancy controls.

Fraud risks identified? Yes. Driver fraud, carrier evidence mismatch, duplicate collection, unauthorized confirmation, partial collection, and settlement discrepancy risks were identified.

Administration complete? No. COD configuration and settlement exist, but complete COD operations, exception, reconciliation, and reporting administration is missing.

Migration recommended? Yes.

Runtime update required? No immediate update is required for the current happy-path COD runtime. A runtime/design update is required before COD can be certified as enterprise-complete.

Canonical Runtime Mapping completed? Yes.

Immediate action required? No emergency action is required unless live operations treat `Delivered` as cash collected when carriers can report delivered, refused, not collected, or partially collected separately.

Recommended next phase? `PAYMENT-0.6 COD Fulfillment-Driven Collection Authority and Administration Design`.

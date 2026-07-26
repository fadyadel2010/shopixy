# COD Architecture Decision Record

Date: 2026-07-16

Status: Accepted

Scope: Cash On Delivery architecture for standard orders, Storefront, POS Admin,
Shipment, Warehouse, CommerceHub, Treasury, Inventory, and payment runtime
adaptation.

Runtime impact in this phase: none.

Implementation impact in this phase: none.

Authoritative inputs:
- `docs/architecture/COD_REFERENCE_ARCHITECTURE_DISCOVERY.md`
- `docs/architecture/COD_ARCHITECTURE_VALIDATION.md`
- `docs/architecture/PAYMENT_REFERENCE_FREEZE.md`
- `docs/architecture/PAYMENT_RUNTIME_FINAL_CERTIFICATION.md`

## Board Declaration

This Architecture Decision Record is the permanent architectural contract for
Cash On Delivery. It supersedes informal COD discussions and converts the
completed Discovery and Validation conclusions into accepted architecture
decisions.

Future COD implementation and certification phases must implement this ADR.
They must not redefine COD identity, payment finality, evidence authority,
bounded-context ownership, or runtime posture.

COD architecture is stable enough to begin implementation from this validated
record. Implementation may refine details only inside the boundaries accepted
here.

## Summary Decisions

| ADR | Decision | Status |
|---|---|---|
| ADR-001 | COD is a hybrid payment method, payment policy, and fulfillment-driven collection workflow. COD is not an external payment provider. | Accepted |
| ADR-002 | Payment finality is owned by payment lifecycle authorities, not by evidence producers, controllers, shipment services, or carriers. | Accepted |
| ADR-003 | Order Delivered, Cash Collected, Payment Collected, Order Paid, and Settlement Completed are independent business facts. | Accepted |
| ADR-004 | Carrier, courier, driver, POS Admin, CommerceHub, and manual entry are evidence sources, not payment providers. | Accepted |
| ADR-005 | Standard COD reuses lifecycle, SQL truth, accounting outbox, settlement batch, and mutation guards; COD currently bypasses Payment Runtime and must adapt to frozen Runtime in a future certification phase without modifying Runtime. | Accepted |
| ADR-006 | Canonical authorities are `PaymentLifecycleService`, `CommerceHubPaymentLifecycleAuthority`, Evidence Authority, and Settlement Authority, each scoped by bounded context. | Accepted |
| ADR-007 | Bounded-context ownership is explicitly split across Payments, Orders, Shipment, Warehouse, CommerceHub, Storefront, POS Admin, Treasury, and Inventory. | Accepted |
| ADR-008 | Implementation must preserve lifecycle authority, evidence-only admission, no controller-owned payment completion, and no shipment-owned payment truth. | Accepted |
| ADR-009 | Certification must proceed through Foundation, Evidence, Runtime Integration, Lifecycle, Settlement, Operations, Operational Readiness, and Final Certification. | Accepted |

## Canonical COD Identity

COD is:
- A payment method selected during checkout or order creation.
- A payment policy that permits fulfillment before payment collection.
- A fulfillment-driven collection workflow where delivery and collection
  evidence may later support payment collection.
- A hybrid workflow because it touches payment, fulfillment, shipment,
  evidence, treasury, settlement, and marketplace finance.

COD is not:
- An external payment provider.
- A payment gateway.
- A webhook-driven online provider.
- A Stripe, Paymob, Fawry, or manual wallet variant.
- A provider credential or external account authority.

Provider-like strings such as `PaymentProvider.Cod`, `PaymentGateway = "cod"`,
or payment method code `COD` are labels and routing facts. They do not make COD
an external provider.

## ADR-001: Architectural Identity

### Decision

COD is accepted as a hybrid payment method, payment policy, and
fulfillment-driven collection workflow.

COD is rejected as an external payment provider or certified Payment Reference
Provider in the active source.

### Status

Accepted.

### Rationale

Discovery and Validation confirm that standard checkout creates COD orders with
pending payment state and does not recognize collected money at checkout. COD
collection occurs later through delivery/fulfillment collection workflow.
CommerceHub has a separate marketplace financial workflow for COD suborders.

COD can adapt to the frozen Payment Runtime in a future phase, but active COD is
not currently Runtime-reached or reference-provider certified.

### Source Evidence

- `COD_REFERENCE_ARCHITECTURE_DISCOVERY.md`: COD is implemented as a payment
  method plus fulfillment-driven collection policy, not as an active certified
  Payment Reference Provider.
- `COD_ARCHITECTURE_VALIDATION.md`: confirms COD is offline method plus
  delivery/fulfillment collection policy and not an active provider adapter.
- `PAYMENT_REFERENCE_FREEZE.md`: COD is ready to adapt to the frozen reference
  architecture; Stripe and Paymob are the certified reference providers for POS
  Admin plus Storefront.
- `PAYMENT_RUNTIME_FINAL_CERTIFICATION.md`: Runtime is ready for COD workflow
  adaptation to canonical delivery/collection evidence.

### Architectural Impact

COD implementation must begin from offline collection workflow architecture, not
from external gateway architecture. COD must not inherit provider webhook,
credential, or SDK assumptions from Stripe, Paymob, Fawry, or manual wallet.

### Future Constraints

Future phases may add COD Runtime adaptation, evidence admission, and
certification. They may not redefine COD as an external provider to bypass this
ADR.

### Affected Bounded Contexts

Payments, Orders, Shipment, Warehouse, CommerceHub, Storefront, POS Admin,
Treasury, and Inventory.

## ADR-002: Payment Finality

### Decision

Standard order payment completion authority is `PaymentLifecycleService`.

CommerceHub COD payment completion authority is
`CommerceHubPaymentLifecycleAuthority` inside the CommerceHub bounded context.

Evidence producers may produce evidence, but may not complete payment.

Controllers, checkout services, shipment services, carrier adapters, courier
integrations, driver interfaces, frontend state, cache, projection payloads,
manual entry screens, and CommerceHub delivery screens may not independently
complete payment.

### Status

Accepted.

### Rationale

Validation corrects the global single-owner phrasing: `PaymentLifecycleService`
owns standard `Order` payment completion, while CommerceHub COD uses a separate
bounded-context payment lifecycle authority. Both are accepted as scoped
authorities. Neither evidence nor delivery status is payment finality by itself.

### Source Evidence

- Standard COD graph:
  `FulfillmentDeliveredEventConsumer -> PaymentLifecycleService.MarkCodDeliveredAsync -> HandleSuccessAsync -> Order.PaymentStatus = Collected`.
- Standard guard:
  `PaymentStatusSaveChangesInterceptor` blocks unauthorized direct standard
  `Order.PaymentStatus` mutation.
- CommerceHub graph:
  `CommerceHubOrderManagementService -> CommerceHubFinancialWorkflowService -> CommerceHubPaymentLifecycleAuthority.MarkCodSubOrderCollected`.
- CommerceHub guard:
  CommerceHub boundary mutation scope protects marketplace/suborder payment
  fields.
- `PAYMENT_REFERENCE_FREEZE.md`: provider evidence is never final payment
  status by itself; lifecycle mutation must route through the single durable
  lifecycle writer in the reference architecture.

### Architectural Impact

Payment finality must be implemented as an authority transition. Evidence and
delivery facts become inputs to authority, not substitutes for authority.

### Future Constraints

Any new COD entry point must prove which accepted authority owns the final
payment transition for its bounded context. A new payment completion authority
requires a new ADR or explicit supersession.

### Affected Bounded Contexts

Payments, CommerceHub, Orders, Shipment, POS Admin, Storefront, Treasury.

## ADR-003: Independent Business Events

### Decision

The following are independent business facts and must not be collapsed into one
implicit state:

| Fact | Meaning | Payment authority? |
|---|---|---|
| Order Delivered | Fulfillment or shipment confirms goods reached the customer or delivery endpoint. | No |
| Cash Collected | A person, carrier, courier, or marketplace workflow asserts that cash was received. | No |
| Payment Collected | Accepted payment authority records payment collection for the order or suborder. | Yes, only through accepted lifecycle authority |
| Order Paid | The order/suborder paid state is durably recorded according to payment lifecycle rules. | Yes, only through accepted lifecycle authority |
| Settlement Completed | Collected COD money is settled/remitted/reconciled through accepted settlement authority. | No direct payment completion; settlement authority owns settlement state |

### Status

Accepted.

### Rationale

Discovery found that current paths can collapse delivery and collection for
standard COD, while shipment COD collection evidence exists separately and does
not write payment truth. This ADR separates the facts to prevent future
implementation from treating delivery, evidence, paid state, and settlement as
the same event.

### Source Evidence

- `Shipment.CodCollected` and `ShipmentCodCollectedEvent` exist as evidence but
  no active source path writes payment truth from that event.
- `FulfillmentDeliveredEventConsumer` currently triggers standard lifecycle
  collection from fulfillment delivery events.
- `PaymentLifecycleService.RecordCodSettlementAsync` records standard COD
  settlement after validating already-collected orders.
- CommerceHub records COD suborder collection through delivered suborder
  financial workflow.

### Architectural Impact

Future COD models must represent event independence explicitly. Certification
must prove that one fact cannot forge another fact without the accepted
authority transition.

### Future Constraints

Delivery completion may trigger collection evaluation, but may not itself be
treated as payment completion. Cash collection evidence may support payment
collection, but may not itself be the durable paid state. Settlement must occur
after collection and must not retroactively create collection unless explicitly
certified through lifecycle authority.

### Affected Bounded Contexts

Orders, Payments, Shipment, Warehouse, CommerceHub, POS Admin, Treasury,
Inventory.

## ADR-004: Evidence Model

### Decision

The following actors and systems are accepted as evidence sources only:

| Evidence source | Accepted role | Rejected role |
|---|---|---|
| Carrier | Carrier tracking/webhook or shipment evidence source | Payment provider |
| Courier | Delivery or collection evidence source | Payment provider |
| Driver | Human delivery/collection evidence source | Payment provider |
| POS Admin | Administrative evidence entry and review surface | Payment provider or direct payment writer |
| CommerceHub | Marketplace delivery/financial workflow evidence and bounded authority surface | Standard storefront payment provider |
| Manual Entry | Operational evidence capture mechanism | Payment provider or lifecycle writer |

### Status

Accepted.

### Rationale

The active source has shipment COD evidence, fulfillment delivery events,
admin lifecycle requests, and CommerceHub delivered-suborder financial workflow.
Discovery found no canonical COD evidence model that treats driver, carrier,
courier, admin, and manual evidence as durable payment evidence types. This ADR
accepts those actors as evidence sources and rejects them as providers.

### Source Evidence

- `CarrierShipmentService` writes shipment COD collection fields and publishes
  `ShipmentCodCollectedEvent`.
- `FulfillmentDeliveredEventConsumer` consumes fulfillment delivered events and
  invokes standard payment lifecycle.
- `PaymentLifecycleController` exposes admin lifecycle endpoints for COD
  delivered and COD settlement.
- CommerceHub delivery transitions enter CommerceHub financial workflow and
  CommerceHub payment lifecycle authority.
- `PAYMENT_REFERENCE_FREEZE.md`: non-webhook workflows require equivalent
  inbound evidence boundaries; evidence adapters admit evidence only.

### Architectural Impact

COD evidence must be modeled as candidate evidence entering an accepted
admission boundary. Evidence source identity, actor identity, timestamp,
amount, currency, order/suborder identity, and correlation must be preserved
where applicable.

### Future Constraints

No evidence source may mutate payment truth directly. Driver, courier, carrier,
POS Admin, CommerceHub, and manual entry flows require evidence admission,
replay controls, authority validation, and lifecycle routing before payment
completion.

### Affected Bounded Contexts

Shipment, Warehouse, Payments, POS Admin, CommerceHub, Storefront, Treasury.

## ADR-005: Payment Runtime

### Decision

The following Runtime-related components and patterns are reused or preserved:

| Component or pattern | COD decision |
|---|---|
| `PaymentLifecycleService` | Reused for standard COD collection, paid-state mutation, settlement validation, SQL truth, outbox, replay/idempotency, and accounting staging. |
| `PaymentStatusSaveChangesInterceptor` | Reused to protect standard payment status mutation. |
| Accounting outbox | Reused for lifecycle-owned accounting side effects. |
| `PaymentSettlementBatch` | Reused for standard COD settlement batches. |
| `PaymentRuntimeOrchestrator` | Not currently reached by COD; must be used by future COD Runtime adaptation without changing Runtime. |
| Provider-neutral Runtime objects and stages | Reused in future Runtime Integration certification for COD delivery/collection evidence. |
| Stripe/Paymob/Fawry adapters | Intentionally bypassed; must not be reused as COD implementations. |
| Online webhook provider path | Intentionally bypassed for COD; COD has no external provider webhook requirement. |
| Manual wallet proof path | Intentionally bypassed; manual wallet proof is not COD evidence. |
| CommerceHub payment lifecycle authority | Reused only inside CommerceHub bounded context, not as the standard storefront writer. |

Future convergence with Payment Runtime is required for COD to become
reference-provider or reference-workflow compliant under `PAYMENT_REFERENCE_FREEZE`.
This convergence must adapt COD to the frozen Runtime; it must not modify the
Runtime.

### Status

Accepted.

### Rationale

Validation proves COD currently bypasses Runtime completely. The Runtime itself
is certified and frozen, and the payment reference freeze requires every future
payment provider or workflow, including COD, to adapt through equivalent
application, evidence, bridge, Runtime, outcome admission, and lifecycle layers.

### Source Evidence

- `COD_ARCHITECTURE_VALIDATION.md`: active Runtime paths exist for Stripe,
  Paymob, Fawry, and Manual Payment; COD paths do not reach Runtime.
- `COD_REFERENCE_ARCHITECTURE_DISCOVERY.md`: no `CodRuntimeBridge`, no COD call
  to `PaymentRuntimeOrchestrator`, and direct standard lifecycle or CommerceHub
  authority mutation.
- `PAYMENT_REFERENCE_FREEZE.md`: future COD must use delivery, failed-delivery,
  collection, remittance, discrepancy, and settlement evidence through the same
  Runtime and lifecycle writer while fulfillment and cash/treasury authorities
  remain separate.
- `PAYMENT_RUNTIME_FINAL_CERTIFICATION.md`: Runtime is ready for COD workflow
  adaptation to canonical delivery/collection evidence.

### Architectural Impact

Current COD implementation may continue from lifecycle-owned standard payment
truth and CommerceHub bounded authority, but future reference compliance must
add COD Runtime integration rather than changing the Runtime.

### Future Constraints

No COD implementation may claim reference-provider compliance until it passes
Runtime Integration and convergence certification. No Runtime modification is
authorized by this ADR.

### Affected Bounded Contexts

Payments, CommerceHub, Storefront, POS Admin, Shipment, Treasury.

## ADR-006: Authority Model

### Decision

Canonical COD authorities are:

| Authority | Owns | Does not own |
|---|---|---|
| `PaymentLifecycleService` | Standard order COD payment collection, standard order paid state, standard COD settlement validation and lifecycle transition, SQL truth/outbox/audit for standard orders. | CommerceHub marketplace/suborder payment lifecycle, evidence production, shipment completion, treasury cash custody. |
| `CommerceHubPaymentLifecycleAuthority` | CommerceHub marketplace/suborder payment collection and refund payment state inside CommerceHub bounded context. | Standard storefront order payment lifecycle, standard settlement batch ownership, external provider status. |
| Evidence Authority | Admission and provenance of carrier, courier, driver, admin, manual, shipment, and marketplace evidence candidates. | Payment completion or settlement finality. |
| Settlement Authority | Standard COD settlement through `PaymentLifecycleService.RecordCodSettlementAsync`; CommerceHub settlement/remittance through CommerceHub financial/debt/payout services; Treasury reconciliation where applicable. | Initial payment collection evidence or shipment delivery completion. |

### Status

Accepted.

### Rationale

The validated source has two COD payment completion authorities by bounded
context. The discovery and validation also identify evidence and settlement as
separate authority concerns. This ADR accepts scoped authority rather than
pretending a single global COD owner exists in active source.

### Source Evidence

- Standard payment completion: `PaymentLifecycleService`.
- CommerceHub completion: `CommerceHubPaymentLifecycleAuthority`.
- Evidence: `CarrierShipmentService`, `ShipmentCodCollectedEvent`,
  `FulfillmentDeliveredEventConsumer`, `PaymentLifecycleController`,
  CommerceHub delivered-suborder financial workflow.
- Settlement: `PaymentLifecycleService.RecordCodSettlementAsync` for standard
  orders; CommerceHub financial/debt/payout services for marketplace money
  flow.

### Architectural Impact

Authority must be named at every COD mutation boundary. Cross-context reuse is
allowed only when explicitly certified and must not silently collapse
CommerceHub and standard storefront ownership.

### Future Constraints

The accepted split may be reconciled in a future lifecycle convergence phase,
but it may not be ignored. Any unification must preserve bounded-context
exclusions and pass certification.

### Affected Bounded Contexts

Payments, CommerceHub, Treasury, Orders, Shipment, POS Admin.

## ADR-007: Bounded Context Ownership

### Decision

COD ownership is assigned as follows:

| Bounded context | Accepted ownership |
|---|---|
| Payments | Standard order payment lifecycle, COD collection transition, paid state, refund/payment status transitions, standard COD settlement lifecycle, SQL payment truth, payment outbox. |
| Orders | Order creation, order identity, order lifecycle, order delivered facts only through accepted fulfillment/order authority, immutable order facts needed by payment lifecycle. |
| Shipment | Shipment creation, carrier/tracking status, shipment COD evidence fields, carrier/courier/driver evidence capture, shipment events. Shipment does not own payment truth. |
| Warehouse | Physical fulfillment execution, pick/pack/dispatch support, delivery handoff evidence where applicable. Warehouse does not own payment completion. |
| CommerceHub | Marketplace checkout, marketplace/suborder payment state, CommerceHub delivered-suborder financial workflow, vendor earning/debt/payout/settlement workflows inside CommerceHub. |
| Storefront | Payment method selection candidate, checkout intent, customer-visible order/payment presentation. Storefront does not own collection or paid finality. |
| POS Admin | Administrative evidence entry, lifecycle command surface, settlement command surface, operations review. POS Admin does not own payment completion outside lifecycle authority. |
| Treasury | Cash custody, reconciliation, remittance, settlement accounting, financial reporting, treasury controls. Treasury does not rewrite initial payment collection truth. |
| Inventory | Stock reservation/release/fulfillment inventory effects. Inventory does not own payment evidence, collection, paid state, or settlement. |

### Status

Accepted.

### Rationale

COD spans several domains but must preserve ownership separation. Discovery and
validation prove payment truth, shipment evidence, fulfillment delivery,
CommerceHub marketplace finance, and settlement are related but not the same
authority.

### Source Evidence

- Standard checkout creates orders with COD payment method and pending payment
  state.
- Fulfillment delivery event currently triggers standard payment lifecycle.
- Shipment stores COD evidence fields but does not write payment truth.
- CommerceHub uses marketplace/suborder payment lifecycle authority.
- Standard settlement uses payment lifecycle settlement batch.
- Reference freeze keeps fulfillment and cash/treasury authorities separate
  from payment lifecycle finality.

### Architectural Impact

Implementation must route each COD command, event, and projection to its owning
bounded context and may not create a convenience service that owns several
truths.

### Future Constraints

Certification must prove no context writes another context's authority:
shipment cannot write payment truth, payment cannot forge shipment delivery,
treasury cannot backfill payment collection without lifecycle authority, and
frontend/admin cannot convert evidence into paid state directly.

### Affected Bounded Contexts

Payments, Orders, Shipment, Warehouse, CommerceHub, Storefront, POS Admin,
Treasury, Inventory.

## ADR-008: Implementation Principles

### Decision

All future COD implementation must obey the following mandatory rules:

1. No controller-owned payment completion.
2. No shipment-owned payment truth.
3. No warehouse-owned payment truth.
4. No frontend-owned payment truth.
5. No carrier, courier, driver, manual entry, or POS Admin evidence source may
   complete payment directly.
6. Evidence never completes payment directly.
7. Standard `Order` payment completion must route through
   `PaymentLifecycleService`.
8. CommerceHub marketplace/suborder COD completion must route through
   `CommerceHubPaymentLifecycleAuthority` unless a future certified
   convergence phase supersedes this rule.
9. `PaymentLifecycleService` remains authoritative within the standard payment
   context.
10. CommerceHub payment lifecycle authority remains bounded to CommerceHub.
11. COD checkout may create a pending/offline payment obligation but may not
    mark money collected at checkout.
12. COD delivery may trigger payment collection evaluation but may not itself
    be treated as paid finality.
13. Settlement may occur only after accepted collection authority proves the
    collection state.
14. Payment Runtime must remain frozen; COD adapts to Runtime, not the reverse.
15. Stripe, Paymob, Fawry, and manual wallet adapters must not be repurposed as
    COD implementations.
16. Projection, cache, queue payload, dashboard, export, and frontend state are
    never authoritative payment truth.
17. Replay, idempotency, tenant scope, amount, currency, order/suborder
    identity, and actor/source provenance must be certified before lifecycle
    mutation.

### Status

Accepted.

### Rationale

These rules preserve the validated authority graph and the payment reference
freeze while allowing implementation to begin safely. They prevent the common
COD failure mode where delivery, evidence, admin action, shipment status, or
cash custody becomes a direct paid-state mutation.

### Source Evidence

- `PaymentStatusSaveChangesInterceptor` protects standard payment status.
- CommerceHub boundary guards protect CommerceHub marketplace/suborder payment
  fields.
- Storefront payment authority certification states COD intent creation can
  confirm fulfillment but does not recognize collected money.
- Storefront shipping authority certification states shipment COD collection
  events are evidence only and payment truth is created by payment lifecycle.
- Payment reference freeze forbids bypassing frozen layers because a workflow
  is cash-based, manual, webhook-free, or marketplace-specific.

### Architectural Impact

Implementation must be certification-led. New endpoints, services, events,
workers, and projections must show their authority boundary and must not mix
evidence production with lifecycle mutation.

### Future Constraints

Any implementation violating these rules is architecturally non-compliant even
if tests pass. A change to any accepted rule requires a superseding ADR.

### Affected Bounded Contexts

Payments, Orders, Shipment, Warehouse, CommerceHub, Storefront, POS Admin,
Treasury, Inventory.

## ADR-009: Certification Strategy

### Decision

COD certification must proceed through the following minimal roadmap:

| Phase | Required certification outcome |
|---|---|
| Foundation | COD identity, bounded contexts, authority graph, tenant scope, payment method policy, and non-provider posture are certified. |
| Evidence | Carrier, courier, driver, POS Admin, CommerceHub, and manual evidence sources are modeled as evidence-only inputs with replay, identity, amount, currency, actor, timestamp, and provenance controls. |
| Runtime Integration | COD runtime application service, COD runtime bridge, equivalent evidence adapter, exact Runtime outcome admission, and no Runtime modification are certified. |
| Lifecycle | Standard `PaymentLifecycleService` and CommerceHub lifecycle authority ownership are certified or deliberately reconciled through a bounded-context convergence decision. |
| Settlement | Standard COD settlement batch, CommerceHub remittance/debt/payout, Treasury reconciliation, accounting outbox, and settlement idempotency are certified. |
| Operations | COD exception states, failed delivery, refused delivery, not collected, partial/discrepant collection, return-to-origin, refund, recovery, replay, and reconciliation operations are certified where in scope. |
| Operational Readiness | Health, telemetry, alerts, runbooks, migrations, dashboards, drills, rollback, retention, and support ownership are source-certified. |
| Final Certification | Unified architecture, implementation, security, operations, migration, regression, extensibility, and production readiness decisions are issued with explicit certified scope and exclusions. |

### Status

Accepted.

### Rationale

Discovery recommended current-source, evidence, runtime bridge, lifecycle
convergence, settlement/remittance, and exception certification phases. The
payment reference freeze provides a broader provider certification pipeline.
This ADR compresses those into the minimal COD roadmap required before final
COD certification.

### Source Evidence

- `COD_REFERENCE_ARCHITECTURE_DISCOVERY.md`: recommends discovery/source,
  evidence, runtime bridge, lifecycle convergence, settlement/remittance, and
  exception certifications.
- `COD_ARCHITECTURE_VALIDATION.md`: implementation can safely begin from the
  validated graph without additional discovery for the mission questions.
- `PAYMENT_REFERENCE_FREEZE.md`: defines foundation through final certification
  and closure/re-certification governance for payment workflows.
- `PAYMENT_RUNTIME_FINAL_CERTIFICATION.md`: Runtime is certified but activation
  and integration are separate phases.

### Architectural Impact

Implementation may begin only against the accepted roadmap. A phase cannot
claim a later phase's label. Evidence certification does not imply lifecycle
certification. Runtime Integration does not imply Operational Readiness. Final
Certification must name the exact certified surface and exclusions.

### Future Constraints

Future implementation work must cite this ADR and state the certification phase
being advanced. No phase may use implementation progress to redefine accepted
architecture decisions.

### Affected Bounded Contexts

Payments, Orders, Shipment, Warehouse, CommerceHub, Storefront, POS Admin,
Treasury, Inventory, Operations.

## Explicit Rejections

The following decisions are rejected:

| Rejected decision | Status | Reason |
|---|---|---|
| COD is an external payment provider. | Rejected | No active COD provider adapter, credential authority, external gateway protocol, webhook, or Runtime bridge exists. |
| COD is already a certified Payment Reference Provider. | Rejected | Active COD bypasses Payment Runtime and has split completion authorities. |
| Shipment COD collection writes payment truth. | Rejected | Shipment evidence exists, but payment truth is lifecycle-owned. |
| Delivery equals paid. | Rejected | Delivery and paid state are independent business facts. |
| Cash collected evidence equals payment collected. | Rejected | Evidence must be admitted by accepted payment authority. |
| POS Admin may directly mark payment complete outside lifecycle authority. | Rejected | Admin is a command/evidence surface, not a lifecycle writer. |
| CommerceHub authority may be silently reused as standard storefront payment authority. | Rejected | CommerceHub authority is bounded to CommerceHub. |
| COD requires Payment Runtime modification. | Rejected | Runtime is certified and frozen; COD must adapt to it. |

## Final Questions

| Question | Answer |
|---|---|
| Is COD architecture now stable enough to begin implementation? | Yes. Implementation may begin from this ADR and the validated Discovery/Validation graph. |
| Should future implementation be allowed to redefine any accepted ADR? | No. Future implementation must not redefine accepted decisions. A change requires a superseding architecture decision. |
| Should this ADR become the mandatory reference for all COD work? | Yes. This ADR is the mandatory reference for all future COD implementation, certification, review, and operational readiness work. |

## Final Authority Statement

COD is architecturally stable as a hybrid payment method, payment policy, and
fulfillment-driven collection workflow. It is not an external payment provider.

Standard COD payment truth belongs to `PaymentLifecycleService`.
CommerceHub COD payment truth belongs to `CommerceHubPaymentLifecycleAuthority`
inside the CommerceHub bounded context. Evidence sources produce evidence only.
Settlement is a separate authority. Runtime is frozen and future COD reference
compliance requires COD to adapt to the Runtime without modifying it.

Future COD work must implement this ADR. It must not redefine it.

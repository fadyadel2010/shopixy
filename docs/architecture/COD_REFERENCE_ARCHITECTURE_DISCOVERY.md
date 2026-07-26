# COD Reference Architecture Discovery

## Executive Summary

This report is source-first. Markdown was read only after executable source, tests, persistence, events, and runtime registrations were traced.

Conclusion: COD is not implemented as a certified Payment Reference Provider in the active source. COD is implemented as a payment method plus fulfillment-driven collection policy. In standard orders, durable payment completion is owned by `PaymentLifecycleService`. In CommerceHub, COD completion is owned by a separate `CommerceHubPaymentLifecycleAuthority` invoked from CommerceHub financial workflow.

Source evidence:
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Payments/PaymentService.cs`
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`
- `PointOfSale.Infrastructure/Consumers/OrderDeliveredEventConsumer.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubOrderManagementService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubFinancialWorkflowService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentLifecycleAuthority.cs`
- `PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs`
- `PointOfSale.Infrastructure/Data/PaymentStatusSaveChangesInterceptor.cs`

Documentation comparison:
- `docs/architecture/PAYMENT_REFERENCE_FREEZE.md` requires providers and workflows to adapt through Runtime Application Service, Runtime Bridge, `PaymentRuntimeOrchestrator`, exact runtime outcome admission, then `PaymentLifecycleService`.
- `docs/architecture/PAYMENT_RUNTIME_FINAL_CERTIFICATION.md` certifies runtime readiness for COD adaptation, but explicitly leaves runtime activation and integration to later phases.
- Active COD source does not satisfy the freeze path for COD. Standard COD reuses lifecycle directly. CommerceHub COD uses a separate CommerceHub lifecycle authority.

## Current Source Architecture

### Conclusion: Standard storefront COD is a payment method and offline collection policy, not an online provider.

Source evidence:
- `CheckoutController.Checkout(...)` calls `ICheckoutService.ProcessCheckoutAsync(...)`.
- `CheckoutService.ProcessCheckoutAsync(...)` creates the order through `CreateOrderAsync(...)`.
- `CheckoutService.CreateOrderAsync(...)` detects COD from payment method code/supports-offline flags, creates the order with pending payment state, and records fulfillment ownership.
- `PaymentService.CreatePaymentIntentAsync(...)` has a COD branch that emits skipped/confirmed events, but it does not recognize collected money.
- `PaymentService.ConfirmCodOrderAsync(...)` confirms the COD order for fulfillment using authorized order status mutation, but it does not set `PaymentStatus.Collected`.

Concrete classes:
- `CheckoutController`
- `CheckoutService`
- `PaymentsController`
- `PaymentService`
- `Order`

Execution path:

```text
CheckoutController.Checkout
-> CheckoutService.ProcessCheckoutAsync
-> CheckoutService.CreateOrderAsync
-> Order.PaymentMethodCode = COD
-> Order.PaymentStatus = Pending
-> Order.IsPaid = false
```

Documentation comparison:
- `storefront-payment-authority-certification-findings.md` agrees that COD intent creation can confirm fulfillment and does not recognize collected money.
- `PAYMENT_REFERENCE_FREEZE.md` does not certify COD as a reference provider; it says COD is ready to adapt to the frozen runtime.

### Conclusion: CommerceHub COD is a marketplace payment method and financial workflow, not the standard `IPaymentLifecycleService` path.

Source evidence:
- `CommerceHubCheckoutController.Checkout(...)` calls `ICommerceHubCheckoutService.CheckoutAsync(...)`.
- `CommerceHubCheckoutService.BuildMarketplaceOrder(...)` maps COD to `CommerceHubPaymentMethod.CashOnDelivery`, sets `PaymentGateway = "cod"`, and does not create an online payment reference.
- `CommerceHubOrderManagementService.UpdateSubOrderStatusAsync(...)` calls `CommerceHubFinancialWorkflowService.ApplyDeliveredFinancialsAsync(...)` when a suborder is delivered.
- `CommerceHubFinancialWorkflowService.ApplyDeliveredFinancialsAsync(...)` calls `CommerceHubPaymentLifecycleAuthority.MarkCodSubOrderCollected(...)`.
- `CommerceHubPaymentLifecycleAuthority.MarkCodSubOrderCollected(...)` writes `PaymentStatus.Collected` and `PaidAt` for COD suborders and the marketplace order when all COD suborders are collected.

Concrete classes:
- `CommerceHubCheckoutController`
- `CommerceHubCheckoutService`
- `CommerceHubOrdersController`
- `CommerceHubOrderManagementService`
- `CommerceHubFinancialWorkflowService`
- `CommerceHubPaymentLifecycleAuthority`
- `MarketplaceOrder`
- `CommerceHubSubOrder`

Execution path:

```text
CommerceHubCheckoutController.Checkout
-> CommerceHubCheckoutService.CheckoutAsync
-> CommerceHubCheckoutService.BuildMarketplaceOrder
-> CommerceHubSubOrder.PaymentMethod = CashOnDelivery
-> CommerceHubOrdersController.UpdateSubOrderStatus
-> CommerceHubOrderManagementService.UpdateSubOrderStatusAsync
-> CommerceHubFinancialWorkflowService.ApplyDeliveredFinancialsAsync
-> CommerceHubPaymentLifecycleAuthority.MarkCodSubOrderCollected
-> CommerceHubSubOrder.PaymentStatus = Collected
-> MarketplaceOrder.PaymentStatus = Collected when all COD suborders are collected
```

Documentation comparison:
- `PAYMENT_REFERENCE_FREEZE.md` says `PaymentLifecycleService` is the single durable lifecycle writer in the reference architecture.
- Source diverges for CommerceHub COD because CommerceHub writes COD payment completion through `CommerceHubPaymentLifecycleAuthority`.

## Execution Graph

### Standard storefront COD graph

```text
CheckoutController
-> CheckoutService
-> Order
-> PaymentService.ConfirmCodOrderAsync
-> Fulfillment or shipment delivery
-> FulfillmentDeliveredEventConsumer
-> PaymentLifecycleService.MarkCodDeliveredAsync
-> PaymentLifecycleService.HandlePaymentResultAsync
-> PaymentLifecycleService.HandleSuccessAsync
-> Order.PaymentStatus = Collected
-> Accounting outbox
-> Payment events
-> COD settlement through PaymentLifecycleService.RecordCodSettlementAsync
```

### Admin lifecycle COD graph

```text
PaymentLifecycleController.MarkCodDelivered
-> PaymentLifecycleService.HandlePaymentResultAsync
-> PaymentLifecycleService.HandleSuccessAsync
-> Order.PaymentStatus = Collected
-> Accounting outbox
-> Payment events
```

### CommerceHub COD graph

```text
CommerceHubCheckoutController
-> CommerceHubCheckoutService
-> MarketplaceOrder and CommerceHubSubOrder
-> CommerceHubOrdersController
-> CommerceHubOrderManagementService
-> CommerceHubFinancialWorkflowService
-> CommerceHubPaymentLifecycleAuthority
-> CommerceHubSubOrder.PaymentStatus = Collected
-> MarketplaceOrder.PaymentStatus = Collected when complete
-> CommerceHub earning, audit, ledger, debt/settlement workflow
```

### Runtime graph

```text
Standard COD source path
-> no CodRuntimeBridge found
-> no COD call to PaymentRuntimeOrchestrator found
-> direct PaymentLifecycleService admission

CommerceHub COD source path
-> no CodRuntimeBridge found
-> no COD call to PaymentRuntimeOrchestrator found
-> direct CommerceHubPaymentLifecycleAuthority mutation
```

Runtime documentation comparison:
- `PAYMENT_REFERENCE_FREEZE.md` required path: controller/application service -> runtime application service -> provider adapter/runtime bridge -> `PaymentRuntimeOrchestrator` -> exact runtime outcome admission -> `PaymentLifecycleService`.
- Active COD source path bypasses that runtime path.

## Bounded Context Ownership

### Cash Collection

Conclusion: Cash collection ownership is split by bounded context.

Source evidence:
- Standard orders: `FulfillmentDeliveredEventConsumer` calls `PaymentLifecycleService.MarkCodDeliveredAsync(...)` for delivered COD orders.
- Standard payment lifecycle: `PaymentLifecycleService.HandleSuccessAsync(...)` writes `Order.PaymentStatus = Collected`.
- CommerceHub: `CommerceHubFinancialWorkflowService.ApplyDeliveredFinancialsAsync(...)` calls `CommerceHubPaymentLifecycleAuthority.MarkCodSubOrderCollected(...)`.

Concrete classes:
- `FulfillmentDeliveredEventConsumer`
- `PaymentLifecycleService`
- `CommerceHubFinancialWorkflowService`
- `CommerceHubPaymentLifecycleAuthority`

Execution path:

```text
Standard: FulfillmentDeliveredEvent -> PaymentLifecycleService -> Order payment fields
CommerceHub: Suborder Delivered -> CommerceHubFinancialWorkflowService -> CommerceHubPaymentLifecycleAuthority -> suborder payment fields
```

Documentation comparison:
- Standard storefront documentation agrees that payment truth is created by payment lifecycle, not shipping.
- Freeze documentation requires one durable lifecycle writer, but CommerceHub COD has its own writer.

### Settlement and Remittance

Conclusion: Standard COD settlement is owned by `PaymentLifecycleService.RecordCodSettlementAsync(...)`. CommerceHub COD settlement/remittance is owned by CommerceHub financial services and ledger/debt workflows.

Source evidence:
- `PaymentLifecycleService.RecordCodSettlementAsync(...)` validates tenant, settlement id, order set, currency, gross amount, and current `PaymentStatus.Collected` before setting standard orders to `PaymentStatus.Settled`.
- `CommerceHubFinancialWorkflowService.ApplyDeliveredFinancialsAsync(...)` creates or updates vendor earning, audit, and ledger effects for delivered COD suborders.
- CommerceHub debt and payout services are tested as separate marketplace finance authorities.

Concrete classes:
- `PaymentLifecycleService`
- `PaymentSettlementBatch`
- `CommerceHubFinancialWorkflowService`
- `CommerceHubVendorDebtService`
- `CommerceHubVendorPaymentsService`

Execution path:

```text
Standard: PaymentLifecycleController.RecordCodSettlement -> PaymentLifecycleService.RecordCodSettlementAsync -> PaymentSettlementBatch -> Order.PaymentStatus = Settled -> Accounting outbox
CommerceHub: Delivered suborder -> CommerceHubFinancialWorkflowService -> vendor earning/audit/ledger/debt workflow
```

Documentation comparison:
- `TreasurySettlementAuthorityCertificationTests` and storefront payment authority docs agree that standard COD settlement is lifecycle-owned.
- Marketplace freeze language requires independent certification for marketplace contexts. Source implements CommerceHub separately.

### Driver, Carrier, Courier, Admin, Manual Evidence

Conclusion: The active source does not contain a single durable COD collection evidence model with distinct driver, carrier, courier, admin, and manual evidence types.

Source evidence:
- `Shipment` stores `IsCashOnDelivery`, `CodAmount`, `CodCollected`, and `CodCollectedAtUtc`.
- `CarrierShipmentService` sets shipment COD collection fields from carrier tracking/webhook parse results and publishes `ShipmentCodCollectedEvent`.
- `FulfillmentDeliveredEventConsumer` uses `FulfillmentDeliveredEvent`, not `ShipmentCodCollectedEvent`, to call `PaymentLifecycleService.MarkCodDeliveredAsync(...)`.
- `PaymentLifecycleController` exposes an admin lifecycle endpoint for COD delivered.
- `PaymentIntent.ProofImageUrl` and manual wallet proof paths exist for manual wallet payments, not COD collection.

Concrete classes:
- `Shipment`
- `CarrierShipmentService`
- `ShipmentCodCollectedEvent`
- `FulfillmentDeliveredEventConsumer`
- `PaymentLifecycleController`
- `PaymentIntent`

Execution path:

```text
Carrier tracking/webhook
-> CarrierShipmentService
-> Shipment.CodCollected = true
-> ShipmentCodCollectedEvent
-> no source path found from ShipmentCodCollectedEvent to PaymentLifecycleService

Fulfillment delivery
-> FulfillmentDeliveredEvent
-> FulfillmentDeliveredEventConsumer
-> PaymentLifecycleService.MarkCodDeliveredAsync
```

Documentation comparison:
- `storefront-shipping-authority-certification-findings.md` agrees that shipment COD events are evidence only and payment truth is created by payment lifecycle.
- `PAYMENT_0_5E_COD_DISCOVERY.md` agrees that dedicated collection evidence is incomplete.

## Payment Authority

### Conclusion: Standard payment completion is owned by `PaymentLifecycleService`.

Source evidence:
- `IPaymentLifecycleService.MarkCodDeliveredAsync(...)` exists as the standard COD delivery collection entry.
- `PaymentLifecycleService.MarkCodDeliveredAsync(...)` creates a succeeded lifecycle request with `PaymentLifecycleSource.CodDelivery`.
- `PaymentLifecycleService.HandleSuccessAsync(...)` validates the order, amount, and lifecycle state, then mutates payment fields under authorized mutation context.
- `PaymentStatusSaveChangesInterceptor` blocks unauthorized direct `Order.PaymentStatus` mutation and directs callers to lifecycle methods.

Concrete classes:
- `IPaymentLifecycleService`
- `PaymentLifecycleService`
- `PaymentStatusSaveChangesInterceptor`
- `OrderStatusSaveChangesInterceptor`
- `Order`

Execution path:

```text
PaymentLifecycleService.MarkCodDeliveredAsync
-> PaymentLifecycleService.HandlePaymentResultAsync
-> PaymentLifecycleService.HandleSuccessAsync
-> PaymentStatusSaveChangesInterceptor authorized context
-> Order.PaymentStatus = Collected
-> Order.IsPaid = true
-> Order.PaidAt = now
```

Documentation comparison:
- `PAYMENT_REFERENCE_FREEZE.md` agrees that `PaymentLifecycleService` is the durable lifecycle writer.
- Active standard COD complies with lifecycle ownership, but bypasses required runtime admission.

### Conclusion: There is no single authoritative COD completion owner across all source contexts.

Source evidence:
- Standard orders use `PaymentLifecycleService`.
- CommerceHub COD uses `CommerceHubPaymentLifecycleAuthority`.
- `ApplicationDbContext` enforces CommerceHub payment mutation boundaries through `CommerceHubBoundaryMutationScope`, not through the standard `PaymentStatusSaveChangesInterceptor`.

Concrete classes:
- `PaymentLifecycleService`
- `CommerceHubPaymentLifecycleAuthority`
- `ApplicationDbContext`
- `CommerceHubBoundaryMutationScope`

Execution path:

```text
Standard: PaymentLifecycleService -> Order.PaymentStatus
CommerceHub: CommerceHubPaymentLifecycleAuthority -> CommerceHubSubOrder.PaymentStatus and MarketplaceOrder.PaymentStatus
```

Documentation comparison:
- Freeze documentation requires a single durable lifecycle writer for payment lifecycle.
- Source diverges by bounded context.

## Collection Authority

Conclusion: Fulfillment admits the operational delivery fact. Payment lifecycle admits the standard COD payment collection fact. CommerceHub order management and financial workflow admit marketplace COD collection.

Source evidence:
- `IOrderFulfillmentService` comments identify fulfillment delivery as the source event for COD lifecycle collection.
- `FulfillmentDeliveredEventConsumer` only calls lifecycle collection when the event is COD and current payment status is pending.
- `PaymentLifecycleService.HandleSuccessAsync(...)` revalidates SQL state before writing collection.
- `CommerceHubOrderManagementService.UpdateSubOrderStatusAsync(...)` validates transition readiness before delivered financials.
- `CommerceHubPaymentLifecycleAuthority.EnsureCanCollect(...)` guards COD suborder collection.

Concrete classes:
- `IOrderFulfillmentService`
- `FulfillmentDeliveredEventConsumer`
- `PaymentLifecycleService`
- `CommerceHubOrderManagementService`
- `CommerceHubPaymentLifecycleAuthority`

Execution path:

```text
Standard: Fulfillment delivery evidence -> PaymentLifecycleService SQL validation -> payment collection write
CommerceHub: Suborder status Delivered -> financial workflow -> CommerceHub payment authority validation -> payment collection write
```

Documentation comparison:
- Storefront shipping docs agree that shipping evidence cannot write payment truth.
- COD discovery docs agree that delivery and collection are collapsed in current paths.

## Settlement Authority

Conclusion: Standard COD settlement is lifecycle-owned and SQL-truth based. CommerceHub COD settlement is marketplace-finance-owned.

Source evidence:
- `PaymentLifecycleService.RecordCodSettlementAsync(...)` rejects missing orders, tenant mismatches, currency mismatches, gross mismatches, duplicate batch mismatches, and non-collected orders.
- `PaymentLifecycleService.RecordCodSettlementAsync(...)` sets `Order.PaymentStatus = Settled` and enqueues COD settlement accounting after validation.
- CommerceHub delivered financials create vendor earning, ledger, and audit facts when COD is collected.

Concrete classes:
- `PaymentLifecycleService`
- `PaymentSettlementBatch`
- `AccountingOutbox`
- `CommerceHubFinancialWorkflowService`

Execution path:

```text
PaymentLifecycleController.RecordCodSettlement
-> PaymentLifecycleService.RecordCodSettlementAsync
-> SQL validation
-> PaymentSettlementBatch
-> Order.PaymentStatus = Settled
-> Accounting outbox
```

Documentation comparison:
- Settlement authority tests agree for standard lifecycle settlement.
- Freeze requires marketplace providers to be independently certified. CommerceHub source is separate from standard settlement lifecycle.

## Evidence Authority

Conclusion: COD evidence exists as shipment fields, fulfillment events, controller requests, and CommerceHub delivery/audit facts. It is not implemented as a canonical runtime evidence artifact for COD.

Source evidence:
- `CarrierShipmentService.PublishShipmentCodCollectedEventAsync(...)` creates `ShipmentCodCollectedEvent`.
- `FulfillmentDeliveredEventConsumer` consumes `FulfillmentDeliveredEvent` and calls lifecycle.
- `PaymentLifecycleController.MarkCodDelivered(...)` can directly call lifecycle from API.
- `CommerceHubFinancialWorkflowService` records COD collected audit/ledger effects.
- No `CodRuntimeBridge`, `CodRuntimeApplicationService`, or COD-specific call to `PaymentRuntimeOrchestrator` was found in active source.

Concrete classes:
- `CarrierShipmentService`
- `ShipmentCodCollectedEvent`
- `FulfillmentDeliveredEventConsumer`
- `PaymentLifecycleController`
- `CommerceHubFinancialWorkflowService`
- `PaymentRuntimeOrchestrator`

Execution path:

```text
Evidence sources
-> shipment COD fields or fulfillment delivered event or admin lifecycle request or CommerceHub delivered status
-> lifecycle/CommerceHub authority
-> persistence

Not found:
Evidence source -> CodRuntimeBridge -> PaymentRuntimeOrchestrator -> exact runtime outcome admission
```

Documentation comparison:
- Runtime final certification says COD is ready for workflow adaptation to canonical delivery/collection evidence.
- Active source has not implemented that adaptation.

## Architecture Validation

### Validated source facts

1. COD checkout does not mark standard orders paid.
   - Evidence: `CheckoutService.CreateOrderAsync(...)`, `Order.PaymentStatus`, `Order.IsPaid`.
   - Documentation comparison: agrees with storefront payment authority docs.

2. Standard COD collection is blocked from ordinary payment intent/webhook paths.
   - Evidence: `PaymentService.CreatePaymentIntentAsync(...)`, `PaymentService.ConfirmCodOrderAsync(...)`, COD webhook blocking tests.
   - Documentation comparison: agrees with COD discovery docs.

3. Standard COD payment completion is lifecycle-owned.
   - Evidence: `PaymentLifecycleService.MarkCodDeliveredAsync(...)`, `HandleSuccessAsync(...)`, `PaymentStatusSaveChangesInterceptor`.
   - Documentation comparison: agrees with storefront payment authority docs and freeze lifecycle ownership.

4. Shipment can store COD collection evidence, but it does not own payment truth.
   - Evidence: `Shipment.CodCollected`, `CarrierShipmentService`, `ShipmentCodCollectedEvent`, `FulfillmentDeliveredEventConsumer`.
   - Documentation comparison: agrees with storefront shipping authority docs.

5. CommerceHub COD does not use the standard payment lifecycle writer.
   - Evidence: `CommerceHubPaymentLifecycleAuthority.MarkCodSubOrderCollected(...)`.
   - Documentation comparison: diverges from the freeze single-writer reference model unless CommerceHub is treated as an independently certified marketplace bounded context.

6. Active COD does not reach Payment Runtime.
   - Evidence: no COD runtime bridge/application service found; standard path calls `PaymentLifecycleService`; CommerceHub path calls `CommerceHubPaymentLifecycleAuthority`.
   - Documentation comparison: diverges from `PAYMENT_REFERENCE_FREEZE.md`.

## Documentation Validation

Documents read after source inspection:
- `docs/architecture/PAYMENT_REFERENCE_FREEZE.md`
- `docs/architecture/PAYMENT_RUNTIME_FINAL_CERTIFICATION.md`
- `docs/architecture/PAYMENT_0_5E_COD_DISCOVERY.md`
- `docs/architecture/storefront-payment-authority-certification-findings.md`
- `docs/architecture/storefront-shipping-authority-certification-findings.md`

Agreement:
- COD must not be treated as Stripe, Paymob, Fawry, or manual wallet.
- COD does not mark orders paid at checkout.
- Standard COD becomes collected only through payment lifecycle after fulfillment delivery authority.
- Shipping COD collection facts are evidence, not payment truth.
- Runtime is certified but activation/integration is a separate phase.

Contradiction or gap:
- `PAYMENT_REFERENCE_FREEZE.md` requires provider/workflow execution through runtime bridge and exact runtime admission.
- Active COD standard source bypasses runtime bridge and runtime orchestrator.
- Active CommerceHub COD bypasses `PaymentLifecycleService` and writes through `CommerceHubPaymentLifecycleAuthority`.
- COD discovery documentation already identifies incomplete dedicated collection evidence; source confirms this.

## Architecture Divergences

1. Divergence: COD is not implemented as a runtime-adapted reference provider.
   - Source evidence: no active `CodRuntimeBridge`; no COD call path to `PaymentRuntimeOrchestrator`.
   - Classes: `PaymentService`, `PaymentLifecycleService`, `CommerceHubPaymentLifecycleAuthority`.
   - Execution path: COD goes to lifecycle or CommerceHub authority directly.
   - Documentation comparison: diverges from `PAYMENT_REFERENCE_FREEZE.md`.

2. Divergence: COD has two payment completion writers by bounded context.
   - Source evidence: standard orders use `PaymentLifecycleService`; CommerceHub uses `CommerceHubPaymentLifecycleAuthority`.
   - Classes: `PaymentLifecycleService`, `CommerceHubPaymentLifecycleAuthority`, `ApplicationDbContext`.
   - Execution path: standard and CommerceHub graphs differ.
   - Documentation comparison: diverges from the freeze single durable lifecycle writer statement for reference providers.

3. Divergence: Dedicated COD collection evidence is not canonical.
   - Source evidence: `ShipmentCodCollectedEvent` is produced, but no source path to payment lifecycle was found. Payment lifecycle collection is driven by fulfillment delivered events or admin lifecycle request.
   - Classes: `CarrierShipmentService`, `ShipmentCodCollectedEvent`, `FulfillmentDeliveredEventConsumer`, `PaymentLifecycleController`.
   - Execution path: carrier collection evidence remains shipment/event evidence; fulfillment delivery triggers payment.
   - Documentation comparison: agrees with `PAYMENT_0_5E_COD_DISCOVERY.md` gap.

4. Divergence: CommerceHub COD uses marketplace financial workflow instead of standard settlement batch lifecycle.
   - Source evidence: `CommerceHubFinancialWorkflowService.ApplyDeliveredFinancialsAsync(...)` and CommerceHub debt/payout services handle marketplace money flow.
   - Classes: `CommerceHubFinancialWorkflowService`, `CommerceHubPaymentLifecycleAuthority`, `PaymentLifecycleService`.
   - Execution path: delivered suborder -> CommerceHub financial workflow -> CommerceHub payment authority.
   - Documentation comparison: marketplace paths require independent certification under the freeze.

## Reusable Components

### Reusable

- `PaymentLifecycleService` for standard COD collection, settlement validation, idempotency, outbox staging, and SQL-truth posting.
- `PaymentStatusSaveChangesInterceptor` for protecting standard `Order.PaymentStatus`.
- `OrderStatusSaveChangesInterceptor` for protecting standard order lifecycle writes.
- `AccountingOutbox` and accounting posting from committed SQL truth.
- `FulfillmentDeliveredEventConsumer` as the current standard bridge from fulfillment delivery to lifecycle collection.
- `CarrierShipmentService` shipment COD fields and carrier parsing as collection evidence sources.
- `PaymentSettlementBatch` for standard COD settlement.
- Existing settlement/replay/idempotency tests around COD lifecycle and settlement.

### Partially reusable

- Payment Runtime and `PaymentRuntimeOrchestrator`: certified and provider-neutral, but not connected to active COD.
- Runtime bridge pattern: reusable for COD only after a COD runtime application service and COD runtime bridge exist.
- Evidence authority model: reusable conceptually, but active COD lacks canonical evidence admission by driver/carrier/courier/admin/manual evidence type.
- CommerceHub boundary scopes: reusable inside CommerceHub only, not as a substitute for standard payment lifecycle.
- CommerceHub financial workflow: reusable for marketplace COD money flow, but not for standard storefront COD.

### Must not be reused

- Stripe, Paymob, or Fawry provider adapters as COD implementations.
- Online payment webhook processing for COD payment completion.
- Manual wallet proof image flow as COD collection proof.
- Provider payment intent/session assumptions that require external gateway credentials for COD.
- CommerceHub payment lifecycle authority as the standard storefront payment writer without a deliberate bounded-context migration.

## Required New Components

These are gaps proven by source. They are not implementation instructions.

1. COD runtime application service and runtime bridge.
   - Evidence: freeze requires runtime bridge; active source has no COD bridge.
   - Classes absent: `CodRuntimeApplicationService`, `CodRuntimeBridge`.

2. Canonical COD evidence admission model.
   - Evidence: shipment events and fulfillment delivered events exist, but no canonical COD evidence artifact is admitted through runtime.
   - Source classes: `ShipmentCodCollectedEvent`, `FulfillmentDeliveredEventConsumer`, `PaymentLifecycleController`.

3. Explicit COD collection evidence types.
   - Evidence: no durable model found for driver, carrier, courier, admin, and manual COD evidence as distinct payment evidence.
   - Source classes: `Shipment`, `PaymentIntent`, `CommerceHubPaymentLifecycleAuthority`.

4. Standard/CommerceHub COD authority reconciliation.
   - Evidence: two writers exist for COD completion.
   - Source classes: `PaymentLifecycleService`, `CommerceHubPaymentLifecycleAuthority`.

5. COD exception lifecycle.
   - Evidence: current source has generic delivery, collection, settlement, cancellation, and return mechanisms, but no first-class COD not-collected, refused, partial collection, discrepancy, or RTO payment states were proven.
   - Source classes: `PaymentLifecycleService`, `Shipment`, `CommerceHubOrderManagementService`.

## Unnecessary Components

These are not required by current source behavior:

- A COD gateway webhook endpoint. Source blocks COD webhook processing.
- A COD external credential provider. COD checkout and confirmation do not require gateway credentials.
- A COD implementation inside Stripe, Paymob, or Fawry adapters.
- A shipping-owned payment mutation path. Shipping already produces evidence fields/events; payment truth belongs outside shipping.
- A runtime change. The freeze requires providers and workflows to adapt to runtime, not runtime to adapt to COD.

## Recommended Certification Roadmap

1. COD software discovery certification.
   - Certify the current source facts in this report, including standard and CommerceHub divergence.

2. COD evidence certification.
   - Certify driver, carrier, courier, admin, and manual evidence sources as durable evidence inputs.

3. COD runtime bridge certification.
   - Add and certify COD runtime application service, COD runtime bridge, and exact runtime outcome admission.

4. COD lifecycle convergence certification.
   - Certify that standard COD and CommerceHub COD have explicit bounded-context ownership and no unauthorized payment writers.

5. COD settlement/remittance certification.
   - Certify standard settlement batch and CommerceHub marketplace debt/remittance as separate or unified certified flows.

6. COD exception certification.
   - Certify refused, not collected, partial collection, RTO, return, discrepancy, and post-collection refund paths if those are required business states.

## Final Answers

### What is COD?

COD is a hybrid payment method plus fulfillment-driven collection policy. It is not proven to be an active Payment Reference Provider.

Source evidence:
- `CheckoutService.CreateOrderAsync(...)` treats COD as a payment method/offline order state.
- `PaymentService.ConfirmCodOrderAsync(...)` confirms order fulfillment readiness, not payment collection.
- `PaymentLifecycleService.MarkCodDeliveredAsync(...)` collects payment only after delivery evidence.
- `CommerceHubPaymentLifecycleAuthority.MarkCodSubOrderCollected(...)` treats CommerceHub COD as delivered-suborder financial collection.

Documentation comparison:
- `PAYMENT_0_5E_COD_DISCOVERY.md` calls COD provider-compatible in concept.
- `PAYMENT_REFERENCE_FREEZE.md` says COD is ready to adapt, not already reference-certified.

### Is COD a Payment Provider?

No, not in active source. COD has provider-like labels and gateway strings in places, but no active COD provider adapter, runtime bridge, or gateway execution path was found.

### Is COD a Reference Provider?

No. Active COD does not follow the certified reference-provider path through runtime bridge, `PaymentRuntimeOrchestrator`, exact runtime outcome admission, and `PaymentLifecycleService`.

### Is COD a Reference Policy?

Yes for current implementation. COD is implemented as an offline payment method and delivery/collection policy, with separate standard and CommerceHub lifecycle writers.

### Should COD follow Stripe/Fawry architecture?

COD should follow the same frozen reference architecture boundaries, not Stripe/Fawry provider protocols. The source supports using runtime/lifecycle/authority patterns, but not gateway webhook or credential behavior.

### Should COD reuse Payment Runtime?

Yes, if COD is to become reference-provider-certified. Current source does not reuse it.

### Should COD reuse Lifecycle?

Yes for standard orders. Current standard COD already uses `PaymentLifecycleService`. CommerceHub currently does not; that divergence must be explicitly certified or reconciled.

### Should COD reuse Authorities?

Yes for authority patterns and mutation guards. Current source reuses standard payment lifecycle authorities for standard orders and CommerceHub boundary authorities for marketplace orders. Cross-context ownership is not unified.

### Who owns payment completion?

Standard orders: `PaymentLifecycleService`.

CommerceHub: `CommerceHubPaymentLifecycleAuthority`.

Single global COD owner: Not proven.

### Who produces collection evidence?

Standard source producers:
- Fulfillment delivery via `FulfillmentDeliveredEvent`.
- Carrier/shipment evidence via `Shipment.CodCollected` and `ShipmentCodCollectedEvent`.
- Admin lifecycle request via `PaymentLifecycleController`.

CommerceHub producer:
- Delivered suborder transition through `CommerceHubOrderManagementService`.

Dedicated driver/carrier/courier/admin/manual COD evidence model: Not proven.

### Who admits collection evidence?

Standard orders: `PaymentLifecycleService` admits collection after fulfillment delivery evidence and SQL state validation.

CommerceHub: `CommerceHubOrderManagementService`, `CommerceHubFinancialWorkflowService`, and `CommerceHubPaymentLifecycleAuthority` admit the delivered suborder financial collection path.

### Who writes payment completion?

Standard orders: `PaymentLifecycleService.HandleSuccessAsync(...)`.

CommerceHub: `CommerceHubPaymentLifecycleAuthority.MarkCodSubOrderCollected(...)`.

### Who writes Order Paid?

Standard orders: `PaymentLifecycleService.HandleSuccessAsync(...)` writes `Order.IsPaid`, `PaidAt`, and `PaymentStatus.Collected`.

CommerceHub: `CommerceHubPaymentLifecycleAuthority.MarkCodSubOrderCollected(...)` writes CommerceHub paid timestamps and payment status. A standard `Order.IsPaid` write is not part of the CommerceHub suborder path.

### Who owns shipment completion?

Standard shipment status is owned by shipment/fulfillment services:
- `CarrierShipmentService` owns shipment carrier/tracking state.
- `IOrderFulfillmentService`/fulfillment delivery owns fulfillment completion and publishes delivery events.

CommerceHub shipment completion through a `Shipment` entity was not proven. CommerceHub suborder delivery is owned by `CommerceHubOrderManagementService`.

### Which bounded context owns cash collection, settlement, remittance, driver evidence, carrier evidence, treasury?

- Cash collection: standard `PaymentLifecycleService`; CommerceHub `CommerceHubPaymentLifecycleAuthority`.
- Settlement: standard `PaymentLifecycleService.RecordCodSettlementAsync(...)`; CommerceHub financial/debt/payout services.
- Remittance: CommerceHub financial/debt/payout services for marketplace; standard remittance beyond COD settlement batch is not separately proven.
- Driver evidence: Not proven as a distinct source model.
- Carrier evidence: `CarrierShipmentService` and `Shipment`.
- Treasury/accounting: standard payment lifecycle accounting outbox and CommerceHub financial workflow/ledger services.

### Does COD currently violate PAYMENT_REFERENCE_FREEZE?

Yes for reference-provider compliance. Active COD bypasses required runtime bridge and exact runtime admission.

No for standard lifecycle mutation guard intent. Standard COD uses `PaymentLifecycleService` as durable payment writer and shipping does not write payment truth.

CommerceHub divergence remains: CommerceHub COD writes through `CommerceHubPaymentLifecycleAuthority`, not `PaymentLifecycleService`.

### Estimated implementation effort

Medium to high.

Reason from source:
- Standard lifecycle, settlement, shipment evidence fields, and outbox already exist.
- Missing runtime bridge, canonical COD evidence admission, evidence type model, and CommerceHub/standard authority reconciliation are cross-cutting.

### Estimated certification phases

Six phases:
1. Current-source COD certification.
2. COD evidence model certification.
3. COD runtime bridge certification.
4. COD lifecycle convergence certification.
5. COD settlement/remittance certification.
6. COD exception-state certification.

### Recommended next mission

Run a COD runtime-adaptation design certification mission that starts from this source graph and specifies only the missing bridge, evidence admission, and ownership reconciliation needed to make COD comply with `PAYMENT_REFERENCE_FREEZE.md`.

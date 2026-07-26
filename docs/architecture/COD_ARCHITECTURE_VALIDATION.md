# COD Architecture Validation

## Scope

Primary input: `docs/architecture/COD_REFERENCE_ARCHITECTURE_DISCOVERY.md`.

Validation rule used for this report: executable source is authoritative. Markdown was treated only as the claim source to validate.

## Executive Verdict

The discovery is mostly accurate.

Confirmed:
- COD is implemented as an offline payment method plus delivery/fulfillment collection policy, not as a certified runtime payment reference provider.
- Standard storefront COD payment completion is owned by `PaymentLifecycleService`.
- CommerceHub COD payment completion is owned by `CommerceHubPaymentLifecycleAuthority`, reached from CommerceHub order/financial workflow.
- There is no single global COD owner across all executable source contexts.
- COD does not currently participate in Payment Runtime.
- Carrier shipment COD collection exists as evidence, but it does not reach payment truth in the active source.

Rejected or corrected:
- The phrase "PaymentLifecycleService is the owner of payment completion" is true only for standard `Order` payment lifecycle. It is false globally because CommerceHub marketplace/sub-order payment lifecycle has a separate authority.
- Runtime exists and is actively registered for Stripe, Paymob, Fawry, and Manual Payment. The correct conclusion is not "Runtime is unused"; it is "COD does not reach Runtime."

Uncertain:
- Whether CommerceHub is formally certified as a separate bounded payment context is not provable from source alone. Source proves deliberate boundary guards and separate authorities, but certification status is outside executable behavior.

Final answers:
- Was the discovery accurate? Yes, with the scope correction above.
- Does COD currently satisfy `PAYMENT_REFERENCE_FREEZE`? No, not as a reference-provider flow, because COD bypasses Runtime and has split completion authorities.
- Does Runtime participate in COD? No.
- How many lifecycle writers exist? Two COD payment completion authorities: standard `PaymentLifecycleService` and CommerceHub `CommerceHubPaymentLifecycleAuthority`. Additional CommerceHub refund/compensation services can invoke the CommerceHub authority for refund-state transitions.
- How many payment completion authorities exist? Two for COD completion.
- Can implementation safely begin? Yes for work scoped from this validated graph; no additional discovery is required for the questions in this mission.

## Validation Matrix

| Discovery claim | Validated? | Confidence | Concrete source evidence | Counter evidence |
|---|---:|---:|---|---|
| COD is payment method plus fulfillment policy | YES | High | `CheckoutService` creates standard orders with `PaymentStatus.Pending`, `PaymentMethodCode`, `IsPaid = false`; `PaymentService` COD branch skips provider payment and confirms fulfillment; `FulfillmentDeliveredEventConsumer` calls `MarkCodDeliveredAsync`. | CommerceHub also has `PaymentStatus.CashOnDelivery`, but still uses delivery financial workflow, not provider runtime. |
| COD is not an active provider adapter | YES | High | `PaymentService` has `PaymentProvider.Cod` branch that creates `PaymentIntentStatus.Skipped`; active adapter registrations are Stripe/Paymob/Fawry/manual payment. No `CodRuntimeBridge`, `CodPaymentAdapter`, or COD provider registration found. | `PaymentProvider.Cod`, `PaymentGateway = "cod"`, and `PaymentIntent.Provider = Cod` exist as labels/state. |
| COD has no Runtime Bridge | YES | High | Runtime bridge/application services are registered for Stripe, Paymob, Fawry, Manual Payment; COD paths call lifecycle or CommerceHub authority directly. | Runtime exists and is active for other providers. |
| Standard COD completion is lifecycle-owned | YES | High | `FulfillmentDeliveredEventConsumer` -> `IPaymentLifecycleService.MarkCodDeliveredAsync`; `PaymentLifecycleController` -> `HandlePaymentResultAsync` with `CodDelivery`; `PaymentLifecycleService.HandleSuccessAsync` writes `Collected`, `IsPaid`, `PaidAt`. | CommerceHub does not use this standard writer. |
| CommerceHub COD has separate completion owner | YES | High | `CommerceHubOrderManagementService.UpdateSubOrderStatusAsync` calls `CommerceHubFinancialWorkflowService.ApplyDeliveredFinancialsAsync`; that calls `CommerceHubPaymentLifecycleAuthority.MarkCodSubOrderCollected`. | None found for CommerceHub COD using `IPaymentLifecycleService`. |
| There is no single global COD owner | YES | High | Standard ownership graph and CommerceHub ownership graph terminate in different authorities and different entities. | Source has guards in both contexts, so split appears deliberate, not accidental. |
| Shipment COD collection is evidence, not payment truth | YES | High | `CarrierShipmentService` writes `Shipment.CodCollected` and publishes `ShipmentCodCollectedEvent`; no consumer for `ShipmentCodCollectedEvent` was found; payment truth is driven by `FulfillmentDeliveredEvent` or admin lifecycle endpoint. | Carrier recovery code excludes delivered pending COD from shipment authority recovery, preserving lifecycle responsibility. |
| CommerceHub authority is architectural duplication | NO as source fact; UNCERTAIN as certification claim | Medium | `ApplicationDbContext.GuardCommerceHubBoundaryOwnership` explicitly protects CommerceHub marketplace/sub-order payment fields with `CommerceHubBoundaryMutationScope.IsPaymentLifecycleAuthorized`. | It still duplicates payment lifecycle ownership relative to the global reference freeze model. |
| Active COD satisfies reference freeze | NO | High | Required runtime path is absent for COD; no COD runtime application service/bridge; completion does not flow through `PaymentRuntimeOrchestrator`. | Standard lifecycle mutation guard intent is satisfied for standard orders. |

## Ownership Graph

Standard storefront COD:

```text
CheckoutService
-> Order.PaymentMethodCode = COD
-> Order.PaymentStatus = Pending
-> Order.IsPaid = false

PaymentService.CreatePaymentIntentAsync / ConfirmCodOrderAsync
-> PaymentIntent.Provider = Cod
-> PaymentIntent.Status = Skipped
-> Order.Status = Confirmed
-> no payment collection

OrderFulfillmentService
-> FulfillmentDeliveredEvent
-> FulfillmentDeliveredEventConsumer
-> PaymentLifecycleService.MarkCodDeliveredAsync
-> PaymentLifecycleService.HandlePaymentResultAsync
-> PaymentLifecycleService.HandleSuccessAsync
-> Order.PaymentStatus = Collected
-> Order.IsPaid = true when items are fully paid
-> Order.PaidAt = now

PaymentLifecycleController.RecordCodSettlement
-> PaymentLifecycleService.RecordCodSettlementAsync
-> Order.PaymentStatus = Settled
```

Standard admin/manual lifecycle:

```text
PaymentLifecycleController.MarkCodDelivered
-> IPaymentLifecycleService.HandlePaymentResultAsync(Source = CodDelivery)
-> PaymentLifecycleService.HandleSuccessAsync
-> Order payment truth
```

CommerceHub COD:

```text
CommerceHubCheckoutService
-> CommerceHubMarketplaceOrder.PaymentMethodCode = COD
-> CommerceHubMarketplaceOrder.PaymentGateway = cod
-> CommerceHubMarketplaceOrder.PaymentStatus = CashOnDelivery
-> CommerceHubSubOrder.PaymentMethod = CashOnDelivery
-> CommerceHubSubOrder.PaymentStatus = CashOnDelivery

CommerceHubOrderManagementService.UpdateSubOrderStatusAsync(Delivered)
-> CommerceHubFinancialWorkflowService.ApplyDeliveredFinancialsAsync
-> CommerceHubPaymentLifecycleAuthority.MarkCodSubOrderCollected
-> CommerceHubSubOrder.PaymentStatus = Collected
-> CommerceHubSubOrder.PaidAt = now
-> CommerceHubMarketplaceOrder.PaymentStatus = Collected when all COD suborders are collected
-> CommerceHubMarketplaceOrder.PaidAt = now
```

CommerceHub refunds/compensation:

```text
CommerceHubRefundService / RefundExecutionService / CommerceHubPaymentCompensationOutboxWorker
-> CommerceHubPaymentLifecycleAuthority.ApplyRefundPaymentState
-> CommerceHubSubOrder.PaymentStatus = Refunded or PartiallyRefunded
-> CommerceHubMarketplaceOrder.PaymentStatus = aggregate suborder payment status
```

## Runtime Reachability Graph

Active Runtime paths:

```text
StripePaymentService
-> StripeRuntimeApplicationService
-> StripeRuntimeBridge
-> PaymentRuntimeOrchestrator

PaymobPaymentService
-> PaymobRuntimeApplicationService
-> PaymobRuntimeBridge
-> PaymentRuntimeOrchestrator

PaymentService for Fawry
-> FawryRuntimeApplicationService
-> FawryRuntimeBridge
-> PaymentRuntimeOrchestrator

PaymentService for Manual Wallet
-> ManualPaymentRuntimeApplicationService
-> ManualPaymentRuntimeBridge
-> PaymentRuntimeOrchestrator
-> PaymentLifecycleService.AdmitManualWalletReviewAsync
```

COD paths:

```text
Standard COD
-> PaymentService COD branch or fulfillment delivery
-> PaymentLifecycleService
-> no Runtime Application Service
-> no Runtime Bridge
-> no PaymentRuntimeOrchestrator

CommerceHub COD
-> CommerceHubOrderManagementService
-> CommerceHubFinancialWorkflowService
-> CommerceHubPaymentLifecycleAuthority
-> no Runtime Application Service
-> no Runtime Bridge
-> no PaymentRuntimeOrchestrator
```

Conclusion: Runtime is not dead globally, but COD bypasses it completely.

## Lifecycle Writer Enumeration

Standard `Order` payment fields:
- Initial pending state: `CheckoutService` and admin order creation can create orders with `PaymentStatus.Pending`; this is initial state, not payment completion.
- Completion/collection: `PaymentLifecycleService.HandleSuccessAsync` writes `Order.PaymentStatus = Collected`, `Order.IsPaid`, and `Order.PaidAt`.
- Settlement: `PaymentLifecycleService.RecordCodSettlementAsync` and `RecordGatewaySettlementAsync` write `Order.PaymentStatus = Settled`.
- Refund: `PaymentLifecycleService.RecordRefundAsync` writes `Order.PaymentStatus = Refunded` or `PartiallyRefunded`.
- Expiry/failure/cancellation/rejection: `PaymentLifecycleService.HandleFailureAsync`, late-payment quarantine, and amount-mismatch quarantine write terminal non-collected statuses and clear `IsPaid`/`PaidAt`.
- Guard: `PaymentStatusSaveChangesInterceptor` blocks direct `Order.PaymentStatus` mutation unless authorized.

CommerceHub marketplace/sub-order payment fields:
- Initial COD state: `CommerceHubCheckoutService` creates marketplace/suborders with `PaymentStatus.CashOnDelivery`.
- Completion/collection: `CommerceHubPaymentLifecycleAuthority.MarkCodSubOrderCollected` and `MarkOnlinePaymentCollected` write `Collected` and `PaidAt`.
- Retry: `CommerceHubPaymentLifecycleAuthority.ResetOnlinePaymentToPendingForRetry` writes `Pending` and clears `PaidAt`.
- Refund: `CommerceHubPaymentLifecycleAuthority.ApplyRefundPaymentState` writes `Refunded` or `PartiallyRefunded`; called by CommerceHub refund and compensation flows.
- Guard: `ApplicationDbContext.GuardCommerceHubBoundaryOwnership` protects marketplace/sub-order `PaymentStatus`, `PaymentReferenceId`, and `PaidAt` with the CommerceHub payment lifecycle boundary.

Single writer? No globally.

Authorized writers? Yes by context:
- Standard `Order`: `PaymentLifecycleService` under `PaymentStatusSaveChangesInterceptor`.
- CommerceHub marketplace/sub-order: CommerceHub payment lifecycle authority/boundary under `CommerceHubBoundaryMutationScope`.

## Evidence Validation

Driver:
- No distinct driver COD evidence model or driver-specific COD admission path found.

Courier/carrier:
- Carrier adapters parse COD collection into `IShippingCarrierAdapter` tracking/webhook results.
- `CarrierShipmentService` writes `Shipment.CodCollected` and `Shipment.CodCollectedAtUtc`.
- `CarrierShipmentService` publishes `ShipmentCodCollectedEvent`.
- No active consumer of `ShipmentCodCollectedEvent` was found, so carrier evidence does not write payment truth.

Shipment:
- `Shipment` stores `IsCashOnDelivery`, `CodAmount`, `CodCollected`, `CodCollectedAtUtc`.
- Shipment delivery can publish delivery events, but standard payment truth is admitted through fulfillment delivery/lifecycle, not by mutating `Order.PaymentStatus` in shipping.

Admin/manual:
- Admin lifecycle endpoint can call `PaymentLifecycleService` for COD delivered and COD settlement.
- Manual wallet proof/evidence paths are Manual Payment, not COD evidence.

CommerceHub admin/vendor delivery:
- Delivered suborder transition is admitted by `CommerceHubOrderManagementService`, then financial workflow admits COD collection through `CommerceHubPaymentLifecycleAuthority`.

Who writes payment truth:
- Standard: `PaymentLifecycleService`.
- CommerceHub: `CommerceHubPaymentLifecycleAuthority`.

## Dead, Dormant, and Non-COD Runtime Code

Not dead globally:
- `PaymentRuntimeOrchestrator`.
- Stripe/Paymob/Fawry/manual payment runtime bridges and application services.

Dormant or bypassed for COD:
- No COD runtime bridge/application service exists.
- `ShipmentCodCollectedEvent` is produced but no consumer was found that admits it into `PaymentLifecycleService`.
- Infrastructure `PaymentRuntimeServiceCollectionExtensions` registers disabled placeholder runtime/provider/policy services; active API provider registrations use concrete provider runtime bridges elsewhere.

Legacy/non-authoritative paths:
- Obsolete manual wallet confirmation/rejection methods in `PaymentService` are disabled in favor of `IPaymentLifecycleService`.
- `PaymentStatus.Paid` remains as deprecated compatibility status; active collection writes use `Collected`.

## CommerceHub Authority Judgment

From executable source, `CommerceHubPaymentLifecycleAuthority` is a legitimate bounded-context authority, not an accidental duplicate:
- It is internal/static and used only inside CommerceHub services.
- CommerceHub checkout, financial workflow, payment service, refund service, refund execution, and compensation worker coordinate through CommerceHub boundary scopes.
- `ApplicationDbContext` explicitly protects CommerceHub marketplace/sub-order payment fields with the CommerceHub payment lifecycle boundary.

However, relative to a single global reference-provider freeze, it is still a separate payment completion authority. Therefore:
- Legitimate bounded-context authority in current source: YES.
- Single global lifecycle owner: NO.
- Reference-freeze compliant for COD as a global reference provider: NO.

## Final Answers

Was the Discovery accurate?
Yes, mostly. Its core conclusions are confirmed. The main correction is that "PaymentLifecycleService owns payment completion" must be scoped to standard orders.

Which conclusions are confirmed?
COD is an offline method plus fulfillment policy; no active COD provider adapter; no COD runtime bridge; standard completion uses `PaymentLifecycleService`; CommerceHub completion uses `CommerceHubPaymentLifecycleAuthority`; no single global COD owner.

Which conclusions are rejected?
Any global reading that `PaymentLifecycleService` is the only COD completion owner is rejected. Runtime is not generally unused, only bypassed by COD.

Which conclusions remain uncertain?
Formal certification of CommerceHub as an independently accepted bounded payment context remains uncertain from source alone.

Does COD currently satisfy `PAYMENT_REFERENCE_FREEZE`?
No. COD does not flow through Runtime Application Service, Runtime Bridge, `PaymentRuntimeOrchestrator`, and exact runtime outcome admission.

Does Runtime participate?
No for COD. Yes for other providers.

How many lifecycle writers exist?
Two COD payment completion authorities: `PaymentLifecycleService` for standard orders and `CommerceHubPaymentLifecycleAuthority` for CommerceHub marketplace/sub-orders.

How many payment completion authorities exist?
Two.

Can implementation safely begin?
Yes, if implementation starts from this validated ownership graph and does not assume a single global COD owner or existing COD Runtime participation.

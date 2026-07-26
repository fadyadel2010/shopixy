# PAYMENT-IMPLEMENTATION-3: Canonical CommerceHub Payment Completion Pipeline

## Executive Summary

CommerceHub payment completion now remains inside the CommerceHub payment runtime boundary after provider verification. Provider adapters authenticate, create sessions, and verify protocol notifications only. They do not complete orders, inventory, earnings, settlement, accounting, outbox, or notification workflows.

## Objective

Convert a verified provider payment success notification into CommerceHub-owned business completion through a deterministic, idempotent pipeline.

## Motivation

Provider success proves only that the provider accepted or captured a payment. CommerceHub must decide whether that evidence may complete business state. This prevents provider callbacks from bypassing order authority, inventory authority, vendor earnings, settlement, accounting, and outbox consistency.

## Architectural Context

The starting flow is:

Verified Provider Webhook -> Provider Adapter -> CommerceHubPaymentService -> Canonical Completion Pipeline.

`PaymobAdapter` remains protocol-only. `CommerceHubPaymentService` is the canonical completion boundary. `CommerceHubFinancialWorkflowService` and `CommerceHubFinancialLedgerService` remain CommerceHub-owned financial authorities.

## Existing Completion Flow

Before this phase, `CommerceHubPaymentService` verified provider evidence, locked payment processing, marked payments paid, moved orders to collected, invoked financial workflow, and staged status events. Inventory was validated but not explicitly finalized for paid orders, and the completion sequence was not represented as a named canonical pipeline with completion telemetry and canonical completion events.

## New Canonical Completion Pipeline

The success branch in `CommerceHubPaymentService.ApplyWebhookOutcomeAsync` now performs canonical completion after provider verification and inside CommerceHub transaction ownership. It records completion telemetry, persists completion evidence, finalizes active reservations for payment, marks order/payment state paid, invokes financial workflow, and writes canonical completion outbox events.

## Completion Sequence

The deterministic sequence is:

1. Verified provider notification.
2. Idempotency validation through processed-payment and provider identity guards.
3. Load payment.
4. Acquire payment processing lock.
5. Validate payment state.
6. Validate order state.
7. Validate provider evidence, amount, currency, and identity.
8. Mark payment completed.
9. Persist completion evidence in `CommerceHubPaymentLog`.
10. Finalize inventory reservation for payment.
11. Mark marketplace order and online sub-orders paid/collected.
12. Create vendor earnings through `CommerceHubFinancialWorkflowService`.
13. Create settlement/accounting entries through existing CommerceHub financial ledger records.
14. Publish accounting events through `AccountingOutboxMessage` created by the financial ledger service.
15. Write canonical domain outbox events.
16. Persist and commit transaction.
17. Release lock.
18. Notification request is delivered from outbox after commit.

## Business Ownership

CommerceHub owns payment completion, order completion, inventory finalization, vendor earnings, settlement/accounting records, accounting outbox events, domain outbox events, and notification requests.

Paymob owns protocol authentication, hosted checkout session creation, webhook signature verification, and provider payload normalization only.

## Idempotency Strategy

Duplicate webhooks, callbacks, and retries are handled by payment processing locks, provider identity checks, payment processed guards, single-paid payment indexes, earning uniqueness by sub-order, ledger idempotency keys, and deterministic event idempotency keys.

Repeated notifications do not duplicate payment completion, order paid state, inventory finalization, earnings, accounting outbox messages, or canonical completion events because the duplicate path exits before completion side effects.

## Inventory Finalization

`IInventoryConsistencyService.FinalizeMarketplaceOrderReservationsForPaymentAsync` finalizes active marketplace order reservations by clearing expiration and recording the payment completion mutation event. It does not decrement stock. Delivery/fulfillment remains responsible for stock consumption through existing delivery commit authority.

## Order Completion

`CommerceHubPaymentLifecycleAuthority.MarkOnlinePaymentCollected` transitions marketplace and online sub-order payment state from pending payment to collected only after payment evidence, amount, currency, order, and inventory validation have passed.

## Vendor Earnings

`CommerceHubFinancialWorkflowService.ApplyOnlinePaymentSuccessAsync` creates vendor earnings once from CommerceHub sub-order financial snapshots. Provider adapters do not calculate earnings.

## Settlement

Settlement remains internal accounting. This phase emits canonical settlement-created completion events and relies on existing CommerceHub financial transaction/accounting outbox records as the authoritative accounting trail. It does not create payouts or call payout runtime.

## Accounting Events

`CommerceHubFinancialLedgerService` creates `AccountingOutboxMessage` rows for CommerceHub ledger postings in the same transaction as business completion. Adapters do not write accounting or ledger records.

## Outbox Events

Canonical completion events are written through the existing outbox-backed `IEventPublisher`:

1. `CommerceHubPaymentCompletedEvent`.
2. `CommerceHubOrderPaidEvent`.
3. `CommerceHubVendorEarningsCreatedEvent`.
4. `CommerceHubSettlementCreatedEvent`.
5. `CommerceHubInventoryFinalizedEvent`.
6. `CommerceHubPaymentNotificationsRequestedEvent`.

## Transaction Boundaries

The completion transaction is owned by `CommerceHubPaymentService`. Provider adapter calls occur before completion transaction entry. No external HTTP calls are performed inside the completion transaction.

## Recovery Strategy

If completion fails before commit, the transaction rolls back and a retry can re-enter the pipeline. If completion commits, duplicate provider deliveries observe the processed payment and do not re-run business side effects. Accounting and domain outbox messages remain durable for post-commit delivery.

## Telemetry

Metrics added to `CommerceHubPaymentOrchestrationTelemetry` include `PaymentCompletionStarted`, `PaymentCompletionSucceeded`, `PaymentCompletionFailed`, `OrderMarkedPaid`, `VendorEarningsCreated`, `SettlementCreated`, `InventoryFinalized`, `OutboxPublished`, and `CompletionDuration`.

## Logging

Structured logs identify completion start, success, inventory finalization, outbox writes, provider, payment, order, transaction identity, correlation id, and duration. Provider protocol logs remain in the adapter boundary.

## Files Added

`PointOfSale.Core/Events/CommerceHubPaymentCompletionEvents.cs`

`PointOfSale.Tests/Architecture/PaymentImplementation3CanonicalCompletionPipelineTests.cs`

`docs/architecture/payment/PAYMENT-IMPLEMENTATION-3_CANONICAL_COMMERCEHUB_PAYMENT_COMPLETION_PIPELINE.md`

## Files Modified

`PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`

`PointOfSale.API/Services/CommerceHub/CommerceHubPaymentLifecycleAuthority.cs`

`PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs`

`PointOfSale.API/Instrumentation/CommerceHubPaymentOrchestrationTelemetry.cs`

`PointOfSale.Tests/CommerceHub/CommerceHubPaymentServiceTests.cs`

## Testing

Unit coverage was added for successful canonical completion and duplicate completion replay. Architecture coverage verifies provider adapter boundaries and runtime ownership of completion.

## Verification

Verification should run the CommerceHub payment service tests and payment implementation architecture tests with `dotnet test`.

## Compatibility

No DTO contracts, checkout UI, provider adapter APIs, payout runtime, or finance runtime APIs were changed. The inventory service interface gained an internal CommerceHub finalization method used by the runtime boundary.

## Remaining Gaps

Settlement remains represented through existing financial records and canonical events rather than a new settlement lifecycle table because payout and finance runtime contracts were intentionally not changed in this phase.

Additional end-to-end tests against the production database provider should validate transactional outbox behavior under real lock and unique-index constraints.

## Next Phase

The next phase should harden recovery jobs for post-commit notification request gaps and add operational dashboards for completion latency, accounting outbox lag, and inventory finalization reconciliation.

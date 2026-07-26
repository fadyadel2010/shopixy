# PAYMENT-CERT-001 - Canonical CommerceHub Payment Runtime Certification

Date: 2026-07-19

Certification decision: FAIL

## Executive Summary

CommerceHub has a canonical Paymob payment path that is substantially implemented and tested:

Checkout creates an online CommerceHub order with reserved inventory and a payment reference, the CommerceHub frontend calls `/api/commercehub/payments/initiate`, `CommerceHubPaymentsController` delegates to `ICommerceHubPaymentOrchestrationService`, orchestration delegates to `ICommerceHubPaymentService`, and the CommerceHub payment service calls Paymob through `IPaymentProviderAdapter`.

The verified Paymob webhook path also contains a deterministic CommerceHub-owned completion pipeline: provider HMAC and payload parsing occur in `PaymobAdapter`, while payment completion, inventory finalization, order paid transition, vendor earnings, ledger/accounting outbox, domain outbox, transaction commit, and notification-request publishing are owned by CommerceHub services.

Certification fails because the implementation is not yet fully provider-independent across the solution:

- Legacy Paymob-specific controllers still bypass `CommerceHubPaymentOrchestrationService` and call `IPaymobPaymentService` directly.
- `PaymobPaymentService` remains a provider-named runtime surface that calls storefront/core payment lifecycle business services for Paymob webhooks.
- `CommerceHubPaymentService` still contains direct Stripe SDK construction and Stripe webhook signature parsing, so the CommerceHub runtime is not provider-neutral for every online provider it supports.
- The abstract core `IPaymentRuntime` DI registration is a disabled placeholder; the certified CommerceHub runtime is `ICommerceHubPaymentService`, not the generic runtime abstraction.

Targeted certification tests passed, but these architecture/runtime boundary issues prevent certification of the stated objective: CommerceHub owning the complete online payment runtime independently of any payment provider.

## Certification Scope

Reviewed implementation areas:

- `PointOfSale.API/Controllers/CommerceHubPaymentsController.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentOrchestrationService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.API/Services/Payments/ProviderContracts/CanonicalPaymentGatewayContracts.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobAdapter.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentLifecycleAuthority.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubFinancialWorkflowService.cs`
- `PointOfSale.API/Services/CommerceHub/FinancialLedger/CommerceHubFinancialLedgerService.cs`
- `PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs`
- Accounting outbox and MassTransit bus outbox registration
- Notification request events
- Payment and Paymob telemetry
- Architecture and unit tests
- DI and configuration
- Recovery and unresolved-payment logic

Targeted verification command:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~PaymentImplementation3CanonicalCompletionPipelineTests|FullyQualifiedName~PaymobProviderAdapterCanonicalIntegrationTests|FullyQualifiedName~CommerceHubPaymentOrchestrationServiceTests|FullyQualifiedName~CommerceHubPaymentServiceTests|FullyQualifiedName~PaymentRuntimeOrchestrationCertificationTests|FullyQualifiedName~PaymentRuntimeFinalCertificationTests" --no-restore
```

Result: Passed. 49 tests passed, 0 failed.

## Runtime Conformance Matrix

| Requirement | Status | Evidence |
|---|---:|---|
| CommerceHub payment controller delegates to orchestration | PASS | `CommerceHubPaymentsController` depends on `ICommerceHubPaymentOrchestrationService` and calls `ExecuteAsync`/`ProcessWebhookAsync` at `PointOfSale.API/Controllers/CommerceHubPaymentsController.cs:24`, `:85`, `:190`. |
| Payment orchestration delegates to canonical payment runtime | PASS | `CommerceHubPaymentOrchestrationService` delegates to `_paymentService.InitiateAsync` and `_paymentService.ProcessWebhookAsync` at `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentOrchestrationService.cs:44`, `:122`, `:144`. |
| CommerceHub runtime uses Paymob through provider abstraction | PASS | `CommerceHubPaymentService` stores `IReadOnlyDictionary<string, IPaymentProviderAdapter>` and uses `GetProviderAdapter(CommerceHubPaymentMethods.Paymob)` for session creation and webhook parsing at `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs:87`, `:676`, `:2798`. |
| Paymob provider owns authentication, API protocol, webhook parsing, HMAC verification, status lookup, and capability discovery | PASS | `PaymobAdapter` implements `IPaymentProviderAdapter`, exposes `PaymentProviderCapabilities`, calls `AuthenticateAsync`, `RegisterOrderAsync`, `CreatePaymentKeyAsync`, verifies HMAC, and supports status lookup at `PointOfSale.API/Services/Payments/Paymob/PaymobAdapter.cs:12`, `:36`, `:84`, `:101`, `:127`, `:187`, `:195`, `:238`. |
| Paymob adapter has no CommerceHub business mutations | PASS | `PaymentImplementation3CanonicalCompletionPipelineTests.ProviderAdapter_MustRemainProtocolOnlyAndNotOwnBusinessCompletion` asserts no `CommerceHubMarketplaceOrder`, inventory, vendor settlement, vendor earning, outbox, or accounting references in `PaymobAdapter`. |
| Verified provider notification enters canonical completion pipeline | PASS | `ProcessVerifiedPaymobWebhookAsync` parses Paymob evidence through adapter and calls `ApplyWebhookOutcomeAsync` at `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs:676`, `:864`. |
| Completion pipeline validates payment/order/provider evidence | PASS | `ApplyWebhookOutcomeAsync` reloads payment/order in a serializable transaction, calls `EnsurePaymentMatchesMarketplaceOrder`, `EnsureAmountMatches`, and provider identity guards before mutation at `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs:1518` through `:1584`. |
| Completion pipeline persists completion evidence | PASS | Adds `CommerceHubPaymentLog` event type `CanonicalPaymentCompletionEvidence` before finalization at `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs:1848`. |
| Inventory finalized by CommerceHub | PASS | Payment completion calls `FinalizeMarketplaceOrderReservationsForPaymentAsync`; inventory service nulls reservation expiry and updates reservation mutation state at `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs:1862` and `PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs:849`. |
| Order paid transition owned by CommerceHub | PASS | `CommerceHubPaymentLifecycleAuthority.MarkOnlinePaymentCollected` sets marketplace/sub-order payment status and paid timestamps; called from completion at `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs:1872` and authority code at `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentLifecycleAuthority.cs:7`. |
| Vendor earnings owned by CommerceHub | PASS | Financial workflow creates `CommerceHubVendorEarning` rows and audit records in `EnsureEarningRecord` at `PointOfSale.API/Services/CommerceHub/CommerceHubFinancialWorkflowService.cs:482`, `:534`, `:546`. |
| Ledger/accounting outbox owned by CommerceHub | PASS | Ledger service appends `CommerceHubFinancialTransaction` and `AccountingOutboxMessage.Create` in same EF context at `PointOfSale.API/Services/CommerceHub/FinancialLedger/CommerceHubFinancialLedgerService.cs:724`, `:745`, `:755`. |
| Domain outbox and notifications are written before commit | PASS | `WriteCanonicalPaymentCompletionOutboxEventsAsync` publishes payment completed, order paid, vendor earnings, settlement, inventory finalized, and notification request before `SaveChangesAsync`/`CommitAsync` at `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs:1954`, `:2017`, `:2018`, `:2181`-`:2186`. |
| External Paymob HTTP calls occur outside completion transaction | PASS | Paymob webhook parsing happens before `ApplyWebhookOutcomeAsync` opens the serializable transaction; completion transaction begins at `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs:676`, `:1446`, `:1518`. |
| Provider-independent runtime for all online providers | FAIL | `CommerceHubPaymentService` still directly uses Stripe `EventUtility.ConstructEvent` and `new PaymentIntentService()` at `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs:894`, `:2861`, `:3042`. |
| No provider-specific controller bypasses CommerceHub orchestration | FAIL | `PaymobWebhooksController` and `StorefrontPaymobPaymentsController` inject `IPaymobPaymentService` directly at `PointOfSale.API/Controllers/PaymobWebhooksController.cs:39`, `:142` and `PointOfSale.API/Controllers/StorefrontPaymobPaymentsController.cs:33`, `:226`. |
| Generic `IPaymentRuntime` is active in DI | FAIL | `AddPaymentRuntimeFoundation()` registers `IPaymentRuntime` to `DisabledPaymentRuntime` at `PointOfSale.Infrastructure/Payments/Runtime/PaymentRuntimeServiceCollectionExtensions.cs:8`-`:10`; CommerceHub runtime is implemented separately in `ICommerceHubPaymentService`. |

## Business Authority Verification

CommerceHub owns the canonical Paymob completion path:

- Payment lifecycle: `CommerceHubPaymentService.ApplyWebhookOutcomeAsync` sets payment status, paid time, processed markers, failure handling, and duplicate guards.
- Payment completion: the service records `PaymentCompletionStarted`, writes `CanonicalPaymentCompletionEvidence`, and marks payment `Paid`.
- Order completion: `CommerceHubPaymentLifecycleAuthority.MarkOnlinePaymentCollected` sets marketplace and sub-order payment status to collected.
- Inventory authority: `InventoryConsistencyService.FinalizeMarketplaceOrderReservationsForPaymentAsync` owns reservation finalization.
- Vendor earnings: `CommerceHubFinancialWorkflowService.ApplyOnlinePaymentSuccessAsync` calls `EnsureEarningRecord`.
- Settlement/ledger creation: `CommerceHubFinancialLedgerService.RecordOrderAsync` records platform/vendor financial transactions.
- Accounting events: ledger service writes `AccountingOutboxMessage` in the same context as financial transactions.
- Domain outbox events: `CommerceHubPaymentService.WriteCanonicalPaymentCompletionOutboxEventsAsync` publishes canonical completion events.
- Notification requests: the same method publishes `CommerceHubPaymentNotificationsRequestedEvent`; notification request is non-authoritative and after business state is staged.

Providers own only protocol functions in the canonical Paymob adapter:

- `PaymobAdapter` performs authentication, order registration, payment-key creation, hosted checkout URL creation, HMAC verification, webhook parsing, status lookup, provider capabilities, and provider error translation.
- `PaymobAdapter` does not reference CommerceHub order aggregates, inventory reservations, vendor earnings, settlement rows, ledger rows, accounting outbox, or domain outbox.

Business authority gaps:

- Legacy Paymob-specific API surfaces still exist and invoke Paymob-specific runtime services directly.
- Stripe-specific SDK work remains inside `CommerceHubPaymentService`, so provider protocol is not fully isolated behind adapters for every provider.

## Provider Boundary Audit

Forbidden pattern search results:

| Pattern | Result | Evidence |
|---|---:|---|
| Order state mutation inside `PaymobAdapter` | PASS | No CommerceHub order aggregate references in `PaymobAdapter`; architecture test asserts absence. |
| Inventory mutation inside `PaymobAdapter` | PASS | No `InventoryReservation` reference in `PaymobAdapter`; architecture test asserts absence. |
| Vendor earnings inside `PaymobAdapter` | PASS | No `CommerceHubVendorEarning` reference in `PaymobAdapter`; architecture test asserts absence. |
| Settlement creation inside `PaymobAdapter` | PASS | No `CommerceHubVendorSettlement` reference in `PaymobAdapter`; architecture test asserts absence. |
| Accounting/ledger/outbox writes inside `PaymobAdapter` | PASS | No accounting/outbox/ledger references in `PaymobAdapter`; architecture test asserts absence. |
| Business decisions inside `PaymobAdapter` | PASS | Adapter returns canonical provider session/webhook/status results and errors only. |
| Direct Paymob HTTP outside provider boundary | OBSERVATION | HTTP transport lives in `PaymobOfficialApiHttpClient`, a provider-internal class in `PaymobPaymentAdapter.cs`, with endpoints `auth/tokens`, `ecommerce/orders`, `acceptance/payment_keys`, and `SendAsync` at `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentAdapter.cs:228`, `:249`, `:261`, `:273`, `:298`, `:331`. |
| Controller calling `PaymobAdapter` directly | PASS | No direct `PaymobAdapter` reference in `CommerceHubPaymentsController`; architecture test asserts absence. |
| Controller bypassing canonical orchestration | FAIL | Legacy Paymob controllers call `IPaymobPaymentService` directly: `PaymobWebhooksController.cs:39`, `:142`; `StorefrontPaymobPaymentsController.cs:33`, `:226`. |
| Orchestration bypassing CommerceHub payment runtime | PASS | Orchestration delegates to `ICommerceHubPaymentService`. |
| CommerceHubPaymentService bypassing Paymob provider abstraction | PASS | Paymob path uses `IPaymentProviderAdapter`. |
| CommerceHubPaymentService bypassing provider abstraction for other providers | FAIL | Stripe path directly uses Stripe SDK/webhook utilities at `CommerceHubPaymentService.cs:894`, `:2861`, `:3042`. |

## Completion Pipeline Verification

Deterministic successful Paymob completion order:

1. Controller reads raw webhook body and calls orchestration.
2. Orchestration calls `ICommerceHubPaymentService.ProcessWebhookAsync`.
3. Payment service calls Paymob `IPaymentProviderAdapter.ParseWebhookAsync`.
4. Payment service resolves payment by provider reference/intent/transaction identity.
5. Payment service validates non-pending terminal callback, currency, amount, and provider identity.
6. `ApplyWebhookOutcomeAsync` acquires a payment processing lock.
7. A serializable database transaction starts.
8. Payment and marketplace order are reloaded for mutation.
9. Duplicate/processed guards run before financial side effects.
10. Completion evidence is persisted as `CanonicalPaymentCompletionEvidence`.
11. Inventory reservations are finalized.
12. Payment lifecycle authority marks marketplace and sub-orders collected.
13. Financial workflow creates vendor earnings and ledger entries.
14. Accounting outbox messages are staged by ledger service.
15. Canonical payment completion domain events and notification request are published.
16. `SaveChangesAsync` persists mutations/outbox.
17. Transaction commits.
18. Payment completion success telemetry records duration.

Evidence:

- Processing lock: `CommerceHubPaymentService.cs:1468` through `:1518`.
- Serializable transaction: `CommerceHubPaymentService.cs:1518`.
- Duplicate processed guard: `CommerceHubPaymentService.cs:1584` through `:1601`.
- Inventory finalization: `CommerceHubPaymentService.cs:1862`.
- Order paid transition: `CommerceHubPaymentService.cs:1872`.
- Financial workflow: `CommerceHubPaymentService.cs:1879`.
- Outbox publish before commit: `CommerceHubPaymentService.cs:1954`, `:2017`, `:2018`.
- Canonical events: `CommerceHubPaymentService.cs:2181` through `:2186`.

## Transaction Verification

PASS for canonical Paymob completion:

- Paymob HMAC/payload verification occurs before `ApplyWebhookOutcomeAsync` opens its serializable transaction.
- Financial mutations occur after `BeginTransactionAsync(IsolationLevel.Serializable)`.
- Inventory finalization and ledger writes participate in the current EF transaction; `InventoryConsistencyService.ExecuteInTransactionAsync` opens a transaction only when no current transaction exists.
- Domain outbox publishes happen before `SaveChangesAsync` and `CommitAsync`; MassTransit EF outbox is configured with `UseBusOutbox`.
- Accounting outbox rows are added by `CommerceHubFinancialLedgerService` in the same EF context as ledger rows.

Evidence:

- Paymob parse before completion transaction: `CommerceHubPaymentService.cs:676`, `:1446`, `:1518`.
- Commit ordering: `CommerceHubPaymentService.cs:2017`, `:2018`.
- Ledger outbox append: `CommerceHubFinancialLedgerService.cs:724`, `:745`, `:755`.
- Transaction reuse in ledger: `CommerceHubFinancialLedgerService.cs:865`, `:869`.
- MassTransit outbox: `PointOfSale.API/Program.cs:1942`, `:1945`.

FAIL at architecture level for full provider independence:

- Direct Stripe provider SDK calls are inside `CommerceHubPaymentService`, not outside CommerceHub transaction/runtime code.

## Idempotency Verification

Canonical Paymob duplicate/replay protections:

- Payment initiation uses a distributed lock per order/provider.
- Pending session reuse prevents duplicate provider sessions.
- Reinit fence blocks creating a new provider session when a prior provider outcome may exist.
- Unresolved captured provider evidence blocks retry until reconciliation.
- Webhook completion uses a payment processing lock.
- Processed payment guard returns "Webhook was already applied" without repeating inventory, financial workflow, or canonical events.
- Inventory service uses event inbox replay guards and payload conflict detection.
- Ledger service checks idempotency keys and financial fingerprints before adding duplicate financial transactions.
- Accounting outbox idempotency keys are generated per ledger transaction.
- Domain events include deterministic idempotency keys.

Evidence:

- Initiation lock: `CommerceHubPaymentService.cs:158`.
- Unresolved evidence blocks initiation: `CommerceHubPaymentService.cs:192`, `:2618`.
- Processing lock: `CommerceHubPaymentService.cs:1468`.
- Processed guard: `CommerceHubPaymentService.cs:1584`.
- Duplicate completion test: `CommerceHubPaymentServiceTests.ApplyWebhookOutcomeAsync_WhenPaymentAlreadyCompleted_DoesNotDuplicateCompletionSideEffects`.
- Inventory event inbox and replay handling: `InventoryConsistencyService.cs:2017` through `:2092`.
- Ledger idempotency/fingerprint check: `CommerceHubFinancialLedgerService.cs:773` through `:835`.

Assessment:

- Duplicate webhook: PASS.
- Callback replay: PASS for canonical webhook endpoint because callbacks are not completion authority; provider notification drives completion.
- Retry/provider replay/network retry: PASS for canonical Paymob path due to locks, processed guard, unique provider identity checks, and idempotent ledger/inventory logic.
- Duplicate notifications: PASS at request-event level; downstream notification delivery idempotency is outside this certification scope.

## Recovery Verification

Supported recovery evidence:

- Before commit: completion transaction rollback records `PaymentCompletionFailed` with reason `TRANSACTION_ROLLED_BACK`; provider should retry because exceptions bubble to controller 5xx.
- After commit: processed guard treats provider replay as already applied and does not duplicate side effects.
- Provider retry: webhook controller returns retry-worthy 5xx for unexpected errors and canonical payment service returns `ShouldRetry` on lock or missing payment/order during application.
- Unknown provider session outcome: initiation catches provider outcome unknown, persists/marks reconciliation-required state, and blocks duplicate session creation.
- Database rollback during provider session persistence: retry logic and direct durable recovery path exist in `PersistProviderSessionAsync` / `TryPersistProviderSessionDirectlyAsync`.
- Unresolved provider success: evidence persists to `CommerceHubUnresolvedPaymentWebhooks`, optionally routes suspense accounting outbox, and marks known orders on hold.
- Recovery worker compatibility: `CommerceHubPaymentCompensationOutboxWorker` is registered, and unresolved/compensation outbox tables are used for recovery paths.

Evidence:

- Provider session outcome unknown: `CommerceHubPaymentService.cs:389` through `:421`.
- Provider session persistence retry/direct recovery: `CommerceHubPaymentService.cs:3131` through `:3294`.
- Unresolved payment persist transaction: `CommerceHubPaymentService.cs:3627` through `:3876`.
- Completion rollback telemetry: `CommerceHubPaymentService.cs:2049`.
- Compensation worker registration: `PointOfSale.API/Program.cs:504`.

Gap:

- The existence of legacy Paymob webhook/runtime path means provider retry behavior is not exclusively governed by the canonical CommerceHub orchestration endpoint.

## Dependency Verification

Expected canonical Paymob graph:

`CommerceHubPaymentsController -> CommerceHubPaymentOrchestrationService -> CommerceHubPaymentService -> IPaymentProviderAdapter -> PaymobAdapter -> IPaymobOfficialApiClient -> Paymob`

PASS for canonical Paymob CommerceHub path:

- Controller injects orchestration only for CommerceHub payments.
- Orchestration injects `ICommerceHubPaymentService`.
- Payment service injects `IEnumerable<IPaymentProviderAdapter>`.
- Paymob adapter implements `IPaymentProviderAdapter`.
- DI registers `ICommerceHubPaymentService`, `ICommerceHubPaymentOrchestrationService`, and `IPaymentProviderAdapter, PaymobAdapter`.

FAIL for full solution dependency purity:

- Legacy Paymob controllers call `IPaymobPaymentService` directly.
- `IPaymobPaymentService` is registered in DI.
- Stripe direct provider calls remain in `CommerceHubPaymentService`.
- `IPaymentRuntime` is registered to disabled placeholders.

## Configuration Verification

PASS with observations:

- Strongly typed `CommerceHubPaymobOptions` and `PaymobOptionsValidator` exist.
- Paymob supports `Enabled`, `ApiKey`, `IntegrationId`, `IframeId`, `Secret`, `HmacSecret`, `BaseUrl`, `Environment`, `SandboxMode`, timeout, retry count, callback/webhook URLs, feature flags, and capabilities.
- Validator enforces absolute base URL, sandbox/production consistency, production feature flag, numeric integration/iframe IDs, callback/webhook absolute URLs, and required credentials when enabled.
- `appsettings.json` and `appsettings.Production.json` use empty placeholders for credentials; no live Paymob credential was found in the reviewed settings.

Evidence:

- Options and validator: `PointOfSale.API/Configuration/CommerceHubPaymobOptions.cs`.
- DI binding: `PointOfSale.API/Program.cs:187`.
- Placeholder settings: `PointOfSale.API/appsettings.json` Paymob section with empty `ApiKey`, `IntegrationId`, `IframeId`, and `HmacSecret`.

Observation:

- Paymob options are separate from `CommerceHubPaymentsOptions`; Stripe remains embedded in generic payments options and is not adapter-neutral in runtime code.

## Telemetry Verification

PASS for canonical CommerceHub Paymob path:

- CommerceHub telemetry metrics exist for payment execution, webhook receipt, payment completion start/success/failure, completion duration, order marked paid, vendor earnings, settlement, inventory finalization, and outbox publishing.
- Paymob telemetry metrics exist for provider create attempts/success, provider failures, webhook received, replay/duplicate rejection, runtime admission/rejection, refunds/voids/captures, background recovery, retry, and provider latency.
- Meters are registered with OpenTelemetry.

Evidence:

- CommerceHub metrics: `PointOfSale.API/Instrumentation/CommerceHubPaymentOrchestrationTelemetry.cs`.
- Paymob metrics: `PointOfSale.API/Instrumentation/PaymobOperationalTelemetry.cs`.
- Telemetry DI and OpenTelemetry registration: `PointOfSale.API/Program.cs:2085`, `:2089`, `:2108`, `:2133`.

Observation:

- Webhook validation metrics are provider-owned in Paymob telemetry; canonical webhook receipt is CommerceHub-owned.

## Logging Verification

PASS with observations:

- Paymob adapter logs provider-only events such as authentication started/succeeded, hosted checkout creation, webhook validation/rejection, and provider latency.
- CommerceHub payment service logs business events such as initiation, duplicate processing, canonical completion started/succeeded, inventory authority failures, unresolved payment routing, and outbox publishing.
- Financial audit records are emitted by `CommerceHubFinancialWorkflowService` through `ICommerceHubFinancialAuditService`, making financial audit authoritative outside provider logs.

Observation:

- Legacy PaymobPaymentService also logs Paymob business lifecycle events for core storefront orders. That is another symptom of provider-specific runtime still present.

## Test Coverage Assessment

Targeted tests passed:

- `PaymentImplementation3CanonicalCompletionPipelineTests`
- `PaymobProviderAdapterCanonicalIntegrationTests`
- `CommerceHubPaymentOrchestrationServiceTests`
- `CommerceHubPaymentServiceTests`
- `PaymentRuntimeOrchestrationCertificationTests`
- `PaymentRuntimeFinalCertificationTests`

Coverage strengths:

- Canonical completion pipeline architecture invariants.
- Paymob adapter contract, HMAC verification, canonical errors, and DI registration.
- CommerceHub orchestration delegation.
- CommerceHub duplicate webhook non-duplication.
- Core runtime deterministic stage order and fail-closed behavior.
- Inventory idempotency and ledger idempotency have separate unit coverage.
- Accounting outbox critical event retention and coverage tests exist.

Coverage gaps:

- Tests do not fail on legacy Paymob controllers bypassing CommerceHub orchestration.
- Tests do not fail on direct Stripe provider SDK calls inside `CommerceHubPaymentService`.
- Tests do not assert that `IPaymentRuntime` is active in the CommerceHub runtime path.
- Tests primarily validate source/structure and key units; full database integration for serializable completion plus MassTransit EF outbox was not run in this certification slice.

## Architectural Evidence

Business ownership:

- `CommerceHubPaymentService.ApplyWebhookOutcomeAsync` owns terminal payment mutation and completion sequencing.
- `CommerceHubPaymentLifecycleAuthority` owns payment lifecycle state transitions.
- `CommerceHubFinancialWorkflowService` owns order confirmation, vendor earnings, financial audit, and ledger invocation.
- `InventoryConsistencyService` owns inventory reservation finalization.
- `CommerceHubFinancialLedgerService` owns ledger and accounting outbox writes.

Provider ownership:

- `PaymobAdapter` owns Paymob authentication, hosted checkout creation, HMAC verification, callback parsing, status lookup, capabilities, and error translation.
- `PaymobOfficialApiHttpClient` owns Paymob HTTP transport inside the provider package.

Runtime ownership:

- The implemented CommerceHub runtime is `ICommerceHubPaymentService`.
- The pure core payment runtime exists and is tested, but DI registers `IPaymentRuntime` as disabled.

Dependency ownership:

- Canonical Paymob path respects controller -> orchestration -> CommerceHub payment service -> provider adapter -> Paymob.
- Legacy Paymob and Stripe direct paths violate complete provider independence.

Transaction ownership:

- CommerceHub payment service opens the completion transaction.
- Inventory and ledger services join current transaction when present.
- Domain and accounting outbox writes occur before commit.

Recovery ownership:

- CommerceHub owns locks, processed guards, unresolved webhook persistence, provider session recovery, compensation outbox, and retry/rollback behavior.

## Remaining Gaps

Runtime:

- Legacy Paymob webhook/payment intent runtime remains live via `PaymobWebhooksController`, `StorefrontPaymobPaymentsController`, and `IPaymobPaymentService`.
- Generic `IPaymentRuntime` is disabled in DI and not the runtime object used by CommerceHub payment completion.

Architecture:

- Provider-specific Stripe SDK calls remain in `CommerceHubPaymentService`.
- Legacy provider-specific controllers bypass CommerceHub payment orchestration.
- Tests do not enforce absence of all provider-specific runtime paths across the solution.

API:

- `/api/webhooks/paymob` and `/api/storefront/payments/paymob/*` remain provider-specific API surfaces.
- `/api/commercehub/payments/webhook` and `/api/payments/paymob/webhook` exist for canonical CommerceHub webhook handling.

DTO:

- No certification-blocking DTO gap found for canonical CommerceHub Paymob path.

UI:

- CommerceHub frontend uses `/api/commercehub/payments/initiate`; no certification-blocking UI gap found.

Provider:

- Paymob adapter boundary is clean for canonical CommerceHub path.
- Paymob HTTP transport is provider-internal but physically located in `PaymobPaymentAdapter.cs`, not `PaymobAdapter.cs`.

Testing:

- Targeted certification tests pass.
- Missing tests for solution-wide provider-specific controller/runtime bypass.
- Missing test to enforce adapter abstraction for Stripe within `CommerceHubPaymentService`.

Documentation:

- This certification record documents the current failure.

Operations:

- Telemetry and outbox processing are registered.
- No live Paymob credentials were found in reviewed appsettings.

## Certification Decision

FAIL

## Certification Rationale

The canonical CommerceHub Paymob runtime path is implemented with strong evidence: controller delegation, orchestration, CommerceHub-owned completion, provider-only Paymob adapter, serializable transaction, inventory finalization, order paid transition, vendor earnings, ledger/accounting outbox, domain outbox, notification request, recovery handling, telemetry, DI, and targeted tests all support the intended design.

The certification objective, however, is broader than "Paymob adapter works." It asks whether CommerceHub owns the complete online payment runtime independently of any payment provider. The implementation still contains provider-specific runtime bypasses and direct provider SDK logic:

- Legacy Paymob controllers bypass CommerceHub orchestration and call `IPaymobPaymentService` directly.
- `PaymobPaymentService` remains an active Paymob-named runtime service that invokes payment lifecycle business logic.
- `CommerceHubPaymentService` embeds Stripe SDK/webhook logic directly instead of routing every provider through provider adapters.
- The abstract `IPaymentRuntime` is registered as disabled, while CommerceHub runtime ownership is implemented as a service-specific boundary.

Because these are runtime/architecture issues, certification cannot pass. The correct certification decision is FAIL.

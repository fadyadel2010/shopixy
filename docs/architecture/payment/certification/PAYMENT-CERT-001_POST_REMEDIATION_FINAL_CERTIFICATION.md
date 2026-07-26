# PAYMENT-CERT-001 - Post-Remediation Final Canonical CommerceHub Payment Runtime Certification

Date: 2026-07-20

## 1. Executive Summary

Certification decision: PASS WITH OBSERVATIONS.

CommerceHub now owns the active online payment runtime for CommerceHub Stripe and Paymob execution paths. The authoritative runtime chain is:

```text
Controller / Checkout
  -> CommerceHubPaymentOrchestrationService
  -> IPaymentRuntime
  -> CommerceHubPaymentRuntime
  -> CommerceHubPaymentService
  -> IPaymentProviderAdapter
  -> StripeAdapter / PaymobAdapter
  -> Provider
```

No active CommerceHub online payment execution path was found through `IPaymentGateway`, `IProviderPaymentGateway`, `StripePaymentService`, or `PaymobPaymentService`. Those legacy services and gateway contracts remain in source and some DI registrations remain for compatibility and operational health, but they are not the active CommerceHub online runtime authority after REM-002.

The canonical completion pipeline is deterministic: verified provider result, idempotency guard, validation, completion evidence, inventory finalization, order paid state, vendor earnings, settlement/financial ledger, accounting outbox, event/notification outbox, commit, then downstream notification handling.

## 2. Runtime Conformance Matrix

| Requirement | Status | Evidence |
| --- | --- | --- |
| One runtime authority | PASS | `CommerceHubPaymentOrchestrationService` depends on `IPaymentRuntime`; `CommerceHubPaymentRuntime` delegates to `ICommerceHubPaymentService`; `CommerceHubPaymentService` uses `IPaymentProviderAdapter`. |
| One orchestration authority | PASS | Controllers and checkout initiate online payments through `ICommerceHubPaymentOrchestrationService.ExecuteAsync`; no active controller constructor accepts `IStripePaymentService` or `IPaymobPaymentService`. |
| Checkout converged | PASS | `CheckoutService` uses `_paymentOrchestrationService.ExecuteAsync` when `CommerceHubPaymentMethods.IsOnline(paymentMethodCode)` is true; legacy `_paymentService.CreatePaymentIntentAsync` remains for Fawry, COD, and Manual Wallet only. |
| Recovery converged | PASS | `CheckoutPaymentIntentRecoveryService` has `IPaymentRuntime _paymentRuntime` and uses `_paymentRuntime.InitiateAsync` for Stripe/Paymob recovery. |
| Runtime provider-neutral | PASS WITH OBSERVATION | Protocol parsing/signature verification is adapter-owned. Runtime still has provider-selection branches and provider-labeled error mapping, which are non-blocking routing/application concerns. |
| Provider adapters protocol-only | PASS | `StripeAdapter` owns Stripe SDK session creation, webhook construction/signature verification, status lookup, and error translation. `PaymobAdapter` owns Paymob API calls, callback/webhook HMAC verification, callback parsing, status lookup, and error translation. |
| No provider protocol inside runtime | PASS WITH OBSERVATION | `CommerceHubPaymentService` contains no `HMACSHA512`, `EventUtility.ConstructEvent`, provider SDK client creation, `PaymobOptionsValidator`, or callback field ordering; adapter error-code names such as HMAC/SIGNATURE still appear in exception mapping only. |
| Completion pipeline canonical | PASS | `CommerceHubPaymentService` performs completion evidence, inventory finalization, lifecycle marking, financial workflow, outbox writes, save, and commit in a single ordered path. |
| Dependency graph canonical | PASS | `Program.cs` registers `PointOfSale.API.Services.CommerceHub.IPaymentRuntime` to `CommerceHubPaymentRuntime`; `IPaymentGateway` and `IProviderPaymentGateway` are not registered as active CommerceHub online runtime authorities. |
| Transaction boundaries correct | PASS | Payment webhook completion opens a serializable transaction; inventory and ledger services join the ambient EF transaction when present; outbox events are published before `SaveChangesAsync` and commit. |
| Recovery deterministic | PASS | Provider-session gaps, unresolved captured payments, and reservation authority loss block retry; outcome-unknown provider sessions are fenced for reconciliation. |
| Idempotency preserved | PASS | Processed-payment guard, payment logs, provider identity uniqueness, compensation outbox idempotency keys, ledger idempotency keys, and event idempotency keys prevent duplicate completion/effects. |
| Tests support implementation | PASS | Targeted certification run passed: 26 passed, 0 failed. |

## 3. Runtime Authority Audit

Active controller and checkout paths converge on the canonical chain:

- `CommerceHubPaymentsController` delegates initiate and webhook handling to `ICommerceHubPaymentOrchestrationService`.
- `StripeWebhooksController`, `PaymobWebhooksController`, `StorefrontStripePaymentsController`, and `StorefrontPaymobPaymentsController` delegate provider-named routes to CommerceHub orchestration.
- `CheckoutService` routes online `STRIPE` and `PAYMOB` initiation through `_paymentOrchestrationService.ExecuteAsync`.
- `CommerceHubPaymentOrchestrationService` delegates to `IPaymentRuntime`.
- `CommerceHubPaymentRuntime` delegates to `ICommerceHubPaymentService`.
- `CommerceHubPaymentService` resolves `IPaymentProviderAdapter` implementations and delegates provider session creation, webhook parsing, callback parsing, status lookup, and provider error translation.

No active alternative CommerceHub online runtime authority was found.

## 4. Checkout Audit

Result: PASS.

`CheckoutService` does not use `IPaymentGateway`, `StripePaymentService`, or `PaymobPaymentService` for CommerceHub online provider initiation. Online payment session creation is guarded by `CommerceHubPaymentMethods.IsOnline(paymentMethodCode)` and delegates to `ICommerceHubPaymentOrchestrationService.ExecuteAsync`.

Compatibility paths remain:

- Fawry uses legacy local payment intent creation.
- COD uses legacy local payment intent creation.
- Manual Wallet uses local durable evidence behavior and manual payment runtime services.

These paths are outside the CommerceHub online Stripe/Paymob provider runtime and do not fail certification.

## 5. Recovery Audit

Result: PASS.

`CheckoutPaymentIntentRecoveryService` depends on `IPaymentRuntime` for Stripe and Paymob recovery. It constructs `CommerceHubPaymentInitiateRequestDto` and calls `_paymentRuntime.InitiateAsync`. It does not contain `IStripePaymentService`, `IPaymobPaymentService`, `_stripePaymentService`, `_paymobPaymentService`, `StripePaymentService`, or `PaymobPaymentService`.

Manual Wallet recovery remains local because it resumes/verifies durable manual evidence instead of initiating an online provider session.

## 6. Runtime Boundary Audit

Result: PASS WITH OBSERVATIONS.

`CommerceHubPaymentService` does not own provider signature verification, callback field ordering, provider DTO parsing, SDK clients, or provider API communication. It delegates:

- Paymob webhook parsing to `PaymobAdapter.ParseWebhookAsync`.
- Paymob browser callback parsing to `PaymobAdapter.ParseCallbackAsync`.
- Stripe webhook verification/parsing to `StripeAdapter.ParseWebhookAsync`.
- Stripe/Paymob session creation to `IPaymentProviderAdapter.CreateSessionAsync`.
- Provider status lookup to `IPaymentProviderAdapter.GetStatusAsync`.

Observation: `CommerceHubPaymentService` still has provider-named application methods and maps adapter error codes containing terms such as HMAC/SIGNATURE to canonical HTTP responses. This is not active provider protocol verification and is not treated as a certification failure, but future cleanup could move more provider-specific event labeling into canonical adapter result fields.

## 7. Provider Boundary Audit

Result: PASS.

`StripeAdapter` owns:

- Stripe PaymentIntent session creation.
- Stripe SDK client use and request options.
- Stripe webhook signature verification through `EventUtility.ConstructEvent`.
- Stripe webhook payload translation into `PaymentProviderWebhookResult`.
- Stripe status lookup.
- Stripe provider error translation.

`PaymobAdapter` owns:

- Paymob authentication, order registration, and payment-key creation through `IPaymobOfficialApiClient`.
- Hosted checkout URL creation.
- Paymob webhook HMAC verification through `PaymobWebhookHmacVerifier.Verify`.
- Paymob callback parsing and HMAC verification.
- Paymob status lookup.
- Paymob capability discovery and provider error translation.

Adapters do not own order lifecycle, inventory finalization, vendor earnings, settlement, accounting, outbox, notifications, or business completion decisions.

## 8. Dependency Audit

Result: PASS.

Active CommerceHub online dependencies follow the canonical graph. Whole-solution search confirms:

- Controllers use `ICommerceHubPaymentOrchestrationService`, not provider payment services.
- `CheckoutService` uses `ICommerceHubPaymentOrchestrationService`, not `IPaymentGateway`, `IStripePaymentService`, or `IPaymobPaymentService`.
- `CheckoutPaymentIntentRecoveryService` uses `IPaymentRuntime`, not provider payment services.
- `CommerceHubPaymentOrchestrationService` uses `IPaymentRuntime`, not provider gateways/services.
- `CommerceHubPaymentService` uses `IPaymentProviderAdapter`, not `IStripePaymentService` or `IPaymobPaymentService`.

Compatibility code still exists:

- `IPaymentGateway`, `IProviderPaymentGateway`, and provider gateway classes remain in source.
- `StripePaymentService` and `PaymobPaymentService` remain registered for legacy/health compatibility.
- Historical documentation still describes pre-REM-002 failures.

None of those were found as active CommerceHub online runtime authorities.

## 9. Dependency Injection Audit

Result: PASS WITH OBSERVATIONS.

`Program.cs` registers:

- `PointOfSale.API.Services.CommerceHub.ICommerceHubPaymentService` -> `CommerceHubPaymentService`.
- `PointOfSale.API.Services.CommerceHub.IPaymentRuntime` -> `CommerceHubPaymentRuntime`.
- `PointOfSale.API.Services.CommerceHub.ICommerceHubPaymentOrchestrationService` -> `CommerceHubPaymentOrchestrationService`.

Provider adapters are registered as:

- `IPaymentProviderAdapter` -> `StripeAdapter`.
- `IPaymentProviderAdapter` -> `PaymobAdapter`.

`IPaymentGateway` and `IProviderPaymentGateway` are not registered as active CommerceHub online runtime authorities.

Observation: `IStripePaymentService` and `IPaymobPaymentService` remain registered. Certification treats them as compatibility registrations because canonical controllers, checkout, recovery, orchestration, and runtime services do not depend on them for CommerceHub online execution.

## 10. Completion Pipeline Audit

Result: PASS.

Canonical success ordering:

1. Provider adapter returns canonical provider result.
2. Payment processing lock is acquired.
3. Serializable transaction starts.
4. Payment and marketplace order are loaded for mutation.
5. Amount, currency, provider identity, and order state are validated.
6. Duplicate processed-payment guard exits without reapplying effects.
7. Completion evidence is written.
8. Inventory reservations are finalized.
9. `CommerceHubPaymentLifecycleAuthority.MarkOnlinePaymentCollected` marks marketplace/sub-orders paid.
10. `CommerceHubFinancialWorkflowService.ApplyOnlinePaymentSuccessAsync` creates vendor earnings and financial ledger/settlement records.
11. `CommerceHubFinancialLedgerService` enqueues accounting outbox rows.
12. Canonical completion events and notification request events are published before save.
13. `SaveChangesAsync` persists payment, order, inventory, financial, accounting outbox, and event outbox changes.
14. Transaction commits.
15. Notification processing is requested through outbox delivery, not inline external HTTP.

## 11. Transaction Audit

Result: PASS.

Canonical payment completion uses a single authoritative serializable transaction in `CommerceHubPaymentService`.

- Inventory finalization joins the ambient EF transaction because `InventoryConsistencyService.ExecuteInTransactionAsync` only opens its own transaction when no current transaction exists.
- Financial ledger writes join the ambient EF transaction because `CommerceHubFinancialLedgerService.ExecuteAtomicAsync` records directly when `CurrentTransaction` is present.
- Accounting outbox rows are added to the same DbContext transaction.
- Event/notification outbox messages are published before `SaveChangesAsync`.
- No provider HTTP call is made inside the completion transaction; provider HTTP/session creation occurs before durable completion transaction boundaries.

## 12. Idempotency Audit

Result: PASS.

Duplicate webhook/retry/replay protections include:

- Payment processing distributed lock.
- Serializable transaction.
- Provider identity matching and uniqueness checks.
- Processed-payment guard using `IsProcessed` and existing success/failure matching.
- Payment logs keyed by provider event/reference evidence.
- Compensation outbox deterministic idempotency keys.
- Accounting outbox deterministic idempotency keys.
- CommerceHub financial ledger duplicate transaction detection.
- Marketplace/sub-order event idempotency keys.
- Notification request idempotency key.

These prevent duplicate completion, inventory finalization, accounting, settlement, vendor earnings, outbox publication, and notification requests on duplicate webhook/replay paths.

## 13. Recovery Audit

Result: PASS.

Replay safety:

- Existing reusable sessions are reused.
- Already processed payments are not reprocessed.
- Failed-to-success reconciliation is constrained by provider identity matching.

Rollback safety:

- Completion failure rolls back the serializable transaction.
- Completion telemetry records transaction rollback failures.

Outcome unknown:

- Provider-session outcome unknown is fenced with `PROVIDER_SESSION_OUTCOME_UNKNOWN`.
- Provider-session reinit fencing prevents blind duplicate external session creation.
- Persistence gaps are recorded for reconciliation.

Retry safety:

- Lock contention returns retryable results without partial mutation.
- Unconfigured provider/webhook handling returns retry where durable evidence cannot be trusted.
- Already processed duplicate webhooks return non-retry results.

Compensation:

- Captured payments that cannot be applied safely are marked `RefundRequired`.
- `CommerceHubPaymentCompensationOutboxMessage` records deterministic compensation work.

Recovery ownership:

- Checkout payment-intent recovery uses `IPaymentRuntime` for CommerceHub online providers.
- `CommerceHubPaymentRecoveryAuthorityService` blocks retry when unresolved captured payment, provider-session persistence gaps, or reservation authority loss exists.

## 14. Telemetry Audit

Result: PASS WITH OBSERVATIONS.

Metrics exist for:

- Admission/execution: `commercehub_payment_execution_started_total`.
- Provider selection/admission: `commercehub_payment_provider_selected_total`.
- Webhook admission: `commercehub_payment_webhook_received_total`.
- Provider latency: Paymob `paymob_provider_request_latency_ms`; Stripe operational telemetry has `stripe_provider_latency_ms`.
- Completion: `commercehub_payment_completion_started_total`, `commercehub_payment_completion_succeeded_total`, `commercehub_payment_completion_failed_total`, `commercehub_payment_completion_duration_ms`.
- Inventory: `commercehub_inventory_finalized_total`.
- Settlement/vendor earnings: `commercehub_settlement_created_total`, `commercehub_vendor_earnings_created_total`.
- Accounting: `PointOfSale.Accounting` metrics including `accounting.posted_total`, `accounting.failure_total`, and `accounting.posting_latency_ms`.
- Outbox: `commercehub_payment_completion_outbox_published_total`, accounting outbox pending/failed gauges, outbox dead-letter metrics.
- Failures: payment execution/completion failure counters, provider failure counters, accounting failure counters.
- Recovery: Paymob background recovery metrics and recovery logging/ledger evidence exist; checkout recovery is logged.

Observation: checkout payment-intent recovery does not expose a dedicated CommerceHub payment recovery counter in the same telemetry class. This is an operations observability improvement, not an active runtime authority failure.

## 15. Logging Audit

Result: PASS WITH OBSERVATIONS.

Provider logs are isolated in provider adapters and provider compatibility services:

- `StripeAdapter` logs provider session creation and provider errors.
- `PaymobAdapter` logs authentication/session/webhook validation and provider failures.

Business logs are isolated in CommerceHub services:

- `CommerceHubPaymentOrchestrationService` logs orchestration start/success/failure.
- `CommerceHubPaymentService` logs payment initiation, webhook processing, duplicate suppression, completion, compensation, and recovery.

Financial audit is authoritative:

- `CommerceHubFinancialWorkflowService` records payment collected, commission deducted, COD collected, and vendor earning audit facts through `ICommerceHubFinancialAuditService`.
- `CommerceHubFinancialLedgerService` writes financial transactions and accounting outbox evidence.

Observation: legacy provider services still contain older business/lifecycle logs. They are compatibility logs, not active CommerceHub online authority logs.

## 16. Test Assessment

Result: PASS.

Reviewed focused/architecture/runtime tests include:

- `PaymentRemediation002RuntimeAuthorityClosureTests`
- `PaymentRemediation001CanonicalRuntimeConvergenceTests`
- `PaymentImplementation3CanonicalCompletionPipelineTests`
- `CommerceHubPaymentOrchestrationArchitectureTests`
- `CommerceHubPaymentOrchestrationServiceTests`
- `CommerceHubPaymentRecoveryAuthorityServiceTests`
- `CommerceHubPaymentServiceTests`
- provider adapter certification tests for Stripe and Paymob

Targeted test run:

```text
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~PaymentRemediation002RuntimeAuthorityClosureTests|FullyQualifiedName~PaymentRemediation001CanonicalRuntimeConvergenceTests|FullyQualifiedName~PaymentImplementation3CanonicalCompletionPipelineTests|FullyQualifiedName~CommerceHubPaymentOrchestrationArchitectureTests|FullyQualifiedName~CommerceHubPaymentOrchestrationServiceTests|FullyQualifiedName~CommerceHubPaymentRecoveryAuthorityServiceTests" --no-restore
```

Result:

```text
Passed: 26
Failed: 0
Skipped: 0
```

Broader repository failures, if present, should be evaluated separately unless they demonstrate an active CommerceHub online payment runtime authority violation.

## 17. Remaining Findings

### Architecture

- No failing active runtime authority findings.
- Observation: provider-specific route/controller names remain as compatibility shims while delegating to canonical orchestration.
- Observation: `StripePaymentService` and `PaymobPaymentService` remain registered for compatibility/health, but are not dependencies of the canonical CommerceHub online path.

### Runtime

- No failing active runtime authority findings.
- Observation: `CommerceHubPaymentService` still contains provider-labeled routing/application methods and maps adapter HMAC/SIGNATURE error codes. This does not perform protocol verification, but future hardening could replace provider event labels with more canonical adapter outcome enums.

### Testing

- No failing focused payment certification tests in the targeted run.
- Observation: historical documentation/certification tests may still encode earlier phase snapshots; those should not be used to fail this post-REM-002 certification unless tied to active runtime code.

### Operations

- Observation: recovery is logged and evidenced, but checkout payment-intent recovery would benefit from a dedicated bounded metric in the CommerceHub payment telemetry surface.

### Documentation

- Observation: older certification documents under `docs/architecture/payment/certification` intentionally record pre-remediation failures. They are historical documentation, not current runtime evidence.

### Compatibility

- `IPaymentGateway`, `IProviderPaymentGateway`, `StripePaymentService`, `PaymobPaymentService`, Fawry, COD, and Manual Wallet compatibility paths remain.
- These compatibility paths do not fail certification because they are not active CommerceHub online Stripe/Paymob runtime authorities.

## 18. Certification Decision

PASS WITH OBSERVATIONS

## 19. Certification Rationale

CommerceHub now provides a single active online payment runtime for CommerceHub Stripe and Paymob execution. Checkout and recovery have converged on canonical orchestration/runtime. Controllers delegate to orchestration. `IPaymentRuntime` resolves to `CommerceHubPaymentRuntime`. `CommerceHubPaymentService` owns business completion and delegates provider protocol work to `IPaymentProviderAdapter` implementations. Provider adapters own API communication, session creation, callback/webhook parsing, signature/HMAC verification, status lookup, capability reporting, and provider error translation.

The completion pipeline is canonical and transactionally ordered. Inventory, order lifecycle, vendor earnings, settlement/financial ledger, accounting outbox, event outbox, and notification request evidence are written before commit, with no external provider HTTP inside the completion transaction. Replay, duplicate webhook, retry, outcome-unknown, rollback, and compensation paths have deterministic guards.

Remaining observations are compatibility, operations, documentation, or cleanup items. No Architecture or Runtime finding was identified that leaves an active alternative CommerceHub online payment authority in place.

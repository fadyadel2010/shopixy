# PAYMOB-CERTIFICATION-CLOSURE-1 Runtime Outcome Enforcement

Date: 2026-07-14  
Scope: `PAYMOB-FINAL-001` only  
Reference provider: Stripe  
Payment Runtime posture: frozen and unmodified  
Decision: **CRITICAL FINDING CLOSED**

## Finding

`PAYMOB-FINAL-001` found that the POS/Storefront Paymob webhook path invoked the Payment Runtime but did not require a successful required Runtime outcome before entering application persistence and calling `ProcessPaymobTransactionAsync`.

For a Paymob outcome whose application admission explicitly required Runtime authority, Runtime rejection or failure could therefore fall through to the SQL lifecycle writer. That could permit payment success, refund recording, settlement-related work, or another lifecycle state transition without the required canonical Runtime outcome.

This closure addresses only that Critical finding. Every other finding and certification restriction in `PAYMOB_FINAL.md` remains open and unchanged.

## Root Cause

`PaymobPaymentService.ProcessWebhookAsync` handled these pre-persistence outcomes:

- replay or duplicate rejection;
- invalid callback rejection;
- non-terminal or ignored admission.

It did not handle the distinct state where:

```text
runtimeAdmission.Admission.AdmittedToRuntime == true
and
runtimeAdmission.AdmittedToRuntime == false
```

The first value means the application contract required Runtime authority for that outcome. The second means the adapter/Runtime execution did not produce a successful canonical result. Without an explicit guard, execution continued into the SQL transaction and lifecycle dispatcher.

The responsible layer was the Paymob application webhook boundary. No Runtime, bridge, adapter, protocol, controller, persistence-model, frontend, or business-rule defect was required to correct it.

## Correction

The Paymob application boundary now applies the same fail-closed invariant as the certified Stripe provider:

```text
Application admission requires Runtime
  + required Runtime outcome is absent
  -> log the rejected Runtime outcome
  -> throw PAYMOB_RUNTIME_OUTCOME_REJECTED
  -> do not open the webhook SQL transaction
  -> do not invoke ProcessPaymobTransactionAsync
```

`PaymobRuntimeOutcomeGuard` owns the narrow predicate and exception:

```csharp
runtimeAdmission.Admission.AdmittedToRuntime &&
!runtimeAdmission.AdmittedToRuntime
```

`PaymobPaymentService` invokes `EnsureRequiredOutcome(runtimeAdmission)` after replay, duplicate, invalid, non-terminal, and ignored handling, but before `BeginTransactionAsync` and before `ProcessPaymobTransactionAsync`.

The failure code is:

```text
PAYMOB_RUNTIME_OUTCOME_REJECTED
```

Successful required Runtime outcomes continue through the existing lifecycle path. Outcomes not designated by application admission as requiring Runtime retain their existing application behavior; this closure does not redesign admission classification or business rules.

## Architecture Preservation

The closure is confined to the Paymob application boundary.

Unmodified certified/frozen surfaces:

- `PointOfSale.Core/Payments/Runtime/**`;
- `PaymobRuntimeBridge`;
- `PaymobPaymentAdapter`;
- `PaymobWebhookEvidenceAdapter`;
- controllers and public DTOs;
- provider protocol and provider HTTP execution;
- persistence entities, EF model, migrations, and indexes;
- `PaymentLifecycleService` and business rules;
- frontend applications.

The Runtime remains provider-neutral, SQL-free, persistence-free, and frozen. `PaymentLifecycleService` remains the single SQL lifecycle writer. The correction does not add a second writer; it prevents the existing writer from being reached when required Runtime authority is absent.

No new permanent cross-provider architecture truth was discovered. The correction enforces the existing rule that provider evidence is not lifecycle authority and that required Runtime execution must fail closed before application mutation. `.ai-context` was therefore not modified.

## Evidence

### Control-flow evidence

The certified webhook order is now:

```text
EvaluateAndExecuteAsync
  -> replay / duplicate handling
  -> invalid callback handling
  -> non-terminal / ignored handling
  -> PaymobRuntimeOutcomeGuard.EnsureRequiredOutcome
  -> BeginTransactionAsync
  -> PrepareWebhookLogAsync
  -> ProcessPaymobTransactionAsync
  -> PaymentLifecycleService
  -> replay-record persistence
  -> commit
```

Because the guard throws before `BeginTransactionAsync`, a required Runtime rejection or failure cannot create the webhook SQL transaction, call the lifecycle dispatcher, record payment success/refund, execute settlement-related lifecycle work, or transition application payment/order state through this path.

### Focused certification tests

`PointOfSale.Tests/Architecture/PaymobCertificationClosure1Tests.cs` proves:

- Runtime rejection throws `PAYMOB_RUNTIME_OUTCOME_REJECTED` before the following mutation statement can execute.
- Runtime failure after provider-adapter success also blocks the following mutation statement.
- Runtime success permits the following lifecycle statement.
- The production guard is ordered before SQL transaction creation and `ProcessPaymobTransactionAsync`.
- The guard is absent from the frozen Runtime, Runtime Bridge, Provider Adapter, and Webhook Adapter.

### Executed validation

| Validation | Result |
|---|---:|
| `PaymobCertificationClosure1Tests` | **5 passed, 0 failed** |
| Paymob tests excluding the known unrelated `PaymobPaymentAuthorityCertificationTests` artifact guard | **98 passed, 0 failed** |
| Payment Runtime tests | **94 passed, 0 failed** |
| Stripe or Payment Runtime reference tests | **206 passed, 0 failed** |
| Combined Paymob or Payment Runtime tests | **194 passed, 1 failed** |

The one combined-suite failure remains the pre-existing `CertificationArtifacts_MustRecordSfPay5Truth` assertion, which expects `PASS SF-PAY5` in `.ai-context`. It is unrelated to `PAYMOB-FINAL-001`, was already recorded in `PAYMOB_FINAL.md`, and is not changed in this single-finding closure. The test project compiled successfully during focused validation; existing repository warnings remain outside this phase.

## Certification Decision

`PAYMOB-FINAL-001` is **closed for the POS/Storefront Paymob webhook application boundary**.

The required authority implication is now enforced:

```text
Runtime required + Runtime rejected/failed
  -> no SQL transaction
  -> no ProcessPaymobTransactionAsync
  -> no payment success
  -> no refund recording
  -> no settlement lifecycle work
  -> no lifecycle state transition
```

This decision does not upgrade Paymob to Software, Security, Operations, Migration, Regression, Deployment, or Live Operational certification. The five High findings and all Medium/Low/External/Dormant dispositions from `PAYMOB_FINAL.md` remain outside this closure.

## Success Answers

| Question | Answer |
|---|---|
| Critical finding closed? | **Yes. `PAYMOB-FINAL-001` is closed for the audited POS/Storefront Paymob webhook path.** |
| Runtime authority enforced? | **Yes, whenever Paymob application admission requires Runtime.** |
| Lifecycle blocked on rejection? | **Yes. The guard throws before the lifecycle dispatcher.** |
| SQL mutation blocked? | **Yes. The guard executes before the webhook SQL transaction is opened.** |
| Runtime preserved? | **Yes. Frozen Runtime source was not modified.** |
| Architecture preserved? | **Yes. The change is confined to the Paymob application boundary.** |
| Regression passed? | **Closure regression: yes. Full combined Paymob/Runtime aggregate: no, due to the known unrelated `PASS SF-PAY5` artifact assertion (194/195).** |
| Recommend Closure-2? | **Yes, as a separate strictly scoped phase for the next open finding. Do not infer overall Paymob certification from this closure.** |

**FINAL DECISION: PAYMOB-FINAL-001 CLOSED. PAYMENT RUNTIME REMAINS FROZEN. ALL OTHER PAYMOB_FINAL FINDINGS AND CERTIFICATION HOLDS REMAIN OPEN.**

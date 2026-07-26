# PAYMOB-3 Runtime Bridge Certification

Date: 2026-07-13

Scope: application-owned Paymob Runtime Bridge orchestration

Excluded: webhook, callback, persistence, SQL mutation, replay, refund, capture, void, controllers, frontend, lifecycle mutation, and Payment Runtime changes

## Executive Summary

PAYMOB-3 implements `PaymobRuntimeBridge`, the application-owned orchestration layer between the certified Paymob provider adapter and the frozen provider-neutral Payment Runtime.

The bridge delegates a canonical `PaymentAction` to `IPaymobPaymentAdapter`, receives canonical `ProviderEvidence`, invokes `PaymentRuntimeOrchestrator`, and returns `PaymobExecutionResult` containing provider evidence, runtime result, execution status, trace, metadata, and any canonical provider error.

The Payment Runtime remains frozen and unmodified. Paymob protocol concepts terminate inside the Paymob adapter and do not cross the bridge boundary except as already-canonical `ProviderEvidence`.

## Execution Flow

Certified flow:

```text
PaymentAction
  -> Paymob Adapter
  -> ProviderEvidence
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
  -> PaymobExecutionResult
```

The bridge owns sequencing only. It does not create runtime decisions, transitions, transactions, or settlements.

## Bridge Architecture

Implementation surface:

- `IPaymobRuntimeBridge`
- `PaymobRuntimeBridge`
- `PaymobExecutionRequest`
- `PaymobExecutionResult`
- `PaymobExecutionStatus`
- `PaymobExecutionFactory`
- `PaymobExecutionResultFactory`

Dependency injection registers only:

```text
IPaymobRuntimeBridge -> PaymobRuntimeBridge
```

The bridge depends on:

- `IPaymobPaymentAdapter`
- `PaymentRuntimeOrchestrator`

It does not depend on EF Core, controllers, lifecycle services, SQL, frontend DTOs, webhook handlers, callback handlers, refund/capture/void services, or provider protocol DTOs.

## Failure Flow

Canonical provider errors never bypass Runtime.

```text
CanonicalProviderError
  -> rejected canonical ProviderEvidence
  -> PaymentRuntimeOrchestrator
  -> failed RuntimeResult
  -> PaymobExecutionResult
```

If the adapter fails without evidence, the bridge constructs rejected canonical provider-error evidence and still invokes the Runtime. The application result records `ProviderFailed`, preserves the canonical provider error, and exposes the failed `RuntimeResult`.

## Architecture Rules

1. Payment Runtime remains frozen.
2. Paymob Runtime Bridge is application orchestration only.
3. Paymob adapter owns Paymob protocol execution and translation.
4. Bridge consumes only canonical `ProviderEvidence`.
5. Bridge invokes only `PaymentRuntimeOrchestrator`.
6. Bridge never calls runtime stage engines directly.
7. Bridge never creates decisions, transitions, transactions, or settlements.
8. Bridge never mutates lifecycle state.
9. Bridge never persists data or executes SQL.
10. Bridge never implements webhook, callback, replay, refund, capture, void, controller, or frontend behavior.
11. Provider-specific concepts such as auth token, order registration, payment key, iframe, redirect URL, and HMAC remain inside the Paymob adapter.

## Stripe Delta

Only provider protocol differs:

| Area | Stripe | Paymob |
|---|---|---|
| Adapter command | PaymentIntent creation | Auth, order registration, payment-key preparation |
| Customer handoff | Client secret | Adapter-prepared redirect/iframe evidence metadata |
| Provider identity | PaymentIntent id | Paymob order id |
| Idempotency transport | Stripe idempotency key | deterministic merchant order id |
| Provider error codes | Stripe SDK/API error model | Paymob HTTP/API/request-validation error model |

Runtime Bridge architecture remains identical:

```text
PaymentAction -> Provider Adapter -> ProviderEvidence -> PaymentRuntimeOrchestrator -> RuntimeResult -> Provider Execution Result
```

## Certification Decision

PAYMOB-3 is certified as an application-owned Runtime Bridge for source-level adapter delegation, canonical evidence handoff, runtime invocation, provider-error failure routing, runtime isolation, provider neutrality, and bridge-only orchestration.

No new permanent cross-provider architecture truth was discovered. `.ai-context` is not modified.

## Success Answers

| Question | Answer |
|---|---|
| Runtime Bridge implemented? | Yes. |
| Execution Flow certified? | Yes. |
| Bridge delegation certified? | Yes. |
| Failure Flow certified? | Yes. |
| Architecture boundaries preserved? | Yes. |
| Runtime isolation preserved? | Yes. |
| Provider neutrality preserved? | Yes. |
| Production Runtime Bridge certified? | Yes, for source-level bridge orchestration only. |
| Recommend next phase? | Yes: Paymob callback/webhook evidence adaptation and lifecycle integration certification, still without modifying the frozen Runtime. |

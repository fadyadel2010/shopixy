# PAYMOB-4 Webhook Evidence Adapter

Date: 2026-07-13

Scope: Paymob callback/webhook evidence translation only

Excluded: controllers, persistence, replay store, lifecycle mutation, refund, capture, void, settlement, frontend, and Payment Runtime changes

## Executive Summary

PAYMOB-4 implements `PaymobWebhookEvidenceAdapter`, the canonical Paymob callback/webhook evidence adapter for the frozen Payment Runtime.

The adapter verifies Paymob HMAC, parses official callback/webhook transaction payloads, translates authenticated provider facts into canonical `ProviderEvidence`, translates provider-negative facts into `CanonicalProviderError`, and invokes only `PaymentRuntimeOrchestrator`.

The Payment Runtime remains frozen and provider-neutral. Paymob protocol fields terminate inside the adapter.

## Callback/Webhook Architecture

Certified flow:

```text
Paymob callback/webhook payload
  -> Paymob HMAC verification
  -> timestamp validation when configured
  -> replay hook
  -> duplicate hook
  -> canonical ProviderEvidence
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
```

The adapter does not resolve tenants, read or write SQL, persist replay records, mutate payment lifecycle state, or call provider operation services.

## HMAC Verification

Paymob uses HMAC-SHA512 over the official transaction field concatenation:

```text
amount_cents
created_at
currency
error_occured
has_parent_transaction
id
integration_id
is_3d_secure
is_auth
is_capture
is_refunded
is_standalone_payment
is_voided
order / order.id
owner
pending
source_data.pan
source_data.sub_type
source_data.type
success
```

Invalid, missing, or malformed HMAC evidence is rejected as canonical provider-error evidence and still passes through the Runtime orchestrator.

Timestamp validation is supported when the caller supplies a tolerance. Replay and duplicate detection are hooks only; no replay database or persistence is introduced in this phase.

## Event Translation

Supported Paymob event shapes:

- `TRANSACTION`
- `CALLBACK`

Canonical evidence carries:

- provider: `paymob`
- evidence type: `Webhook`
- provider reference: `paymob:order:{orderId}`
- transaction reference: `paymob:transaction:{transactionId}`
- external status: `pending`, `authorized`, `succeeded`, `failed`, `declined`, `voided`, or `refunded`
- deterministic payload and evidence hashes
- normalized provider metadata

Authenticated provider-negative transaction facts remain canonical evidence and carry a `CanonicalProviderError`. They do not mutate lifecycle state.

## Failure Flow

```text
Invalid HMAC / malformed payload / unsupported event / replay / duplicate
  -> CanonicalProviderError
  -> rejected ProviderEvidence
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
```

No failure path bypasses Runtime execution.

## Architecture Rules

1. Payment Runtime remains frozen.
2. Paymob HMAC, callback payload, merchant callback, redirect callback, and authentication fields terminate inside the Paymob adapter.
3. Only canonical `ProviderEvidence` crosses the Runtime boundary.
4. The adapter invokes only `PaymentRuntimeOrchestrator`.
5. The adapter does not create decisions, transitions, transactions, or settlements.
6. The adapter does not persist replay, duplicate, webhook, or audit records.
7. The adapter does not mutate payment/order lifecycle state.
8. The adapter does not implement refund, capture, void, settlement, frontend, or controllers.

## Stripe Delta

Only protocol differs:

| Stripe | Paymob |
|---|---|
| Stripe signature header | Paymob HMAC-SHA512 |
| Stripe `EventUtility.ConstructEvent` | Paymob official field concatenation and HMAC check |
| Stripe event id/type | Paymob transaction id/type |
| Stripe data object id | Paymob order id and transaction id |
| Stripe SDK exception translation | Paymob callback validation/error translation |

Architecture remains identical:

```text
authenticated provider event
  -> provider-specific verification
  -> canonical ProviderEvidence
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
```

## Certification Decision

PAYMOB-4 is certified for source-level callback/webhook evidence translation, HMAC verification, timestamp validation support, replay/duplicate hooks, canonical evidence mapping, canonical provider-error mapping, Runtime invocation, Runtime isolation, and provider neutrality.

No new permanent cross-provider architecture truth was discovered. `.ai-context` is not modified.

## Success Answers

| Question | Answer |
|---|---|
| Webhook/Callback Adapter implemented? | Yes. |
| HMAC verification certified? | Yes. |
| Event translation certified? | Yes. |
| Evidence mapping certified? | Yes. |
| Architecture boundaries preserved? | Yes. |
| Runtime independence preserved? | Yes. |
| Provider-neutral Runtime preserved? | Yes. |
| Production Webhook Adapter certified? | Yes, for source-level evidence adaptation only. |
| Recommend next phase? | Yes: Paymob lifecycle integration and durable replay certification without modifying the frozen Runtime. |

# PAYMOB-5 Advanced Operations Adapter

Date: 2026-07-13

Scope: Paymob provider-operation translation only

Excluded: Payment Runtime changes, Runtime execution, persistence, replay, settlement, controllers, frontend, webhook, callback, lifecycle mutation, SQL, and policy changes

## Executive Summary

PAYMOB-5 implements `PaymobAdvancedOperationsAdapter` as the Paymob advanced-operation translation boundary for the frozen Payment Runtime architecture.

The adapter accepts canonical `PaymentAction` operation requests, translates supported operations into official Paymob HTTPS API calls, maps Paymob operation responses into canonical `ProviderEvidence`, and translates provider/API failures into `CanonicalProviderError`.

No Runtime execution is performed. No lifecycle state is mutated. The Payment Runtime remains permanently frozen and provider-neutral.

## Supported Operations

The supported operation set is limited to Paymob operation capabilities represented in this adapter:

- Refund via `acceptance/void_refund/refund`.
- Partial refund via `acceptance/void_refund/refund` with `amount_cents`.
- Void via `acceptance/void_refund/void`.
- Capture via `acceptance/capture`.
- Transaction inquiry via `acceptance/transactions/{transaction_id}`.

`PaymentStatus` is documented as unsupported as a separate Paymob operation. Callers must use transaction inquiry when canonical status refresh evidence is required.

## Capability Matrix

| Canonical operation | Paymob support | Official Paymob operation | Adapter behavior |
|---|---:|---|---|
| Refund | Supported | `acceptance/void_refund/refund` | Sends authenticated refund request with transaction id and amount. |
| Partial Refund | Supported | `acceptance/void_refund/refund` | Sends authenticated refund request with requested `amount_cents`. |
| Void | Supported | `acceptance/void_refund/void` | Sends authenticated void request with transaction id. |
| Capture | Supported | `acceptance/capture` | Sends authenticated capture request with transaction id and amount. |
| Transaction Inquiry | Supported | `acceptance/transactions/{transaction_id}` | Retrieves official transaction state and maps it to evidence. |
| Payment Status | Unsupported as distinct operation | None | Fails closed as `PAYMOB_OPERATION_UNSUPPORTED`; transaction inquiry is the supported equivalent. |

Unsupported operations are provider capability limitations, not Runtime limitations. The adapter does not emulate missing Paymob behavior.

## Adapter Architecture

Certified flow:

```text
canonical PaymentAction
  -> PaymobOperationFactory
  -> official Paymob operation request
  -> PaymobOfficialApiHttpClient
  -> official Paymob operation response
  -> ProviderEvidence
```

The adapter depends on `IPaymobOfficialApiClient` and `PaymobOptions`. It registers only `IPaymobAdvancedOperationsAdapter`; no Runtime bridge, persistence service, controller, or lifecycle writer is registered for this phase.

## Request Mapping

Canonical metadata keys used by `PaymobOperationFactory`:

- `Operation`: refund, partial_refund, void, capture, transaction_inquiry, or payment_status.
- `ProviderReference`: numeric Paymob transaction id.
- `Amount`: required for refund, partial refund, and capture.
- `Currency`: required when `Amount` is supplied.
- `IdempotencyKey`: optional canonical idempotency override.
- `OperationReference`: optional canonical operation identity override.

The adapter preserves:

- operation identity in `operation` and `operation_reference` metadata;
- correlation through `PaymentAction.CorrelationId`;
- idempotency through `PaymobOperationIdempotencyKey`;
- provider references through numeric Paymob transaction ids and canonical `paymob:transaction:{id}` references.

## Response Mapping

Paymob operation responses are translated to `ProviderEvidence` with:

- provider: `paymob`;
- evidence type: `ProviderNotification`;
- evidence status: `Recorded`;
- evidence reference: `paymob:transaction:{transactionId}`;
- provider payload reference: `paymob:transaction:{transactionId}`;
- external status: `pending`, `authorized`, `succeeded`, `failed`, `voided`, or `refunded`;
- metadata: operation, operation reference, provider reference, transaction reference, idempotency key, correlation id, amount, currency, and Paymob response flags.

No `PaymentDecision`, `StateTransition`, `PaymentTransaction`, `PaymentSettlement`, or `RuntimeResult` is created by this adapter.

## Error Translation

Provider failures are translated into `CanonicalProviderError`:

- Paymob HTTP failures use `PaymobProviderErrorTranslator.Translate(PaymobOfficialApiException)`.
- Network failures use `PaymobProviderErrorTranslator.Translate(HttpRequestException)`.
- malformed operation responses map to `PAYMOB_OPERATION_RESPONSE_INVALID`.
- invalid canonical requests map to `PAYMOB_OPERATION_REQUEST_INVALID`.
- adapter configuration failures map to `PAYMOB_OPERATION_ADAPTER_INVALID`.
- unsupported operations map to `PAYMOB_OPERATION_UNSUPPORTED`.

No Paymob exception escapes the adapter boundary.

## Stripe Delta

Only provider capability and protocol differ:

| Stripe | Paymob |
|---|---|
| Capture PaymentIntent | Capture eligible Paymob transaction via `acceptance/capture`. |
| Cancel PaymentIntent | Void eligible Paymob transaction via `acceptance/void_refund/void`. |
| Refund and partial refund through Stripe Refund API | Refund and partial refund through Paymob `acceptance/void_refund/refund`. |
| Retrieve PaymentIntent and Charge | Retrieve Paymob transaction via transaction inquiry. |
| Separate Stripe object families | Paymob operation responses center on transaction ids. |

Architecture does not differ. Paymob adapts to the certified Stripe reference-provider architecture without copying Stripe-only capabilities.

## Architecture Rules

1. Payment Runtime remains frozen.
2. Paymob advanced-operation protocol terminates inside the Paymob adapter boundary.
3. Only canonical `ProviderEvidence` and `CanonicalProviderError` leave the adapter.
4. The adapter does not invoke `PaymentRuntimeOrchestrator`.
5. The adapter does not create decisions, transitions, transactions, settlements, or runtime results.
6. The adapter does not persist replay, idempotency, webhook, operation, or audit records.
7. The adapter does not mutate payment or order lifecycle state.
8. The adapter does not introduce controllers, SQL, frontend, webhook, callback, policy, settlement, or replay behavior.
9. Unsupported Paymob operations fail closed as provider capability limitations.

## Certification Decision

PAYMOB-5 is certified for source-level advanced-operation translation, supported Paymob operation mapping, capability matrix documentation, canonical `ProviderEvidence` response mapping, canonical provider-error translation, dependency boundary preservation, and Runtime independence.

No new permanent cross-provider architecture truth was discovered. `.ai-context` is not modified.

## Success Answers

| Question | Answer |
|---|---|
| Advanced Operations Adapter implemented? | Yes. |
| Supported operations certified? | Yes: refund, partial refund, void, capture, and transaction inquiry. |
| Capability matrix certified? | Yes. |
| Canonical request mapping certified? | Yes. |
| Canonical response mapping certified? | Yes. |
| Error translation certified? | Yes. |
| Architecture boundaries preserved? | Yes. |
| Runtime independence preserved? | Yes. |
| Production Advanced Adapter certified? | Yes, for source-level provider-operation translation only. |
| Recommend next phase? | Yes: Paymob lifecycle integration and durable operation/replay certification without modifying the frozen Runtime. |

# STRIPE-PROVIDER-1 - Canonical Stripe Adapter

Date: 2026-07-17

Status: SOURCE IMPLEMENTED AND ARCHITECTURE CERTIFIED

Scope: Stripe external provider adapter for the certified Payment Platform. Runtime, Lifecycle, Workflow, Intent Infrastructure, Order Domain, and Event Flow remain unchanged.

## Architecture

Stripe is implemented as an external provider boundary only:

```text
Canonical Payment Runtime contracts
  -> StripePaymentProvider
  -> StripeRequestMapper
  -> Stripe SDK / signed webhook primitive
  -> StripeResponseMapper
  -> canonical ProviderEvidence / CanonicalProviderError
  -> existing Runtime/Application/Lifecycle owners
```

`StripePaymentProvider` is a provider facade. It delegates to the already isolated `StripePaymentIntentAdapter`, `StripeAdvancedOperationsAdapter`, and `StripeWebhookVerifier`. It does not load orders, dispatch workflow intents, call handlers, publish events, write SQL, or mutate lifecycle state.

## Adapter Responsibilities

The adapter may:

- translate canonical payment actions into Stripe PaymentIntent and operation requests;
- execute Stripe protocol through the certified adapter clients;
- translate Stripe responses into canonical `ProviderEvidence`;
- translate Stripe errors into canonical provider errors and stable `PaymentFailureReason` values;
- verify webhook signatures through Stripe's official SDK primitive;
- report provider capabilities and constraints.

The adapter must not:

- know Order Domain, Order Aggregate, Workflow, Dispatcher, handlers, Storefront, Affiliate, CommerceHub, RabbitMQ, MassTransit, frontend, timelines, or SQL;
- own payment finality, lifecycle progression, business policy, tenant configuration, replay persistence, or event publication;
- leak Stripe SDK exceptions or Stripe SDK types outside the provider boundary as canonical runtime contracts.

## Capability Matrix

| Capability | Stripe Provider 1 | Notes |
|---|---:|---|
| Authorize | Yes | PaymentIntent creation/client-confirmation handoff. |
| Capture | Yes | Advanced adapter translation. Active command admission remains application-owned. |
| Cancel | Yes | Stripe cancel maps through advanced operation evidence. |
| Refund | Yes | Full refund request/evidence translation. |
| PartialRefund | Yes | Amount-bound refund request/evidence translation. |
| Void | Yes | Represented by cancel/void capability. |
| Tokenization | Yes | Stripe token/client-secret handoff remains transient provider evidence, not local finality. |
| Webhook Processing | Yes | Signature verification plus canonical evidence translation. |
| Capability reporting | Yes | `StripeCapabilityProvider`. |

## Translation Layer

- `StripeRequestMapper` maps canonical `PaymentAction` metadata to Stripe request objects and operation metadata.
- `StripeResponseMapper` maps Stripe PaymentIntent and webhook event facts into canonical `ProviderEvidence`.
- `StripeErrorMapper` maps `StripeException` to `CanonicalProviderError`, then to canonical `PaymentFailureReason`.

Provider statuses remain metadata/evidence. They do not become runtime statuses or lifecycle decisions.

## Webhook Strategy

`StripeWebhookVerifier` wraps the official `EventUtility.ConstructEvent` signature verification path. The verifier only authenticates and returns the parsed Stripe event or canonical error. The existing webhook runtime integration remains responsible for admission, durable replay lookup, runtime execution, lifecycle writing, and webhook log completion.

Unsupported, malformed, unsigned, expired, replayed, disputed, non-terminal, or provider-negative events fail closed or remain application-owned evidence. They do not execute business workflows directly.

## Security

- Stripe secrets stay server-side.
- Signature verification uses the raw body and Stripe signature header.
- Canonical errors redact provider exception details to bounded metadata.
- Capability and request mapping do not read tenant configuration or credentials.
- No Stripe provider class writes SQL or calls lifecycle services.

## Error Mapping

| Stripe error class | Canonical provider error | PaymentFailureReason |
|---|---|---|
| authentication error / 401 | Authentication | AuthenticationFailed |
| authorization / 403 | Authorization | AuthorizationFailed |
| rate limit / 429 | RateLimited | RateLimited |
| API/server failure | ProviderUnavailable | ProviderUnavailable |
| card error | ProviderRejected | ProviderDeclined |
| invalid request/idempotency | Validation | InvalidRequest |
| unexpected state | InvalidState | InvalidState |
| already captured/refunded | AlreadyCaptured / AlreadyRefunded | AlreadyCaptured / AlreadyRefunded |

No Stripe exception is allowed to leak outside the adapter facade.

## Extension Points

Future Stripe work may add application-owned active commands for capture, refund, partial refund, cancel/void, inquiry, and settlement. Each activation must prove tenant authority, capability admission, durable checkpointing, exact Runtime outcome validation, single-writer lifecycle mutation, replay closure, operational readiness, and sandbox validation.

Marketplace and CommerceHub Stripe payment flows remain outside this certification.

## Certification

| Question | Answer |
|---|---|
| Provider isolated? | Yes. Stripe provider code lives in `PointOfSale.API.Services.Payments.Stripe` and delegates only to provider adapters/verifier. |
| Provider-neutral architecture preserved? | Yes. Runtime contracts remain provider-neutral. |
| Runtime unchanged? | Yes. No `PointOfSale.Core.Payments.Runtime` source is modified. |
| Workflow unchanged? | Yes. No workflow source is modified. |
| Intent infrastructure unchanged? | Yes. No dispatcher or handler source is modified. |
| Order Domain unchanged? | Yes. Stripe provider code has no Order Domain dependency. |
| No provider leakage? | Yes. Stripe SDK types stop inside the adapter/verifier boundary; canonical outputs are provider evidence and provider errors. |
| Capability mapping complete? | Yes for Authorize, Capture, Cancel, Refund, PartialRefund, Void, Tokenization, Webhook Processing, and Capability Reporting. |
| Webhook verification implemented? | Yes. `StripeWebhookVerifier` wraps official Stripe signature verification. |
| Canonical translation complete? | Yes for request mapping, response mapping, error mapping, capability reporting, and webhook verification. |
| Architecture certified? | Yes, for STRIPE-PROVIDER-1 source scope. |
| Recommend STRIPE-LIVE-INTEGRATION? | Yes. Live integration should proceed only as a separate deployment/sandbox certification phase with real credentials, signed webhook delivery, drills, alerts, and rollback evidence. |

Certification tests:

```text
PointOfSale.Tests.Architecture.StripeProvider1CertificationTests
```

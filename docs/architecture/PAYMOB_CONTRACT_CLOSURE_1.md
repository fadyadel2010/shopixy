# PAYMOB-CONTRACT-CLOSURE-1 Application Contract Closure

Date: 2026-07-13

Status: APPLICATION CONTRACT CERTIFIED FOR STOREFRONT PAYMOB PAYMENT INTENT CREATION

## Executive Summary

PAYMOB-CONTRACT-CLOSURE-1 closes the application integration gap between existing storefront Paymob requests and the certified Paymob Runtime Bridge without changing the frozen Payment Runtime, Runtime Bridge, Provider Adapter, controllers, routes, frontend, DTOs, persistence, or SQL.

`PaymobRuntimeApplicationService` is now the application-owned execution boundary for Paymob storefront payment preparation. It resolves tenant authority, builds canonical Runtime inputs, delegates only to `IPaymobRuntimeBridge`, and maps Runtime/provider output back to the existing Paymob DTO contract.

## Application Architecture

Certified flow:

```text
Controller
  -> PaymobPaymentService
  -> PaymobRuntimeApplicationService
  -> PaymobRuntimeBridge
  -> PaymobPaymentAdapter
  -> ProviderEvidence
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
  -> Existing DTO
```

The application boundary owns storefront compatibility and tenant authority. The Runtime remains provider-neutral and unaware of Paymob credentials, iframe URLs, payment keys, tenant configuration, frontend DTOs, and SQL.

## Execution Flow

1. The existing controller calls `IPaymobPaymentService` using the unchanged public route and DTO contract.
2. `PaymobPaymentService` preserves order validation, idempotency, local payment-intent persistence, event publication, and existing response mapping.
3. `PaymobPaymentService` delegates Paymob provider preparation to `IPaymobRuntimeApplicationService`.
4. `PaymobRuntimeApplicationService` resolves tenant Paymob authority and builds canonical `PaymentSession` and `PaymentAction` inputs.
5. `PaymobRuntimeApplicationService` delegates only to `IPaymobRuntimeBridge`.
6. `PaymobRuntimeBridge` delegates to the frozen Paymob adapter and frozen Payment Runtime orchestrator.
7. `PaymobRuntimeApplicationService` maps provider evidence metadata and Runtime outcome into application-owned artifacts.
8. `PaymobPaymentService` returns the unchanged `PaymobPaymentIntentResponseDto`.

## Tenant Authority

Tenant authority is resolved in `PaymobRuntimeApplicationService`:

- Tenant payment method enablement.
- Encrypted tenant configuration from `TenantPaymentMethod.ConfigEncrypted`.
- Server-side Data Protection decryption.
- `PaymentMethodConfigurationValidator` validation.
- Sandbox/production mode alignment.
- Environment feature flags.
- Capability support for official HTTPS API and tenant-scoped credentials.
- Fail-closed alignment between decrypted tenant credentials and the Paymob runtime options consumed by the frozen adapter.

The Runtime, bridge, and adapter do not resolve tenant configuration or encrypted secrets.

## DTO Compatibility

Preserved public DTO fields:

- `IframeUrl`
- `PaymentKey`
- `IntentionId`
- `PaymentIntentId`
- `OrderId`
- `OrderNumber`
- `Amount`
- `Currency`
- `TransactionId`

No Runtime object is returned to the frontend. Provider-owned artifacts remain application-mapped from provider evidence metadata.

## Failure Mapping

`CanonicalProviderError` is mapped inside the application boundary to the existing Paymob public error behavior through `PaymobApiException`.

Runtime outcome failures are mapped to `PAYMOB_RUNTIME_OUTCOME_REJECTED`. Provider exceptions, Runtime internals, and canonical errors are not exposed directly to the frontend.

## Architecture Rules

1. Payment Runtime remains frozen.
2. Runtime Bridge remains frozen.
3. Provider Adapter remains frozen.
4. Controllers, routes, frontend, DTOs, SQL, and persistence schema remain unchanged.
5. `PaymobRuntimeApplicationService` owns tenant credential resolution and storefront application mapping.
6. `PaymobRuntimeApplicationService` delegates Runtime execution only through `IPaymobRuntimeBridge`.
7. Runtime must not resolve tenant configuration, credentials, environment, capabilities, or feature flags.
8. Existing Paymob public response and error contracts remain application-owned.

## Stripe Delta

The architecture mirrors STRIPE-CONTRACT-CLOSURE-1.

| Stripe | Paymob |
|---|---|
| `StripeRuntimeApplicationService` owns PaymentIntent application contract. | `PaymobRuntimeApplicationService` owns Paymob payment-preparation application contract. |
| Client secret and publishable key remain application/provider artifacts. | Iframe URL, payment key, and intention/order id remain application/provider artifacts. |
| `StripeRuntimeApplicationResult` maps Runtime and provider artifacts to existing DTO. | `PaymobRuntimeApplicationResult` maps Runtime and provider artifacts to existing DTO. |
| `StripeApiException` maps canonical provider failure. | `PaymobApiException` maps canonical provider failure. |

No Stripe-specific wire behavior is copied into Paymob. Only the certified application boundary pattern is replicated.

## Certification Decision

PAYMOB-CONTRACT-CLOSURE-1 is certified for application-boundary closure of storefront Paymob payment intent creation. The certified scope is source-level application composition and DTO compatibility. Runtime, bridge, adapter, controller, frontend, and persistence boundaries remain independent.

## Success Answers

| Question | Answer |
|---|---|
| Application Contract implemented? | Yes. |
| DTO compatibility preserved? | Yes. |
| Tenant Credential Resolution certified? | Yes, inside `PaymobRuntimeApplicationService`. |
| Failure Mapping certified? | Yes, canonical/provider failures map to existing Paymob application exceptions. |
| Backward Compatibility preserved? | Yes, public controller, route, DTO, and frontend contract are unchanged. |
| Runtime Independence preserved? | Yes. |
| Architecture boundaries preserved? | Yes. |
| Application Contract certified? | Yes, for storefront Paymob payment intent creation. |
| Recommend next phase? | Yes: Paymob webhook/outcome admission closure and durable replay certification, without modifying the frozen Runtime. |

No new permanent cross-provider architecture truth was discovered. `.ai-context` is not modified.

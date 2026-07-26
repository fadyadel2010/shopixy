# STRIPE-CONTRACT-CLOSURE-1 Application Contract Closure

Date: 2026-07-12

Status: APPLICATION CONTRACT CERTIFIED FOR STOREFRONT PAYMENTINTENT CREATION

## Contract Gap Analysis

The frozen Payment Runtime and certified Stripe adapter path expose canonical execution artifacts: `ProviderEvidence`, `RuntimeResult`, and canonical provider errors. The existing storefront contract also requires provider-owned application artifacts:

- `ClientSecret`
- `PublishableKey`
- `StripePaymentIntentId`

Those values are intentionally not Runtime concepts. Prior production wiring could not safely replace the legacy service with `StripeRuntimeBridge` because doing so would break the storefront DTO and because tenant credentials lived in application SQL configuration rather than Runtime.

## Application Boundary

`IStripeRuntimeApplicationService` and `StripeRuntimeApplicationService` now own the application integration boundary for storefront Stripe PaymentIntent creation.

The boundary:

- Resolves tenant Stripe configuration from `TenantPaymentMethod`.
- Decrypts and validates encrypted tenant credentials.
- Builds canonical `PaymentSession`, `PaymentAttempt`, and `PaymentAction` inputs.
- Invokes `IStripeRuntimeBridge`.
- Captures provider-owned artifacts from the scoped Stripe client.
- Returns an application-owned result for existing DTO mapping.

Runtime, Stripe adapter contracts, provider contracts, and policy engine files were not modified.

## DTO Mapping

Mapping remains application-owned:

```text
StripeRuntimeApplicationResult
  + ProviderEvidence
  + RuntimeResult
  + provider artifact
    -> StripePaymentIntentResponseDto
```

The existing public DTO is unchanged. No Runtime object is returned to the frontend, and no Stripe SDK object escapes the application layer.

## Credential Resolution

Credential resolution moved into the application integration boundary for the new canonical create-intent path:

```text
TenantPaymentMethod
  -> ConfigEncrypted decrypt
  -> PaymentMethodConfigurationValidator
  -> StripeTenantRuntimeCredentials
  -> TenantScopedStripePaymentIntentClient
```

The Runtime remains unaware of tenant configuration, encrypted secrets, sandbox mode, and provider keys.

## Client Secret Contract

`ClientSecret` remains provider-owned and application-mapped. It is captured as a provider artifact by the scoped application client and then persisted encrypted through the existing `PaymentIntent.ClientSecretEncrypted` storage path.

The Runtime does not gain `ClientSecret`, `PublishableKey`, or `StripePaymentIntentId` fields.

## Failure Mapping

`CanonicalProviderError` is mapped inside the application boundary to the existing application exception behavior through `StripeApiException`.

`StripeException` remains adapter/client-side provider detail and does not cross the controller boundary as a public contract.

## Architecture Rules

1. Application Layer owns storefront DTO construction.
2. Application Layer owns tenant credential selection and decryption.
3. Runtime owns canonical payment execution and immutable `RuntimeResult`.
4. Adapter owns Stripe provider translation.
5. Runtime must remain free of Stripe SDK, tenant configuration, and client-secret concepts.
6. Frontend and storefront contracts must remain unchanged.

## Backward Compatibility

Preserved:

- Controllers unchanged.
- Routes unchanged.
- Public DTO shape unchanged.
- Frontend/storefront expectations unchanged.
- Existing SQL persistence, encrypted client-secret storage, and event publication remain the application persistence path.

## Certification Decision

The STRIPE-CONTRACT-CLOSURE-1 application contract is certified for the storefront create PaymentIntent path. The missing public response values are now supplied by an application-owned boundary while the frozen Runtime remains the canonical execution authority and provider-specific artifacts remain outside Runtime.

Remaining broader production wiring work still exists for webhook durable prior evidence, negative/non-terminal outcome admission, advanced operations, CommerceHub payment/refund paths, and full single-writer runtime rollout.

Recommended next phase: **STRIPE-CONTRACT-CLOSURE-2 Webhook and Outcome Admission Closure**.

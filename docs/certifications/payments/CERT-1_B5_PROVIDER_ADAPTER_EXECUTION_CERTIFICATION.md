# CERT-1 B5 Provider Adapter Execution Certification

Certification: CERT-1  
Phase: B  
Control: B5 - Provider Adapter Execution Certification  
Scope: Provider Adapter Execution only  
Certification Date: 2026-07-20  
Source of Truth: Source code implementation only

## Final Certification

PASS WITH OBSERVATIONS

Provider Adapter Execution is implemented as backend-owned provider protocol translation and communication. Stripe and Paymob CommerceHub online payment initiation enter `CommerceHubPaymentService`, which resolves an `IPaymentProviderAdapter` by provider code and invokes `CreateSessionAsync`. Fawry and Manual Payment execution use provider-specific runtime application services and runtime bridges that invoke `IFawryPaymentAdapter` or `IManualPaymentProviderAdapter`, then submit the resulting canonical provider evidence into `PaymentRuntimeOrchestrator`.

The certification is issued with observations because implemented adapter execution is split by provider family. Stripe and Paymob have the shared `IPaymentProviderAdapter` contract used by `CommerceHubPaymentService`; Fawry and Manual Payment use provider-specific runtime bridge contracts. That split is implemented behavior, not a failure. One implementation-backed DTO safety observation is recorded for Paymob raw provider error body metadata.

## Source Evidence

- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.API/Services/Payments/ProviderContracts/CanonicalPaymentGatewayContracts.cs`
- `PointOfSale.API/Services/Payments/ProviderContracts/CanonicalProviderError.cs`
- `PointOfSale.API/Services/Payments/ProviderContracts/ProviderExecutionContracts.cs`
- `PointOfSale.API/Services/Payments/Stripe/StripeAdapter.cs`
- `PointOfSale.API/Services/Payments/Stripe/StripePaymentIntentAdapter.cs`
- `PointOfSale.API/Services/Payments/Stripe/StripeRuntimeBridge.cs`
- `PointOfSale.API/Services/Payments/Stripe/StripeRuntimeApplicationService.cs`
- `PointOfSale.API/Services/Payments/Stripe/StripeFoundationServiceCollectionExtensions.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobAdapter.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentAdapter.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeBridge.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeApplicationService.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobProviderAdapterServiceCollectionExtensions.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobFoundationServiceCollectionExtensions.cs`
- `PointOfSale.API/Services/Payments/Adapters/FawryPaymentAdapter.cs`
- `PointOfSale.API/Services/Payments/Fawry/FawryRuntimeBridge.cs`
- `PointOfSale.API/Services/Payments/Fawry/FawryRuntimeApplicationService.cs`
- `PointOfSale.API/Services/Payments/Fawry/FawryFoundationServiceCollectionExtensions.cs`
- `PointOfSale.API/Services/Payments/Fawry/FawryApplicationServiceCollectionExtensions.cs`
- `PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentProviderAdapter.cs`
- `PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeBridge.cs`
- `PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs`
- `PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentFoundationServiceCollectionExtensions.cs`
- `PointOfSale.Core/Payments/Runtime/Domain/PaymentRuntimeObjects.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.Tests/Architecture/PaymentRemediation001CanonicalRuntimeConvergenceTests.cs`
- `PointOfSale.Tests/Architecture/PaymentRemediation002RuntimeAuthorityClosureTests.cs`
- `PointOfSale.Tests/Architecture/FawryRuntimeBridgeCertificationTests.cs`
- `PointOfSale.Tests/Architecture/ManualPaymentRuntimeBridgeCertificationTests.cs`

## Executive Summary

Provider execution is owned by backend payment services and provider adapters. Browser DTOs do not own provider communication.

For Stripe and Paymob CommerceHub online initiation, execution starts in `CommerceHubPaymentService.InitiateAsync` after payment initiation access checks, payment locking, payment record preparation, financial readiness gating, and reservation recomposition. Adapter execution starts at `GetProviderAdapter(...).CreateSessionAsync(...)` and ends when a `PaymentProviderSessionResult` is returned or a `PaymentProviderAdapterException` is thrown.

For Fawry, execution starts in `FawryRuntimeApplicationService.CreatePaymentIntentAsync`, which resolves tenant credentials, builds a core runtime `PaymentSession` and `PaymentAction`, creates a one-operation `FawryCredentialHandoff`, and calls `IFawryRuntimeBridge.ExecuteAsync`. Adapter execution starts inside the bridge at `_adapter.InitializePaymentAsync(...)` and ends when `FawryPaymentAdapterResult` is converted into provider evidence and passed to `PaymentRuntimeOrchestrator`.

For Manual Payment, execution starts in `ManualPaymentRuntimeApplicationService.InitializeReferencePaymentAsync`, which admits tenant capability, builds runtime objects, constructs `ManualPaymentCanonicalRequest`, and calls `IManualPaymentRuntimeBridge.ExecuteAsync`. Adapter execution starts at `_adapter.CreatePendingPayment(...)` and ends when canonical manual-payment provider evidence is submitted into `PaymentRuntimeOrchestrator`.

Provider communication is owned by the provider adapters or provider HTTP/SDK clients they wrap: `StripeAdapter`, `StripePaymentIntentAdapter` plus `TenantScopedStripePaymentIntentClient`, `PaymobAdapter`, `PaymobPaymentAdapter` plus `PaymobOfficialApiHttpClient`, and `FawryPaymentAdapter`. Manual Payment has no external HTTP communication in the implemented adapter; it translates canonical manual-payment instructions into provider evidence.

## Adapter Entry

Stripe CommerceHub adapter entry is `PaymentProviderSessionRequest` into `StripeAdapter.CreateSessionAsync`. The request contains backend-sourced payment id, marketplace order id, order number, provider reference, existing provider intent id, amount, currency, customer/contact fields, address fields, and correlation id.

Paymob CommerceHub adapter entry is `PaymentProviderSessionRequest` into `PaymobAdapter.CreateSessionAsync`. `CommerceHubPaymentService` builds it from `CommerceHubMarketplaceOrder`, `CommerceHubPayment`, resolved country code, and correlation id.

Stripe runtime bridge entry is `StripeExecutionRequest`, containing `RuntimeExecutionContext`, `PaymentAction`, `PaymentSession`, prior runtime state collections, and execution metadata. `StripeRuntimeBridge` passes only the `PaymentAction` into `IStripePaymentIntentAdapter.CreateAsync`.

Paymob runtime bridge entry is `PaymobExecutionRequest`, with the same canonical runtime shape. `PaymobRuntimeBridge` passes only the `PaymentAction` into `IPaymobPaymentAdapter.CreateAsync`; Paymob credentials are consumed through `IPaymobAdapterCredentialHandoff`.

Fawry runtime bridge entry is `FawryExecutionRequest`, containing runtime context, action, session, prior runtime state, execution metadata, and `FawryCredentialHandoff`. The bridge consumes one-operation credentials and passes `PaymentAction` plus `FawryPaymentCredentials` into `IFawryPaymentAdapter.InitializePaymentAsync`.

Manual Payment runtime bridge entry is `ManualPaymentExecutionRequest`, containing runtime context, action, session, prior runtime state, execution metadata, and `ManualPaymentCanonicalRequest`. The bridge passes the canonical manual request into `IManualPaymentProviderAdapter.CreatePendingPayment`.

## Adapter Execution Pipeline

The implemented Stripe CommerceHub adapter pipeline is:

1. `PaymentProviderSessionRequest`
2. Configuration validation through `CommerceHubPaymentsOptions.Stripe`
3. Stripe `PaymentIntentCreateOptions` mapping
4. Stripe SDK `PaymentIntentService.CreateAsync` or `GetAsync`
5. Stripe `PaymentIntent` response
6. `PaymentProviderSessionResult` containing client secret, provider intent id, and serialized raw response

The implemented Paymob CommerceHub adapter pipeline is:

1. `PaymentProviderSessionRequest`
2. Configuration validation through `CommerceHubPaymobOptions`
3. `PaymobAuthRequest`
4. HTTP POST `auth/tokens`
5. `PaymobOrderRegistrationRequest`
6. HTTP POST `ecommerce/orders`
7. `PaymobPaymentKeyRequest`
8. HTTP POST `acceptance/payment_keys`
9. iframe URL construction
10. `PaymentProviderSessionResult` containing iframe URL, payment key, provider order id, and serialized raw response

The implemented Fawry runtime adapter pipeline is:

1. `PaymentAction` plus one-operation `FawryPaymentCredentials`
2. `FawryPaymentRequestFactory.Create`
3. `FawryChargeRequest` mapping and signature creation
4. HTTP POST `ECommerceWeb/Fawry/payments/charge`
5. `FawryChargeResponse`
6. response acceptance and integrity validation
7. `ProviderEvidence` creation
8. `PaymentRuntimeOrchestrator.Execute`
9. `FawryExecutionResult`

The implemented Manual Payment adapter pipeline is:

1. `ManualPaymentCanonicalRequest`
2. manual-payment lineage, channel, policy, instruction, expiration, amount, currency, and customer normalization
3. `ManualPaymentProviderRequest`
4. `ManualPaymentProviderEvidence`
5. canonical `ProviderEvidence`
6. `PaymentRuntimeOrchestrator.Execute`
7. `ManualPaymentExecutionResult`

## Canonical Contract

The shared CommerceHub adapter contract is `IPaymentProviderAdapter`. It exposes `ProviderCode`, `PaymentProviderCapabilities`, `CreateSessionAsync`, `ParseWebhookAsync`, `GetStatusAsync`, and optional `ParseCallbackAsync`.

The shared CommerceHub canonical request/response models are:

- `PaymentProviderSessionRequest`
- `PaymentProviderSessionResult`
- `PaymentProviderWebhookRequest`
- `PaymentProviderWebhookResult`
- `PaymentProviderCallbackRequest`
- `PaymentProviderCallbackResult`
- `PaymentProviderStatusRequest`
- `PaymentProviderStatusResult`
- `CanonicalProviderError`

The core runtime canonical evidence contract is `ProviderEvidence` in `PointOfSale.Core.Payments.Runtime.Domain`. Runtime bridges convert provider responses or provider failures into `ProviderEvidence` before calling `PaymentRuntimeOrchestrator`.

Provider-specific DTO ownership is implemented inside provider adapter files:

- Stripe owns `PaymentIntentCreateOptions`, `RequestOptions`, `StripePaymentIntentRequest`, `StripePaymentIntentProviderArtifact`, and Stripe metadata mapping.
- Paymob owns `PaymobAuthRequest`, `PaymobOrderRegistrationRequest`, `PaymobPaymentKeyRequest`, Paymob response DTOs, billing data, request metadata keys, redirect URL construction, and Paymob error translation.
- Fawry owns `FawryChargeRequest`, `FawryChargeResponse`, Fawry credentials, charge item mapping, amount/language/mobile/email normalization, signature creation, and response integrity validation.
- Manual Payment owns `ManualPaymentCanonicalRequest`, `ManualPaymentProviderRequest`, `ManualPaymentProviderEvidence`, manual channel mappings, instruction identifier construction, and canonical evidence projection.

Translation ownership is adapter-owned. Normalization is implemented in adapter request factories and metadata mappers. Provider responses are normalized into `PaymentProviderSessionResult`, `PaymentProviderWebhookResult`, `PaymentProviderStatusResult`, or runtime `ProviderEvidence`.

## Provider Isolation

Stripe adapter execution does not reference Paymob, Fawry, or Manual Payment implementations. Stripe CommerceHub adapter code depends on Stripe options, `IHttpClientFactory`, Stripe SDK types, and Stripe provider DTO translation.

Paymob adapter execution does not reference Stripe, Fawry, or Manual Payment implementations. Paymob code depends on Paymob options, Paymob capability providers, Paymob telemetry, credential handoff, Paymob HTTP DTOs, and Paymob HTTP clients.

Fawry adapter execution does not reference Stripe, Paymob, or Manual Payment implementations. Fawry uses Fawry credentials, Fawry DTOs, Fawry signature/integrity logic, and its named HTTP client.

Manual Payment adapter execution does not reference Stripe, Paymob, or Fawry implementations. It has no external HTTP dependency and converts manual-payment canonical instructions into manual-payment provider evidence.

Shared logic is limited to canonical contracts, runtime domain objects, `PaymentRuntimeOrchestrator`, dependency injection, logging/telemetry, and provider-neutral payment services. No provider adapter imports another provider adapter.

## Dependency Inversion

The primary shared adapter interface is `IPaymentProviderAdapter`, owned under `PointOfSale.API.Services.Payments.ProviderContracts`. Stripe and Paymob implement this interface and are registered as scoped services:

- `AddScoped<IPaymentProviderAdapter, StripeAdapter>` in `StripeFoundationServiceCollectionExtensions`
- `AddScoped<IPaymentProviderAdapter, PaymobAdapter>` in `PaymobProviderAdapterServiceCollectionExtensions`

Runtime bridge interfaces are provider-specific:

- `IStripeRuntimeBridge`
- `IPaymobRuntimeBridge`
- `IFawryRuntimeBridge`
- `IManualPaymentRuntimeBridge`

Provider discovery for CommerceHub Stripe/Paymob is implemented by injecting `IEnumerable<IPaymentProviderAdapter>` into `CommerceHubPaymentService` and building an `IReadOnlyDictionary<string, IPaymentProviderAdapter>` keyed by `ProviderCode.Normalized`. Runtime resolution is performed by `GetProviderAdapter(providerCode)`.

Provider replacement is supported at the DI boundary for the registered interfaces. Replacing Stripe or Paymob CommerceHub adapter execution requires replacing the corresponding `IPaymentProviderAdapter` registration. Replacing Fawry or Manual Payment execution requires replacing their provider-specific adapter or runtime bridge registration.

`IProviderPaymentGateway` and `IPaymentGateway` contracts exist with concrete gateway classes for Stripe, Paymob, Fawry, and Manual Wallet, but `Program.cs` and the certification tests indicate they are not registered as the active checkout runtime path. Implemented active execution uses `CommerceHubPaymentService` with `IPaymentProviderAdapter` for Stripe/Paymob and provider-specific runtime application services for Fawry/Manual Payment.

## DTO Safety

Adapter request values are backend-composed at adapter entry. Stripe and Paymob CommerceHub `PaymentProviderSessionRequest` values are built from persisted `CommerceHubMarketplaceOrder` and `CommerceHubPayment` state. Fawry and Manual Payment runtime requests are built by provider runtime application services from backend payment intent/order context and tenant configuration.

Adapters do not trust browser-supplied totals at the provider boundary. Amount and currency are read from backend payment/order state or canonical runtime metadata and are converted/formatted for provider APIs.

Provider-derived values are mapped into canonical response/evidence fields:

- Stripe maps PaymentIntent id, client secret, latest charge id, status, amount, currency, and failure reason.
- Paymob maps provider order id, payment key, iframe URL, callback/webhook transaction id, merchant order id, amount, currency, pending/success/void/refund flags, and failure reason.
- Fawry maps reference number, merchant reference, order status, amount, currency, payment URL, expiration, and canonical status metadata.
- Manual Payment maps instruction identifier, channel, expiration, reference/proof policy, provider status, and canonical status metadata.

Secret handling is implemented through configuration options and tenant credential resolvers:

- Stripe CommerceHub adapter uses configured secret key and publishable key from `CommerceHubPaymentsOptions.Stripe`.
- Stripe runtime application service resolves tenant Stripe credentials and injects them through `IStripeRuntimeCredentialAccessor`.
- Paymob CommerceHub adapter uses `CommerceHubPaymobOptions`; Paymob runtime uses `IPaymobAdapterCredentialHandoff`.
- Fawry runtime resolves tenant credentials and passes them through a one-operation `FawryCredentialHandoff`, which rejects reuse, stale handoff, action mismatch, tenant mismatch, and missing tenant metadata.
- Manual Payment uses tenant manual-payment configuration and has no external secret-bearing HTTP provider call.

Observation: `PaymobProviderErrorTranslator.Translate(PaymobOfficialApiException)` includes `provider_body` in canonical error metadata. In the Paymob runtime bridge, canonical provider error metadata is copied into rejected provider evidence. This is implementation-backed DTO safety exposure for raw provider error bodies. The implementation does not prove that the body contains cardholder data, but it does prove that raw provider response bodies can cross into canonical error metadata.

## Error Translation

Stripe CommerceHub adapter maps `StripeException` into `CanonicalProviderError` with authentication, authorization, rate limited, validation, provider unavailable, provider rejected, or unknown classifications. Stripe webhook verification failures are mapped to validation errors for invalid signatures or expired timestamps. Malformed webhook JSON is mapped to validation.

Stripe runtime adapter maps Stripe SDK exceptions into canonical Stripe provider errors. Argument errors become request validation failures. Invalid operation errors become adapter configuration failures.

Paymob CommerceHub adapter maps Paymob official API exceptions, HTTP request exceptions, JSON exceptions, timeout/cancellation, argument exceptions, and invalid operation exceptions into `CanonicalProviderError`. Paymob callback and webhook parsing performs HMAC verification in the adapter boundary.

Paymob runtime adapter maps checkpoint errors to configuration, Paymob official API errors through `PaymobProviderErrorTranslator`, HTTP request errors to network, JSON errors to response validation, argument errors to request validation, and invalid operations to adapter configuration.

Fawry adapter maps non-success HTTP status codes to canonical authentication, authorization, rate limited, validation, provider unavailable, or provider rejected errors. Official Fawry rejection responses are mapped through `FawryProviderErrorTranslator.FromOfficialResponse`. HTTP request failures become network errors. Timeouts become provider unavailable. JSON and argument errors become validation. Invalid operation errors become configuration.

Manual Payment adapter maps rejected manual-payment policy cases into rejected provider evidence. Argument errors become validation errors. Invalid operation errors become configuration errors. Unsupported capabilities produce rejected provider evidence with `MANUAL_CAPABILITY_UNSUPPORTED`.

Runtime bridges do not discard provider failures. When an adapter returns no evidence but returns a provider error, each bridge creates rejected canonical provider evidence and still passes it through `PaymentRuntimeOrchestrator`. Runtime application services then map canonical provider errors into provider-specific application exceptions.

## Provider Independence

Stripe implementation does not depend on Paymob, Fawry, or Manual Payment.

Paymob implementation does not depend on Stripe, Fawry, or Manual Payment.

Fawry implementation does not depend on Stripe, Paymob, or Manual Payment.

Manual Payment implementation does not depend on Fawry, Stripe, or Paymob.

The source imports and constructor dependencies support provider independence. Cross-provider coupling was not identified in the inspected adapter execution paths.

## Runtime Boundary

Provider adapter responsibility ends at provider response translation, canonical provider error translation, or canonical provider evidence production.

For Stripe and Paymob CommerceHub initiation, adapter execution ends when `PaymentProviderSessionResult` or `PaymentProviderAdapterException` returns to `CommerceHubPaymentService`. `CommerceHubPaymentService` owns payment record persistence, session persistence, locking, retry/recovery policy, and checkout-facing response mapping.

For Fawry and Manual Payment runtime bridges, adapter execution ends when adapter result evidence or error evidence is handed to `PaymentRuntimeOrchestrator`. Runtime decision, transaction, settlement-result evaluation, and application projection are outside the adapter.

Provider adapters do not own pricing, inventory, order admission, settlement, refunds, accounting, CommerceHub business workflow, or marketplace business workflow. The inspected adapter files do not mutate order totals, inventory, coupons, order admission, accounting ledgers, marketplace records, or settlement records.

## Critical Invariants

- Adapters are entered from backend services, not directly from controllers or browser DTOs.
- Stripe and Paymob CommerceHub runtime resolves provider adapters through `IPaymentProviderAdapter` registrations.
- Fawry and Manual Payment runtime bridges execute provider-specific adapters and then submit canonical provider evidence to `PaymentRuntimeOrchestrator`.
- Provider adapters translate canonical/backend runtime input into provider DTOs.
- Provider adapters translate provider responses into canonical session results, callback/webhook/status results, or canonical provider evidence.
- Provider adapters translate provider failures into `CanonicalProviderError`.
- Adapters do not own order admission.
- Adapters do not own checkout validation.
- Adapters do not own inventory.
- Adapters do not own coupons.
- Adapters do not own pricing authority or order total authority.
- Adapters do not own settlement persistence.
- Adapters do not own refunds in the B5 initiation boundary.
- Adapters do not own accounting.
- Provider-specific DTOs stay inside provider-specific namespaces/files.
- Provider-specific secret access is localized to options, tenant credential resolvers, credential accessors, or credential handoff objects.

## Architecture Boundaries

Provider Adapters own:

- Protocol translation
- Provider request creation
- Provider response mapping
- Authentication material placement in provider requests
- HTTP or SDK communication where the provider requires it
- Provider-specific DTOs
- Provider-specific signature or HMAC validation where implemented
- Provider error translation into canonical provider errors
- Provider evidence creation where implemented

Provider Adapters do not own:

- Checkout
- Pricing
- Inventory
- Coupons
- Order Admission
- Settlement
- Refunds in this B5 initiation boundary
- Accounting
- Marketplace business workflow
- CommerceHub business workflow
- Fulfillment

## Architecture Risks

1. Paymob raw provider error body metadata crosses into canonical provider error metadata.

`PaymobProviderErrorTranslator.Translate(PaymobOfficialApiException)` adds `provider_body` from `exception.ResponseBody` into `CanonicalProviderError.Metadata`. `PaymobExecutionFactory.ProviderErrorEvidence` copies canonical provider error metadata into rejected provider evidence. The implementation therefore permits raw Paymob error response bodies to be carried as canonical provider evidence metadata on runtime adapter failure paths.

No other architectural violations were identified.

## Certification Checklist

| Area | Result |
| --- | --- |
| Adapter Entry | PASS |
| Execution Pipeline | PASS |
| Canonical Contract | PASS |
| Provider Isolation | PASS |
| Dependency Inversion | PASS |
| DTO Safety | PASS |
| Error Translation | PASS |
| Provider Independence | PASS |
| Runtime Boundary | PASS |
| Critical Invariants | PASS |
| Architecture Boundaries | PASS |
| Overall Certification | PASS WITH OBSERVATIONS |


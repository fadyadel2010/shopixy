# PAYMENT-REMEDIATION-001 Canonical Payment Runtime Convergence

## Executive Summary

PAYMENT-REMEDIATION-001 converges CommerceHub online payment execution to the canonical runtime chain:

Controller -> CommerceHubPaymentOrchestrationService -> CommerceHubPaymentService -> IPaymentProviderAdapter -> Provider Adapter -> Provider.

This remediation does not add payment features, alter payment lifecycle rules, alter finance behavior, or change checkout business behavior. The change is limited to removing runtime bypasses and provider-specific execution paths from controllers and the CommerceHub payment runtime.

## Certification Findings Addressed

- Legacy provider controllers could execute through provider-named runtime services.
- Paymob webhook and storefront controllers exposed a legacy provider runtime boundary.
- Stripe protocol work was present outside the canonical provider adapter boundary.
- `IPaymentRuntime` existed as a disabled abstraction rather than an active runtime authority.

## Legacy Runtime Inventory

- `IPaymobPaymentService` and `PaymobPaymentService` remain as legacy storefront checkout compatibility services, but they are not controller-facing CommerceHub payment runtime authorities.
- `IStripePaymentService` and `StripePaymentService` remain for legacy non-CommerceHub paths and DTO compatibility, but CommerceHub online Stripe execution no longer depends on them.
- CommerceHub online payment authority is `CommerceHubPaymentOrchestrationService` plus `CommerceHubPaymentService`.
- Provider protocol authority is `IPaymentProviderAdapter` implementations.

## Controller Convergence

The following controllers now delegate payment execution to `ICommerceHubPaymentOrchestrationService`:

- `StorefrontStripePaymentsController`
- `StripeWebhooksController`
- `StorefrontPaymobPaymentsController`
- `PaymobWebhooksController`

Controllers preserve their public DTO contracts where possible, but no longer inject `IStripePaymentService`, `IPaymobPaymentService`, provider SDK services, or provider runtime application services.

## Provider Convergence

Provider protocol operations are owned by `IPaymentProviderAdapter` implementations. CommerceHub runtime uses provider code lookup to select an adapter and does not construct provider SDK clients directly.

## Stripe Convergence

`StripeAdapter` implements `IPaymentProviderAdapter` and owns:

- Stripe PaymentIntent creation and recovery.
- Stripe webhook signature parsing.
- Stripe payment status lookup.
- Stripe SDK client construction.

`CommerceHubPaymentService` calls `GetProviderAdapter(CommerceHubPaymentMethods.Stripe)` for Stripe session creation, webhook parsing, and session recovery.

## Paymob Convergence

`PaymobAdapter` remains the canonical Paymob provider adapter and owns provider protocol translation. CommerceHub Paymob session creation and webhook parsing route through `GetProviderAdapter(CommerceHubPaymentMethods.Paymob)`.

The legacy Paymob service remains available for existing compatibility paths, including recovery code outside CommerceHub controller execution, but it is no longer the authoritative online payment runtime for CommerceHub controllers.

## Runtime Registration Decision

`IPaymentRuntime` is canonical and active.

`PaymentRuntimeOrchestrator` implements `IPaymentRuntime`, and `AddPaymentRuntimeFoundation` registers `PaymentRuntimeOrchestrator` as the single runtime authority exposed through `IPaymentRuntime`.

Disabled placeholder implementations were removed:

- `DisabledPaymentRuntime`
- `DisabledPaymentProvider`
- `DisabledPaymentPolicyEngine`

## Dependency Graph Before

```text
Storefront/Provider Controller
  -> IStripePaymentService or IPaymobPaymentService
  -> provider runtime service
  -> provider SDK / protocol
  -> provider

CommerceHubPaymentService
  -> direct Stripe SDK parsing/PaymentIntent construction
  -> provider

IPaymentRuntime
  -> disabled placeholder
```

## Dependency Graph After

```text
Controller
  -> CommerceHubPaymentOrchestrationService
  -> CommerceHubPaymentService
  -> IPaymentProviderAdapter
  -> StripeAdapter / PaymobAdapter
  -> Provider

IPaymentRuntime
  -> PaymentRuntimeOrchestrator
```

## Files Added

- `PointOfSale.API/Services/Payments/Stripe/StripeAdapter.cs`
- `PointOfSale.Tests/Architecture/PaymentRemediation001CanonicalRuntimeConvergenceTests.cs`
- `docs/architecture/payment/PAYMENT-REMEDIATION-001_CANONICAL_PAYMENT_RUNTIME_CONVERGENCE.md`

## Files Modified

- `PointOfSale.API/Controllers/StorefrontStripePaymentsController.cs`
- `PointOfSale.API/Controllers/StripeWebhooksController.cs`
- `PointOfSale.API/Controllers/StorefrontPaymobPaymentsController.cs`
- `PointOfSale.API/Controllers/PaymobWebhooksController.cs`
- `PointOfSale.API/DTOs/CommerceHub/CommerceHubOrderDtos.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.API/Services/Payments/Stripe/StripeFoundationServiceCollectionExtensions.cs`
- `PointOfSale.Core/Payments/Runtime/Domain/PaymentRuntimeOrchestrator.cs`
- `PointOfSale.Infrastructure/Payments/Runtime/PaymentRuntimeServiceCollectionExtensions.cs`
- `PointOfSale.Tests/Architecture/CommerceHubPaymentAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/PaymentFinalAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/PaymentRuntimeFoundationCertificationTests.cs`
- `PointOfSale.Tests/Architecture/PaymobContractClosureApplicationCertificationTests.cs`
- `PointOfSale.Tests/Architecture/PaymobConvergence1CertificationTests.cs`
- `PointOfSale.Tests/Architecture/PaymobProvider1CertificationTests.cs`
- `PointOfSale.Tests/Architecture/StripeContractClosureApplicationCertificationTests.cs`
- `PointOfSale.Tests/Architecture/StripeProvider1CertificationTests.cs`
- `PointOfSale.Tests/CommerceHub/CommerceHubPaymentServiceTests.cs`
- `PointOfSale.Tests/Payments/WebhookSecurityHardeningTests.cs`
- `PointOfSale.Tests/Security/StorefrontTenantIsolationTests.cs`

## Files Removed

- `PointOfSale.Infrastructure/Payments/Runtime/PaymentRuntimePlaceholders.cs`

## Compatibility

HTTP routes and response DTOs for existing Stripe and Paymob storefront/webhook endpoints are preserved where possible. Controller implementations now map canonical CommerceHub payment orchestration responses back to the existing provider-specific DTO shapes.

Legacy provider services remain registered for compatibility paths outside the remediated CommerceHub controller runtime. They are no longer authoritative for CommerceHub online controller execution.

## Testing

Added `PaymentRemediation001CanonicalRuntimeConvergenceTests` to guard:

- Payment controllers do not depend on provider runtime services.
- Payment controllers do not bypass orchestration.
- CommerceHub runtime uses `IPaymentProviderAdapter` for Stripe and Paymob.
- Stripe SDK construction and webhook parsing are isolated to `StripeAdapter`.
- Paymob provider protocol remains isolated to `PaymobAdapter`.
- `IPaymentRuntime` resolves to the active `PaymentRuntimeOrchestrator`.

Updated existing architecture tests that encoded obsolete disabled-runtime and provider-service controller expectations.

## Verification

Verified:

- `dotnet build PointOfSale.sln --no-restore --nologo -v:minimal`
- `dotnet build PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --nologo -v:minimal`

Focused remediation architecture tests pass except for pre-existing documentation truth tests outside PAYMENT-REMEDIATION-001 that assert older project-context text:

- `CommerceHubPaymentAuthorityCertificationTests.CertificationArtifacts_MustRecordM3CommerceHubPaymentAuthorityTruth`
- `PaymentFinalAuthorityCertificationTests.CertificationArtifacts_MustRecordM3IFinalPaymentAuthorityTruth`

## Remaining Risks

- Legacy provider runtime services still exist for compatibility paths. Architecture tests now guard that they are not authoritative for CommerceHub controller execution.
- Paymob callback redirects for canonical CommerceHub payment rows can only safely route when stored tenant/storefront routing context is available. If that context is unavailable, the controller fails closed instead of deriving tenant identity from customer identity.
- Existing broader architecture documentation tests are already out of sync with current repo documentation and should be handled in a documentation truth pass.

## Next Phase

Run a separate documentation truth reconciliation for stale certification artifact assertions, then plan any future removal of legacy provider runtime services only after all non-CommerceHub compatibility paths have been explicitly migrated.

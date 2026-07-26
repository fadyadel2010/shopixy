# PAYMENT-REMEDIATION-002 Final Runtime Authority Closure

## Executive Summary

PAYMENT-REMEDIATION-002 closes the remaining CommerceHub online payment runtime bypasses. Online payment execution now converges through CommerceHub orchestration, then the payment runtime boundary, then `CommerceHubPaymentService`, then provider adapters. Provider protocol parsing and HMAC validation are adapter-owned.

## Certification Findings Addressed

- REM-1: Paymob browser callback HMAC verification and field handling no longer live in `CommerceHubPaymentService`.
- REM-2: `CheckoutService` no longer depends on `IPaymentGateway` for Stripe or Paymob online payment initiation.
- REM-3: `CheckoutPaymentIntentRecoveryService` no longer depends on `IStripePaymentService` or `IPaymobPaymentService`.

## Paymob Protocol Boundary Closure

`PaymobAdapter` now owns canonical browser callback parsing through `ParseCallbackAsync`. It uses the existing Paymob webhook evidence verifier and returns `PaymentProviderCallbackResult`.

`CommerceHubPaymentService` now receives canonical callback evidence only. It uses the normalized provider reference, provider intent id, transaction id, success state, and validation result to resolve local payment authority. It no longer computes Paymob callback HMACs, owns Paymob callback field ordering, or depends on `CommerceHubPaymobOptions`.

## Checkout Runtime Convergence

`CheckoutService` now routes online payment session creation through `ICommerceHubPaymentOrchestrationService`.

Compatibility payment methods that are not CommerceHub online providers, including Fawry, manual wallet, and COD, continue to use existing local payment intent behavior. They are not registered through `IPaymentGateway` as an online CommerceHub runtime authority.

## Recovery Runtime Convergence

`CheckoutPaymentIntentRecoveryService` now depends on `IPaymentRuntime` for Stripe and Paymob recovery initiation. Provider-specific recovery services are removed from the recovery constructor and fields.

Manual wallet recovery remains local because it repairs durable manual evidence rather than initiating an online provider session.

## Runtime Authority Verification

Authoritative online path after remediation:

```text
Controller / Checkout
  -> CommerceHubPaymentOrchestrationService
  -> IPaymentRuntime
  -> CommerceHubPaymentService
  -> IPaymentProviderAdapter
  -> Provider Adapter
  -> Provider
```

Forbidden paths removed from authoritative runtime:

```text
Checkout -> IPaymentGateway -> Provider runtime
Recovery -> StripePaymentService
Recovery -> PaymobPaymentService
CommerceHubPaymentService -> Paymob callback HMAC / field ordering
```

## Dependency Graph Before

```text
CommerceHubPaymentOrchestrationService
  -> ICommerceHubPaymentService
  -> CommerceHubPaymentService
  -> IPaymentProviderAdapter

CheckoutService
  -> IPaymentGateway
  -> StripePaymentGateway / PaymobPaymentGateway
  -> StripePaymentService / PaymobPaymentService

CheckoutPaymentIntentRecoveryService
  -> IStripePaymentService
  -> IPaymobPaymentService

CommerceHubPaymentService
  -> Paymob callback HMAC and callback field ordering
```

## Dependency Graph After

```text
CommerceHubPaymentOrchestrationService
  -> IPaymentRuntime
  -> CommerceHubPaymentService
  -> IPaymentProviderAdapter
  -> StripeAdapter / PaymobAdapter

CheckoutService
  -> ICommerceHubPaymentOrchestrationService
  -> IPaymentRuntime

CheckoutPaymentIntentRecoveryService
  -> IPaymentRuntime

PaymobAdapter
  -> Paymob callback parsing
  -> Paymob HMAC verification
  -> Canonical callback result
```

## Files Added

- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentRuntime.cs`
- `PointOfSale.Tests/Architecture/PaymentRemediation002RuntimeAuthorityClosureTests.cs`
- `docs/architecture/payment/PAYMENT-REMEDIATION-002_FINAL_RUNTIME_AUTHORITY_CLOSURE.md`

## Files Modified

- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentOrchestrationService.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobAdapter.cs`
- `PointOfSale.API/Services/Payments/ProviderContracts/CanonicalPaymentGatewayContracts.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.Tests/Architecture/CommerceHubPaymentOrchestrationArchitectureTests.cs`
- `PointOfSale.Tests/Architecture/ManualPaymentCertificationClosure4Tests.cs`
- `PointOfSale.Tests/Architecture/PaymentRemediation001CanonicalRuntimeConvergenceTests.cs`
- `PointOfSale.Tests/CommerceHub/CommerceHubPaymentOrchestrationServiceTests.cs`
- `PointOfSale.Tests/CommerceHub/CommerceHubPaymentServiceTests.cs`
- `PointOfSale.Tests/Security/StorefrontTenantIsolationTests.cs`

## Files Removed

None.

## Compatibility Analysis

No payment lifecycle, order lifecycle, inventory finalization, vendor earnings, settlement, accounting, outbox, notifications, recovery semantics, or idempotency rules were intentionally changed.

The remaining legacy provider runtime services are still registered for non-authoritative compatibility and existing non-CommerceHub flows. They are no longer the checkout or recovery authority for CommerceHub online Stripe or Paymob initiation.

`IPaymentGateway` and `IProviderPaymentGateway` are no longer registered in DI, preventing those abstractions from becoming the active CommerceHub online payment runtime.

## Testing

Passed:

```text
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~PaymentRemediation002RuntimeAuthorityClosureTests|FullyQualifiedName~CommerceHubPaymentOrchestrationArchitectureTests|FullyQualifiedName~CommerceHubPaymentOrchestrationServiceTests|FullyQualifiedName~PaymentRemediation001CanonicalRuntimeConvergenceTests|FullyQualifiedName~ManualPaymentCertificationClosure4Tests" --no-restore --logger "console;verbosity=minimal"
```

Result: 23 passed, 0 failed.

Broader architecture filter was also attempted. It currently fails on pre-existing unrelated certification/documentation guards outside PAYMENT-REMEDIATION-002 scope.

## Verification

- `CommerceHubPaymentService` contains no Paymob callback HMAC implementation, callback field ordering, or `PaymobOptionsValidator` dependency.
- `PaymobAdapter` owns callback parsing and HMAC verification.
- `CheckoutService` contains no `IPaymentGateway` or `_paymentGateway` dependency.
- `CheckoutPaymentIntentRecoveryService` contains no Stripe or Paymob payment service dependency.
- `Program.cs` no longer registers `IPaymentGateway` or `IProviderPaymentGateway`.
- New architecture tests guard the canonical runtime chain and protocol isolation.

Remaining occurrence audit:

- `IPaymentGateway`, `IProviderPaymentGateway`, and gateway classes remain in provider contract/source files as inert compatibility code. They are not injected by checkout/recovery and are not registered in DI.
- `IStripePaymentService` and `IPaymobPaymentService` remain registered for legacy compatibility and operational health checks. They are not dependencies of CommerceHub orchestration, `CommerceHubPaymentService`, `CheckoutService`, or `CheckoutPaymentIntentRecoveryService`.
- Provider SDK/protocol calls remain in provider-owned files: Stripe adapter/runtime files and Paymob adapter/evidence/runtime files.
- Historical certification documents under `docs/architecture/payment/certification` still describe the pre-remediation failures by design; they are evidence records, not live runtime references.
- Some pre-existing architecture tests still encode historical certification snapshots unrelated to REM-002 and fail in the broad architecture filter.

## Remaining Risks

- Legacy Stripe and Paymob payment services still exist for compatibility, but are not wired as the CommerceHub online runtime authority.
- Some older certification tests in the repository still encode earlier documentation expectations and fail independently of this remediation.

## Readiness for PAYMENT-CERT-001

The REM-002 runtime authority closure is ready for PAYMENT-CERT-001 re-verification. The canonical CommerceHub online runtime path is explicit and guarded by architecture tests.

## Next Phase

Run PAYMENT-CERT-001 final certification against the updated runtime chain and resolve any unrelated repository-wide certification artifact drift separately.

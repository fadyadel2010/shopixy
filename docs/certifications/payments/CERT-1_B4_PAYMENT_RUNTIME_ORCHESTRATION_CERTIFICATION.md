# CERT-1 B4 Payment Runtime Orchestration Certification

Certification: CERT-1  
Phase: B  
Control: B4 - Payment Runtime Orchestration Certification  
Scope: Payment Runtime Orchestration only  
Certification Date: 2026-07-20  
Source of Truth: Source code implementation only

## Final Certification

PASS WITH OBSERVATIONS

Payment Runtime Orchestration is implemented as a backend-owned post-order-admission runtime. Runtime starts only after an order has been durably admitted or after recovery finds an already committed order that still requires payment-intent materialization. Checkout owns the runtime entry point. Payment runtime services own provider routing, adapter invocation, provider evidence persistence, runtime result mapping, and checkout-facing payment session projection.

The certification is issued with observations because the implemented runtime is split by provider family: Stripe and Paymob storefront checkout enter `ICommerceHubPaymentOrchestrationService` and `IPaymentRuntime`, while Fawry, COD, and ManualWallet enter `IPaymentService`. That is implemented behavior, not a violation. No architectural violations were identified.

## Source Evidence

- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentOrchestrationService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentRuntime.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.API/Services/Payments/PaymentService.cs`
- `PointOfSale.API/Services/Payments/IPaymentService.cs`
- `PointOfSale.API/Services/Payments/ProviderContracts/CanonicalPaymentGatewayContracts.cs`
- `PointOfSale.API/Services/Payments/Stripe/StripeAdapter.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobAdapter.cs`
- `PointOfSale.API/Services/Payments/Fawry/FawryRuntimeApplicationService.cs`
- `PointOfSale.API/Services/Payments/Fawry/FawryPaymentGateway.cs`
- `PointOfSale.API/Services/Payments/ManualPayment/ManualWalletPaymentGateway.cs`
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`
- `PointOfSale.Infrastructure/Payment/PaymentCancellationOutboxProcessor.cs`
- `PointOfSale.Infrastructure/Payment/PaymentCancellationOutboxWorker.cs`
- `PointOfSale.API/Services/Payments/StorefrontUnresolvedPaymentReconciliationHostedService.cs`
- `PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs`
- `PointOfSale.API/DTOs/CommerceHub/CommerceHubOrderDtos.cs`
- `PointOfSale.API/DTOs/Payments/PaymentDtos.cs`
- `PointOfSale.Core/Entities/PaymentIntent.cs`
- `PointOfSale.Core/Enums/PaymentIntentStatus.cs`
- `PointOfSale.Core/Enums/PaymentProvider.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.Tests/Architecture/PaymentRuntimeOrchestrationCertificationTests.cs`
- `PointOfSale.Tests/Architecture/PaymentIntentInfrastructureCertificationTests.cs`
- `PointOfSale.Tests/Architecture/StripeRuntimeBridgeCertificationTests.cs`
- `PointOfSale.Tests/Architecture/PaymobRuntimeBridgeCertificationTests.cs`
- `PointOfSale.Tests/Architecture/FawryRuntimeBridgeCertificationTests.cs`
- `PointOfSale.Tests/Architecture/ManualPaymentRuntimeBridgeCertificationTests.cs`
- `PointOfSale.Tests/Architecture/S16BPaymentAuthorityExternalIdempotencyTests.cs`

## Executive Summary

Payment runtime is owned by backend payment services, not by browser DTOs, provider SDKs, settlement, accounting, marketplace, or fulfillment. The runtime entry is `CheckoutService.ProcessCheckoutAsync` after `CreateOrderAsync` has returned and `orderCommitted = true`, or the recovery worker path in `CheckoutPaymentIntentRecoveryService` for committed orders that still need a complete payment intent/session.

Runtime starts after committed order authority exists. During order admission, `CheckoutService.CreateOrderAsync` may seed a local `PaymentIntent` authority row for `STRIPE`, `PAYMOB`, `FAWRY`, or `MANUALWALLET` through `TryCreateTransactionalPaymentIntentAuthorityAsync`, but that seeding explicitly does not call provider SDKs or execute a provider session. Provider execution starts later in Step 7, after the order transaction has committed.

Runtime ends for checkout initiation when a payment session has been projected back into `CheckoutResponseDto`: payment intent id, provider reference, redirect URL or token, reference code, expiration, manual-wallet display fields, and backend order payment status. Webhook processing, settlement, refunds, accounting, fulfillment, and Marketplace/CommerceHub business workflows are outside this B4 runtime initiation boundary.

Orchestration is owned by `CheckoutService.CreateCheckoutPaymentSessionAsync`, `CommerceHubPaymentOrchestrationService`/`IPaymentRuntime`/`CommerceHubPaymentService` for Stripe and Paymob initiation, and `PaymentService` for Fawry, COD, and ManualWallet initiation. Provider execution is owned by provider adapters or provider-specific runtime application services.

## Runtime Entry

The implemented runtime entry is:

1. `CheckoutService.ProcessCheckoutAsync` validates checkout and calls `CreateOrderAsync`.
2. `CreateOrderAsync` creates and commits the order, reservations, idempotency evidence, outbox evidence, and local payment-intent authority seeding where applicable.
3. `ProcessCheckoutAsync` sets `orderCommitted = true`.
4. Step 7, `PaymentIntent`, calls `CreateCheckoutPaymentSessionAsync` only after order admission.
5. `CreateCheckoutPaymentSessionAsync` routes to the implemented provider runtime branch.
6. The checkout response is populated from backend runtime/session results.

The recovery runtime entry is `CheckoutPaymentIntentRecoveryHostedService`, which periodically resolves `CheckoutPaymentIntentRecoveryService`. It repairs committed orders in `PendingPaymentIntent` or incomplete `PaymentPending` states. Recovery revalidates active reservations before calling `IPaymentRuntime.InitiateAsync` for Stripe/Paymob or `IPaymentService.CreatePaymentIntentAsync`/`ResumeExistingPaymentIntentAsync` for Fawry, COD, and ManualWallet.

## Runtime Pipeline

The implemented checkout runtime pipeline is:

1. Committed order.
2. Local `PaymentIntent` authority exists or is created/resumed.
3. Payment method code is classified.
4. Provider is resolved.
5. Runtime branch is selected.
6. Runtime request is built from backend order/payment data and allowed request fields.
7. Provider adapter or provider-specific runtime application service executes provider initiation.
8. Provider response/evidence is mapped to local runtime/session fields.
9. Backend payment session is projected into checkout response.

For Stripe and Paymob, `CreateCheckoutPaymentSessionAsync` calls `ICommerceHubPaymentOrchestrationService.ExecuteAsync`, which delegates to `IPaymentRuntime.InitiateAsync`, implemented by `CommerceHubPaymentRuntime`, which delegates to `CommerceHubPaymentService.InitiateAsync`. `CommerceHubPaymentService` normalizes provider identity, acquires a payment lock, enforces retry/reuse gates, creates or reuses a pending payment record, builds a `PaymentProviderSessionRequest`, selects `IPaymentProviderAdapter`, calls `CreateSessionAsync`, persists provider identity/session fields, and maps a `CommerceHubPaymentInitiateResponseDto`.

For Fawry, `PaymentService.CreatePaymentIntentAsync` persists a local `PaymentIntent` first, commits that authority, then enters `EnsurePersistedFawryPaymentIntentReadyAsync`. Fawry checkpoint state is advanced before provider mutation. `FawryRuntimeApplicationService` builds a core runtime `PaymentSession` and `PaymentAction`, calls `IFawryRuntimeBridge.ExecuteAsync`, validates the runtime outcome, and projects reference code, expiration, payment URL, provider evidence, and runtime result back to `PaymentService`.

For ManualWallet, `PaymentService` creates a reviewable payment intent through `PaymentLifecycleService.CreateReviewablePaymentIntentAsync`, derives merchant wallet details from tenant payment method configuration, validates private proof-file authority, initializes manual payment runtime/evidence admission, writes durable receipt evidence, and admits the payment for review through `PaymentLifecycleService.AdmitPaymentReviewAsync`.

For COD, `PaymentService` sets `PaymentIntentStatus.Skipped`, confirms the order, emits payment-skipped/order-confirmed events, and does not invoke a gateway.

## Orchestration Ownership

`CheckoutService` owns runtime entry and checkout response mapping. It decides whether the selected payment method uses provider gateway runtime through `PaymentMethodUsesProviderGateway` and `CreateCheckoutPaymentSessionAsync`.

`CommerceHubPaymentOrchestrationService` is a thin orchestration facade for Stripe/Paymob initiation. It records telemetry, logs the canonical boundary, delegates execution to `IPaymentRuntime`, maps expected business exceptions, and wraps unexpected infrastructure failures.

`CommerceHubPaymentService` owns provider selection, retry budget enforcement, payment lock ownership, reusable-session detection, incomplete-session recovery blocking, provider adapter invocation, adapter exception mapping, and provider session persistence for Stripe/Paymob initiation.

`PaymentService` owns Fawry/COD/ManualWallet intent creation, durable local payment intent creation before provider mutation, duplicate active intent rejection, order row locking, idempotency replay/resume, Fawry checkpoint ownership, ManualWallet durable evidence admission, and COD no-gateway confirmation.

Retry ownership is implemented by runtime services and workers, not by provider SDKs. `CommerceHubPaymentService` enforces max attempts and blocks unknown provider outcomes until reconciliation. `CheckoutPaymentIntentRecoveryService` repairs committed orders. `PaymentCancellationOutboxProcessor` retries cancellation lifecycle work with leases and exponential backoff.

Cancellation ownership is implemented outside checkout initiation by `PaymentCancellationOutboxProcessor`, which calls `IPaymentLifecycleService.HandlePaymentResultAsync` with `PaymentLifecycleOutcome.Cancelled`. Checkout initiation passes cancellation tokens through service and adapter calls but does not own business cancellation state.

Timeout ownership is implemented as error classification. `CommerceHubPaymentService` treats `TaskCanceledException` not caused by caller cancellation as provider outcome unknown and records reconciliation-required state. Provider adapters receive cancellation tokens. There is no independent checkout-runtime timeout policy identified beyond configured HTTP clients and propagated cancellation tokens.

Error ownership is implemented by runtime services. Adapter exceptions are mapped to payment checkout exceptions. Duplicate active intents are rejected. Unknown provider outcomes are recorded as pending/reconciliation-required instead of creating another provider session. Fawry registration-started without accepted reference fails closed. ManualWallet missing durable proof/configuration fails closed.

## Provider Resolution

Provider resolution is implementation-specific by path.

`CheckoutService.ResolveTransactionalCheckoutPaymentProvider` maps payment method codes `STRIPE`, `PAYMOB`, `FAWRY`, and `MANUALWALLET` to `PaymentProvider` enum values for local payment-intent seeding. Unsupported methods return null and are not seeded by this method.

`CheckoutService.CreateCheckoutPaymentSessionAsync` routes online payment methods through `CommerceHubPaymentMethods.IsOnline`. The online DTO validator accepts only online providers. For non-online provider methods, it maps `FAWRY` to `PaymentProvider.Fawry`, `MANUALWALLET` to `PaymentProvider.ManualWallet`, and otherwise passes the method code to `PaymentService`.

`CommerceHubPaymentService.NormalizeProvider` defaults blank or Paymob input to Paymob, accepts Stripe, and rejects any other provider with `PAYMENT_PROVIDER_INVALID`. Adapter lookup is case-insensitive through normalized `ProviderCode`.

`PaymentService.DetermineProvider` maps `PaymentMethod.Cash` to `PaymentProvider.Cod`, accepts a parseable requested provider, and defaults card/other methods to Stripe. This is the implemented fallback for the legacy payment-service branch.

Unsupported providers fail closed. `PaymentGateway.CreateSessionAsync` throws `PaymentProviderNotSupportedException` for blank or unregistered provider codes. `CommerceHubPaymentService.GetProviderAdapter` throws `PAYMENT_PROVIDER_ADAPTER_NOT_REGISTERED` when no adapter is registered. `CommerceHubPaymentService.NormalizeProvider` rejects non-Stripe/non-Paymob online provider names.

## Adapter Architecture

The canonical adapter contract is `IPaymentProviderAdapter`. It defines provider code, capabilities, session creation, webhook parsing, status lookup, and optional callback parsing. Stripe and Paymob implement this interface as isolated provider adapters.

`StripeAdapter` owns Stripe-specific PaymentIntent creation, idempotency key propagation, Stripe request options, response validation, client secret extraction, webhook parsing, and Stripe exception translation. It returns a canonical `PaymentProviderSessionResult`.

`PaymobAdapter` owns Paymob authentication, order registration, payment-key creation, iframe URL construction, HMAC/webhook parsing, status/callback mapping, telemetry, and provider error translation. It marks some infrastructure failures as `OutcomeUnknown` with a provider intent id when Paymob accepted an order before payment-key creation completed.

The dependency inversion boundary is implemented: orchestration depends on `IPaymentProviderAdapter` and normalized `ProviderCode`, not concrete Stripe/Paymob classes. Provider replacement is supported by registering another adapter for a provider code. Provider isolation is also implemented through provider-specific runtime bridges for Fawry and ManualWallet application services.

## Runtime State

`PaymentIntent` is the local payment runtime authority for storefront payment intent/session state. It stores tenant id, order id, amount, currency, payment method, provider, status, reference code, expiration, provider intent id, provider payment id, payment reference, redirect URL, encrypted client secret, idempotency key, lifecycle timestamps, failure fields, provider metadata, webhook tracking, manual wallet evidence fields, and inventory commitment state.

Implemented `PaymentIntentStatus` values are `Pending`, `Authorized`, `Paid`, `Failed`, `Cancelled`, `Expired`, `Skipped`, `AwaitingVerification`, `Rejected`, `AmountMismatch`, and `Suspicious`.

Pending state is created for external provider and manual wallet intents. COD uses `Skipped`. ManualWallet moves from `Pending` to `AwaitingVerification` through payment review admission. Success state is `Paid` through payment lifecycle handling. Failure-like terminal states include `Failed`, `Cancelled`, `Expired`, `Rejected`, `AmountMismatch`, and `Suspicious`.

Retry state is not a separate `PaymentIntentStatus`. Retry/recovery is represented by idempotency keys, existing active intent lookup, provider checkpoint state for Fawry, reusable/incomplete provider session state in Stripe/Paymob orchestration, recovery hosted services, outbox attempt counts, and reconciliation-required failure reasons.

Cancellation state exists as `PaymentIntentStatus.Cancelled` and is applied through `IPaymentLifecycleService` from the cancellation outbox path. Checkout initiation does not directly own cancellation mutation.

## DTO Certification

Runtime DTOs are backend-owned command/result shapes:

- `CanonicalPaymentRequest` and `CanonicalPaymentSession` model provider-neutral payment session creation.
- `PaymentProviderSessionRequest` and `PaymentProviderSessionResult` model adapter execution input/output.
- `CommerceHubPaymentInitiateRequestDto` and `CommerceHubPaymentInitiateResponseDto` model Stripe/Paymob initiation.
- `CreatePaymentIntentRequestDto` and `PaymentIntentResponseDto` model Fawry/COD/ManualWallet intent creation.
- `CheckoutResponseDto` projects checkout-visible runtime results.

Trusted values are server-side order total, currency, order id, tenant id, order number, payment method document, payment intent id, provider identity selected by backend mapping, payment reference generated by backend, provider returned intent/reference/token/URL, tenant payment method configuration, and durable local proof file key validation.

Ignored or non-authoritative browser values include order amount, currency, provider totals, merchant wallet number, checkout totals, and order identity. ManualWallet `WalletType`, `WalletPhoneNumber`, and `ProofFileKey` are accepted only as constrained inputs; merchant wallet instructions/number are derived from tenant payment settings and proof file key must normalize to trusted private tenant storage.

`CheckoutResponseDto` uses order totals and payment status from backend order/payment state. Stripe client secret, Paymob iframe URL/payment key, Fawry reference code, expiration, and manual wallet fields are populated from backend runtime/session results and server configuration.

## Error Handling

Provider failures are handled by adapter exception translation and orchestration mapping. Stripe exceptions become `PaymentProviderAdapterException`. Paymob adapter failures become canonical provider errors. `CommerceHubPaymentService.MapProviderAdapterException` maps provider error types to checkout exception status codes.

Network failures and provider timeouts in Stripe/Paymob initiation do not silently retry another provider session. `HttpRequestException` and non-caller `TaskCanceledException` are recorded as unknown provider outcomes and return reconciliation-required failure semantics.

Duplicate execution is guarded by order row locks, serializable transactions, active intent lookup, active intent unique index handling, idempotency-key replay, payment locks for Stripe/Paymob initiation, provider session reuse, and unknown-outcome blocking.

Recovery is implemented by `CheckoutPaymentIntentRecoveryHostedService`, `CheckoutPaymentIntentRecoveryService`, `StorefrontUnresolvedPaymentReconciliationHostedService`, Fawry checkpoint finalization, provider session reuse/recovery, and payment cancellation outbox retry processing.

Compensation is not owned by payment runtime initiation except for failure-state persistence and recovery blocking. Order admission pre-commit compensation is owned by checkout/order admission. Payment cancellation lifecycle work is delegated to `IPaymentLifecycleService`. Settlement/accounting compensation is outside this B4 boundary.

## Runtime Boundary

Payment Runtime Orchestration ends when checkout has either:

- Created/reused a provider payment session and mapped it to `CheckoutResponseDto`; or
- Created COD skipped/no-gateway payment evidence and confirmed COD order state through `PaymentService`; or
- Admitted ManualWallet for review and returned manual payment session/reference evidence; or
- Failed closed with a payment/runtime exception that prevents checkout from returning a successful payment session.

Payment Runtime Orchestration does not own settlement, refunds, webhook processing certification, accounting, CommerceHub marketplace business workflows, Marketplace settlement, fulfillment, product pricing, order admission, checkout validation, shipping, or inventory authority. The inspected source contains webhook, refund, reconciliation, accounting, and marketplace paths, but those are outside this B4 initiation orchestration certification.

## Critical Invariants

- Provider execution starts only after order admission has committed in the normal checkout path.
- A local `PaymentIntent` authority row is created before irreversible Fawry provider mutation.
- Transactional payment-intent seeding during order admission does not call provider APIs.
- Checkout runtime uses server-side order amount and currency; provider adapters do not recalculate checkout pricing.
- Provider adapters cannot mutate order totals through the canonical adapter contract.
- Stripe/Paymob adapter selection is by normalized provider code from backend runtime routing.
- Unsupported online providers fail closed.
- ManualWallet merchant wallet details are derived from tenant payment configuration, not browser input.
- ManualWallet proof must be a trusted tenant-scoped private upload key.
- COD has no gateway execution and rejects COD webhook processing in `PaymentService`.
- Duplicate active payment intent creation is rejected.
- Unknown provider session outcomes are recorded and block competing provider session creation until recovery/reconciliation.
- Cancellation replay is handled by a durable outbox and lifecycle service, not direct ad hoc mutation.
- Runtime response fields are projected from backend order/payment/provider results, not trusted browser totals.

## Architecture Boundaries

Payment Runtime Orchestration owns:

- Runtime coordination after committed order admission.
- Payment provider selection for payment-session creation.
- Adapter/runtime application service invocation.
- Runtime request building from backend order/payment facts.
- Provider session result mapping.
- Local payment intent/session persistence needed for initiation.
- Idempotent replay and recovery of incomplete payment-session initiation.
- Runtime-facing checkout response projection.

Payment Runtime Orchestration does not own:

- Checkout validation.
- Order admission.
- Pricing recalculation.
- Inventory authority.
- Coupon or promotion authority.
- Settlement.
- Refunds.
- Webhook processing certification.
- Accounting.
- Fulfillment.
- Marketplace.
- CommerceHub business-domain certification.
- Provider SDK internals.

## Architecture Risks

No architectural violations were identified.

## Certification Checklist

| Area | Result | Implementation-backed conclusion |
| --- | --- | --- |
| Runtime Entry | PASS | Runtime starts from committed checkout order or committed-order recovery. |
| Runtime Pipeline | PASS | Implemented pipeline routes order/payment facts to provider runtime and maps results to checkout response. |
| Orchestration | PASS | Checkout, orchestration facade/runtime, and payment services own coordination by provider path. |
| Provider Resolution | PASS | Provider mapping and unsupported-provider fail-closed behavior are implemented. |
| Adapter Architecture | PASS | Stripe/Paymob use provider-neutral adapter interfaces; Fawry/ManualWallet use provider-specific runtime application boundaries. |
| Runtime State | PASS | `PaymentIntent` lifecycle and provider/session fields are implemented. |
| DTO Safety | PASS | Runtime response values are backend/provider-derived; sensitive/manual values are constrained or server-derived. |
| Error Handling | PASS | Duplicate execution, provider failures, unknown outcomes, cancellation, and recovery paths are implemented. |
| Runtime Boundary | PASS | Initiation boundary is separate from settlement, refunds, accounting, fulfillment, and webhook certification. |
| Critical Invariants | PASS | Provider execution after committed order, durable intent before Fawry provider mutation, no pricing recalculation in runtime. |
| Architecture Boundaries | PASS | Runtime ownership and non-ownership are implemented by service boundaries and DI. |
| Overall Certification | PASS WITH OBSERVATIONS | Split provider-family orchestration is implemented behavior; no violations identified. |


# PAYMOB CONVERGENCE 1

Date: 2026-07-13

Scope: POS Admin and Storefront Paymob execution paths

Excluded: CommerceHub Paymob payment and refund execution

## Executive Summary

Paymob POS Admin and Storefront execution has been converged onto the certified Runtime Application architecture without modifying the frozen Payment Runtime, Runtime Bridge, Provider Adapter, Webhook Adapter, controllers, DTOs, frontend, or business rules.

The remaining duplicate Paymob execution surface in `PaymobPaymentService` was dormant direct HTTP provider execution and request construction. It has been removed. Storefront creation, retry, checkout recovery, and payment-intent rehydration now have no alternate Paymob HTTP path in `PaymobPaymentService`; they delegate through `IPaymobRuntimeApplicationService`.

## Execution Map

| Entry point | Production route | Runtime path | Status |
| --- | --- | --- | --- |
| Storefront payment creation | `StorefrontPaymobPaymentsController` -> `IPaymobPaymentService.CreatePaymentIntentAsync` -> `PaymobPaymentService` | `PaymobRuntimeApplicationService` -> `PaymobRuntimeBridge` -> `PaymobPaymentAdapter` -> `PaymentRuntimeOrchestrator` -> DTO mapping | Converged |
| Checkout Paymob intent creation | `CheckoutService` delegates to `IPaymobPaymentService` | Same Storefront create-payment path | Converged |
| Existing pending intent reuse | `PaymobPaymentService` reads persisted SQL authority and returns stored iframe/payment key | No provider execution; SQL authority only | Converged |
| Incomplete pending intent rehydration | `PaymobPaymentService.CreatePaymobProviderIntentAsync` | `IPaymobRuntimeApplicationService.CreatePaymentIntentAsync` | Converged |
| Concurrent checkout recovery | `PaymobPaymentService` reloads local claim or rejects in-progress state | Complete claim reuses SQL; incomplete claim re-enters Runtime Application boundary | Converged |
| Background checkout payment-intent recovery | `CheckoutPaymentIntentRecoveryHostedService` -> `CheckoutPaymentIntentRecoveryService` -> `IPaymobPaymentService` | Same canonical Paymob create/rehydrate path | Converged |
| Retry flow | Storefront retry endpoint calls `IPaymobPaymentService.CreatePaymentIntentAsync` | Same canonical Paymob create/rehydrate path with idempotency guard | Converged |
| Status polling | `StorefrontPaymobPaymentsController` -> `IPaymobPaymentService.GetPaymentStatusAsync` | SQL read only; no Paymob HTTP or Runtime execution | Converged |
| Redirect callback recovery | `PaymobWebhooksController` -> `IPaymobPaymentService.ValidateCallbackAsync` | HMAC verification and stored routing-context validation only; frontend polls SQL status | Converged as non-execution evidence path |
| Webhook admission | `PaymobWebhooksController` -> `IPaymobPaymentService.ProcessWebhookAsync` | `PaymobWebhookRuntimeIntegrationService` -> `PaymobWebhookEvidenceAdapter` -> `PaymentRuntimeOrchestrator` -> `PaymentLifecycleService` | Already converged by `PAYMOB_CONTRACT_CLOSURE_2` |
| Webhook replay | `PaymobWebhookRuntimeIntegrationService` loads replay/prior evidence | Replay store -> adapter rejected/admitted evidence -> Runtime -> replay persistence | Already converged |
| Refund webhook evidence | Paymob terminal refund callback | `PaymobWebhookEvidenceAdapter` -> `PaymentRuntimeOrchestrator` -> `PaymentLifecycleService.RecordRefundAsync` | Already converged for webhook-driven refunds |
| Capture, command refund, cancel, retrieve, partial refund | `PaymobAdvancedOperationsAdapter` | Certified adapter translation only; no POS Admin/Storefront production command caller found | Pending production entry point |

## Legacy Audit

| Artifact or path | Legacy? | Already converged? | Modified? | Still remaining? | Decision |
| --- | --- | --- | --- | --- | --- |
| `PaymobPaymentService` direct `IHttpClientFactory.CreateClient("Paymob")` execution | Yes | No | Yes | No | Removed dormant duplicate provider HTTP path |
| `PaymobPaymentService.AuthenticatePaymobAsync` | Yes | No | Yes | No | Removed |
| `PaymobPaymentService.CreatePaymobOrderAsync` | Yes | No | Yes | No | Removed |
| `PaymobPaymentService.CreatePaymentKeyAsync` | Yes | No | Yes | No | Removed |
| `PaymobPaymentService` direct Paymob endpoint constants | Yes | No | Yes | No | Removed |
| Storefront create/retry route | No after convergence | Yes | No controller change | No | Delegates to `IPaymobPaymentService`, then Runtime Application boundary |
| Checkout recovery service | No after convergence | Yes | No | No | Delegates to `IPaymobPaymentService`, then Runtime Application boundary |
| Webhook integration service | No | Yes | No | No | Certified in `PAYMOB_CONTRACT_CLOSURE_2` |
| Redirect callback validation | No | Not provider execution | No | No | HMAC/routing validation only; no lifecycle finality |
| `PaymobPaymentAdapter` | No | Yes | No | No | Active certified provider adapter |
| `PaymobRuntimeApplicationService` | No | Yes | No | No | Active application execution boundary |
| `PaymobRuntimeBridge` | No | Yes | No | No | Active certified bridge |
| `PaymobAdvancedOperationsAdapter` | No active POS path | Adapter certified | No | Pending production command wiring | Out of active execution scope |
| CommerceHub Paymob payment/refund services | Yes | No | No | Yes | Out of mission scope, matching Stripe CommerceHub exclusion |

## Convergence Matrix

| Rule | Certification |
| --- | --- |
| One Paymob create-payment execution pipeline | Certified |
| One Paymob recovery and retry execution pipeline | Certified |
| No direct Paymob HTTP execution in `PaymobPaymentService` | Certified |
| Paymob provider HTTP isolated to certified provider adapter | Certified |
| One Paymob webhook admission pipeline | Certified |
| One Runtime authority | Certified |
| One SQL lifecycle writer after Runtime result | Certified for POS Admin/Storefront |
| Provider-neutral Runtime preserved | Certified |
| Controller-to-runtime isolation | Certified |
| Controller-to-provider HTTP isolation | Certified |
| Advanced operations through production command path | Not applicable until POS Admin/Storefront command entry points exist |
| CommerceHub Paymob convergence | Out of scope for this mission; pending |

## Architecture Rules

1. POS Admin and Storefront Paymob controllers must delegate to `IPaymobPaymentService`.
2. Paymob create, retry, rehydration, and background recovery execution must enter provider execution through `IPaymobRuntimeApplicationService`.
3. `PaymobRuntimeApplicationService` is the only Paymob application execution boundary for create-payment provider execution.
4. `PaymobRuntimeBridge` remains the only create-payment bridge into the Runtime orchestrator.
5. Direct Paymob HTTP execution must not exist in `PaymobPaymentService`.
6. Paymob provider HTTP remains isolated to the certified provider adapter.
7. Paymob webhook lifecycle mutation must occur only after replay/outcome admission and Runtime evidence execution.
8. Runtime remains storage-unaware, credential-unaware, provider-neutral, and independent of Paymob wire contracts.
9. `PaymentLifecycleService` remains the single durable lifecycle writer.

## Stripe Delta

Stripe and Paymob now share the same POS Admin and Storefront convergence shape:

```text
Controller
  -> Provider payment service
  -> Provider Runtime Application Service
  -> Provider Runtime Bridge
  -> Provider Adapter
  -> ProviderEvidence
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
  -> Existing DTO / lifecycle writer
```

Protocol differences remain provider-specific: Stripe creates/reuses PaymentIntents and verifies official Stripe webhook signatures; Paymob creates redirect/payment-key sessions and verifies Paymob HMAC callback fields. These differences terminate inside provider application/adapter boundaries and do not modify Runtime.

## Certification Decision

PAYMOB-CONVERGENCE-1 is certified for POS Admin and Storefront Paymob create-payment, retry, rehydration, background recovery, redirect callback validation, status read, webhook admission, webhook replay, and webhook-driven refund reconciliation.

No Runtime modification was made or authorized. No `.ai-context` file was modified because no new permanent cross-provider architecture truth was discovered.

## Success Answers

| Question | Answer |
| --- | --- |
| Execution Map completed? | Yes. |
| Canonical Pipeline unified? | Yes. |
| Duplicate execution removed? | Yes, dormant direct Paymob HTTP execution was removed from `PaymobPaymentService`. |
| Webhook convergence certified? | Yes, through `PAYMOB_CONTRACT_CLOSURE_2` and retained source guards. |
| Advanced operations converged? | Adapter layer certified; no POS Admin/Storefront production command entry points were found. |
| Legacy audit completed? | Yes. |
| Single Writer preserved? | Yes. |
| Runtime Independence preserved? | Yes. |
| Architecture boundaries preserved? | Yes. |
| Convergence certified? | Yes, for POS Admin and Storefront Paymob. |
| Recommend next phase? | Yes: CommerceHub Paymob convergence and production advanced-operation command wiring when command entry points are introduced. |

# STRIPE CONVERGENCE 1

## Scope

This certification covers Stripe execution inside the POS Admin and Storefront bounded context.

Payment Runtime, Stripe adapters, provider contracts, policy engine, and business rules are treated as frozen. This pass only removes duplicated production execution paths and documents remaining legacy surface.

## Execution Map

| Entry point | Production route | Runtime path | Status |
| --- | --- | --- | --- |
| Storefront create payment | `StorefrontStripePaymentsController` -> `IStripePaymentService.CreatePaymentIntentAsync` -> `StripePaymentService` | `StripeRuntimeApplicationService` -> `StripeRuntimeBridge` -> `PaymentRuntimeOrchestrator` -> application persistence | Converged |
| Checkout Stripe intent creation | `CheckoutService` delegates to `IStripePaymentService` | Same Storefront create-payment path | Converged |
| Existing pending intent reuse | `StripePaymentService` reads persisted SQL authority and returns protected client secret | No provider execution; SQL authority only | Converged |
| Missing provider intent recovery | `StripePaymentService.RehydrateStripeProviderIntentAsync` | `StripeRuntimeApplicationService` -> `StripeRuntimeBridge` -> `PaymentRuntimeOrchestrator` -> persistence retry | Converged |
| Missing client secret recovery | `StripePaymentService.RehydrateStripeProviderIntentAsync` | Same canonical create/rehydrate path with persisted idempotency | Converged |
| Concurrent checkout recovery | `StripePaymentService` reloads local claim, rehydrates through application Runtime boundary, then persists recovered client secret | Same canonical create/rehydrate path | Converged |
| Redirect URL background recovery | `RedirectUrlRecoveryHostedService` resolves `IStripeRuntimeApplicationService` from DI | `StripeRuntimeApplicationService` -> `StripeRuntimeBridge` -> `PaymentRuntimeOrchestrator` -> background persistence | Converged |
| Status polling | `StripePaymentService.GetPaymentIntentStatusAsync` | SQL read only; no Stripe SDK or Runtime execution | Converged |
| Stripe webhook | `StripeWebhooksController` -> `IStripePaymentService.ProcessWebhookAsync` | Replay/admission/evidence adapter before lifecycle persistence | Converged |
| Webhook replay | `StripeWebhookRuntimeIntegrationService` loads prior evidence and replay state | Replay store -> outcome admission -> adapter/runtime -> replay persistence | Converged |
| Refund webhook evidence | Stripe webhook terminal refund evidence | `StripeWebhookEvidenceAdapter` -> `PaymentRuntimeOrchestrator` -> `PaymentLifecycleService.RecordRefundAsync` | Converged for webhook-driven refunds |
| Capture | `StripeAdvancedOperationsAdapter` | Certified adapter translation only; no POS Admin/Storefront production caller found | Pending production entry point |
| Refund command | `StripeAdvancedOperationsAdapter` | Certified adapter translation only; webhook persistence path converged | Pending production command entry point |
| Cancel | `StripeAdvancedOperationsAdapter` | Certified adapter translation only; no POS Admin/Storefront production caller found | Pending production entry point |
| Retrieve | `StripeAdvancedOperationsAdapter` | Certified adapter translation only; no POS Admin/Storefront production caller found | Pending production entry point |
| Partial refund command | `StripeAdvancedOperationsAdapter` | Certified adapter translation only; no POS Admin/Storefront production caller found | Pending production command entry point |

## Canonical Pipeline

All POS Admin and Storefront Stripe provider execution that creates or rehydrates a PaymentIntent now uses:

```text
Application Layer
  -> StripeRuntimeApplicationService
  -> StripeRuntimeBridge
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
  -> Application Persistence
```

The application layer owns tenant configuration, storefront URL, local payment intent identity, DTO mapping, encrypted client secret persistence, and SQL state mutation. The Runtime remains provider-neutral and storage-unaware.

## Webhook Pipeline

Stripe webhook execution now uses one admission path:

```text
Webhook Controller
  -> StripePaymentService.ProcessWebhookAsync
  -> Application replay and prior evidence loading
  -> StripeWebhookOutcomeAdmissionService
  -> StripeWebhookEvidenceAdapter
  -> PaymentRuntimeOrchestrator
  -> PaymentLifecycleService / application persistence
  -> Replay record persistence
```

The controller does not reference the Stripe SDK directly. `StripePaymentService` performs the initial signature parse needed to identify the event and tenant configuration, then the admitted event is submitted to the canonical webhook evidence adapter. Lifecycle mutation occurs after runtime admission.

## Dependency Graph

```text
Controllers
  -> IStripePaymentService
  -> StripePaymentService
  -> IStripeRuntimeApplicationService / IStripeWebhookRuntimeIntegrationService
  -> IStripeRuntimeBridge / IStripeWebhookEvidenceAdapter
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
  -> PaymentLifecycleService / ApplicationDbContext persistence

Stripe SDK
  -> StripePaymentIntentAdapter
  -> StripeAdvancedOperationsAdapter
  -> StripeWebhookEvidenceAdapter
  -> StripeRuntimeApplicationService credential validation only
```

No POS Admin or Storefront controller directly references the Stripe SDK. No POS Admin or Storefront business service now creates or retrieves Stripe PaymentIntents directly.

## Duplicate Execution Removed

| Former duplicate | Replacement | Result |
| --- | --- | --- |
| `StripePaymentService` direct `PaymentIntentService.GetAsync` for client-secret recovery | `RehydrateStripeProviderIntentAsync` -> `StripeRuntimeApplicationService.CreatePaymentIntentAsync` | Removed |
| `StripePaymentService` direct `PaymentIntentService.CreateAsync` for missing provider fields | Same canonical rehydrate/create path | Removed |
| `StripePaymentService` direct `PaymentIntentService.GetAsync` for concurrent claim recovery | Same canonical rehydrate/create path | Removed |
| `RedirectUrlRecoveryHostedService` direct `PaymentIntentService.GetAsync` | `IStripeRuntimeApplicationService.CreatePaymentIntentAsync` | Removed |
| Parallel webhook lifecycle before runtime admission | Runtime admission before lifecycle persistence | Removed |

## Legacy Audit

| Artifact | Classification | Decision |
| --- | --- | --- |
| `StripePaymentService` direct PaymentIntent SDK recovery | Deprecated duplicate | Removed in this convergence pass |
| `RedirectUrlRecoveryHostedService` direct PaymentIntent SDK recovery | Deprecated duplicate | Removed in this convergence pass |
| `StripePaymentIntentAdapter` | Active canonical adapter | Keep |
| `StripeRuntimeApplicationService` | Active application boundary | Keep |
| `StripeRuntimeBridge` | Active canonical runtime bridge | Keep |
| `StripeWebhookRuntimeIntegrationService` | Active webhook admission integration | Keep |
| `StripeWebhookEvidenceAdapter` | Active canonical webhook adapter | Keep |
| `StripeAdvancedOperationsAdapter` | Active certified adapter without POS production caller | Keep; pending production operation entry points |
| `StripeFoundationServiceCollectionExtensions` / `StripeFoundationService` | Foundation registration helper | Keep unless superseded by composition root cleanup |
| `CommerceHubPaymentService` direct Stripe SDK calls | Active outside this bounded context | Pending CommerceHub payment convergence |
| `CommerceHubRefundGatewayService` direct Stripe refund SDK calls | Active outside this bounded context | Pending CommerceHub refund convergence |

## Convergence Matrix

| Rule | Certification |
| --- | --- |
| One Stripe create-payment execution pipeline | Certified |
| One Stripe recovery execution pipeline | Certified |
| One Stripe webhook admission pipeline | Certified |
| One Runtime authority | Certified |
| One SQL writer after Runtime result | Certified for POS Admin/Storefront |
| Provider-neutral Runtime preserved | Certified |
| Controller-to-SDK isolation | Certified |
| Business-service-to-SDK isolation | Certified for POS Admin/Storefront |
| Capture/refund/cancel/retrieve/partial refund through one runtime-coordinated production command path | Not applicable until POS production command entry points exist |
| CommerceHub Stripe execution convergence | Out of scope for this mission; pending |

## Architecture Rules

1. POS Admin and Storefront controllers must delegate Stripe work to the application layer.
2. POS Admin and Storefront create/recovery execution must enter through `StripeRuntimeApplicationService`.
3. `StripeRuntimeBridge` is the only create-payment bridge into `PaymentRuntimeOrchestrator`.
4. Stripe webhook lifecycle mutation must occur only after replay/outcome admission and runtime evidence execution.
5. Runtime remains storage-unaware, credential-unaware, provider-neutral, and independent of Stripe SDK types.
6. Application persistence remains the only SQL writer after Runtime result interpretation.
7. Stripe SDK calls remain isolated to certified adapters or explicitly documented external legacy contexts.

## Certification Decision

STRIPE-CONVERGENCE-1 is certified for POS Admin and Storefront create-payment, recovery, retry/reuse, background recovery, status-read, and webhook execution convergence.

Advanced operation adapters are certified as adapters, but no POS Admin or Storefront production command entry points for capture, cancel, retrieve, or partial refund were found during this pass. Those operations therefore have no alternate POS execution path to remove in this bounded context.

CommerceHub Stripe payment/refund execution still contains direct Stripe SDK usage and remains an active legacy area outside this mission scope.

## Success Answers

| Question | Answer |
| --- | --- |
| Execution Map completed? | Yes. |
| Canonical Pipeline unified? | Yes, for POS Admin and Storefront create/recovery execution. |
| Duplicate execution removed? | Yes. |
| Webhook convergence certified? | Yes. |
| Advanced operations converged? | Adapter layer certified; POS Admin/Storefront production command entry points were not found. |
| Legacy audit completed? | Yes. |
| Single Writer preserved? | Yes. |
| Runtime Independence preserved? | Yes. |
| Architecture boundaries preserved? | Yes. |
| Convergence certified? | Yes, for the requested bounded context. |
| Recommend next phase? | Yes: CommerceHub Stripe convergence and production advanced-operation command wiring. |

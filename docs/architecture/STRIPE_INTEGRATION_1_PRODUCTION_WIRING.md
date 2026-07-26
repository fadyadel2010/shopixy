# STRIPE-INTEGRATION-1 Production Wiring

Date: 2026-07-12

Scope: Production execution wiring review for live Stripe payment operations against the certified Stripe Runtime Bridge and frozen Payment Runtime.

Status: **PRODUCTION WIRING CERTIFICATION WITHHELD**

## Certification Decision

Production wiring cannot be certified in the current workspace without modifying frozen Stripe adapter behavior or changing the existing public payment response contract.

The certified canonical path exists:

```text
Application Service
  -> StripeRuntimeBridge
  -> StripePaymentIntentAdapter
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
  -> Application Response
```

The live production path still exists separately:

```text
Controller / Checkout / Recovery
  -> IStripePaymentService
  -> StripePaymentService
  -> direct Stripe SDK calls
  -> SQL payment lifecycle service
  -> existing DTO response
```

This review therefore records the execution map and the exact blockers. It does not alter Payment Runtime, Stripe adapters, provider contracts, or policy engine.

## Legacy Flow

Storefront PaymentIntent creation:

```text
StorefrontStripePaymentsController.CreatePaymentIntent
  -> IPaymentRequestAuthorizationService
  -> IStripePaymentService.CreatePaymentIntentAsync
  -> StripePaymentService
  -> PaymentIntentService.CreateAsync
  -> PaymentIntents SQL row / PaymentIntentCreatedEvent
  -> StripePaymentIntentResponseDto
```

Storefront payment status:

```text
StorefrontStripePaymentsController.GetPaymentStatus
  -> IStripePaymentService.GetPaymentIntentStatusAsync
  -> SQL PaymentIntent / Order state
  -> StripePaymentStatusDto
```

Stripe webhook:

```text
StripeWebhooksController.HandleWebhook
  -> IPaymentWebhookRequestPipeline
  -> IStripePaymentService.ProcessWebhookAsync
  -> EventUtility.ConstructEvent
  -> PaymentWebhookLogs SQL rows
  -> IPaymentLifecycleService
  -> StripeWebhookResultDto
```

Checkout recovery:

```text
CheckoutPaymentIntentRecoveryHostedService
  -> CheckoutPaymentIntentRecoveryService
  -> IStripePaymentService.CreatePaymentIntentAsync
  -> StripePaymentService
  -> PaymentIntentService.CreateAsync / GetAsync
```

Redirect/client-secret recovery:

```text
RedirectUrlRecoveryHostedService
  -> PaymentIntentService.GetAsync
  -> PaymentIntent.ClientSecret
  -> PaymentIntents.ClientSecretEncrypted
```

CommerceHub payment:

```text
CommerceHubPaymentService
  -> PaymentIntentService.CreateAsync / GetAsync
  -> EventUtility.ConstructEvent
  -> CommerceHub payment SQL authority
```

CommerceHub refund:

```text
CommerceHubRefundGatewayService
  -> RefundService.CreateAsync / GetAsync
  -> CommerceHub refund SQL authority
```

## Canonical Flow

The target production flow is:

```text
Controller
  -> Application abstraction
  -> StripeRuntimeBridge
  -> Stripe adapter
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
  -> Existing application response
```

Webhook target:

```text
StripeWebhooksController
  -> Application webhook abstraction
  -> StripeWebhookEvidenceAdapter
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
  -> Existing webhook response
```

Advanced operation target:

```text
Application refund/capture/cancel/retrieve service
  -> StripeAdvancedOperationsAdapter
  -> Runtime bridge/coordination boundary
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
  -> Existing application response
```

## Execution Map

| Area | Current entry point | Current execution authority | Canonical target | Wiring status |
|---|---|---|---|---|
| Storefront create intent | `StorefrontStripePaymentsController.CreatePaymentIntent` | `StripePaymentService` plus direct `PaymentIntentService.CreateAsync` | `StripeRuntimeBridge` | Not wired |
| Storefront status | `StorefrontStripePaymentsController.GetPaymentStatus` | SQL `PaymentIntent` read | Runtime-backed application status projection | Not wired |
| Stripe webhook | `StripeWebhooksController.HandleWebhook` | `StripePaymentService.ProcessWebhookAsync` plus direct `EventUtility.ConstructEvent` | `StripeWebhookEvidenceAdapter` | Not wired |
| Checkout recovery | `CheckoutPaymentIntentRecoveryService` | `IStripePaymentService` | Application abstraction using bridge | Not wired |
| Client-secret recovery | `RedirectUrlRecoveryHostedService` | direct `PaymentIntentService.GetAsync` | Adapter/bridge-compatible retrieval path | Not wired |
| CommerceHub payment intake | `CommerceHubPaymentService` | direct `PaymentIntentService` and direct webhook parse | Runtime bridge / webhook adapter | Not wired |
| CommerceHub refund execution | `CommerceHubRefundGatewayService` | direct `RefundService` | `StripeAdvancedOperationsAdapter` plus Runtime coordination | Not wired |

## Routing Matrix

| Operation | Required canonical route | Current route | Certification |
|---|---|---|---|
| PaymentIntent create | `StripeRuntimeBridge -> PaymentRuntimeOrchestrator` | direct SDK inside `StripePaymentService` | Withheld |
| Webhook evidence | `StripeWebhookEvidenceAdapter -> PaymentRuntimeOrchestrator` | direct webhook parsing and SQL lifecycle | Withheld |
| Refund | `StripeAdvancedOperationsAdapter -> Runtime coordination` | direct `RefundService` in CommerceHub refund gateway | Withheld |
| Partial refund | `StripeAdvancedOperationsAdapter -> Runtime coordination` | direct `RefundService` path | Withheld |
| Capture | `StripeAdvancedOperationsAdapter -> Runtime coordination` | no certified production route found | Withheld |
| Cancel | `StripeAdvancedOperationsAdapter -> Runtime coordination` | legacy status/lifecycle paths only | Withheld |
| Retrieve PaymentIntent | `StripeAdvancedOperationsAdapter -> Runtime coordination` | direct `PaymentIntentService.GetAsync` in recovery paths | Withheld |
| Retrieve Charge | `StripeAdvancedOperationsAdapter -> Runtime coordination` | no certified production route found | Withheld |

## Dependency Graph

Current live dependency graph:

```text
Controllers / hosted services / CommerceHub services
  -> IStripePaymentService or direct Stripe SDK
  -> StripePaymentService / direct Stripe SDK service
  -> SQL payment lifecycle / CommerceHub payment authority
```

Certified but inactive graph:

```text
StripeRuntimeBridge
  -> IStripePaymentIntentAdapter
  -> IStripePaymentIntentClient
  -> official Stripe SDK

StripeWebhookEvidenceAdapter
  -> PaymentRuntimeOrchestrator

StripeAdvancedOperationsAdapter
  -> IStripeAdvancedOperationsClient
  -> official Stripe SDK
```

Required graph:

```text
Controllers
  -> application abstractions
  -> Stripe runtime application wiring
  -> StripeRuntimeBridge / StripeWebhookEvidenceAdapter / StripeAdvancedOperationsAdapter
  -> PaymentRuntimeOrchestrator
```

## Failure Routing

Certified adapter failure routing exists:

```text
StripeException
  -> CanonicalProviderError
  -> provider-error ProviderEvidence
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
```

Current production failure routing still allows legacy boundaries:

```text
StripeException
  -> StripeApiException / StripeWebhookSignatureException / provider-specific DTO result
  -> controller response or recovery retry
```

No evidence was found that `StripeException` escapes directly to controllers, but canonical `CanonicalProviderError -> RuntimeResult -> application response` is not the single production route.

## Single Writer Verification

The current SQL lifecycle writer remains active:

- `IPaymentLifecycleService` is invoked from legacy Stripe webhook handling.
- `StripePaymentService` writes `PaymentIntents`, webhook logs, provider references, encrypted client secrets, and payment events.
- CommerceHub payment and refund services maintain separate SQL authority flows for marketplace payments and refunds.

The Payment Runtime is not yet the single writer or single execution authority for Stripe outcomes. It is registered and tested but not activated in live application flow.

## Backward Compatibility

Public contracts currently depend on data not exposed by the certified bridge result:

- `StripePaymentIntentResponseDto.ClientSecret`
- `StripePaymentIntentResponseDto.PublishableKey`
- `StripePaymentIntentResponseDto.StripePaymentIntentId`
- existing webhook DTO shape
- existing frontend Stripe.js confirmation flow

Because the certified adapter result exposes canonical evidence and `RuntimeResult`, but not `PaymentIntent.ClientSecret`, replacing the live `create-intent` route with the bridge would break the existing storefront contract unless a separately reviewed application mapping or adapter contract extension is introduced.

## Production Blockers

1. The certified PaymentIntent adapter does not expose `ClientSecret`, so the existing public `create-intent` response cannot be produced through the bridge alone.
2. The certified Stripe SDK client is registered from global `StripeOptions`, while live storefront Stripe execution uses tenant-scoped encrypted Stripe credentials from `TenantPaymentMethod`.
3. Webhook durable replay history is owned by the legacy SQL webhook log path and is not supplied to `StripeWebhookEvidenceAdapter` as canonical prior evidence.
4. Negative, pending, canceled, failed, unpaid, and disputed provider events still need safe outcome admission before Runtime approval can be treated as production payment truth.
5. Advanced operations have an adapter but no production runtime coordination boundary.
6. CommerceHub has independent direct Stripe payment and refund paths that are outside the storefront `IStripePaymentService` path.

## Backward-Compatible Wiring Requirement

A safe production wiring phase must first introduce an application-owned Stripe runtime integration boundary that can:

- Build canonical runtime `PaymentSession`, `PaymentAttempt`, `PaymentAction`, and prior-history inputs from current SQL authority.
- Invoke the certified bridge/adapters without direct SDK calls in application services.
- Preserve tenant-scoped Stripe credential selection without changing Runtime.
- Preserve `clientSecret` response behavior through a certified adapter/application contract.
- Persist runtime artifacts and legacy SQL lifecycle outcomes through one writer, not two.
- Load durable webhook prior evidence before runtime execution.
- Map `CanonicalProviderError` and `RuntimeResult` to existing DTOs without leaking Stripe exceptions.

## Final Certification

| Question | Answer |
|---|---|
| Execution Map completed? | Yes. |
| Legacy flow removed? | No. |
| Runtime Bridge activated? | No. Registered only; not live production route. |
| Controllers rewired? | No. Controllers still depend on `IStripePaymentService`. |
| Webhook routing certified? | No. Live webhook route does not delegate to `StripeWebhookEvidenceAdapter`. |
| Advanced operations routed? | No. CommerceHub refund/retrieve paths still use direct Stripe SDK services. |
| Single Writer preserved? | No for the target runtime model. Legacy SQL lifecycle remains the live writer. |
| Backward compatible? | Current live behavior remains backward compatible because no cutover was made. A bridge-only cutover is not backward compatible yet. |
| Production wiring certified? | No. Certification withheld. |
| Recommend next phase? | Yes. Create a narrow STRIPE-INTEGRATION-1A contract-closure phase for client secret exposure, tenant credential selection, durable prior evidence loading, and one-writer persistence before attempting controller cutover. |


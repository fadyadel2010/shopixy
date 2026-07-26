# FAWRY-CONVERGENCE-1 Runtime Convergence Certification

Date: 2026-07-15
Scope: Fawry POS Admin + Storefront execution convergence
Governing architecture: `PAYMENT_REFERENCE_FREEZE.md`
Runtime posture: Payment Runtime remains certified, provider-neutral, and permanently frozen

## Decision

Fawry POS Admin + Storefront execution is converged onto the certified application-owned execution path.

Application creation now routes through:

```text
PaymentService
  -> IFawryRuntimeApplicationService
  -> IFawryRuntimeBridge
  -> IFawryPaymentAdapter
  -> PaymentRuntime
  -> RuntimeResult
  -> PaymentIntent projection
```

Webhook processing now routes through:

```text
FawryWebhookController
  -> IFawryWebhookRuntimeIntegrationService
  -> IFawryWebhookEvidenceAdapter
  -> PaymentRuntime
  -> FawryWebhookLifecycleAdmission
  -> PaymentLifecycleService
  -> FawryWebhookReplayRecords
```

## Legacy Path Removal

Removed legacy Fawry execution from the active POS Admin + Storefront paths:

- `PaymentService` no longer falls through to the generic provider adapter/fake hosted-checkout fallback for Fawry.
- `FawryWebhookController` no longer parses legacy Fawry DTO status into lifecycle outcomes.
- `FawryWebhookController` no longer performs its own Fawry HMAC success/failure decision.
- `FawryWebhookController` no longer constructs webhook `HandlePaymentResultRequest` directly from provider payload fields.

Provider HTTP remains only inside the certified provider adapter and translation-only advanced adapter. Webhook evidence verification remains inside the certified webhook evidence adapter. Runtime remains unchanged.

## Architecture Preservation

| Boundary | Decision |
|---|---|
| Payment Runtime | Preserved; no source changes. |
| Runtime Bridge | Preserved. |
| Provider Adapter | Preserved as protocol/translation only. |
| Webhook Evidence Adapter | Preserved as evidence admission only. |
| Advanced Adapter | Preserved as translation-only; not activated. |
| Controllers | HTTP/body/tenant/log response boundary only. |
| Public DTOs | Preserved. |
| Frontend | Preserved. |
| Business Rules | Preserved. |
| CommerceHub | Preserved and excluded. |
| Lifecycle Writer | Preserved as `PaymentLifecycleService`. |

## Certification Decision

| Question | Decision |
|---|---|
| Application execution converged? | Yes. |
| Webhook execution converged? | Yes. |
| Legacy execution removed? | Yes for POS Admin + Storefront Fawry. |
| Direct provider execution removed? | Yes outside certified adapters. |
| Certified execution path enforced? | Yes. |
| Architecture boundaries preserved? | Yes. |
| Runtime preserved? | Yes. |
| Backward compatibility preserved? | Yes; public DTOs and Fawry handoff fields remain. |
| Convergence certified? | Yes for POS Admin + Storefront. |

## Recommended Next Phase

Proceed to Fawry source operational readiness and sandbox validation. Do not activate advanced operations or CommerceHub Fawry flows without separate application-contract and convergence certification.

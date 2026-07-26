# STRIPE-PRODUCTION-2 Sandbox Validation

Date: 2026-07-13

Scope: POS Admin and Storefront Stripe payment pipeline validation against the certified Payment Runtime, Stripe adapters, application contracts, and STRIPE-PRODUCTION-1 operational readiness.

Status: **SANDBOX EXECUTION NOT CERTIFIED IN THIS WORKSPACE; SOURCE-LEVEL PRODUCTION VALIDATION COMPLETE**

## Certification Decision

This pass validated the production Stripe integration boundaries that can be proven from the repository without real Stripe sandbox credentials, a running API connected to tenant test configuration, and signed Stripe Workbench webhook delivery.

The frozen Payment Runtime, Stripe adapters, application contracts, business rules, and execution pipeline were not redesigned. No runtime code was changed.

Live Stripe sandbox execution is **not certified** by this report because this workspace exposes no Stripe sandbox environment variables, repository defaults keep Stripe disabled, and no real signed Stripe webhook delivery channel is available to execute the required end-to-end drills.

## Environment

| Item | Evidence | Decision |
| --- | --- | --- |
| Sandbox credentials | Repository defaults have `Stripe.Enabled=false`, empty `SecretKey`, empty `PublishableKey`, empty `WebhookSecret`, `Environment=Sandbox`, and `SandboxMode=true`. No Stripe environment variable names were present in this process. | Not present in workspace; must be supplied by deployment secret manager or local user secrets before live drill. |
| Webhook endpoint | `StripeWebhooksController` delegates to `IStripePaymentService.ProcessWebhookAsync`; operational health verifies `IStripePaymentService` registration. | Source-certified. |
| Webhook signing secret | Required by tenant Stripe configuration and startup validation; missing or invalid `whsec_` fails closed when enabled. | Source-certified; not live-verified. |
| API version | `appsettings.json` pins `2026-06-24.dahlia`; startup validation rejects drift from `StripeOptions.CertifiedApiVersion`. | Source-certified. |
| SDK version | `PointOfSale.API` references official `Stripe.net` `52.1.0`. | Source-certified. |
| Health checks | `stripe_foundation` and `stripe_operational_readiness` are registered. | Source-certified. |
| Feature flags | Sandbox execution requires sandbox mode and sandbox feature flag; production requires production flag. Defaults are disabled. | Source-certified. |
| Tenant configuration | Runtime application service resolves enabled tenant `TenantPaymentMethod` config, decrypts it, validates keys, and keeps credentials outside Runtime. | Source-certified. |

## Scenario Matrix

| Scenario | Validation evidence | Result |
| --- | --- | --- |
| 1. Successful Card Payment | Create/recovery path routes through `StripeRuntimeApplicationService -> StripeRuntimeBridge -> PaymentRuntimeOrchestrator`; webhook terminal success is admitted through replay/outcome admission before `PaymentLifecycleService.HandlePaymentResultAsync`. | Source-certified; live sandbox payment not executed. |
| 2. Failed Card | `payment_intent.payment_failed` is terminal-admitted, then application lifecycle persistence handles failed provider result. | Source-certified; live decline card not executed. |
| 3. Webhook Replay | `StripeWebhookReplayRecord` durable store plus `PaymentWebhookLogs` duplicate handling reject replay before lifecycle mutation. | Source-certified. |
| 4. Webhook Delay | Non-terminal `created`, `processing`, and `requires_action` are not admitted to Runtime; terminal event is admitted later. | Source-certified. |
| 5. Refund | `charge.refunded` terminal webhook is admitted and then persisted through `PaymentLifecycleService.RecordRefundAsync`. | Source-certified for webhook-driven refund; command-initiated refund remains pending production command wiring. |
| 6. Partial Refund | Stripe advanced adapter supports partial refund translation and requires an amount; webhook-driven refund persistence is certified. | Adapter/source-certified; POS production partial-refund command E2E not certified. |
| 7. Cancel | `payment_intent.canceled` is terminal-admitted and handled by application lifecycle persistence. | Source-certified for webhook-driven cancel. |
| 8. Recovery | Missing provider intent/client secret recovery rehydrates through canonical runtime application path using persisted idempotency. | Source-certified. |
| 9. Retry | Create path normalizes idempotency keys; webhook duplicate/replay is collapsed; transient provider errors map to retryable provider/application failures without false success. | Source-certified. |
| 10. Tenant Isolation | Tenant credential resolution is tenant-scoped; payment/order/customer validation checks tenant ownership; telemetry uses `tenant_bucket` rather than raw tenant ids. | Source-certified; two-merchant live sandbox drill not executed. |

## Runtime And Persistence Validation

Validated source boundaries:

- `RuntimeResult` is produced by `PaymentRuntimeOrchestrator` through the Stripe runtime bridge or webhook evidence adapter.
- `PaymentDecision` remains Runtime-owned; Stripe adapters do not create decisions directly.
- Provider evidence is produced before Runtime execution and application persistence.
- Replay protection loads durable prior evidence before webhook Runtime execution.
- Settlement and transaction artifacts remain Runtime artifacts; SQL lifecycle service remains the application writer.
- Correlation IDs are carried through create, webhook, replay, lifecycle, and telemetry paths.

## Security

| Control | Decision |
| --- | --- |
| Secrets never logged | Source guard passes for Stripe structured log tokens. |
| ClientSecret never logged | Source guard passes; client secret is stored as `ClientSecretEncrypted`. |
| Webhook verification enforced | Official `EventUtility.ConstructEvent` is used over raw payload and signature. |
| Credential isolation | Tenant payment-method config is decrypted inside the application boundary only. |
| Tenant isolation | Tenant-owned order, customer, payment intent, and payment-method checks are present. |
| Idempotency | Create and Stripe operation keys are deterministic and bounded to Stripe limits. |
| Replay protection | Durable replay store and webhook logs collapse duplicates before lifecycle mutation. |

## Observability

Meter: `PointOfSale.Payments.Stripe`

Validated metric names:

- `stripe_payment_created_total`
- `stripe_payment_succeeded_total`
- `stripe_payment_failed_total`
- `stripe_webhook_received_total`
- `stripe_webhook_duplicate_total`
- `stripe_replay_prevented_total`
- `stripe_refund_success_total`
- `stripe_refund_failure_total`
- `stripe_capture_success_total`
- `stripe_capture_failure_total`
- `stripe_runtime_decision_total`
- `stripe_payment_duration_ms`
- `stripe_provider_latency_ms`
- `stripe_webhook_processing_ms`
- `stripe_webhook_replay_total`
- `stripe_webhook_failures_total`
- `stripe_provider_availability_total`
- `stripe_provider_retries_total`
- `stripe_refund_latency_ms`

Logs include correlation and provider identifiers. Metric tags use `tenant_bucket` and must not expose raw tenant ids, API keys, webhook secrets, or client secrets.

## Failure Injection

| Drill | Expected behavior | Workspace result |
| --- | --- | --- |
| Stripe timeout | Provider error; no SQL success; retry/recovery explicit. | Source-certified by provider error mapping; not live-injected. |
| Webhook duplicate | Replay store/webhook log returns duplicate/replay result and no lifecycle mutation. | Source-certified. |
| Webhook replay | Existing provider event id prevents Runtime/lifecycle reprocessing. | Source-certified. |
| Invalid signature | Official SDK verification fails closed. | Covered by existing tests. |
| Expired webhook timestamp | Signature verifier tolerance fails closed. | Covered by existing tests. |
| Credential failure | Startup/health or tenant credential validation fails closed. | Source-certified. |
| Network interruption | Provider error/retry path; idempotency preserves retry safety. | Source-certified; not live-injected. |
| Unexpected provider error | Canonical provider error is returned; Runtime remains provider-neutral. | Source-certified. |

## Performance

No real sandbox timing was captured in this workspace. The following limits are the acceptance targets for the required live drill:

| Measurement | Target |
| --- | --- |
| Create Payment latency | p95 <= 2000 ms excluding user card confirmation. |
| Webhook processing latency | p95 <= 1000 ms for admitted terminal events excluding provider delivery delay. |
| Replay detection latency | p95 <= 250 ms with indexed replay store lookup. |
| Runtime latency | p95 <= 50 ms for in-memory Runtime execution. |
| Recovery latency | p95 <= 3000 ms for missing client secret/provider intent recovery excluding Stripe outage. |

## Test Evidence

Production validation tests added:

- `PointOfSale.Tests/Architecture/StripeProductionSandboxValidationTests.cs`

Recommended execution:

```text
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~StripeProductionSandboxValidationTests|FullyQualifiedName~StripeProductionOperationalReadinessTests|FullyQualifiedName~StripeConvergence1CertificationTests|FullyQualifiedName~StripeContractClosure2CertificationTests" -nologo -v minimal
```

## Certification Limits

Certified in this pass:

- POS Admin/Storefront source-level create/recovery/webhook/replay/refund-by-webhook/cancel-by-webhook architecture.
- Security guards for secret and client-secret non-logging.
- Health, metrics, feature flag, API version, SDK version, replay, outcome admission, and tenant credential isolation source behavior.

Not certified in this pass:

- Real Stripe sandbox card confirmation.
- Real signed webhook delivery from Stripe Workbench.
- Real failed-card drill.
- Real refund or partial-refund command execution.
- Real cancel command execution.
- Real two-merchant tenant-isolation drill against separate sandbox credentials.
- Real latency measurements.
- CommerceHub Stripe convergence.
- POS production command entry points for capture, refund command, partial refund command, retrieve, and cancel command.

## Success Answers

| Question | Answer |
| --- | --- |
| Environment validated? | Partially. Source configuration validated; real sandbox secrets and webhook endpoint were not present. |
| Successful payment certified? | Source-certified for POS Admin/Storefront pipeline; live sandbox payment not certified. |
| Failed payment certified? | Source-certified for terminal failed webhook path; live decline drill not certified. |
| Webhook certified? | Source-certified for signature, admission, replay, Runtime, and lifecycle ordering. |
| Replay certified? | Source-certified with durable replay store and duplicate collapse. |
| Refund certified? | Webhook-driven refund source-certified; command-initiated refund not certified. |
| Partial refund certified? | Adapter translation source-certified; production command E2E not certified. |
| Cancel certified? | Webhook-driven cancel source-certified; command-initiated cancel not certified. |
| Recovery certified? | Source-certified for canonical missing provider/client-secret recovery. |
| Retry certified? | Source-certified for deterministic idempotency and duplicate collapse. |
| Tenant isolation certified? | Source-certified; real Merchant A/B sandbox drill not executed. |
| Security certified? | Source-certified for inspected POS Admin/Storefront Stripe path. |
| Performance acceptable? | Not measured; acceptance targets documented. |
| Production Sandbox certified? | **No. Live Stripe sandbox execution was not completed in this workspace.** |
| Recommend next phase? | Yes. Provision sandbox credentials and signed webhook endpoint, run the live drill matrix, then proceed to CommerceHub Stripe convergence and production advanced-operation command wiring. |

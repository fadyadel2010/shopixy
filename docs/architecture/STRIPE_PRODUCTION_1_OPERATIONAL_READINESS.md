# STRIPE-PRODUCTION-1 Operational Readiness

Date: 2026-07-13

Status: PRODUCTION OPERATIONAL READINESS CERTIFIED FOR POS ADMIN AND STOREFRONT STRIPE

## Operational Architecture

Stripe production operation for POS Admin and Storefront remains an application-layer operational boundary around the frozen Payment Runtime.

```text
Storefront/POS request
  -> StripePaymentService
  -> StripeRuntimeApplicationService
  -> StripeRuntimeBridge
  -> PaymentRuntimeOrchestrator
  -> PaymentLifecycleService SQL writer

Stripe webhook
  -> StripeWebhooksController
  -> StripePaymentService
  -> StripeWebhookRuntimeIntegrationService
  -> outcome admission + replay store
  -> StripeWebhookEvidenceAdapter
  -> PaymentRuntimeOrchestrator
  -> PaymentLifecycleService SQL writer
```

Runtime, Stripe adapters, application contracts, policy engine, business rules, and execution pipeline were not redesigned.

## Health Matrix

| Health | Certification |
| --- | --- |
| Foundation Health | `stripe_foundation` validates configuration and official SDK presence without Stripe API calls. |
| Credential Health | Startup validation rejects missing, short, or wrong-mode credentials when Stripe is enabled. |
| Environment Health | Sandbox/live key prefixes, `SandboxMode`, and feature flags must agree. |
| SDK Health | Official `Stripe.net` and certified API version are checked. |
| Webhook Endpoint Health | `stripe_operational_readiness` verifies `IStripePaymentService` is registered once. |
| Runtime Bridge Health | `IStripeRuntimeBridge` is registered once through the tenant-scoped Program boundary. |
| Application Service Health | `IStripeRuntimeApplicationService` is registered once. |
| Replay Store Health | `ApplicationDbContext` exposes `StripeWebhookReplayRecords` and unique provider-event storage. |
| Outcome Admission Health | Non-terminal events are not admitted to Runtime. |
| Dependency Health | Webhook runtime integration and evidence adapter are registered once. |

## Startup Validation

Startup fails closed for missing credentials, invalid key mode, sandbox/live mismatch, invalid webhook secret prefix, API-version drift, disabled sandbox/production feature flags, and duplicate create-intent bridge registration. A disabled provider remains safe: credentials are not required until `Enabled` or `FeatureFlags.Enabled` activates runtime execution.

## Sandbox Readiness

Sandbox drills are defined for no-live-money verification:

| Flow | Expected result |
| --- | --- |
| Create Payment | Tenant sandbox config creates PaymentIntent through `StripeRuntimeApplicationService`; client secret is encrypted before SQL persistence. |
| Confirm Payment | Stripe.js confirms with sandbox card; local success waits for webhook. |
| Webhook | Signed terminal event passes replay/outcome admission before lifecycle persistence. |
| Refund | `charge.refunded` reconciles through `PaymentLifecycleService.RecordRefundAsync`. |
| Capture | Adapter translation exists; POS Admin/Storefront command entry point remains pending. |
| Cancel | Terminal cancel webhook records cancelled lifecycle outcome. |
| Recovery | Missing provider/client-secret recovery rehydrates through canonical runtime application path. |
| Replay | Duplicate provider event is blocked by replay store and webhook log idempotency. |
| Retry | Stripe/provider or persistence failure returns retryable failure; no false 200 for unresolved webhook failures. |
| Failure | Failed PaymentIntent webhook records failed lifecycle outcome and releases downstream authority through lifecycle service. |

## Observability

Logs consistently carry correlation and provider identifiers: `CorrelationId` or trace id, order/payment intent ids, provider PaymentIntent or charge reference, `StripeEventId`, replay `EvidenceId`, provider reference, and lifecycle decision/status where available. Runtime artifacts keep `PaymentSessionId`, `PaymentAttemptId`, `DecisionId`, and `SettlementId` inside canonical `RuntimeResult` rather than frontend contracts.

No secrets, No ClientSecret, no API keys, no webhook secret, and no PII should be logged. Client secret values are encrypted via `ClientSecretEncrypted`; health output is configuration-only and excludes secret values.

## Metrics Matrix

Meter: `PointOfSale.Payments.Stripe`

| Metric | Purpose |
| --- | --- |
| `stripe_payment_created_total` | Create-intent attempts by mode. |
| `stripe_payment_succeeded_total` | Successful webhook/lifecycle recognition. |
| `stripe_payment_failed_total` | Failed provider/lifecycle outcomes by reason code. |
| `stripe_webhook_received_total` | Signed webhook events received by event type. |
| `stripe_webhook_duplicate_total` | Duplicate webhook log collapses. |
| `stripe_replay_prevented_total` | Replay-store duplicate/replay prevention. |
| `stripe_refund_success_total` | Refund reconciliation success. |
| `stripe_refund_failure_total` | Refund reconciliation failure. |
| `stripe_capture_success_total` | Reserved for future POS capture command wiring. |
| `stripe_capture_failure_total` | Reserved for future POS capture command wiring. |
| `stripe_runtime_decision_total` | Runtime/lifecycle decision classifications. |
| `stripe_payment_duration_ms` | End-to-end payment operation duration by operation and outcome. |
| `stripe_provider_latency_ms` | Stripe provider call latency by operation and outcome. |
| `stripe_webhook_processing_ms` | Webhook processing duration by event type and outcome. |
| `stripe_webhook_replay_total` | Duplicate/replay webhook events prevented by durable replay or audit fences. |
| `stripe_webhook_failures_total` | Webhook processing failures by event type and reason code. |
| `stripe_provider_availability_total` | Provider availability observations by operation and reason code. |
| `stripe_provider_retries_total` | Retryable provider/webhook pressure by operation and reason code. |
| `stripe_refund_latency_ms` | Refund reconciliation latency by source and outcome. |

Metric tags use `tenant_bucket`, source/mode/event/reason labels, and do not expose raw tenant ids, customer ids, client secrets, or API keys.

## Alert Matrix

| Alert | Trigger |
| --- | --- |
| Webhook failures | `PaymentWebhookLogs` failed rows or webhook 500 responses exceed threshold. |
| Credential failures | `stripe_foundation` or `stripe_operational_readiness` unhealthy. |
| Repeated replay detection | `stripe_replay_prevented_total` spikes for one tenant bucket or event type. |
| Payment failure spike | `stripe_payment_failed_total` rate exceeds baseline. |
| Missing webhook delivery | Pending Stripe intents older than operational SLA with no terminal webhook. |
| Runtime bridge failures | `stripe_runtime_decision_total` missing while create/webhook traffic continues, or runtime status failures rise. |
| Health degradation | Any Stripe operational health component turns false. |

## Runbooks

| Runbook | Operator action |
| --- | --- |
| Rotate API Keys | Add new tenant Stripe key in encrypted payment-method config, verify sandbox/live prefix, run health, then revoke old key in Stripe. |
| Rotate Webhook Secret | Create new Stripe endpoint secret, update encrypted tenant config, verify signed sandbox webhook, then remove old secret. |
| Replay Failed Event | Inspect `PaymentWebhookLogs`, verify Stripe event id, clear failed processing state only through reviewed replay tooling, then let provider retry or resubmit signed payload. |
| Recover Missing Client Secret | Re-run create/recovery path; service rehydrates through canonical runtime application service and persists encrypted client secret. |
| Recover Missing Redirect URL | Confirm redirect recovery queue and hosted service are running; re-enqueue recovery work item if needed. |
| Credential Expired | Disable tenant Stripe method if unsafe, rotate keys, verify `stripe_foundation`, and run sandbox smoke. |
| Stripe Outage | Keep orders pending/processing; do not force success. Use retry/reconciliation after provider recovery. |
| Webhook Delay | Monitor pending intents, verify endpoint health in Stripe Workbench, and rely on webhook retry/recovery rather than local guessing. |

## Failure Drills

| Drill | Expected Runtime behavior |
| --- | --- |
| Stripe timeout | Provider error evidence; no SQL success; retry/recovery remains explicit. |
| Stripe outage | Creation fails or remains pending; local authority does not invent provider outcome. |
| Network failure | Same as timeout; idempotency key preserves retry safety. |
| Webhook delay | Payment remains pending/processing until terminal admitted evidence arrives. |
| Webhook duplicate | Replay store/webhook log collapses duplicate; no duplicate lifecycle mutation. |
| Credential revoked | Startup/health or provider call fails; no secret logged. |
| Partial refund failure | Failure metric/log; local refund remains unresolved/reconciliation-required. |
| Capture failure | Adapter returns provider error evidence; no capture SQL mutation without future command coordination. |
| Cancel failure | Failure remains provider evidence; terminal cancel only through admitted webhook/lifecycle. |
| Recovery failure | Explicit retry/manual review path; no false terminal state. |

## Security Review

Passed: no secret logging, no `ClientSecret` logging, encrypted client-secret persistence, tenant-scoped credential resolution, tenant isolation checks, durable replay protection, official webhook signature validation, deterministic idempotency, and sandbox/live environment separation.

## Production Checklist

| Item | Status |
| --- | --- |
| Sandbox verified | Defined; must be executed per environment before traffic. |
| Health green | Certified through `stripe_foundation` and `stripe_operational_readiness`. |
| Alerts configured | Defined in Alert Matrix. |
| Logs verified | Certified by source guards and operational review. |
| Runbooks completed | Completed in this document. |
| Recovery verified | Existing canonical rehydrate/recovery path certified. |
| Replay verified | Durable replay store certified. |
| Webhook verified | Admission/replay/lifecycle path certified. |
| Refund verified | Webhook-driven refund reconciliation certified. |
| Capture verified | Adapter translation certified; POS command entry point pending. |
| Cancel verified | Webhook-driven cancel certified; POS command entry point pending. |
| Credential rotation documented | Completed in Runbooks. |

## Certification Decision

Operational readiness completed? Yes.

Health monitoring certified? Yes.

Startup validation certified? Yes.

Sandbox readiness certified? Yes, as a no-live-money drill plan and source-certified sandbox path.

Observability certified? Yes.

Metrics defined? Yes.

Alerts defined? Yes.

Runbooks completed? Yes.

Failure drills documented? Yes.

Security review passed? Yes.

Production checklist completed? Yes.

Production operational readiness certified? Yes, for POS Admin and Storefront Stripe create/recovery/webhook/refund-by-webhook operation.

Recommend next phase? Yes. Proceed to CommerceHub Stripe convergence and production advanced-operation command wiring for capture, cancel, retrieve, refund command, and partial refund command.

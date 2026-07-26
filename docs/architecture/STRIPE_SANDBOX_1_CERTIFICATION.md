# STRIPE-SANDBOX-1 Certification

Date: 2026-07-17

Status: SOURCE OPERATIONAL READINESS CERTIFIED; LIVE STRIPE SANDBOX EXECUTION PENDING

Scope: POS Admin and Storefront Stripe sandbox validation against the certified Stripe Provider and frozen Payment Platform architecture.

## Sandbox Environment

The repository remains configured for a disabled sandbox-by-default posture:

- `CommerceHub:Payments:Stripe:Environment=Sandbox`
- `SandboxMode=true`
- `Enabled=false`
- `FeatureFlags.Enabled=false`
- `FeatureFlags.PaymentIntentsEnabled=false`
- `FeatureFlags.WebhooksEnabled=false`
- `FeatureFlags.CaptureEnabled=false`
- `FeatureFlags.RefundsEnabled=false`
- blank `SecretKey`, `PublishableKey`, and `WebhookSecret`

No Stripe-related environment variables were present in the current workspace process during this certification pass. Because no `sk_test_` or `rk_test_`, `pk_test_`, `whsec_`, public API base URL, or signed Stripe webhook ingress was available, real Stripe sandbox calls were not executed in this workspace.

## Credentials Strategy

Stripe credentials remain server-side and outside source control.

Sandbox enablement requires:

- tenant-owned encrypted Stripe payment-method configuration;
- `pk_test_` publishable key;
- `sk_test_` or `rk_test_` secret key;
- `whsec_` webhook signing secret;
- `SandboxMode=true`;
- `FeatureFlags.SandboxEnabled=true`;
- explicit enablement of the operation feature flags used by the drill.

Startup and credential validation reject missing credentials, wrong sandbox/live key prefixes, invalid webhook secret prefixes, API-version drift, disabled sandbox execution, and mismatched sandbox/production mode.

## Operational Flow

The operational flow remains the certified Stripe application path:

```text
Storefront/POS request
  -> StripePaymentService
  -> StripeRuntimeApplicationService
  -> StripeRuntimeBridge
  -> StripePaymentIntentAdapter / StripeAdvancedOperationsAdapter
  -> PaymentRuntimeOrchestrator
  -> PaymentLifecycleService SQL writer
```

Runtime, workflow, handlers, and lifecycle architecture were not modified.

Create PaymentIntent is source-certified through the canonical runtime application path. Confirmation remains client-side through Stripe.js and is not local finality. Capture, cancel, refund, and partial refund translation are available in the provider adapter; active command-level production certification remains bounded by the existing architecture records. Webhook-driven cancel and refund reconciliation are source-certified through the lifecycle writer.

## Webhook Flow

The webhook flow remains:

```text
StripeWebhooksController
  -> raw body + Stripe-Signature header
  -> tenant webhook configuration
  -> EventUtility.ConstructEvent
  -> StripeWebhookRuntimeIntegrationService outcome admission
  -> durable replay lookup
  -> StripeWebhookEvidenceAdapter
  -> PaymentRuntimeOrchestrator
  -> PaymentLifecycleService
  -> webhook audit and replay completion
```

Invalid, expired, malformed, duplicate, unsupported, disputed, and non-terminal events fail closed or are skipped without lifecycle authority.

## Replay Validation

Replay protection is source-certified through:

- `StripeWebhookReplayRecord` durable replay lookup and persistence;
- `PaymentWebhookLogs` duplicate/in-flight/failed state handling;
- Runtime prior-evidence loading before webhook Runtime execution;
- duplicate webhook metrics: `stripe_webhook_duplicate_total`, `stripe_replay_prevented_total`, and `stripe_webhook_replay_total`.

Live duplicate delivery from Stripe Workbench remains pending real signed webhook delivery.

## Failure Drills

| Drill | Source result | Live sandbox result |
| --- | --- | --- |
| Create payment | Source-certified. | Pending credentials. |
| Successful payment | Source-certified through terminal webhook path. | Pending signed webhook. |
| Failed payment | Source-certified for `payment_intent.payment_failed`. | Pending decline-card drill. |
| Cancelled payment | Source-certified for `payment_intent.canceled`. | Pending live cancel event. |
| Refund | Source-certified for `charge.refunded` reconciliation. | Pending live refund event. |
| Partial refund | Adapter/source-certified; webhook delta reconciliation source-certified. | Pending live partial refund drill. |
| Duplicate webhook | Source-certified through replay/log duplicate handling. | Pending duplicate resend. |
| Expired signature | Source-certified by Stripe signature verifier tolerance tests. | Pending signed stale payload drill. |
| Invalid signature | Source-certified by fail-closed signature path. | Pending signed invalid-header drill. |
| Provider timeout | Source-certified by canonical provider error handling. | Pending network drill. |
| Provider retry | Source-certified through deterministic idempotency and retry metrics. | Pending provider retry drill. |
| 429 handling | Source-certified by Stripe error-to-rate-limited mapping. | Pending forced 429 drill. |
| 500 handling | Source-certified by provider-unavailable mapping. | Pending forced 500 drill. |
| Network interruption | Source-certified as provider failure without local success. | Pending network drill. |

## Metrics

Meter: `PointOfSale.Payments.Stripe`

Required operational metrics are exposed by `StripeOperationalTelemetry`:

- `stripe_payment_created_total`
- `stripe_payment_succeeded_total`
- `stripe_payment_failed_total`
- `stripe_payment_duration_ms`
- `stripe_provider_latency_ms`
- `stripe_provider_availability_total`
- `stripe_provider_retries_total`
- `stripe_webhook_received_total`
- `stripe_webhook_processing_ms`
- `stripe_webhook_duplicate_total`
- `stripe_webhook_replay_total`
- `stripe_webhook_failures_total`
- `stripe_replay_prevented_total`
- `stripe_refund_success_total`
- `stripe_refund_failure_total`
- `stripe_refund_latency_ms`
- `stripe_capture_success_total`
- `stripe_capture_failure_total`
- `stripe_runtime_decision_total`

Metric tags use bounded labels such as `tenant_bucket`, `operation`, `outcome`, `event_type`, `reason_code`, `source`, and `available`. Raw tenant ids, client secrets, API keys, webhook secrets, and customer PII are not metric labels.

## Dashboards

Grafana dashboard artifact:

```text
docs/observability/stripe/grafana-dashboard.json
```

Panels cover:

- payment duration p95;
- Stripe provider latency p95;
- webhook processing p95;
- webhook replay and failures;
- provider availability and retries;
- refund latency p95.

Dashboard operational status in this workspace is source-defined but not live-loaded into a Grafana instance.

## Alerts

Prometheus alert artifact:

```text
docs/observability/stripe/prometheus-alerts.yml
```

Rules cover:

- provider availability failures;
- webhook failures;
- webhook replay spikes;
- provider retry pressure;
- payment latency p95;
- webhook latency p95;
- refund latency p95.

Alert operational status in this workspace is source-defined but not live-loaded into a Prometheus/Alertmanager instance.

## Known Issues

- Real Stripe sandbox credentials were not present.
- Public signed webhook delivery was not available from this workspace.
- Live PaymentIntent creation, card confirmation, capture, cancel, refund, partial refund, and Stripe Workbench duplicate/invalid/expired signature drills were not executed.
- Grafana dashboard and Prometheus alert files were added as source artifacts; no running Grafana/Prometheus instance was available to load and fire them.
- Capture, refund command, partial refund command, and cancel command activation remains bounded by existing certification records; adapter translation does not by itself certify every active business command surface.

## Certification

| Question | Answer |
| --- | --- |
| Sandbox connected? | No. Credentials and signed ingress were not present. |
| Payments created? | No live sandbox payment was created in this workspace. Source path is certified. |
| Payments confirmed? | No live sandbox confirmation was executed. Terminal webhook path is source-certified. |
| Capture verified? | Adapter/source-certified; live capture drill pending. |
| Refund verified? | Webhook-driven refund source-certified; live refund drill pending. |
| Partial refund verified? | Adapter/source-certified; live partial refund drill pending. |
| Webhook verified? | Source-certified for signature, admission, replay, Runtime, and lifecycle ordering. Live Stripe delivery pending. |
| Signature validation verified? | Source-certified through official Stripe verifier path and tests. Live invalid/expired signed payload pending. |
| Replay protection verified? | Source-certified through durable replay/log handling and metrics. Live duplicate resend pending. |
| Metrics operational? | Source-operational and wired through OpenTelemetry meter. Live scrape validation pending. |
| Dashboards operational? | Dashboard artifact created. Live Grafana import pending. |
| Alerts operational? | Alert rules artifact created. Live Prometheus/Alertmanager load and firing pending. |
| Architecture unchanged? | Yes. Runtime, workflow, handlers, and lifecycle writer were not modified. |
| Ready for Production Readiness? | Source-ready, but not production-ready until real sandbox credentials, signed webhook delivery, dashboard import, alert load, and live drill evidence pass. |

Final decision: STRIPE-SANDBOX-1 is source-operationally certified and live-sandbox blocked on external credentials/ingress. Live Operational Certification is not granted by this document.

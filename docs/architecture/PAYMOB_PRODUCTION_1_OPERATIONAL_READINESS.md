# PAYMOB-PRODUCTION-1 Operational Readiness

Date: 2026-07-13

Status: PRODUCTION OPERATIONAL READINESS CERTIFIED FOR POS ADMIN AND STOREFRONT PAYMOB

## Executive Summary

Paymob operational readiness is certified at source level for the POS Admin and Storefront Paymob provider. This phase certifies operations only: health, startup validation, telemetry, metrics, alerting, runbooks, failure drills, security review, and production checklists.

The Payment Runtime remains permanently frozen. No Runtime, Runtime Bridge, Provider Adapter, Webhook Adapter, controller, frontend, business-rule, persistence, or SQL change is part of this certification.

## Operational Architecture

Paymob follows the Stripe reference operational architecture around the frozen Runtime:

```text
Storefront/POS request
  -> PaymobPaymentService
  -> PaymobRuntimeApplicationService
  -> PaymobRuntimeBridge
  -> PaymentRuntimeOrchestrator
  -> PaymentLifecycleService SQL writer

Paymob webhook/callback
  -> PaymobWebhooksController
  -> PaymobPaymentService
  -> HMAC validation + replay/admission checks
  -> PaymobWebhookRuntimeIntegrationService
  -> PaymobWebhookEvidenceAdapter
  -> PaymentRuntimeOrchestrator
  -> PaymentLifecycleService SQL writer
```

Operational readiness is implemented through `PaymobOperationalReadinessHealthCheck` and `PaymobOperationalTelemetry`. The health check validates readiness without Paymob API calls. The telemetry meter is registered as `PointOfSale.Payments.Paymob` for OpenTelemetry export.

## Health

Health check: `paymob_operational_readiness`.

| Health | Certification |
| --- | --- |
| Provider enabled | Validates Paymob is enabled before operational readiness can pass. |
| Environment configured | Sandbox requires `SandboxMode=true` and sandbox flag; production requires `SandboxMode=false` and production flag. |
| Credentials configured | Requires API key, integration id, iframe id, and HMAC secret through existing startup validation. |
| Merchant identifiers | Requires numeric integration and iframe identifiers. |
| HMAC secret | Requires configured HMAC secret and existing minimum credential validation. |
| Callback URL | Requires explicit callback and webhook URL configuration. |
| Feature flags | Requires runtime enablement, environment flag, and validated credential state to agree. |
| Capability consistency | Requires official HTTPS API, HMAC verification, tenant credentials, sandbox, and production capabilities. |
| Runtime bridge | Verifies `IPaymobRuntimeBridge` is registered once. |
| Application service | Verifies `IPaymobRuntimeApplicationService` is registered once. |
| Webhook endpoint service | Verifies `IPaymobPaymentService` is registered once. |
| Replay store | Verifies `PaymobWebhookReplayRecords` are queryable. |
| Webhook audit store | Verifies `PaymentWebhookLogs` are queryable. |
| Tenant configuration | Verifies at least one enabled encrypted tenant Paymob method exists. |
| Dependency health | Verifies Paymob webhook integration, evidence adapter, and provider adapter registrations. |

The check fails closed. Any missing registration, invalid startup configuration, unavailable replay/audit table, missing tenant configuration, or persistence exception returns unhealthy.

## Telemetry

Meter: `PointOfSale.Payments.Paymob`.

Telemetry uses tenant buckets and operational labels only. Raw tenant ids, customer ids, API keys, HMAC secrets, callback secrets, provider payload secrets, and PII are not metric labels.

OpenTelemetry registration is wired through `AddMeter(PointOfSale.API.Instrumentation.PaymobOperationalTelemetry.MeterName)` and exported through the existing metrics pipeline.

## Metrics

| Metric | Purpose |
| --- | --- |
| `paymob_payment_create_attempt_total` | Create-payment attempts by mode. |
| `paymob_payment_create_success_total` | Successful payment-key/redirect creation recognition. |
| `paymob_provider_failure_total` | Provider failures by operation and reason. |
| `paymob_webhook_received_total` | Paymob webhook/callback receipts by event type. |
| `paymob_replay_rejected_total` | Replay attempts rejected by replay/admission controls. |
| `paymob_duplicate_rejected_total` | Duplicate webhook/callback events rejected. |
| `paymob_runtime_admitted_total` | Evidence admitted to Runtime. |
| `paymob_runtime_rejected_total` | Runtime/admission rejection by source and reason. |
| `paymob_refund_received_total` | Refund webhook evidence received. |
| `paymob_void_received_total` | Void/cancel evidence received. |
| `paymob_capture_received_total` | Capture evidence received. |
| `paymob_background_recovery_total` | Background checkout recovery work by reason. |
| `paymob_retry_total` | Retry attempts by operation and reason. |

## Alerts

| Alert | Trigger |
| --- | --- |
| Provider disabled | `paymob_operational_readiness` unhealthy because provider enablement is false in an environment expecting Paymob traffic. |
| Credential invalid | Health unhealthy for credential, merchant identifier, HMAC, callback, or environment checks. |
| Repeated HMAC failures | HMAC validation failures exceed tenant/provider baseline. |
| Replay spikes | `paymob_replay_rejected_total` spikes by tenant bucket or event type. |
| Webhook failure spikes | Paymob webhook non-2xx responses, failed `PaymentWebhookLogs`, or runtime rejected metrics exceed threshold. |
| High provider error rate | `paymob_provider_failure_total` rate exceeds baseline for create, retry, refund, void, capture, or inquiry. |
| Background recovery failures | Recovery queue failures or `paymob_background_recovery_total` with unresolved outcomes exceeds SLA. |
| Tenant configuration failures | No enabled encrypted Paymob tenant method or repeated tenant resolution failures. |

## Runbooks

| Runbook | Operator action |
| --- | --- |
| Credential rotation | Add new encrypted tenant Paymob credentials, verify integration/iframe ids and HMAC, run health, execute sandbox smoke, then revoke old provider credentials. |
| Environment switch | Change environment, sandbox mode, and environment feature flag together; run startup validation and health before traffic. |
| Sandbox rollout | Enable sandbox only, configure sandbox tenant credentials/callbacks, execute create, callback, webhook, duplicate, replay, refund, retry, and recovery drills. |
| Production rollout | Apply migrations, provision live tenant credentials, verify public callback/webhook URLs, run health, configure alerts, execute limited live smoke, then increase traffic. |
| Webhook recovery | Inspect webhook logs and replay records, verify HMAC, use provider retry/resend where available, and avoid manual lifecycle success. |
| Replay recovery | Identify rejected evidence, confirm replay identity, keep duplicate financial effects blocked, and reprocess only through reviewed recovery tooling. |
| Tenant disablement | Disable the tenant Paymob payment method and provider flag, confirm health/traffic drop, keep existing orders pending or reconcile through approved workflows. |
| Callback outage | Keep payments pending, restore public callback route, verify HMAC delivery, and rely on webhook/recovery instead of local guessing. |

## Failure Drills

| Drill | Expected behavior |
| --- | --- |
| Invalid credentials | Startup or health fails closed; provider errors are recorded; no secret is logged. |
| Expired callback secret | HMAC validation fails; callbacks/webhooks are rejected; repeated failures alert. |
| Webhook outage | Payments remain pending/processing until admitted terminal evidence arrives. |
| Replay attack | Replay store rejects repeated evidence; no duplicate lifecycle mutation. |
| Duplicate webhook storm | Duplicate/replay metrics rise; webhook log and replay storage collapse duplicate effects. |
| Provider downtime | Create/retry paths fail or stay pending; local system does not invent success. |
| HTTP timeout | Provider failure/retry metric rises; idempotent retry and recovery remain explicit. |
| Network partition | Same as provider downtime; no Runtime or SQL success without admitted evidence. |

## Security Review

Passed at source-operation level:

| Area | Result |
| --- | --- |
| Credential storage | Tenant Paymob configuration remains encrypted through existing tenant payment-method configuration. |
| Secret logging | Operational health and metrics expose booleans, environment, buckets, and reason codes only. |
| Telemetry redaction | Metrics use `tenant_bucket`, not raw tenant ids or secrets. |
| Tenant isolation | Health checks encrypted tenant configuration presence; execution remains tenant-scoped through certified Paymob paths. |
| Environment isolation | Sandbox/production mode and feature flags must agree before readiness passes. |
| Operational auditability | Webhook audit logs and Paymob replay records remain required readiness dependencies. |

## Production Checklist

Operational checklist:

| Item | Status |
| --- | --- |
| Health green | Certified through `paymob_foundation` and `paymob_operational_readiness`. |
| Startup validation green | Certified through existing `PaymobOptionsValidator`. |
| Metrics registered | Certified through `PointOfSale.Payments.Paymob`. |
| Alerts defined | Completed in Alert Matrix. |
| Runbooks completed | Completed in this document. |
| Failure drills documented | Completed in this document. |
| Security review passed | Passed at source-operation level. |

Deployment checklist:

| Item | Status |
| --- | --- |
| Migrations applied | Required per environment, including Paymob replay storage. |
| Tenant credentials provisioned | Required before traffic. |
| Callback and webhook URLs public | Required before traffic. |
| Sandbox smoke complete | Required before production. |
| Production smoke complete | Required before broad rollout. |

Monitoring checklist:

| Item | Status |
| --- | --- |
| Health dashboard | Must include `paymob_operational_readiness`. |
| Metrics dashboard | Must include all Paymob operational counters. |
| Alert routes | Must page or notify owning operations team. |
| Webhook backlog view | Must monitor failed webhook logs and replay rejection rate. |
| Recovery queue view | Must monitor background recovery age and failures. |

Rollback checklist:

| Item | Status |
| --- | --- |
| Disable provider | Disable tenant Paymob method and provider feature flag. |
| Stop new traffic | Remove Paymob from available payment methods for affected tenants. |
| Preserve pending orders | Do not mark unresolved Paymob payments successful. |
| Reconcile in-flight payments | Use webhook/recovery/reconciliation after provider state is known. |
| Keep audit data | Preserve webhook logs and replay records for investigation. |

## Stripe Delta

Only operational differences are certified:

| Area | Stripe | Paymob |
| --- | --- | --- |
| Provider protocol | PaymentIntent and official Stripe signature verification. | Payment-key/redirect flow and Paymob HMAC callback/webhook verification. |
| Credential shape | Publishable key, secret/restricted key, webhook secret. | API key, integration id, iframe id, HMAC secret. |
| Merchant identifiers | Stripe account/API key mode. | Paymob integration id and iframe id. |
| Callback operation | Stripe webhook endpoint. | Paymob callback and webhook URLs. |
| Metrics meter | `PointOfSale.Payments.Stripe`. | `PointOfSale.Payments.Paymob`. |

The architecture remains identical. Provider differences terminate inside Paymob operational configuration and existing provider boundaries; Runtime remains frozen and provider-neutral.

## Certification Decision

Paymob production operational readiness is certified at source level for POS Admin and Storefront operations. Live production validation remains per-deployment and requires real credentials, public callback/webhook reachability, alert firing, sandbox drills, live smoke, and operator sign-off.

No new permanent cross-provider architecture truth was discovered. `.ai-context` is not modified.

## Success Answers

| Question | Answer |
| --- | --- |
| Operational Readiness completed? | Yes. |
| Health Monitoring certified? | Yes. |
| Startup Validation certified? | Yes. |
| Observability certified? | Yes. |
| Metrics defined? | Yes. |
| Alerts defined? | Yes. |
| Runbooks completed? | Yes. |
| Failure Drills documented? | Yes. |
| Security Review passed? | Yes. |
| Production Checklist completed? | Yes. |
| Operational Readiness certified? | Yes, at source level for POS Admin and Storefront Paymob operations. |
| Recommend next phase? | Yes: execute environment-specific sandbox and production validation, then continue CommerceHub Paymob convergence or production advanced-operation command wiring when in scope. |

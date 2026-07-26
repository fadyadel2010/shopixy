# FAWRY-PRODUCTION-1 Operational Readiness

Date: 2026-07-15

Status: PRODUCTION OPERATIONAL READINESS CERTIFIED AT SOURCE LEVEL FOR POS ADMIN AND STOREFRONT FAWRY

## Executive Summary

Fawry operational readiness is certified at source level for the POS Admin and Storefront Fawry provider.

This phase is operational only: health, startup validation, telemetry, metrics, alerting, runbooks, source-level failure drills, security review, production checklist, and certification. It does not change payment execution.

The Payment Runtime remains permanently frozen. No Runtime, Runtime Bridge, Provider Adapter, Webhook Adapter, Advanced Adapter, controller, frontend, persistence, SQL, CommerceHub, provider protocol, or business-rule change is part of this certification.

## Operational Architecture

Fawry remains converged onto the certified application-owned execution path:

```text
POS Admin / Storefront request
  -> PaymentService
  -> IFawryRuntimeApplicationService
  -> IFawryRuntimeBridge
  -> IFawryPaymentAdapter
  -> PaymentRuntimeOrchestrator
  -> exact Runtime outcome admission
  -> PaymentIntent projection / lifecycle authority
```

Webhook processing remains:

```text
FawryWebhookController
  -> IFawryWebhookRuntimeIntegrationService
  -> IFawryWebhookEvidenceAdapter
  -> PaymentRuntimeOrchestrator
  -> FawryWebhookLifecycleAdmission
  -> PaymentLifecycleService
  -> FawryWebhookReplayRecords
```

Operational readiness is implemented through `IFawryOperationalReadinessHealthContributor`, `FawryOperationalReadinessHealthCheck`, and `FawryOperationalTelemetry`.

Health check name: `fawry_operational_readiness`.

OpenTelemetry meter: `PointOfSale.Payments.Fawry`.

## Health

Health is configuration-only and local-dependency-only. It does not call Fawry, execute payments, invoke the provider adapter, or drive webhook execution.

| Health | Certification |
| --- | --- |
| Provider enabled | Requires Fawry provider/runtime enablement before readiness passes. |
| Foundation health | Reuses certified Fawry foundation validation. |
| Environment | Sandbox requires `SandboxMode=true` and sandbox flag; production requires `SandboxMode=false` and production flag. |
| Credentials | Requires merchant code, API key, secret, and successful credential validation. |
| Callback/webhook URLs | Requires explicit configured absolute callback and webhook URLs. |
| Feature flags | Requires environment flags and credential validation to agree. |
| Capabilities | Requires official HTTPS API, SHA-256 signature support, tenant credentials, sandbox, production, notification, and inquiry capability. |
| Runtime bridge | Verifies exactly one `IFawryRuntimeBridge` registration. |
| Application service | Verifies exactly one `IFawryRuntimeApplicationService` registration. |
| Webhook integration | Verifies exactly one `IFawryWebhookRuntimeIntegrationService` registration. |
| Replay store | Verifies `FawryWebhookReplayRecords` are queryable. |
| Webhook audit store | Verifies `PaymentWebhookLogs` are queryable. |
| Tenant configuration | Verifies at least one enabled encrypted tenant Fawry payment method exists. |
| Dependencies | Verifies evidence adapter, provider adapter, and tenant resolver registrations. |

Readiness fails closed when any required configuration, registration, local table, or tenant configuration check is missing.

## Telemetry

Telemetry uses operational labels only. It does not include raw tenant IDs, order IDs, customer IDs, provider payloads, credentials, callback secrets, signatures, payment keys, references, or PII.

Tenant labels use `tenant_bucket`.

OpenTelemetry registration is wired through `AddMeter(PointOfSale.API.Instrumentation.FawryOperationalTelemetry.MeterName)`.

## Metrics

| Metric | Purpose |
| --- | --- |
| `fawry_payment_create_attempt_total` | Payment create attempts by mode. |
| `fawry_webhook_received_total` | Fawry webhook receipts by event type. |
| `fawry_webhook_accepted_total` | Webhooks accepted for processing. |
| `fawry_webhook_rejected_total` | Webhooks rejected by reason. |
| `fawry_replay_detected_total` | Replay evidence detected. |
| `fawry_duplicate_detected_total` | Duplicate webhook or operation evidence detected. |
| `fawry_runtime_accepted_total` | Evidence accepted by Runtime-admission path. |
| `fawry_runtime_rejected_total` | Runtime/admission rejection by source and reason. |
| `fawry_application_failure_total` | Application orchestration failures by operation and reason. |
| `fawry_provider_failure_total` | Provider failures by operation and reason. |
| `fawry_refund_request_total` | Refund operation requests. |
| `fawry_capture_request_total` | Capture operation requests. |
| `fawry_void_request_total` | Void/cancel operation requests. |
| `fawry_inquiry_request_total` | Inquiry operation requests. |
| `fawry_advanced_operation_failure_total` | Advanced-operation failures by operation and reason. |

## Alerts

| Alert | Trigger |
| --- | --- |
| Provider unavailable | Provider failure rate exceeds baseline or create attempts fail repeatedly. |
| High rejection rate | Webhook or Runtime rejection rate exceeds baseline by tenant bucket/source. |
| Replay spike | Replay detections spike above tenant/provider baseline. |
| Duplicate spike | Duplicate detections spike above tenant/provider baseline. |
| Configuration invalid | `fawry_operational_readiness` becomes unhealthy for configuration, credentials, callback, capability, or environment checks. |
| Tenant disabled | Tenant configuration check fails or tenant Fawry method is disabled while traffic is expected. |
| Webhook verification failures | Webhook rejected rate increases for verification/signature reasons. |
| Unexpected Runtime rejection | Runtime rejection appears for previously healthy event types or create flows. |
| Operational degradation | Replay/audit table failures, webhook audit failures, provider failures, or local dependency failures persist beyond SLA. |

## Runbooks

| Runbook | Operator action |
| --- | --- |
| Provider outage | Stop increasing traffic, keep payments pending, monitor provider failures, use inquiry/reconciliation only through certified paths, and never mark local success without admitted evidence. |
| Credential rotation | Add encrypted tenant credentials, validate merchant code/API key/secret, run health, execute sandbox verification, then revoke old provider credentials. |
| Certificate rotation | Rotate TLS/certificate material at ingress or provider portal, verify callback/webhook HTTPS reachability, run health, then run signed webhook verification. |
| Webhook outage | Restore public webhook route, verify Fawry delivery and signature, inspect replay records and webhook logs, and avoid manual lifecycle success. |
| Replay growth | Identify source/event type, confirm durable replay identity, keep duplicate financial effects blocked, and reprocess only through reviewed recovery tooling. |
| Recovery procedure | Reload current SQL payment authority, use original local/provider identities, execute recovery through certified application/runtime path, and quarantine ambiguous outcomes. |
| Sandbox verification | Use isolated Sandbox tenant credentials; test create, callback/webhook, duplicate, replay, timeout, failure, inquiry, and rotation scenarios. |
| Rollback | Disable tenant Fawry method and provider feature flags, remove Fawry from available methods, keep unresolved orders pending/reconciliation-required, and preserve audit/replay data. |
| Emergency disable | Disable global and tenant Fawry configuration, confirm health and metrics show traffic stopped, leave in-flight payments to webhook/recovery/reconciliation. |

## Failure Drills

Source-level drills are documented only. No live drills were executed in this phase.

| Drill | Expected behavior |
| --- | --- |
| Provider unavailable | Create/inquiry operations fail or remain pending; local system does not invent success. |
| Webhook unavailable | Payments remain pending or reconciliation-required until admitted terminal evidence arrives. |
| Runtime unavailable | Application fails closed; no lifecycle mutation occurs without required Runtime outcome. |
| Replay attack | Replay store detects repeated evidence; duplicate lifecycle mutation is blocked. |
| Expired credentials | Startup/health fails or provider failure metrics rise; no secret is logged. |
| Invalid configuration | `fawry_operational_readiness` becomes unhealthy before traffic is considered ready. |
| Duplicate webhook storm | Duplicate metrics rise; replay/audit storage collapses duplicate effects. |

## Security Review

| Area | Result |
| --- | --- |
| Credential storage | Tenant Fawry configuration remains encrypted through tenant payment-method configuration. |
| Telemetry redaction | Metrics use `tenant_bucket` and reason codes only. |
| Secret handling | Health/metrics expose booleans, environment, mode, and reason codes only. |
| Payload protection | Telemetry and readiness do not log or label provider payloads. |
| Log redaction | Operational readiness adds no payload, credential, signature, or secret log templates. |
| Artifact protection | Webhook replay and audit stores are required dependencies; operations must preserve evidence for investigation. |
| Replay protection | Readiness verifies replay storage presence; execution remains protected by durable Fawry replay records. |

Security review passed at source-operation level. Live credential permission review and provider-portal configuration remain deployment prerequisites.

## Production Checklist

Operational checklist:

| Item | Status |
| --- | --- |
| Configuration | Completed through Fawry foundation and operational readiness validation. |
| Health | Completed through `fawry_operational_readiness`. |
| Telemetry | Completed through `PointOfSale.Payments.Fawry`. |
| Alerts | Defined in Alert Matrix. |
| Runbooks | Completed in this document. |
| Recovery | Completed as documented procedure; environment validation pending. |
| Monitoring | Metrics and health names defined; dashboards and alert routes are deployment work. |
| Security | Source-level security review passed. |
| Deployment prerequisites | Migrations, credentials, callback reachability, dashboards, alerts, and sandbox evidence required per environment. |

Deployment checklist:

| Item | Status |
| --- | --- |
| Migrations applied | Required per environment, including Fawry replay storage. |
| Tenant credentials provisioned | Required before traffic. |
| Callback and webhook URLs public | Required before traffic. |
| Sandbox verification complete | Required before production. |
| Production smoke complete | Required before broad rollout. |
| Rollback switch verified | Required before production. |

Monitoring checklist:

| Item | Status |
| --- | --- |
| Health dashboard | Must include `fawry_operational_readiness`. |
| Metrics dashboard | Must include all Fawry operational counters. |
| Alert routing | Must notify the owning operations team. |
| Webhook/replay view | Must monitor rejected, replayed, duplicate, and failed webhook evidence. |
| Recovery view | Must monitor inquiry/recovery failures and unresolved payment age. |

## Certification Decision

Fawry production operational readiness is certified at source level for POS Admin and Storefront operations.

This certification does not certify a target deployment, live credentials, public ingress, Sandbox results, production traffic, CommerceHub Fawry, or active advanced-operation commands.

No new permanent cross-provider architecture truth was discovered. `.ai-context` is not modified.

## Success Answers

| Question | Answer |
| --- | --- |
| Operational readiness completed? | Yes. |
| Health monitoring certified? | Yes. |
| Startup validation certified? | Yes. |
| Observability certified? | Yes. |
| Metrics defined? | Yes. |
| Alerts defined? | Yes. |
| Runbooks completed? | Yes. |
| Failure drills documented? | Yes. |
| Security review passed? | Yes. |
| Production checklist completed? | Yes. |
| Operational readiness certified? | Yes, at source level for POS Admin and Storefront Fawry operations. |
| Recommend next phase? | Yes: execute environment-specific Sandbox validation, then production deployment validation after credentials, public ingress, dashboards, alerts, and rollback proof are available. |

# FAWRY-PRODUCTION-2 Sandbox Validation

Date: 2026-07-15

Status: SOURCE CERTIFIED WITH EXCEPTIONS; DEPLOYMENT PENDING; LIVE SANDBOX NOT CERTIFIED

Decision shorthand: Source Certified With Exceptions; Deployment Pending; Live Sandbox Not Certified.

## Validation Scope

This phase validates source readiness for official Fawry Sandbox execution for POS Admin and Storefront only.

It does not validate CommerceHub, live provider execution, public ingress, real credentials, provider-delivered callbacks, provider-delivered webhooks, or latency measurements.

No Runtime, Runtime Bridge, Application Service, Provider Adapter, Webhook Adapter, business rule, controller, frontend, persistence, provider protocol, SQL, or CommerceHub runtime change is authorized by this phase.

`PAYMENT_REFERENCE_FREEZE.md` remains the governing architecture. Fawry must adapt to the frozen Runtime; the Runtime must not adapt to Fawry.

## Source Certification

Source is certified for the following readiness controls:

| Area | Decision | Source evidence |
| --- | --- | --- |
| Sandbox configuration | Certified | `FawryOptions` separates Sandbox and Production base URLs and requires matching `SandboxMode`. |
| Environment separation | Certified | Startup validation and tenant resolver reject Sandbox/Production mismatch. |
| Tenant credential model | Certified | `FawryTenantConfigurationResolver` resolves encrypted tenant `FAWRY` configuration and does not use global credentials as tenant fallback. |
| Official callback URLs | Certified at source level | `FawryCallbackConfiguration.CallbackUrl` must be configured as an absolute server-owned URL. |
| Official webhook URLs | Certified at source level | `FawryCallbackConfiguration.WebhookUrl` must be configured as an absolute server-owned URL. |
| Payment creation | Certified at source level | POS/Admin and Storefront Fawry creation delegate through `IFawryRuntimeApplicationService`. |
| Hosted/reference payment flow | Certified at source level | Adapter creates Fawry reference-code evidence and application projects `ReferenceCode`, expiry, and optional payment URL. |
| Callback processing | Certified at source level | Fawry callback/webhook evidence enters the runtime integration and lifecycle admission path. |
| Webhook processing | Certified at source level | Controller delegates evidence evaluation to `IFawryWebhookRuntimeIntegrationService`. |
| Replay handling | Certified at source level | `FawryWebhookReplayRecords` rehydrate prior evidence and block provider-event replay. |
| Duplicate handling | Certified at source level | `PaymentWebhookLogs` duplicate/in-flight checks collapse already processed or active events. |
| Recovery | Certified at source level | Recovery remains through current SQL authority, same provider identity, Runtime integration, and single lifecycle writer. |
| Inquiry | Translation certified only | Advanced operations adapter maps official transaction inquiry; active command certification remains out of scope. |
| Refund | Translation certified only | Advanced operations adapter maps official refund; active command certification remains out of scope. |
| Capture | Translation certified only | Advanced operations adapter maps official capture; active command certification remains out of scope. |
| Void | Translation certified only | Advanced operations adapter maps official cancel/void; active command certification remains out of scope. |
| Partial refund | Translation certified only | Advanced operations adapter maps partial refund amount; active command certification remains out of scope. |
| Provider capability activation | Certified as guarded source configuration | Feature flags and capability fields must be enabled before execution; advanced operations require certified capability. |

Advanced operations adapter certified translation-only. Active refund, capture, void, inquiry, and partial refund command certification remains out of scope for this Sandbox Validation source pass. Do NOT fabricate support beyond the source-supported and officially mapped adapter capabilities.

## Deployment Prerequisites

Deployment readiness remains pending until a named environment proves:

- Fawry replay migration is applied and verified.
- Tenant Fawry Sandbox credentials are provisioned through encrypted tenant payment-method configuration.
- Global Fawry Sandbox flags and tenant `IsSandbox` agree.
- Callback and webhook URLs are public, HTTPS, stable, and registered with Fawry.
- Health checks include `fawry_foundation` and `fawry_operational_readiness`.
- Dashboards, alert routes, and ownership escalation are deployed.
- Rollback and emergency-disable switches are rehearsed.

## Sandbox Prerequisites

Live Sandbox is not certified because this workspace contains no official external evidence for:

- Sandbox merchant account.
- Sandbox credentials.
- Public callback reachability.
- Public webhook reachability.
- Official provider callback delivery.
- Official provider webhook delivery.
- Success, failure, expiration, replay, duplicate, recovery, inquiry, refund, capture, void, or partial-refund drills.
- Two-tenant isolation with separate credentials.
- Secret rotation drill.
- Backlog/replay drill.
- Latency measurements.

This is not a software failure for live-provider execution. It means the external Sandbox evidence set has not yet been collected.

## Security Review

Certified source controls:

- Tenant credentials are encrypted in tenant payment-method configuration.
- Runtime and adapters do not persist credentials.
- Runtime remains Fawry-free and provider-neutral.
- Provider evidence metadata avoids the raw secure key and raw message signature.
- Telemetry labels use `tenant_bucket`, source, operation, and reason code values rather than raw tenant IDs or secrets.
- Replay identity is tenant-scoped and backed by SQL replay records.

Architecture Exception:

- `FawryWebhookController` currently persists raw webhook payload and raw message signature in `PaymentWebhookLogs`.
- This fails complete durable artifact protection for a production Sandbox certification standard.
- No Runtime modification is required or authorized.
- Corrective work belongs to a separate webhook/persistence security closure that minimizes or hashes persisted payload/signature evidence while preserving replay, audit, recovery, and compatibility.

Security decision: source security is certified for credential protection, Runtime isolation, replay identity, telemetry redaction, and log-template posture; complete artifact protection is not certified until the raw webhook payload/signature persistence exception is closed.

## Operational Review

Health readiness is source-certified through `fawry_operational_readiness`.

Operational readiness is source-scaffolded through:

- `FawryOperationalReadinessHealthCheck`
- `FawryOperationalReadinessHealthContributor`
- `FawryOperationalTelemetry`
- documented alerts, runbooks, drills, rollback, and emergency-disable procedures

Telemetry readiness is partially certified at source level. The meter and required counters exist and are registered, but executing-path counter calls are not yet wired outside the telemetry class and Program registration. Live metrics, alert firing, and dashboard behavior therefore remain deployment/Sandbox evidence.

## Performance

No benchmarks were executed.

Performance expectations are documented through operational expectations, alert triggers, readiness checks, replay/backlog monitoring, and the requirement to capture measured create, callback, webhook, recovery, and advanced-operation latency during official Sandbox validation.

## Remaining External Evidence

The following evidence must be collected before declaring Sandbox Validated:

- official Fawry Sandbox merchant and credential proof;
- public callback and webhook reachability proof;
- provider-delivered callback and webhook samples;
- successful reference-code payment evidence;
- failure/decline or expiration evidence;
- duplicate and replay drill evidence;
- recovery/inquiry drill evidence;
- refund, capture, void, and partial-refund evidence only if active command support is separately certified and enabled;
- two-tenant isolation evidence;
- secret rotation evidence;
- health, alert, backlog, and latency evidence.

## Certification Decision

```text
SOURCE CERTIFIED WITH EXCEPTIONS
DEPLOYMENT PENDING
LIVE SANDBOX NOT CERTIFIED
```

Source readiness is certified for Fawry POS Admin + Storefront payment creation, hosted/reference payment flow, callback/webhook admission, replay, duplicate handling, recovery, tenant credential authority, environment separation, and operational readiness scaffolding.

Source readiness is not a live Sandbox certification. Deployment and live-provider evidence remain pending. Complete security certification is blocked by the raw webhook artifact persistence exception.

Recommended next phase: close the Fawry webhook artifact-protection exception, wire executing-path Fawry telemetry counters, deploy Sandbox configuration, then collect official Fawry Sandbox evidence.

## Success Answers

| Question | Answer |
| --- | --- |
| Environment validated? | Source validated; target environment pending. |
| Payment creation certified? | Yes, at source level for POS Admin + Storefront. |
| Callback certified? | Yes, at source level. |
| Webhook certified? | Yes, at source level. |
| Replay certified? | Yes, at source level. |
| Duplicate certified? | Yes, at source level. |
| Recovery certified? | Yes, at source level. |
| Inquiry certified? | Translation-only source support; active command and live Sandbox not certified. |
| Refund certified? | Translation-only source support; active command and live Sandbox not certified. |
| Capture certified? | Translation-only source support; active command and live Sandbox not certified. |
| Void certified? | Translation-only source support; active command and live Sandbox not certified. |
| Partial refund certified? | Translation-only source support; active command and live Sandbox not certified. |
| Security certified? | Partially. Credential, replay, Runtime isolation, telemetry/log redaction are source-certified; raw webhook artifact protection is not certified. |
| Performance expectations documented? | Yes. No benchmark was executed. |
| Production Sandbox certified? | No. Live Sandbox Not Certified. |
| Recommend next phase? | Yes: artifact-protection closure, telemetry wiring, deployment validation, then official Sandbox evidence collection. |

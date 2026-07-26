# MANUAL-PRODUCTION-1 Operational Readiness

Date: 2026-07-16  
Scope: Manual Payment source-level operational readiness only  
Governing architecture: `PAYMENT_REFERENCE_FREEZE.md`  
Runtime posture: Payment Runtime remains certified, provider-neutral, and frozen

## Executive Summary

MANUAL-PRODUCTION-1 certifies source-level operational readiness for Manual Payment.

The implementation adds health, telemetry source definitions, metrics registration, alert definitions, startup validation evidence, runbooks, security review, and production checklist documentation. It does not add live deployment, live Sandbox execution, SQL, persistence, approval workflow, review workflow, production evidence, Runtime behavior, Runtime Bridge behavior, Provider Adapter behavior, or Evidence Adapter behavior.

Certification decision: **Operational readiness certified at source level.**

## Operational Readiness

Certified source artifacts:

- `ManualPaymentOperationalReadinessHealthContributor`
- `ManualPaymentOperationalReadinessHealthCheck`
- `ManualPaymentOperationalReadinessValidator`
- `ManualPaymentOperationalTelemetry`
- `ManualPaymentOperationalAlerts`
- `manual_payment_operational_readiness` health registration
- `PointOfSale.Payments.ManualPayment` meter registration

This phase verifies readiness controls only. It does not certify a target deployment, live operator process, live evidence, or production traffic.

## Health

Health registration:

```text
manual_payment_operational_readiness
```

Certified checks:

- provider enabled;
- configuration valid;
- supported channels exist;
- instruction configuration valid;
- expiration policy valid;
- proof configuration valid;
- environment consistent;
- no secrets exposed;
- Runtime Bridge registration present;
- application service registration present;
- evidence application service registration present;
- provider adapter registration present;
- evidence adapter registration present;
- tenant configuration resolver registration present;
- telemetry definitions present;
- alert definitions present.

Health output exposes only safe shape data: enablement, environment, sandbox mode, channel count, proof type count, maximum upload bytes, provider code, and external-network requirement. It does not expose customer instructions, account numbers, wallet numbers, reference values, proof file names, proof content, credentials, SQL data, or Runtime artifacts.

## Telemetry

Telemetry source:

```text
PointOfSale.Payments.ManualPayment
```

Registered event names:

- `ManualPayment.Initialized`
- `ManualPayment.EvidenceSubmitted`
- `ManualPayment.RuntimeRejected`
- `ManualPayment.ConfigurationRejected`
- `ManualPayment.CapabilityRejected`
- `ManualPayment.ChannelRejected`
- `ManualPayment.EnvironmentRejected`

Telemetry uses low-cardinality tags such as `tenant_bucket`, `channel`, `operation`, `outcome`, `capability`, and `reason_code`. It does not define raw tenant identifiers, credentials, proof contents, instructions, account numbers, wallet numbers, or reusable artifacts.

## Metrics

Defined source metrics:

- `manual_payment_initializations_total`
- `manual_payment_evidence_total`
- `manual_payment_runtime_rejections_total`
- `manual_payment_configuration_failures_total`
- `manual_payment_capability_failures_total`
- `manual_payment_environment_failures_total`

The meter is registered with the OpenTelemetry metrics pipeline. This phase defines source metrics only; it does not claim deployed dashboards, alert firing, or measured production latency.

## Alerts

Defined operational alerts:

- `ManualPayment.ConfigurationInvalid`
- `ManualPayment.ProviderDisabled`
- `ManualPayment.EnvironmentMismatch`
- `ManualPayment.RepeatedRuntimeRejection`
- `ManualPayment.EvidenceAdmissionFailures`
- `ManualPayment.HighConfigurationFailures`

Alerts are source definitions. Deployment-specific thresholds, routing, paging, dashboards, and firing evidence remain environment validation tasks.

## Runbooks

Configuration validation:

1. Review `CommerceHub:Payments:ManualPayment` for enabled state, environment, sandbox mode, feature flags, instructions, proof policy, expiration policy, and channels.
2. Run startup validation and `manual_payment_operational_readiness`.
3. Roll back invalid configuration before accepting Manual Payment traffic.

Tenant enablement:

1. Confirm the tenant payment method remains enabled, non-deleted, and compatible with `MANUALWALLET` persisted compatibility.
2. Confirm provider evidence source remains `manual_payment`.
3. Do not use global fallback configuration.

Environment validation:

1. Sandbox requires `SandboxMode=true` and sandbox feature flag enabled.
2. Production requires `SandboxMode=false` and production feature flag enabled.
3. Environment mismatch must fail closed before Runtime delegation.

Capability troubleshooting:

1. Confirm provider adapter, Runtime Bridge, and evidence adapter certification flags are enabled for the source readiness profile.
2. Confirm review, approval, and persistence certifications remain disabled unless separately certified.
3. Reject unsupported channel or capability before Runtime execution.

Evidence troubleshooting:

1. Validate proof identifier and metadata are present.
2. Validate required reference number behavior.
3. Validate channel support and instruction version.
4. Treat rejected evidence as evidence only, not lifecycle finality.

Runtime troubleshooting:

1. Inspect Runtime rejection reason and canonical lineage.
2. Confirm the application service invoked the Manual Runtime Bridge only once.
3. Confirm provider/evidence adapters translated facts only.
4. Do not mutate lifecycle state outside the single lifecycle writer.

Operational recovery guidance:

1. Fail closed on ambiguous, missing, malformed, environment-mismatched, or unsupported evidence.
2. Preserve evidence for later reconciliation without exposing proof contents or sensitive artifacts.
3. Use separately certified recovery or review workflow only when implemented and certified.
4. Do not introduce SQL, approval, review, or live-operation shortcuts during incident response.

## Security Review

Source-level security checks passed:

- health output contains no instructions, proof content, account number, wallet number, credential, raw tenant id, SQL state, or Runtime artifacts;
- telemetry uses `tenant_bucket` instead of raw tenant id;
- Manual Payment has no external SDK, HTTP credential, or reusable provider secret in this phase;
- proof configuration reports only count and size limits;
- approval and review workflow remain out of scope;
- persistence remains out of scope;
- Runtime remains provider-neutral and unchanged.

## Production Checklist

Source checklist completed:

- startup validation present;
- health contributor present;
- health check registered;
- telemetry source defined;
- metrics registered;
- alert definitions present;
- runbooks documented;
- security review documented;
- architecture guards present;
- Runtime preserved;
- provider/evidence/runtime bridge behavior preserved.

Deployment checklist pending outside this phase:

- target environment health observation;
- tenant enablement evidence;
- dashboard and alert deployment;
- on-call routing and escalation;
- live failure drills;
- production evidence collection;
- approval/review workflow certification if ever activated.

No live drills were executed. No live Sandbox or production execution is certified.

## Regression

Focused certification tests:

```text
PointOfSale.Tests/Architecture/ManualPaymentProductionOperationalReadinessTests.cs
```

Coverage includes:

- health contributor and health check;
- startup validation;
- telemetry registration;
- metrics registration;
- alert definitions;
- safe health output;
- architecture guards for no Runtime, SQL, persistence, controller, approval, review, or lifecycle activation;
- documentation completeness.

## Certification Decision

| Question | Decision |
|---|---|
| Operational readiness implemented? | Yes, source-level only. |
| Health monitoring certified? | Yes. |
| Startup validation certified? | Yes. |
| Observability certified? | Yes, telemetry source definitions only. |
| Metrics defined? | Yes. |
| Alerts defined? | Yes. |
| Runbooks completed? | Yes. |
| Security review passed? | Yes, source-level. |
| Production checklist completed? | Yes for source controls; deployment controls remain pending. |
| Architecture preserved? | Yes. |
| Runtime preserved? | Yes. No Runtime change. |
| Recommend MANUAL-PRODUCTION-2? | Yes, only for separately bounded Sandbox/environment validation without claiming live operation. |

## Final Verdict

MANUAL-PRODUCTION-1 certifies Manual Payment source operational readiness only.

It does not certify live deployment, live Sandbox execution, operational execution, approval workflow, review workflow, SQL persistence, production evidence, or live operational certification.

No new permanent cross-provider architecture truth was discovered. No `.ai-context` update is required.

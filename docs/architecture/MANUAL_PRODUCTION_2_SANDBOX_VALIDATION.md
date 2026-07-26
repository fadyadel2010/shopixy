# MANUAL-PRODUCTION-2 Sandbox Validation

Date: 2026-07-16  
Scope: Manual Payment source readiness for Sandbox operation only  
Status: SOURCE CERTIFIED; DEPLOYMENT PENDING; LIVE SANDBOX NOT CERTIFIED

## Executive Summary

MANUAL-PRODUCTION-2 validates Manual Payment source readiness for Sandbox execution. This is a repository/source certification only.

Manual Payment has source-level Sandbox configuration, production isolation, tenant-scoped enablement, instruction configuration, supported-channel validation, proof metadata validation, reference policy, proof policy, expiration policy, capability validation, health, telemetry, alert definitions, and runbook coverage.

No live Sandbox traffic was executed. No deployment evidence was available. Therefore:

```text
SOURCE CERTIFIED
DEPLOYMENT PENDING
LIVE SANDBOX NOT CERTIFIED
```

This phase does not certify live deployment, live Sandbox operation, or production operation. Live success must not be inferred from source code.

NO Runtime changes. NO Runtime Bridge changes. NO Provider Adapter changes. NO Evidence Adapter changes. NO SQL. NO persistence. NO approval workflow. NO review workflow.

## Sandbox Scope

In scope:

- source-level Sandbox mode configuration;
- source-level production mode isolation;
- startup and tenant environment validation;
- Manual Payment enabled-state gates;
- instruction, reference, proof, expiration, channel, and capability configuration;
- evidence metadata validation and evidence-only flow;
- source health, telemetry, metrics, alerts, runbooks, and security review;
- architecture guards proving the frozen Runtime remains provider-neutral.

Out of scope:

- live Sandbox execution;
- live production execution;
- provider simulation;
- deployment approval;
- SQL, persistence, migrations, replay store, or lifecycle mutation;
- approval workflow, review workflow, or business-rule changes;
- controller, frontend, Runtime, Runtime Bridge, Provider Adapter, or Evidence Adapter changes.

## Environment Validation

| Control | Source decision | Evidence |
|---|---|---|
| Sandbox mode configuration | Certified at source level | `ManualPaymentOptions` defaults to `Environment=Sandbox` and `SandboxMode=true`; appsettings keeps Manual Payment disabled by default. |
| Production mode isolation | Certified at source level | Production requires `SandboxMode=false` and `ProductionEnabled=true`. Sandbox requires `SandboxMode=true` and `SandboxEnabled=true`. |
| Environment validation | Certified at source level | `ManualPaymentConfigurationValidator` rejects Sandbox/Production mode mismatch. `ManualPaymentTenantConfigurationResolver` rejects tenant `IsSandbox` mismatch. |
| Manual Payment enabled state | Certified at source level | Provider enablement and feature flags must agree before operation admission. |
| Instruction configuration | Certified at source level | Enabled provider requires configured customer instructions. Health output does not expose instruction text. |
| Supported channels | Certified at source level | Bank Transfer, Vodafone Cash, Instapay, Wallet Transfer, Cash Deposit, Custom Instructions, and Offline Invoice are declared and validated. |
| Evidence metadata validation | Certified at source level | Evidence admission requires proof identifier and proof metadata before Runtime Bridge delegation. |
| Reference policy | Certified at source level | Required references are configured and missing required references fail closed. |
| Proof policy | Certified at source level | Accepted MIME types and maximum upload size are validated. Proof content is not inspected or exposed by the evidence adapter. |
| Expiration policy | Certified at source level | Pending payment TTL and proof access TTL must be configured and positive. Expiration execution remains out of scope. |
| Capability configuration | Certified at source level | Provider adapter, Runtime Bridge, and evidence adapter capability flags gate the active source path; review, approval, and persistence capabilities remain disabled. |

## Security Review

Source-certified controls:

- no Manual Payment external SDK, external account credential, or reusable provider secret exists in this phase;
- health output reports only safe shape data: enablement, environment, sandbox mode, channel count, proof type count, maximum upload bytes, provider code, and external-network requirement;
- health output does not expose instructions, account numbers, wallet numbers, reference values, proof file names, proof content, SQL state, or Runtime artifacts;
- telemetry uses low-cardinality tags including `tenant_bucket`, `channel`, `operation`, `outcome`, `capability`, and `reason_code`;
- telemetry does not define raw tenant IDs, account numbers, wallet numbers, credentials, proof content, instructions, SQL artifacts, or Runtime artifacts;
- evidence adapter receives proof metadata only and does not read, write, persist, inspect, or transform proof content;
- Runtime source remains free of Manual Payment and Manual Wallet provider logic;
- SQL artifacts and Runtime artifacts are not exposed by health, telemetry, or metrics.

Security decision: source security is certified for Manual Payment Sandbox readiness. Live Sandbox security remains deployment-dependent because target secrets, tenant configuration, public ingress, operator process, telemetry output, dashboard behavior, and live evidence were not available.

## Operational Review

Operational readiness from MANUAL-PRODUCTION-1 is preserved.

Validated runbook coverage:

- tenant enablement: confirm enabled, non-deleted tenant `MANUALWALLET` compatibility configuration and `manual_payment` evidence source;
- environment selection: Sandbox requires `SandboxMode=true`; Production requires `SandboxMode=false`;
- configuration validation: run startup validation and `manual_payment_operational_readiness` before accepting Manual Payment traffic;
- capability validation: provider adapter, Runtime Bridge, and evidence adapter capability flags must be enabled for this source-readiness profile;
- evidence troubleshooting: validate proof identifier, proof metadata, required reference behavior, channel support, and instruction version;
- Runtime troubleshooting: verify a single Manual Runtime Bridge invocation and exact Runtime outcome admission;
- incident response: fail closed on missing, malformed, ambiguous, environment-mismatched, unsupported, or rejected evidence;
- recovery guidance: preserve evidence for separately certified reconciliation or workflow without exposing proof content or introducing SQL, approval, review, or lifecycle shortcuts.

No live drills were executed. Live alert firing, dashboard availability, on-call routing, latency, tenant enablement evidence, and operator recovery evidence remain pending.

## Deployment Classification

| Classification | Decision | Reason |
|---|---|---|
| Source readiness | Certified | Source configuration, validation, evidence flow, security boundaries, operational readiness, and architecture guards are present. |
| Deployment readiness | Pending | No named deployment health, tenant enablement, alert routing, dashboards, operator ownership, or rollback rehearsal evidence is available. |
| Environment readiness | Pending | No target Sandbox tenant, target configuration dump, deployed health result, or secret-management evidence is available. |
| Live Sandbox readiness | Not certified | No live Manual Payment Sandbox execution, operator proof flow, public ingress, tenant isolation drill, incident drill, or latency evidence was collected. |
| Live Production readiness | Not certified | Production execution is explicitly outside this phase and requires separate deployment and production certification evidence. |

The classifications are separate and must not be merged. Source readiness does not imply deployment readiness, environment readiness, live Sandbox readiness, or production readiness.

## Regression

Focused certification tests:

```text
PointOfSale.Tests/Architecture/ManualPaymentProduction2SandboxValidationTests.cs
```

Coverage includes:

- Sandbox configuration;
- production isolation;
- environment validation;
- Manual Payment enabled state;
- instruction, supported-channel, reference, proof, expiration, and capability configuration;
- evidence metadata validation and evidence-only flow;
- safe health, telemetry, and metrics boundaries;
- operational boundary preservation;
- deployment classification language;
- architecture guards for no Runtime, SQL, persistence, approval, review, controller, lifecycle, Provider Adapter, Evidence Adapter, or Runtime Bridge changes.

Recommended execution:

```text
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~ManualPaymentProduction2SandboxValidationTests -nologo -v minimal --no-restore
```

## Certification Decision

```text
SOURCE CERTIFIED
DEPLOYMENT PENDING
LIVE SANDBOX NOT CERTIFIED
PRODUCTION NOT CERTIFIED
```

Manual Payment source readiness for Sandbox execution is certified. Environment and configuration validation are certified at source level. Evidence flow is certified at source level as evidence-only metadata admission into the existing Manual Payment application/evidence/Runtime Bridge path. Security is certified at source level. Operational readiness from MANUAL-PRODUCTION-1 is preserved.

Deployment evidence is unavailable. Live Sandbox evidence is unavailable. Production evidence is unavailable. No unsupported live-operation claim is made.

## Architecture Exception

None.

No new permanent cross-provider architecture truth was discovered. No `.ai-context` update is required. `PAYMENT_REFERENCE_FREEZE.md` remains authoritative. Manual Payment continues to follow the same production governance model used for Stripe, Paymob, and Fawry.

## Success Answers

| Question | Answer |
|---|---|
| Sandbox validation implemented? | Yes, source-level Sandbox Validation certification is implemented. |
| Environment validated? | Yes, at source level. Target environment validation remains pending. |
| Configuration validated? | Yes, at source level for Sandbox mode, production isolation, enabled state, instructions, channels, proof, reference, expiration, and capabilities. |
| Evidence flow validated? | Yes, at source level. Evidence remains evidence-only and proof content is not exposed. |
| Security certified? | Yes, source-level security certified. Live Sandbox security remains deployment-dependent. |
| Operational readiness preserved? | Yes. MANUAL-PRODUCTION-1 source operational readiness remains preserved. |
| Deployment readiness classified? | Yes. Source readiness, deployment readiness, environment readiness, live Sandbox readiness, and live production readiness are classified separately. |
| Live Sandbox certified? | No. LIVE SANDBOX NOT CERTIFIED. |
| Production certified? | No. Production is not certified. |
| Architecture preserved? | Yes. No Runtime, SQL, persistence, workflow, controller, lifecycle, adapter, or bridge change is authorized. |
| Runtime preserved? | Yes. Runtime remains provider-neutral and unchanged. |
| Recommend MANUAL-FINAL-RECERTIFICATION? | Yes. Recommend MANUAL-FINAL-RECERTIFICATION after this source-only Sandbox Validation record, without claiming live Sandbox or production certification. |

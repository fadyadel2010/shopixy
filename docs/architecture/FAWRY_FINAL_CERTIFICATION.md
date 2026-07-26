# Fawry Final Certification

**Review:** FAWRY-FINAL-RECERTIFICATION — Final Independent Certification Board Review  
**Audit date:** 2026-07-15  
**Audit mode:** Current-source audit only; no implementation, Runtime, architecture, or `.ai-context` changes  
**Supreme authority:** `PAYMENT_REFERENCE_FREEZE.md`  
**Reference baseline:** the bounded Stripe and Paymob reference-provider certifications  
**Certified Fawry scope:** active POS/Admin and Storefront PayAtFawry reference creation, signed charge-response admission, signed webhook evidence admission, replay, checkpoint recovery, and lifecycle handoff  
**Excluded scope:** CommerceHub and inactive refund, partial-refund, capture, void, and inquiry command execution

## Executive Summary

The current Fawry software is **certified** for the bounded reference-provider
scope above.

The six High source findings in the original final recertification are closed.
Closures 1–5 remain effective. Current source evidence additionally proves that
Closure 6 changed credential transport to a one-shot, consume-and-clear handoff
and that Closure 7 prevents non-success provider response bodies from crossing
the active adapter boundary. No new source evidence reopens any earlier closure.

The current implementation preserves the frozen payment architecture:

- application code owns tenant authority, credential resolution,
  orchestration, durable checkpoints, recovery policy, and provider invocation;
- the active provider adapter owns only Fawry protocol construction,
  transport, response verification, and canonical translation;
- provider webhook input is admitted only as authenticated evidence;
- the provider-neutral Runtime remains free of Fawry, HTTP, credentials, SQL,
  EF Core, controller, or frontend responsibilities;
- `PaymentLifecycleService` remains the single durable lifecycle writer; and
- durable started checkpoints, accepted-reference recovery, replay evidence,
  and tenant-scoped unique keys prevent blind provider recreation and duplicate
  lifecycle effects.

There are no current software-certification blockers. The remaining findings
are only Operational, Deployment, External, Historical, or Out of Scope. Under
the mandatory certification rule, they cannot withhold software certification.
They do remain release gates where identified.

## Certification Board

### Authority and method

This independent review applied `PAYMENT_REFERENCE_FREEZE.md` as the supreme
authority and used the same bounded standard as the Stripe and Paymob reference
baselines. It audited the current working tree, not a reconstructed historical
revision. Historical findings were retained as closed unless current executable
source supplied new regression evidence.

The review covered architecture, Runtime boundaries, application ownership,
provider and webhook adapters, application contracts, checkpointing, replay,
recovery, duplicate prevention, credential authority and handoff, response
integrity, official API compliance, artifact protection, provider-error
redaction, operations, sandbox readiness, regression, migration, deployment,
and documentation.

### Governance corpus reviewed

All seven current `.ai-context` files were read:

- `ARCHITECTURE_TRUTH.md`
- `CODEX_RULES.md`
- `CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `KNOWN_RISKS.md`
- `LESSON_UPDATE_RULES.md`
- `LOCAL_DEV_STRIPE_HANDOFF_CERTIFICATION_LESSON.md`
- `PROJECT_CONTEXT.md`

The required cross-provider authorities were read:

- `PAYMENT_REFERENCE_FREEZE.md`
- `PAYMENT_RUNTIME_FINAL_CERTIFICATION.md`
- `STRIPE_FINAL_RECERTIFICATION.md`
- `PAYMOB_FINAL_RECERTIFICATION.md`

All Fawry architecture and certification records in `docs/architecture` were
read, including discovery, authority certification, Foundation, Provider
Adapter, Runtime Bridge, Webhook Evidence Adapter, Advanced Operations,
Contract Closures 1–2, Convergence, Production Readiness, Sandbox Validation,
the original and updated final recertifications, and Certification Closures
1–7.

No new permanent cross-provider architectural truth was discovered. No
`.ai-context` update is warranted.

### Official protocol baseline

The active source was compared with Fawry's current official staging
documentation for [PayAtFawry reference creation](https://developer.fawrystaging.com/docs/server-apis/create-payment-refno-apis),
[server notification V2](https://developer.fawrystaging.com/docs/card-tokens/payment-notifications/server-notification-v2),
and [payment-status V2](https://developer.fawrystaging.com/docs/sdks/payment-notifications/get-payment-status-v2).
The active create and callback paths use the documented ordered SHA-256
contracts, official field names, numeric merchant reference, two-decimal amount,
PayAtFawry method, signed response fields, and notification
`messageSignature`. Status inquiry remains translation-only and is not claimed
as an active certified command.

## Closed Findings

### Final recertification findings

| Previous finding | Status | Current evidence |
|---|---|---|
| FAWRY-FINAL-001 — active charge request did not satisfy the official PayAtFawry contract | Resolved | Closure 2 remains present. `FawryPaymentAdapter.cs:23-43,113-160` posts the official endpoint and constructs `merchantCode`, numeric `merchantRefNum`, customer fields, PayAtFawry method, two-decimal amount, description, language, items, and ordered signature. Current contract tests pass. |
| FAWRY-FINAL-002 — charge-response integrity and exact outcome admission were absent | Resolved | Closure 3 remains present. `FawryPaymentAdapter.cs:67-75,402-504` requires accepted status, reference, merchant-reference correlation, signed amounts/method/status/customer fields, fixed-time signature equality, and exact request correlation before evidence is created. |
| FAWRY-FINAL-003 — no durable mutation-started checkpoint prevented blind recreation | Resolved | Closure 5 remains present. `FawryProviderCheckpointStore.cs:35,100-108,158-205` commits `charge_registration_started` before provider I/O, records accepted reference separately, and fails closed on ambiguous started state. The application service and bridge are invoked only after that checkpoint. |
| FAWRY-FINAL-004 — webhook credential/capability authority bypassed the shared resolver | Resolved | Closure 4 remains present. `FawryTenantConfigurationResolver.cs:58-137` resolves tenant-scoped enabled/non-deleted method configuration, capability, environment, active webhook tenant, and protected credentials with no global fallback; the webhook controller uses that authority before evidence admission. |
| FAWRY-FINAL-005 — credential handoff was reusable | Resolved | Closure 6 is present. `FawryRuntimeBridge.cs:64-66,190-205` consumes once using atomic compare/exchange, atomically clears credential storage, and rejects reuse/concurrent reuse. Tenant/action scope is validated before the adapter receives credentials. Closure 6 tests pass. |
| FAWRY-FINAL-006 — canonical errors could retain provider response values | Resolved | Closure 7 is present. `FawryPaymentAdapter.cs:48-52,230-250` never reads a non-success HTTP body and emits only status-derived canonical fields plus a fixed diagnostic. Closure 7 tests pass. |

### Earlier certification findings and assertions

| Previous finding or assertion | Status | Current evidence |
|---|---|---|
| Discovery-era simulated outbound registration/reference generation | Resolved | The active adapter performs the official HTTPS charge request and admits only integrity-verified provider evidence. |
| Discovery-era custom request signature/field contract | Resolved | Closure 2 request factory and current tests implement the official ordered contract. |
| Discovery-era custom webhook field/signature contract | Resolved | The evidence adapter parses official notification V2 fields and verifies `messageSignature` in fixed time at `FawryWebhookEvidenceAdapter.cs:324-345,448-481`. |
| Missing durable webhook replay history | Resolved | `FawryWebhookRuntimeIntegrationService.cs:205-218,425-454`, `ApplicationDbContext.cs:785-803`, and migration `20260715120000_FawryContractClosure2WebhookReplay` provide durable prior evidence and tenant-scoped replay keys. |
| Durable raw webhook payload and raw signature artifacts | Resolved | Closure 1 remains present. `FawrySensitiveArtifactProtector.cs:34-67,81-94` recursively removes raw signatures, encrypts payload evidence, and stores a SHA-256 signature digest. |
| Legacy direct POS/Storefront provider creation path | Resolved | The active path is application service → checkpoint store → Runtime bridge → adapter; provider execution is not owned by frontend or Runtime. Storefront production build passes. |
| Provider protocol leaked into Runtime | False Positive | Current Runtime source scan found no Fawry/provider HTTP, credential, EF Core, SQL, controller, or frontend dependency. Runtime tests are green. |
| Provider or webhook controller became a second lifecycle writer | False Positive | `FawryWebhookController.cs:199-214` delegates admitted persistence to `IPaymentLifecycleService`; adapter and Runtime do not write durable lifecycle state. |
| FAWRY-FINAL-012 — Runtime SQL, provider-owned orchestration, Runtime modification, or a duplicate active lifecycle writer was introduced | False Positive | Current Runtime remains a pure provider-neutral decision boundary; all tenant/configuration/checkpoint/transport/persistence code is outside Runtime, and lifecycle persistence still delegates to `PaymentLifecycleService`. |

## Remaining Findings

No remaining finding blocks Architecture, Implementation, Security, Regression,
Reference Provider, or Software certification.

| Current finding | Status | Evidence and bounded effect |
|---|---|---|
| FAWRY-FINAL-007 — authenticated terminal-negative callbacks are identified but skipped rather than converged to a terminal lifecycle result | Operational | `FawryWebhookRuntimeIntegrationService.cs:398-419` remains fail-closed: negative evidence cannot create a false paid result or a second writer. Inquiry/reconciliation procedures must supply terminal-negative recovery before live operations. |
| FAWRY-FINAL-008 — executing-path metrics are declared but not called, and configured `RetryCount` is not applied | Operational | Telemetry is registered/exported but executing-path calls are absent. Blind retry of a non-idempotent charge after `RegistrationStarted` would be unsafe; retry must be inquiry/reconciliation-aware. This is an operational runbook/observability gate, not an authority defect. |
| FAWRY-FINAL-009 — EF model snapshot and target database are not converged | Deployment | EF discovers `20260715120000_FawryContractClosure2WebhookReplay`, but `dotnet ef migrations has-pending-model-changes` reports current model drift and the snapshot lacks the Fawry replay entity. The actual target migration state also remains unproved. |
| FAWRY-FINAL-010 — official live Sandbox and operational evidence are absent | External | No named sandbox merchant execution, provider-delivered signed callback, duplicate delivery, timeout/ambiguous acceptance, replay, expiration, or recovery drill was supplied. |
| FAWRY-FINAL-011 — CommerceHub and advanced-operation commands are inactive | Out of Scope | Refund, partial refund, capture, void, and inquiry code remains translation-only with no production caller. It is not part of the bounded POS/Admin + Storefront reference-creation/webhook certification. |
| Current local PostgreSQL schema is behind current source | Deployment | Four reliability tests fail before Fawry execution because local `Orders` lacks `BillingAddressAdmissionEvidenceId`. This is target/local schema drift, not Fawry behavior evidence. |
| Legacy authority tests assert pre-closure source strings and context markers | Historical | Three source-scanning assertions expect obsolete strings or `PASS SF-PAY7`; they do not execute the current official provider path. |
| One pre-Closure-6 Closure-3 fixture omits tenant metadata now required by the credential handoff | Historical | The old fixture reaches `FAWRY_CREDENTIAL_HANDOFF_TENANT_MISSING` before its intended amount mismatch. Production request construction supplies the tenant, and current Closure 3/6 behavior is covered by the green current-contract slice. |
| Storefront browser-data, import, and bundle-size warnings | Operational | The production build succeeds. The warnings do not alter Fawry authority or payment behavior. |

## Reclassified Findings

| Item | Previous treatment | Current status | Reason |
|---|---|---|---|
| FAWRY-FINAL-005 | High / Still Valid in updated recertification | Resolved | Closure 6 supplies atomic one-shot consume-and-clear with tenant/action validation and passing concurrency/reuse tests. |
| FAWRY-FINAL-006 | High / Still Valid in updated recertification | Resolved | Closure 7 discards the non-success body before canonical translation; no arbitrary non-success-body value crosses the active boundary. |
| Terminal-negative convergence | Medium implementation/recovery concern | Operational | Current behavior is deliberately fail-closed and cannot assert false success; recovery activation is an operational completeness matter. |
| Metrics and retry configuration | Operational / Deployment | Operational | Observability and inquiry-aware retry evidence remain necessary for live operation but do not change payment authority. |
| Replay migration snapshot/target state | Migration concern | Deployment | Source migration/table/index intent is present and discoverable; snapshot and deployed schema convergence are release tasks. |
| Four reliability failures | Product regression candidate | Deployment | All stop during unrelated local schema seeding, before a Fawry path executes. |
| Legacy string/marker test failures | Product regression candidate | Historical | Assertions describe an older implementation/document state and conflict with the closed official-contract source. |
| Live sandbox evidence | Certification evidence gap | External | Provider credentials, provider delivery, and external events cannot be manufactured by source review. |
| Translation-only advanced operations, including their dormant error parsing | Provider completeness concern | Out of Scope | There is no application-owned production command path. Activation requires a separate certification and must not copy dormant boundary behavior without review. |
| Runtime/lifecycle/provider-leakage allegations | Architecture concern | False Positive | Current source responsibilities and passing Runtime tests show no such coupling or second writer. |

## Regression Summary

### Verification performed

| Verification | Result | Board interpretation |
|---|---:|---|
| Combined `FullyQualifiedName~Fawry\|FullyQualifiedName~PaymentRuntime` aggregate | 238 passed / 8 failed / 246 total | The eight failures are fully classified above: four Deployment, three Historical source assertions, and one Historical fixture. No new active-source blocker was found. |
| Current-contract Fawry slice excluding the known reliability class, legacy authority class, and obsolete fixture | 143 passed / 0 failed | Active request, response, webhook, credential, checkpoint, replay, artifact, convergence, and source-readiness contracts are green. |
| Closure 6 + Closure 7 + Payment Runtime focused slice | 104 passed / 0 failed | The two formerly open High findings are closed and Runtime behavior remains green. |
| Payment Runtime tests within the focused runs | 94 passed / 0 failed | Provider-neutral deterministic Runtime behavior is preserved. |
| .NET Core/Infrastructure/API/Test compilation during test execution | Succeeded | Current backend source compiles. |
| Storefront `npm run build` | Succeeded | TypeScript and Vite production output complete; warnings are operational only. |
| EF migration discovery (`migrations list --no-connect --no-build`) | Fawry replay migration listed | Migration source is discoverable. |
| EF pending-model check | Failed: pending model changes | Confirms the Deployment finding; it does not show a Fawry execution or Runtime regression. |

### Regression determinations

- Runtime regression: none found.
- Architecture regression: none found.
- Provider-protocol leakage: none found.
- Credential leakage in the active scope: none found.
- Durable artifact leakage: none found.
- Response-integrity regression: none found.
- Replay regression: none found in current source or applicable tests.
- Checkpoint regression: none found.
- Duplicate provider-execution regression: none found; started-state recovery
  blocks blind charge recreation.
- Duplicate lifecycle-effect regression: none found; tenant-scoped replay/log
  uniqueness and the lifecycle service boundary remain intact.
- Closed Closure 1–7 regression: none found.

## Architecture Summary

| Required confirmation | Decision | Evidence basis |
|---|---|---|
| Runtime unchanged | Yes | No Fawry closure requires a Runtime responsibility change; current Runtime content remains provider neutral and its 94 tests pass. A byte-for-byte tracked baseline is unavailable because the current Runtime tree is not represented as a clean tracked baseline in this working tree; that repository-state limitation does not alter the current-source responsibility audit. |
| Runtime remains provider-neutral | Yes | No provider, HTTP, credential, SQL, EF Core, controller, or frontend concern was found in Runtime. |
| Application owns orchestration | Yes | Tenant resolution, credentials, checkpoint state, provider invocation, replay, recovery, and lifecycle delegation live in application/infrastructure boundaries. |
| Lifecycle remains single writer | Yes | All admitted Fawry finality delegates to `PaymentLifecycleService`; adapters and Runtime return evidence/decisions only. |
| No provider protocol leakage | Yes | Official Fawry JSON, endpoints, signatures, and status mapping remain inside provider/webhook boundaries. |
| No credential leakage | Yes | Credentials are tenant scoped, decrypted only at the application boundary, handed off once, cleared atomically, and never passed to Runtime or durable evidence. |
| No artifact leakage | Yes | Webhook payloads are signature-scrubbed and protected; signatures are digested; active non-success response bodies are discarded. |
| No replay regression | Yes | Durable replay history and exact tenant/provider/event uniqueness remain present. |
| No checkpoint regression | Yes | The committed started checkpoint precedes external mutation; accepted reference and completion are separate durable states. |
| No duplicate provider execution | Yes | Retries encountering ambiguous started state fail closed to inquiry/reconciliation rather than issuing a second create. |
| No architecture regression | Yes | The frozen application → adapter → evidence → Runtime → lifecycle-service → SQL sequence is preserved. |

The current tree therefore preserves the provider-neutral Runtime and freezes the
reference architecture. “Runtime unchanged” here certifies unchanged authority
and current-source boundaries; it does not claim a VCS byte identity that this
dirty/untracked working-tree baseline cannot independently establish.

## Deployment Summary

Software certification is complete. Deployment is not yet approved.

Before a governed live release, the deployment owner must:

1. reconcile the EF model snapshot/pending-model state and prove the exact
   target database migration state;
2. configure and validate tenant-scoped sandbox credentials, endpoint,
   callback reachability, and HTTPS/TLS behavior;
3. collect official sandbox evidence for create, signed response, signed
   callback, duplicate callback, ambiguous acceptance, replay, terminal
   negative, expiration, and recovery;
4. connect executing-path telemetry and alerts; and
5. document inquiry/reconciliation-aware recovery rather than blind mutation
   retry.

These are Deployment, Operational, and External gates. They do not require a
Runtime or architectural change and do not withhold software certification.

## Reference Provider Decision

Fawry is certified as a Reference Provider for future payment-provider work in
the bounded POS/Admin + Storefront reference-payment scope.

Future providers may reuse the architectural pattern:

`application authority → durable pre-mutation checkpoint → one-shot credential
handoff → provider adapter → authenticated canonical evidence → provider-neutral
Runtime → exact outcome admission → PaymentLifecycleService → SQL`

They must not copy Fawry wire fields, signatures, status semantics, dormant
advanced-operation code, or recovery assumptions. Those remain provider
specific and require their own official-contract and sandbox certification.

## Final Certification Matrix

| Certification question | Decision | Basis |
|---|---|---|
| Architecture certified? | Yes | Frozen ownership and authority boundaries are preserved. |
| Implementation certified? | Yes | The active bounded implementation closes all current source blockers. |
| Security certified? | Yes | Credential, signature, response-integrity, artifact, and error-redaction boundaries are source-certified for the active scope. |
| Regression certified? | Yes | Applicable active-contract and Runtime slices are green; all broad-suite failures are nonblocking classified evidence. |
| Migration certified? | No — Deployment | Migration source exists and is discoverable, but snapshot/target convergence remains a release gate. |
| Operations certified? | No — Operational | Live telemetry, recovery procedure, and operational drills remain pending. |
| Reference Provider certified? | Yes | Certified for active POS/Admin + Storefront reference creation and webhook admission. |
| Provider-neutral Runtime preserved? | Yes | Runtime remains provider neutral and 94/94 Runtime tests pass. |
| Architecture frozen? | Yes | No architecture or `.ai-context` update is required. |
| Software certified? | Yes | No Critical, High, or other software-authority blocker remains. |
| Deployment pending only? | Yes | Every remaining item is Deployment, Operational, External, Historical, or Out of Scope. |
| Live Sandbox pending only? | No | Live sandbox is external and pending, and schema/observability deployment gates also remain. |
| Ready as Reference Provider for future payment providers? | Yes | Reuse the frozen pattern within the stated scope, not Fawry-specific protocol details. |

## Final Decision

**CERTIFIED**

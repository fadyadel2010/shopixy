# Manual Payment Final Certification

Date: 2026-07-16  
Audit mode: independent current-source certification; no implementation, Runtime, migration, test, or `.ai-context` changes  
Supreme authority: `PAYMENT_REFERENCE_FREEZE.md`  
Reference baseline: Stripe, bounded Paymob, and Fawry final provider records  
Requested scope: POS Admin and Storefront Manual Payment / Manual Wallet  
Excluded scope: Marketplace and CommerceHub payment flows  
Controlling decision: **MANUAL PAYMENT SOFTWARE AND REFERENCE PROVIDER CERTIFICATION WITHHELD**

## Executive Summary

The Manual Payment foundation, provider adapter, Runtime bridge, evidence
adapter, application services, and initialization/evidence convergence are real
and preserve the provider-neutral Payment Runtime. They do not certify the
active Manual Payment workflow end to end.

Current source exposes proof upload, operator approval, operator rejection,
expiration configuration, proof retention, and checkout recovery beyond the
limited responsibilities certified by the earlier phase records. Those active
responsibilities do not all pass through one Manual Payment application-owned
authority with current capability admission, canonical review evidence, exact
Runtime outcome validation, durable replay/recovery facts, and enforced
retention/expiration policy.

Nine Blocking High source findings remain:

- operator approval and rejection bypass the Manual Payment application and
  Runtime evidence path;
- active operation endpoints do not enforce the declared feature and workflow
  certification gates;
- pending-payment expiration is not executed or enforced before approval;
- checkout recovery selects Manual Wallet work but cannot resume the selected
  incomplete intent and refuses to recreate a missing intent;
- configured proof retention is not authoritative and proof access does not
  fail closed at expiry;
- canonical evidence and admission facts required for durable audit and replay
  are not persisted;
- operational telemetry and readiness do not observe or prove the executing
  workflow;
- Manual `PaymentIntent` creation and the `AwaitingVerification` transition are
  persisted outside the frozen lifecycle writer; and
- disconnected and competing migration artifacts do not provide one safe,
  EF-governed upgrade path.

The broad Manual Payment / Manual Wallet / Payment Runtime regression aggregate
is also not green: **215 passed and 5 failed**. The frozen Payment Runtime slice
is independently green at **101/101**, the API and both frontend production
builds pass, and the POS Admin Manual Payments frontend test passes **5/5**.

The final posture is:

```text
PAYMENT RUNTIME: CERTIFIED, PROVIDER-NEUTRAL, AND FROZEN
PAYMENT LIFECYCLE TERMINAL OPERATOR-OUTCOME WRITER: PRESERVED
PAYMENT LIFECYCLE FULL SINGLE-WRITER RULE: NOT PRESERVED
MANUAL PAYMENT LAYER ARTIFACTS: VERIFIED ONLY FOR THEIR STATED LIMITED SCOPES
MANUAL PAYMENT ARCHITECTURE: NOT CERTIFIED OR FROZEN
MANUAL PAYMENT SOFTWARE: NOT CERTIFIED
MANUAL PAYMENT REFERENCE PROVIDER: NOT CERTIFIED
MANUAL PAYMENT DEPLOYMENT: NOT READY
MANUAL PAYMENT SANDBOX: NOT VALIDATED
MANUAL PAYMENT PRODUCTION OPERATION: NOT CERTIFIED
MARKETPLACE / COMMERCEHUB: OUT OF SCOPE AND NOT CERTIFIED HERE
```

## Audit Basis

The board reviewed the complete current Manual Payment record, including
discovery, Foundation, Provider Adapter, Runtime Bridge, Evidence Adapter,
Advanced Operations, Contract Closures 1-2, Convergence, Production Operational
Readiness, Sandbox Validation, and the prior Manual Payment authority finding.
Earlier phase records were treated as bounded claims requiring verification
against current executable source.

The board also reviewed:

- all seven current `.ai-context` governance files;
- `PAYMENT_REFERENCE_FREEZE.md` and
  `PAYMENT_RUNTIME_FINAL_CERTIFICATION.md`;
- `STRIPE_FINAL_RECERTIFICATION.md`,
  `PAYMOB_FINAL_RECERTIFICATION.md`, and
  `FAWRY_FINAL_CERTIFICATION.md`;
- active POS Admin and Storefront clients and API controllers;
- Manual Payment application services, adapters, Runtime bridge, options,
  readiness, telemetry, persistence, lifecycle, cleanup, expiration, replay,
  and recovery paths;
- EF model configuration, snapshot, migrations, and standalone SQL artifacts;
- focused architecture, application, integration, cleanup, frontend, and
  Payment Runtime tests; and
- current API, POS Admin, and Storefront build results.

No new permanent cross-provider architecture truth was discovered. The current
defects are Manual Payment implementation and certification gaps governed by the
existing freeze, so `.ai-context` is not modified.

## Scope And Boundary

### In scope

- Storefront Manual Wallet method selection and configuration presentation;
- checkout-session proof upload, clear, retry, and proof-key admission;
- SQL `PaymentIntent` creation and initialization;
- canonical instruction and proof evidence translation;
- Manual Payment Runtime bridge and exact initialization/evidence result use;
- pending operator-review presentation;
- POS Admin proof retrieval, approval, and rejection;
- duplicate, replay, concurrency, recovery, expiration, retention, audit,
  telemetry, readiness, and migration behavior supporting those operations.

### Out of scope

- Marketplace and CommerceHub payment execution;
- translation-only advanced adapter operations for cancellation, expiration,
  payment/evidence inquiry, and capability discovery; expiration execution
  itself remains in scope;
- unsupported or out-of-scope adapter operations for refund, partial refund,
  capture, settlement, evidence withdrawal, and adapter-owned approval/review
  execution;
- a target environment, real operator roster, external object-store account,
  deployed dashboards, or live transaction evidence.

The Marketplace / CommerceHub exclusion is a separate bounded-context decision.
It does not weaken the POS Admin and Storefront findings, and this record does
not certify Marketplace Manual Payment behavior.

## Architecture Review

### Preserved partial architecture

The inspected initialization and receipt-submission path follows the required
direction through exact Runtime outcome interpretation, then diverges at durable
persistence:

```text
Checkout / PaymentService
  -> ManualPaymentRuntimeApplicationService
  -> ManualPaymentRuntimeBridge
  -> ManualPaymentProviderAdapter
  -> canonical action/evidence
  -> PaymentRuntimeOrchestrator
  -> exact application result interpretation
  -> PaymentService direct lifecycle persistence (nonconforming)
```

Verified properties:

- Runtime source remains provider neutral and contains no Manual Payment,
  controller, frontend, SQL, EF Core, storage, or credential responsibility.
- `PaymentRuntimeOrchestrator` remains the canonical Runtime stage sequencer.
- the Manual Payment adapter and bridge translate canonical instructions and
  receipt evidence without writing SQL lifecycle state;
- `PaymentService` requires both Manual Payment application services and routes
  initialization and evidence through them at
  `PaymentService.cs:2557-2637`;
- tenant method, global enablement, channel, environment, and certified adapter
  capabilities are checked in the initialization/evidence application path at
  `ManualPaymentRuntimeApplicationService.cs:59-171`; and
- active approval/rejection routes ultimately delegate terminal outcome
  mutation to `PaymentLifecycleService`; the controllers are not independent
  SQL writers.

### Architecture not yet preserved end to end

The frozen reference requires Manual receipt submission, operator review,
approval, rejection, expiration, and reconciliation to use a workflow adapter
and canonical evidence. Operator action is evidence and policy input, not a
direct controller-to-lifecycle command.

Current operator flow is instead:

```text
POS Admin
  -> AdminManualPaymentController
  -> PaymentLifecycleService
  -> PostgreSQL
```

An additional active route family is:

```text
authorized caller
  -> PaymentLifecycleController wallet-approve / wallet-reject
  -> PaymentLifecycleService
  -> PostgreSQL
```

Neither path enters a Manual Payment review application service, review evidence
adapter, Runtime bridge, or exact Runtime outcome admission. The second route
family is also actively advertised by the lifecycle actions endpoint even
though no checked-in frontend caller was found.

Architecture decision: **Withheld.**  
Provider-neutral Runtime decision: **Preserved and frozen.**  
Terminal operator-outcome writer decision: **Preserved.**  
Full single SQL lifecycle writer decision: **Not preserved.**

## Findings Matrix

| ID | Classification | Affected decision | Evidence-based finding |
|---|---|---|---|
| MANUAL-FINAL-001 | Blocking High | Architecture, Implementation, Reference Provider, Software | `AdminManualPaymentController.cs:137-216,266-346` and `PaymentLifecycleController.cs:132-189` send operator outcomes directly to `PaymentLifecycleService`. No Manual review application service, workflow evidence adapter, Runtime execution, or exact Runtime outcome admission governs approval/rejection. |
| MANUAL-FINAL-002 | Blocking High | Architecture, Security, Implementation, Software | `ManualPaymentOptions.cs:28-32,193-210` declares proof-upload, review, approval, rejection, and expiration gates, but controller source has no `ManualPaymentOptions` consumer. Checked-in flags are false while upload/clear and both mutation route families remain executable. |
| MANUAL-FINAL-003 | Blocking High | Implementation, Recovery, Software | Initialization persists `ExpiresAtUtc`, but no Manual expiration worker consumes it. Pending review and approval do not reject elapsed intent expiry, and the state machine cannot expire `AwaitingVerification`. An expired receipt can remain reviewable and can be approved. |
| MANUAL-FINAL-004 | Blocking High | Recovery, Regression, Software | `CheckoutPaymentIntentRecoveryService.cs:77-101` selects pending Manual Wallet intents as incomplete, while lines 426-440 declare every Manual Wallet intent complete. Lines 416-423 reject missing Manual Wallet intent recovery although lines 265-274 contain an unreachable creation branch. |
| MANUAL-FINAL-005 | Blocking High | Security, Data Retention, Software | `ProofAccessTtl` is validated but does not govern persistence or access. Pending proofs receive no bounded proof expiry, terminal retention uses hard-coded durations, and proof download does not deny access when `ProofImageExpiresAtUtc` has elapsed or `ProofImageDeleted` is true. |
| MANUAL-FINAL-006 | Blocking High | Audit, Replay, Recovery, Reference Provider, Software | The evidence adapter computes canonical evidence identity/hash, but durable `PaymentIntent` state does not retain that identity/hash, proof MIME/size/version, instruction version, evidence submission time, or admission checkpoint. Later replay/audit cannot prove the canonical receipt admitted into `AwaitingVerification`. |
| MANUAL-FINAL-007 | Blocking High | Source Operational Readiness, Reference Provider, Software | `ManualPaymentOperationalTelemetry` is registered, but its six `Record*` methods have no production caller. Readiness checks configuration shape/DI rather than executing dependencies, requires workflow certifications to remain false, and can make global readiness unhealthy when the optional provider is intentionally disabled. |
| MANUAL-FINAL-008 | Blocking High | Architecture, Implementation, Single Writer, Software | `PaymentService.cs:288-385` directly creates and commits the Manual `PaymentIntent`; lines 2557-2636 directly assign `AwaitingVerification`, reference, expiry, and provider identity after Runtime work, and lines 2110-2172 save that state. The frozen lifecycle writer does not own the complete Manual lifecycle. |
| MANUAL-FINAL-009 | Blocking High | Migration, Source Operational Readiness, Software | EF reports pending model changes. Two standalone `[Migration]` classes are absent from EF discovery; separate SQL paths can normalize proof references less safely or stamp migration history while deleting duplicate intents. Source does not provide one safe, EF-governed, tested upgrade path. |
| MANUAL-FINAL-010 | Non-blocking Improvement | Regression quality | Several certification tests assert source strings rather than executable behavior. Current tests do not prove request-time capability denial, expiration execution, expired-proof access denial, pending-intent recovery, emitted metrics, or the absence/parity of alternate lifecycle routes. This compounds but does not create the source blockers above. |
| MANUAL-FINAL-011 | Operational / Deployment Only | Deployment, Environment | Checked-in Manual Payment configuration is intentionally disabled, operation flags are false, channels/instructions are absent, and no named target tenant, applied migration state, storage dependency, operator process, alerts, drills, measured latency, rollback, or production approval is verified. |
| MANUAL-FINAL-012 | External Dependency | Environment, Sandbox, Production | No named external private-object-storage account/service and service-behavior evidence is available. Manual Payment has no external processor credential, but external storage availability and isolation must still be validated in the target environment. |
| MANUAL-FINAL-013 | Out of Scope | Marketplace / CommerceHub | Marketplace and CommerceHub are excluded from this bounded POS Admin and Storefront certification and receive no implied pass. |
| MANUAL-FINAL-014 | False Positive | Controller writer assessment | The active controllers are not themselves direct SQL lifecycle writers: they delegate terminal outcomes to `PaymentLifecycleService`, which validates Manual Wallet intent/state/proof. This does not clear the separate full-lifecycle single-writer violation in `PaymentService`. |

## Blocking Findings

### MANUAL-FINAL-001 - Operator outcome bypass

`PAYMENT_REFERENCE_FREEZE.md` requires an equivalent inbound evidence boundary
for non-webhook workflows and specifically requires Manual receipt submission,
operator review, approval, rejection, expiration, and reconciliation to use a
workflow adapter and canonical evidence.

The UI-used confirm/reject endpoints validate role, tenant, provider, state, and
rejection reason, then construct `HandlePaymentResultRequest` directly. The
order-based `wallet-approve` and `wallet-reject` routes perform the same direct
handoff with a different input contract and without the UI-used routes'
`FinancialWrite` rate-limit attribute.

`PaymentLifecycleService` remains the correct durable writer, but the required
application Outcome Admission and canonical Runtime decision are missing before
that writer. Approval/rejection therefore cannot be certified as a Manual
Payment reference workflow.

Closure requires one application-owned review command family that admits
trusted actor action plus canonical receipt/review evidence, executes the Manual
Runtime path, validates the exact required result, and only then invokes
`PaymentLifecycleService`. Alternate routes must delegate to that authority or
be removed from the active surface.

### MANUAL-FINAL-002 - Feature and capability gates are not execution authority

The checked-in configuration sets provider enablement and proof/review/approval/
rejection/expiration execution flags to false. Configuration validation only
requires matching certifications when a flag is true; it does not deny an
operation when the operation flag is false.

The Storefront upload/clear controller, Admin Manual controller, and generic
lifecycle controller do not consume Manual Payment options or operational
readiness. Final checkout initialization fails closed through the application
service, but standalone proof upload and existing-intent operator mutations do
not share that gate.

Closure requires current tenant/global configuration and operation-specific
capability admission at every active ingress, including upload, clear, pending
review, proof access, approval, rejection, expiration, retry, and recovery.

### MANUAL-FINAL-003 - Expiration is presentation-only

Initialization computes a pending-payment expiry. Checkout response recovery
hides stale presentation fields, but no Manual Payment worker scans elapsed
intent expiry. The pending Admin query does not exclude elapsed intents, and
lifecycle approval validates pending status and trusted proof but not
`ExpiresAtUtc`.

This is a lifecycle defect, not merely a missing cleanup convenience: stale
evidence can still acquire financial authority. Closure requires an idempotent,
tenant-safe expiration execution path through the lifecycle writer and an
approval-time expiry fence, including concurrency with approval/rejection and
recovery.

### MANUAL-FINAL-004 - Recovery path is contradictory

The hosted recovery query considers a pending Manual Wallet intent incomplete.
After loading it, `IsProviderInitiationIncomplete` returns false solely because
the provider is Manual Wallet, so initialization/evidence admission is not
resumed. For a missing intent, `IsRecoverablePaymentMethod` returns false even
though a Manual Wallet creation branch exists.

Closure requires behavioral crash-window tests proving recovery from durable
intent creation through Runtime initialization/evidence admission, same-key
replay, missing/invalid receipt facts, configuration disablement, expiration,
and concurrency without constructing a privileged alternate path.

### MANUAL-FINAL-005 - Retention and access policy are not authoritative

The entity and cleanup job support proof expiry/deletion fields, and cleanup
normalizes tenant-scoped private keys. However, configured `ProofAccessTtl` is
not applied as the workflow authority. Terminal transitions use unrelated
hard-coded durations, a pending proof has no proof-access expiry, and the Admin
download endpoint checks only for a proof URL.

Closure requires one configured retention policy, expiry at evidence admission,
immediate access denial at policy expiry, lifecycle-aware deletion, observable
and retryable cleanup failure, and behavioral storage-outage tests.

### MANUAL-FINAL-006 - Canonical review evidence is not durable

The evidence adapter derives canonical evidence and a deterministic hash, but
the durable intent retains only selected wallet/proof/operator fields. The
system cannot later establish which canonical hash, instruction version,
content metadata, or application admission result justified
`AwaitingVerification` and approval.

Closure requires minimized, protected, durable evidence/checkpoint facts and a
tenant-scoped replay identity sufficient for duplicate, changed-proof,
recovery, audit, and reconciliation decisions. Raw proof bytes need not be
copied into SQL.

### MANUAL-FINAL-007 - Operational controls do not observe execution

The meter, counter names, health check, runbooks, and source documentation exist.
Production code does not call the Manual telemetry recorder. Readiness does not
probe SQL, object storage, cleanup/expiration execution, review dependencies, or
the active operation gates, and its intentionally disabled-provider semantics
can make the shared ready endpoint unhealthy.

Closure requires executing-path low-cardinality metrics, dependency-backed
readiness, intentionally disabled/skipped semantics, operation-capability
parity, MeterListener behavior tests, and alert/runbook ownership tied to the
actual counters.

### MANUAL-FINAL-008 - Full lifecycle single-writer rule is violated

The controllers route terminal approval/rejection to `PaymentLifecycleService`,
but the frozen rule covers accepted creation and every lifecycle-affecting
transition, not terminal outcomes alone. `PaymentService` directly constructs
and commits the Manual `PaymentIntent`, then directly changes it from `Pending`
to `AwaitingVerification` and persists the Runtime-derived reference and expiry.

This means the current initialization/evidence convergence still bypasses the
owning lifecycle writer at durable creation and pending-review transition.
Closure requires the application-owned orchestration to pass the admitted exact
Runtime result into `PaymentLifecycleService` for all durable lifecycle effects,
including creation and entry into review, without moving persistence into
Runtime or the adapter.

### MANUAL-FINAL-009 - Source migration governance is not converged

EF migration discovery and current model-state review are mandatory source
controls. Current EF discovery omits two standalone Manual Wallet migration
classes, while standalone SQL supplies competing paths. The proof-normalization
SQL does not enforce row-tenant/path-tenant equality like the C# artifact, and
the active-intent SQL can delete data and stamp migration history outside the
discovered chain before S12A creates the same index.

Closure requires one discoverable context-specific migration chain, reviewed
idempotent SQL, explicit compatibility/data-preservation behavior, clean and
upgrade PostgreSQL tests, and a converged model snapshot. Applying that chain
and verifying it in each target database remains a separate deployment task.

## Security Review

Verified controls include tenant-bound checkout upload tokens, recovery-nonce
matching, file size/content inspection, tenant-scoped private object keys,
checkout proof-key rebinding checks, Owner/Admin authorization, and lifecycle
validation of Manual Wallet provider/state/trusted proof before success.

Security certification is nevertheless withheld because disabled capability
flags do not deny the active ingress, expired proof access is not denied, and
the canonical evidence/admission record needed for audit and replay is not
durable. The order-based alternate routes also lack security-control parity with
the UI-used routes.

## Operational Review

Source artifacts exist for configuration validation, health, telemetry names,
proof cleanup, runbooks, alert guidance, and sandbox scenarios. They do not meet
the frozen minimum source controls because metrics are inert, readiness is not
dependency backed, expiration has no executor, and cleanup failure is caught at
the top level rather than surfaced as a failed Hangfire run.

Source Operational Readiness decision: **Withheld.**

## Migration Review

Positive evidence:

- EF discovers `20260128151641_ManualWalletCleanup`;
- the snapshot contains Manual Wallet intent fields and the active-intent unique
  index;
- `PaymentIntent` is tenant owned and uses PostgreSQL `xmin` optimistic
  concurrency; and
- S12A supplies current checkout recovery fields and an active-intent uniqueness
  fence.

Open evidence:

- `dotnet ef migrations has-pending-model-changes` reports model drift;
- `20260419190000_AddCheckoutSessionManualWalletRecovery` and
  `20260420103000_NormalizeManualWalletProofReferences` are not in EF migration
  discovery;
- the standalone proof-normalization SQL does not verify that the path tenant
  equals the row tenant as the C# artifact does; and
- standalone active-intent SQL can delete duplicate rows and stamp an EF history
  identifier outside the discovered chain, while S12A creates the same index.

Migration source certification: **Withheld.**  
Target database verification: **Not performed and not implied.**

## Regression Review

Executed against the current working tree on 2026-07-16:

| Verification | Result | Decision |
|---|---:|---|
| `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~ManualPayment\|FullyQualifiedName~ManualWallet\|FullyQualifiedName~PaymentRuntime"` | 215 passed / 5 failed / 220 total | Aggregate not green |
| Manual Payment / Manual Wallet slice | 122 passed / 5 failed / 127 total | Not green |
| Payment Runtime slice | 101 passed / 0 failed | Frozen Runtime green |
| API build | Passed, 0 warnings / 0 errors | Green |
| POS Admin production build | Passed | Green |
| Storefront production build | Passed with non-blocking Vite warnings | Green |
| POS Admin `ManualPayments.test.ts` | 5 passed / 0 failed | Green but narrow |
| EF migration discovery | Main Manual cleanup migration listed; two standalone artifacts absent | Not certified |
| EF pending-model check | Model changes reported | Not converged |

The five .NET failures are:

1. Manual Wallet approval concurrency expects one success, but the fixture has
   no trusted proof and both attempts fail before the asserted race result.
2. Checkout-session Manual Wallet recovery omits current tenant currency setup.
3. PaymentService Manual Wallet creation omits the now-required Manual Payment
   application services and therefore fails closed.
4. The authority artifact test requires `PASS SF-PAY3` in permanent context
   files even though the marker is confined to the bounded findings record.
5. The Foundation source guard rejects current shared
   `PaymentRuntimeOrchestrator` registration.

The first three are stale or incomplete behavioral fixtures around current
execution. The last two are source/marker assertions. They are not used to
invent an architecture pass, and they do not negate the independently green
Runtime result. Regression certification remains withheld because the full
applicable Manual aggregate is red and required expiration/recovery/gating/
telemetry behaviors are not tested.

## Readiness Separation

| Certification layer | Decision | Reason |
|---|---|---|
| Source Certification | **Withheld** | Nine Blocking High implementation, security, recovery, replay, single-writer, migration, and operations findings remain |
| Architecture Certification | **Withheld** | Operator outcomes bypass the Manual application/Runtime path and the full lifecycle writer is not singular |
| Implementation Certification | **Withheld** | Active gates, expiration, recovery, retention, and audit behavior are incomplete |
| Security Certification | **Withheld** | Capability, expired-access, alternate-route parity, and durable evidence gaps remain |
| Regression Certification | **Withheld** | Manual aggregate is 122/127 and critical behaviors lack execution tests |
| Migration Certification | **Withheld** | Pending model changes and ambiguous/disconnected migration paths remain |
| Source Operational Readiness | **Withheld** | Metrics are inert and readiness does not prove executing dependencies |
| Software Certification | **Withheld** | Blocking High source findings remain |
| Reference Provider Certification | **Withheld** | The active workflow is not a reusable example of the frozen architecture |
| Deployment Readiness | **Not ready** | Source blockers plus migration/configuration/operations work remain |
| Environment Readiness | **Not established** | No named environment has migrations, storage, tenant flags, operators, alerts, or rollback verified |
| Sandbox Readiness | **Not validated** | No governed end-to-end scenario and failure-drill evidence exists |
| Production Readiness | **Not ready** | Software, deployment, environment, and operational gates remain open |
| Live Operational Certification | **Not certified** | No named live evidence or operational approval exists |

## Reference Provider Decision

**Manual Payment is not Reference Provider Certified.**

Its adapter, Runtime bridge, initialization service, and evidence application
service are useful partial examples. The complete active workflow is not a safe
future-provider template because approval/rejection bypass that application and
Runtime authority, operation gates are not enforced, recovery and expiration
are incomplete, the full lifecycle writer is not singular, and durable evidence,
migrations, and operations do not satisfy the reference freeze.

Stripe remains the primary certified reference baseline. Fawry and the bounded
Paymob reference records remain comparison evidence under their exact scopes.
No Manual Payment defect requires or authorizes a Payment Runtime change.

## Required Closure Order

1. Converge proof review, approval, rejection, expiration, recovery, and
   reconciliation through one Manual Payment application-owned evidence and
   Runtime path before `PaymentLifecycleService`.
2. Enforce current provider, tenant method, environment, operation feature, and
   workflow certification gates at every active ingress.
3. Implement expiration execution and approval-time expiry denial with
   concurrency and replay proof.
4. Repair pending and missing Manual Wallet recovery through the same certified
   initialization/evidence path.
5. Make proof retention authoritative and enforce access expiry independently
   of cleanup timing.
6. Persist minimized canonical evidence/admission checkpoints and durable replay
   identity required for audit and reconciliation.
7. Wire production-path telemetry, dependency-backed readiness, and observable
   background-job failure.
8. Move durable Manual intent creation and entry into pending review behind the
   owning lifecycle writer while preserving application orchestration.
9. Establish one EF-governed, non-destructive, tested migration/upgrade path.
10. Replace stale/source-string certification assertions with behavioral tests,
   rerun the complete Manual/Runtime regressions, and conduct independent final
   re-certification.

After source closure, deployment owners must separately configure a named
environment, apply and verify migrations, enable exact capabilities, validate
private storage and operator access, install alerts, execute failure/recovery/
expiration/replay/two-tenant drills, measure latency, and approve rollback.

## Final Decision

| Final question | Decision |
|---|---|
| Payment Runtime unchanged and frozen? | **Yes.** |
| Provider-neutral Runtime preserved? | **Yes; 101/101 focused Runtime tests pass.** |
| Terminal approval/rejection SQL writer singular? | **Yes; both controller families delegate to `PaymentLifecycleService`.** |
| Full durable SQL lifecycle writer preserved? | **No; `PaymentService` persists creation and `AwaitingVerification` directly.** |
| Manual Payment architecture certified? | **No.** |
| Manual Payment implementation certified? | **No.** |
| Manual Payment security certified? | **No.** |
| Manual Payment source operations certified? | **No.** |
| Manual Payment migration certified? | **No.** |
| Manual Payment regression certified? | **No.** |
| Manual Payment software certified? | **No.** |
| Manual Payment Reference Provider certified? | **No.** |
| Deployment pending only? | **No; source blockers remain.** |
| Marketplace / CommerceHub certified here? | **No; Out of Scope.** |
| Runtime modification required? | **No; forbidden for these provider/workflow defects.** |
| Another closure and final re-certification required? | **Yes.** |

**FINAL DECISION: MANUAL PAYMENT SOFTWARE CERTIFICATION AND REFERENCE PROVIDER CERTIFICATION ARE WITHHELD. THE PROVIDER-NEUTRAL PAYMENT RUNTIME REMAINS PRESERVED; THE FULL MANUAL PAYMENT SINGLE-LIFECYCLE-WRITER RULE DOES NOT. DEPLOYMENT, ENVIRONMENT, SANDBOX, PRODUCTION, AND LIVE OPERATION ARE NOT READY OR CERTIFIED. MARKETPLACE AND COMMERCEHUB REMAIN SEPARATELY OUT OF SCOPE.**

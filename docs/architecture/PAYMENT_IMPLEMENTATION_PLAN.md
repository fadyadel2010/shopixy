# Payment Implementation Plan

Date: 2026-07-10
Scope: Enterprise Payment Runtime implementation roadmap
Status: OFFICIAL IMPLEMENTATION ROADMAP - PLANNING ONLY

Runtime modified: No.
Implementation modified: No.
Architecture modified: No.

## Executive Summary

This roadmap converts the certified Payment architecture into an incremental implementation plan. It does not redesign the architecture, change authority, introduce new payment concepts, or authorize runtime shortcuts.

The implementation objective is to build the certified provider-neutral Payment Platform while preserving continuous production operation. The migration must be additive, backward compatible, feature-flagged, dual-run capable, replayable, auditable, and reversible at every rollout boundary.

Frozen source material:

- `PAYMENT_0_PLATFORM_ARCHITECTURE_CERTIFICATION`
- `PAYMENT_0_7_CANONICAL_PAYMENT_DOMAIN`
- `PAYMENT_0_8_CANONICAL_RUNTIME_MODEL`
- `PAYMENT_0_9_PROVIDER_ADAPTER_CERTIFICATION`
- `PAYMENT_1_0_POLICY_ENGINE`
- `PAYMENT_1_1_POLICY_FRAMEWORK`
- `PAYMENT_1_2_POLICY_EVALUATION_PIPELINE`
- `PAYMENT_0_5A_STRIPE_DISCOVERY`
- `PAYMENT_0_5B_PAYMOB_DISCOVERY`
- `PAYMENT_0_5C_FAWRY_DISCOVERY`
- `PAYMENT_0_5D_MANUAL_PAYMENT_DISCOVERY`
- `PAYMENT_0_5E_COD_DISCOVERY`
- all `.ai-context` files

Certified boundary summary:

```text
Policy decides.
Payment Runtime executes.
Provider Adapter translates.
Provider facts are evidence.
PaymentDecision owns lifecycle mutation.
PaymentTransaction owns immutable financial recognition.
SQL Payment authority remains the source of internal payment truth.
```

## Implementation Principles

- Planning only in this document. No runtime code changes are authorized here.
- Preserve `PaymentIntent` and existing DTO/provider fields as compatibility surfaces while implementing canonical `PaymentSession` concepts behind additive contracts.
- Add small commits that compile independently.
- Prefer additive folders, contracts, entities, services, and tests before cutting over behavior.
- Keep every production path under feature flags until certification tests and dual-run validation pass.
- Never let providers, frontend, redirects, SDK results, cache, projections, or raw payloads own final payment state.
- Never add provider-specific runtime states or provider-specific transaction writers.
- Fail closed on missing facts, invalid authority, amount/currency mismatch, ambiguous provider outcome, missing policy versions, missing audit, or replay failure.
- Run shadow and dual-run modes before traffic migration.
- Treat rollback as disabling flags and returning to the existing runtime path, not deleting new code.

## Implementation Phases

### Phase 1: Foundation

Goal: create the structural shell without provider logic.

Deliverables:

- Core folders for Payment Runtime, Policy, Providers, Evidence, Verification, Decisions, Transactions, Settlement, Recovery, Audit, Projections, Administration, and Tests.
- Provider-neutral namespaces and contracts.
- Dependency graph that enforces Policy -> Runtime -> Adapter direction.
- Feature flag definitions for runtime enablement, policy enablement, provider shadow mode, provider live mode, dual-run, persistence, admin surfaces, and rollout percentage.
- Architecture tests that prevent provider-specific runtime branching, direct payment status mutation, direct transaction creation by adapters, frontend finality, and projection authority.

Dependency order:

1. Namespace and folder scaffolding.
2. Feature flag names and configuration contracts.
3. Core marker interfaces and base abstractions.
4. Architecture guard tests.
5. No-op composition registrations behind disabled flags.

Rollback: remove or disable registrations; no production behavior changes.

### Phase 2: Canonical Runtime

Goal: implement canonical runtime objects and state machine in exact dependency order.

Dependency order:

1. `PaymentSession`: root aggregate and compatibility mapping to existing `PaymentIntent`.
2. `PaymentAttempt`: retry/execution fence under a session.
3. `ProviderSession`: provider handoff/reference/token/session evidence.
4. `PaymentAction`: redirect, embedded checkout, receipt upload, reference payment, COD receive-and-pay, manual review, status refresh.
5. `ProviderEvidence`: immutable candidate facts.
6. `PaymentVerification`: admission results for evidence.
7. `PaymentDecision`: only lifecycle transition authority.
8. `PaymentTransaction`: immutable financial recognition.
9. `PaymentSettlement`: distinct settlement admission.
10. `AuditRecord`: append-only runtime trace.
11. State machine: transition table, forbidden transition tests, terminal-state collapse, retry and recovery transitions.

Implementation order must keep persistence optional at first. Build in-memory/domain-level models and tests before repositories.

Rollback: leave canonical models unused by production path; disable runtime feature flag.

### Phase 3: Policy Engine

Goal: implement the policy engine without persistence.

Deliverables:

- `PolicyContext`, `PipelineContext`, `PolicyResult`, `PolicyDecision`, `PolicyEvaluation`, `PolicyAudit` contracts.
- Fact model interfaces for tenant, merchant, customer, order, warehouse, shipping, payment, provider, capability, constraint, risk, fraud, settlement, evidence, and decision history.
- Rule contracts and deterministic evaluation model.
- Policy chain: context admission, eligibility, routing, operation policy, risk/fraud, compliance, manual review, recovery/settlement, execution decision.
- Conflict resolution according to certified precedence.
- Decision composition with reason codes, version references, context hash, decision hash, and audit requirement.
- In-memory policy repository only; no persistence yet.

Rollback: runtime continues using existing behavior because policy engine remains disabled and in-memory only.

### Phase 4: Provider Contracts

Goal: implement provider-facing contracts only.

Deliverables:

- `IPaymentProvider`.
- Evidence admission contracts.
- Verification contracts.
- Recovery contracts.
- Settlement contracts.
- Capability and constraint contracts.
- Provider certification test base.
- No provider implementation logic.

Rules:

- Adapters expose capabilities/constraints and translate evidence only.
- Adapters do not own lifecycle state, transactions, policy, or finality.
- Provider-specific DTO compatibility may remain at boundaries.

Rollback: provider contracts are unused until provider flags activate.

### Phase 5: Reference Provider

Recommended first provider: Stripe.

Justification:

- Stripe is already closest to the certified provider-neutral runtime.
- Current flow uses backend-created PaymentIntents, Payment Element handoff, backend webhook signature verification, SQL webhook logs, idempotency, encrypted secrets, and lifecycle-owned finality.
- It exercises real external provider session creation, frontend handoff evidence, webhooks, duplicate events, amount/currency validation, and recovery better than Manual or COD.
- It has migration opportunities, but no immediate architecture break.

Why not first:

- Paymob: current implementation is compatible but uses legacy iframe/payment-key flow while official docs now prefer Intention API and Unified Checkout/Pixel. This increases migration surface.
- Fawry: current implementation is architecture-compatible simulation and official-API-incomplete. It should not be the first reference for real external-provider runtime behavior.
- Manual: safest operationally, but it is a workflow provider and does not prove external gateway session/webhook behavior.
- COD: requires delivery/collection evidence separation before enterprise completeness.

Reference provider rollout order:

1. Stripe shadow adapter around current behavior.
2. Stripe dual-run evidence comparison.
3. Stripe certification suite.
4. Stripe limited live traffic under feature flag.

### Phase 6: Additional Providers

Provider migration order:

1. Manual Payment
   - Complexity: Medium.
   - Reason: backend-owned workflow is already compatible and avoids external API uncertainty.
   - Risks: evidence integrity, duplicate proof detection, reviewer audit, expiration/cancellation/refund workflow gaps.

2. Paymob
   - Complexity: High.
   - Reason: compatible existing flow, but migration to Intention/Unified Checkout requires credential, config, DTO, and checkout-mode coexistence planning.
   - Risks: legacy iframe support, new key model, regional base URLs, callback/HMAC variants, method-specific capabilities.

3. COD
   - Complexity: High.
   - Reason: business critical but requires delivery and cash collection separation before enterprise certification.
   - Risks: delivery-as-collection, role authority, cash custody, partial collection, refusal/RTO, settlement discrepancies.

4. Fawry
   - Complexity: Very High.
   - Reason: current adapter simulates reference creation; official provider API, signature, notification, status, refund, cancel, and recovery must be built before live production processing.
   - Risks: official signature mismatch, payload field mismatch, provider reference mapping, status inquiry, merchant activation, secure key governance.

This order minimizes external API risk while exercising increasingly complex evidence and operations.

### Phase 7: Persistence

Goal: persist runtime and policy history after domain behavior is tested.

Deliverables:

- Repositories for sessions, attempts, provider sessions, actions, evidence, verifications, decisions, transactions, settlements, refunds, captures, voids, recoveries, and audit.
- Version store for policy, rule, catalog, fact schema, evaluation, audit, and replay versions.
- Audit store with append-only behavior.
- Replay store with immutable input snapshots, context hashes, decision hashes, and version manifests.
- Governance persistence for draft/review/approved/published/deprecated/archived policy lifecycle.
- History and snapshots for runtime and policy replay.
- Additive migrations only.

Rollback: migrations must be additive; production path remains old runtime until persistence flag is enabled.

### Phase 8: Administration

Goal: expose operator surfaces only after runtime, policy, and persistence are stable.

Deliverables:

- Policy Administration.
- Rule Administration.
- Replay Viewer.
- Audit Viewer.
- Decision Explorer.
- Provider Dashboard.
- Feature Flag control surface.
- Provider certification status and migration readiness view.

Rules:

- Admin actions submit governed intent only.
- Admin UI never edits runtime state directly.
- Policy draft publication requires governance lifecycle and audit.

### Phase 9: Testing

Required test suites:

- Unit tests for runtime objects, state machine, policies, rules, conflict resolution, and provider contracts.
- Architecture tests for authority boundaries and forbidden dependencies.
- Integration tests for existing runtime compatibility and flag-off behavior.
- Provider certification tests for Stripe, Manual, Paymob, COD, and Fawry.
- Replay tests for evidence, decisions, policy versions, audit, and recovery.
- Migration tests for additive schema and backfill-safe cutover.
- Regression tests for current checkout/payment/provider behavior.
- Performance tests for policy evaluation, evidence admission, webhook processing, and replay.
- Chaos tests for duplicate webhooks, late events, provider timeout, lost callback, audit failure, projection lag, settlement mismatch, refund overlap, and external outcome uncertainty.

### Phase 10: Rollout

Rollout sequence:

1. Feature flags registered and default off.
2. Dark launch with no production request routing.
3. Shadow mode records canonical decisions beside existing runtime outcomes.
4. Dual-run compares existing runtime outcome to canonical runtime outcome without changing customer-visible behavior.
5. Provider-by-provider certification gates.
6. Tenant allowlist.
7. 1 percent traffic for one provider and one tenant cohort.
8. 5 percent, 25 percent, 50 percent, 100 percent only after metrics pass.
9. Rollback rehearsed before 25 percent.
10. Production validation with dashboards, alert thresholds, audit review, replay drills, and provider reconciliation.

Rollback:

- Disable provider live flag.
- Disable canonical runtime execution flag.
- Keep shadow logging enabled only if safe.
- Route all requests to existing runtime path.
- Preserve new audit/evidence records for investigation.

## Sprint Plan

### Sprint 1: Foundation Shell

Goal: create payment platform structure without behavior.
Deliverables: folders, namespaces, flags, base contracts, dependency graph tests.
Dependencies: certified docs.
Risk: accidental runtime registration.
Rollback: remove registrations or keep flags off.
Definition of Done: solution compiles, architecture tests pass, no provider logic.

### Sprint 2: Runtime Object Contracts

Goal: define canonical runtime object contracts.
Deliverables: PaymentSession through AuditRecord contracts and DTO-free domain models.
Dependencies: Sprint 1.
Risk: leaking provider-specific fields into runtime.
Rollback: unused contracts remain inert.
Definition of Done: compile, unit tests cover object relationships.

### Sprint 3: State Machine

Goal: implement provider-neutral transition rules.
Deliverables: allowed/forbidden transitions, terminal-state collapse, retry/recovery rules.
Dependencies: Sprint 2.
Risk: hidden provider state or direct status mutation.
Rollback: flag off; existing lifecycle unchanged.
Definition of Done: transition matrix tests and forbidden-transition tests pass.

### Sprint 4: Evidence and Verification

Goal: implement evidence admission and verification primitives.
Deliverables: evidence candidates, verification outcomes, duplicate/idempotency model, amount/currency validation tests.
Dependencies: Sprint 3.
Risk: treating evidence as finality.
Rollback: evidence pipeline remains shadow-only.
Definition of Done: evidence cannot mutate state without PaymentDecision.

### Sprint 5: Decisions and Transactions

Goal: connect verified evidence to decisions and immutable transactions.
Deliverables: decision composition, transaction writer contract, audit linkage.
Dependencies: Sprint 4.
Risk: duplicate transactions.
Rollback: shadow-only writer.
Definition of Done: duplicate evidence collapses; transactions are immutable.

### Sprint 6: Policy Engine In-Memory

Goal: implement policy evaluation without persistence.
Deliverables: PolicyContext, facts, rules, policy chain, conflict resolution, decision composition.
Dependencies: Sprint 5.
Risk: runtime business rules outside policy.
Rollback: policy flag off.
Definition of Done: fail-closed tests, conflict precedence tests, deterministic replay tests.

### Sprint 7: Provider Contracts

Goal: add provider contract layer with no providers.
Deliverables: IPaymentProvider, capability/constraint/evidence/verification/recovery/settlement contracts.
Dependencies: Sprint 6.
Risk: adapter overreach.
Rollback: contracts unused.
Definition of Done: architecture tests prove adapters cannot mutate runtime state or create transactions.

### Sprint 8: Stripe Shadow Adapter

Goal: wrap current Stripe behavior as the reference shadow provider.
Deliverables: capability map, evidence mapping, webhook mapping, shadow comparison logs.
Dependencies: Sprint 7.
Risk: API version/key governance gaps.
Rollback: Stripe canonical flag off; existing Stripe path remains.
Definition of Done: shadow outcomes match existing lifecycle for certified scenarios.

### Sprint 9: Runtime Persistence

Goal: add additive persistence for canonical runtime.
Deliverables: repositories, additive migrations, audit store, replay store.
Dependencies: Sprints 3-8.
Risk: schema rollout coupling.
Rollback: leave tables unused; flags off.
Definition of Done: migration tests pass; no existing table destructive changes.

### Sprint 10: Policy Persistence and Governance

Goal: persist policy versions and governance state.
Deliverables: version store, policy repository, governance lifecycle, replay metadata.
Dependencies: Sprint 9.
Risk: draft policy leakage.
Rollback: policy persistence flag off; in-memory defaults remain for tests.
Definition of Done: only published effective versions selected.

### Sprint 11: Manual Payment Migration

Goal: add Manual Payment canonical workflow.
Deliverables: manual evidence mapping, review action, approval/rejection decision mapping, audit.
Dependencies: Sprints 7-10.
Risk: thin evidence model.
Rollback: keep existing Manual Wallet path.
Definition of Done: current workflow remains compatible; canonical path passes certification tests.

### Sprint 12: Paymob Migration

Goal: introduce Paymob canonical adapter with legacy coexistence.
Deliverables: legacy iframe mapping, credential model plan, Intention/Unified Checkout feature flags, webhook/callback evidence mapping.
Dependencies: Stripe reference and persistence.
Risk: legacy/new checkout mode ambiguity.
Rollback: Paymob canonical flag off; legacy path remains.
Definition of Done: dual-run for legacy flow; new mode remains tenant-disabled until certified.

### Sprint 13: COD Migration

Goal: separate delivery evidence from collection evidence.
Deliverables: COD obligation mapping, collection evidence admission, settlement linkage, exception-state policy.
Dependencies: Sprints 6-10.
Risk: changing operational assumptions.
Rollback: keep current happy-path COD behind existing behavior; new COD evidence path disabled.
Definition of Done: Payment collects only admitted collection evidence in canonical path.

### Sprint 14: Fawry Migration

Goal: build official Fawry readiness behind disabled flags.
Deliverables: secure key/signature governance, reference charge adapter, notification/status mapping, recovery/status inquiry.
Dependencies: Sprints 7-10.
Risk: official API mismatch and merchant activation.
Rollback: canonical Fawry disabled; existing simulation unchanged.
Definition of Done: sandbox certification passes before any live enablement.

### Sprint 15: Administration Surfaces

Goal: expose audit, replay, decision, policy, provider, and flag administration.
Deliverables: admin APIs and UI surfaces for read/review/governance workflows.
Dependencies: persistence and provider certification.
Risk: admin bypassing runtime or policy.
Rollback: admin feature flags off.
Definition of Done: admin actions submit intent only; no direct state mutation.

### Sprint 16: Production Rollout

Goal: controlled production migration.
Deliverables: dashboards, alerts, runbooks, rollback drill, traffic ramp.
Dependencies: all prior sprints.
Risk: external-provider uncertainty.
Rollback: disable live flags and return to existing runtime.
Definition of Done: rollout metrics pass and dual-run mismatch rate is within threshold.

## Commit Plan

Recommended commit strategy:

1. Commit 1: foundation folders, namespaces, and feature flag constants.
2. Commit 2: core runtime contracts.
3. Commit 3: runtime object models and relationship tests.
4. Commit 4: state machine and transition tests.
5. Commit 5: evidence contracts and admission tests.
6. Commit 6: verification model and duplicate/idempotency tests.
7. Commit 7: decision model and transaction contracts.
8. Commit 8: audit contracts and replay metadata contracts.
9. Commit 9: in-memory policy facts, rules, and policies.
10. Commit 10: policy pipeline and conflict resolution.
11. Commit 11: provider contracts and capability/constraint registry contracts.
12. Commit 12: provider certification test harness.
13. Commit 13: Stripe shadow adapter mapping.
14. Commit 14: runtime persistence migrations and repositories.
15. Commit 15: policy version store and governance persistence.
16. Commit 16: Manual Payment canonical workflow.
17. Commit 17: Paymob legacy mapping and v2 migration scaffolding.
18. Commit 18: COD collection evidence path.
19. Commit 19: Fawry official adapter scaffolding.
20. Commit 20: administration read surfaces.
21. Commit 21: rollout dashboards, alerts, and runbooks.

Every commit must compile. Behavior-changing commits must include focused tests and a rollback flag.

## Migration Strategy

Migration is provider-by-provider and tenant-by-tenant.

Stage 1: Compatibility baseline

- Existing runtime remains primary.
- Canonical runtime is built with flags off.
- No production behavior changes.

Stage 2: Shadow mode

- Canonical runtime receives copied inputs after existing runtime completes.
- Canonical decisions are recorded as non-authoritative shadow outcomes.
- Mismatches are audited and investigated.

Stage 3: Dual-run mode

- Existing runtime remains authoritative.
- Canonical runtime evaluates in parallel before or beside existing execution.
- Provider actions are not duplicated across external boundaries unless explicitly fenced.

Stage 4: Limited live mode

- One provider, one tenant cohort, low traffic percentage.
- Existing runtime remains rollback path.
- Audit, replay, provider reconciliation, and policy decisions are monitored.

Stage 5: Provider migration

- Stripe first.
- Manual Payment second.
- Paymob third.
- COD fourth.
- Fawry fifth.

Stage 6: Full cutover

- Canonical runtime becomes primary only after provider certification, policy replay, migration tests, and chaos tests pass.
- Existing compatibility surfaces remain until all callers and historical sessions are retired.

## Risk Matrix

| Risk Area | Risk | Impact | Mitigation |
|---|---|---|---|
| Technical | Runtime state machine diverges from certified states | Payment finality errors | Transition table tests, architecture guards, provider-neutral state enums only |
| Technical | Provider fields leak into runtime | Runtime specialization | Provider contracts, capability/constraint registry, source guards |
| Technical | Duplicate transactions | Financial overstatement | Idempotency keys, evidence duplicate collapse, immutable transaction tests |
| Migration | Existing sessions cannot be read by canonical runtime | Cutover failure | Compatibility mapping from `PaymentIntent` to `PaymentSession` |
| Migration | Schema rollout blocks production | Deployment interruption | Additive migrations, flags off, no destructive schema changes |
| Provider | Stripe API/webhook version drift | Webhook/typed SDK mismatch | version governance, webhook drills, restricted-key migration plan |
| Provider | Paymob legacy/new mode ambiguity | Broken checkout handoff | legacy coexistence, tenant flags, Intention mode disabled until certified |
| Provider | Fawry official contract mismatch | Invalid live provider processing | sandbox-only official adapter, signature tests, Status V2 recovery |
| Provider | Manual proof fraud | False payment approval | evidence hash, duplicate detection, reviewer audit, manual review policy |
| Provider | COD delivery treated as collection | Cash recognition error | separate collection evidence, Payment admission, cash custody audit |
| Runtime | Recovery retries external operation blindly | Duplicate external charge/session | retry fences, ambiguity quarantine, status inquiry before retry |
| Testing | Shadow mode misses edge cases | Unsafe cutover | provider certification tests, replay tests, chaos tests |
| Operational | Rollback path untested | Extended incident | rollback rehearsal before traffic ramp |
| Operational | Audit failure after allow | Unreplayable decisions | audit-before-executable-allow fail-closed rule |

## Success Metrics

- Architecture preserved: no authority boundary changes.
- Compilation succeeds after every commit.
- Architecture tests pass for provider neutrality and lifecycle authority.
- 100 percent provider certification tests pass before provider live enablement.
- No provider-specific runtime states.
- No provider-specific runtime transaction writers.
- No frontend, cache, projection, redirect, SDK, or raw provider payload finality.
- Feature flags operational for runtime, policy, provider, shadow, dual-run, and rollout percentage.
- Dual-run operational for each provider before live cutover.
- Shadow mismatch rate below approved threshold before traffic migration.
- Replay succeeds for policy decisions and runtime decisions.
- Audit recorded before executable allow decisions.
- Migration validated with additive migrations and rollback flags.
- Provider rollback restores existing runtime path without data loss.
- Production rollout has dashboards, alerts, runbooks, and rollback rehearsal evidence.

## Production Rollout

Required gates:

1. Flag-off deployment compiles and runs with no behavior change.
2. Architecture and unit tests pass.
3. Existing provider regression tests pass.
4. Shadow mode produces no material authority mismatches.
5. Dual-run proves deterministic decision parity where parity is expected.
6. Provider certification suite passes for selected provider.
7. Replay and audit tests pass.
8. Rollback drill succeeds.
9. Dashboards and alerts are active.
10. Operational owner approves traffic ramp.

Traffic ramp:

- 0 percent live: dark launch and shadow only.
- 1 percent: one provider, one tenant cohort.
- 5 percent: continue only if mismatch/error/rollback metrics pass.
- 25 percent: require rollback rehearsal and provider reconciliation review.
- 50 percent: require audit/replay sample review.
- 100 percent: provider considered migrated, existing compatibility path retained for historical sessions.

## Definition of Done

The Payment Platform implementation is done only when:

- Certified architecture boundaries are preserved.
- Runtime, policy, provider contracts, persistence, administration, tests, and rollout gates are complete.
- Existing production behavior remains backward compatible.
- Each provider passes certification before live enablement.
- Feature flags, dual-run, rollback, audit, replay, and monitoring are operational.
- No direct provider, frontend, projection, cache, or order/shipping authority can mutate final payment state.
- Operations can prove payment decisions, policy decisions, evidence admission, provider events, transactions, settlements, refunds, and recovery through audit/replay.

## Final Recommendation

Start with Phase 1 Foundation and implement only inert folders, contracts, feature flags, and architecture guards. Do not start with provider logic, persistence, admin screens, or provider migrations.

The first live provider implementation should be Stripe because it is the safest real external-provider reference and already aligns most closely with the certified runtime. Manual Payment should follow as the safest workflow provider. Paymob, COD, and Fawry should follow in that order because each requires heavier migration or operational evidence separation.

## Explicit Success Answers

Implementation roadmap completed? Yes.

Sprint plan completed? Yes.

Commit strategy completed? Yes.

Migration strategy completed? Yes.

Testing strategy completed? Yes.

Rollout strategy completed? Yes.

Risk matrix completed? Yes.

Production-ready roadmap? Yes, as a planning roadmap. No implementation has been performed.

Recommended first implementation task? Create the Phase 1 foundation shell: folders, namespaces, feature flag contracts, provider-neutral base contracts, and architecture guard tests with all behavior disabled by default.

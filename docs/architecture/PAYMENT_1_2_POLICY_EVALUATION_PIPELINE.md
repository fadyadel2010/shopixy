# PAYMENT-1.2 Payment Policy Evaluation Pipeline

Date: 2026-07-10
Scope: Provider-neutral canonical evaluation pipeline for every payment policy request
Status: CERTIFIED ARCHITECTURE

Runtime modified: No.

Implementation modified: No.

Adapters modified: No.

`.ai-context` updated: Yes. PAYMENT-1.2 defines the permanent policy evaluation pipeline truth: deterministic stage ordering, `PipelineContext`, `PolicyContext` assembly, policy discovery, rule execution, conflict resolution, decision composition, failure handling, event model, and extensibility rules. It preserves the certified PAYMENT-0.8 Runtime, PAYMENT-1.0 Policy Engine, and PAYMENT-1.1 Policy Framework boundaries.

## Executive Summary

PAYMENT-1.2 defines the canonical enterprise Payment Policy Evaluation Pipeline.

The Pipeline coordinates the complete lifecycle of a payment policy evaluation. It assembles and validates authoritative facts, binds immutable versions, discovers applicable policies, orders policies deterministically, executes rule groups, resolves conflicts, composes one final policy decision, records audit evidence, and returns the decision to the certified Payment Runtime.

The Pipeline does not own facts, edit policies, execute provider calls, mutate payment state, create runtime transactions, or serialize provider wire contracts.

Canonical boundary:

```text
Payment Runtime Request
  -> Policy Evaluation Pipeline
  -> Immutable PolicyContext
  -> Versioned Policy Discovery
  -> Deterministic Rule Execution
  -> Conflict Resolution
  -> Decision Composition
  -> PolicyDecision + Audit
  -> Payment Runtime Enforcement
```

Certification decision: **PAYMENT-1.2 is certified as the enterprise Payment Policy Evaluation Pipeline architecture.**

## Pipeline Architecture

The Policy Engine orchestrates policy evaluation. The Pipeline is the canonical execution lifecycle used by that engine for every payment policy request.

Responsibilities:

- Establish one deterministic evaluation flow for all payment policy requests.
- Assemble `PolicyContext` from current authority, immutable snapshots, and admitted evidence only.
- Maintain `PipelineContext` as the internal coordination state for one evaluation.
- Select mandatory, operation-specific, dependency-driven, and optional policies.
- Enforce version binding for policies, rules, catalog, facts, evaluation semantics, audit, and replay.
- Execute rules and rule groups according to declared ordering, dependencies, priorities, and short-circuit rules.
- Resolve conflicts using certified precedence.
- Compose one immutable `PolicyDecision`.
- Record every material stage, warning, failure, decision candidate, and audit reference.
- Fail closed whenever authority, facts, versions, policy definitions, rule definitions, replay artifacts, or audit requirements are incomplete.

Non-responsibilities:

- The Pipeline does not own, repair, fetch, or mutate facts.
- The Pipeline does not edit policies, rules, catalog entries, governance state, or versions.
- The Pipeline does not call providers.
- The Pipeline does not execute payments, refunds, captures, voids, settlements, retries, recovery, or manual review.
- The Pipeline does not create `PaymentTransaction` or mutate `PaymentSession` state.
- The Pipeline does not trust frontend, cache, projection, redirect, SDK, dashboard, analytics, or raw provider payload state as policy authority.

## Pipeline Stages

| Stage | Purpose | Inputs | Outputs | Owner | Failure Mode |
|---|---|---|---|---|---|
| 1. Evaluation Admission | Admit one policy evaluation request and bind operation intent, actor/system, tenant scope, idempotency/replay reference, and requested effective time. | Runtime request, operation intent, actor/system, tenant/payment identifiers, replay reference when applicable. | Admitted evaluation identity and initial `PipelineContext`. | Policy Engine / Pipeline. | Fail closed as `Denied`, `NoDecisionFailClosed`, or `Recovery` if request identity, scope, actor, or intent is invalid. |
| 2. Context Assembly | Assemble immutable provider-neutral facts into `PolicyContext`. | Authoritative fact sources, immutable snapshots, admitted evidence, decision history. | Candidate `PolicyContext`, fact lineage, missing-fact list, source-authority evidence. | Pipeline coordinates; fact owners remain source domains. | Fail closed when required facts are missing, stale, non-authoritative, or contradictory. |
| 3. Authority Validation | Prove every fact came from current backend authority, immutable snapshot, or admitted evidence. | Candidate `PolicyContext`, fact lineage, authority declarations. | Validated authority map and rejected source list. | Pipeline validates; source domains own facts. | Fail closed as `InvalidAuthority`, `MissingFacts`, `Quarantine`, or `Recovery` depending on domain rule. |
| 4. Fact Normalization | Normalize facts into canonical provider-neutral forms for deterministic rule evaluation. | Validated facts, fact schema version, normalization contract. | Normalized immutable `PolicyContext`, context hash inputs. | Pipeline. | Fail closed on unsupported schema, unknown capability/constraint, invalid enum/domain value, or lossy normalization. |
| 5. Completeness Validation | Verify required fact families for the operation and candidate policies are present and compatible. | Normalized facts, operation intent, policy required-fact declarations. | Completeness result, missing-fact diagnostics, warning list. | Pipeline. | Missing required facts fail closed; optional facts may produce warnings only when no selected policy requires them. |
| 6. Snapshot and Version Binding | Freeze the `PolicyContext` and bind exact policy, rule, catalog, fact schema, evaluation, audit, and replay versions. | Normalized context, effective timestamp, published version registry. | Immutable `PolicyContext`, context hash, version binding manifest. | Pipeline with Policy Repository. | Version mismatch, unpublished version, missing replay artifact, or incompatible fact schema fails closed. |
| 7. Policy Discovery | Select applicable policies for the operation, tenant, domain, method, provider candidate, action, and evidence posture. | Operation intent, `PolicyContext`, policy scope declarations, dependencies, publication metadata. | Candidate policy set with mandatory/optional/dependency classification. | Policy Engine / Pipeline using Policy Repository. | Missing mandatory policy or unresolved dependency fails closed. Optional missing policy is skipped only if declared optional by governance. |
| 8. Policy Ordering | Produce deterministic policy order across domains and dependencies. | Candidate policy set, domain order, priority, dependencies, effective versions. | Ordered policy plan. | Pipeline. | Cyclic dependency, ambiguous priority, duplicate terminal policy, or incompatible ordering fails closed. |
| 9. Rule Group Execution | Execute rule groups within each policy according to declared composition. | Ordered policy plan, immutable `PolicyContext`, `PipelineContext`, rule group declarations. | Policy-level decision candidates, group traces, skipped groups. | Policy Evaluator under Pipeline coordination. | Rule-group missing, dependency failure, required fact missing, or non-deterministic declaration fails closed. |
| 10. Rule Evaluation | Evaluate atomic rules deterministically against immutable facts. | `PolicyContext`, rule versions, rule dependencies, priority, short-circuit declarations. | Rule results, reason codes, effects, severity, trace entries. | Policy Evaluator. | Rule missing, unknown version, invalid result, side-effect attempt, or exception fails closed according to rule severity and policy fail-closed contract. |
| 11. Conflict Resolution | Resolve competing policy and rule outcomes into a deterministic decision direction. | Decision candidates, severity, mandatory/optional classification, policy domain precedence, dependencies. | Resolved outcome and conflict trace. | Pipeline. | Unresolved conflict fails closed to the highest safe non-allow outcome. |
| 12. Decision Composition | Compose one immutable `PolicyDecision` with reasons, rule trace, version evidence, provider recommendation, and execution metadata. | Resolved outcome, decision candidates, context hash, rule traces, versions, audit buffer. | Final `PolicyDecision` candidate and decision hash. | Pipeline. | Composition failure, missing reason code, missing version linkage, or invalid selected provider/method fails closed. |
| 13. Audit Recording | Record replay-safe evaluation audit before or atomically with decision return according to audit contract. | Final decision, context hash, decision hash, traces, versions, actor/system, timestamps. | `PolicyAudit` record/reference and immutable evaluation trace. | Policy Audit boundary. | Audit failure fails closed; the Pipeline must not return an executable allow decision without required audit evidence. |
| 14. Decision Return | Return the final decision to Runtime for enforcement. | Audited `PolicyDecision`, audit reference, execution metadata. | Immutable decision returned to Runtime. | Policy Engine / Pipeline. | Return failure records failure where possible; Runtime must treat absence of decision as fail-closed. |
| 15. Evaluation Completion | Close internal pipeline state and emit completion event. | Return outcome, audit status, warnings/failures. | Completed evaluation record and terminal pipeline event. | Pipeline. | Completion telemetry/audit append failure is recorded as audit failure; executable allow remains invalid if mandatory audit did not complete. |

## Pipeline Context

`PipelineContext` is the internal coordination state for one policy evaluation. It is separate from `PolicyContext`.

`PolicyContext` is the immutable provider-neutral input snapshot consumed by policies and rules.

`PipelineContext` is the mutable, evaluation-local execution envelope used by the Pipeline to track progress, intermediate results, warnings, failures, traces, and audit material.

Canonical fields:

| Field | Purpose | Mutability |
|---|---|---|
| Evaluation Id | Stable identity for one pipeline run. | Immutable after admission. |
| Operation Intent | Requested policy operation. | Immutable after admission. |
| Actor/System | Initiating actor, system, job, reviewer, runtime process, or recovery actor. | Immutable after admission. |
| Current Stage | Current pipeline stage. | Mutable only by Pipeline stage transitions. |
| Current Policy | Policy currently being evaluated. | Mutable during policy execution. |
| Current Rule Group | Rule group currently being evaluated. | Mutable during rule-group execution. |
| Current Rule | Rule currently being evaluated. | Mutable during rule execution. |
| PolicyContext Reference | Immutable input snapshot reference and hash. | Bound once after assembly. |
| Version Binding Manifest | Policy, rule, catalog, fact schema, evaluation, audit, and replay versions. | Bound once; corrections require a new evaluation. |
| Candidate Providers | Provider-neutral provider candidates and rankings under evaluation. | Mutable only as intermediate evaluation output; never provider execution authority. |
| Candidate Methods | Provider-neutral method candidates under evaluation. | Mutable only as intermediate evaluation output. |
| Intermediate Results | Rule, group, and policy result candidates. | Append-only during evaluation. |
| Decision Candidates | Policy-level and conflict-resolution candidates. | Append-only until decision composition. |
| Evaluation Trace | Ordered stage, policy, group, and rule trace. | Append-only. |
| Warnings | Non-terminal diagnostics. | Append-only. |
| Failures | Fail-closed diagnostics and classification. | Append-only. |
| Audit Buffer | Material required to create policy audit. | Append-only until audit recording. |
| Replay Metadata | Artifacts required for deterministic replay. | Append-only and then frozen. |

Ownership:

- Owned by the Policy Evaluation Pipeline for the lifetime of one evaluation.
- Not persisted as mutable business authority.
- May be materialized as part of audit/replay evidence after completion.
- Never exposed to Runtime or Adapters as a mutable command surface.

Lifetime:

- Created at Evaluation Admission.
- Mutable only while the Pipeline is executing.
- Frozen at Evaluation Completion.
- Retained only through audit/replay materialization according to audit retention rules.

Mutability:

- Stage pointers are mutable by the Pipeline only.
- Trace, warnings, failures, intermediate results, and audit buffer are append-only.
- `PolicyContext`, version manifest, and final decision are immutable once bound.
- Re-evaluation creates a new `PipelineContext`; it does not edit a prior one.

## Context Assembly

PolicyContext assembly is the canonical process that converts authoritative payment, order, tenant, merchant, customer, provider-neutral capability, constraint, risk, fraud, compliance, settlement, evidence, and decision-history facts into one immutable input snapshot.

Assembly steps:

1. Fact Collection
2. Source Authority Verification
3. Fact Normalization
4. Snapshot Creation
5. Version Binding
6. Hash Generation
7. Completeness Validation
8. Missing Fact Detection

### Fact Collection

The Pipeline may coordinate fact collection, but it does not own the facts. Facts come only from:

- Current backend authority.
- Immutable snapshots.
- Runtime-admitted evidence.
- Immutable decision and audit history.

Forbidden fact sources:

- Frontend state.
- Browser storage.
- Route parameters.
- Redirect parameters.
- SDK result state by itself.
- Raw provider payload replay.
- Cache.
- Redis.
- MongoDB projections.
- Elasticsearch projections.
- Dashboards.
- Analytics summaries.
- Stale DTOs.

### Authority Verification

Every fact must declare owner, authority source, lifetime, mutability, source version, effective timestamp, and lineage.

Authority failure examples:

- Tenant fact from a request header rather than trusted tenant authority.
- Order total from frontend checkout state rather than SQL payable snapshot.
- Provider capability from documentation or UI configuration rather than certified capability registry.
- Payment state from projection/cache rather than Payment Runtime authority.
- Evidence from raw provider payload before runtime admission.

Authority validation failure fails closed.

### Snapshot Creation

The assembled `PolicyContext` is immutable after assembly. Mutable current-authority facts are copied as evaluation snapshots with source lineage and effective timestamp. Immutable facts are referenced by stable identity and hash where appropriate.

### Version Binding

The Pipeline binds:

- Policy versions.
- Rule versions.
- Catalog version.
- Fact schema version.
- Evaluation semantics version.
- Audit contract version.
- Replay contract version.
- Normalization contract version where separately governed.

Replays must use the exact versions recorded by the original evaluation. Missing versions fail closed with replay-incomplete classification.

### Hash Generation

The Pipeline generates:

- Context hash over normalized input facts and source lineage.
- Version manifest hash.
- Decision hash after composition.
- Audit material hash where required by the audit contract.

Hashes must be deterministic over canonical normalized forms.

### Completeness and Missing Facts

Missing facts are classified as:

| Classification | Meaning | Result |
|---|---|---|
| Required Missing | A selected mandatory policy or rule requires the fact. | Fail closed. |
| Dependency Missing | A dependency policy/rule cannot execute without the fact. | Fail closed. |
| Optional Missing | A declared optional policy/rule could use the fact but is not required. | Warning or skipped optional evaluation. |
| Unknown Fact | Fact appears but is not in the fact schema. | Warning if ignored; fail closed if referenced by policy/rule. |
| Contradictory Fact | Two authoritative facts conflict. | Quarantine, Recovery, ManualReview, or Denied according to policy. |

## Policy Discovery

Policy discovery selects which published policy versions run for one operation intent and context.

Selection inputs:

- Operation intent.
- Tenant/merchant/customer/order/payment scope.
- Requested method/action/provider candidate where applicable.
- Provider-neutral capabilities and constraints.
- Risk, fraud, compliance, settlement, recovery, and evidence posture.
- Policy scope declarations.
- Policy dependencies.
- Effective timestamp.
- Publication status.
- Governance activation scope.

Policy classes:

| Class | Meaning | Missing Behavior |
|---|---|---|
| Mandatory Policies | Required for the operation or domain. | Missing fails closed. |
| Operation-Specific Policies | Required only for matching operations such as refund, capture, void, retry, settlement, COD, wallet, QR, reference, manual review, or recovery. | Missing fails closed when operation matches. |
| Dependency Policies | Required because another selected policy depends on their output. | Missing fails closed. |
| Optional Policies | Evaluated only when published and applicable; absence is certified as safe. | Missing creates warning or skip trace, not allow-by-default. |
| Advisory Policies | Produce warnings or non-terminal constraints only. | Cannot override mandatory deny/review/quarantine/recovery outcomes. |

Deterministic discovery rules:

1. Select only published, effective, scope-compatible policy versions.
2. Resolve operation-specific policies before optional policies.
3. Include all dependencies of selected mandatory and operation-specific policies.
4. Exclude expired or deprecated-for-new-evaluation versions unless replaying a historical decision that used them.
5. Never substitute an older or newer version silently.
6. Never treat no discovered policy as allow.
7. Missing mandatory policy is `PolicyMissing` and fails closed.

## Rule Execution

Rule execution occurs inside policy versions and rule groups. Rules evaluate immutable facts and produce deterministic effects.

Supported execution modes:

| Mode | Meaning | Determinism Requirement |
|---|---|---|
| Sequential | Rules execute in declared order. | Stable order by policy version, group order, priority, and rule id tie-breaker. |
| Priority | Higher-severity or higher-priority rules execute first within a group. | Priority collisions require deterministic tie-breaker or governance rejection. |
| Short-Circuit | A rule may stop later execution according to declared behavior. | Short-circuit must be declared in the rule version and traced. |
| Rule Groups | Rules compose into named groups with group-level effects. | Group order and composition are versioned. |
| Composition | Results combine by all-of, any-of, first-match, threshold, score, precedence, or policy-specific declared semantics. | Composition semantics are part of the evaluation version and policy version. |
| Parallel-Safe Analysis | Independent rules may be identified as parallel-safe for future implementation. | Final result must be identical to declared deterministic order. |

Parallel-safe analysis is architectural permission, not an implementation requirement. Parallel execution is allowed only when:

- Rules read only immutable `PolicyContext`.
- Rules have no side effects.
- Rules have no hidden dependency on clock, random values, provider calls, mutable caches, or external state.
- Composition reorders results back into canonical deterministic order before conflict resolution and audit.
- Audit records the canonical order, not worker timing.

Rule failure handling:

- A missing mandatory rule fails closed as `RuleMissing`.
- A rule exception fails closed according to the rule severity and policy fail-closed contract.
- A side-effect attempt is an architecture violation and fails closed.
- A non-deterministic rule declaration cannot be published; if encountered at runtime, evaluation fails closed.

## Conflict Resolution

Conflict resolution converts multiple rule, group, and policy candidates into one resolved outcome.

Canonical precedence from strongest to weakest:

1. Invalid Authority
2. Replay Failure
3. Version Mismatch
4. Policy Missing / Rule Missing
5. Audit Failure
6. Compliance Deny / Sanctions / Legal Block
7. Fraud Deny / Confirmed Blocklist
8. Tenant, Merchant, Customer, Order, Product, Country, Region, Currency, or Amount Deny
9. Quarantine
10. Recovery
11. Manual Review
12. Retry
13. Alternative Method
14. Alternative Provider
15. Redirect Provider
16. Require Evidence
17. Allow with Constraints
18. Allow

Resolution rules:

- Deny outranks Allow.
- Quarantine outranks Recovery, Manual Review, Retry, Alternative Provider, Alternative Method, and Allow when ambiguity can affect money, tenant scope, provider identity, amount, currency, evidence integrity, or replay safety.
- Recovery outranks Retry when external outcome certainty is missing.
- Manual Review outranks automation when policy requires human approval.
- Compliance and fraud terminal blocks cannot be overridden unless a certified override policy exists and the override itself is selected, authorized, audited, and version-bound.
- Alternative Provider and Alternative Method are allowed only when every mandatory eligibility, authority, risk, fraud, compliance, and constraint policy for the alternative passes.
- Allow is valid only when no mandatory selected policy produces a stronger non-allow outcome.
- Unresolved conflict fails closed to the safest applicable non-allow outcome.

## Decision Composition

Decision composition creates the final immutable `PolicyDecision` returned to Runtime.

Required decision content:

| Field | Purpose |
|---|---|
| Decision Id | Stable identity for the policy decision. |
| Evaluation Id | Links decision to pipeline run. |
| Operation Intent | Declares what was evaluated. |
| Outcome | Allowed, Denied, ManualReview, Quarantine, Retry, Recovery, RedirectProvider, AlternativeProvider, AlternativeMethod, RequireEvidence, or NoDecisionFailClosed. |
| Severity | Terminal block, quarantine, recovery, manual review, constrained allow, allow, warning. |
| Reason Codes | Machine-stable reasons from rules, policies, and conflict resolution. |
| Rule Trace | Rules executed, skipped, failed, short-circuited, and their effects. |
| Policy Trace | Policies selected, skipped, missing, ordered, and evaluated. |
| Conflict Trace | Competing decisions and selected precedence. |
| Context Hash | Hash of normalized immutable input snapshot. |
| Decision Hash | Hash of normalized output decision. |
| Version Linkage | Policy, rule, catalog, fact schema, evaluation, audit, replay, and normalization versions. |
| Audit Linkage | Audit id/reference and audit contract version. |
| Provider Recommendation | Provider-neutral selected provider, provider class, fallback, or alternative only when policy allows it. |
| Method Recommendation | Selected or alternative method only when policy allows it. |
| Required Constraints | Required evidence, action, expiry, retry, recovery, manual review, or settlement constraints. |
| Execution Metadata | Runtime-facing metadata needed to enforce the decision without inventing business rules. |
| Warnings | Non-terminal diagnostics. |
| Failures | Fail-closed diagnostics where applicable. |

Decision composition rules:

- Runtime receives only one final policy decision per evaluation.
- Reason aggregation must preserve all terminal and material non-terminal reason codes.
- Provider recommendation is not provider execution.
- Execution metadata must be provider-neutral.
- The decision must not include provider wire contracts.
- The decision must be replayable from recorded context and versions.

## Failure Model

The Pipeline is fail-closed by default.

| Failure | Meaning | Fail-Closed Result | Recovery |
|---|---|---|---|
| Missing Facts | Required facts absent from `PolicyContext`. | Denied, ManualReview, Quarantine, Recovery, or NoDecisionFailClosed by domain severity. | Reassemble context after authoritative facts are available; do not infer from projections or frontend state. |
| Invalid Authority | Fact source is not current authority, immutable snapshot, or admitted evidence. | NoDecisionFailClosed or Denied; Quarantine if evidence is plausible but ambiguous. | Rebind facts from authoritative source or admitted evidence. |
| Version Mismatch | Fact schema, policy, rule, catalog, evaluation, audit, or replay version is incompatible. | NoDecisionFailClosed. | Use compatible published version or run governed migration/version publication. |
| Policy Missing | Mandatory or operation-specific policy is absent. | NoDecisionFailClosed. | Publish required policy version through governance. |
| Rule Missing | Required rule version is absent. | NoDecisionFailClosed. | Restore replay artifact or publish corrected policy/rule version. |
| Replay Failure | Exact replay artifacts or input snapshot unavailable. | Replay-incomplete fail-closed result. | Restore required artifacts or classify as unreplayable with audit evidence. |
| Audit Failure | Required audit cannot be recorded. | No executable allow decision may be returned. | Retry audit recording if safe; otherwise deny/fail closed and alert operations. |
| Unknown Capability | Capability fact is not in the fact schema or certified registry. | Denied or NoDecisionFailClosed for affected operation/provider. | Certify capability fact and policy handling through governance. |
| Unknown Constraint | Constraint fact is not in the fact schema or certified registry. | Denied, RequireEvidence, Quarantine, or NoDecisionFailClosed. | Certify constraint fact and rule handling through governance. |
| Dependency Cycle | Policy/rule dependencies cannot be ordered. | NoDecisionFailClosed. | Governance correction with new versions. |
| Conflict Unresolved | Candidate outcomes cannot be resolved by precedence. | Highest safe non-allow outcome, otherwise NoDecisionFailClosed. | Governance correction to precedence/composition semantics. |
| Non-Deterministic Rule | Rule depends on mutable external state, random, clock without snapshot, provider call, or cache. | NoDecisionFailClosed and architecture violation. | Reject rule version; publish deterministic replacement. |
| Side-Effect Attempt | Policy/rule/pipeline attempts provider/runtime mutation during evaluation. | NoDecisionFailClosed and architecture violation. | Remove side effect; route execution to Runtime after policy decision. |

Recovery principles:

- Recovery never converts missing authority into allow.
- Recovery may request reassembly from authoritative sources or runtime-admitted evidence.
- Recovery may return a policy `Recovery` decision for Runtime to execute controlled recovery.
- Pipeline recovery is decision recovery, not provider recovery execution.

## Pipeline Events

Pipeline events are audit and observability evidence. They are not business state authority and do not replace `PolicyAudit`.

Canonical events:

| Event | Meaning |
|---|---|
| EvaluationStarted | Evaluation admitted and `PipelineContext` created. |
| ContextAssemblyStarted | Fact collection and source lineage assembly began. |
| FactsAssembled | Candidate facts assembled. |
| AuthorityVerified | Fact authority validation completed. |
| FactNormalizationCompleted | Facts normalized into canonical form. |
| CompletenessValidated | Required and optional fact completeness classified. |
| SnapshotCreated | Immutable `PolicyContext` snapshot created. |
| VersionsBound | Policy/rule/catalog/fact/evaluation/audit/replay versions bound. |
| ContextHashGenerated | Context hash produced. |
| PolicyDiscoveryStarted | Policy selection began. |
| PolicySelected | One policy selected with scope and version. |
| PolicySkipped | Optional/advisory policy skipped with reason. |
| PolicyOrderingCompleted | Deterministic policy order established. |
| RuleGroupStarted | Rule group evaluation began. |
| RuleEvaluated | Rule result recorded. |
| RuleSkipped | Rule skipped because dependency, short-circuit, optional fact absence, or scope mismatch. |
| RuleGroupCompleted | Group result produced. |
| PolicyEvaluated | Policy-level decision candidate produced. |
| ConflictDetected | Competing outcomes identified. |
| ConflictResolved | Final outcome precedence selected. |
| DecisionComposed | Final decision candidate created. |
| AuditRecorded | Required audit record/reference created. |
| DecisionReturned | Decision returned to Runtime. |
| EvaluationCompleted | Evaluation ended with terminal pipeline status. |
| EvaluationFailedClosed | Evaluation failed closed with classified reason. |

Event rules:

- Events must carry evaluation id, operation intent, stage, timestamp, version references where relevant, and bounded reason classifications.
- Events must not carry raw sensitive payloads, provider secrets, or unbounded labels.
- Events are append-only.
- Missing event telemetry does not allow skipping audit.
- Audit is the replay contract; events are operational trace.

## Architecture Rules

1. The Pipeline coordinates evaluation; it does not own facts.
2. The Pipeline never edits policies.
3. The Pipeline never edits rules.
4. The Pipeline never edits governance state.
5. The Pipeline never calls providers.
6. The Pipeline never executes payment operations.
7. The Pipeline never mutates Payment Runtime state.
8. The Pipeline never creates `PaymentTransaction`.
9. The Pipeline never serializes provider wire contracts.
10. `PipelineContext` is internal evaluation coordination state.
11. `PolicyContext` is immutable policy input.
12. Rules evaluate immutable facts only.
13. Policies compose decisions.
14. The Policy Engine orchestrates the Pipeline.
15. Runtime enforces the final policy decision.
16. Adapters expose provider-neutral capabilities, constraints, and evidence candidates only.
17. Deterministic ordering is mandatory.
18. Replay safety is mandatory.
19. Version binding is mandatory.
20. Context hash and decision hash are mandatory.
21. Audit recording is mandatory before returning an executable allow decision.
22. Missing required facts fail closed.
23. Invalid authority fails closed.
24. Missing mandatory policy fails closed.
25. Missing required rule fails closed.
26. Unknown capability or constraint fails closed for affected operation.
27. Silent fallback is forbidden.
28. No discovered policy never means allow.
29. Provider support is not business eligibility.
30. Optional policy absence cannot override mandatory policy requirements.
31. Compliance and fraud terminal blocks outrank allow.
32. Quarantine outranks retry when external outcome certainty is missing.
33. Recovery outranks blind retry.
34. Manual review is a policy outcome.
35. Alternative provider/method requires full eligibility evaluation.
36. Parallel execution is allowed only when canonical deterministic output and audit ordering are preserved.
37. Pipeline events are operational evidence, not business authority.
38. Re-evaluation creates a new evaluation; it does not edit the prior one.
39. Replay uses exact recorded versions and normalized input snapshot.
40. Future providers, methods, capabilities, constraints, policies, rules, facts, and domains enter through governed facts, catalog entries, policy versions, rule versions, and evaluation semantics without Pipeline redesign.

## Extensibility Review

| Future Need | Pipeline Fit | Pipeline Redesign Required |
|---|---|---:|
| New Policies | Add governed policy version, scope, dependencies, ordering, and required facts. | No |
| New Rules | Add governed catalog entry and rule version. | No |
| New Facts | Add fact schema version, authority declaration, normalization, completeness rules, and policies that consume them. | No |
| New Providers | Add provider-neutral capability/constraint/evidence facts and routing/eligibility policies. | No |
| New Capabilities | Add certified capability fact and rules; unknown capabilities fail closed until governed. | No |
| New Constraints | Add certified constraint fact and rules; unknown constraints fail closed until governed. | No |
| New Domains | Add domain policy order, mandatory/optional classification, facts, rules, and conflict precedence where needed. | No |
| New Payment Methods | Add method facts, capability/constraint mapping, and policy/rule versions. | No |
| New Risk/Fraud Controls | Add risk/fraud facts, rule versions, policy versions, and precedence entries. | No |
| New Replay Requirements | Add replay contract version and audit requirements. | No |
| New Audit Fields | Add audit contract version and decision/audit composition requirements. | No |

The Pipeline is future-proof because extension changes are declared in governed facts, versions, policy scopes, rule catalog entries, evaluation semantics, and audit/replay contracts rather than hard-coded runtime or provider branches.

## Certification Decision

Pipeline designed: **Yes**.

Stages completed: **Yes**.

`PipelineContext` defined: **Yes**.

Context Assembly defined: **Yes**.

Policy Discovery defined: **Yes**.

Rule Execution defined: **Yes**.

Conflict Resolution defined: **Yes**.

Decision Composition defined: **Yes**.

Failure Model defined: **Yes**.

Replay-safe: **Yes**. The Pipeline binds exact versions, creates immutable context and decision hashes, records audit/replay metadata, and fails closed when replay artifacts are missing.

Future-proof: **Yes**. New policies, rules, facts, providers, capabilities, constraints, methods, and domains enter through governed versions and fact schemas without Pipeline redesign.

Production-certifiable: **Yes, as architecture.** Runtime implementation, policy persistence, governance UI, rule authoring UI, replay tooling, operational controls, and provider-specific regression suites remain later phases.

Recommended next phase: **PAYMENT-1.3 Policy Persistence, Governance Workflow, and Replay Test Architecture**, followed by implementation planning for policy storage, publication, runtime invocation, audit persistence, replay tooling, and provider-specific policy regression certification.

## Success Answers

Pipeline designed? **Yes**.

Stages completed? **Yes**.

PipelineContext defined? **Yes**.

Context Assembly defined? **Yes**.

Policy Discovery defined? **Yes**.

Rule Execution defined? **Yes**.

Conflict Resolution defined? **Yes**.

Decision Composition defined? **Yes**.

Failure Model defined? **Yes**.

Replay-safe? **Yes**.

Future-proof? **Yes**.

Production-certifiable? **Yes, as certified architecture.**

Recommended next phase? **PAYMENT-1.3 Policy Persistence, Governance Workflow, and Replay Test Architecture.**

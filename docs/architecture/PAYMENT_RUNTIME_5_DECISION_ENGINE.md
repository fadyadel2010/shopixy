# PAYMENT-RUNTIME-5 Canonical Payment Decision Engine

Date: 2026-07-10
Scope: Provider-neutral payment decision production from verified evidence only
Decision: Certified for the PAYMENT-RUNTIME-5 decision-engine scope

## Decision Model

`PaymentDecision` is the canonical immutable runtime output produced after a verification execution.

It contains:

- `DecisionId`
- `DecisionType`
- `DecisionReason`
- `DecisionTime`
- `DecisionSource`
- `DecisionTrace`
- `DecisionMetadata`
- `VerifiedEvidenceId`
- `CorrelationId`

`PaymentDecision` is not `PaymentSession`, not `PaymentAttempt`, not payment state, not settlement, not capture, not refund, and not policy output.

## Decision Runtime

`PaymentDecisionRuntime` receives `VerifiedEvidence`, validates decision-production invariants, evaluates only the canonical runtime verification outcome, and returns exactly one immutable `PaymentDecision` for that verification execution.

The runtime does not:

- Mutate runtime objects.
- Change `PaymentSession`.
- Change `PaymentAttempt`.
- Execute providers.
- Persist data.
- Evaluate policy.
- Transition state.
- Capture, refund, settle, or void money.

## Relationships

The canonical relationship chain is:

```text
VerifiedEvidence
  -> PaymentDecision
```

There is exactly one decision per verification execution. `PaymentDecision` references verified evidence through `VerifiedEvidenceId` only. It never owns or embeds `PaymentSession`, `PaymentAttempt`, providers, transactions, settlements, policy context, or pipeline context.

## Decision Types

Canonical provider-neutral decision types are:

- `Approved`
- `Rejected`
- `ManualReview`
- `Retry`
- `Cancelled`
- `Expired`
- `Recovery`
- `Quarantine`
- `FraudReview`
- `ComplianceReview`
- `Unknown`

`PaymentDecisionRuntime` maps verification success to `Approved`, canonical duplicate evidence failure to `Quarantine`, canonical ordering failure to `Recovery`, and other verification failures to `Rejected`. Other decision types are reserved canonical vocabulary for later certified runtimes and policy boundaries.

## Decision Results

Implemented decision result model:

- `DecisionResult`
- `DecisionStatus`
- `DecisionReason`
- `DecisionSeverity`
- `DecisionTrace`
- `DecisionMetadata`

Decision results are construction-time output details only. They do not authorize state transition, provider execution, persistence, capture, refund, settlement, or policy evaluation.

## Value Objects

Implemented value objects:

- `DecisionId`
- `DecisionReason`
- `DecisionTraceId`
- `DecisionTimestamp`
- `DecisionMetadata`
- `DecisionCorrelationId`
- `DecisionReference`
- `DecisionSource`
- `VerifiedEvidenceId`

Existing verification identifiers remain part of the canonical relationship model.

## Factories

Implemented factories:

- `PaymentDecisionFactory`
- `DecisionResultFactory`

Factories perform construction and invariant validation only. They contain no providers, policy, persistence, state machine, settlement, refund, capture, adapter behavior, or business rules.

## Validation

Decision production validates:

- Verified evidence exists.
- Verification completed.
- Required identifiers are present.
- Decision type is canonical.
- Decision source is present.
- Decision trace is present.
- Decision result belongs to the decision trace.

No business rules are evaluated.

## Architecture Decisions

1. The decision engine lives in `PointOfSale.Core.Payments.Runtime.Domain`.
2. Decision production is provider-neutral and contains no provider-specific names or dependencies.
3. Verification remains separate from decision production.
4. Decision production consumes verified evidence and emits immutable decisions only.
5. Decisions never own runtime aggregates.
6. Decisions never mutate runtime state.
7. Decisions never execute provider APIs.
8. Decisions never evaluate policy.
9. Decisions never write persistence.
10. Decision types are canonical runtime vocabulary, not provider adapter behavior.

## Certification Decision

PaymentDecision implemented: Yes.

Decision Engine implemented: Yes.

Decision Results implemented: Yes.

Relationships implemented: Yes.

Factories implemented: Yes.

Architecture guards added: Yes.

Provider-neutral: Yes.

Decision boundary preserved: Yes.

Production decision engine certified: Yes for verified-evidence-to-payment-decision production only. State machine, persistence, settlement, refund, capture, provider adapters, provider execution, and policy evaluation remain future phases.

Recommended next phase: PAYMENT-RUNTIME-6 state-transition admission architecture, only after the decision-to-state boundary is explicitly authorized.

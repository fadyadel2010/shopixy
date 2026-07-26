# PAYMENT-RUNTIME-9 Runtime Orchestration Engine

Date: 2026-07-10
Scope: Provider-neutral payment runtime orchestration only
Decision: Certified for the PAYMENT-RUNTIME-9 orchestration scope

## Workflow

`PaymentRuntimeOrchestrator` is the canonical runtime workflow coordinator. Every payment provider, manual workflow, COD workflow, receipt workflow, and future provider-neutral payment workflow must enter runtime execution through this orchestrator.

The orchestrator owns sequencing only. It does not own verification logic, decision logic, state transition logic, transaction logic, settlement logic, policy evaluation, provider execution, persistence, accounting, or external APIs.

## Pipeline

```text
ProviderEvidence
  -> EvidenceAdmissionRuntime
  -> EvidenceVerificationRuntime
  -> PaymentDecisionRuntime
  -> StateTransitionRuntime
  -> PaymentTransactionRuntime
  -> PaymentSettlementRuntime
  -> RuntimeResult
```

The orchestrator does not execute providers. Provider adapters and external APIs remain outside this phase. The orchestrator only receives canonical `ProviderEvidence` and delegates to certified runtime stages.

## Execution Context

`RuntimeExecutionContext` is immutable and contains:

- `CorrelationId`
- `PaymentSessionId`
- `ExecutionId`
- `PipelineMetadata`
- `RuntimeTrace`
- `ExecutionTimestamp`

The execution context is construction-only through `RuntimeExecutionFactory`. It carries orchestration identity and trace metadata. It does not authorize payment behavior.

## Execution Result

`RuntimeResult` is immutable and contains:

- `AdmissionResult`
- `VerificationResult`
- `DecisionResult`
- `TransitionResult`
- `TransactionResult`
- `SettlementResult`
- `OverallStatus`
- `ExecutionTrace`

The result also preserves runtime artifacts produced by delegated stages for replay, audit, and certification inspection. It does not persist those artifacts.

## Failure Model

`RuntimeFailure` contains:

- `FailureStage`
- `FailureReason`
- `FailureTrace`
- `FailureMetadata`

The orchestrator fails closed. A failed stage stops the pipeline and returns `RuntimeResult` with `OverallStatus = Failed`. Later stages are not invoked after failure.

## Orchestration Rules

- Each stage executes once on a successful run.
- No successful stage is skipped.
- No stage is repeated.
- Failure stops the pipeline.
- Replay follows the same stage order.
- Execution order is deterministic for a supplied execution context and delegated stage behavior.
- Runtime artifacts are collected in stage order.
- Stages never call each other.
- All stage-to-stage communication flows through the orchestrator.

## Orchestration Invariants

- Orchestrator owns sequencing only.
- Each runtime stage owns itself.
- Stages never call each other.
- All communication flows through the orchestrator.
- Replay always follows identical order.
- Failure stops execution.
- No hidden stage execution is allowed.
- Orchestrator does not call providers.
- Orchestrator does not change runtime state directly.
- Orchestrator does not evaluate policy.
- Orchestrator does not persist data.

## Architecture Decisions

1. The orchestrator lives in `PointOfSale.Core.Payments.Runtime.Domain` with the certified runtime stages.
2. The orchestrator delegates to explicit stage interfaces for admission, verification, decision, transition, transaction, and settlement.
3. Default stage implementations wrap the certified runtime engines without adding behavior.
4. Runtime execution context and runtime result models are immutable and serialization-safe.
5. Runtime failure is explicit, traceable, and fail-closed.
6. Orchestration result collection is not persistence.
7. The orchestrator is provider-neutral and contains no provider-specific code paths.

## Golden Pipelines

Stripe-like:

```text
Evidence
  -> Verification
  -> Decision
  -> Transition
  -> Transaction
  -> Settlement
```

Manual:

```text
Receipt
  -> Verification
  -> Decision
  -> Transition
  -> Transaction
  -> Settlement
```

COD:

```text
Driver Confirmation
  -> Verification
  -> Decision
  -> Transition
  -> Transaction
  -> Settlement
```

## Certification Decision

Runtime Orchestrator implemented: Yes.

Pipeline implemented: Yes.

Execution Context implemented: Yes.

Runtime Result implemented: Yes.

Failure Model implemented: Yes.

Architecture guards added: Yes.

Provider-neutral: Yes.

Workflow authority preserved: Yes.

Production orchestration certified: Yes for orchestration only. Provider execution, persistence, accounting, external APIs, policy evaluation, and business logic remain outside this certification.

Recommended next phase: PAYMENT-RUNTIME-10 certified projection, replay audit, or integration registration only if orchestration remains sequencing-only and provider-neutral.

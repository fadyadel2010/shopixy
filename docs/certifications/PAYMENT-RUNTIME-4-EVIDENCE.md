# PAYMENT-RUNTIME-4 Evidence Admission And Verification Runtime

Date: 2026-07-10
Scope: Provider-neutral evidence admission and verification runtime only
Decision: Certified for the PAYMENT-RUNTIME-4 verification scope

## Admission Runtime

`EvidenceAdmissionRuntime` receives canonical `ProviderEvidence` and validates only runtime-admission invariants:

- Ownership against the supplied `PaymentAction`.
- Evidence and action identifiers.
- Evidence timestamp relative to the action.
- Required evidence references.
- Correlation with the action.
- Provider-neutral structure.

Admission normalizes evidence into immutable `AdmittedEvidence`, stamps an `AdmissionReference`, and computes the runtime canonical evidence hash. It does not validate provider signatures, call provider APIs, evaluate business eligibility, mutate payment state, or persist evidence.

## Verification Runtime

`EvidenceVerificationRuntime` receives `AdmittedEvidence` and an in-memory `EvidenceVerificationContext`. It validates:

- Evidence integrity.
- Structure.
- Required fields.
- Correlation.
- Ownership.
- Duplicate evidence.
- Ordering.
- Runtime canonical hash.

Verification produces immutable `VerifiedEvidence` containing `EvidenceId`, `VerificationId`, `VerificationTime`, `EvidenceHash`, `VerificationResult`, `VerificationTrace`, and `VerificationMetadata`.

## Pipeline

```text
ProviderEvidence
  -> EvidenceAdmissionRuntime
  -> AdmittedEvidence
  -> EvidenceVerificationRuntime
  -> VerifiedEvidence
  -> VerificationResult
```

No payment decision exists in this pipeline.

## Verified Evidence

`VerifiedEvidence` is proof that admitted runtime evidence passed or failed verification checks. It is not `PaymentDecision`, not payment state, not settlement, not capture, not refund, and not policy output.

## Verification Results

Implemented result model:

- `VerificationResult`
- `VerificationStatus`
- `VerificationFailure`
- `VerificationReason`
- `VerificationTrace`
- `VerificationMetadata`

Failures are evidence-verification failures only. They do not authorize or deny payment execution.

## Value Objects

Implemented value objects:

- `VerificationId`
- `VerificationHash`
- `VerificationReason`
- `VerificationTimestamp`
- `VerificationTraceId`
- `AdmissionReference`
- `VerificationMetadata`

Existing evidence identifiers and references remain in the canonical runtime domain.

## Factories

Implemented factories:

- `AdmittedEvidenceFactory`
- `VerifiedEvidenceFactory`

Factories perform construction and invariant validation only. They contain no decisions, provider calls, persistence, state mutation, settlement, refund, capture, or adapter behavior.

## Architecture Decisions

1. Admission and verification live in `PointOfSale.Core.Payments.Runtime.Domain`.
2. The runtime is provider-neutral and contains no provider-specific names or dependencies.
3. Verification is executable runtime behavior, but only for evidence integrity.
4. Verification never creates `PaymentDecision`.
5. Verification never mutates `PaymentSession`.
6. Verification never executes provider APIs.
7. Verification never evaluates policy.
8. Verification never writes persistence.
9. Duplicate and ordering checks use caller-supplied prior admitted evidence only.
10. Hash validation uses the runtime canonical evidence hash, not provider trust.

## Certification Decision

Admission Runtime implemented: Yes.

Verification Runtime implemented: Yes.

VerifiedEvidence implemented: Yes.

VerificationResult implemented: Yes.

Architecture guards added: Yes.

Provider-neutral: Yes.

Decision boundary preserved: Yes.

Production verification runtime certified: Yes for admission and verification only. Payment decisions, policy evaluation, provider adapters, persistence, state machine, settlement, refund, and capture remain future phases.

Recommended next phase: PAYMENT-RUNTIME-5 decision admission/state transition architecture, only after policy and runtime decision boundaries are explicitly authorized.

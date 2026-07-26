# MANUAL-4 Evidence Adapter

Date: 2026-07-16

Scope: Manual Payment evidence adapter translation only.

Status: CERTIFIED - EVIDENCE ADAPTER ONLY

## Executive Summary

MANUAL-4 implements the Manual Payment equivalent inbound evidence adapter. The
adapter accepts proof facts already received by the Application layer and
translates them into canonical `ProviderEvidence` or a provider-neutral
`CanonicalProviderError`.

The adapter owns translation only. It does not upload proof files, persist
evidence, approve payment, reject payment, execute review workflow, call Runtime,
write SQL, mutate lifecycle, replay, recover, checkpoint, or expose controllers.

## Evidence Model

Input evidence facts:

- tenant id;
- payment id;
- payment action id and correlation id;
- Manual Payment channel;
- supported channel facts supplied by the Application layer;
- reference-number requirement and submitted reference number;
- proof identifier;
- proof metadata;
- submission timestamp;
- optional expiration timestamp;
- customer-notes presence;
- instruction version;
- configured provider identity.

The adapter treats proof facts as received metadata only. It does not read,
write, inspect, transform, or store proof content.

## Canonical Translation

Accepted proof submission maps to canonical `ProviderEvidence` with:

- source `manual_payment`;
- evidence type `Receipt`;
- evidence status `PendingReview`;
- canonical metadata `ManualReview`;
- evidence outcome `EvidenceSubmitted` or `ExpiredSubmission`;
- reference fact `ReferencePresent` or `ReferenceMissing`;
- proof fact `ProofPresent`;
- channel, instruction version, submitted timestamp, tenant, payment, and proof
  metadata.

The adapter does not make a payment decision. `PendingReview` is evidence
classification only and does not activate an approval or review workflow.

## Failure Mapping

Fail-closed mappings:

| Condition | Output |
|---|---|
| Missing proof identifier or proof metadata | Rejected `ProviderEvidence` with `MANUAL_EVIDENCE_PROOF_MISSING` |
| Missing reference when required | Rejected `ProviderEvidence` with `MANUAL_EVIDENCE_REFERENCE_MISSING` |
| Unsupported manual channel | Rejected `ProviderEvidence` with `MANUAL_EVIDENCE_CHANNEL_UNSUPPORTED` |
| Configuration/provider mismatch | `CanonicalProviderError` with `MANUAL_EVIDENCE_CONFIGURATION_MISMATCH` |
| Malformed canonical lineage | `CanonicalProviderError` with `MANUAL_EVIDENCE_SUBMISSION_INVALID` |

Rejected evidence remains canonical evidence. It is not an approval, rejection
workflow, durable lifecycle transition, or Runtime result.

## Regression

Focused certification tests were added in:

```text
PointOfSale.Tests/Architecture/ManualPaymentEvidenceAdapterCertificationTests.cs
```

Coverage includes:

- evidence mapping;
- reference mapping;
- proof mapping;
- missing proof rejection;
- missing reference rejection;
- unsupported channel rejection;
- expired submission mapping;
- canonical provider error mapping;
- architecture guards for no Runtime, SQL, persistence, file handling, approval,
  review workflow, controller, lifecycle, HTTP, or configuration ownership.

Certification command:

```text
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~ManualPaymentEvidenceAdapterCertificationTests -nologo -v minimal --no-restore
```

## Certification Decision

| Question | Answer |
|---|---|
| Evidence Adapter implemented? | Yes, translation only. |
| Evidence mapping certified? | Yes. |
| Reference mapping certified? | Yes. |
| Proof mapping certified? | Yes. |
| Rejected evidence certified? | Yes. |
| CanonicalProviderError certified? | Yes. |
| Architecture preserved? | Yes. |
| Runtime preserved? | Yes. No Runtime change. |
| SQL or persistence added? | No. |
| Proof transfer implemented? | No. |
| Approval or review workflow implemented? | No. |
| Lifecycle mutation implemented? | No. |
| Recommend MANUAL-5? | Yes, only if the next phase preserves application-owned orchestration, single-writer lifecycle authority, and the frozen Runtime. |

No permanent cross-provider architecture truth was discovered. No `.ai-context`
update is required.

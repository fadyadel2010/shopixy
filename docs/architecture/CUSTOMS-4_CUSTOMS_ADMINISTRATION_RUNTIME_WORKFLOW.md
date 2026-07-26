# CUSTOMS-4 - Customs Administration Runtime Workflow

Status: Implemented
Date: 2026-07-06
Scope: Backend Customs Administration runtime workflow only

## Executive Summary

CUSTOMS-4 implements the governed backend runtime workflow frozen by
CUSTOMS-3. It adds Customs Administration workflow services, role enforcement,
approval separation, state transition enforcement, publication locking,
single-active publication behavior, append-only workflow audit, and admission
validation for published approved evidence.

This phase does not implement Product UI, POS Admin pages, Checkout changes,
shipment redesign, DHL, FedEx, UPS, carrier adapters, Commercial Invoice
generation, Export Declaration, or International Shipping.

## Runtime Workflow

The runtime workflow is implemented by
`ICustomsAdministrationWorkflowService` and
`CustomsAdministrationWorkflowService`.

Implemented operations:

- Create Draft
- Edit Draft
- Submit For Review
- Approve
- Reject
- Reopen
- Publish
- Retire
- Supersede
- Admission validation

Draft edits create a new immutable `CustomsEvidenceVersion`. Approved and
published versions are not edited in place.

## Authorization Matrix

| Operation | Required role | Separation rule |
|---|---|---|
| Create Draft | Editor | none |
| Edit Draft | Editor | only Draft, Rejected, or Reopened evidence |
| Submit For Review | Editor | exact current version checksum is preserved |
| Approve | Approver | actor must differ from creator, last editor, and submitter |
| Reject | Reviewer or Approver | reason and audit required |
| Reopen | Editor or Reviewer | only Rejected evidence |
| Publish | Publication Operator | approval must exist; publisher differs from editor and approver |
| Retire | Publication Operator | only Published evidence |
| Supersede | Publication Operator | replacement must already be Published |
| Admission validation | Admission Service | evidence must be Published and current |

Unauthorized actors fail closed and append audit evidence.

## State Machine

Runtime states:

```text
Draft
  -> UnderReview
  -> Approved
  -> Published
  -> Retired
  -> Superseded
```

Review path:

```text
UnderReview
  -> Rejected
  -> Reopened
  -> Draft
```

Invalid transitions return `INVALID_TRANSITION` and do not mutate evidence.

## Publication Engine

Publication requires:

- current Approved state;
- current immutable evidence version;
- current approval bound to the evidence checksum;
- publication operator authority;
- publication role separation;
- publication lock row scoped by tenant and product/variant;
- append-only publication record.

Publishing a new approved version for the same tenant/product/variant
supersedes the prior Published evidence for future admission. Historical
shipment snapshots remain immutable.

## Admission Runtime

Shipment admission and direct admission validation now consume only:

- Published evidence;
- exact tenant;
- exact product and variant;
- exact current version;
- exact checksum;
- current approval record;
- current publication record.

Admission fails closed for draft, rejected, retired, superseded, missing
approval, missing publication, wrong tenant, wrong product, wrong version,
wrong checksum, self approval, and invalid transition.

## Audit Runtime

Added append-only audit/evidence records:

- `CustomsEvidenceApproval`
- `CustomsEvidencePublication`
- `CustomsAdministrationAudit`

Existing append-only evidence remains:

- `CustomsEvidenceVersion`
- `CustomsEvidenceHistory`
- `CustomsEvidenceFingerprint`
- `CustomsAdmissionAudit`
- `ShipmentCustomsSnapshot`
- `ShipmentCustomsSnapshotLine`

Audit records include tenant, evidence, version, product/variant, actor, role,
transition, reason, correlation ID, checksum, and failure code where relevant.

## Regression Tests

Added runtime certification tests:

- `CustomsAdministrationRuntimeWorkflowTests`

Coverage:

- workflow runtime;
- authorization;
- approval separation;
- invalid transitions;
- publication;
- single active publication / superseding;
- admission validation;
- retired/superseded rejection;
- append-only audit evidence;
- CUSTOMS-4 report scope.

Existing CUSTOMS-1, CUSTOMS-2, and CUSTOMS-3 certification tests remain in
place.

## Verification

Implementation verification performed:

- backend workflow service added;
- backend DI registration added;
- EF configuration added;
- additive Customs-only migration added;
- shipment Customs admission lookup restricted to Published evidence with
  approval and publication proof;
- focused runtime certification tests added.

No frontend files, UI routes, Checkout redesign, carrier adapters, DHL, FedEx,
UPS, Commercial Invoice, or Export Declaration work was added.

## Risks

- Legacy CUSTOMS-2 `Admitted` evidence is no longer shipment-usable until it is
  governed through approval and publication. This is intentional fail-closed
  behavior.
- Operational UI is still absent; callers must use backend services or future
  approved administration endpoints.
- Production rollout requires role assignment and operator training for
  Editor, Reviewer, Approver, Publication Operator, and Admission Service.

## Final Recommendation

Approve CUSTOMS-4 as the backend Customs Administration Runtime Workflow.

Recommended next phase: Customs Administration API surface and operational
rollout, limited to backend endpoints/DTOs and deployment role assignment. Do
not begin DHL, carrier adapter customs mapping, Commercial Invoice generation,
Export Declaration, Product UI, Checkout redesign, or international shipping
expansion until runtime evidence coverage is operational.

## Explicit Success Answers

- Reviewed ONLY Customs Administration Runtime? **Yes.**
- Runtime modified? **Yes.**
- Backend modified? **Yes.**
- Frontend modified? **No.**
- Workflow implemented? **Yes.**
- Authorization implemented? **Yes.**
- State machine implemented? **Yes.**
- Publication implemented? **Yes.**
- Admission enforced? **Yes.**
- Audit enforced? **Yes.**
- Backward compatible? **Yes, schema-additive; legacy unapproved/unpublished evidence intentionally fails closed for new shipment admission.**
- Production runtime ready? **Yes for backend workflow enforcement; operational rollout still requires role assignment and approved API exposure.**
- Recommend next phase: **Customs Administration API surface and operational rollout.**

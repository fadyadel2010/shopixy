# CUSTOMS-3 - Customs Administration and Admission Authority

Status: Governance authority freeze
Date: 2026-07-06
Scope: Customs Administration Authority and Admission Governance only
Runtime impact: none
Backend runtime impact: none
Frontend impact: none
Product UI impact: none
Shipment redesign impact: none
Carrier adapter impact: none
International shipping implementation impact: none

## Executive Summary

CUSTOMS-3 freezes the administrative governance required before Customs
Evidence can be admitted into shipment registration. It defines who may create,
edit, review, approve, publish, retire, correct, reopen, and use Customs
Evidence, and it binds those actions to immutable versions and append-only
audit.

This is not a DHL phase, not a carrier adapter phase, not Product UI, not POS
Admin screens, not Checkout, not Commercial Invoice generation, and not an
international shipment redesign.

The approved authority chain remains:

```text
Product source evidence
  -> Customs Administration governance
  -> Customs Evidence approval and publication
  -> Shipment Customs Snapshot admission
  -> Carrier-neutral Customs DTO
  -> Carrier Adapter serialization
```

Product remains source evidence only. Customs owns admitted evidence. Shipment owns the immutable shipment customs snapshot. Carrier adapters consume only the admitted snapshot-derived DTO.

## Authority Matrix

| Question | Authority owner | Allowed action | Forbidden authority |
|---|---|---|---|
| Who may create Customs Evidence? | Customs Evidence Editor | Create `Draft` evidence from product/order/geographic/source evidence candidates | Carrier adapters, Product UI alone, Checkout, browser, provider payloads |
| Who may edit Customs Evidence? | Customs Evidence Editor | Edit only `Draft`, `Rejected`, or `Reopened` evidence before approval | Editing `Approved`, `Published`, `Retired`, `Superseded`, or snapshot-used versions |
| Who may approve Customs Evidence? | Customs Evidence Approver | Approve `Under Review` evidence after validation; approver must differ from creator/editor | Self-approval, carrier/provider approval, frontend approval |
| Who may publish Customs Evidence? | Customs Publication Operator | Publish approved evidence versions for future shipment admission | Publishing drafts, rejected evidence, unapproved evidence, or stale validation |
| Who may retire Customs Evidence? | Customs Publication Operator with approval evidence | Retire a published evidence version for future admissions only | Rewriting historical snapshots or deleting evidence |
| Who may correct Customs Evidence? | Customs Evidence Editor proposes; Customs Approver approves; Publication Operator publishes | Create a new evidence version and supersede the old published version | Mutating an approved or published version in place |
| Who may reopen rejected evidence? | Customs Evidence Editor or Review Coordinator | Reopen `Rejected` evidence to `Draft` with reason and audit | Reopening `Retired`, `Superseded`, `Approved`, or `Published` evidence |
| Who may use draft evidence? | Customs administrators for review only | Draft evidence may be previewed, validated, and reviewed | Shipment registration, carrier DTOs, adapters, provider calls, recovery |
| Who admits evidence into shipment? | Customs Admission service under backend authority | Admit only published, approved, tenant/product/version-matching evidence | Product, Checkout, frontend, adapter, provider, cache, projection |
| Who owns admitted Customs Evidence? | Customs Platform | Current published version for future admissions and immutable historical versions | Product edits, carrier response updates, regenerated invoice artifacts |
| Who owns shipment history? | Shipment owns the immutable shipment customs snapshot | Immutable Shipment Customs Snapshot created from admitted evidence | Customs correction rewriting existing shipment snapshots |

Role separation is mandatory for production publication. The actor who last
created or materially edited evidence cannot be the sole approver for that same
version. Publication requires an approved validation snapshot and append-only
audit evidence.

## Governance Model

Customs Administration is a governed backend administration authority composed
of four workspaces:

1. Evidence Staging creates and edits draft Customs Evidence from source
   evidence candidates.
2. Evidence Review validates required customs facts, source fingerprints,
   tenant/product lineage, country support, financial basis, units, and
   version consistency.
3. Evidence Approval accepts or rejects reviewed evidence with reason,
   validation checksum, and role separation.
4. Evidence Publication publishes, retires, supersedes, and audits immutable
   approved versions for future shipment admission.

Drafts, rejected evidence, retired evidence, superseded evidence, and evidence
with missing publication are not runtime shipment authority.

CUSTOMS-3 does not choose screen placement or implement an administration UI.
Any future UI is a candidate-entry surface only and must submit actions to the
governed Customs Administration backend.

## Evidence Lifecycle

The governed lifecycle is:

```text
Draft
  -> Under Review
  -> Approved
  -> Published
  -> Retired
  -> Superseded
```

Additional review states:

```text
Under Review
  -> Rejected
  -> Reopened
  -> Draft
```

Valid transitions:

| From | To | Required control |
|---|---|---|
| None | Draft | Create evidence with tenant, product, source evidence fingerprint, actor, and correlation ID |
| Draft | Under Review | Validation request with complete required evidence and version checksum |
| Under Review | Approved | Approver authorization, approval separation, validation checksum agreement, reason/evidence |
| Under Review | Rejected | Reviewer or approver rejection reason and audit |
| Rejected | Reopened | Reopen reason and editor/review coordinator authority |
| Reopened | Draft | Editable draft version resumes with prior rejection lineage |
| Approved | Published | Publication operator authority, publication lock, approval still current |
| Published | Retired | Retirement approval, reason, effective timestamp, future-admission block |
| Published | Superseded | New approved/published correction version replaces it for future admissions |

Invalid transitions must fail closed, including:

- `Draft` -> `Published`;
- `Draft` -> shipment admission;
- `Rejected` -> shipment admission;
- `Retired` -> shipment admission;
- `Superseded` -> new shipment admission unless an explicit historical reuse
  policy applies to an already-created snapshot;
- `Approved` mutation in place;
- `Published` mutation in place;
- self-approved publication;
- publication without approval;
- publication after draft checksum changes.

## Approval Workflow

The approval workflow is:

```text
Create Draft
  -> Validate Evidence
  -> Submit Under Review
  -> Approve or Reject
  -> Publish Approved Version
  -> Admit Published Version into Shipment Snapshot
```

Approval rules:

- Evidence must bind tenant, product, variant where applicable, source
  evidence fingerprint, country evidence, HS code, commodity, declared value,
  currency, units, quantity basis, net weight, and optional dangerous-goods or
  export classification where modeled.
- Approval is explicit and audited.
- Approval binds to the exact evidence version and validation checksum.
- Any material edit invalidates prior approval and returns the evidence to
  `Draft`.
- Approval does not make evidence shipment-usable until publication succeeds.
- Rejection requires a non-PII reason and blocks shipment admission.
- Reopened rejected evidence returns to draft governance and must be reviewed
  again.

## Admission Governance

Shipment admission can consume only evidence that satisfies every condition:

- tenant matches the shipment tenant;
- product and variant match the shipment/order line;
- evidence status is `Published`;
- evidence version is the exact published version requested or selected;
- approval exists and is current for the version checksum;
- publication exists and is current for the version;
- required Customs facts are complete;
- evidence is not rejected, retired, superseded for new admission, deleted, or
  draft;
- source evidence fingerprint and admitted version match the snapshot request.

Admission produces a Shipment Customs Snapshot. After snapshot creation,
shipment history is owned by Shipment and must not be rewritten by later
Customs corrections, product edits, carrier retries, provider responses,
invoice artifacts, tracking, webhooks, cache, or projections.

## Versioning Rules

Approved evidence cannot mutate.

Versioning rules:

- Every approval applies to one immutable evidence version.
- Every publication applies to one immutable approved evidence version.
- Corrections create a new evidence version.
- A correction may supersede a prior published version for future admissions
  only.
- Historical shipments continue to reference the evidence version frozen in
  their Shipment Customs Snapshot.
- Evidence retirement blocks future admissions but never rewrites history.
- Version mismatch between requested evidence, approved version, published
  version, and snapshot line fails closed.
- Reopening rejected evidence keeps rejection lineage and creates or resumes an
  editable draft; it does not mutate an approved or published version.

## Audit Model

Customs audit is append-only and mandatory.

Audited events:

- Creation;
- Review;
- Approval;
- Publication;
- Correction;
- Retirement;
- Rejection;
- Version creation;
- Snapshot admission.

Every audit record must include tenant ID, product ID, variant ID where
applicable, Customs Evidence ID, evidence version, prior version where
applicable, status transition, actor/system, role/capability, timestamp,
correlation ID, source evidence fingerprint, validation checksum, reason where
required, and non-PII failure reason for rejected operations.

Audit must not be updated in place to erase or restate governance history.
Compensation, correction, retry, and rejection create new audit records.

## Fail-Closed Rules

Shipment registration must reject before carrier adapter or provider I/O when
any of the following is true:

- Draft evidence in shipment;
- Rejected evidence in shipment;
- Retired evidence in shipment;
- Superseded evidence in new shipment admission;
- Wrong tenant;
- Wrong product;
- Wrong variant where applicable;
- Version mismatch;
- Missing approval;
- Missing publication;
- Self-approval where separation is required;
- Stale validation checksum;
- Evidence status is not `Published`;
- Required HS code, commodity, country of origin, declared value, currency,
  quantity, unit, net weight, or governed optional classification is missing;
- Shipment snapshot request conflicts with Customs Evidence version or source
  evidence fingerprint.

Fail closed means no provider call, no label request, no successful carrier
registration, no carrier-neutral DTO emission, and no adapter-supplied
substitute Customs authority.

## Regression Strategy

Architecture and governance certification tests freeze:

- Customs Administration Authority;
- authority matrix and role separation;
- governance model;
- approval workflow;
- publication requirement;
- evidence lifecycle and valid transitions;
- invalid transition rejection;
- immutable version governance;
- append-only audit governance;
- shipment admission fail-closed rules;
- Product as source evidence only;
- Customs as admitted evidence owner;
- Shipment as immutable snapshot owner;
- Carrier as admitted snapshot consumer only;
- no Product UI, Checkout, POS Admin screen, shipment redesign, DHL, FedEx,
  UPS, carrier adapter, commercial invoice, export declaration, or
  international shipping implementation in this phase.

## Risks

| Risk | Status | Mitigation |
|---|---|---|
| Draft or rejected evidence leaks into shipment registration | Mitigated by governance | Admission requires published, approved evidence and fails closed otherwise. |
| Approved evidence mutates in place | Mitigated by governance | Approved and published versions are immutable; corrections create new versions. |
| Self-approval weakens evidence quality | Mitigated by governance | Approval separation is mandatory for production publication. |
| Retirement rewrites historical shipment customs | Mitigated by governance | Retirement blocks future admissions only; historical snapshots remain unchanged. |
| Product UI becomes Customs authority | Mitigated by governance | Product remains source evidence only; Customs owns admitted evidence. |
| Carrier adapter uses draft or missing evidence | Mitigated by governance | Carrier DTO emission requires admitted shipment snapshot; adapters serialize only. |
| Operational workflows are not implemented yet | Accepted | This phase freezes governance and tests only; runtime administration workflow implementation is a later phase. |

## Final Recommendation

Approve CUSTOMS-3 as the Customs Administration and Admission Governance
authority freeze.

The next phase should be **Customs Administration Runtime Workflow
Implementation**, limited to backend governance APIs/services, authorization,
state transitions, validation, publication locks, and audit enforcement. It
must not implement Product UI, Checkout, shipment redesign, DHL, FedEx, UPS,
carrier adapters, commercial invoice generation, export declarations, or
international shipping expansion.

## Explicit Success Answers

- Reviewed ONLY Customs Administration? **Yes.**
- Runtime modified? **No.**
- Backend modified? **No runtime backend files modified; architecture tests added.**
- Frontend modified? **No.**
- Governance implemented? **Yes, as architecture/governance certification.**
- Approval workflow defined? **Yes.**
- Evidence lifecycle frozen? **Yes.**
- Versioning governed? **Yes.**
- Audit governed? **Yes.**
- Backward compatible? **Yes; no runtime or schema behavior changed. Existing CUSTOMS-2 fail-closed runtime remains unchanged.**
- Production governance ready? **Yes as a governance freeze; runtime workflow implementation and operational role assignment remain required before production administration.**
- Recommend next phase: **Customs Administration Runtime Workflow Implementation.**

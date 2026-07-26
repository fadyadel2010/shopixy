# CUSTOMS-2 - Customs Evidence Runtime Foundation

Status: Implemented
Date: 2026-07-06
Scope: Carrier-neutral Customs Runtime Foundation only

## Executive Summary

CUSTOMS-2 implements the carrier-neutral Customs Evidence runtime required by
CUSTOMS-1. It adds admitted Customs Evidence, immutable evidence versions,
shipment-owned Customs Snapshots, append-only audit/history/fingerprints,
fail-closed admission, snapshot-only recovery, and a carrier-neutral Customs
DTO.

This is not a DHL, FedEx, UPS, carrier-adapter, international-shipping,
Checkout, POS Admin, or Product UI implementation.

## Runtime Model

Product customs data is source evidence only. It becomes carrier-usable only
after backend Customs admission creates `CustomsEvidence` plus an immutable
`CustomsEvidenceVersion`.

Shipment registration consumes admitted Customs evidence only. Cross-border
carrier registration acquires or reuses a `ShipmentCustomsSnapshot` before any
carrier adapter call. Domestic same-country registration is unchanged.

## SQL Model

The additive migration is
`20260706143000_Customs2EvidenceRuntimeFoundation`.

Carrier-neutral tables added:

- `CustomsEvidence`
- `CustomsEvidenceVersions`
- `CustomsEvidenceHistory`
- `CustomsEvidenceFingerprints`
- `ShipmentCustomsSnapshots`
- `ShipmentCustomsSnapshotLines`
- `CustomsAdmissionAudit`

No Product, Order, Checkout, DHL, FedEx, UPS, carrier-specific, or
`exportDeclaration` columns were added.

## Admission Pipeline

`ICustomsEvidenceAdmissionService` admits evidence after validating HS code,
commodity, country of origin, declared value, currency, weight, units,
supported country, and source evidence JSON.

Fail-closed admission rejects:

- missing HS code;
- missing commodity;
- missing country of origin;
- missing declared value;
- missing currency;
- invalid quantity;
- invalid weight;
- invalid units;
- unsupported country;
- malformed evidence;
- missing admitted evidence for shipment snapshot creation.

## Versioning Model

Evidence versions are immutable and append-only. Corrections create a new
`CustomsEvidenceVersion`, update the parent evidence current-version pointer,
write `CustomsEvidenceHistory`, and store a SHA-256 fingerprint.

Historical shipments continue using the snapshot lines created from the
original admitted evidence version.

## Snapshot Model

`ShipmentCustomsSnapshot` is shipment-owned. It freezes snapshot version,
carrier-neutral DTO version, origin/destination countries, gross weight,
fingerprint, and JSON audit payload.

`ShipmentCustomsSnapshotLine` freezes each order line's admitted Customs
evidence version, HS code, commodity, origin country, value/currency, quantity,
unit, net weight, dangerous-goods flag, and export classification where
modeled.

## Recovery Model

Recovery reads only SQL `ShipmentCustomsSnapshots` and
`ShipmentCustomsSnapshotLines`.

Recovery never rebuilds customs from current Product, current Catalog, carrier
payloads, labels, invoices, Mongo, Redis, browser state, cache, or projection
state. Missing or malformed SQL snapshot evidence fails closed.

## Audit Model

`CustomsAdmissionAudit` records evidence admission/rejection, snapshot creation,
snapshot reuse, carrier-neutral DTO emission, and recovery rejection. Evidence
history and fingerprints provide immutable version lineage.

## Regression Tests

Runtime certification tests were added in
`PointOfSale.Tests/Architecture/CustomsEvidenceRuntimeFoundationTests.cs`.

They verify:

- carrier-neutral schema and no provider-specific fields;
- admission fail-closed rules;
- immutable versioning and append-only snapshots/audit;
- shipment registration snapshot acquisition before adapter I/O;
- recovery reuses SQL snapshot only;
- carrier-neutral DTO remains provider-neutral.

## Verification

- Carrier owns nothing.
- Product is candidate/source evidence only.
- Shipment owns immutable Customs Snapshot.
- Evidence is versioned.
- Recovery uses SQL snapshot only.
- Infrastructure build passed with 0 errors; pre-existing warnings remain.

## Risks

- Existing cross-border carrier-managed shipments without admitted Customs
  evidence now fail closed before provider I/O.
- Product UI and operational admission workflows are not implemented in this
  phase.
- Carrier-specific mapping into DHL/FedEx/UPS/provider request contracts remains
  future work.
- Commercial invoice PDF generation remains future work and is not authority.

## Final Recommendation

Approve CUSTOMS-2 as the carrier-neutral Customs Evidence Runtime Foundation.
Next phase should certify operational Customs evidence admission rollout and
then carrier-specific contract mapping.

## Explicit Success Answers

- Reviewed ONLY Customs Runtime Foundation? **Yes.**
- Runtime modified? **Yes.**
- Backend modified? **Yes.**
- Frontend modified? **No.**
- SQL added? **Yes.**
- Migration added? **Yes, `20260706143000_Customs2EvidenceRuntimeFoundation`.**
- Admission implemented? **Yes.**
- Versioning implemented? **Yes.**
- Snapshot implemented? **Yes.**
- Recovery implemented? **Yes.**
- Carrier-neutral DTO implemented? **Yes.**
- Backward compatible? **Schema-additive; cross-border registration without admitted Customs evidence intentionally fails closed.**
- Production foundation ready? **Yes, as a carrier-neutral foundation; operational evidence coverage is required before enabling cross-border carrier traffic.**
- Recommend next phase: **Customs operational admission rollout and carrier-specific contract mapping certification.**

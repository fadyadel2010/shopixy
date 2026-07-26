# SHIP-ORIGIN-1 — Canonical Shipment Origin Authority

Status: Implemented  
Date: 2026-07-06  
Scope: Shipment Origin Authority only

## Executive Summary

SHIP-ORIGIN-1 removes carrier configuration from shipment-registration origin
authority. Carrier-managed registration now requires an active tenant warehouse,
an admitted `AddressAdmissionEvidence` row owned by that warehouse, active
canonical places, and an active supported country profile. Those facts are
serialized into an immutable `ShipmentOriginDto` and persisted on `Shipment`
before carrier invocation.

Carrier configuration remains credential, environment, and carrier-eligibility
configuration. Its pickup text is no longer read when building either initial
or recovery carrier registration requests.

## Architecture

Geographic continues to own canonical identity and admission. Warehouse owns
the operational location lifecycle. Shipment owns the immutable historical
origin snapshot. The carrier adapter receives a carrier-neutral DTO and has no
authority to query, infer, repair, or replace origin.

No Checkout, destination geography, Geographic Catalog, POS Admin, Warehouse
administration, carrier administration, tracking, or label design was changed.

## Authority Matrix

| Concern | Authority |
|---|---|
| Operational origin selection | Order-bound tenant warehouse |
| Canonical place identity | Admitted warehouse `AddressAdmissionEvidence` |
| Country support | Active canonical `GeographicCountryProfile` |
| Origin snapshot | SQL `Shipment` |
| Origin DTO | Backend `ShipmentOriginDto` |
| Wire serialization | Carrier adapter |
| Recovery | Persisted `Shipment.OriginSnapshotJson` |
| Carrier pickup text | Candidate/configuration only; never registration authority |
| Cache, provider response, projection, browser | Never origin authority |

## Pipeline

```text
Order-bound Warehouse
  -> admitted Canonical Geographic Identity
  -> Shipment Origin admission validation
  -> immutable Shipment Origin Snapshot
  -> existing Carrier Geographic Resolution boundary
  -> carrier-neutral CreateShipmentRequest
  -> Carrier Adapter
  -> Provider
```

## Snapshot Model

The shipment snapshot contains warehouse ID, admission evidence ID, catalog
release ID, canonical country/administrative-area/locality/district/zone IDs,
country ISO-2, frozen display names, warehouse contact/name/street evidence,
snapshot timestamp, and snapshot version. The same model is serialized as JSON
for deterministic recovery and retained in indexed scalar columns for audit and
operational inspection.

The additive migration is
`20260706120000_ShipOrigin1CanonicalShipmentOrigin`.

## Validation

Registration fails before provider I/O when any of the following is true:

- the order has no warehouse;
- the warehouse is missing, deleted, inactive, cross-tenant, or incomplete;
- no admitted warehouse-owned address evidence exists;
- canonical country is missing;
- any referenced canonical place is missing or inactive;
- no active country profile exists;
- country ISO-2 is missing;
- the persisted snapshot is malformed, cross-linked, incomplete, or uses an
  unsupported snapshot version.

Persistence also rejects partial origin snapshots.

## Recovery

Recovery deserializes `OriginSnapshotJson`, checks its version and linkage
against the indexed shipment columns, and reuses it. Once external registration
has completed, a missing or malformed snapshot fails closed. Recovery never
reads live carrier pickup configuration and never rebuilds a successful
registration's origin from current warehouse, catalog, or publication state.

## Audit

Successful snapshot creation writes `ShipmentOriginAdmitted` with tenant,
shipment, catalog release, ISO country, provider, correlation, and warehouse
admission lineage. Rejection writes `ShipmentOriginAdmissionFailure` and stores
the normalized `SHIPMENT_ORIGIN_NOT_ADMITTED` registration failure.

## Regression Tests

`ShipmentOriginAuthorityTests` freezes:

- first-class shipment origin persistence;
- the carrier-neutral origin DTO;
- prohibition on carrier-configuration origin construction;
- warehouse admission as canonical evidence;
- recovery deserialization;
- persistence immutability.

Existing GEO-8, GEO-9, Aramex, and carrier tests remain in the verification
slice.

## Verification

- Infrastructure build: passed, 0 errors (pre-existing warnings remain).
- Focused Shipment Origin / GEO-8 / GEO-9 / Aramex slice: 28 passed, 0 failed.
- Full solution build: passed, 0 warnings, 0 errors.

## Risks

- Existing orders without an order-bound warehouse now fail carrier
  registration closed.
- Existing warehouses need a governed admitted `AddressAdmissionEvidence` row,
  complete operational contact data, and a supported active country profile.
- The migration is additive and nullable for legacy compatibility, but new
  carrier registration intentionally requires complete origin evidence.
- Deployment readiness depends on populating governed warehouse admission
  evidence before enabling registration traffic.

## Final Recommendation

Approve SHIP-ORIGIN-1 for deployment after the additive migration is applied and
each shipping warehouse has reviewed admitted canonical evidence. The next
phase should be operational warehouse-origin admission coverage and rollout
certification, not a Checkout, carrier, Geographic Catalog, or Warehouse
redesign.

## Explicit Success Answers

- Reviewed ONLY Shipment Origin? **Yes.**
- Runtime files modified: **Yes; shipment registration and persistence only.**
- Backend files modified: **Yes.**
- Frontend files modified: **No.**
- Shipment Origin implemented? **Yes.**
- Origin Admission implemented? **Yes.**
- Origin Snapshot implemented? **Yes.**
- Origin immutable? **Yes, after snapshot creation and therefore after successful registration.**
- Carrier Configuration still authoritative? **NO.**
- Recovery uses Snapshot? **Yes.**
- Backward compatible? **Schema-additive; legacy/incomplete origins intentionally fail closed for new carrier registration.**
- Production-certifiable? **Yes, conditional on migration deployment and admitted warehouse-origin coverage.**
- Recommend next phase: **Warehouse-origin admission coverage and production rollout certification.**

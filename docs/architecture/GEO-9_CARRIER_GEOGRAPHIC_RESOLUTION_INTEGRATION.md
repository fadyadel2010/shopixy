# GEO-9 - Carrier Geographic Resolution Integration

Status: Implemented  
Date: 2026-07-05  
Scope: Carrier geographic resolution during shipment registration only

## Executive Summary

GEO-9 connects immutable shipment canonical geography to published,
provider-scoped carrier mappings before carrier invocation. Provider identifiers
are persisted only in shipment registration evidence and are passed to adapters
as resolved DTO values. Orders, customer addresses, Checkout, and generic
addresses remain provider-independent.

## Runtime Changes

- `CarrierShipmentService` resolves a shipment's copied GEO-8 lineage through
  `ICarrierGeographicMappingService` before the first provider network call.
- The mapping release/version/scope and resolved provider items are persisted on
  `Shipment` as immutable registration evidence.
- Recovery deserializes the stored snapshot and never resolves against the
  current mapping release.
- Missing canonical identity or inadmissible mappings fail before adapter I/O.

## Carrier Resolution Pipeline

```text
Shipment registration
  -> immutable shipment destination snapshot
  -> published provider/environment/account mapping resolution
  -> immutable carrier mapping evidence on Shipment
  -> resolved provider-geography DTO
  -> carrier adapter
  -> carrier response admission
  -> registered Shipment
```

## Authority Matrix

| Concern | Authority |
|---|---|
| Canonical identity | Order-derived immutable Shipment snapshot |
| Provider mapping | Published SQL carrier mapping release |
| Resolution | `CarrierGeographicMappingService` |
| Registration evidence | SQL `Shipment` |
| Wire serialization | Carrier adapter |
| Recovery | Shipment mapping snapshot |
| Adapter lookup, alias, hierarchy inference | Forbidden |

## Snapshot Model

Shipment registration evidence persists mapping release ID, release number,
release key, environment, account scope, resolution timestamp, provider
geographic type, ID, code, display name, and parent provider ID. The provider
items live only in `CarrierGeographicMappingSnapshotJson`; the release evidence
is also indexed on `Shipment`. EF and SQL guards reject partial evidence and EF
blocks mutation after the snapshot has been acquired.

## Audit

Successful resolution writes `CarrierResolution` audit evidence covering
resolved mapping, mapping release, and shipment registration resolution.
Failures write `CarrierResolutionFailure` with tenant, provider, catalog
release, mapping release when known, environment/scope, and correlation.

## Recovery

If a mapping snapshot exists, recovery validates and reuses it. It does not call
the mapping service. A malformed, cross-shipment, carrier-mismatched,
country-mismatched, or incomplete snapshot fails closed.

## Regression Tests

`CarrierGeographicResolutionIntegrationTests` freezes resolver placement before
adapter calls, published/scope/provider/country validation, ambiguity rejection,
adapter isolation for Bosta, Aramex, Mylerz, DHL, and Manual, recovery snapshot
reuse, and provider-ID exclusion from Orders and customer addresses.

## Verification

- Infrastructure build: passed.
- Test-project build: passed.
- Focused GEO-9/Bosta/GEO-8/shipment slice: passed after GEO-8 change-tracker
  enumeration was materialized before audit insertion.
- Full solution build passed with 0 errors.
- Broad Carrier/Geographic/Shipment/Bosta/Aramex/Mylerz/DHL slice passed:
  152 passed, 0 failed.
- Migration discovery includes
  `20260705200000_Geo9CarrierGeographicResolution`.

## Risks

- Published mapping data must use operation key `ShipmentRegistration` and the
  tenant-provider-account scope expected by runtime.
- Legacy carrier-managed shipments without canonical snapshots now fail closed;
  they require governed admission/migration rather than text inference.
- Provider wire contracts may use different geographic type labels; governed
  mappings must use certified types. Bosta retains its city/district/zone DTO
  compatibility fields derived from the resolved snapshot.

## Provider Compatibility

| Provider | GEO-9 behavior |
|---|---|
| Bosta | Resolved City/Locality, District/Area, and Zone IDs populate certified request fields. |
| Aramex | Receives resolved provider geography; performs no lookup. |
| DHL | Receives resolved provider geography; performs no lookup. |
| Mylerz | Receives resolved provider geography; performs no lookup. |
| Manual | Performs no external registration and receives no provider identifiers. |

No adapter references EF, SQL, the canonical catalog, country profiles,
hierarchy, localization, aliases, drafts, or the mapping service.

## Final Recommendation

Approve GEO-9 for production once the target environment has published,
reviewed mapping releases with complete carrier/country coverage and the
additive migration is deployed. The next phase should certify provider mapping
data coverage and operational release rollout; it should not redesign Shipping,
Checkout, or Orders.

## Explicit Success Answers

- Reviewed ONLY Carrier Geographic Resolution? **Yes.**
- Runtime files modified: **Yes, shipment registration and geographic resolver only.**
- Backend files modified: **Yes.**
- Frontend files modified: **No.**
- Carrier Resolution implemented: **Yes.**
- Carrier Snapshot implemented: **Yes.**
- Adapters isolated: **Yes.**
- Authority preserved: **Yes.**
- Provider IDs leaked outside Shipment? **NO.**
- Backward compatible: **Schema-additive; legacy carrier registration without canonical evidence intentionally fails closed.**
- Production-certifiable: **Yes, conditional on published mapping data and migration deployment.**
- Recommend next phase: **Provider mapping coverage and production rollout certification.**

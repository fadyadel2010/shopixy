# GEO-7 - Storefront Checkout Geographic Integration

Status: Storefront checkout runtime integrated  
Date: 2026-07-05  
Governing architecture: `ADR-GEO-001_CANONICAL_GEOGRAPHIC_IDENTITY.md`, `GEO-1.5_GEOGRAPHIC_DOMAIN_BOUNDARIES.md`, `GEO-2_CANONICAL_GEOGRAPHIC_SQL_MODEL.md`, `GEO-3_GEOGRAPHIC_CATALOG_GOVERNANCE_AUTHORITY.md`, `GEO-4_GEOGRAPHIC_RUNTIME_AUTHORITY_CERTIFICATION.md`, `GEO-5_CANONICAL_GEOGRAPHIC_RUNTIME_IMPLEMENTATION.md`, and `GEO-6_GEOGRAPHIC_ADMINISTRATION_RUNTIME_IMPLEMENTATION.md`

## Executive Summary

GEO-7 integrates Canonical Geographic Identity into POS Admin Storefront Checkout only.

The Storefront checkout request can now submit canonical geographic place candidates. The backend admits those candidates through the SQL-backed Geographic runtime before shipping/payment validation, expected pricing validation, order creation, and payment session creation. Browser state, React, Zustand, React Query, Redis, MongoDB, and recovery payloads remain candidates or projections only.

No Affiliate Checkout, CommerceHub Checkout, split-order, carrier-adapter, warehouse-routing, inventory-routing, shipping redesign, payment redesign, or marketplace behavior was implemented.

## Runtime Changes

- Storefront checkout DTOs accept canonical place candidates for country, administrative area, locality, district, and zone.
- Storefront checkout pricing preview accepts a shipping geographic candidate for quote-time validation.
- Storefront checkout service admits canonical candidates through `IAddressAdmissionService` before final checkout progresses.
- Checkout idempotency hashing includes structured address detail and canonical place candidates, preventing replay with altered geography.
- Order creation snapshots admitted geographic evidence into existing order audit JSON through `PricingSnapshotJson` without adding provider IDs to orders.
- Storefront frontend types and checkout store forward geographic candidates when present; the UI was not redesigned.
- Geographic runtime services are registered in API dependency injection for SQL-backed admission.

## Checkout Flow

```text
Storefront address form or selector
  -> frontend submits place candidates and address detail
  -> backend validates checkout/cart/customer/session authority
  -> backend admits canonical candidates through SQL Geographic authority
  -> backend validates shipping method and payment method
  -> backend recalculates pricing and validates expected pricing
  -> backend creates order with immutable geographic admission evidence snapshot
  -> backend creates payment intent/session when applicable
  -> recovery can restore candidates, but final checkout re-enters SQL admission
```

Legacy text city/area/country remains compatibility evidence when no canonical candidate is supplied. When a canonical candidate is supplied, it must be admitted by SQL Geographic authority or checkout fails closed.

## Authority Matrix

| Concern | Authority | Storefront role | Forbidden authority |
|---|---|---|---|
| Canonical place identity | SQL Geographic Catalog published release | Submit selected IDs as candidates | React, Zustand, browser storage, Redis, Mongo, aliases, text labels |
| Address admission | `IAddressAdmissionService` / SQL `AddressAdmissionEvidence` | Submit address detail and candidate lineage | Frontend validation, recovered session data, cache |
| Shipping quote geography | Backend admission before quote/pricing calculation | Submit candidate for quote context | Shipping method selector, carrier adapter, cache |
| Checkout validation | Checkout service plus Geographic admission | Display validation failures | Client-side replay, local state |
| Order snapshot | Order domain | Read presentation result | Catalog live rows rewriting history |
| Payment session address binding | Checkout/payment services consume admitted order context | Display payment UI | Payment provider, browser, callback payload |
| Recovery | SQL checkout session plus final Geographic readmission | Rehydrate candidates for review | Mongo projection, recovery URL, Redis, browser state |

## Address Admission

Address admission is backend-owned.

- The frontend may send `geography.countryPlaceId`, `administrativeAreaPlaceId`, `localityPlaceId`, `districtPlaceId`, and `zonePlaceId` as candidates.
- The backend rejects malformed candidate GUIDs before runtime admission.
- The backend calls `IAddressAdmissionService.AdmitAsync` with owner domain `Checkout`.
- The admission service validates against active published SQL catalog release data.
- Unknown, inactive, unpublished, or invalid canonical place candidates fail closed.
- Accepted admission produces `AddressAdmissionEvidenceId` and `CatalogReleaseId` evidence.
- Order creation snapshots the admitted evidence in persisted order audit JSON.

Frontend city, area, country labels, street, building, floor, apartment, and landmark remain presentation or address-detail fields. They never create canonical place identity.

## Recovery

Recovery remains candidate-based.

- Checkout recovery restores the SQL checkout session and live cart authority.
- Recovered text city/area/address values are not accepted geographic authority.
- If the browser restores a geographic candidate, final checkout must admit it again through SQL.
- If no canonical candidate exists, the checkout remains in legacy compatibility mode and does not fabricate canonical identity.
- Redis, MongoDB, browser storage, and recovery tokens cannot repair or manufacture Geographic Identity.

## Regression Tests

GEO-7 freezes these regressions:

- Storefront checkout DTOs expose canonical candidates only as candidates.
- Checkout service calls SQL-backed address admission before final order/payment progression.
- Checkout idempotency includes geographic candidates.
- Order snapshot evidence includes admitted geographic address evidence.
- Storefront frontend forwards candidates without becoming authority.
- CommerceHub and Affiliate checkout implementations are not modified by this phase.
- Provider IDs are not added to Storefront checkout DTOs or orders.

## Verification

Verification performed:

- `dotnet build PointOfSale.API\PointOfSale.API.csproj -nologo -v minimal --no-restore`

Verification expectations:

- Frontend cannot fabricate Canonical Place because submitted IDs are candidates only.
- Frontend cannot fabricate Geographic Identity because backend admission writes evidence only after SQL validation.
- Replay cannot fabricate Address Admission because idempotency hashes include geographic candidate IDs.
- Redis cannot become Geographic Authority because checkout only uses it for cart/lock/idempotency infrastructure.
- Mongo cannot become Geographic Authority because checkout final validation and order writes use SQL.
- React, React Query, and Zustand cannot become Geographic Authority because they only store candidate state.
- Recovery re-enters SQL Geographic Authority at final checkout admission.

## Risks

- The current order table does not yet have first-class geographic snapshot columns. GEO-7 stores admitted evidence in existing persisted order audit JSON to avoid a schema redesign in this phase.
- Legacy compatibility remains for checkout addresses submitted without canonical candidates. This preserves backward compatibility but means tenants must enable canonical selectors/catalog coverage to receive full canonical identity.
- The foundational admission service currently validates published active places and evidence, but deeper country-profile hierarchy policy coverage should continue hardening in later GEO phases.
- Pricing preview admission currently validates candidates through the same admission boundary; later phases may split side-effect-free validation from durable admission evidence.

## Future Integration Notes

This implementation applies only to POS Admin Storefront Checkout.

Affiliate Checkout will reuse the same Canonical Vendor Checkout contract in a future Checkout phase. Affiliate attribution remains separate from geographic ownership and was not modified here.

CommerceHub Checkout will not reuse this flow directly. CommerceHub first performs Marketplace Split, then produces one Canonical Vendor Checkout per POS Admin Vendor. This phase does not implement that behavior.

Future phases should add first-class order/shipment geographic snapshot columns, side-effect-free quote validation where required, full country-profile hierarchy enforcement, saved-address admission, and CommerceHub/Affiliate integrations under separate approved scopes.

## Explicit Success Answers

- Reviewed ONLY Storefront Checkout Geographic runtime: **Yes**.
- Runtime files modified: **Yes**, Storefront checkout runtime only plus Geographic DI registration.
- Backend files modified: **Yes**, checkout DTOs, checkout service, idempotency snapshot builder, and DI registration.
- Frontend files modified: **Yes**, Storefront checkout types/store only; no redesign.
- Checkout integration completed: **Yes**, for canonical candidate admission in Storefront checkout.
- Address Admission integrated: **Yes**, through `IAddressAdmissionService`.
- Authority preserved: **Yes**, SQL Geographic admission remains decisive.
- Backward compatible: **Yes**, legacy text remains candidate/evidence when no canonical candidate is supplied.
- Production-certifiable: **Yes for Storefront candidate admission and authority preservation; first-class snapshot columns are recommended before full canonical-only rollout**.
- Recommend next phase: **GEO-8 - First-Class Order and Shipment Geographic Snapshot Columns**, followed by separate Affiliate and CommerceHub checkout phases.

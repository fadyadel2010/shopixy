# BOSTA-2.5 — Geographic Identity Compatibility Audit

Audit date: 5 July 2026  
Scope: Shipping Domain geographic identity  
Status: COMPLETE — REDESIGN REQUIRED

## Executive Summary

The current Shipping Domain does **not** own authoritative geographic identity.

It owns an authoritative shipment and carrier lifecycle, but its destination geography is an order-derived, mostly free-text snapshot. Checkout accepts `Street`, optional `Street2`, `City`, optional `State`, optional `ZipCode`, and an ISO country code. `CheckoutService` flattens the street lines into `Order.ShippingAddress`; `Order` retains city/state/postal/country strings; `ShipmentService` and `CarrierShipmentService` copy those strings into `Shipment`; and `CarrierShipmentService` builds carrier requests from them.

The carrier-neutral `CreateShipmentRequest` can now carry `RecipientCityId`, `RecipientDistrictId`, `RecipientDistrictName`, and `RecipientZoneId`, but neither production request builder populates them. No internal geographic entity, stable geographic key, hierarchy, provider mapping table, geographic resolver, synchronization service, or cached authoritative mapping exists. Bosta exposes the gap because its create contract requires `districtId`, or `cityId` plus `districtName`; the same architecture cannot reliably support any future carrier that requires provider location keys.

This is not an adapter-only defect. The best solution is a first-class, provider-independent Geographic Identity concept owned at the shared Shipping/Address boundary, with provider identifiers owned by a dedicated carrier geographic mapping service and persistence model. Checkout should select or submit a canonical geographic identity; orders and shipments should snapshot it; adapters should only consume resolved provider mappings.

No runtime code was changed. A local adapter workaround, hardcoded mapping, or free-text inference would manufacture identity and violate the existing fail-closed rule.

## Reviewed Scope

Reviewed:

- Storefront checkout address DTOs, validation, recovery, and persistence
- checkout-session address fields
- SQL `Order`, `Shipment`, `Customer`, `Warehouse`, `CarrierConfiguration`, and CommerceHub order address fields
- shipment creation and external carrier registration
- carrier-neutral quote/create DTOs
- Bosta, Aramex, Mylerz, and DHL geographic request mapping
- SQL EF mappings and Mongo order projections
- Storefront, CommerceHub, split-order, affiliate, warehouse, and manual-shipment address-bearing paths
- PCC-0, PCC-1, BOSTA-1, BOSTA-2, and all discovered shipping/shipment certification findings

Excluded:

- provider response/DTO certification
- webhook and status-contract recertification
- live provider/database calls
- large geographic redesign implementation

## Current Geographic Pipeline

```text
Customer form / checkout recovery
  Street, Street2, City, State, ZipCode, Country
        |
        v
Checkout AddressDto validation
  required: Street, City, Country
  no canonical geographic-key admission
        |
        v
SQL Order snapshot
  ShippingAddress = flattened Street + Street2
  ShippingCity / State / ZipCode / Country = strings
        |
        v
SQL Shipment snapshot
  copies Order strings; coordinates exist but are not populated
        |
        v
CarrierShipmentService
  builds CreateShipmentRequest from free text
  leaves CityId / DistrictId / DistrictName / ZoneId null
        |
        v
Carrier adapter
  Bosta: fails BOSTA_ADDRESS_IDENTITY_REQUIRED
  Aramex/Mylerz/DHL: send free-text city/country/postal fields
```

Geographic identity is lost before it can become authority: checkout never receives a canonical internal place identity, and order persistence has nowhere to retain one. `CheckoutSession.Area` is a free-text recovery field and is not mapped into `AddressDto`/`Order` as a district identity.

## Current Address Model

| Surface | Persisted/captured geography | Authority quality |
|---|---|---|
| `AddressDto` | street, street2, city, state, ZIP, ISO country | validated free text; no geographic identity |
| `CheckoutSession` | city, area, address, country | editable recovery snapshot; no IDs/hierarchy |
| `Order` | flattened address, city, state, ZIP, country | authoritative historical text snapshot only |
| `Customer` | one free-text `Address` | no saved-address identity or structured geography |
| `CommerceHubMarketplaceOrder` | address line, city, area | separate free-text snapshot; no country/postal/IDs |
| `Warehouse` | one free-text `Address` | no structured origin geography |
| `CarrierConfiguration` | pickup address, city, country | carrier origin configuration; no provider location IDs |
| Mongo order projection | street, city, state, ZIP, country | derivative copy of SQL order text |

There is no saved customer address entity, address book, canonical address aggregate, or address-version identity in the inspected runtime.

## Current Shipping Model

`Shipment` has:

- street line 1 and optional line 2
- city, state, postal code, country
- optional latitude and longitude
- carrier metadata JSON

It does not have:

- internal country/governorate/city/district/zone keys
- district, zone, governorate, landmark, building, floor, or apartment fields
- normalized address or validation state
- provider city/district/zone/region mappings
- mapping version/source/synchronization evidence

`Shipment.CarrierMetadata` is unsuitable as source authority. Its documented boundary is carrier-specific opaque JSON parsed only by adapters, while geographic identity is needed before adapter execution and across providers.

## Geographic Identity Inventory

| Geographic fact | Current ownership | Verdict |
|---|---|---|
| Country | checkout/order/shipment string; checkout validates ISO length/normalization | PARTIAL |
| Governorate | optional `State` string only | NO authoritative identity |
| City | required checkout free-text string | NO authoritative identity |
| District | checkout-session/CommerceHub `Area` free text only; absent from canonical order/shipment | NO |
| Zone | absent | NO |
| Postal code | optional free-text snapshot | PARTIAL |
| Coordinates | nullable on `Shipment` and carrier request | MODELED, not produced by pipeline |
| Latitude/Longitude | nullable decimals on `Shipment` | MODELED, not captured/resolved |
| Street | required checkout text; flattened into order | YES as text snapshot, not normalized identity |
| Building/Floor/Apartment/Landmark | no dedicated fields | NO |
| Internal geographic identity | absent | NO |
| Cross-provider geographic identity | absent | NO |

## Provider Identity Inventory

| Provider | Current geographic input | Provider IDs owned? | Result |
|---|---|---:|---|
| Bosta | city text plus optional carrier-neutral city/district/zone fields | No; builders leave all null | Create fails closed |
| Aramex | city, country code, postal code, address lines | No provider place mapping | Structurally sendable; semantic validity remains provider-side |
| Mylerz | city, country, postal code, address lines | No provider place mapping | Structurally sendable; semantic validity remains provider-side |
| DHL | city name, country code, postal code, address lines | No provider place mapping | Structurally sendable; semantic validity remains provider-side |

No provider region ID is modeled. No provider city/district/zone ID is persisted. The fields added to `CreateShipmentRequest` are transient request properties, not owned identity.

## Pipeline Trace

1. **Customer input** — Storefront `ShippingAddress`/`AddressDto` accepts free text. The current checkout certification correctly treats it as a candidate.
2. **Checkout session** — autosave persists city, area, address, and country strings. Recovery restores editable candidates only.
3. **Checkout admission** — backend validates required text, country format, owner/cart/session, phone policy, pricing, and transaction authority. It does not validate place membership or hierarchy.
4. **Order** — `CheckoutService` flattens `Street` and `Street2`, then stores city/state/ZIP/country strings. No district/area or canonical ID survives.
5. **Shipment** — both shipment creation paths copy order address strings. Coordinates remain unset.
6. **Carrier service** — both create-request builders populate recipient address/city/country/postal but not provider identity fields.
7. **Adapter** — Bosta requires authoritative location identity and rejects locally when missing. Other adapters serialize text accepted by their current request shapes.
8. **Outbound request** — there is no resolver or mapping boundary between shipment and adapter.

## Architecture Findings

1. Shipping lifecycle authority and geographic identity authority are different concerns. SF-SHP certifications prove shipment/carrier authority, not canonical geography.
2. SQL owns historical address text, but no SQL model owns a canonical place hierarchy.
3. The model is carrier-agnostic only at the field-name level. It cannot translate one internal place into multiple provider identities.
4. Provider identity fields on `CreateShipmentRequest` are an integration seam, not an authority source.
5. Bosta's fail-closed behavior is correct and must remain.
6. Checkout-session `Area` and CommerceHub `Area` cannot be promoted to district identity: they are free text, separate pipelines, and lack hierarchy/version evidence.
7. Coordinates cannot repair the gap because the pipeline never captures or geocodes them, and provider-specific IDs still require mapping.
8. Warehouse and pickup configuration have weaker structured geography than destination shipments, so a complete design must cover origin as well as destination.
9. Mongo projections contain only derivative text and cannot be used as geographic authority.
10. Manual shipment flow is not an alternative authority path; it copies the same order snapshot.

## Architecture Gaps

- no canonical geographic catalog
- no stable internal IDs for country/governorate/city/district/zone
- no hierarchy or membership validation
- no normalized multilingual names/aliases
- no address-to-geography binding
- no provider geographic mapping persistence
- no mapping provenance, effective dates, version, or enabled state
- no resolver/lookup API
- no provider synchronization/import workflow
- no cache backed by SQL mapping authority
- no stale/missing/ambiguous mapping policy
- no offline mapping snapshot for shipment creation
- no address validation/geocoding boundary
- no order/shipment geographic snapshot
- no origin/pickup geographic identity
- no CommerceHub parity

## Risk Assessment

| Risk | Severity | Evidence |
|---|---|---|
| Bosta shipment creation unavailable | HIGH | production builders leave required IDs null |
| Future provider incompatibility | HIGH | no cross-provider mapping architecture |
| Wrong-city/district delivery | HIGH | free text has no hierarchy validation |
| Silent provider-specific drift | HIGH | no synchronization/version model |
| Offline shipment creation impossible when provider lookup is required | HIGH | no local authoritative mapping |
| Hardcoded adapter mapping divergence | HIGH if introduced | no shared authority or update path |
| Address normalization/duplicate ambiguity | MEDIUM | names are unnormalized strings |
| Coordinates falsely treated as identity | MEDIUM | fields exist but have no source/provenance |
| CommerceHub/storefront geographic divergence | MEDIUM | independent free-text schemas |

## Recommended Architecture

Introduce a first-class provider-independent Geographic Identity model:

- `GeographicPlace`: stable internal ID, type (`Country`, `Governorate`, `City`, `District`, `Zone`), parent ID, canonical code where available, names/aliases, active state, and version.
- `Address`: structured address details bound to canonical place IDs; retain raw text for delivery instructions and historical display.
- `ProviderGeographicMapping`: provider code, internal place ID, provider place type, provider ID/code/name, parent mapping, environment/account scope where required, effective/version timestamps, source, verification state, and enabled state.
- `GeographicIdentityResolver`: resolves and validates submitted internal identities; never guesses from ambiguous text.
- `CarrierGeographicMappingService`: resolves canonical place IDs to provider-specific IDs, supports SQL-backed offline resolution, and fails closed on missing/ambiguous/stale mappings.
- provider import/synchronization jobs: call provider location APIs where available, stage differences, validate hierarchy, and publish reviewed mappings.
- cache: optional read-through cache keyed by provider + internal place + mapping version; SQL remains authority.
- immutable order/shipment snapshot: internal geographic IDs plus resolved names and the provider mapping/version used for registration.

Text-only addresses may remain for manual fulfillment, but carrier eligibility must declare required geographic identity levels. Shipment creation should reject before external registration when the chosen carrier's requirements are not satisfied.

## Recommended Ownership

| Concern | Owner |
|---|---|
| provider-independent place hierarchy | shared Geographic Identity capability at Shipping/Address boundary |
| checkout selection/capture | Checkout as candidate collector and admission client |
| canonical address binding and validation | Address/Geographic Identity service |
| order historical geographic snapshot | Order persistence, copied from admitted address identity |
| shipment geographic snapshot | Shipping Domain |
| provider city/district/zone/region IDs | Carrier Geographic Mapping service and SQL mapping tables |
| outbound provider mapping | `CarrierShipmentService` before adapter invocation |
| wire serialization | carrier adapter only |
| synchronization/import | provider mapping service/background job |
| cache | derivative of SQL mapping authority |

The adapter must not own mappings, perform ad hoc free-text inference, or hide identifiers in `CarrierMetadata`. Checkout must not own provider IDs because it should remain provider-independent and the provider may be selected or changed later.

## Runtime Hardening

No runtime change was made.

The existing `BOSTA_ADDRESS_IDENTITY_REQUIRED` fail-closed guard is the correct small hardening and was already completed by BOSTA-2. Any further useful change requires new persistence, contracts, services, migration, operational mapping data, and multi-provider policy; that is a redesign rather than a small audit hardening.

## Regression Tests

No new tests were added because runtime behavior did not change.

Existing focused protection remains:

- `PointOfSale.Tests/Infrastructure/BostaEndpointSurfaceTests.cs` freezes official Bosta address field names and fail-closed behavior.
- shipping/shipment architecture certifications freeze SQL shipment, carrier, lifecycle, tenant, and replay authority.
- checkout-address certification freezes frontend address candidates versus backend order snapshots.

The redesign phase should add tests for hierarchy validation, mapping uniqueness, ambiguous/missing mapping rejection, mapping version snapshots, offline resolution, provider synchronization, cross-tenant isolation, and Storefront/CommerceHub parity.

## Verification

Verification was static and repository-local:

- inspected all mandatory architecture context files for applicable shipping/address truths
- read PCC-0, PCC-1, BOSTA-1, and BOSTA-2
- read all discovered shipping/shipment certification findings (the repository contains descriptive certification filenames rather than literal `SF-SHP*` filenames)
- traced `AddressDto` → `CheckoutService` → `Order` → `ShipmentService`/`CarrierShipmentService` → carrier adapters
- inspected SQL EF mappings and Mongo address projections
- searched for geographic entities, provider mappings, resolvers, lookup services, caches, and hardcoded mapping tables

No relevant runtime test was run because no runtime or test file changed.

## Explicit Answers

**Does the current Shipping Domain own authoritative geographic identity? NO.**  
It owns authoritative shipment snapshots containing address text, but no stable internal geographic identity, hierarchy, or mapping provenance.

**Can all current providers be supported without redesign? NO.**  
Bosta cannot be created through normal production builders. The other current adapters can serialize their present text-based shapes, but the architecture cannot guarantee professional provider identity requirements or future contract changes.

**Should Geographic Identity become a first-class Shipping Domain concept? YES.**  
Shipping must own the admitted shipment geographic snapshot and require provider-independent identity. The reusable catalog/address binding should be a shared Geographic Identity capability rather than Bosta-specific fields embedded in Shipping.

**Who should own provider geographic identifiers?**  
A backend Carrier Geographic Mapping service backed by tenant-safe SQL mapping tables. Adapters consume resolved identifiers; Checkout, frontend, orders, Mongo, cache, and carrier metadata do not own them.

**Can Bosta be fully supported today? NO.**  
The adapter can serialize valid identifiers and correctly fails closed, but no production path captures, persists, resolves, or supplies them.

**What architectural work is required before BOSTA-3?**  
For response-shape research alone, BOSTA-3 could technically proceed. For the certification sequence to represent an executable Bosta integration, first complete a Geographic Identity architecture phase: canonical hierarchy, structured address binding, provider mapping persistence/service, order/shipment snapshots, Bosta mapping import/lookup, offline resolution, and regression tests. Then rerun BOSTA-2 create compatibility before BOSTA-3.

## Final Verdict

**GEOGRAPHIC IDENTITY COMPATIBLE: NO**

**REDESIGN REQUIRED: YES**

The next phase should be **GEO-1 — Geographic Identity Domain & Provider Mapping Architecture**, followed by implementation/data migration and a BOSTA-2 re-certification. BOSTA-3 should not be treated as the next production-readiness gate until that prerequisite is complete.

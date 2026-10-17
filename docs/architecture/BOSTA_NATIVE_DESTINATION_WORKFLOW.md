# Bosta simple connection and native destinations — 2026-10-16

This carrier-specific workflow supersedes the national-publication prerequisite
for Bosta setup and new native shipment destinations. It does not supersede
canonical catalog governance or other carriers' mapping/admission contracts.

## Independent authorities

* Saved encrypted credentials and version-bound connection tests authenticate the
  saved environment. Neither reading nor testing activates the integration.
* Explicit activation/disable controls Shopixy's Bosta carrier switch and account
  lifecycle, not a merchant-wide shipping flag or Bosta's operational activation.
* An authenticated explicit pickup or unique provider-marked default is origin
  authority. Discovery never saves a default selection. Every create freezes and
  independently revalidates its concrete pickup.
* Bosta provider-account/service entitlement remains unverified by the API-key
  pickup endpoint. New production registration fails closed with
  `BOSTA_PROVIDER_ACCOUNT_ELIGIBILITY_UNVERIFIED`. The retained October 15 profile
  had `isActive=false`; it must not be promoted to fresh eligibility evidence.
  The documented business-profile endpoint uses a business identity and Bearer
  authentication; neither a catalog publication nor a successful pickup read
  establishes that entitlement. No caller-authored readiness Boolean overrides it.
* A Bosta destination is independently admitted on a Bosta shipment. Account-level
  destination validation is a read-only preview, not booking certification.

## Provider contract

Official retained OpenAPI `.ai/evidence/phase6-20261015/bosta-api.yaml`,
`AddressCreateDelivery` (999–1033), accepts `districtId` or `cityId` together with
`districtName`, plus independent `firstLine` and `city` text. `/cities` and
`/cities/{cityId}/districts` supply provider references. No Shopixy canonical UUID
or complete national mapping publication occurs on this wire contract.

Only provider-observed Egypt country/city/district identities and explicit
drop-off availability are admitted. Unknown IDs, wrong parent, unavailable
references, malformed/expired discovery and wrong account/environment fail closed.

## Persistence, without a schema migration

Existing `Shipment.CarrierGeographicMappingSnapshotJson` (jsonb) holds a tagged
`BostaNativeDestination` v1 record. Existing environment/account-scope/timestamp
columns hold its companion scope; canonical mapping-release ID/version/key stay
null. No canonical IDs or catalog records are manufactured. The JSON contains:

* tenant, original order and shipment IDs;
* carrier account/configuration/current credential-reference/environment binding;
* credential-material hash (never credential plaintext);
* exact unchanged recipient/address fingerprint;
* genuine provider references/parents/serviceability and observation time.

The EF persistence guard restores this tagged evidence separately from canonical
mapping evidence. Retained snapshots cannot be replaced or deleted by the normal
save path. Address, account or environment substitution fails closed. Existing
canonical mapping snapshots keep their historical format and recovery contract.

Checkout retains the existing real domestic address and supported standard
service. It no longer claims canonical mapping is a prerequisite for accepting
that address. A Bosta shipment still cannot be booked until the operator admits a
provider destination (or supplies the existing published-mapping path). Existing
order/customer address and global geography are not rewritten. Other carrier
admission and pricing are unchanged.

Before durable create claim, the native snapshot is bound to the current account
and credential reference; the frozen create request includes it. Before delivery
POST, the adapter performs fresh authenticated reference discovery and verifies
the exact IDs, hierarchy and current drop-off serviceability. No provider HTTP is
added inside claim transactions. After an ambiguous attempt, retained request
identity and the existing no-blind-repost recovery rules continue to apply.

## Admin/API

Carrier setup exposes explicit Save, Test, activation and pickup discovery. Native
city/district discovery and validation are separate explicit actions. Governed
catalog bootstrap remains under optional advanced controls. Shipment details have
an explicit destination setup/readback and save action; it cannot convert a Manual
order into Bosta. Save requires both shipping management and fulfillment permission.

New routes:

* `GET .../carrier-accounts/{id}/bosta/native-geography?cityId=...`
* `POST .../carrier-accounts/{id}/bosta/destinations/validate` (provider reads only)
* `GET /api/admin/shipments/{id}/bosta-destination`
* `PUT .../carrier-accounts/{id}/bosta/destinations/{shipmentId}`

All are tenant/account/environment scoped and no-store. Page rendering does not
call Bosta, activate an account, select/save a default or publish geography.

No new shared schema, migration, live order or production shipping effect is
needed to verify these contracts. Controlled transport tests are not certification
of PostgreSQL concurrency, live provider entitlement or production bookings.

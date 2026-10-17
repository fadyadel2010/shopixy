# Governed Bosta geography bootstrap

This workflow reuses the geographic catalog, hierarchy, sync-candidate, mapping-release,
publication and audit tables. It performs no migration, schema reset, carrier activation,
shipment registration, cancellation, pickup request or dispatch.

## Two independent sources of authority

1. **Bosta references** are discovered with the current saved tenant/account credentials
   through GET `/pickup-locations`, `/cities`, and `/cities/{cityId}/districts` on the
   production or sandbox host already used by the adapter. Egypt's provider country ID
   is taken from the account-owned pickup reference. Provider IDs never become canonical
   Shopixy IDs merely because labels look similar.
2. **Canonical places** must be supplied in a source-attributed import. The application
   does not ship an invented Egypt catalog, treat carrier labels as official canonical
   records, or certify that an operator-supplied URI is official. An independent human
   reviewer must inspect the actual authoritative source and its SHA256 before approval.

## Admin workflow

Under **Shipping → Carrier Accounts → Bosta geography**:

1. Load references and select a genuine provider city. If provider metadata is missing,
   supply a reason and explicitly approve **Bootstrap verified geography provider and
   draft**. This registers only the missing geographic `CarrierProvider` and stages
   authenticated references atomically. `CarrierAccount` state, credentials, permissions,
   warehouse assignments and activation are unchanged. Deliberately disabled geographic
   providers are not silently re-enabled.
2. Paste a canonical JSON manifest, preview it without saving, inspect diagnostics,
   and explicitly stage the exact preview checksum with a reason. This creates a **Draft**
   catalog, canonical IDs, names, profiles, edges and manifests in one transaction.
3. An independent reviewer lists/reads the saved catalog, inspects the source URI/hash,
   canonical identities, names, hierarchy and address requirements, then confirms source
   review. Validation binds the content checksum and imported source identity. A separate
   explicit publish approval is required. The creator and all editors cannot approve their
   own changes. Imported publication requires the exact reviewed checksum. This initial
   bootstrap does not pretend to implement catalog replacement/rollback.
4. Reload Bosta references after canonical publication. Review/select the canonical
   country, city and destination against the provider candidates. Save a mapping **Draft**.
   Cross-country, ambiguous, unsupported, unserviceable or incompatible-parent choices
   fail before candidate mutation. An independent reviewer validates and publishes the
   exact mapping revision. Saved reference drafts and mapping releases can be reloaded.

Publication alone grants no booking authority. Existing readiness, account activation,
entitlement, warehouse, parcel declaration, destination and freshness gates still apply.
Provider references expire after the existing one-day window; credential/environment
changes invalidate old reference bindings. No production publication is performed merely
by installing this code or opening the Admin screen.

## Canonical manifest contract

`POST /api/admin/geographic/catalog/import/preview` accepts `CatalogImportManifest`.
`POST /api/admin/geographic/catalog/import/drafts` accepts
`{ manifest, expectedManifestChecksumSha256, reason }`.
Both are bounded to 4 MiB. The typed contract is
`PointOfSale.API/DTOs/GeographicAdministration/CatalogImportDtos.cs`.

- Release fields: `releaseNumber`, `releaseKey`, `countryScopeIso2`, `sourceSummary`,
  `sourceEvidenceUri` (bounded HTTPS URI without credentials/query/fragment),
  `sourceEvidenceSha256` (hash of the authoritative source, not an invented proof).
- `places`: stable `id` GUID, `stableKey`, `placeKind`, `countryPlaceId`, `parentPlaceId`,
  `levelCode`, `canonicalCode`, optional paired `externalStandard`/`externalStandardCode`,
  and `names` (`languageTag`, `name`). Preserve authoritative identity relationships;
  do not copy Bosta IDs into these fields as canonical authority.
- `countryProfiles`: country ID, ISO2/ISO3, default/supported languages, required delivery
  and operations levels, postal policy/regex, street/coordinate provenance and offline
  package requirements. These are reviewed country policy, not guessed shipping defaults.
- Place kinds are `Country`, `AdministrativeArea`, `Locality`, `District`, `Zone`.
  Initial hierarchy is contiguous, single-parent and country-consistent. Every country
  needs a locality, a profile and preferred names in its supported languages.

Preview validates duplicates (including case-insensitive stable keys), existing identity
collisions requiring reconciliation, missing/cross-country parents, levels, names, postal
rules and source attribution. Preview's manifest hash covers the typed proposed import;
it is distinct from the supplied authoritative-source hash. Existing canonical records are
never overwritten or silently coalesced. Publication revalidates the persisted content.

All routes retain the existing POS roles and `PosPermissions.ShippingManage` authorization. Provider
sync/readback binds tenant, account, saved credential reference and environment. Global
canonical data uses the existing canonical administration authority; tenant carrier mapping
and reference evidence remain tenant-scoped.

## Evidence limitations

Synthetic unit tests are not official-source review, live PostgreSQL locking certification,
or complete Admin-to-controlled-shipment acceptance. Live acceptance needs a reviewed
canonical import, an independent authorized reviewer, the new API build and controlled
shipment-effect transport. No test fixture is a production catalog or production parcel.

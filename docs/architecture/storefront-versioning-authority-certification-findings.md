# PASS P2-GE Storefront Versioning Authority

Date: 2026-06-09

Status: production-certifiable for the inspected runtime after hardening.

## Authority Decision

Tenant-owned PostgreSQL `StoreConfig` owns storefront publication lineage, current publication
version, version snapshots, restore lineage, and draft/live branches for the versioned builder,
theme, and navigation state stored there. PostgreSQL `CmsPages` separately owns CMS page versions.
Versioning may promote historical state only after current P2-GA, P2-GB, P2-GC, and P2-GD authority
accepts it. Snapshots, audit rows, events, MongoDB, Redis, frontend history, and DTOs are evidence or
derivative state, never independent authority.

## Findings

### Version Identity

`StoreConfig.LiveVersion` and snapshot `Version` are server-generated. Publish selects
`max(stored versions) + 1` under `StoreConfig.RowVersion` concurrency. Rollback now creates a new
monotonic publication and records `RestoredFromVersion`; it never rewinds identity or permits the
next publish to reuse an old number.

### Drafts

Draft writes remain SQL and row-version protected. Rollback canonicalizes historical snapshots to
explicit live branches, strips every historical `draft` branch, then overlays only current drafts.
Historical drafts cannot resurrect or overwrite current drafts.

### Publication

Owner/Admin APIs derive tenant identity from trusted context, require an expected row version, and
use the tenant publish lock. Publication snapshots and projection events are staged from the same
tracked SQL mutation. A stale publish or replay cannot overwrite a newer row.

### Rollback And Restore

Rollback requires explicit confirmation, an existing same-tenant snapshot, current row version,
and the tenant publish lock. It validates snapshot JSON and current theme authority, restores only
live state, preserves current drafts, appends a new publication snapshot, and emits the new
publication version. There is no separate hidden restore API.

### Snapshots And History

Snapshots are tenant-contained historical evidence inside the tenant's SQL `StoreConfig` row.
History reads are tenant-predicated and newest-first. Snapshot identity is not accepted from DTO
payloads. Audit snapshots are append-only evidence and are not used as restore authority.

### Theme Integration

Historical theme restore requires the referenced preset to still exist and all overrides to pass
the current presentation-only P2-GC validator. Rollback restores live preset/overrides only and
does not overwrite the current draft preset or draft overrides.

### Content Integration

SQL `CmsPages` keeps its own immutable version authority and exact-version publish/preview
controls. StoreConfig rollback cannot publish CMS page drafts or rewrite `CmsPages`. Historical
builder page state restores only explicit live branches.

### Navigation Integration

Historical navigation drafts are stripped. Restored live navigation remains subject to current
public SQL target filtering: CMS targets must still be published and category targets must retain
live lineage. Deleted or hidden targets cannot regain visibility from a snapshot.

### Configuration Integration

Version snapshots do not carry tenant currency, domain ownership, tenant lifecycle, or general
StoreConfig identity fields. Rollback therefore cannot replace P2-GA currency/domain authority.
Public configuration continues to re-read current SQL live state.

### Caching And Projections

Projection events are triggers only. The StoreConfig consumer fences duplicate delivery, reloads
current tenant/config SQL authority, applies freshness filters, writes derivative Mongo state, and
invalidates Redis. Cache or projection replay cannot restore snapshot payload state.

### Tenant Isolation

Admin version, rollback, preview, and audit APIs are authenticated, tenant-derived, and SQL
tenant-predicated. Version numbers are meaningful only inside the tenant-owned StoreConfig row.
Foreign versions or snapshots cannot be read or restored through inspected APIs.

### Concurrency And Idempotency

`RowVersion` rejects stale publish/rollback/write requests even if the Redis lock expires. The
publish lock serializes tenant publication attempts and fails closed on lock infrastructure
failure. Replayed requests carrying the original row version conflict; repeated authorized
restores create distinct monotonic publications.

### Historical Consistency

Publication lineage never moves backward. Retention keeps the latest 50 snapshots without reusing
version numbers. Malformed snapshots, retired presets, invalid theme overrides, absent versions,
and historical drafts fail closed or remain inert.

### Adversarial Review

Version guessing, DTO tenant tampering, route version spoofing, stale row replay, publish replay,
snapshot resurrection, draft resurrection, retired-theme resurrection, projection poisoning, and
cache resurrection fail closed on inspected paths.

## Runtime Hardening Performed

- Changed rollback from pointer rewind to a new monotonic publication.
- Added `RestoredFromVersion` lineage to snapshots and version-history responses.
- Removed historical draft branches before restore and preserved only current drafts.
- Canonicalized historical pages, product templates, and navigation to explicit live branches.
- Revalidated historical theme presets and overrides against current P2-GC authority.
- Emitted rollback projection events with the new publication version.

## Exact Runtime Files Modified

- `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs`
- `PointOfSale.API/DTOs/Admin/VersionHistoryDtos.cs`

## Regression Freezes

- `PointOfSale.Tests/Admin/AdminStoreConfigRollbackSafetyTests.cs`
- `PointOfSale.Tests/Admin/AdminStoreConfigPublishReliabilityTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontVersioningAuthorityCertificationTests.cs`

## Explicit Answers

- Who owns versioning authority? Tenant-owned PostgreSQL StoreConfig publication lineage and
  PostgreSQL CmsPages for CMS-specific versions.
- Can drafts bypass publication? No.
- Can stale snapshots resurrect? No.
- Can rollback overwrite newer truth? No; stale row versions conflict and rollback creates newer
  publication truth.
- Can version replay overwrite live state? No.
- Can versioning bypass configuration authority? No.
- Can versioning bypass content authority? No.
- Can versioning bypass theme authority? No.
- Can versioning bypass navigation authority? No.
- Can caches become authority? No.
- Can tenant leakage occur? No inspected runtime path permits it.
- Were runtime code changes required? Yes.
- Is Storefront Versioning Authority production-certifiable? Yes, for the inspected runtime.

## Residual Risk

Version snapshots remain a bounded JSON history embedded in `StoreConfig`, rather than normalized
append-only rows with database uniqueness constraints. Current row concurrency and server-only
mutation protect the inspected runtime, but a future standalone snapshot import, cross-store clone,
scheduled publication, branch merge, or normalized version service requires renewed P2-GE review.

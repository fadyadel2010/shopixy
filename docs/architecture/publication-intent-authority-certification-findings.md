# POS-ADMIN-CERTIFICATION-01 PASS 12.1 Merchant Publication Intent Authority

Date: 2026-05-30

## Scope

This pass certifies merchant publication intent for CommerceHub marketplace listings after PASS 9.x public visibility authority, PASS 10.x freshness/convergence, and PASS 11.x super-admin/governance authority.

Publication intent is a tenant-owned merchant request. It is not visibility authority. Marketplace visibility remains current SQL `CommerceHubListings` plus current SQL product, variant, CommerceHub category, tenant, vendor-governance, pricing, stock, media, and review authority, filtered through marketplace governance and derivative convergence controls.

## 1. Publication Inventory Findings

- Publish to marketplace: `AdminCommerceHubController.Publish` routes to `PublishToCommerceHubHandler`. Request owner is the tenant. Authority owner is SQL product/variant/listing/category/tenant/vendor governance. Enforcement owner is the handler plus `CommerceHubSearchSqlAuthorityOverlay`.
- Remove from marketplace: `AdminCommerceHubController.Unpublish` routes to `UnpublishFromCommerceHubHandler`. Request owner is the tenant. Authority owner is the SQL listing row. Enforcement owner is tenant isolation, SQL listing mutation, projection replay fencing, and SQL search deletion.
- Republish: implemented as publish on an existing listing through `CommerceHubListing.Publish`. It must pass the same governance, category, product publication, and variant lineage checks as first publish.
- Update listing: `UpdateCommerceHubListingHandler` changes category/attributes only after current governance, category, product, and variant authority accept the listing.
- Clone listing: no CommerceHub listing clone path was found. Product/variant clone/import paths exist, but they do not copy CommerceHub listing publication as durable authority.
- Bulk publish and bulk unpublish: no tenant bulk publish/unpublish API was found. Bulk search/reindex operations are derivative convergence only.

No hidden runtime listing creation path was found outside `CommerceHubListing.Create` in the publish handler and tests. Development and test seed paths are not certified as production publication authority.

## 2. Lifecycle Findings

The SQL listing lifecycle is intentionally compact:

- Draft/Pending/Submitted/Approved/Rejected/Suspended/Hidden are not separate listing status enum states in the current runtime.
- Tenant publish creates or updates a SQL listing with `IsPublished = true` only after governance and SQL product/category/variant authority pass.
- Tenant unpublish sets `IsPublished = false` and clears `RestorePublicationOnActivate`.
- Vendor suspension/ban calls `HideByGovernance`, sets `IsPublished = false`, and records only conditional restore intent in `RestorePublicationOnActivate`.
- Vendor reactivation calls `RestoreAfterGovernance` only after current tenant/product/variant/category authority still accepts the listing; otherwise `ClearGovernanceRestoreState` removes stale restore intent.
- Deleted SQL listing, missing product/variant/category authority, inactive tenant, or invalid vendor governance fails closed on public visibility overlays.

No reviewed transition allows derivative replay, search reindex, homepage curation, or stale restore state to skip current SQL authority.

## 3. Merchant Intent Findings

Merchant publish, republish, and update are request-only. They require trusted tenant context, tenant-owned variant identity, current public product publication, live CommerceHub category lineage, active shipping, disabled price overrides, and current vendor governance.

Merchant unpublish is tenant-owned withdrawal of intent. PASS 12.1 hardening removed the publish/update governance gate from unpublish so a governance-restricted tenant is not blocked from clearing publication intent or stale restore state. This operation can only reduce visibility.

Merchant intent cannot directly create public search/detail/homepage visibility because every reviewed public surface recomposes or filters through current SQL authority.

## 4. Governance Findings

Marketplace governance remains required for publish, republish, update, vendor suspension, and vendor restore. `EnsureVendorListingAllowedAsync` stays on publish/update. Vendor suspension and ban hide listings and record visibility snapshots in governance audit metadata.

Approval is not final visibility authority. Public visibility still requires current SQL listing/product/variant/category/tenant authority and public vendor visibility at read/index time. Stale approvals and stale restore flags are cleared instead of resurrected when current authority is gone.

## 5. Product Authority Findings

Publication is bounded by product authority:

- publish/update require active, undeleted, online-visible, `PublishStatus.Published` products.
- public search/detail/list/homepage visibility require the same current SQL product state through overlays and recomposition.
- current public variant lineage is required for publish/update and public visibility.
- product deletion, archive, unpublish, invalid variant lineage, stale stock-owner lineage, or missing category lineage collapses derivative visibility to delete/suppress/fail-closed behavior.

SQL listing intent may remain as an administrative record after product authority changes, but it cannot create visibility while product authority is absent.

## 6. Ownership Findings

Listings remain tenant-owned by `TenantId`, and the unique SQL boundary is `{ TenantId, VariantId }`. Admin routes use trusted tenant context rather than raw client tenant authority. Handlers reject cross-tenant requests through `CommerceHubTenantIsolationGuard`.

Public and operational paths carry tenant-scoped identities where available. Search recovery and auto-healing use `{ TenantId, ListingId }` when tenant evidence exists, preventing listing-id-only recovery from becoming tenant authority.

## 7. Replay And Recovery Findings

CommerceHub listing events are triggers. `CommerceHubProjectionConsumer` uses `ProjectionConsumerEventFence`, stale event filters, current SQL listing reloads, and `CommerceHubSearchSqlAuthorityOverlay` before search mutation.

CommerceHub reindex and auto-healing discover candidates from MongoDB/search state but index only through SQL overlays and delete stale search documents absent from current SQL-visible listing authority. Replay cannot publish a listing if current SQL rejects listing/product/variant/category/tenant authority.

## 8. Search And Discovery Findings

Search, discovery, suggestions, homepage sections, and public detail/listing reads are derivative. `CommerceHubSearchSqlAuthorityOverlay` requires current SQL listing publication, active tenant, published/online product, current public variant lineage, live CommerceHub category lineage, current price/stock/media, and approved reviews before indexing.

Search reindex performs SQL-visible count verification and stale delete. Query service recomposes visible listing items before return. Intent-driven visibility, orphan search documents, stale category suggestions, and stale homepage entries are suppressed on reviewed paths.

## 9. Auditability Findings

Publication actions produce structured logs and CommerceHub listing lifecycle events. Vendor governance actions write `UserGovernanceAuditLogs` with actor, reason, before/after state, listing snapshot, and cache invalidation/search verification metadata. Super-admin routes remain covered by operational audit middleware.

This pass did not add a separate immutable publication-decision ledger. The current observable boundary is SQL listing state, integration events, governance audit logs, search sync status, auto-healing logs/metrics, and convergence health.

## 10. Chaos Findings

- publish during recovery: publish/update must pass current SQL authority; recovery overlays before derivative mutation.
- publish during reindex: reindex is SQL-derived and stale-delete capable; current SQL authority wins.
- publish during governance change: governance hide suppresses visibility, and restore requires current product/category/tenant/variant authority.
- publish during category removal: live category lineage fails closed before publish/update and before search visibility.
- publish during product unpublish: publish/update reject unpublished products, and public overlays delete/suppress stale visibility.
- publish during tenant disablement: active tenant lineage fails closed on public overlays and recovery/search health.

No authority inversion was found on reviewed paths.

## 11. Runtime Hardening Performed

- `UnpublishFromCommerceHubHandler` no longer requires `EnsureVendorListingAllowedAsync`. Tenant-owned unpublish remains tenant-isolated but can clear publication intent even when governance blocks new listing management.
- Publish and update still require vendor governance, live category lineage, active shipping, current product publication/online visibility, and current public variant lineage.
- Regression-freeze coverage was added in `PublicationIntentAuthorityCertificationTests`.

## 12. Monitoring Improvements

No new telemetry backend was introduced. PASS 12.1 relies on existing structured publication logs, listing lifecycle events, governance audit metadata, search sync status, CommerceHub search health, auto-healing enqueue diagnostics, stale-delete counts from reindex, and bounded convergence telemetry.

## 13. Governance Updates

PASS 12.1 establishes merchant publication intent as tenant-owned request authority only. Future publish, unpublish, republish, update, clone, bulk publish, bulk unpublish, recovery, reindex, feed/export, cache-warmer, support, ranking, or global-script work that touches CommerceHub publication must declare authority owner, request owner, enforcement owner, tenant scope, replay behavior, auditability, and regression coverage.

## 14. Honest Remaining Debt

- The current listing lifecycle uses booleans instead of a full Draft/Pending/Submitted/Approved/Rejected/Suspended/Hidden/Deleted state machine.
- There is no dedicated immutable publication-decision ledger or enterprise moderation workflow.
- No generated publication path registry, universal SQL-vs-derivative parity scanner, or universal chaos harness was added.
- Future bulk publication, feed/export, support, localization, ranking, analytics, and global-script publication paths remain path-specific certification debt.
- The platform remains eventually consistent. The guarantee is SQL-authoritative convergence and fail-closed filtering on reviewed paths, not strong consistency.

# Merchant vs Marketplace Governance Precedence Certification

POS-ADMIN-CERTIFICATION-01 PASS 12.6 certifies that CommerceHub merchant publication intent remains subordinate to current SQL marketplace governance authority.

## 1. Governance Inventory Findings

Governance-capable controls are explicit across vendor governance, listing publication, listing governance hide/restore, category lineage, product/variant publication, tenant activity, search overlays, homepage/discovery sanitation, reindex, and auto-healing.

Authority owners:
- Merchant publication intent: tenant-owned request authority through `CommerceHubListing`.
- Marketplace vendor/listing governance: SQL `UserGovernanceProfiles` plus `CommerceHubVendorVisibilityService`.
- Product, variant, inventory, pricing, category, and tenant eligibility: current SQL authority through existing lineage helpers and overlays.
- Search, MongoDB, Redis, homepage, and recovery tooling: derivative convergence only.

Enforcement owners are the publish/update handlers, vendor visibility service, public query filters, `CommerceHubSearchSqlAuthorityOverlay`, and CommerceHub reindex/auto-healing paths. Execution owners are the API handlers, governance service, projection consumers, reindex service, and public read services.

## 2. Merchant-vs-Governance Findings

Publish and update remain requests only. They require current vendor listing governance, live CommerceHub category lineage, active shipping, current product publication/online visibility, and public variant lineage before setting or preserving listing publication intent.

Unpublish remains a tenant-owned withdrawal path and can only reduce visibility. Merchant updates cannot approve themselves, remove governance suppression, restore vendor-hidden publication, or bypass category/product/variant authority.

## 3. Suppression Findings

Suppression is represented by current SQL governance state and listing state, not by derivative search or Mongo documents. `HideByGovernance` clears public publication and preserves restore intent only as conditional evidence. `ClearGovernanceRestoreState` removes stale restore evidence when current SQL authority no longer accepts restoration.

Runtime hardening in this pass makes `CommerceHubSearchSqlAuthorityOverlay` fail closed when the current vendor governance profile is `Suspended` or `Banned`, even if a stale listing row or derivative document still claims publication.

## 4. Vendor Governance Findings

Vendor suspension and ban hide currently published listings, stage unpublish events, invalidate visibility caches, and enqueue search verification. Reactivation restores only listings with `RestorePublicationOnActivate` when current tenant, product, variant, and category authority still accept them. Otherwise restore intent is cleared.

The shared search overlay and SQL-visible reindex identity scan now independently suppress suspended or banned vendors, so recovery/reindex cannot resurrect search visibility during governance lag or partial failure.

## 5. Category/Product Governance Findings

CommerceHub category mapping remains classification authority only. Publish/update, search overlay, and vendor reactivation require live SQL category lineage. Product and variant governance remain SQL-owned through published/online product checks and public variant lineage checks.

When category, product, variant, tenant, or listing authority is absent, derivative paths converge by deletion, suppression, stale-search deletion, no-op, or fail closed behavior.

## 6. Search/Discovery Governance Findings

CommerceHub public list/search/detail paths filter governance-hidden vendors and debt-suspended vendors before returning results, then recompose every candidate through `CommerceHubSearchSqlAuthorityOverlay`.

Homepage/discovery references are treated as candidate references and filtered through current SQL-visible listing/category/vendor authority before return. Elasticsearch documents are not visibility authority.

## 7. Replay/Recovery Governance Findings

Replay, reindex, catch-up reindex, auto-healing, and missing-SQL-listing recovery use SQL overlays before indexing or preserving public visibility. Stale Elasticsearch documents absent from current SQL-visible authority are deleted.

PASS 12.6 hardening extends that recovery boundary so a stale `IsPublished` listing cannot survive reindex when current vendor governance is suspended or banned.

## 8. Auditability Findings

Governance actions are observable through `UserGovernanceAuditLogs`, vendor visibility snapshots, cache invalidation metadata, search verification enqueue counts, structured logs, search sync status fields, and operational convergence telemetry on health/drift paths.

This pass did not introduce a new immutable audit store; it preserves and documents the existing SQL audit and operational telemetry boundaries.

## 9. Chaos Findings

Reviewed chaos cases converge as follows:
- Publish during suppression: publish/update fail through vendor governance.
- Update during suspension: update fails through vendor governance.
- Reindex during governance change: overlay and SQL-visible identity scans suppress suspended/banned vendors.
- Replay during vendor disablement: replay is forced through current SQL overlay before search mutation.
- Startup recovery during category/product removal: category/product/variant/listing authority loss deletes or suppresses derivative visibility.

## 10. Runtime Hardening Performed

`CommerceHubSearchSqlAuthorityOverlay` now checks current SQL `UserGovernanceProfiles` and returns missing visibility for suspended or banned vendors.

`CommerceHubReindexService.LoadSqlVisibleListingIdentitiesAsync` now excludes suspended or banned vendor governance profiles when building SQL-visible listing identities for stale Elasticsearch deletion and count verification.

`MerchantMarketplaceGovernancePrecedenceCertificationTests` freezes the reviewed precedence, suppression, search, recovery, and documentation boundaries.

## 11. Monitoring Improvements

No new telemetry primitive was added. Existing governance audit logs, vendor visibility result metadata, cache invalidation status, search sync status updates, stale-search deletion counts, health checks, structured logs, and `OperationalConvergenceTelemetry` remain the monitoring surface.

## 12. Governance Updates

`.ai-context` architecture files now record PASS 12.6. Future merchant publication, listing governance, vendor reactivation, suppression, search, discovery, replay, recovery, reindex, feed/export, support, ranking, analytics, localization, or global-script changes must preserve current SQL governance precedence and update these tests/docs in the same change.

## 13. Honest Remaining Debt

The listing lifecycle remains boolean-based rather than a full moderation state machine. There is no immutable marketplace governance ledger, universal SQL-vs-derivative parity scanner, universal chaos harness, SIEM-grade audit sink, generated governance registry, or path-specific certification for future feed/export/support/ranking/global-script marketplace visibility writers.

The certified guarantee is scoped to the reviewed CommerceHub backend paths: merchant intent may request, marketplace governance decides, and current SQL governance authority wins over stale merchant, MongoDB, Redis, Elasticsearch, replay, recovery, and discovery evidence.

# SUPERADMIN-CON-01 Phase 2 Marketplace Governance Certification Findings

Date: 2026-05-24

## Scope

This certification reviewed CommerceHub marketplace-wide public visibility governance across featured/global discovery, homepage sections, curated product references, CommerceHub listings, marketplace product read models, public search, moderation-adjacent publication controls, affiliate/public overlap, replay/rebuild recovery, and operational diagnostics.

The marketplace remains a shared public cross-tenant surface. The certification target is not hard tenant isolation of discovery; it is deterministic tenant-owned visibility under replay-safe global governance.

## Boundaries Audited

- CommerceHub listing ownership: SQL `CommerceHubListings` joined to current tenant, product, variant, category, stock, price, media, and publication authority decides marketplace listing visibility.
- Marketplace product read models: Mongo product read models are derivative and now require a current published SQL CommerceHub listing before public marketplace product visibility is rebuilt.
- Homepage and recommendation surfaces: SQL `HomepageSections` are governance records, but curated product/category references are filtered against current SQL CommerceHub listing/category visibility before publish reads and projection rebuilds.
- Public search: CommerceHub Elasticsearch documents are derivative visibility. Index writes run through `CommerceHubSearchSqlAuthorityOverlay`; document ids are tenant-scoped while legacy listing-only ids are deleted during cleanup.
- Moderation/publication overlap: tenant-owned listing publication and governance hide/restore state remain SQL-owned. Super-admin/platform governance may moderate marketplace-wide surfaces, but stale replay must converge through SQL listing authority.
- Affiliate/public overlap: affiliate public projection remains tenant-scoped and SQL-recomposed; CommerceHub visibility does not infer affiliate visibility, and affiliate projection does not become CommerceHub listing authority.
- Replay/rebuild: homepage projection now fences duplicate active delivery before rebuild side effects; CommerceHub product/search rebuilds redeclare SQL authority and delete when SQL visibility is missing.

## Runtime Hardening Added

- `MarketplaceProductReadModelProjectionService` now fails closed unless a current published SQL `CommerceHubListing` exists for the tenant-owned product and a current public variant. Product-only replay can no longer resurrect marketplace product visibility after listing unpublish/delete.
- Marketplace product read-model category identity now comes from the live SQL CommerceHub category attached to the listing, not only the tenant-local product category.
- `HomepageProjectionConsumer` now uses `ProjectionConsumerEventFence` before projection side effects, collapsing duplicate completed and active overlapping homepage replay before Mongo writes.
- Super-admin homepage reference validation now requires active tenant lineage and public variant lineage for curated CommerceHub listings.
- Homepage read/projection hydration now also filters curated listing references through active tenant lineage.
- `CommerceHubProductSearchDocument.GetDocumentId()` now uses tenant+listing scope. Search deletion paths delete both tenant-scoped ids and legacy listing-only ids so stale legacy documents are scrubbed during replay/reindex.

## Replay And Rebuild Guarantees

- A replayed product event may trigger marketplace rebuild, but current SQL listing/product/variant/category/tenant authority decides upsert or delete.
- A replayed homepage publish event may trigger homepage rebuild, but duplicate active delivery collapses before side effects and stale curated references are removed.
- Reindex and search retry can rehydrate from Mongo only as a seed; `CommerceHubSearchSqlAuthorityOverlay` recomposes current SQL authority before index mutation.
- Stale listing unpublish or publish replay cannot preserve search visibility if current SQL listing authority is missing, unpublished, invalid-category, inactive-tenant, inactive-product, or invalid-variant.
- Legacy listing-only Elasticsearch ids remain cleanup targets, not accepted future identity.

## Moderation And Recommendation Governance

- Marketplace moderation remains intentionally lightweight: SQL listing publication/governance state owns listing visibility; super-admin global homepage/CMS moderation owns platform-curated surfaces.
- Recommendations and curated shelves are governance references, not visibility authority. A curated listing id is retained only while current SQL proves tenant, listing, product, variant, and category visibility.
- This phase does not introduce personalized ranking authority or a new moderation bureaucracy. Future ranking engines must persist tenant-scoped identities and recompose SQL visibility before serving or indexing.

## Operational Auditability Findings

- Replay fences, SQL rebuild declarations, dangerous primitive governance, search sync status, and structured logs now cover the inspected marketplace product/homepage/search paths.
- Fail-closed delete/scrub outcomes are expected healthy results when current SQL authority rejects stale public visibility.
- Remaining auditability is intentionally lightweight: not every marketplace management action has a durable SIEM-grade audit store, but high-risk super-admin marketplace mutation remains behind current SQL platform authority.

## Remaining Marketplace Risk Debt

- Marketplace reviews/localization/CMS-adjacent surfaces were reviewed at boundary level but not universally migrated to shared replay fences in this phase.
- Some older CommerceHub listing Mongo paths still use specialized freshness filters and local SQL overlay calls rather than a single universal marketplace projection framework.
- Legacy Elasticsearch listing-only documents may exist until replay/reindex/delete paths scrub them; new writes use tenant-scoped ids.
- No adaptive marketplace rebuild SLO, personalized recommendation framework, enterprise moderation workflow, support impersonation ceremony, or SIEM-grade audit store was introduced.
- Future feed/export, ranking, cache warmer, review, localization, and global runbook paths must add path-specific SQL authority, replay, tenant identity, moderation/audit, and documentation tests when expanded.

## Regression Freeze Invariants

- Do not rebuild marketplace public product visibility from product SQL alone; require current SQL CommerceHub listing authority.
- Do not serve or index curated homepage/recommendation references without current SQL listing/category/tenant visibility.
- Do not introduce listing-only or product-only search document ids for tenant-owned marketplace visibility.
- Do not allow duplicate homepage/recommendation replay to repeat Mongo/Search/Redis side effects before a durable replay fence or documented equivalent.
- Do not let super-admin marketplace moderation bypass current SQL platform authority, SQL listing ownership, or operational audit diagnostics.
- Do not claim universal marketplace helper adoption for paths that still use equivalent local SQL checks.

## 2026-05-24 Phase 3 Recovery Governance Addendum

- Marketplace recovery was re-certified across product read-model rebuilds, homepage replay, CommerceHub search reindex, tenant-admin CommerceHub recovery, dead-letter retry, and rebuild interruption/restart behavior.
- Tenant-admin CommerceHub search reindex is now tenant-scoped. It uses trusted tenant context and `ReindexTenantAsync`; global all-listing reindex remains platform/runbook scope.
- CommerceHub search SQL recomposition now declares listing authority explicitly through `SqlAuthorityOverlayKind.CommerceHubListing`, so stale product-only recovery cannot resurrect search visibility after listing unpublish/delete or moderation removal.
- Reindex overlap is deterministic: overlapping runs may repeat SQL authority declarations and index/delete attempts, but Mongo, Elasticsearch, and old listing ids remain derivative evidence only.
- Tenant-scoped reindex handles Mongo-present and Mongo-missing listing paths. Mongo-present documents are recomposed through SQL overlay; Mongo-missing SQL listing seeds are rebuilt from SQL or deleted from search when current SQL rejects visibility.
- Operational pacing assumptions are bounded configuration, not capacity promises. Operators may tune batch size and delay, but must not bypass SQL overlays, tenant scope, legacy-id cleanup, or fail-closed deletion.
- Remaining marketplace recovery debt stays path-specific: review/localization/feed/export/ranking/cache-warmer/global-runbook paths still need their own replay/rebuild certification before universal marketplace recovery claims.

## 2026-05-24 Phase 4 Emergency Governance Addendum

- Marketplace-wide moderation and discovery operations remain platform authority, not tenant authority.
- Non-read marketplace super-admin operations are now covered by request-level operational audit after current SQL platform actor recomposition; domain-specific moderation, refund, payout, and finance audit records remain the richer business audit where present.
- Emergency marketplace rollback must scrub or rebuild marketplace read models, homepage references, and search documents from current SQL listing/category/tenant/product/variant authority. It must not restore curated visibility from old homepage JSON, Mongo documents, search hits, exports, or support-tool snapshots.
- Support impersonation remains uncertified for marketplace mutation. Any future support path must preserve SQL platform/support actor authority, tenant/listing lineage, request-level audit, domain audit, and replay-safe convergence.

## 2026-05-24 Phase 5 Operational Chaos Addendum

- Marketplace visibility was re-certified under replay storms, rebuild overlap, stale homepage/recommendation replay, cross-tenant search pressure, moderation/review replay, and emergency recovery overlap.
- Targeted hardening added: review aggregate replay now revalidates CommerceHub search mutation through `CommerceHubSearchSqlAuthorityOverlay`; stale review-triggered search updates delete tenant-scoped search visibility when SQL listing authority no longer proves publication.
- Validated convergence remains SQL-listing-authoritative: product replay, review replay, homepage replay, reindex restart, and global recovery can trigger recomposition, but current SQL tenant/listing/product/variant/category/stock/price/media authority decides index, delete, scrub, no-op, or fail-closed behavior.
- Operational misuse finding: raw search writes, listing-only search identity, curated JSON resurrection, and rebuilding marketplace visibility from stale Mongo documents remain forbidden shortcuts.
- Remaining debt is still path-specific: review export surfaces, localization, personalized ranking, feeds, cache warmers, support tooling, and future global runbooks need their own replay/rebuild/moderation/audit certification when expanded.
- Detailed Phase 5 findings live in `docs/architecture/super-admin-operational-chaos-certification-findings.md`.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 5 Public Visibility Addendum

- This pass re-certified CommerceHub search, marketplace product read models, homepage/recommendation projection, curated-reference validation, review/moderation replay search sync, reindex/rebuild overlap, tenant-safe aggregation, and operational public projection governance.
- Confirmed residual gap hardened: several non-search marketplace/public visibility paths treated an active CommerceHub leaf category as enough category authority. CommerceHub search already walked the active parent chain, but marketplace product read-model rebuilds, homepage projection sanitation, homepage public-read sanitation, super-admin homepage reference validation, and reindex count verification could preserve or count a listing/category whose parent lineage was inactive, missing, or cyclic.
- Additional residual gap hardened: CommerceHub search auto-healing and popularity maintenance still had narrow derivative shortcuts. Auto-healing could delete only the legacy listing id in some fail-closed branches, and popularity replay could reindex search documents directly from Mongo publication state after updating popularity metadata.
- Additional residual gap hardened: CommerceHub search overlay and marketplace product read-model rebuild still accepted legacy or derivative image strings after current SQL media ownership was gone. A removed public image could therefore remain visible through rebuild/replay even though `ProductMedia` and `ProductVariants.MediaId` no longer proved it.
- Additional residual gap hardened: CommerceHub search replay retained Mongo-carried `AverageRating` and `ReviewCount` fields. A stale review summary could survive reindex/recovery after current SQL moderation state approved, rejected, deleted, or otherwise changed the review set.
- Additional residual gap hardened: CommerceHub product-detail reads still trusted a published Mongo listing and storefront product document without reapplying the CommerceHub SQL visibility overlay. That allowed a stale listing/category/image detail response to survive even after search and rebuild paths had already failed closed.
- Runtime hardening added `CommerceHubCategoryLineageAuthority` as the shared SQL lineage boundary for global CommerceHub categories. CommerceHub search overlay, CommerceHub listing projection, marketplace product read-model rebuild, homepage/recommendation projection, homepage public reads, super-admin homepage validation, CommerceHub query category/suggestion filtering, and reindex verification now converge on the same live category parent-chain semantics.
- Runtime hardening also routes CommerceHub auto-healing and search-analytics search mutation back through `CommerceHubSearchSqlAuthorityOverlay` and uses tenant-scoped delete when current SQL visibility is gone.
- Runtime hardening also routes CommerceHub product-detail reads back through `CommerceHubSearchSqlAuthorityOverlay`, keeps CommerceHub category identity on the listing-authoritative value, and stops detail variant image resolution from restoring legacy per-variant image strings when SQL media authority is gone.
- Runtime hardening also makes CommerceHub public image recomposition media-authoritative: search overlay, public product read-model rebuild, and product-update listing fanout use only current SQL `ProductMedia`/`ProductVariants.MediaId` and clear the public image when SQL cannot prove current media linkage.
- Runtime hardening also makes search review decorations moderation-authoritative on every overlay mutation: `AverageRating` and `ReviewCount` are rebuilt from current approved, non-deleted SQL `ProductReviews`, never retained from a Mongo/search seed document.
- Replay/rebuild guarantee strengthened: a listing or curated category reference with stale CommerceHub category lineage now fails closed by search deletion, marketplace read-model delete, curated reference scrub, validation rejection, or reindex-count exclusion. A replayed homepage/recommendation event cannot preserve a curated product/category through a leaf-only category check.
- Search maintenance/recovery guarantee strengthened: replayed popularity updates, auto-healing retries, and stale maintenance overlaps may update derivative popularity metadata, but they cannot preserve or resurrect search visibility without current SQL listing/category/product authority and cannot leave a tenant-scoped search document behind when fail-closed deletion runs with known tenant scope.
- Media and moderation replay guarantee strengthened: replay/rebuild/recovery cannot restore a deleted image from legacy product/variant image fields or derivative documents, and review-triggered or maintenance-triggered search replay cannot restore stale rating/count decorations that differ from current approved SQL review truth.
- Public read guarantee strengthened: a stale Mongo listing can no longer keep the CommerceHub detail endpoint alive after SQL listing/category/publication authority is gone, and detail hydration can no longer relabel the listing with a storefront category name or a legacy variant image string after replay/rebuild drift.
- Tenant-safe aggregation guarantee held: marketplace discovery remains global, but listing/product visibility still requires tenant-owned SQL listing/product/variant/stock/price/media authority and now also shared CommerceHub category lineage authority before aggregation, recommendation, homepage, or search visibility is retained.
- Regression protection added: runtime projection tests cover invalid CommerceHub category parent lineage for product read-model rebuilds and homepage curated category scrubbing; detail-mapping tests cover category-identity preservation and legacy image suppression; architecture freeze tests require shared category lineage, overlay-governed maintenance, SQL-overlaid detail reads, tenant-scoped delete, SQL-only public media, and SQL-approved review aggregate recomposition.
- Moderation/review semantics are now strengthened and certified: review aggregate replay is not visibility authority; review-triggered search sync passes through `CommerceHubSearchSqlAuthorityOverlay`, deletes tenant-scoped search visibility when SQL listing/category/product authority is missing, and rehydrates search rating/count decoration only from approved, non-deleted SQL reviews.
- Remaining marketplace debt is honest: this pass does not introduce a personalized ranking engine, enterprise moderation workflow, universal feed/export/cache-warmer certification, adaptive rebuild SLO, SIEM-grade audit store, or support impersonation ceremony. Popularity/click metadata remains derivative ranking input, not a promoted visibility authority. Future ranking, feed/export, localization, support, review-export, analytics, and cache-warmer paths must adopt the same SQL listing/category/media/moderation recomposition before public visibility mutation.

## 2026-05-27 PASS 5 Suggestion-Term Visibility Addendum

- Follow-up certification found one residual drift inside the already-hardened CommerceHub suggestion boundary: product suggestion cards were SQL-overlaid correctly, but the final “is this term still public?” proof for popular/similar/autocomplete text terms still relied on a narrower SQL candidate query.
- That narrower proof accepted any active variant row and did not re-run the full CommerceHub SQL overlay or vendor-order-suspension suppression before keeping a term alive. Under replay pressure, a superseded variant or a vendor suspended from new orders could therefore leave a public term suggestion behind after product-card visibility had already failed closed.
- Runtime hardening now treats text-term survival as a public-visibility decision, not just a query-log heuristic. Candidate listings are only accepted when they survive `CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(...)`, and candidate vendors hidden by current governance or suspended from new orders are suppressed before a term is considered live.
- Replay/rebuild guarantee strengthened: popular/similar/autocomplete term suggestions can no longer survive only because a raw SQL listing/product/category LIKE match still exists. If no current SQL-overlaid CommerceHub listing remains public, or only suspended/hidden vendors match, the term is dropped.
- Regression protection added: focused runtime tests now freeze superseded-variant and vendor-suspension term suppression on `GetPopularQueriesAsync(...)`, and architecture source guards keep the query service on overlay-driven term validation.

## 2026-05-27 PASS 5 Public Vendor Visibility Addendum

- Follow-up certification found one remaining cross-surface drift after search and suggestion hardening: CommerceHub search already suppressed governance-hidden vendors and vendors suspended from new orders, but adjacent public marketplace product/homepage reads and curated-reference validation did not apply the same runtime vendor-visibility proof consistently.
- That mismatch could let marketplace product list/detail responses, homepage curated shelves, or super-admin curated product references preserve stale visibility for a vendor that CommerceHub search had already failed closed.
- Runtime hardening added `CommerceHubPublicVendorVisibilityService` as the shared boundary for public vendor suppression, combining current SQL governance hide/ban state with current vendor-financial "suspended from new orders" state.
- Marketplace product list reads now re-key cache entries by current visibility version, compute matching vendor ids for the current query, and exclude suppressed vendors before paging. Replay/rebuild overlap may re-run suppression, but it cannot cache or serve a hidden/suspended vendor from stale Mongo state.
- Marketplace product detail reads now fail closed when the current vendor is suppressed, even if a stale Mongo listing document and cached detail payload still exist.
- Homepage/recommendation semantics are now tighter and deterministic: public homepage reads, homepage section sanitation, and super-admin curated-reference validation all filter curated product ids through the same current vendor-visibility boundary before product visibility is preserved.
- Replay/rebuild guarantee strengthened: rebuild, reindex, moderation replay, cache hydration, and homepage overlap may ask current SQL for visibility repeatedly, but hidden or order-suspended vendors converge to scrub/reject/not-found outcomes instead of stale public visibility resurrection.
- Tenant-safe aggregation guarantee held: marketplace discovery remains intentionally global, but every retained listing now requires both tenant-owned SQL listing/product/variant/category/media/stock/publication authority and current vendor suppression clearance before aggregation into search, homepage, or direct public product reads.
- Regression protection added: focused runtime tests freeze suppressed-vendor exclusion on marketplace list/detail/homepage reads and super-admin curated-reference rejection, while architecture freeze tests require the inspected controller/services to keep using the shared vendor-visibility boundary.
- Remaining debt remains honest: this addendum closes the reviewed product/homepage/curation slice, not every future ranking, feed/export, localization, review-export, analytics, support, cache-warmer, or global-script path that may preserve marketplace visibility. Those paths still require path-specific public-visibility certification before expansion.

## 2026-05-27 PASS 5 Service-Marketplace Operational Reindex Addendum

- Follow-up certification found one residual operational-governance gap on the older service-marketplace search surface adjacent to CommerceHub: `MarketplaceController.ReindexServices(...)` allowed both tenant admins and super admins to delete/recreate the global `marketplace_services` Elasticsearch index and rebuild all tenants.
- That endpoint is not CommerceHub product listing authority, but it is still a public marketplace search projection primitive. Under incident pressure, a tenant-admin route that can run global index recreation is the same class of unsafe scope widening already forbidden for CommerceHub search recovery.
- Runtime hardening now splits scope deterministically. Super admins retain the global service-marketplace index recreation/rebuild primitive, while tenant admins run a tenant-scoped repair branch that scans only their tenant's current search documents, indexes only current active SQL `Service` rows for that tenant, and deletes only stale documents for that tenant.
- Tenant-admin repair no longer creates or deletes the global index. If the global index is missing, tenant repair fails closed with `GLOBAL_SEARCH_INDEX_UNAVAILABLE` and requires platform/global recovery rather than turning a tenant operation into global search mutation.
- Both global and tenant branches now index only current active, non-deleted SQL services. Inactive or deleted service rows converge to absence from public service search rather than being preserved as inactive derivative documents.
- Replay/rebuild guarantee strengthened: repeated tenant repair may re-index current tenant service documents and delete stale tenant service documents, but it cannot recreate global mappings, delete other tenants' documents, or rebuild public service visibility from inactive/deleted SQL rows.
- Tenant-safe aggregation guarantee held: service-marketplace discovery remains intentionally cross-tenant, but tenant-admin operational recovery is tenant-scoped. Platform-wide repair remains explicit super-admin/global scope.
- Regression protection added: marketplace governance source-freeze tests now require the tenant/global split, active SQL service filtering, tenant-scoped stale deletion, and fail-closed missing-index behavior.
- Remaining debt remains scoped: this hardens the reviewed legacy service-marketplace reindex endpoint only. The service discovery projection consumer and drift reconciliation job still use specialized local SQL/Elasticsearch controls and need path-specific certification if their authority model is expanded toward CommerceHub-like product/listing governance.

## 2026-05-27 PASS 6 Service-Marketplace Recovery Follow-Up

- Follow-up operational tooling certification closed the adjacent legacy service-marketplace replay/reconciliation gap left as debt in the tenant-admin reindex addendum.
- `ServiceDiscoveryProjectionConsumer` now treats service create/update/activation/trust/statistics events as triggers only. Before preserving search visibility, it reloads current SQL service state and requires active service, active non-deleted tenant, and active same-tenant provider-profile authority.
- If current SQL authority is missing, inactive, deleted, or cross-tenant, replay deletes or confirms absence of the stale `marketplace_services` document instead of restoring it from event payload fields.
- `ElasticsearchDriftReconciliationJob` now declares global SQL search authority and builds its live service set from the same service/tenant/provider-profile authority. Global drift repair no longer treats raw active service ids as enough public search authority.
- Regression protection now freezes the legacy service search consumer and drift job around current SQL authority recomposition, stale delete behavior, and the `ServiceMarketplace` rebuild authority marker.
- Remaining service-marketplace debt is still scoped: this does not add CommerceHub-style category/media/moderation governance, personalized ranking certification, or a universal service projection framework. Future service ranking, localization, feed/export, support, cache-warmer, profile-governance, or global-script paths need path-specific certification.

## 2026-05-27 PASS 5 Search Read Suppression Addendum

- Follow-up certification found one narrower CommerceHub search-integrity drift on the already-hardened public read surface: product-card response shaping suppressed hidden and order-suspended vendors after SQL overlay, but the upstream search/suggestion/autocomplete request shaping and cached suggestion-product revalidation still handled those suppression modes inconsistently.
- That mismatch let Elasticsearch/Mongo search totals, facet buckets, and suggestion/autocomplete candidate sets over-count or temporarily retain vendors that the final public product-card layer would later strip. The visible page could look correct while public counts or cached suggestion payloads still reflected stale vendor visibility.
- Runtime hardening now treats current vendor suppression as part of CommerceHub search-read authority on the inspected response boundary. `CommerceHubQueryService` computes the current publicly suppressed vendor set before browse/search/suggestion/autocomplete requests, reuses that set in cache keys, and applies the same combined suppression when revalidating cached or projected suggestion/autocomplete products.
- Public-search guarantee strengthened: browse/search totals, facet inputs, Elasticsearch/Mongo suggestion candidates, and cached autocomplete/suggestion product payloads now converge through the same hidden-or-suspended vendor suppression contract before the public response is returned on the inspected CommerceHub query paths.
- Regression protection added: focused runtime tests now freeze suspended-vendor exclusion on global browse/search requests plus governance-hidden-vendor suppression on suggestion/autocomplete revalidation, and source-freeze coverage now requires the shared suppressed-vendor loader on the query service boundary.
- Remaining debt stays explicit: this addendum hardens the reviewed CommerceHub query/read surface, not every search-index maintenance path. Elasticsearch may still contain stale suppressed-vendor documents until overlay-governed reindex, replay, or recovery touches them, so future index-maintenance certification should bring the same vendor-suppression proof into mutation/rebuild verification paths.

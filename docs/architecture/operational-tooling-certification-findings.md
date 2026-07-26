# POS-ADMIN-CERTIFICATION-01 PASS 6 Operational Tooling & Dangerous Primitive Certification

Date: 2026-05-25

## Scope

This pass reviewed operational tooling, recovery primitives, rebuild/reindex endpoints, repair APIs, replay/retry tooling, raw mutation surfaces, dangerous overrides, and operator-facing recovery guidance.

The certification target is not removal of operational power. The target is deterministic SQL-authoritative convergence under operational pressure, with dangerous primitives staying fail-closed, attributable, tenant-safe, and replay-safe.

## Boundaries Audited

- Tenant-admin rebuild and reindex endpoints for storefront search, CommerceHub search, category/search sync, and product read-model repair.
- SQL-first recovery workers and replay/retry paths already hardened in prior passes.
- Dangerous primitive governance boundaries for raw SQL/bulk mutation, Mongo full replacement, raw Elasticsearch writes, Redis fanout, and platform/global repair scope.
- Existing recovery/runbook documentation, chaos/convergence tests, and source-governance freezes.

## Findings

### Hardened finding: baseline operational search repair was still a hidden authority path

- `AdminSearchSyncController.SyncProducts(...)` still rebuilt storefront product search visibility from partial SQL rows and direct Elasticsearch bulk writes.
- That path did not recompose the current SQL search overlay, so it could omit unit-authoritative storefront pricing, stock/promotion/affiliate/search enrichment, and fail-closed category-lineage suppression.
- Under operator pressure, that made the endpoint a hidden operational authority path rather than a pure SQL-first recovery primitive.

### Hardened finding: product read-model repair bypassed the reviewed Redis invalidation boundary

- `AdminSyncController` already routed product projection rebuilds through `IProductSqlAuthorityProjectionWriter`, but it still enumerated Redis keys and deleted them directly after bulk or single-product repair.
- That direct Redis key mutation duplicated the reviewed cache service boundary and left a tenant-admin recovery primitive outside the documented dangerous-mutation freeze.
- The cache values were not business truth, but operational bypass of a governed invalidation boundary is still an attributable primitive-governance defect.

### Hardened finding: affiliate repair/reindex could silently inject fallback currency and preserve stale visibility

- `AF16E_ReindexAffiliateProductsWithStorefrontUrl` is a reviewed tenant-scoped repair primitive that updates MongoDB and Elasticsearch affiliate visibility directly.
- The script still normalized blank tenant currency to `USD`, which let an operational repair run manufacture tenant-facing derivative currency instead of requiring current SQL `Tenant.Currency` authority.
- When tenant storefront metadata or currency authority was missing, the script could also skip mutation and leave stale affiliate visibility behind instead of converging fail closed.

### Hardened finding: one-way Mongo sync could preserve stale category and shipping-method visibility

- `AdminDataSyncController` category and shipping-method sync operations already rebuilt Mongo documents from current tenant-scoped SQL rows, but they behaved like additive repair only.
- Repeated sync runs updated current rows without deleting tenant-scoped Mongo documents whose SQL rows had been hard-deleted or otherwise removed from the current authority set.
- That made the endpoint a hidden stale-visibility preservation primitive under recovery pressure: SQL authority could be correct while old category or shipping-method documents survived indefinitely in Mongo.

### Hardened finding: category search sync could preserve stale Elasticsearch visibility after SQL deletion

- `AdminSearchSyncController.SyncCategories(...)` already rebuilt category search documents from the current SQL lineage snapshot and deleted invalid-lineage categories it still saw in SQL.
- It did not, however, scrub tenant-scoped Elasticsearch category documents whose source SQL rows were gone entirely.
- That made the endpoint a hidden one-way additive search repair primitive under recovery pressure: repeated sync runs could preserve stale tenant-scoped category search visibility after SQL deletion.

### Hardened finding: legacy service-marketplace search recovery could restore service visibility without tenant/profile authority

- `ServiceDiscoveryProjectionConsumer` and `ElasticsearchDriftReconciliationJob` are legacy service-marketplace search mutation paths that write the global `marketplace_services` Elasticsearch index directly.
- Before this follow-up, replayed service create/update/activation events and global drift repair could restore a public service document from event payloads or active service rows without proving current active tenant lineage and current same-tenant active provider-profile authority.
- Under replay or global drift repair pressure, that made service search visibility more permissive than the tenant-admin service reindex branch hardened in PASS 5.

## Runtime Hardening Added

- `AdminSearchSyncController` product sync now recomposes each product through `ProductSearchSqlAuthorityOverlay.ApplyAsync(...)` before any Elasticsearch mutation.
- Search documents now come from `ProductSearchDocumentMapper.MapFromProductDocument(...)`, so the repair path uses the same SQL-authoritative storefront/unit/media/promotion/search shape as the reviewed recovery path.
- Missing or invalid current SQL visibility now converges by delete through `DeleteProductFromSearchAsync(...)` instead of indexing partial product rows.
- The endpoint remains tenant-scoped through `ITenantContext.RequiredTenantId`; no global/platform scope was added.
- `AdminSyncController` now delegates product cache invalidation to tenant-scoped `IProductCacheService.InvalidateAllProductsAsync(...)` and `InvalidateProductAsync(...)`; the controller no longer obtains Redis servers or deletes Redis keys directly.
- Cache invalidation failure remains non-authoritative and diagnosable: repair convergence is retained and a structured warning is emitted for follow-up instead of inventing a second cache-repair path.
- Focused runtime tests verify bulk and single-product repair delegate cache invalidation through the reviewed service boundary after SQL-backed projection repair.
- `AF16E_ReindexAffiliateProductsWithStorefrontUrl` now normalizes tenant currency through `CurrencyNormalizationHelper.NormalizeCurrencyOrThrow(...)` instead of a silent `USD` fallback.
- The affiliate reindex script now declares `SqlTruthRebuildGuard.RequireSqlAuthority(...)` for each reviewed product/intelligence repair mutation, making the raw Mongo/Elasticsearch repair surface an explicit SQL-first rebuild boundary instead of an implicit helper script.
- When current tenant storefront metadata or tenant currency authority is missing, the script now deletes stale public affiliate visibility and stale affiliate intelligence visibility rather than preserving derivative state.
- `AdminDataSyncController.SyncCategories(...)` and `SyncShippingMethods(...)` now declare `SqlTruthRebuildGuard.RequireSqlAuthority(...)` before bulk Mongo mutation.
- Both sync paths now delete tenant-scoped stale Mongo documents that are missing from the current SQL authority set after the authoritative upsert pass finishes, so reruns converge instead of only ever adding or updating derivative rows.
- The concrete stale-derivative scrub now lives in `DeleteStaleCategoryDocumentsAsync(...)` and `DeleteStaleShippingMethodDocumentsAsync(...)`, making the reviewed one-way Mongo sync escape hatch explicit and frozen.
- Focused runtime tests now verify category and shipping-method sync both carry authoritative ids into the stale-delete filter and report the stale-delete result in their operational response payloads.
- `AdminSearchSyncController.SyncCategories(...)` now declares SQL rebuild authority through `SqlTruthRebuildGuard.RequireSqlAuthority(...)` and calls `IUnifiedSearchIndexService.DeleteStaleCategoriesAsync(...)` after the authoritative sync pass.
- The reviewed search-service stale scrub deletes tenant-scoped category documents that are missing from the current SQL authority set, making repeated category search repair convergent instead of additive-only.
- Focused runtime tests now verify tenant-admin category search sync carries authoritative category ids into the stale-search-delete filter and reports `staleDeletedFromSearch` in the operational response payload.
- `ServiceDiscoveryProjectionConsumer` now treats service events as triggers only. Create, update, activation, trust, and statistics replay first prove current SQL service, active tenant, and same-tenant active provider-profile authority before preserving service search visibility.
- Missing or inactive service, tenant, or provider-profile authority now deletes the stale `marketplace_services` document instead of restoring it from event payload fields.
- Trust and statistics replay now rehydrates the base service search document from current SQL authority before applying trust/stat fields, so a missing Elasticsearch document converges from SQL rather than failing until a separate service-event replay.
- `ElasticsearchDriftReconciliationJob` now declares global SQL search authority through `SqlTruthRebuildGuard.RequireGlobalSqlAuthority(...)` and builds its live service set only from current active services joined to active tenants and same-tenant active provider profiles.
- The legacy service-marketplace drift job therefore deletes stale documents for inactive/deleted services, inactive/deleted tenants, and missing/inactive/cross-tenant provider profiles rather than treating raw active service ids as sufficient public-search authority.

## Replay / Rebuild / Recovery Guarantees Validated

- Product search repair is now SQL-authoritative, not baseline-row-authoritative.
- Invalid current SQL category lineage, deleted rows, unpublished/offline state, or missing current SQL authority fail closed by deleting stale search visibility.
- Overlapping or repeated admin search repair calls remain safe because each product redeclares SQL authority through the overlay before index/delete mutation.
- The repair endpoint does not restore search visibility from stale Mongo, Elasticsearch, or historical repair snapshots.
- Category search sync now also converges stale derivative absence as well as presence. If a tenant-scoped category row no longer exists in current SQL authority, the reviewed sync path deletes the stale Elasticsearch document instead of preserving it as hidden search visibility.
- Product read-model rebuild continues to recompose Mongo visibility through `IProductSqlAuthorityProjectionWriter`; post-rebuild cache clearing is now performed only through the tenant-scoped product cache boundary.
- Affiliate repair/reindex no longer treats blank tenant currency as acceptable operational evidence. Missing current SQL tenant currency now rejects silent fallback and converges by tenant-scoped stale derivative deletion.
- The reviewed AF16E raw Elasticsearch/Mongo repair primitive now redeclares SQL rebuild authority and fail-closed tenant/storefront metadata semantics before mutation.
- Category and shipping-method sync now converge stale derivative absence as well as presence. If a tenant-scoped category or shipping-method row no longer exists in current SQL authority, the reviewed sync path deletes the stale Mongo document instead of preserving it as hidden recovery state.
- Legacy service-marketplace search recovery now also converges from SQL authority. Replayed service events and global drift repair cannot restore a public service document unless current SQL proves active service, active tenant, and active same-tenant provider-profile authority.

## Dangerous Primitive Guarantees

- Raw Elasticsearch mutation remains allowed only behind the reviewed search service boundary.
- Tenant-admin operational search repair remains tenant-scoped and cannot widen into global index deletion/recreation.
- Partial SQL-row search rebuild is now explicitly classified as a dangerous operational shortcut, not an acceptable recovery mode.
- Direct Redis key mutation in `AdminSyncController` is removed; product repair must use `IProductCacheService` rather than scanning/deleting keys from a controller.
- Silent tenant currency fallback during operational affiliate repair is now explicitly forbidden. Reviewed affiliate repair tooling must use current SQL `Tenant.Currency` authority or fail closed.
- One-way Mongo sync is now explicitly forbidden on reviewed rebuild tooling. Tenant-admin category and shipping-method sync must delete tenant-scoped stale derivative rows that no longer exist in SQL authority.
- One-way category search sync is now explicitly forbidden on reviewed rebuild tooling. Tenant-admin category search sync must delete tenant-scoped stale Elasticsearch rows that no longer exist in SQL authority.
- Raw service-marketplace Elasticsearch mutation is still allowed for the legacy service discovery boundary, but it is no longer allowed to publish from event payloads or raw service rows alone. Current SQL tenant and provider-profile authority must accept the service first.
- `PaymentCancellationOutboxProcessor` is a reviewed direct-SQL exception only for PostgreSQL transaction-scoped advisory locking. The reviewed primitive is `SELECT pg_advisory_xact_lock` inside the claim transaction using the stable tenant/order key; it is not approval for direct payment, order, outbox processed-state, or lifecycle mutation.

## Operational Auditability Findings

- The hardening preserves existing request logs and low-cardinality recovery diagnostics.
- Failed reviewed cache invalidation after product repair is logged at the operational controller boundary without treating cache state as SQL authority.
- Missing reviewed tenant currency/storefront authority during AF16E affiliate repair is now logged as fail-closed stale-visibility deletion rather than hidden fallback mutation.
- Category and shipping-method sync responses now report `staleDeleted`, so operators can distinguish a no-op rebuild from a convergent stale-document scrub on the reviewed Mongo repair boundary.
- Category search sync responses now report `staleDeletedFromSearch`, so operators can distinguish current-row indexing from a convergent stale-search scrub on the reviewed Elasticsearch repair boundary.
- Service-marketplace replay and drift-reconciliation logs now distinguish SQL-authority convergence from stale search deletion. Global drift repair also emits the existing drift diagnostics plus a global SQL authority declaration before mutation.
- Payment-cancellation outbox overlap remains diagnosable through the existing claim outcome and active-overlap telemetry; the reviewed direct SQL path is limited to claim serialization before lifecycle-service processing.
- The pass does not claim a new SIEM-grade audit store or a new operator workflow; it narrows concrete hidden-authority and primitive-bypass paths in existing tooling.

## Regression Freeze Invariants

- Tenant-admin storefront product search repair must recompose through `ProductSearchSqlAuthorityOverlay`.
- Search repair documents must be built from `ProductSearchDocumentMapper.MapFromProductDocument(...)` after SQL overlay recomposition, not from partial SQL rows.
- Missing or invalid current SQL visibility must delete/tombstone search visibility rather than index partial or stale derivative state.
- Tenant-admin search tooling must remain tenant-scoped; global search control remains platform/runbook scope.
- `AdminSearchSyncController.SyncCategories(...)` must declare SQL rebuild authority and must delete tenant-scoped stale Elasticsearch category documents that are missing from current SQL authority. One-way category search sync is not a certified recovery mode.
- `AdminSyncController` must retain `IProductSqlAuthorityProjectionWriter` for read-model convergence and `IProductCacheService` for post-rebuild cache invalidation; it must not restore direct Redis server/key mutation.
- `AF16E_ReindexAffiliateProductsWithStorefrontUrl` must not inject `USD` or any other silent default when tenant currency authority is blank.
- Reviewed affiliate repair/reindex must declare SQL rebuild authority and delete stale affiliate visibility when current tenant storefront/currency authority is missing.
- `AdminDataSyncController` category and shipping-method sync must declare SQL rebuild authority and must delete tenant-scoped stale Mongo documents that are missing from current SQL authority. One-way Mongo sync is not a certified recovery mode.
- `ServiceDiscoveryProjectionConsumer` and `ElasticsearchDriftReconciliationJob` must keep current SQL service, active tenant, and same-tenant active provider-profile checks before restoring `marketplace_services` visibility. Raw service event payloads and raw active service ids are not enough service-search authority.
- `PaymentCancellationOutboxProcessor` may retain direct SQL only for transaction-scoped advisory locking and must keep the reviewed transaction, stable tenant/order lock key, claim lease, and concurrency-conflict markers.

## Validation

- Focused validation passed on 2026-05-25 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore -m:1 -nologo -v minimal --filter "FullyQualifiedName~AffiliateCurrencyProjectionArchitectureTests|FullyQualifiedName~GlobalReliabilityInfrastructureExtractionTests|FullyQualifiedName~SemanticConvergenceChaosValidationTests|FullyQualifiedName~OperationalToolingCertificationFreezeTests|FullyQualifiedName~OperationalRecoveryRunbookArchitectureTests"` (`33` passed, `0` failed).
- Focused follow-up validation passed on 2026-05-27 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build -nologo -v minimal --filter "FullyQualifiedName~MarketplaceGovernanceCertificationTests|FullyQualifiedName~OperationalToolingCertificationFreezeTests|FullyQualifiedName~OperationalRecoveryRunbookArchitectureTests|FullyQualifiedName~GlobalReliabilityInfrastructureExtractionTests|FullyQualifiedName~CrossSystemAuthorityRegressionGuardTests|FullyQualifiedName~EventOutboxConsistencyArchitectureTests.ProviderTrustProjections_MustRejectStaleAggregateEvents"` (`44` passed, `0` failed).
- `dotnet build PointOfSale.sln -nologo -v minimal -m:1` passed on 2026-05-27. Compilation emitted existing nullable, obsolete API, and xUnit-analyzer warnings outside the targeted PASS 6 changes; no compile error blocked certification.

## Remaining Operational-Risk Debt

- This pass hardens four concrete tenant-admin operational surfaces: storefront search repair authority, category search stale-visibility convergence, product-rebuild cache invalidation governance, and category/shipping Mongo sync convergence. It does not certify every future CMS, SEO, analytics, cache-warmer, export, or support-tool search/rebuild path.
- This pass also narrows one concrete reviewed affiliate repair primitive: AF16E affiliate reindex now uses explicit tenant currency authority and fail-closed stale-visibility deletion, but it still relies on equivalent local SQL tenant checks rather than universal helper adoption.
- `AdminSearchSyncController.SyncCmsPages(...)` remains intentionally unimplemented rather than acting as a partial repair primitive. If it becomes operational, it must declare SQL authority, replay behavior, dangerous primitive governance, and tests in the same change.
- The broader repo still uses focused allowlists and reviewed-pattern tests for dangerous primitives rather than a universal analyzer.
- The legacy service-marketplace path is now SQL-first for service, tenant, and provider-profile authority, but it still does not claim CommerceHub-grade listing/category/media/moderation governance. Future service ranking, localization, feed/export, support, cache-warmer, or service-profile governance expansion still needs path-specific certification.
- Two `EventOutboxConsistencyArchitectureTests` assertions were already out of sync with current repository code during the 2026-05-27 broad focused run: `ClaimLeaseDurationSeconds = 300` and `var currency = await ResolveTenantCurrency...`. They are tracked as unrelated freeze drift and were not suppressed by this pass.

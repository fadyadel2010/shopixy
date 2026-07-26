# PASS F-S1-F Service Marketplace Frontend Listing Visibility & Lifecycle Authority

Service Marketplace Frontend Listing Visibility & Lifecycle Authority is production-certifiable for inspected runtime paths after tenant-status, provider-governance, projection, reindex, request-entry, and cross-tab lifecycle convergence hardening.

## Listing Visibility Findings

- PostgreSQL `Services.IsActive`, `Services.IsDeleted`, current tenant lifecycle, active non-deleted marketplace profile lineage, and provider governance decide visibility.
- Hidden, inactive, or deleted services fail current SQL discovery and detail composition. DTOs and Elasticsearch documents cannot restore them.

## Provider Lifecycle Findings

- Suspended or banned Vendor governance for the service tenant now suppresses discovery, detail, projection, reindex, diagnostics, and request entry.
- Provider lifecycle state is backend authority. Frontend provider names, cards, route state, and cached DTOs are derivative only.

## Profile Lifecycle Findings

- Marketplace profiles must exist, remain non-deleted, active, and belong to the service tenant.
- Profile deactivation or deletion removes service visibility at current SQL read time and causes projection convergence to delete stale search documents.

## Tenant Lifecycle Findings

- `Tenant.Status == TenantStatus.Active` is now required in addition to legacy `Tenant.IsActive` and non-deletion.
- Pending, suspended, deleted, inactive, or missing tenants cannot preserve service visibility or request-entry capability.

## Discovery Lifecycle Findings

- Elasticsearch remains candidate evidence. Every result is recomposed from current SQL lifecycle authority before filtering, counting, and pagination.
- Stale pages cannot resurrect removed listings; invalid candidates are excluded before visible totals and page slicing.

## Card Lifecycle Findings

- Cards render only from current backend discovery snapshots and are withheld while authority refresh is in progress or after refresh failure.
- Card navigation carries only the service ID and re-enters current detail visibility authority.

## Detail Lifecycle Findings

- Deep-link details require current active service, tenant, profile, and provider governance authority.
- Cached details and reviews are withheld during validation and removed from presentation after authority failure.

## Request-Entry Lifecycle Findings

- Request entry derives from current same-tenant service, profile, tenant, provider governance, actor, and active-request authority.
- Visibility loss removes request capability. Request creation independently rechecks lifecycle authority before SQL mutation.

## Cache Findings

- Marketplace React Query keys remain tenant/user scoped and prior actor scopes are removed before replacement-route presentation.
- Cache is withheld during current-authority validation and cannot become lifecycle truth.

## Realtime Findings

- Service activation and deactivation mutations now publish cross-tab invalidation triggers.
- BroadcastChannel, focus, visibility, reconnect, and periodic triggers only invalidate/refetch; they never publish service DTO or lifecycle truth.

## History Findings

- Browser history, bookmarks, refresh, and direct URLs re-enter current backend discovery/detail authority.
- Route state carries no service snapshot capable of resurrecting a hidden or deleted listing.

## Tenant Isolation Findings

- Frontend cache identity includes authenticated tenant and user scope.
- Backend request entry requires trusted same-tenant lineage; global discovery still validates each service against its owning current SQL tenant authority.

## Failure-Handling Findings

- Discovery failures return non-success and clear current presentation.
- Detail, review, profile, and request-entry authority failures fail closed; retries can only recover through a fresh backend authority response.

## Adversarial Findings

- Visibility replay, lifecycle replay, stale Elasticsearch documents, drift repair, reindex, cache replay, history replay, deep links, actor switches, tenant switches, and cross-tab mutation races were reviewed.
- Current SQL lifecycle recomposition blocks inactive, hidden, deleted, suspended, orphaned, stale, and cross-tenant service resurrection on inspected paths.

## Runtime Hardening Performed

- Added authoritative `TenantStatus.Active` checks to discovery, detail, projections, drift repair, reindex, diagnostics, and request-entry paths.
- Added suspended/banned Vendor governance suppression to the same lifecycle boundaries.
- Added cross-tab lifecycle invalidation for service activation and deactivation without optimistic cache writes.

## Exact Runtime Files Modified

- `PointOfSale.Infrastructure/Services/MarketplaceSearchService.cs`
- `PointOfSale.Infrastructure/Services/ServiceCatalogService.cs`
- `PointOfSale.Infrastructure/Services/ServiceRequestService.cs`
- `PointOfSale.Infrastructure/Consumers/ServiceDiscoveryProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Jobs/ElasticsearchDriftReconciliationJob.cs`
- `PointOfSale.API/Controllers/MarketplaceController.cs`
- `PointOfSale.Web/src/marketplace/hooks/useActivateService.ts`
- `PointOfSale.Web/src/marketplace/hooks/useDeactivateService.ts`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendVisibilityLifecycleAuthorityCertificationTests.cs`

## Architecture Updates

- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers

- Who owns listing visibility authority? Current PostgreSQL service, tenant, marketplace profile, and provider-governance authority.
- Can hidden services remain visible? No on inspected current-authority paths.
- Can deleted services be resurrected? No; reads, projection replay, drift repair, and reindex reject deleted lifecycle state.
- Can provider suspension preserve visibility? No; suspended or banned Vendor governance suppresses visibility.
- Can tenant suspension preserve visibility? No; `TenantStatus.Active` is required.
- Can request entry survive visibility loss? No.
- Can deep links bypass lifecycle authority? No.
- Can stale cache preserve lifecycle truth? No; cache is scoped, withheld during validation, and derivative.
- Can tenant leakage occur? No inspected cache, route, request-entry, or backend lifecycle path crosses tenant authority.
- Were runtime code changes required? Yes.
- Is Service Marketplace Frontend Listing Visibility & Lifecycle Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk

- Backend lifecycle changes outside the current browser converge through focus, visibility, reconnect, or the 30-second authority interval unless a local lifecycle mutation broadcasts immediately.
- Production should monitor projection drift, search suppression counts, lifecycle-driven 404/503 rates, and browser convergence latency.

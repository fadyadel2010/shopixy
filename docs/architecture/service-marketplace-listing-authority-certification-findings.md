# PASS S1-A Service Marketplace Listing Authority

Service Marketplace Listing Authority is production-certifiable for inspected runtime paths after search read-boundary, service-detail visibility, and DTO listing-intent hardening.

## Identity findings

- PostgreSQL `Services` owns service listing identity. Service IDs are minted server-side in `Service.Create(...)`; DTOs, routes, Elasticsearch documents, events, and frontend state cannot mint service identity.
- Service identity is bounded by current SQL service row existence, non-deletion, active status, tenant lineage, and same-tenant active provider profile authority.

## Ownership findings

- PostgreSQL `MarketplaceProfiles` owns provider ownership. Service mutation requires the authenticated user to own the current SQL provider profile.
- Public listing visibility now requires the provider profile to be active, non-deleted, and same-tenant as the service row.

## Publication findings

- Current SQL `Services.IsActive`, `Services.IsDeleted`, active tenant lineage, and active provider profile lineage decide public service visibility.
- Public search and direct service detail now fail closed when SQL does not prove current visibility.

## Pricing findings

- `Services.StartingPrice`, `PricingType`, and `Currency` are SQL listing authority. Search responses now return pricing fields from current SQL, not Elasticsearch documents.
- Runtime mutation rejects negative prices, requires pricing type and currency when a starting price is present, and normalizes currency to a three-letter uppercase code.

## Category findings

- Service categories are currently SQL-owned free-form classification strings on `Services.Category`, not a separate taxonomy authority.
- Runtime mutation trims and bounds category input before SQL persistence; search responses return category from current SQL rather than stale search documents.

## Media findings

- `Services.ServiceImages` is SQL listing media authority for the current implementation.
- Runtime mutation now requires service media to be a JSON array of safe tenant-owned storage-key references under `marketplace/services/`; absolute URLs, traversal references, malformed JSON, and non-array payloads are rejected before SQL mutation.

## Discovery findings

- Elasticsearch `marketplace_services` is candidate discovery evidence only. It may find and rank candidate IDs, but cannot decide returned listing identity, ownership, visibility, pricing, category, or media.
- `MarketplaceSearchService` now reloads current SQL service, tenant, and profile authority for every returned hit and suppresses stale or forged documents.

## Projection findings

- `ServiceDiscoveryProjectionConsumer` and `ElasticsearchDriftReconciliationJob` already recompose search documents from current SQL service/tenant/profile authority and delete stale documents when SQL rejects visibility.
- Projection trust/stat overlays remain derivative ranking/display data and do not own listing authority.

## Isolation findings

- Service listing writes are tenant-context scoped. Public discovery is intentionally global marketplace visibility, but the response does not accept route or DTO tenant scope as authority.
- Tenant-admin repair remains tenant-scoped; global search index recreation remains Super Admin scoped.

## Concurrency findings

- Listing mutation commits service row and outbox event atomically through EF/Bus outbox usage. Replayed projection events reload SQL authority before search mutation.
- Search read-time SQL revalidation prevents stale Elasticsearch snapshots from overwriting newer SQL listing truth in customer-visible responses.

## Historical findings

- Historical service events are triggers only. Stale create/update/activation replay can cause recomposition, but current SQL decides upsert versus delete/no visibility.
- Requests, chat, completion, ratings, analytics, and trust metrics remain separate authorities and cannot create service listing identity or publication.

## Adversarial findings

- Service forgery through Elasticsearch, replayed events, DTO service IDs, route tampering, stale projections, and frontend state is blocked on inspected search/detail paths by current SQL revalidation.
- Ownership bypass is blocked on inspected mutation paths by authenticated profile ownership plus current SQL provider-profile lineage.
- Price, category, and media tampering through DTOs is bounded by server-side validation before SQL mutation and SQL-derived response mapping after indexing.

## Runtime Hardening Performed

- Changed service marketplace search reads to treat Elasticsearch hits as candidates and reload current SQL service/tenant/profile authority before returning listing fields.
- Changed public service detail lookup to require current SQL active service, active tenant, active non-deleted same-tenant provider profile authority.
- Added service listing mutation validation for title, description, category, negative price, pricing type, currency, media JSON, and safe service-media storage keys.
- Updated the marketplace search interface authority comment so future code does not treat Elasticsearch as final service listing authority.

## Exact Runtime Files Modified

- `PointOfSale.Infrastructure/Services/MarketplaceSearchService.cs`
- `PointOfSale.Infrastructure/Services/ServiceCatalogService.cs`
- `PointOfSale.Core/Interfaces/IMarketplaceSearchService.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/ServiceMarketplaceListingAuthorityCertificationTests.cs`

## Architecture Updates

- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers

- Who owns service listing authority? PostgreSQL `Services` plus active tenant and same-tenant active `MarketplaceProfiles` own service listing authority.
- Can services be forged? No on inspected paths; search/detail responses require current SQL authority.
- Can ownership be bypassed? No on inspected mutation paths; authenticated users must own the SQL provider profile.
- Can unpublished services appear? No on inspected search/detail paths; inactive/deleted services fail closed.
- Can prices be manipulated? Not as returned listing authority; runtime now validates DTO price intent and returns SQL pricing.
- Can hidden services appear? No on inspected search/detail paths after SQL revalidation.
- Can tenant leakage occur? No tenant-scoped mutation or admin repair leakage was found; global public discovery is intentional and SQL-filtered.
- Were runtime code changes required? Yes.
- Is Service Marketplace Listing Authority production-certifiable? Yes, for inspected runtime paths after the hardening above.

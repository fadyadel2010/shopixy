# PASS S1-F Service Marketplace Analytics Authority

Service Marketplace Analytics Authority is production-certifiable for the inspected runtime after provider KPI tenant/profile hardening and analytics-boundary freezes.

## Performance findings

Provider performance analytics is derivative read authority. Response, acceptance, and completion metrics must derive from SQL `ServiceRequests` under trusted tenant, buyer, provider, provider-profile, and lifecycle predicates. The inspected runtime currently exposes provider profile statistics through `GET /api/marketplace/me/stats`; completed request counts now require current tenant scope, provider user ownership, provider profile lineage, and final `CompletedByBuyer` SQL completion.

Stored aggregates, Elasticsearch `ServiceStats`, Redis, MongoDB, frontend cards, DTOs, and replayed events do not own response, acceptance, or completion truth.

## Revenue findings

Service marketplace analytics owns no revenue authority. No inspected service-marketplace revenue dashboard, earnings report, CSV export, or Excel export endpoint exists. Marketing review evidence may display immutable POS-verified order/revenue evidence attached to reviews, but it is trust/evidence metadata only and cannot create payments, orders, ledger rows, payouts, settlement, provider earnings, or financial reporting truth.

Future service marketplace revenue analytics must derive from certified SQL financial/order/ledger authority, or from SQL-built aggregates that are complete, fresh, valid, and backed by live SQL fallback.

## Trust findings

Trust analytics derives from SQL `ServiceReviews` joined to final `CompletedByBuyer` `ServiceRequests`. `ProviderTrustService` recomposes averages, totals, distributions, badge inputs, and marketing trust values from SQL review/request/evidence truth at read time. `ProviderTrustAggregate`, MongoDB trust snapshots, Elasticsearch trust fields, and search ranking documents remain derivative.

## Funnel findings

The service request funnel is owned by SQL lifecycle rows: service listing visibility comes from S1-A, request creation/acceptance/rejection/cancellation from S1-B, chat engagement from S1-C, final completion from S1-D, and rating from S1-E. Analytics may count funnel stages only from those SQL authorities. Search views and behavioral telemetry are approximate signals and cannot create requests, accepted state, completion, ratings, or revenue.

## Dashboard findings

Dashboard cards are derivative. `me/stats` recomputes service count and completed request count from SQL, then reads SQL-composed trust. It does not trust cached KPI composites, route tenant IDs, client values, Elasticsearch service stats, or stored provider trust aggregates as final authority.

## Export findings

No inspected service marketplace analytics export route exists in `MarketplaceController`. Future CSV, Excel, report, or BI export surfaces must read through trusted tenant or Super Admin scope and SQL authority readers, with bounded filters and no cache/projection final authority.

## Projection findings

Elasticsearch service documents may retain `ServiceStats` and trust-shaped fields for projection compatibility, but inspected runtime returns zero service stats in reindex documents and recomposes listing/trust values from SQL before public return. Projection replay, Redis, MongoDB, Elasticsearch, and cached analytics rows cannot inflate, suppress, resurrect, or leak analytics truth.

## Isolation findings

Provider KPI hardening explicitly binds stats to `GetTenantId()`, `Service.TenantId`, `ServiceRequest.TenantId`, authenticated provider user id, and `ProviderProfileId`. Buyer/provider request reads in `ServiceRequestService` are tenant-scoped. Service review and trust reads require review/request tenant and lineage equality.

## Historical findings

Historical analytics must preserve immutable SQL request snapshots and final lifecycle truth. Service listing edits cannot rewrite request snapshots; provider-only completion cannot count as final completion; hidden or stale review/projection rows cannot rewrite trust history.

## Adversarial findings

Analytics inflation, analytics suppression, projection poisoning, replay attacks, dashboard tampering, export tampering, trust inflation, revenue inflation, tenant guessing, DTO tampering, and route tampering cannot become service marketplace analytics authority on inspected runtime paths because metrics are recomposed from current SQL business authorities or absent as runtime capabilities.

## Runtime Hardening Performed

- Hardened provider profile statistics so service counts and completed request counts require trusted tenant scope.
- Hardened completed-request statistics so final provider performance requires matching SQL `ProviderProfileId` and `CompletedByBuyer` request state.
- Corrected the marketplace search endpoint comment to record SQL-recomposed listing authority instead of Elasticsearch-only authority.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/MarketplaceController.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/ServiceMarketplaceAnalyticsAuthorityCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Answers

- Who owns analytics authority? Analytics owns derivative calculations, KPIs, dashboards, reports, exports, trends, and presentation only; SQL service listing, request, chat, completion, review/trust, and financial authorities own business truth.
- Can analytics inflate business truth? No. Inspected analytics paths cannot create listings, requests, completion, ratings, revenue, payouts, or ledger truth.
- Can analytics suppress business truth? No. Inspected KPI and trust reads recompute from SQL truth instead of trusting stale suppressible projections.
- Can exports bypass authority? No inspected service marketplace analytics exports exist. Future exports must use SQL authority readers.
- Can tenant leakage occur? No inspected runtime blocker remains after provider KPI tenant/profile hardening; metrics are tied to trusted tenant scope.
- Were runtime code changes required? Yes. `MarketplaceController.GetMyProfileStats` required tenant/profile hardening and a stale authority comment was corrected.
- Is Service Marketplace Analytics Authority production-certifiable? Yes, for the inspected runtime scope.

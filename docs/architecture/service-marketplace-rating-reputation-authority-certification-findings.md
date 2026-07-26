# PASS S1-E Service Rating & Reputation Authority

Service Rating & Reputation Authority is production-certifiable for inspected runtime paths after review immutability, self-review blocking, SQL-composed trust reads, service-review visibility, search trust display, and rating check-constraint hardening.

## Review findings
- PostgreSQL `ServiceReviews` owns service review identity, content, rating values, reviewer ownership, provider ownership, and review history.
- Review creation derives service, provider, reviewer, tenant, and eligibility from the SQL `ServiceRequests` row. DTOs provide rating/comment intent only.

## Reviewer findings
- Reviewer authority is `ServiceRequests.BuyerUserId`; provider authority is `ServiceRequests.ProviderUserId`.
- Runtime blocks provider self-review and rejects any review whose caller is not the SQL buyer for the final completed request.

## Uniqueness findings
- One review per service engagement is enforced by the unfiltered SQL unique index `UQ_ServiceReviews_ServiceRequest`.
- Deleted or hidden review replay cannot create a second review for the same request because uniqueness is not scoped to live rows only.

## Rating findings
- Rating values are bounded in the entity factory and by `CK_ServiceReviews_Rating_Range`.
- Review reads and reputation recomposition count only SQL reviews that still match a final `CompletedByBuyer` request lineage.

## Content findings
- Service review content is immutable after creation. `MarketplaceInvariantInterceptor` rejects modified or deleted `ServiceReview` rows.
- Marketing review evidence is also immutable after creation; POS-derived evidence metrics remain server-computed.

## Reputation findings
- Runtime reputation authority now recomposes from SQL `ServiceReviews` joined to final-completed SQL `ServiceRequests`.
- `ProviderTrustAggregate`, Mongo trust snapshots, Elasticsearch trust fields, and search documents are derivative only and no longer own public/profile trust responses.

## Visibility findings
- Public service review reads first prove current SQL service listing visibility, then return only review rows whose request is final `CompletedByBuyer` and whose reviewer/provider lineage still matches the request.
- The inspected runtime has no provider-owned moderation path for service reviews. Hidden/deleted service reviews cannot remain visible or affect reputation because modification/deletion is rejected and read authority filters final SQL review truth.

## Projection findings
- Elasticsearch may still carry derivative trust fields for ranking hints, but returned search DTO rating/trust fields are recomposed from SQL trust authority.
- Reindex routes no longer hydrate service search trust fields from stored `ProviderTrustAggregate` rows.

## Isolation findings
- Review creation is tenant-scoped to trusted `ITenantContext.TenantId` and same-tenant SQL request authority.
- Review and trust reads use explicit SQL lineage predicates under `IgnoreQueryFilters`; foreign review rows cannot satisfy request/reviewer/provider/tenant equality.

## Concurrency findings
- Review creation relies on SQL unique review identity per `ServiceRequestId`; replay or concurrent creation converges to one review.
- Reputation reads recompute from committed SQL review truth, so stale aggregate/projection update races cannot inflate or suppress returned trust.

## Historical findings
- Review history is append-only for inspected service marketplace runtime. Review text, rating, reviewer ownership, provider ownership, and marketing evidence do not mutate after creation.
- Completed request snapshots remain S1-B authority; final completion remains S1-D authority; ratings consume those facts but do not rewrite them.

## Adversarial findings
- Fake reviews, duplicate reviews, self reviews, review replay, rating inflation, rating suppression, reputation manipulation, projection poisoning, tenant guessing, DTO tampering, and route tampering are blocked or fail closed on inspected paths by SQL request/review authority, immutable review rows, rating bounds, unique request review identity, and SQL-composed trust reads.

## Runtime Hardening Performed
- Blocked provider self-review in `ServiceReviewService`.
- Required all service review reads to rejoin final-completed SQL request authority and reviewer/provider lineage.
- Made `ServiceReview` and `MarketingReviewEvidence` immutable after creation at the EF interceptor boundary.
- Added SQL rating check-constraint migration.
- Changed `ProviderTrustService` to recompute trust from SQL review/request truth instead of stored aggregate rows.
- Changed provider trust event consumption and drift repair to re-read final-completed SQL review/request authority before mutating derivative aggregates.
- Changed marketplace service detail/search/reindex trust displays to use SQL-composed trust authority.
- Changed public service reviews to prove current SQL service visibility before returning review history.

## Exact Runtime Files Modified
- `PointOfSale.Core/Entities/ProviderTrustAggregate.cs`
- `PointOfSale.Infrastructure/Services/ProviderTrustService.cs`
- `PointOfSale.Infrastructure/Consumers/ProviderTrustConsumer.cs`
- `PointOfSale.Infrastructure/Jobs/TrustDriftReconciliationJob.cs`
- `PointOfSale.Infrastructure/Services/ServiceReviewService.cs`
- `PointOfSale.Infrastructure/Data/MarketplaceInvariantInterceptor.cs`
- `PointOfSale.Infrastructure/Data/Configurations/ServiceReviewConfiguration.cs`
- `PointOfSale.Infrastructure/Migrations/20260611203933_ServiceMarketplaceRatingAuthorityHardening.cs`
- `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- `PointOfSale.Infrastructure/Services/MarketplaceSearchService.cs`
- `PointOfSale.API/Controllers/MarketplaceController.cs`

## Regression Freezes Added
- `PointOfSale.Tests/Architecture/ServiceMarketplaceRatingReputationAuthorityCertificationTests.cs`

## Architecture Updates
- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers
- Who owns rating authority? PostgreSQL `ServiceReviews` joined to final `CompletedByBuyer` SQL `ServiceRequests` owns rating authority; reputation is recomposed from that SQL truth.
- Can reviews be forged? No on inspected paths; creation requires trusted tenant scope, SQL buyer ownership, final completion, and SQL request lineage.
- Can duplicate reviews occur? No on inspected paths; `UQ_ServiceReviews_ServiceRequest` is unfiltered and replay/concurrency collapses at SQL uniqueness.
- Can reputation be manipulated? No on inspected public/profile paths after hardening; stored aggregates, projections, search documents, DTOs, and frontend state are derivative.
- Can hidden reviews affect reputation? No inspected hidden/moderation authority exists for service reviews; deleted/mutated review rows are blocked and reputation reads count only live SQL review/request truth.
- Can tenant leakage occur? No inspected review/trust paths expose or count foreign rows without matching SQL request/review/provider/reviewer lineage.
- Were runtime code changes required? Yes.
- Is Service Rating & Reputation Authority production-certifiable? Yes, for inspected runtime paths after the hardening above.

## Residual Operational Risk
- Production still needs review/trust SQL/projection parity dashboards, WORM/SIEM review evidence retention, query-plan review for large provider trust reads, backup/restore rehearsal, and PostgreSQL-backed high-concurrency chaos runs for duplicate review, replay, and trust recomposition races.

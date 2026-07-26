# PASS S1-B Service Marketplace Request & Engagement Authority

Service Marketplace Request & Engagement Authority is production-certifiable for inspected runtime paths after request creation, snapshot, lifecycle, tenant isolation, duplicate-request, and row-lock hardening.

## Identity findings
- SQL `ServiceRequests` owns request identity. Request creation now requires current same-tenant active SQL service, active provider profile, active tenant lineage, and a serializable SQL transaction.
- Duplicate active requests for the same tenant/service/buyer are blocked by preflight and `UX_ServiceRequests_Active_Service_Buyer` for `Pending`, `Accepted`, and `CompletedByProvider` rows.

## Ownership findings
- Requester ownership is `ServiceRequests.BuyerUserId`; provider ownership is loaded from current SQL `Services.Profile.UserId` at creation and persisted as immutable `ProviderUserId` plus `ProviderProfileId`.
- Lifecycle reads and mutations require trusted current tenant scope plus buyer/provider ownership checks. Route IDs and DTOs cannot reassign ownership.

## Snapshot findings
- Requests persist immutable service snapshots: title, category, starting price, pricing type, currency, packages, provider user, and provider profile.
- Request list/detail responses now display historical request snapshot fields, so later service listing edits cannot rewrite old request economics or category/title history.

## Acceptance findings
- Acceptance is a SQL transition from `Pending` to `Accepted`, provider-only, row-locked on PostgreSQL, and validated by the entity state machine plus interceptor transition guard.
- Rejected, cancelled, completed, or already accepted requests cannot be accepted again.

## Cancellation findings
- Cancellation remains buyer/provider-only and valid only from `Pending` or `Accepted`.
- Completed, rejected, or already cancelled requests cannot be cancelled or reopened through inspected runtime paths.

## Engagement findings
- Engagement/chat enablement derives from accepted SQL request state. No separate engagement entity was found on inspected paths; the accepted `ServiceRequest` is the engagement authority for chat enablement.
- Engagement cannot be forged without an accepted SQL request, and the active-request uniqueness boundary prevents multiple active engagements for the same buyer/service lane.

## Projection findings
- No Redis, MongoDB, Elasticsearch, frontend state, or projection path was found owning request lifecycle authority. Projections and UI capabilities are derivative of SQL request status and snapshots.

## Isolation findings
- Request creation and request reads/mutations are tenant-scoped to trusted `ITenantContext.TenantId`; `IgnoreQueryFilters` is retained only with explicit `TenantId` predicates for marketplace cross-query correctness.
- Cross-tenant service IDs now fail closed at request creation because current SQL service/profile/tenant authority must match the trusted tenant.

## Concurrency findings
- Creation uses serializable SQL and a live unique active-request index. Lifecycle mutations load the request under serializable transaction and PostgreSQL `FOR UPDATE` where available.
- Concurrent accept/reject/cancel/complete attempts converge through row locks and transition validation instead of trusting stale frontend capabilities.

## Historical findings
- Historical request snapshots are immutable by entity private setters and `MarketplaceInvariantInterceptor` immutable-property guards.
- Existing rows are migration-backfilled from SQL services/profiles; duplicate active historical rows must be resolved before the unique index can be created.

## Adversarial findings
- Request forgery, DTO tampering, route tampering, cross-tenant service IDs, acceptance spoofing, cancellation spoofing, duplicate active requests, stale service edits, and replayed lifecycle calls are blocked or collapse through SQL authority on inspected paths.
- Chat messages, completion confirmation, ratings, and analytics remain separate authorities and do not own request creation or acceptance.

## Runtime Hardening Performed
- Hardened `ServiceRequestService.RequestServiceAsync` to require same-tenant active SQL service/profile/tenant authority and serializable creation.
- Added immutable request service snapshots and provider-profile lineage to `ServiceRequest`.
- Added provider rejection lifecycle support.
- Added row-locked, tenant-scoped lifecycle mutation loading.
- Added duplicate active request preflight plus SQL unique index migration.
- Added interceptor guards for immutable request authority fields and valid status transitions.
- Updated request API responses to serve historical snapshots.

## Exact Runtime Files Modified
- `PointOfSale.Core/Entities/ServiceRequest.cs`
- `PointOfSale.Core/Enums/ServiceRequestStatus.cs`
- `PointOfSale.Core/Interfaces/IServiceRequestService.cs`
- `PointOfSale.Infrastructure/Services/ServiceRequestService.cs`
- `PointOfSale.Infrastructure/Data/MarketplaceInvariantInterceptor.cs`
- `PointOfSale.Infrastructure/Data/Configurations/ServiceRequestConfiguration.cs`
- `PointOfSale.Infrastructure/Migrations/20260611120000_ServiceMarketplaceRequestAuthorityHardening.cs`
- `PointOfSale.API/Controllers/MarketplaceController.cs`

## Regression Freezes Added
- `PointOfSale.Tests/Architecture/ServiceMarketplaceRequestAuthorityCertificationTests.cs`
- Updated marketplace/chat tests for the active-tenant request authority gate.

## Architecture Updates
- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers
- Who owns request authority? PostgreSQL `ServiceRequests` plus current SQL service/profile/tenant authority at creation and SQL row-locked lifecycle transitions.
- Can requests be forged? No, inspected runtime creation derives provider, tenant, service lineage, and snapshots from SQL authority.
- Can ownership be bypassed? No, inspected lifecycle/read paths require trusted tenant plus SQL buyer/provider ownership.
- Can service snapshots drift? No, request snapshots are persisted and guarded immutable after creation.
- Can acceptance be spoofed? No, acceptance is provider-only, SQL row-locked, and valid only from `Pending`.
- Can engagements be forged? No, engagement/chat enablement derives from accepted SQL request state; no projection/frontend engagement authority exists on inspected paths.
- Can tenant leakage occur? No inspected request API path exposes or mutates foreign-tenant requests after hardening.
- Were runtime code changes required? Yes.
- Is Service Marketplace Request Authority production-certifiable? Yes, for inspected runtime paths after the hardening above.

## Residual Operational Risk
- Production still needs request/engagement SQL audit dashboards, WORM/SIEM lifecycle evidence retention, PostgreSQL-backed high-concurrency chaos runs for request/create/accept/reject/cancel races, and operational duplicate-preflight run evidence before applying the unique index.

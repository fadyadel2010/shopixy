# 2026-07-22 Storefront / Builder Runtime Certification Registration

- PASS SF-ADM2.3 Builder Section Composition Authority Hardening.
- PASS SF-ADM2.4 Builder Layout Authority Hardening.
- PASS SF-ADM2.5 Builder Theme Authority Certification.
- PASS SF-ADM2.6 Builder Section Content Authority Hardening.
- PASS SF-ADM2.7 Builder Publish Authority Certification.
- PASS SF-ADM2.8 Builder Preview Authority Certification.
- PASS SF-ADM2.9 Builder Version History & Rollback Authority Certification.
- PASS SF-ADM2.FINAL Store Builder End-to-End Authority Certification.
- PASS SF-CMS2 CMS Page Identity Authority Hardening.
- PASS SF-CMS3 CMS Content Authority Hardening.
- PASS SF-CMS4 CMS SEO Authority Hardening.
- PASS SF-CMS5 CMS Publish Authority Hardening.
- PASS SF-CMS6 CMS Preview Authority Hardening.
- PASS SF-CMS7 CMS Version History & Rollback Authority Hardening.
- PASS SF-CMS.FINAL CMS End-to-End Authority Certification.
- P2-GD Storefront Navigation Authority remains production-certifiable SQL authority over live navigation reads and POS Admin navigation writes.

# 2026-07-17 PAYMENT-END-TO-END-1 Unified Payment Execution Boundary

- Certified the cross-system handoff from pure payment workflow output to canonical Payment Intent execution, Order aggregate mutation, SQL persistence, and existing Order Domain event propagation.
- The execution layer crosses checkout channels only through `IPaymentWorkflowIntent`; it does not introduce Storefront, Affiliate, CommerceHub, UI, provider, RabbitMQ, SignalR, notification, or frontend dependencies.
- Existing `IEventPublisher` / MassTransit EF outbox flow remains the propagation boundary for POS Admin, Storefront customer visibility, Affiliate synchronization, Marketplace/CommerceHub synchronization, timelines, and future channels.
- Replay correctness is SQL-state collapse: already-applied intent status transitions return before persistence and before event publication; unsafe terminal or money-recognized transitions still fail closed.

# 2026-07-21 PASS T8 Treasury Analytics Authority

- Treasury analytics owns derivative financial visibility only. Treasury analytics may calculate KPIs, reports, summaries, forecasts, and exports from tenant-scoped SQL ledger and CommerceHub financial transaction authority, but it cannot post journals, recognize cash, settle payouts, or override SQL financial truth.

# 2026-07-21 PASS T6 Treasury Reconciliation Authority

- Treasury reconciliation can detect provider, bank, refund, payout, settlement, and ledger drift, then quarantine or re-enter certified authorities. Treasury reconciliation may detect inconsistencies but cannot create money, complete money movement, or treat external evidence alone as internal SQL authority.

# 2026-07-21 PASS C7-G Settlement & Payout Final Currency Certification

- CommerceHub settlement and payout final currency authority is EGP-only and CommerceHub-owned. Settlement, payout, dashboard, queue, projection, and reconciliation paths must not derive final platform-money currency from tenant/storefront currency labels or frontend/projection payloads.

# 2026-07-10 PAYMENT-1.2 Payment Policy Evaluation Pipeline Boundary

- Payment Policy Evaluation Pipeline crosses Payment, Checkout, Order, Shipping, Warehouse, Marketplace, Affiliate, Tenant, Merchant, Customer, Risk, Fraud, Compliance, Settlement, Treasury, Accounting, Provider Capability, Provider Constraint, Evidence, Policy Governance, Audit, and Replay boundaries only through immutable `PolicyContext` facts and versioned policy/rule contracts.
- The Pipeline may coordinate context assembly but does not own or repair facts. Every fact must prove current backend authority, immutable snapshot, or admitted evidence lineage before rule evaluation; non-authoritative frontend/cache/projection/provider-payload sources fail closed.
- `PipelineContext` may carry candidate providers, intermediate results, decision candidates, warnings, failures, traces, and audit buffers, but those values are evaluation-local and cannot become runtime state, provider authority, fact authority, or governance authority.
- Cross-system conflict resolution is deterministic and fail-closed. Compliance/fraud/eligibility denies, quarantine, recovery, manual review, retry, alternative provider/method, required evidence, and allow outcomes resolve by certified precedence; unresolved conflicts cannot silently allow execution.
- Audit failure, replay failure, missing versions, missing mandatory policy/rule, unknown capabilities, and unknown constraints block executable allow decisions until the relevant authority, governance, or replay artifact is restored.

# 2026-07-10 PAYMENT-1.1 Payment Policy Framework Boundary

- Payment Policy Framework crosses Payment, Checkout, Order, Shipping, Warehouse, Marketplace, Affiliate, Tenant, Merchant, Customer, Risk, Fraud, Compliance, Settlement, Treasury, Accounting, Provider Capability, Provider Constraint, Evidence, and Governance boundaries through canonical facts only.
- Cross-system facts may enter policy only from current backend authority, immutable snapshots, or admitted evidence. Frontend state, browser storage, route data, SDK results, redirects, raw provider payload replay, cache, Redis, MongoDB, Elasticsearch, projections, dashboards, and analytics summaries are forbidden as policy authority.
- Rules consume immutable policy context snapshots and never fetch, own, mutate, or repair facts. Policies compose versioned rules; Governance publishes immutable versions; Runtime enforces policy decisions; Adapters expose capabilities/constraints/evidence only.
- Replay requires exact policy, rule, catalog, fact schema, evaluation, audit, replay versions, and the original normalized input snapshot or materialization. Missing replay artifacts fail closed.

# Cross-System Authority Hardening

## 2026-06-01 PASS 16.1B Customer Identity Runtime Completion

- Added CommerceHub password recovery, registration verification issuance, verification resend and consumption, profile update, password change, email change, and customer-facing revoke-all settings UX.
- Routed identity links through `IEmailSender` and configurable `CommerceHub:CustomerIdentity:PublicBaseUrl`; raw tokens are hashed at rest and are not returned through customer auth APIs.
- Reset and verification replay now collapse through conditional SQL consumption updates.
- Added `20260601213000_CommerceHubCustomerIdentityLifecycle` for customer verification state, reset and verification token stores, identity evidence, indexes, foreign keys, and PostgreSQL append-only trigger enforcement.
- Launch approval remains blocked on deployment application and verification, SMTP proof, evidence-role inventory, refresh-token rotation, monitoring, alerts, and PostgreSQL-backed chaos tests.

## 2026-06-01 PASS 16.2 Product Discovery Certification

- Certified the inspected CommerceHub discovery journey across homepage, featured/promoted shelves, search, autocomplete, categories, filters, product cards, product details, recommendation-like surfaces, inventory/pricing display, direct links, frontend UX, auditability, chaos, and monitoring.
- Verified that CommerceHub search/list/detail/suggestion/autocomplete responses are subordinate to `CommerceHubSearchSqlAuthorityOverlay`, homepage reads are filtered through public SQL authority, category/filter data requires live SQL lineage/schema, and reindex removes stale Elasticsearch documents missing from SQL-visible authority.
- No runtime code hardening was required because prior PASS 10.x through PASS 16.1B work already supplied the required SQL-first discovery controls.
- Added regression freezes: `CommerceHubProductDiscoveryCertificationTests`, `CommerceHubSearchVisibilityCertificationTests`, and `CommerceHubDiscoveryAuthorityCertificationTests`. Findings: `docs/architecture/commercehub-product-discovery-certification-findings.md`.

## 2026-06-01 PASS 16.4 Checkout Certification

- Certified the inspected CommerceHub checkout journey across checkout session, pricing, inventory reservation, shipping, address snapshots, order creation, payment preparation, idempotency, concurrency, recovery, frontend UX, financial controls, auditability, chaos, and monitoring.
- Verified that checkout remains SQL-authoritative through authenticated customer authority, customer/idempotency locks, request-hash idempotency, saved-cart row lock/version match, serializable transaction, current listing/product/variant/tenant/payment/shipping/promo/price/inventory recomposition, vendor/readiness gates, and reservation-before-commit order creation.
- No runtime code hardening was required because the inspected runtime already contained the needed controls from prior pricing, inventory, finance, emergency, identity, discovery, and cart passes.
- Added regression freezes: `CommerceHubCheckoutCertificationTests`, `CommerceHubCheckoutFinancialCertificationTests`, and `CommerceHubCheckoutReservationCertificationTests`. Findings: `docs/architecture/commercehub-checkout-certification-findings.md`.

## 2026-06-01 PASS 16.5A Order & Fulfillment Lifecycle Certification

- Certified the inspected CommerceHub order lifecycle across creation handoff, ownership, customer/merchant/Super Admin visibility, POS Admin fulfillment, transition graph, cancellation, delivery, return handling, refund triggers, projections, dashboards, frontend alignment, replay/recovery, auditability, chaos, and monitoring.
- Verified that CommerceHub fulfillment authority remains SQL-first and tenant-scoped: POS Admin mutation requires current tenant context, serializable locks, `StoreId` ownership, transition validation, payment readiness, inventory side effects, status history, and projection event publication.
- Verified that direct CommerceHub marketplace order/sub-order/item lifecycle writes are blocked by `ApplicationDbContext` unless an explicit `CommerceHubBoundaryMutationScope.BeginOrderLifecycle()` is active.
- No runtime code hardening was required because inspected order, return, refund, projection, and guard paths already enforce the required lifecycle authority boundaries.
- Added regression freezes: `CommerceHubOrderLifecycleCertificationTests`, `CommerceHubFulfillmentAuthorityCertificationTests`, and `CommerceHubOrderTransitionCertificationTests`. Findings: `docs/architecture/commercehub-order-fulfillment-lifecycle-certification-findings.md`.

## 2026-06-01 PASS 16.1 Customer Identity Certification

- CommerceHub customer identity remains **NOT READY** for public production launch.
- Hardened registration now wraps `CommerceHubCustomer` and customer governance-profile creation in one SQL transaction and maps concurrent PostgreSQL email uniqueness races to `EMAIL_EXISTS`.
- Customer login and JWT validation now reject suspended accounts as well as banned accounts.
- Frontend guest-to-account cart transition now preserves local guest cart contents until authenticated cart bootstrap merges and persists them.
- Remaining launch blockers are password recovery, email verification/resend, profile mutation, password/email change, customer-facing revoke-all UX, append-only SQL identity lifecycle evidence, identity-specific monitoring, and missing chaos coverage for flows that do not yet exist.
- Regression freezes: `CustomerIdentityCertificationTests`, `CustomerSessionLifecycleCertificationTests`, and `CustomerRecoveryCertificationTests`. Findings: `docs/architecture/customer-identity-certification-findings.md`.

Last updated: 2026-05-27

## Purpose

This file captures the production hardening decisions from the system-of-systems adversarial audit. It exists so a new Codex chat can continue from `.ai-context` without relying on spec-kit or `.kiro` files.

## 2026-05-12 Full-Stack Audit Result

- The broader frontend/backend adversarial revalidation on 2026-05-11 validated one new likely-risk gap and the current worktree closed it: CommerceHub unmatched provider webhooks no longer depend on provider retry when payment-initiation lock contention prevents the serialized authority path from running.
- The 2026-05-12 completion validated and closed one additional likely-risk gap: critical storefront and CommerceHub Mongo order reads no longer trust arbitrary duplicate projection documents when index creation failed open or historical races left more than one document for the same authoritative order.
- The 2026-05-12 revalidation confirmed that the live worktree still matches the current hardening model in the highest-risk areas: SQL-first authority on critical order reads, serializable storefront checkout authority refresh, backend-derived CommerceHub payment recovery/retry guidance, fail-closed customer/session cutoff enforcement, and currency-scoped Super Admin payout visibility.
- The 2026-05-12 follow-up revalidated the broader architecture guardrail suite after aligning stale source assertions with the current SQL-order, fail-closed promotion, and EGP-only vendor-repayment authority boundaries; no new evidence-backed active risk was found.
- Remaining customer-visible lag and operator blocking in the already-hardened areas are intentional fail-closed or reconciliation-first tradeoffs, not newly validated authority breaks. Any future regression should therefore be treated as a violation of the rules captured below rather than as an unclassified new architecture model.

## Fixed Authority Gaps

### PRODUCT-CON-03 category hierarchy and lineage authority

Problem:
- Category projection, rebuild, storefront, affiliate, CommerceHub, and search paths could treat stale parent references, Mongo hierarchy, event payload names/parents, cached path strings, or breadcrumb lineage as enough category authority.
- Orphan categories could be promoted into storefront roots, invalid descendants could remain searchable, and stale category replay could preserve CommerceHub/affiliate category visibility after SQL lineage was deleted or detached.
- Product create/update accepted category IDs that were non-empty without proving the full SQL parent chain was live.

Production rule:
- PostgreSQL `Categories` own category hierarchy, lineage, and product default-category eligibility.
- A category is live only when the row belongs to the tenant, is not deleted, and every parent in the chain is present, same-tenant, not deleted, non-empty, not self-referential, and not cyclic.
- Rebuild/reindex/repair paths must recompose live lineage from SQL and tombstone/delete invalid descendants. Storefront, affiliate, CommerceHub, search, cache, breadcrumb, and event payload category state are derivative visibility only.

Implemented hardening:
- `CategoryLineageAuthority` centralizes SQL lineage snapshot recomposition for category rebuilds and product mutation validation.
- Product create/update now requires `CategoryLineageAuthority.RequireLiveCategoryAsync(...)` before accepting `CategoryId`.
- Admin category sync and admin category search sync use the SQL lineage snapshot; invalid descendants become inactive/offline in Mongo or are deleted from Elasticsearch instead of being rebuilt from stale projection state.
- Storefront category reads prune inactive, orphan, self-referential, and cyclic projection lineage; storefront tree building hides orphans instead of promoting them as root categories.
- Affiliate category update/deactivate projection now fences duplicate delivery, reloads current SQL affiliate-category authority, and nullifies category fields when SQL no longer proves a live parent relationship.
- CommerceHub listing/category projection and CommerceHub search overlay reject missing, inactive, or cyclic global category paths and unpublish/delete derivative listing/search visibility when category authority is gone.

Current evidence:
- `PointOfSale.API/Services/Catalog/CategoryLineageAuthority.cs`
- `PointOfSale.API/Controllers/ProductsController.cs`
- `PointOfSale.API/Controllers/AdminDataSyncController.cs`
- `PointOfSale.API/Controllers/AdminSearchSyncController.cs`
- `PointOfSale.API/Services/Storefront/StorefrontCategoryService.cs`
- `PointOfSale.Infrastructure/MongoDB/Services/CategoryReadRepository.cs`
- `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateCategoryUpdatedConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs`
- Focused validation passed on 2026-05-21 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~CategoryLineageAuthorityTests|FullyQualifiedName~CategoryTreeBuilderTests|FullyQualifiedName~GlobalReliabilityInfrastructureExtractionTests" -nologo -v minimal`.

### Offer promotion usage version authority

Problem:
- Paid-offer confirmation wrote `PromotionUsages` under payment lifecycle authority, but then recomposed `Promotions.CurrentUsage` with `ExecuteUpdateAsync`.
- Because that bulk update bypassed the tracked `IProjectionVersioned` mutation guard, the following `PromotionAppliedEvent` could be staged with the previous promotion version.
- Promotion projection freshness filters can reject same-version usage updates, so SQL usage truth could be correct while storefront promotion usage visibility and repair/rebuild watermarks drifted.

Production rule:
- Promotion usage-count mutations are SQL authority and must advance `Promotions.ProjectionMutationVersion` before a promotion visibility event is staged.
- Payment success may detect duplicate usage claims early, but the final usage-count mutation and `PromotionAppliedEvent` outbox row must share the tracked SQL mutation boundary.
- Bulk SQL may not mutate versioned promotion visibility fields unless it explicitly preserves the same monotonic version contract.

Implemented hardening:
- `PaymentLifecycleService.ConfirmOfferPromotionUsageAsync(...)` now updates `promotion.CurrentUsage` on the locked tracked `Promotion` row instead of using `ExecuteUpdateAsync`.
- The existing final `SaveChangesAsync` now persists the usage-count mutation, advances `ProjectionMutationVersion`, and stores `PromotionAppliedEvent` with the resolved next promotion version in the same transaction.
- `CheckoutPromotionVisibilityTests` now guards that this path does not regress to `ExecuteUpdateAsync` and that the tracked promotion mutation happens before event staging.

Current evidence:
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`
- `PointOfSale.Tests/Architecture/CheckoutPromotionVisibilityTests.cs`
- Focused validation passed on 2026-05-21 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~CheckoutPromotionVisibilityTests -nologo -v minimal`, `dotnet build PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --no-restore -nologo -v minimal -m:1`, and `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore -nologo -v minimal -m:1`.

### Promotion search replay SQL-authority recomposition

Problem:
- `PromotionProjectionConsumer` already reloaded SQL promotion authority before Mongo/product/category/cache/search side effects, but `ProductPromotionSearchIndexConsumer` still used promotion event payload scope/target and Mongo product promotion fields as the direct Elasticsearch mutation input.
- A delayed promotion update, delete, expiration, or duplicate background-expiration fan-out could reindex stale promotion visibility from Mongo after current SQL promotion truth had changed.

Production rule:
- Promotion search events are triggers only.
- Before Elasticsearch side effects, the search consumer must acquire duplicate EventId collapse, validate current SQL promotion version when the SQL row exists, reload current SQL scope/target, and recompute the winning product promotion from SQL.
- Hard-delete events may use the event's last scope/target only as deletion evidence after the SQL row is gone; the indexed promotion overlay must still come from remaining SQL promotion authority.

Implemented hardening:
- `ProductPromotionSearchIndexConsumer` now requires `ApplicationDbContext` instead of optional SQL wiring.
- The consumer uses `ProjectionConsumerEventFence` for promotion search side-effect collapse.
- It validates event version with `ProjectionAuthorityValidator`, reloads SQL `Promotions`, recomposes the winning active product promotion from SQL, applies the overlay in memory, and then indexes through the existing product SQL authority overlay.
- Tenant-wide product promotion reindex now calls the same SQL recomposition path per product rather than indexing raw Mongo promotion fields.

Current evidence:
- `PointOfSale.Infrastructure/Consumers/ProductPromotionSearchIndexConsumer.cs`
- `PointOfSale.Tests/Architecture/GlobalReliabilityInfrastructureExtractionTests.cs`
- Focused validation passed on 2026-05-21 with `dotnet build PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj --no-restore -nologo -v minimal -m:1`, `dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore -nologo -v minimal -m:1`, `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-build --filter FullyQualifiedName~GlobalReliabilityInfrastructureExtractionTests -nologo -v minimal`, and `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-build --filter "FullyQualifiedName~DataAuthorityViolationHardeningTests|FullyQualifiedName~EventOutboxConsistencyArchitectureTests|FullyQualifiedName~CheckoutPromotionVisibilityTests" -nologo -v minimal`.

### Storefront config, CommerceHub metadata, and marketplace rebuild authority gaps

Problem:
- Storefront config projection reloaded SQL and preserved tenant-currency watermarks, but duplicate active delivery could still repeat full-document Mongo replacement and Redis invalidation before a durable projection fence.
- CommerceHub store metadata propagation used event payload store name as derivative visibility input while adjacent shipping/currency fan-out already recomposed SQL.
- CommerceHub store/shipping/currency metadata fan-out could repeat search/Mongo side effects on duplicate delivery.
- Marketplace product rebuild treated SQL-deleted products as tombstones, but SQL-inactive or not-online-visible products could remain present in public marketplace read models after rebuild.

Production rule:
- Storefront config, CommerceHub metadata, and marketplace rebuild events are triggers only.
- Replacement/rebuild/fan-out paths must acquire duplicate side-effect collapse where replay can amplify cache/search/Mongo writes.
- Store metadata and rebuild visibility must recompose current SQL truth or tombstone/delete when SQL no longer proves public visibility.

Implemented hardening:
- `StorefrontConfigProjectionConsumer` now acquires `ProjectionEventFence` before SQL-to-Mongo replacement and Redis invalidation, and completes it only after side effects are safe.
- The same storefront config projection path now calls `DerivativeProjectionMutationGuard` before reviewed Mongo full replacement and Redis cache invalidation, declaring storefront config, checkout, and store metadata authority after SQL recomposition.
- `CommerceHubProjectionConsumer` now wraps store-name, shipping-fee, and currency metadata fan-out in `ProjectionConsumerEventFence`.
- CommerceHub store-name propagation now resolves current SQL store name from `StoreConfigs` with tenant fallback instead of using `TenantUpdatedEvent` / `StoreConfigUpdatedEvent` payload values.
- `MarketplaceReadModelProjectionServices` now tombstones/deletes product rebuild output for SQL-deleted, inactive, or offline products, and declares explicit `SqlTruthRebuildGuard` overlays for order and tenant-scoped homepage rebuild entrypoints.
- `SqlAuthorityOverlayKind` now includes `Order`, `StoreMetadata`, `Shipping`, and `Homepage` so rebuild entrypoints can state the SQL authority they intentionally require.

Current evidence:
- `PointOfSale.Infrastructure/Consumers/StorefrontConfigProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs`
- `PointOfSale.Infrastructure/ReadModels/Marketplace/MarketplaceReadModelProjectionServices.cs`
- `PointOfSale.Infrastructure/Reliability/SqlAuthorityOverlay.cs`
- `PointOfSale.Tests/Storefront/StorefrontConfigProjectionConsumerTests.cs`
- `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs`
- Focused validation passed on 2026-05-21 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~StorefrontConfigProjectionConsumerTests -nologo -v minimal`, `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter "FullyQualifiedName~CheckoutConcurrencyHardeningSourceTests|FullyQualifiedName~EventOutboxConsistencyArchitectureTests|FullyQualifiedName~GlobalReliabilityInfrastructureExtractionTests|FullyQualifiedName~ReliabilityAdoptionStrategyArchitectureTests" -nologo -v minimal`, and `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore -nologo -v minimal -m:1`.

### Coupon and shipping-method projection SQL-authority/replay collapse

Problem:
- Coupon projection was still mapping checkout-visible coupon create/update fields and usage counters from event payloads.
- A stale replay could therefore regress Mongo coupon discount, scope, activation, or usage visibility even though checkout mutation authority remained SQL-recomputed.
- Shipping-method projection already reloaded SQL, but duplicate active delivery could repeat Mongo/cache side effects.

Production rule:
- Coupon and shipping-method events are visibility triggers only.
- Coupon projection must reload current SQL `Coupon` authority before create/update/delete visibility writes and must derive usage from `CouponUsages`, not payload counters.
- Missing or deleted SQL coupon authority must collapse to inactive tombstone visibility, not preserve or resurrect a stale document.
- Duplicate active EventId delivery must collapse before MongoDB or Redis side effects.

Implemented hardening:
- `CouponProjectionConsumer` now injects `ApplicationDbContext`, acquires `ProjectionEventFence`, reloads SQL `Coupons`, maps SQL fields into Mongo, writes tombstones when SQL authority is missing/deleted, and derives usage from current SQL `CouponUsages`.
- `ShippingMethodProjectionConsumer` now acquires `ProjectionEventFence` around update/delete processing and completes it only after Mongo/tombstone/cache side effects finish.
- Focused source guards now assert coupon SQL reload, SQL usage recomposition, tombstone-on-missing authority, and shipping duplicate side-effect collapse.

Current evidence:
- `PointOfSale.Infrastructure/Consumers/CouponProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/ShippingMethodProjectionConsumer.cs`
- `PointOfSale.Tests/CouponProjectionConsumerConsistencyTests.cs`
- `PointOfSale.Tests/Architecture/EventOutboxConsistencyArchitectureTests.cs`
- `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs`
- Focused validation passed on 2026-05-20 with `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore -nologo -v minimal` and `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter "FullyQualifiedName~CouponProjectionConsumerConsistencyTests|FullyQualifiedName~CheckoutPromotionVisibilityTests|FullyQualifiedName~CheckoutConcurrencyHardeningSourceTests|FullyQualifiedName~EventOutboxConsistencyArchitectureTests" -nologo -v minimal`.

### PRODUCT-CON-05 storefront search visibility and multi-index authority

Problem:
- Product search create/update/delete/archive/publication and stock/price replay paths could still let stale derivative state influence Elasticsearch side effects.
- Stock/price search sync checked Mongo hidden visibility before the SQL product-search overlay, so a stale hidden Mongo document could delete a currently visible SQL product from search.
- Product delete search handling deleted unconditionally and did not consume archive/publication lifecycle events, so stale delete/archive/publication replay could remove a currently visible SQL product or leave publication changes to indirect repair timing.
- Product search overlays did not explicitly reject invalid SQL category lineage, leaving stale category/search lineage as a possible search-document side channel.

Production rule:
- Product search events are triggers only. Current PostgreSQL product, variant, stock, category lineage, media, promotion, publication, and CommerceHub listing authority decides whether storefront, CommerceHub, affiliate, autocomplete/suggestion, and homepage discovery search documents may exist.
- Search indexing must prefer SQL recomposition over Mongo or Elasticsearch state, collapse duplicate EventId side effects where migrated, and delete/tombstone when SQL no longer proves live visibility.
- Invalid SQL category lineage means product search visibility is not live; stale category names or paths must not be preserved in search documents.

Implemented hardening:
- `ProductSearchSqlAuthorityOverlay` now includes `SqlAuthorityOverlayKind.Category`, walks the current SQL category parent chain with cycle/self/empty/deleted checks, and returns no searchable projection when category lineage is invalid.
- `ProductSearchIndexConsumer`, `ProductUpdatedSearchIndexConsumer`, and `ProductStockPriceSearchIndexConsumer` now require `ApplicationDbContext`, apply `ProductSearchSqlAuthorityOverlay` before Elasticsearch mutation, and use `ProjectionEventFence` for duplicate create/update/stock/price search side-effect collapse.
- `ProductStockPriceSearchIndexConsumer` now applies SQL authority before hidden-product deletion, preventing stale Mongo hidden visibility from deleting current SQL-visible search documents.
- `ProductDeletedSearchIndexConsumer` now also handles `ProductArchivedEvent` and `ProductPublishStatusChangedEvent`, fences duplicate lifecycle search side effects, recomposes current SQL authority, reindexes if SQL says the product is visible, and deletes only when SQL says search visibility is gone.
- CommerceHub search overlay marks category authority explicitly and continues to delete/unpublish when SQL listing/product/variant/category authority is missing.

Current evidence:
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Infrastructure/Consumers/ProductSearchIndexConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/ProductUpdatedSearchIndexConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/ProductStockPriceSearchIndexConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/ProductDeletedSearchIndexConsumer.cs`
- `PointOfSale.Infrastructure/Reliability/SqlAuthorityOverlay.cs`
- `PointOfSale.Tests/Architecture/GlobalReliabilityInfrastructureExtractionTests.cs`
- Focused validation passed on 2026-05-21 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~GlobalReliabilityInfrastructureExtractionTests -nologo -v minimal`, `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter FullyQualifiedName~DataAuthorityViolationHardeningTests -nologo -v minimal`, `dotnet build PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --no-restore -nologo -v minimal -m:1`, and `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore -nologo -v minimal -m:1`.

Residual debt:
- This is targeted search visibility hardening, not a full search architecture rewrite. Product search still hydrates from Mongo first in several event paths and then overlays SQL authority; it is not a synchronous SQL search-read model.
- Autocomplete/suggestion behavior is protected through the same Elasticsearch product document fields, but there is no separate dedicated suggestion index authority layer in this sweep.
- Affiliate and CommerceHub search propagation were already partially SQL-overlaid; remaining promotion/policy refresh and lower-risk discovery paths remain mixed/local and should continue migrating incrementally.

### PRODUCT-CON-06 homepage and recommendation visibility consistency

Problem:
- Storefront homepage shelves could serve Redis/Mongo product-card snapshots for featured, new-arrival, best-seller, or curated product requests without rechecking current SQL product visibility.
- Curated homepage product hydration could return Mongo documents for products that SQL had since hidden, deleted, moved to invalid category lineage, unpublished, repriced, restocked, or had media/promotion state changed.
- Homepage section authoring validated CommerceHub product references from Mongo `CommerceHubProducts` instead of SQL listing/product/variant/category authority.
- CommerceHub homepage rebuild copied homepage section JSON as-is, so stale curated listing/category IDs could survive product deletion, listing unpublish, category deactivation, or stale homepage replay.
- Homepage publish cache invalidation removed only the default localized CommerceHub homepage key, and stock-only product events did not invalidate homepage caches.

Production rule:
- Homepage/recommendation events, Redis homepage caches, Mongo product documents, Elasticsearch ranking/search hits, homepage section JSON, CommerceHub homepage documents, affiliate discovery documents, and rebuild payloads are derivative visibility only.
- Storefront homepage product-card reads must recompose current SQL product, category lineage, media, stock, price, promotion, publication, and affiliate-policy authority before returning cached/projected cards.
- Homepage section authoring and rebuild/read paths must validate or scrub curated CommerceHub listing/category IDs against current SQL listing, product, variant, category, and publication authority.
- Rebuilds must prefer dropping invalid curated sections or tombstoning tenant homepage projections over preserving stale discovery references.

Implemented hardening:
- `StorefrontHomeService` now recomposes homepage product DTOs from SQL after Redis/Mongo/Elasticsearch cache/projection lookup, preserving requested order but dropping products that SQL no longer proves visible. It also derives current stock, price, media, active promotion badge, affiliate-enabled state, and live category lineage from PostgreSQL.
- `SuperAdminHomepageSectionManagementService` now validates curated product section IDs against SQL `CommerceHubListings`, `Products`, `ProductVariants`, and `CommerceHubCategories` instead of Mongo CommerceHub product documents.
- `HomepageSectionReadService` sanitizes cached/published section product and category IDs from SQL authority before returning sections.
- `MarketplaceHomepageReadModelProjectionService` filters inactive homepage sections, scrubs invalid curated product/category references, skips curated shelves that lose all live references, and deletes tenant homepage projections when SQL no longer produces live content.
- `ProductCacheInvalidationConsumer` now invalidates homepage caches on stock changes, and `RedisInvalidationConsumer` invalidates all localized CommerceHub homepage cache keys for the published scope.

Current evidence:
- `PointOfSale.API/Services/Storefront/StorefrontHomeService.cs`
- `PointOfSale.API/Services/Homepage/HomepageSectionReadService.cs`
- `PointOfSale.API/SuperAdmin/Services/SuperAdminHomepageSectionManagementService.cs`
- `PointOfSale.Infrastructure/ReadModels/Marketplace/MarketplaceReadModelProjectionServices.cs`
- `PointOfSale.Infrastructure/Consumers/ProductCacheInvalidationConsumer.cs`
- `PointOfSale.API/Consumers/RedisInvalidationConsumer.cs`
- `PointOfSale.Tests/Infrastructure/MarketplaceReadModelProjectionTests.cs`
- `PointOfSale.Tests/Architecture/GlobalReliabilityInfrastructureExtractionTests.cs`
- Focused validation passed on 2026-05-21 with `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore -nologo -v minimal -m:1`, `dotnet build PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --no-restore -nologo -v minimal -m:1`, and `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~MarketplaceReadModelProjectionTests|FullyQualifiedName~GlobalReliabilityInfrastructureExtractionTests|FullyQualifiedName~SuperAdminHomepageSectionManagementServiceTests" -nologo -v minimal`.

Residual debt:
- This hardens the inspected homepage/recommendation visibility paths; it is not a recommendation ranking or ML personalization migration.
- No separate dedicated personalized recommendation engine, ranking snapshot store, delayed recommendation retry worker, or external bulk affiliate/CommerceHub feed writer was found and migrated in this sweep. Future implementations must start from the SQL-authority rule above.
- Storefront product list/search pages and lower-risk discovery pages retain their existing projection/cache strategy outside the homepage product-card recomposition added here.

### PRODUCT-CON-07 multi-channel publication replay authority

Problem:
- `ProductPublishStatusChangedEvent` existed but was not emitted by the main product update path, and the storefront publication lifecycle consumer was not registered, so publication changes could depend on indirect update/search repair timing.
- Product and CommerceHub search overlays did not both require current SQL `PublishStatus.Published` and `IsOnlineVisible`, leaving Draft/offline products able to remain visible in derivative search/listing surfaces.
- CommerceHub listing projection reacted to product delete/archive but not product publish-status/online-visibility changes, and a stale listing-unpublish replay could hide a currently SQL-published listing.
- Affiliate public/intelligence product propagation reacted to delete/archive/media/stock/price in places, but publish-status replay was not a first-class lifecycle trigger and affiliate intelligence visibility did not require `PublishStatus.Published`.

Production rule:
- Product publication events, unpublication events, Mongo/read-model state, search documents, homepage/discovery state, affiliate documents, CommerceHub listing documents, feed/export snapshots, repair payloads, and delayed retries are triggers or derivative visibility only.
- Current SQL product authority must prove `!IsDeleted`, active, online-visible, and `PublishStatus.Published` before public product visibility can be restored.
- CommerceHub listing visibility additionally requires current SQL listing authority: listing exists, is not deleted, and is published. A stale unpublish event must fail closed against current SQL listing/product publication truth instead of hiding a republished listing.
- Runtime mutation paths must prefer SQL recomposition and field-scoped/tombstone convergence over full replacement from stale projections.

Implemented hardening:
- `ProductsController.UpdateProduct(...)` now stages `ProductPublishStatusChangedEvent` when `PublishStatus` or `IsOnlineVisible` changes and includes current publish status in the product update event shape.
- `ProductVisibilityLifecycleProjectionConsumer` is now registered, fences duplicate lifecycle side effects, reloads current SQL product publication authority, recomposes visible storefront documents through `ProductSearchSqlAuthorityOverlay`, and tombstones when SQL no longer proves publication.
- `ProductSearchSqlAuthorityOverlay` and `CommerceHubSearchSqlAuthorityOverlay` now require SQL `PublishStatus.Published`; CommerceHub search also requires SQL product online visibility.
- `CommerceHubProjectionConsumer` now consumes product publication changes, recomposes listing publication from current SQL product and listing authority, field-updates listing visibility with product/listing freshness metadata, applies the SQL overlay before listing upsert/search sync, and ignores stale listing-unpublish replay when SQL says the listing is currently published.
- Affiliate public policy and affiliate intelligence consumers now consume product publication changes; affiliate visibility paths require SQL `PublishStatus.Published` before retaining public/intelligence visibility.

Current evidence:
- `PointOfSale.API/Controllers/ProductsController.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.Infrastructure/Consumers/ProductVisibilityLifecycleProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProductPolicyProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProductIntelligenceProjectionConsumer.cs`
- `PointOfSale.Tests/Architecture/GlobalReliabilityInfrastructureExtractionTests.cs`
- Focused validation passed on 2026-05-21 with `dotnet build PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --no-restore -nologo -v minimal -m:1`, `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore -nologo -v minimal -m:1`, and `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter FullyQualifiedName~GlobalReliabilityInfrastructureExtractionTests -nologo -v minimal`.

Residual debt:
- This is targeted publication replay hardening, not a full publication architecture rewrite.
- The audited hardened paths cover storefront lifecycle projection, product search, CommerceHub listing/search, and affiliate public/intelligence publication propagation. Dedicated external feed/export publication writers, if added later, must be hardened separately.
- `ProductPublishStatusChangedEvent` is now emitted from the main product update controller path. Future bulk product import, admin repair, direct SQL, or synchronization paths that mutate publication/online visibility must either emit the same trigger or directly invoke SQL-authoritative recomposition/tombstone behavior.
- Affiliate intelligence republish still relies on the adjacent product update projection to rebuild rich intelligence content after SQL becomes visible; the publish-status lifecycle trigger is primarily a delete/tombstone/convergence guard.
- There is no new DB-level invariant tying every derivative CommerceHub/affiliate/search document to current product publication state. The guarantee is runtime/rebuild enforced on migrated paths.

### PRODUCT-CON-08 bulk product cache fanout replay collapse

Problem:
- `ProductCacheInvalidationConsumer` fenced duplicate stock and price invalidation side effects, but product create, update, delete, and publication invalidation still executed Redis product-list, product-detail, promotion, and homepage pattern deletes directly.
- Bulk publication replay or duplicate product lifecycle redelivery could therefore amplify Redis invalidation work even though cache state is derivative and business authority stayed in SQL.
- Product detail invalidation used event payload slugs when present, which let stale payloads influence cache-key selection after the product slug had already converged in SQL.

Production rule:
- Product cache invalidation events are infrastructure triggers only.
- High-volume product cache invalidation must acquire duplicate/active EventId collapse before Redis side effects when SQL context is available.
- Cache-key identity should prefer current SQL product identity/slug over event payload slugs. Redis caches remain non-authoritative and invalidation remains best-effort.

Implemented hardening:
- `ProductCacheInvalidationConsumer` now requires `ApplicationDbContext` instead of optional SQL context.
- Product created, updated, deleted, stock, price, and publish-status invalidation paths all route through a shared `ProjectionConsumerEventFence` wrapper before Redis side effects.
- The consumer reloads current SQL product slug with query filters ignored before product-detail cache invalidation, falling back to the payload slug only when SQL no longer has a row.
- Focused architecture guard coverage now asserts the all-event fence constants, SQL slug reload, and non-optional SQL context.

Current evidence:
- `PointOfSale.Infrastructure/Consumers/ProductCacheInvalidationConsumer.cs`
- `PointOfSale.Tests/Architecture/GlobalReliabilityInfrastructureExtractionTests.cs`
- Focused validation passed on 2026-05-21 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~GlobalReliabilityInfrastructureExtractionTests -nologo -v minimal`, `dotnet build PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --no-restore -nologo -v minimal -m:1`, and `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore -nologo -v minimal -m:1`.

Residual debt:
- This closes duplicate EventId overlap for the product cache consumer; it does not make Redis authoritative and does not throttle legitimate large publication batches.
- Distinct older EventIds can still invalidate caches by design, because cache invalidation is a freshness tool and not a mutation authority path.
- CMS/navigation/footer, CDN purge, feed/export, cache warmer, and future bulk import/admin repair fanout paths remain separate review targets if their invalidation or external purge side effects become costly.

### PRODUCT-CON-09 admin product rebuild resurrection safety

Problem:
- Admin product-variant repair could rebuild storefront Mongo product documents from partial SQL field copies rather than the hardened SQL overlay.
- That path could preserve `IsOnlineVisible` without requiring current `PublishStatus.Published` or live SQL category lineage, and could full-replace visibility fields after a lifecycle tombstone.
- Under stale repair replay, a manual rebuild could therefore restore featured/homepage/recommendation metadata for a product SQL no longer allowed to be public.

Production rule:
- Admin repair, rebuild, reindex, cache-warmer, homepage/recommendation, affiliate, and CommerceHub discovery repair paths are triggers only.
- Before derivative product visibility mutation, rebuild tooling must recompose current SQL product, category, media, variant, stock, price, promotion, and publication authority.
- If SQL no longer proves active, online, published, live-category visibility, repair must write or preserve a freshness-aware tombstone instead of full-replacing Mongo documents from stale rebuild state.

Implemented hardening:
- `AdminSyncController` now routes product repair through `ProductSearchSqlAuthorityOverlay.ApplyAsync(...)`.
- The all-products AdminSync repair scan includes deleted SQL rows so soft-deleted products converge to Mongo tombstones instead of being skipped.
- AdminSync upserts only SQL-recomposed public product documents and calls `ProductReadRepository.TombstoneIfFreshAsync(...)` for deleted, inactive, offline, unpublished, or invalid-category SQL products.
- The dormant direct `BuildProductDocumentFromSql(...)` full-document mapper was removed from AdminSync so future edits cannot easily bypass the overlay.
- `GlobalReliabilityInfrastructureExtractionTests` now guards the AdminSync SQL overlay and tombstone behavior.

Current evidence:
- `PointOfSale.API/Controllers/AdminSyncController.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Infrastructure/MongoDB/Services/ProductReadRepository.cs`
- `PointOfSale.Tests/Architecture/GlobalReliabilityInfrastructureExtractionTests.cs`
- Focused validation passed on 2026-05-21 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~GlobalReliabilityInfrastructureExtractionTests -nologo -v minimal` and `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore -nologo -v minimal -m:1`. A first parallel API build attempt hit a transient Windows `obj` file lock and passed when rerun serially.

Residual debt:
- This hardens the inspected AdminSync repair endpoint; it is not a universal recommendation/rebuild engine.
- Existing storefront homepage reads, CommerceHub homepage projection, CommerceHub reindex, and CommerceHub auto-healing were already SQL-overlay protected on inspected paths. Future personalized recommendation engines, ranking snapshot stores, feed exporters, CDN/cache warmers, bulk importers, and direct repair endpoints must adopt the same rule explicitly.
- The single-product AdminSync endpoint still returns 404 for a hard-deleted SQL row because no SQL timestamp/version remains to derive a fresh tombstone. Normal soft-delete repair and lifecycle tombstones remain covered.

### PRODUCT-CON-10 suggestion/autocomplete consistency safety

Problem:
- CommerceHub autocomplete and suggestion APIs could return cached response DTOs, Elasticsearch hits, or Mongo fallback suggestions after only vendor-suspension filtering at the response boundary.
- That left a replay window where stale product/category/title/media suggestion state, stale zero-result suggestion products, or stale popular/product-name completion terms could remain visible after SQL product/listing/category authority changed.

Production rule:
- Autocomplete, typeahead, suggestions, zero-result suggestions, popular search terms, and product-name completion text are derivative discovery visibility only.
- Product suggestions must be recomposed from current SQL CommerceHub listing, product publication, variant, stock, price, media, tenant, governance, and category authority before response mapping, even when the response was served from an in-memory API cache.
- Category suggestions must pass active SQL CommerceHub category lineage, and popular/suggested text terms must still resolve to current SQL-visible CommerceHub products or categories.

Implemented hardening:
- `CommerceHubQueryService` now revalidates cached suggestion/autocomplete responses before returning them.
- Fresh Elasticsearch and Mongo fallback products for suggestions, autocomplete, and zero-result suggestions are recomposed through `CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(...)`; stale or unauthorized SQL authority drops the suggestion.
- SQL listing tenant ownership is loaded from `CommerceHubListings` instead of Mongo read models before authority recomposition.
- Popular and similar terms are filtered by current SQL-visible listing/product/category authority, and autocomplete category buckets are filtered through live active SQL CommerceHub category lineage.
- Focused source guard coverage requires the response-boundary SQL recomposition helpers and blocks the old vendor-only suggestion/autocomplete filters.

Current evidence:
- `PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Tests/Architecture/GlobalReliabilityInfrastructureExtractionTests.cs`
- Focused validation passed on 2026-05-21 with `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore -nologo -v minimal -m:1` and `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~GlobalReliabilityInfrastructureExtractionTests -nologo -v minimal`.

Residual debt:
- This is response-boundary hardening, not a dedicated completion-index architecture. No separate standalone suggestion index was found and migrated.
- Storefront header autocomplete remains protected through the product Elasticsearch document authority path; its browser-side in-memory cache is still a short-lived client cache and not server-side tombstone enforcement.
- Future dedicated keyword caches, trending workers, suggestion rebuild jobs, cache warmers, feed/export completion writers, or delayed autocomplete retry workers must add SQL recomposition, tombstone/delete convergence, and replay collapse explicitly.

### Payment-method and CommerceHub listing projection SQL-authority recomposition

Problem:
- Payment-method projection had freshness filters and tombstones, but still wrote customer-visible payment-method fields from legacy event payloads.
- CommerceHub listing create/update/unpublish projection had field freshness and search SQL overlays, but still used event payload category, publication, price, and attributes as Mongo mutation input before search fan-out.

Production rule:
- Checkout payment-method and CommerceHub listing events are projection triggers only.
- Before Mongo/search/cache visibility mutation, consumers must reload current SQL `TenantPaymentMethod`, `CommerceHubListing`, and dependent variant price authority.
- Duplicate active EventId delivery must collapse before Mongo/search side effects.

Implemented hardening:
- `PaymentMethodProjectionConsumer` now uses `ProjectionEventFence`, reloads current `TenantPaymentMethods`, validates exact SQL version, decrypts/evaluates current provider configuration, and writes Mongo from SQL-derived public configuration only.
- `CommerceHubProjectionConsumer` now uses `ProjectionConsumerEventFence` around listing lifecycle events, reloads current `CommerceHubListings`, resolves current SQL variant price, uses SQL listing category/publication/attribute state, and unpublishes derivative visibility when SQL listing authority is missing or deleted.
- `ProjectionEventFence` now has an EventId overload so legacy event shapes can still use the shared duplicate/active replay collapse primitive without widening event contracts during a hardening sweep.

Current evidence:
- `PointOfSale.Infrastructure/Consumers/PaymentMethodProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Reliability/ProjectionEventFence.cs`
- `PointOfSale.Tests/Architecture/EventOutboxConsistencyArchitectureTests.cs`
- `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs`

### PRODUCT-CON-04 media/CDN authority and resurrection safety

Problem:
- Product gallery mutations already updated storefront product documents and product search from SQL media authority, but adjacent public propagation paths did not consume `ProductMediaChangedEvent`.
- CommerceHub listing images and affiliate public/intelligence thumbnails could therefore keep an old primary image, deleted image, or stale media ordering until an unrelated product, policy, or listing refresh happened.
- That stale image visibility is derivative, but it is still customer-facing and can resurrect deleted media under replay/rebuild timing.

Production rule:
- PostgreSQL `ProductMedia` rows and `ProductVariants.MediaId` own media existence, ordering, primary-image derivation, and variant media linkage.
- `ProductMediaChangedEvent`, Mongo product media, legacy `Images`, CDN URLs, search documents, affiliate documents, and CommerceHub listings are triggers or derivative visibility only.
- Runtime media propagation must collapse duplicate active delivery, validate the media event version against current SQL product mutation version, and recompose media from SQL before changing public projections.
- Rebuild/reindex/repair paths must use SQL media overlays or tombstone/delete when SQL no longer proves product/media visibility. CDN cache expiry remains operational-only unless a dedicated purge worker exists.

Implemented hardening:
- `CommerceHubProjectionConsumer` now consumes `ProductMediaChangedEvent`, acquires `ProjectionConsumerEventFence`, rejects stale/future media versions against SQL product authority, derives the listing image through `CommerceHubSearchSqlAuthorityOverlay`, writes only the image field with product-projection freshness markers, and re-syncs CommerceHub search from SQL overlay.
- `AffiliateProductPolicyProjectionConsumer` now consumes `ProductMediaChangedEvent`, fences duplicate active delivery, validates SQL media event authority, and rebuilds the public affiliate product/Elasticsearch thumbnail from SQL-overlaid product media instead of `product.Images`.
- `AffiliateProductIntelligenceProjectionConsumer` now consumes `ProductMediaChangedEvent`, fences duplicate active delivery, reloads current SQL media ordering, rejects stale/future media versions, and updates the intelligence image list without creating missing affiliate visibility from a media event alone.

Current evidence:
- `PointOfSale.Infrastructure/Consumers/ProductMediaChangedEventConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProductPolicyProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProductIntelligenceProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Tests/Architecture/EventOutboxConsistencyArchitectureTests.cs`

### Reservation cleanup stale-candidate authority recomposition

Problem:
- Inventory reservation cleanup scanned stale/orphan reservation candidates and preloaded order/payment state before mutation.
- If order, payment, provider, or unresolved-webhook truth advanced after discovery, the direct local release branch could release stock and coupon reservations from an obsolete snapshot.

Production rule:
- Cleanup discovery is evidence only. Reservation release authority must be current SQL truth at mutation time.
- Before local stock/coupon release, cleanup must reload current reservation, order, payment-intent, CommerceHub order, CommerceHub payment, and unresolved-webhook evidence under a serializable SQL transaction and row locks.
- Changed or uncertain SQL truth must collapse to skip or route through the payment lifecycle expiration path rather than directly releasing inventory.

Implemented hardening:
- `InventoryReservationCleanupJob` now recomposes current cleanup authority per candidate, recomputes remaining reserved quantity, and opens a serializable local release transaction.
- The direct release branch clears stale EF tracking, reloads locked SQL authority, and skips if current order/payment/provider truth no longer permits release.
- If locked recomposition discovers a storefront pending/authorized/awaiting-verification payment state, the job exits the local release transaction and delegates to `IPaymentLifecycleService.MarkPaymentExpiredAsync(...)`.

Current evidence:
- `PointOfSale.Infrastructure/Jobs/InventoryReservationCleanupJob.cs`
- `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs`
- Focused validation passed on 2026-05-20 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~CheckoutConcurrencyHardeningSourceTests -nologo -v minimal`; API build passed with `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore -nologo -v minimal`.

### CHECKOUT-CON-14 recovery/reconciliation authority recomposition

Problem:
- Recovery and reconciliation workers intentionally scan unresolved provider/webhook/payout evidence, but those scans can become stale before mutation.
- A stale recovery worker must not clear unresolved external money, enqueue suspense clearance, release marketplace holds, or move payout status based on a pre-transaction candidate snapshot.

Production rule:
- Recovery discovery is evidence only. Mutation authority must be current SQL truth.
- Before unresolved captured-payment clearance, storefront suspense payment recovery, payout reconciliation mutation, recovery accounting enqueue, or marketplace hold release, the worker must reload the exact evidence row and dependent payment/order/payout rows inside a serializable SQL transaction.
- Duplicate recovery workers, delayed retries, and stale reconciliation loops must collapse when the locked SQL row is no longer in the recoverable state.

Implemented hardening:
- `CommerceHubFinanceReconciliationJob.ResolveLinkedUnresolvedCapturedPaymentsAsync(...)` now selects candidate IDs only, opens a per-candidate serializable transaction, locks the unresolved webhook row, locks the matched paid `CommerceHubPayment`, locks the current `CommerceHubMarketplaceOrder`, and only then queues suspense clearance or releases marketplace holds.
- `StorefrontUnresolvedPaymentReconciliationJob.ExecuteAsync(...)` now selects webhook log IDs only, opens a serializable recovery transaction, locks the `PaymentWebhookLog`, locks the current `PaymentIntent` and `Order`, then delegates payment lifecycle replay and resolved-unresolved accounting enqueue inside that SQL boundary.
- `CommerceHubPayoutReconciliationHostedService` now quarantines stale `Processing` payouts only after a serializable transaction reloads and locks the current payout row.

Current evidence:
- `PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationJob.cs`
- `PointOfSale.API/Services/Payments/StorefrontUnresolvedPaymentReconciliationJob.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPayoutReconciliationHostedService.cs`
- `PointOfSale.Tests/Architecture/FinancialIntegrityRiskHardeningTests.cs`
- Focused validation passed on 2026-05-20 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~FinancialIntegrityRiskHardeningTests -nologo -v minimal`, `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore -nologo -v minimal`, and adjacent finance/refund/fulfillment/checkout regression source slices.

### Fulfillment replay after inventory consumption

Problem:
- Checkout, payment, reservation, inventory consumption, and shipment lineage are committed in SQL, but delayed fulfillment callbacks, duplicate shipment replays, stale warehouse retries, delayed carrier confirmations, and recovery flows can arrive after that irreversible authority has advanced.
- Those payloads are useful evidence, but they must not become a second fulfillment authority lane or recreate shipment eligibility after inventory has already been consumed.

Production rule:
- After inventory consumption, fulfillment and shipment state are SQL-authoritative only.
- Before shipment mutation, warehouse allocation/release, fulfillment release, carrier release, inventory movement mutation, fulfillment recovery, or fulfillment-derived commission/payout eligibility, the service must reload current SQL order/payment/reservation/inventory/fulfillment truth.
- Duplicate or older outbound fulfillment targets must collapse against current SQL fulfillment status; stale payloads must fail closed or re-enter SQL recomposition before side effects.

Implemented hardening:
- `OrderFulfillmentService` now starts a serializable SQL transaction when it owns the boundary, reloads the current order row with provider-specific row locks, and collapses duplicate or older outbound fulfillment targets before lifecycle events are published.
- `ShipmentService` now runs shipment creation, shipment status mutation, and shipment authority repair inside serializable SQL transactions around the existing locked order recomposition and `IOrderFulfillmentService` synchronization.
- `AffiliateService.ApproveCommissionForDeliveredOrderAsync(...)` now reloads locked SQL order fulfillment/payment/accounting authority inside a serializable transaction before delivered commission approval.
- Existing inventory delivery commit protections remain the stock-consumption authority: event inbox fencing, SQL delivery authority validation, reservation/stock `FOR UPDATE` locks, already-committed quantity accounting, and movement idempotency.

Current evidence:
- `PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs`
- `PointOfSale.API/Services/Shipping/ShipmentService.cs`
- `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs`
- `PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs`
- `PointOfSale.Tests/Architecture/OrderFulfillmentArchitectureTests.cs`
- Focused validation passed on 2026-05-20 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter FullyQualifiedName~OrderFulfillmentArchitectureTests -nologo -v minimal`, shipment/inventory replay slices, CHECKOUT-CON-01 through 09 focused regression slices, and STOCK/PRICE/VAR smoke slices.

### CHECKOUT-CON-13 shipment/fulfillment double-execution authority

Problem:
- Carrier create/registration execution is intentionally outside the local SQL transaction, but successful carrier responses can return after the local shipment/order authority has changed.
- A delayed carrier registration completion, registration lookup replay, or recovery retry must not apply a stale success payload over a cancelled, terminal, already advanced, or payment/accounting-ineligible SQL shipment/order state.

Production rule:
- Carrier payloads and recovery lookup results are evidence, not shipment authority.
- After every carrier create/registration success and before local success persistence, the system must re-enter SQL authority, lock the current shipment and order rows, and reject stale shipment authority into reconciliation.
- Terminal/cancelled shipment or order truth is monotonic for carrier-registration persistence; local shipment status, order tracking fields, and fulfillment synchronization may advance only when current SQL still proves shipment execution authority.

Implemented hardening:
- `CarrierShipmentService.PersistSuccessfulCarrierRegistrationAsync(...)` and `ReconcileExistingExternalRegistrationAsync(...)` now open a serializable persistence boundary when needed, clear stale EF tracking, reload locked `Shipments` and `Orders` rows, and validate current authority before applying carrier success.
- The stale authority guard rejects cancelled, terminal, already-advanced, deleted, or payment/accounting-ineligible shipment/order truth by marking external registration `ReconciliationRequired`; external reference/tracking evidence is retained for operator follow-up without advancing shipment or fulfillment authority.
- Focused architecture coverage now asserts that carrier success persistence happens after external call, before `ApplySuccessfulCarrierRegistration(...)`, and includes row locks, stale-authority rejection, and reconciliation marking.

Current evidence:
- `PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs`
- `PointOfSale.Infrastructure/Consumers/ShipmentExternalRegistrationRequestedConsumer.cs`
- `PointOfSale.API/Services/Shipping/ShipmentAuthorityRecoveryHostedService.cs`
- `PointOfSale.Tests/Architecture/OrderFulfillmentArchitectureTests.cs`
- Focused validation passed on 2026-05-20 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~OrderFulfillmentArchitectureTests -nologo -v minimal`, checkout/payment/inventory regression source slices, shipment external registration tests, and STOCK/PRICE/VAR smoke slices.

### CommerceHub active customer-cart checkout overlap collapse

Problem:
- CommerceHub checkout already had idempotency-key locking and final SQL recomposition, but the distributed checkout lock was scoped to `customer + idempotency key`.
- A duplicate mobile/browser/provider retry against the same active CommerceHub customer cart could choose a different idempotency key and enter a second active checkout lane until the later cart-concurrency claim failed.
- That later collapse protected the final order path, but it still left unnecessary pre-authority idempotency side effects and a stale pending-claim/cart-lock cleanup edge when cart claiming or request fingerprint validation failed before the final transaction.

Production rule:
- CommerceHub checkout idempotency is subordinate to active customer-cart checkout authority.
- A request must first collapse per active customer cart, then bind request idempotency, then claim SQL cart/order/promo/reservation/payment-method authority.
- Changing the idempotency key during active duplicate checkout processing must not create an independent checkout authority lane.

Implemented hardening:
- `CommerceHubCheckoutService.CheckoutAsync(...)` now acquires a customer-cart checkout lock before the existing idempotency-key lock and before inserting a `CommerceHubCheckoutIdempotency` row.
- If the customer-cart lock is held, checkout returns a matching persisted replay when available or fails closed with checkout-in-progress before order, reservation, promotion, inventory, payment, or outbox side effects.
- `LockCartForCheckoutAsync(...)` now claims the CommerceHub cart inside a serializable transaction and reloads the cart row through provider-specific SQL row locks (`FOR UPDATE` / `UPDLOCK, ROWLOCK, HOLDLOCK`).
- Failed cart claiming or cart/request mismatch after idempotency insertion now cleans the pending idempotency row and cart lock before surfacing the failure.

Current evidence:
- `PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs`
- `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs`
- `PointOfSale.Tests/CommerceHub/CommerceHubCheckoutFinancialControlTests.cs`
- Focused validation passed on 2026-05-20 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~CheckoutConcurrencyHardeningSourceTests -nologo -v minimal`, `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~CommerceHubCheckoutFinancialControlTests -nologo -v minimal`, and `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter "FullyQualifiedName~CheckoutRequestIdempotencyCoordinatorTests|FullyQualifiedName~PaymentServiceCreatePaymentIntentGuardTests|FullyQualifiedName~CommerceHubPaymentServiceTests" -nologo -v minimal`.

### Critical storefront and CommerceHub Mongo order reads must prefer the freshest duplicate projection

Problem:
- Storefront and CommerceHub critical order reads are intentionally Mongo-served but SQL-repairable.
- Mongo uniqueness for order projections depended on startup/consumer index creation that already fails open with warning-only logging when index creation cannot be completed.
- Critical repositories and projection-repair paths still used `FirstOrDefaultAsync(...)` for `OrderId`, `OrderNumber`, or `MarketplaceOrderId`, so if duplicate documents existed from a historical race or failed unique-index creation, repair could update one copy while customer-facing or operator-facing reads served another stale duplicate.

Production rule:
- SQL remains the authority anchor, but critical Mongo order reads must treat historical duplicate projections as possible whenever uniqueness is not synchronously re-proven at request time.
- If more than one Mongo order document matches the same authoritative order identity, read and repair paths must prefer the freshest projection using authoritative markers such as `LastSourceMutationTicks`, `LastEventVersion`, `LastEventOccurredAtUtc`, `UpdatedAt`, and `CreatedAt`.
- Arbitrary first-match Mongo reads are not an acceptable authority decision on customer-critical or operator-critical order surfaces.

Implemented hardening:
- `OrderReadRepository.GetByIdAsync(...)`, `GetByOrderNumberAsync(...)`, and `GetByIdsAsync(...)` now load matching documents and choose the freshest storefront projection before serving it.
- `CommerceHubOrderReadRepository.GetByMarketplaceOrderIdAsync(...)` now chooses the freshest marketplace projection instead of the first Mongo match.
- `StorefrontOrderProjectionRepairService`, `StorefrontOrderProjectionReconciliationService`, and `CommerceHubOrderProjectionRepairService` now sort existing projections by freshness before deciding whether the existing document can be reused or must be repaired from SQL.
- Architecture regression coverage now asserts this freshest-projection rule alongside the still-fail-open Mongo index initialization behavior so future changes cannot silently regress to first-match reads.

Current evidence:
- `PointOfSale.Infrastructure/MongoDB/Services/OrderReadRepository.cs`
- `PointOfSale.Infrastructure/MongoDB/Services/CommerceHubOrderReadRepository.cs`
- `PointOfSale.API/Services/Storefront/StorefrontOrderProjectionRepairService.cs`
- `PointOfSale.API/Services/Storefront/StorefrontOrderProjectionReconciliationService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubOrderProjectionRepairService.cs`
- `PointOfSale.Infrastructure/MongoDB/MongoDbIndexInitializer.cs`
- `PointOfSale.Infrastructure/Consumers/OrderProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/CommerceHubOrderProjectionConsumer.cs`
- Focused validation passed on 2026-05-12 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~CrossSystemCriticalRiskHardeningTests -nologo -v minimal --no-restore`.

### CommerceHub unmatched provider-webhook durability under initiation-lock contention

Problem:
- `CommerceHubPaymentService.InitiateAsync(...)` holds the distributed payment lock across provider handoff and provider-session persistence.
- `PersistUnresolvedPaymentWebhookAsync(...)` still needs that same lock for the fully serialized suspense-routing and order-hold branch, but first-seen external-money evidence must survive even when the lock is busy or its infrastructure is degraded.

Production rule:
- Unmatched CommerceHub provider-success and provider-refund callbacks must durably record webhook evidence before returning retryable failure caused by initiation-lock contention or transient local processing failure.
- If exact order/payment linkage is still racing, the system may persist a tenant-scoped or scope-less unresolved webhook row and reconcile it later, but it must not depend solely on provider redelivery to preserve external-money truth.
- Distributed payment locks are serialization aids, not permission to drop the only copy of externally confirmed money evidence.

Implemented hardening:
- `PersistUnresolvedPaymentWebhookAsync(...)` now degrades from serialized suspense-routing/order-hold execution to durable unresolved-webhook persistence when payment-lock acquisition fails.
- The degraded branch freezes safe order/store scope when available, records an explicit deferred-reconciliation error, preserves any prior routed-suspense state on replay, and returns success once local evidence is durably stored.
- Controller retry semantics therefore no longer depend on provider redelivery for first-seen unmatched provider-success or provider-refund callbacks.

Current evidence:
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.API/Controllers/CommerceHubPaymentsController.cs`
- Focused validation passed on 2026-05-11 with `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --filter FullyQualifiedName~CommerceHubPaymentServiceTests -nologo -v minimal`, `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --filter FullyQualifiedName~CommerceHubPaymentRecoveryAuthorityServiceTests -nologo -v minimal`, and `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --filter FullyQualifiedName~CrossSystemCriticalRiskHardeningTests -nologo -v minimal`.

### Provider webhook tenantless ingress authority

Problem:
- Payment and shipping provider webhooks are normally delivered to the shared API hostname and do not carry an authenticated tenant claim or verified storefront host.
- The webhook controllers resolved tenant authority later from signed provider payloads plus stored SQL payment/shipment records, but they were only `[AllowAnonymous]`.
- `TenantResolutionMiddleware` strips raw `X-Tenant-Id` and rejects tenantless requests before controller execution unless an endpoint is marked `[AllowAnonymousTenant]`, so provider callbacks could be blocked before payment, suspense, reconciliation, or carrier fulfillment recovery code ran.

Production rule:
- Provider webhook endpoints that resolve tenant from provider payload and stored SQL authority must be marked `[AllowAnonymousTenant]`.
- Raw provider tenant headers remain advisory at most; business authority must come from the webhook trust pipeline and stored payment/shipment records.
- Generic tenant middleware must not be the authority gate for webhook callbacks that intentionally resolve tenant after body/signature parsing.

Relevant implementation:
- `PointOfSale.API/Controllers/PaymentWebhooksController.cs`
- `PointOfSale.API/Controllers/StripeWebhooksController.cs`
- `PointOfSale.API/Controllers/PaymobWebhooksController.cs`
- `PointOfSale.API/Controllers/FawryWebhookController.cs`
- `PointOfSale.API/Controllers/ShippingWebhooksController.cs`
- `PointOfSale.API/Middleware/TenantResolutionMiddleware.cs`

Regression coverage:
- `PointOfSale.Tests/Payments/WebhookSecurityHardeningTests.cs`
- Focused validation passed on 2026-05-11 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~WebhookSecurityHardeningTests -nologo -v minimal`.

### Storefront final checkout full in-transaction authority refresh

Problem:
- Storefront checkout hardening initially landed in partial steps: first the final transaction re-locked storefront, shipping, and payment-method authority; then header refresh was fixed; then `OrderItem` money and affiliate snapshots were rebuilt from the final pricing snapshot.
- The remaining gaps were that the transaction-scoped pricing refresh still trusted the pre-transaction `CartSnapshot` for catalog, coupon, and promotion inputs, persisted coupon/promotion winner fields could still remain on the pre-transaction result, and immutable affiliate commission snapshots still reused affiliate commission policy data loaded before the final transaction.
- A concurrent admin change to variant price, product visibility/active state, coupon validity, promotion outcome, or affiliate commission policy between the pre-transaction checkout refresh and final commit could therefore have persisted stale order money, stale winner identity, or stale commission ownership.

Production rule:
- The final storefront order-creation transaction must treat the pre-transaction cart snapshot as advisory only.
- Before `EnsureExpectedPricingMatches(...)`, `ValidatePaymentMethodOrderAmountLimits(...)`, `RefreshOrderAuthoritySnapshot(...)`, and `RefreshOrderItemAuthoritySnapshots(...)`, the flow must row-lock current storefront, shipping, payment-method, variant, and product authority; rebuild the checkout snapshot from that in-transaction catalog authority; rerun coupon and promotion authority from that refreshed snapshot; reload affiliate commission policy authority for the in-scope products inside the same transaction with row locking for existing policy rows; and then persist header, item, affiliate, and coupon/promotion winner snapshots from the same final result.
- Header-only refresh or unlocked catalog rereads are not sufficient because storefront refund, commission, analytics, customer order detail, and payment-method enforcement all depend on the persisted order snapshot remaining internally consistent with the final SQL authority.
- The current storefront implementation closes that remaining set-membership gap by running the final order commit inside `IsolationLevel.Serializable`, retrying bounded serialization/deadlock conflicts, and failing closed with `CHECKOUT_CONCURRENCY_CONFLICT` if the checkout cannot safely win the final authority race.

Relevant implementation:
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Pricing/StorefrontPricingService.cs`
- `PointOfSale.Core/Entities/OrderItem.cs`
- `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs`

Regression coverage:
- `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs`
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`
- Focused validation passed on 2026-05-11 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~CheckoutConcurrencyHardeningSourceTests -nologo -v minimal`.

### Tenant-scoped storefront tax and settings authority

Problem:
- Tenant-facing `Tax.*`, company-profile, and receipt settings were previously stored in shared global `SystemSettings` rows with no tenant scope.
- `SettingsController` wrote those shared rows, and `TaxCalculationService` read `Tax.Enable` and `Tax.Rate` from the same global table while treating them as tenant settings.
- Any tenant-admin write could therefore change every other tenant's checkout tax or customer-visible business identity.

Production rule:
- Tenant-facing settings that influence storefront pricing or customer-visible business identity must resolve through tenant-scoped SQL authority.
- Runtime reads and writes for those keys must use `SystemSettings.TenantId`; shared global rows are allowed only for explicit platform/super-admin control-plane settings.
- `SettingsController` and `TaxCalculationService` must resolve tenant-facing `SystemSettings` through explicit `TenantId` queries and must not depend on ambient EF query filters to find the same rows.
- Currency authority is intentionally split: POS Admin and storefront currency remain tenant-scoped and configurable, while Super Admin settlement/payout policy is a separate control-plane concern and must not overwrite tenant-facing currency truth.
- Tenant-facing numeric pricing or tax settings must be serialized in a culture-invariant canonical form so runtime locale cannot silently change checkout authority.
- Authority-shifting backfill migrations for those settings must preflight duplicate live global keys and fail with explicit reconciliation guidance before fan-out into tenant rows.
- Production rollout must include a backfill step that copies legacy global tenant-facing keys into tenant-owned rows so current tenant behavior is preserved while future writes stop crossing boundaries.

Relevant implementation:
- `PointOfSale.Core/Entities/SystemSetting.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.API/Controllers/SettingsController.cs`
- `PointOfSale.API/Services/Pricing/TaxCalculationService.cs`
- `PointOfSale.Infrastructure/Migrations/20260510210000_TenantScopedSystemSettingsAuthority.cs`

Regression coverage:
- `PointOfSale.Tests/Controllers/SettingsControllerCurrencyTests.cs`
- `PointOfSale.Tests/StorefrontPricingAuthorityTests.cs`
- `PointOfSale.Tests/Architecture/SettingsAuthorityIsolationArchitectureTests.cs`

### CommerceHub unresolved captured-payment retry containment

Problem:
- CommerceHub already persisted provider-success ambiguity into `CommerceHubUnresolvedPaymentWebhooks`, suspense accounting, and marketplace-order `OnHold` state when provider cash could not yet be matched to a paid local payment row.
- Before this hardening pass, the payment-initiation service still treated `PaymentStatus.Pending` plus `OrderStatus.OnHold` as retryable, and the customer order details page derived its "Continue to payment" action from that same generic status pair.
- That allowed a customer to reopen provider-session authority while captured-money ambiguity for the same order/provider was still unresolved.

Production rule:
- A marketplace order with unresolved provider-success evidence, suspense-routed captured cash, or pending suspense-clearance reconciliation is not a retryable customer payment state.
- `CommerceHubPaymentService` must fail closed before lock acquisition or provider handoff when unresolved captured-payment evidence still exists for the order/provider.
- Customer-facing order details must consume backend retry authority directly and show manual-review guidance instead of inferring retryability from generic `Pending`/`OnHold` state alone.

Relevant implementation:
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubCustomerOrderService.cs`
- `PointOfSale.API/DTOs/Customers/CustomerOrderDtos.cs`
- `PointOfSale.CommerceHub/src/pages/CustomerOrderDetailsPage.tsx`
- `PointOfSale.CommerceHub/src/types/customer-orders.ts`

Regression coverage:
- `PointOfSale.Tests/CommerceHub/CommerceHubPaymentServiceTests.cs`
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

### CommerceHub cart persistence authority scope and auth-hydrated bootstrap

Problem:
- CommerceHub cart persistence originally used a single browser key without binding the local snapshot to the current authenticated customer authority.
- Customer session bootstrap and cart bootstrap ran independently, so persisted cart state could hydrate before customer auth authority resolved.
- On a shared browser, a prior authenticated customer's local cart snapshot could be reused by the next authenticated customer. Earlier merge behavior also risked re-adding locally persisted authenticated quantities into the already authoritative server cart during reload.

Production rule:
- Browser-persisted CommerceHub cart state is intent only and must be scoped to `guest` or the exact authenticated CommerceHub customer.
- CommerceHub cart bootstrap must not run until customer auth hydration completes and the persisted cart scope matches current customer authority.
- When the authenticated customer changes or logs out, local CommerceHub cart persistence must be cleared or rebased before any authenticated bootstrap can occur.
- Authenticated server cart truth remains authoritative on bootstrap. Merging local state into an authenticated cart is allowed only for the explicit one-time guest-to-auth adoption path.

Relevant implementation:
- `PointOfSale.CommerceHub/src/store/cart-store.ts`
- `PointOfSale.CommerceHub/src/cart/useCommerceHubCartSync.tsx`
- `PointOfSale.CommerceHub/src/store/customer-auth-store.ts`

Regression coverage:
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

### CommerceHub durable customer session cutoff authority

Problem:
- CommerceHub customer browser auth is cookie-backed and validates `CustomerJwt` on every protected request.
- The prior authority gap was that logout and incident response depended primarily on exact-token JTI blacklist persistence. A copied token could remain valid until expiry if the exact-token revoke could not be durably stored.

Production rule:
- `CustomerJwt` validation must continue failing closed when revocation state cannot be verified.
- CommerceHub customer auth must enforce a durable per-account session cutoff through `CommerceHubCustomers.TokensValidAfterUtc` in addition to exact-token blacklist and governance checks.
- Issued customer tokens must carry both standard JWT `iat` and a precise session-issued-at claim so cutoff validation can distinguish same-second revoke/reissue sequences safely.
- Cutoff validation must reject tokens issued at or before the stored cutoff. Equality is not safe because a copied token minted in the same tick as the cutoff still belongs to the pre-revocation authority window.
- Logout must attempt exact-token blacklist persistence first, but if that durable write fails and the subject is known it must fail closed by advancing the per-account cutoff instead of relying on browser cookie clearing alone.
- Authenticated CommerceHub customers must have an explicit revoke-all-sessions path that advances the durable cutoff and expires the browser cookie.
- Governance suspension or ban remains a coarse immediate write and read authority boundary for all existing tokens.

Relevant implementation:
- `PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs`
- `PointOfSale.API/Services/CommerceHub/CustomerTokenRevocationService.cs`
- `PointOfSale.API/Controllers/CustomersAuthController.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.API/Security/CustomerSecurityConstants.cs`
- `PointOfSale.Core/Entities/CommerceHubCustomer.cs`
- `PointOfSale.Infrastructure/Migrations/20260511143000_S79_CommerceHubCustomerSessionCutoff.cs`

Regression coverage:
- `PointOfSale.Tests/Security/CustomerTokenRevocationServiceTests.cs`
- `PointOfSale.Tests/Security/CookieAuthSecurityTests.cs`
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

### CommerceHub auth-boundary recovery-cookie scrubbing

Problem:
- CommerceHub anonymous payment recovery intentionally persists a signed HttpOnly order-access cookie plus non-secret browser recovery context so a pending order can survive cart cleanup and page reloads.
- That continuity channel became a shared-browser leak because customer login/logout/revoke-all only cleared the auth cookie. A later user on the same tab/browser could inherit the previous customer's visible recovery CTA and still-present signed recovery cookie.

Production rule:
- Anonymous CommerceHub order recovery is continuity-only authority between auth boundaries, not durable cross-user browser state.
- Customer login, register, logout, and revoke-all-sessions must expire the `commercehub_order_access` recovery cookie.
- The recovery-cookie scrub is a server-owned boundary and must run even when the customer auth cookie is already expired, missing, or unreadable. Logout success must not be conditional on auth-cookie presence if that would leave `commercehub_order_access` behind.
- The CommerceHub SPA must clear visible `activePaymentRecoveryContext` on explicit logout and when a new interactive customer session is established so stale order identity is not advertised across users.
- As additional defense in depth, generic CommerceHub client session-clear paths should also clear `activePaymentRecoveryContext` so auth expiry, hydrate-time auth failure, or future logout callsites cannot keep showing stale order identity after customer authority is gone.
- Auth-expiry or hydration failure alone does not need to destroy anonymous recovery continuity; the mandatory scrub points are explicit auth-boundary changes where browser ownership may change.

Relevant implementation:
- `PointOfSale.API/Controllers/CustomersAuthController.cs`
- `PointOfSale.API/Security/CommerceHubOrderAccessCookie.cs`
- `PointOfSale.CommerceHub/src/store/customer-auth-store.ts`
- `PointOfSale.CommerceHub/src/components/layout/Header.tsx`

Regression coverage:
- `PointOfSale.Tests/Security/CookieAuthSecurityTests.cs`
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`
- Focused validation on 2026-05-11 also asserts logout still emits expiry cookies for both `commercehub_customer_access_token` and `commercehub_order_access` when the auth cookie is already absent.
- The 2026-05-12 follow-up hardening also asserts the CommerceHub auth store clears visible recovery context inside its generic `clearSession` path rather than relying only on the header logout caller.

### Storefront durable customer session cutoff precision

Problem:
- Storefront customer security paths already relied on `OnlineCustomers.TokensValidAfterUtc` as the durable fallback when blacklist persistence failed during password reset or session-revocation incidents.
- Storefront access tokens only carried second-level JWT `iat`, and middleware accepted `tokenIssuedAt == TokensValidAfterUtc`, so a copied token minted in the same cutoff second could survive the fallback authority boundary.

Production rule:
- Storefront-issued access tokens must carry both standard JWT `iat` and the precise `session_iat_ticks` claim across password login, refresh-token rotation, and social-login issuance paths.
- `StorefrontJwtBlacklistMiddleware` must resolve the precise claim before falling back to `iat`.
- Storefront cutoff validation must reject tokens issued at or before `TokensValidAfterUtc`.
- Legacy tokens without the precise claim may still fall back to second-level `iat`, but that fallback must fail closed for the whole cutoff second rather than preserving stale authority.

Relevant implementation:
- `PointOfSale.API/Services/Storefront/StorefrontAuthService.cs`
- `PointOfSale.API/Services/Storefront/StorefrontRefreshTokenService.cs`
- `PointOfSale.API/Services/Storefront/StorefrontSocialLoginService.cs`
- `PointOfSale.API/Middleware/StorefrontJwtBlacklistMiddleware.cs`

Regression coverage:
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

### CommerceHub cookie-authenticated customer authority on cart and public order recovery

Problem:
- CommerceHub customer browser auth is intentionally cookie-backed: `CustomersAuthController` writes the JWT into an HttpOnly cookie and returns an empty browser `AccessToken`.
- `CommerceHubCartController` and the public `CommerceHubOrdersController` recovery surface were still treating raw `Authorization` header presence as the signal that customer authentication should be evaluated.
- The CommerceHub SPA uses `withCredentials` cookies and does not inject a bearer header, so authenticated browser requests could be downgraded to guest/anonymous behavior even though the customer auth scheme would have succeeded.

Production rule:
- Browser-facing CommerceHub customer APIs must evaluate `CustomerSecurityConstants.AuthenticationScheme` directly and accept the platform's cookie-backed JWT contract.
- Cart bootstrap/save/clear/pricing must not fall back to guest solely because a browser request omitted a raw `Authorization` header.
- Public CommerceHub order recovery may require either authenticated customer ownership or a signed order access token, but authenticated customer ownership must be derived from the auth scheme itself, not from bearer-header presence.

Relevant implementation:
- `PointOfSale.API/Controllers/CustomersAuthController.cs`
- `PointOfSale.API/Controllers/CommerceHubCartController.cs`
- `PointOfSale.API/Controllers/CommerceHubOrdersController.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.CommerceHub/src/api/http-client.ts`

Regression coverage:
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

### CommerceHub critical order-detail repair sequencing

Problem:
- Before this hardening pass, CommerceHub order detail had SQL-backed projection repair available, but the controller could still return `ORDER_NOT_FOUND` before invoking the repair path when the Mongo document was missing.
- The previous optimization used the Mongo projection to obtain `OrderNumber` for token validation, which made the self-heal guarantee depend on the projection already existing.

Production rule:
- Customer-critical CommerceHub order detail must not treat a missing Mongo projection as authoritative absence.
- Any projection-dependent `404`, stale response, or token/ownership check that requires projected fields must either read SQL authority directly or invoke `ICommerceHubOrderProjectionRepairService` first.
- If the controller needs `OrderNumber` or ownership fields to validate access, it must obtain them from SQL authority or from a repaired projection, not from an unrepaired best-effort Mongo read.
- Signed order-access token fallback from the HttpOnly recovery cookie is compatible with this rule, but the token must still be validated against SQL-loaded order metadata before the controller trusts Mongo projection reads.
- Regression coverage must assert sequencing, not just the textual presence of `EnsureOrderProjectedAsync`.

Relevant implementation:
- `PointOfSale.API/Controllers/CommerceHubOrdersController.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubOrderProjectionRepairService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubProjectionReconciliationService.cs`

Regression coverage:
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

### CommerceHub payment initiation order authority

Problem:
- `POST /api/commercehub/payments/initiate` was callable without proving authenticated customer ownership or possession of the signed marketplace-order access token.
- The payment service could resolve an unresolved marketplace order by id, order number, or payment reference and create or reuse provider-session authority for any caller who knew that identifier.

Production rule:
- CommerceHub payment initiation is not a public lookup surface.
- Creating or reusing an online payment session must require one of:
  - authenticated CommerceHub customer ownership of the marketplace order
  - signed `OrderAccessToken` validation bound to the exact marketplace order id and order number
- The controller may remain anonymous for token-based recovery, but it must fail closed when neither authenticated customer authority nor a signed order token is present. Browser recovery may source that signed token from the explicit request payload or from the server-issued HttpOnly recovery cookie, but both paths must converge on the same fail-closed access gate.
- The service must re-check the same authority boundary after SQL order resolution and before payment-session reuse, retry recovery, or new provider-session creation returns any order/payment metadata.

Relevant implementation:
- `PointOfSale.API/Controllers/CommerceHubPaymentsController.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubOrderAccessService.cs`
- `PointOfSale.API/DTOs/CommerceHub/CommerceHubOrderDtos.cs`

Regression coverage:
- `PointOfSale.Tests/CommerceHub/CommerceHubPaymentServiceTests.cs`
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

### CommerceHub checkout payment-pending lifecycle visibility

Problem:
- CommerceHub online checkout cleared the local cart and rendered a terminal success state even though backend authority explicitly returned `PaymentSession.Status = requires_initiation`.
- Customers had no server-authorized continue-payment path from the checkout success screen, and the order-details surface did not expose the authoritative payment-method code needed to resume the exact provider later.
- Stripe initiation returns a client-secret handoff rather than a redirect URL, but the frontend treated missing `paymentUrl` as initiation failure.

Production rule:
- CommerceHub online checkout must distinguish `order created` from `payment completed`.
- When checkout returns a non-terminal online payment state such as `requires_initiation`, the frontend must:
  - keep the order in an explicit payment-pending state
  - call the payment-initiation API immediately when possible
  - branch by authoritative handoff type instead of assuming every provider redirects immediately
  - redirect to the provider when the payment URL is available
  - render a secure embedded client-secret flow when the provider requires it
  - preserve a retry/continue-payment action on checkout success, authenticated customer order details, and the dedicated public recovery page when initiation does not complete immediately
- CommerceHub checkout authority must validate the exact requested online provider against each store's enabled payment-method configuration. A store with some `SupportsOnline` method is not sufficient proof that `PAYMOB` and `STRIPE` are interchangeable.
- Local cart cleanup after checkout is allowed only after SQL order creation commits and only if the UI still retains authoritative order/payment recovery context through the HttpOnly recovery cookie plus non-secret browser recovery state.

Relevant implementation:
- `PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx`
- `PointOfSale.CommerceHub/src/pages/CustomerOrderDetailsPage.tsx`
- `PointOfSale.CommerceHub/src/pages/OrderPaymentRecoveryPage.tsx`
- `PointOfSale.CommerceHub/src/features/checkout/activePaymentRecoveryContext.ts`
- `PointOfSale.CommerceHub/src/payments/EmbeddedStripePaymentForm.tsx`
- `PointOfSale.CommerceHub/src/checkout/PaymentMethodSelector.tsx`
- `PointOfSale.CommerceHub/src/services/checkout-service.ts`
- `PointOfSale.CommerceHub/src/services/order-recovery-service.ts`
- `PointOfSale.CommerceHub/src/types/checkout.ts`
- `PointOfSale.CommerceHub/src/api/endpoints.ts`
- `PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`

Regression coverage:
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

### CommerceHub payment-method capability authority now fails closed and drives selector state from pricing authority

Problem:
- The CommerceHub checkout UI previously hardcoded `COD`, `PAYMOB`, and `STRIPE` as user-selectable options instead of binding selection to backend payment-method authority.
- `CommerceHubCheckoutService.ValidateRequestedPaymentMethod` previously treated `methodsForTenant.Count == 0` as support for both COD and online providers.
- That combination allowed order and inventory authority to be created for stores that had not enabled the requested provider, and later payment initiation could still proceed from platform/global provider configuration.

Production rule:
- CommerceHub checkout must fail closed when any store in the order lacks an enabled payment-method row for the requested provider.
- Exact provider validation is not satisfied by "some online method exists" and it is also not satisfied by "no rows exist, therefore allow all."
- Zero-config payment-method state means the provider is unavailable until the store explicitly enables it.
- The customer-facing CommerceHub payment-method selector should be driven by backend capability authority for the exact set of stores being repriced. A local provider list may exist only as presentation metadata layered on top of backend availability, not as selection authority.

Hardening note (2026-05-10):
- `CommerceHubPaymentMethodAuthority` now computes per-provider availability across the exact tenant set in the cart.
- CommerceHub cart pricing now returns `PaymentMethods` capability data with the pricing/shipping authority snapshot.
- `ValidateRequestedPaymentMethod` now fails closed through the shared authority helper instead of permitting zero-config stores.
- `CheckoutPage` and `PaymentMethodSelector` now consume backend capability data and disable unavailable methods instead of assuming a hardcoded provider set is valid for every multi-store order.

Relevant implementation:
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentMethodAuthority.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs`
- `PointOfSale.API/DTOs/CommerceHub/CommerceHubCartDtos.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx`
- `PointOfSale.CommerceHub/src/checkout/PaymentMethodSelector.tsx`

Regression coverage:
- `PointOfSale.Tests/CommerceHub/CommerceHubPaymentMethodAuthorityTests.cs`
- `PointOfSale.Tests/CommerceHub/CommerceHubCheckoutFinancialControlTests.cs`
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

### Shipment-to-fulfillment authority convergence

Problem:
- Admin shipment updates, generic shipment recovery, and carrier tracking/webhook paths could mark a shipment as delivered and publish fulfillment-delivered truth while SQL `Order.FulfillmentStatus` remained behind.
- COD collection and affiliate commission approval reload SQL fulfillment authority, so the system could show delivered in shipment/order projections while accounting and affiliate settlement rejected the same order as not physically delivered.

Production rule:
- Shipment services may own shipment rows and shipment/order projection events, but they do not own fulfillment lifecycle events.
- Any shipment progress that maps to `Confirmed`, `Packed`, `OutForDelivery`, or `Delivered` must converge through `IOrderFulfillmentService.SynchronizeFromShipmentAsync`.
- `FulfillmentDeliveredEvent` must only be emitted by `IOrderFulfillmentService` after the authorized SQL `FulfillmentStatus` transition succeeds.
- COD delivered-event handling must throw when `MarkCodDeliveredAsync` fails so MassTransit retry/recovery is preserved instead of silently dropping the money-recognition attempt.

Relevant implementation:
- `PointOfSale.Core/Interfaces/Fulfillment/IOrderFulfillmentService.cs`
- `PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs`
- `PointOfSale.API/Services/Admin/AdminShipmentService.cs`
- `PointOfSale.API/Services/Shipping/ShipmentService.cs`
- `PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs`
- `PointOfSale.Infrastructure/Consumers/OrderDeliveredEventConsumer.cs`

Regression coverage:
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`
- `PointOfSale.Tests/Architecture/FulfillmentOwnershipArchitectureTests.cs`
- `PointOfSale.Tests/Shipping/ShipmentExternalRegistrationSafetyTests.cs`
- `PointOfSale.Tests/Security/ShippingWebhookTrustPipelineTests.cs`

### Storefront critical order-detail and tracking repair sequencing

Problem:
- Storefront order detail and tracking surfaces had SQL-backed repair available, but `StorefrontOrdersReadController` still performed the first Mongo read before invoking the repair path.
- If the Mongo projection was missing, the controller could return `ORDER_NOT_FOUND` before the SQL-backed repair ever ran, reopening the exact stale-authority failure the earlier hardening pass was meant to close.

Production rule:
- Customer-critical storefront order detail, authenticated tracking, guest tracking, and order-by-number reads must not treat a missing Mongo document as authoritative absence.
- These endpoints must resolve minimal SQL order authority first, then trigger `IStorefrontOrderProjectionRepairService` before any projection-derived `404`, guest-session check, or ownership check that would otherwise depend on Mongo already existing.
- If SQL authority proves the order exists but Mongo still cannot be served after repair, the endpoint must fail closed as temporary unavailability rather than return a false `404`.

Hardening note (2026-05-10):
- `StorefrontOrdersReadController` now resolves SQL authority snapshots for order id and order number flows, triggers repair before Mongo-backed order detail/tracking reads, and returns `503` when SQL authority exists but the projection remains unavailable after repair.
- `StorefrontAccountController` account detail reads now apply the same SQL-first / fail-closed pattern.
- `StorefrontAccountController` paginated order history now also resolves the authoritative SQL count and requested order ids first, invokes `EnsureCustomerOrdersProjectedPageAsync(...)` for that exact page range, serves Mongo documents by those SQL ids, and fails closed if the requested page still cannot be fully projected after repair.
- `CrossSystemCriticalRiskHardeningTests` now asserts sequencing, not just the textual presence of repair calls.

Relevant implementation:
- `PointOfSale.API/Controllers/StorefrontOrdersReadController.cs`
- `PointOfSale.API/Controllers/StorefrontAccountController.cs`
- `PointOfSale.API/Services/Storefront/StorefrontOrderProjectionRepairService.cs`

Regression coverage:
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

### Storefront late-success and provider-mismatch suspense routing

Problem:
- Storefront payment lifecycle quarantine states such as late gateway success after local expiry/cancel or provider amount/currency mismatch were safe in the sense that they stopped automatic completion, but they did not always create durable accounting truth for externally confirmed customer money.

Production rule:
- If provider cash is confirmed while the local storefront payment cannot safely complete, the system must queue tenant-scoped unresolved-payment suspense accounting through the central accounting outbox.
- Webhook/manual-review records must be marked as routed to storefront suspense so recovery tooling and operators can distinguish "charged but not locally recognized" from ordinary manual review.
- Inventory/order/coupon compensation remains blocked behind the existing lifecycle logic until suspense is cleared by reconciliation or follow-up compensation.

Relevant implementation:
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`
- `PointOfSale.API/Services/Payments/StorefrontSuspenseRecoveryPolicy.cs`
- `PointOfSale.API/Services/Payments/PaymentService.cs`
- `PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs`
- `PointOfSale.API/Controllers/FawryWebhookController.cs`
- `PointOfSale.Infrastructure/Accounting/AccountingOutboxService.cs`

Regression coverage:
- `PointOfSale.Tests/Integration/InventoryReservationLifecycleIntegrationTests.cs`

### Storefront canonical payment-status quarantine visibility

Problem:
- Storefront payment lifecycle quarantine paths now correctly hold late-success and provider-mismatch captures in suspense/manual-review authority, but the customer-facing canonical payment-status contract previously collapsed those states into terminal `FAILED`.
- `CanonicalPaymentStatusResolver` used the same customer signal for safe failure and for unresolved captured-money states such as `PaymentIntentStatus.Suspicious`, `PaymentIntentStatus.AmountMismatch`, and `PaymentStatus.AmountMismatch`.

Production rule:
- If provider cash may already have been captured, the storefront customer contract must remain pending/manual-review/reconciliation-visible until local reconciliation or explicit refund/non-execution proof establishes safe terminal failure.
- Storefront payment-status APIs must not reuse the same `FAILED` customer signal for both "safe to retry, no money recognized" and "money may already exist but is quarantined in suspense/manual review".
- Customer-facing recovery UI must suppress return-to-checkout or retry-payment guidance when the backend authority state is late-success quarantine, amount/currency mismatch quarantine, or another suspense-routed manual-review state.

Hardening note (2026-05-10):
- `CanonicalPaymentStatusResolver` now emits `MANUAL_REVIEW` for quarantined captured-money states instead of `FAILED`.
- Storefront pending and confirmation pages now render explicit manual-review messaging, keep checkout recovery context intact, stop retry-safe failure cleanup, and tell the customer to refresh or track the order instead of retrying payment from checkout.

Hardening note (2026-05-11):
- A resolver precedence regression had reopened `PaymentStatus.AmountMismatch` as retry-safe `FAILED` even though the documented contract and tests required `MANUAL_REVIEW`.
- `CanonicalPaymentStatusResolver` now again treats `PaymentStatus.AmountMismatch` as quarantined manual-review authority, and focused resolver coverage passed on 2026-05-11 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~CanonicalPaymentStatusResolverTests -nologo -v minimal`.

Relevant implementation:
- `PointOfSale.API/Services/Payments/CanonicalPaymentStatusResolver.cs`
- `PointOfSale.API/DTOs/Payments/PaymentDtos.cs`
- `PointOfSale.API/Services/Payments/PaymentService.cs`
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`
- `PointOfSale.Storefront/src/features/checkout/paymentFlow.ts`
- `PointOfSale.Storefront/src/pages/CheckoutPendingPage.tsx`
- `PointOfSale.Storefront/src/pages/OrderConfirmationPage.tsx`

Regression coverage:
- `PointOfSale.Tests/Payments/CanonicalPaymentStatusResolverTests.cs`
- `PointOfSale.Tests/Security/StorefrontProductionBlockerFixTests.cs`
- `PointOfSale.Tests/Integration/InventoryReservationLifecycleIntegrationTests.cs`

### CommerceHub unresolved provider-success suspense convergence

Problem:
- A CommerceHub provider-success webhook that was already routed to suspense could later match a paid local payment row, but the earlier hardening pass still left a gap where the suspense could remain uncleared and the marketplace order could remain `OnHold`.

Production rule:
- Reconciliation must search unresolved/suspense-routed provider-success webhooks for later matching paid `CommerceHubPayment` rows.
- When a match is found, the system must reconstruct affected-tenant suspense allocations, enqueue suspense-clearance accounting outbox intent for every affected tenant, release the marketplace hold under the order lifecycle boundary, and only then mark the webhook `ResolvedToPayment`.
- If tenant suspense scope cannot be reconstructed, the webhook must remain explicitly blocked in `SuspenseMatchedPaymentPendingClearance`.

Relevant implementation:
- `PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationJob.cs`
- `PointOfSale.Infrastructure/Accounting/AccountingOutboxService.cs`
- `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs`
- `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs`

Regression coverage:
- `PointOfSale.Tests/CommerceHub/CommerceHubFinanceServiceTests.cs`

## Fixed Payment Recovery Authority Gaps

### CHECKOUT-CON-09 payment replay after checkout finalization

Problem:
- The Paymob legacy webhook fallback could create a pending `CommerceHubPayment` row from a provider/order reference when a callback arrived without an existing payment row.
- That fallback existed for historical callback compatibility, but without a final SQL authority recomposition it could recreate stale payment authority after checkout finalization, payment collection, reservation consumption, or reservation loss.

Production rule:
- Legacy provider callback recovery is not payment authority.
- Before a fallback creates a CommerceHub payment row, it must reload current SQL marketplace order truth, reject already-collected or non-retryable order/payment state, and prove active SQL reservation authority.
- If the callback reports external money but SQL authority is finalized or missing, the system must persist unresolved/suspense evidence for reconciliation rather than recreating a payment lane.

Implemented hardening:
- `CommerceHubPaymentService.CreateLegacyPaymobPaymentAsync(...)` now calls `RecomposeLegacyPaymobPaymentCreationAuthorityAsync(...)` before `new CommerceHubPayment`.
- The helper clears stale tracking, resolves the current marketplace order, calls `EnsureInitiationAllowedAsync(...)`, and calls `IInventoryConsistencyService.EnsureMarketplaceOrderReservationsActiveAsync(...)`.
- Already-paid, non-retryable, or reservation-lost orders now return `null` from the legacy fallback, so the webhook path continues through unresolved evidence handling instead of minting payment authority.

Current evidence:
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.Tests/CommerceHub/CommerceHubPaymentServiceTests.cs`
- `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs`
- Focused validation passed on 2026-05-20 with CommerceHub payment service tests, checkout authority source tests, payment recovery/reservation/payment-intent guard slices, `dotnet build PointOfSale.sln`, and STOCK/PRICE/VAR smoke slices.
- Fawry webhook reliability tests were attempted but the local PostgreSQL schema was behind the EF model (`Orders.FinancialPostedAtUtc` missing), so that slice could not validate until schema migration is applied.

### CommerceHub pending-payment recovery continuity and anonymous recovery contract

Problem:
- CommerceHub checkout already created authoritative marketplace orders and generated signed order-access tokens for non-COD payments, but the SPA previously used that token only for the immediate post-checkout initiation call and then cleared the local cart.
- The anonymous `api/commercehub/orders/{id}` recovery surface also lacked the backend-derived retry/manual-review contract already used by authenticated customer order detail.
- The result was a refresh/navigation continuity gap: a browser could lose the only self-service path back to the same authoritative pending-payment order, and any future public recovery UI would have been forced to infer retryability from generic states instead of backend truth.

Production rule:
- Non-COD CommerceHub checkout must durably reissue signed order-access authority into a server-issued HttpOnly `/api/commercehub` recovery cookie before the browser clears transient state.
- Browser session storage may retain only non-secret active-order identity for reload-safe routing. Secret order-access authority must stay in the HttpOnly cookie.
- Anonymous CommerceHub order recovery must expose backend-derived retry/manual-review authority directly through the API contract.
- Public recovery pages, authenticated customer order details, and checkout recovery affordances must continue the same authoritative order and payment session; they must never recreate payment authority from cart state, persist recovery context from generic `Pending` / `OnHold` alone, or infer retryability when backend recovery authority is blocked by provider-session reconciliation fences.

Hardening note (2026-05-11):
- `CommerceHubCheckoutController` now stores the signed order-access token in `CommerceHubOrderAccessCookie` and strips the raw token from the JSON response after the cookie is issued.
- `CommerceHubPaymentsController` and `CommerceHubOrdersController` now fall back to that cookie for anonymous recovery and refresh the cookie on successful token-based access.
- `CommerceHubPaymentRecoveryAuthorityService` centralizes the `CanRetryPayment` / `PaymentRecoveryMessage` decision so authenticated and anonymous order-detail surfaces use the same backend authority.
- The 2026-05-11 follow-up hardening extracted `CommerceHubPaymentSessionRecoveryAuthority` so both initiation and recovery evaluate the same provider-session persistence-gap evidence, including unresolved captured-payment/manual-review states, `PROVIDER_SESSION_REINIT_FENCE`, `PROVIDER_SESSION_OUTCOME_UNKNOWN`, accepted provider identifiers with missing handoff data, and initiation-log evidence that a provider session may already exist.
- `CommerceHubOrderDetailsDto` now carries that retry/manual-review contract, `CustomerOrderDetailsPage` and `OrderPaymentRecoveryPage` now persist active recovery context only when backend-derived recovery authority exists, and `PointOfSale.CommerceHub` ships a dedicated `OrderPaymentRecoveryPage` plus a checkout empty-state recovery link.

Relevant implementation:
- `PointOfSale.API/Controllers/CommerceHubCheckoutController.cs`
- `PointOfSale.API/Controllers/CommerceHubPaymentsController.cs`
- `PointOfSale.API/Controllers/CommerceHubOrdersController.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentRecoveryAuthorityService.cs`
- `PointOfSale.API/Security/CommerceHubOrderAccessCookie.cs`
- `PointOfSale.API/DTOs/CommerceHub/CommerceHubOrderDtos.cs`
- `PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx`
- `PointOfSale.CommerceHub/src/pages/CustomerOrderDetailsPage.tsx`
- `PointOfSale.CommerceHub/src/pages/OrderPaymentRecoveryPage.tsx`
- `PointOfSale.CommerceHub/src/features/checkout/activePaymentRecoveryContext.ts`
- `PointOfSale.CommerceHub/src/services/order-recovery-service.ts`

Regression coverage:
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`
- `PointOfSale.Tests/CommerceHub/CommerceHubPaymentRecoveryAuthorityServiceTests.cs`
- Compile validation passed on 2026-05-11 with `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore -nologo -v minimal -m:1`.
- Frontend production build passed on 2026-05-11 with `npm.cmd run build` in `PointOfSale.CommerceHub`.

### CommerceHub refund reconciliation recovery from persisted evidence

Problem:
- A manual CommerceHub refund whose external transfer was already confirmed could enter `ReconciliationRequired`, but the recovery path did not yet safely converge local refund, ledger, and status truth from persisted execution evidence.

Production rule:
- Recovery must scan `ReconciliationRequired` manual-transfer refunds, reload the authority graph inside the refund/order/payment/financial workflow boundaries, and complete local recognition from persisted execution evidence without re-authorizing outward money.
- Recovery must not mutate immutable refund reversal history fields in place. If persisted immutable refund breakdown values are inconsistent, recovery must build a normalized refund snapshot for ledger/state verification while only mutating the allowed refund lifecycle fields on the tracked refund row.
- Successful recovery must move the refund to `Completed`, move the request to `Paid`, clear failure state, publish any resulting order/payment status events, and append recovery audit/financial operation evidence.

Relevant implementation:
- `PointOfSale.API/Services/CommerceHub/RefundExecutionService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubRefundRecoveryHostedService.cs`
- `PointOfSale.API/Program.cs`

Regression coverage:
- `PointOfSale.Tests/CommerceHub/RefundExecutionServiceTests.cs`

### Storefront checkout write authority

Problem:
- Public storefront reads were guarded by POS Admin storefront publication state, but checkout writes could still create order, inventory, payment, and outbox authority after the storefront was disabled or put into maintenance.

Production rule:
- Durable idempotency replay of an already committed checkout may be returned without requiring the storefront to still be live, because no new authority is created.
- Any checkout request that would create new authority must read SQL `StoreConfigs.StorefrontStatus`.
- Only `live` permits new checkout authority.
- Missing config, non-live status, or uncertainty reading authority must fail closed.
- `CreateOrderAsync` must re-check storefront availability inside the order-creation transaction before inventory reservation or order insert.
- The transaction-scoped SQL authority read must lock the row:
  - PostgreSQL: `FOR UPDATE`
  - SQL Server: `UPDLOCK, ROWLOCK, HOLDLOCK`

Relevant implementation:
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Checkout/ICheckoutService.cs`
- `PointOfSale.API/Controllers/CheckoutController.cs`

Regression coverage:
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

### Storefront promotion pricing authority

Problem:
- Checkout and pricing preview could be configured to continue after promotion calculation failure by clearing promotion state and charging a no-promotion total.
- That fail-open path could desynchronize customer-visible Storefront pricing, POS/Admin promotion authority, payment capture amount, order totals, and accounting/affiliate consequences.

Production rule:
- Customer-visible checkout pricing must fail closed when promotion calculation fails.
- Promotion calculation failure must return `PROMOTION_CALCULATION_FAILED` and require pricing refresh/retry before order, payment, inventory reservation, affiliate attribution, or accounting authority is created.
- Historical persisted `PromotionCalculationFailed` fields remain for backward-compatible replay of already-created orders, but new checkout/preview authority must not silently downgrade promotions to zero.

Relevant implementation:
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Checkout/ICheckoutService.cs`
- `PointOfSale.Storefront/src/store/checkoutStore.ts`

Regression coverage:
- `PointOfSale.Tests/Architecture/CheckoutPromotionVisibilityTests.cs`
- `PointOfSale.Tests/CheckoutPromotionCorrectnessControllerTests.cs`

Hardening note (2026-05-11 follow-up):
- `ReconcileCheckoutDiscountAuthorityAsync(...)` now routes the real promotion-engine exception path back through fail-closed handling instead of clearing promotion fields and continuing checkout.
- `CheckoutPromotionVisibilityTests` was strengthened to inspect the live discount-authority method body so the catch branch itself must delegate back into fail-closed promotion handling.

### Storefront customer session authority

Problem:
- Storefront login rejected inactive customers, but already-issued JWTs could still authenticate cart and checkout requests.
- Checkout verified customer existence and tenant ownership, while cart verified tenant claim shape; neither path re-read current SQL customer active status before creating customer-owned business authority.

Production rule:
- A storefront JWT is authentication evidence only. Current SQL `OnlineCustomer` authority must still be checked before cart mutation or checkout authority creation.
- Authenticated cart, cart merge, checkout pricing, and checkout order paths must reject inactive or tenant-mismatched online customers before order, inventory, payment, affiliate, or cart write effects.
- Customer deactivation is an immediate write-authority boundary even if access tokens have not expired.

Relevant implementation:
- `PointOfSale.API/Controllers/StorefrontCartController.cs`
- `PointOfSale.API/Controllers/CheckoutController.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Checkout/ICheckoutService.cs`

Regression coverage:
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

### Storefront payment-method eligibility contract alignment

Problem:
- The Storefront payment step previously treated subtotal as the customer-visible min/max eligibility amount even though backend checkout enforces payment-method limits on final total after shipping, tax, discounts, and fees.
- That mismatch did not create financial corruption because backend checkout failed closed, but it still let the UI advertise a different authority contract than the API.

Production rule:
- When pricing preview is available, Storefront payment-method min/max gating must use the authoritative preview total, not subtotal.
- Before pricing preview exists, any subtotal-based payment-method availability is advisory only and must never be treated as the final eligibility contract.
- Backend checkout remains the final authority and must continue revalidating payment-method limits against authoritative final totals before reservation and order creation.

Relevant implementation:
- `PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.Storefront/src/types/checkout.ts`

Regression coverage:
- `PointOfSale.Tests/Architecture/StorefrontReliabilityUxSourceTests.cs`

### Frontend stale-state mutation authority containment

Problem:
- Storefront, POS, and admin clients can hold stale read models, cached carts, local storage, optimistic payment state, or outdated inventory/payment-method visibility.
- If backend APIs accepted those client values as authority, retries or stale clients could create incorrect payment totals, duplicate order/payment authority, oversold stock, false terminal payment state, or accounting/affiliate divergence.

Production rule:
- Frontend/admin/POS requests carry intent and stale-client assertions only.
- Backend services must recompute authoritative pricing, tax, discounts, stock availability, payment eligibility, and payment status from SQL/lifecycle authority before creating durable business state.
- Checkout payment-method amount limits must be validated against backend-calculated totals before inventory reservation and again inside the final order-creation transaction.
- Customer-visible Storefront payment-method min/max gating should use the authoritative preview total once pricing is available. Subtotal-only gating is advisory and must not be treated as the final eligibility contract because backend enforcement runs on final total.
- POS sale creation must recompute authoritative unit price/tax, validate payment splits server-side, lock SQL stock rows before decrement, and stage stock-sync outbox intent in the same authority workflow.
- Provider client-side outcomes and frontend polling must surface pending/unknown until backend canonical status, webhook evidence, or reconciliation produces terminal payment truth.
- Idempotent replay of committed responses is allowed only when it does not create new authority; cleanup paths must not delete or rewrite committed order/payment/inventory truth.

Hardening note (2026-05-10):
- Storefront `CheckoutService` now row-locks SQL `StoreConfigs`, `ShippingMethods`, and `TenantPaymentMethods` inside the final order transaction, recalculates pricing again from that locked authority snapshot, re-runs `EnsureExpectedPricingMatches(...)` and `ValidatePaymentMethodOrderAmountLimits(...)`, refreshes the persisted order header, and rebuilds `OrderItem` money plus immutable affiliate commission snapshots before inventory reservation continues.
- The 2026-05-11 audit confirmed the pre-transaction snapshot reuse gap is closed: the final transaction row-locks catalog authority, reruns coupon and promotion authority from the refreshed in-transaction snapshot, reloads affiliate commission policy authority for the in-scope products inside the same transaction, and persists coupon/promotion winner identity plus immutable affiliate commission snapshots from that same final authority before inventory reservation and commit continue.
- The 2026-05-11 follow-up closed the last set-membership race by promoting that final authority boundary to `IsolationLevel.Serializable` with bounded retry and fail-closed conflict surfacing.

Relevant implementation:
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Controllers/SalesController.cs`
- `PointOfSale.Infrastructure/Inventory/ProductVariantStockMutationSupport.cs`
- `PointOfSale.API/Services/Payments/PaymentRequestAuthorizationService.cs`
- `PointOfSale.API/Services/Payments/CanonicalPaymentStatusResolver.cs`
- `PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx`
- `PointOfSale.Storefront/src/services/paymentFlow.ts`

Regression coverage:
- `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs`
- `PointOfSale.Tests/Architecture/CrossSystemAuthorityRegressionGuardTests.cs`
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`
- `PointOfSale.Tests/Payments/PaymentRequestAuthorizationServiceTests.cs`

### Vendor repayment inward-cash recognition authority

Problem:
- CommerceHub vendor repayment approval required external proof but then used the outward-money readiness gate before recording platform cash inflow and vendor debt reduction.
- Unrelated outward-money ambiguity could therefore block local recognition of already-received vendor cash, leaving bank/provider truth ahead of SQL ledger and vendor debt truth.

Production rule:
- Recognition of already-received vendor repayment cash is inward external-cash recognition, not outward money movement.
- Approval must still require one-time external evidence, duplicate-reference/proof protection, pending-request ledger checks, serializable mutation, and financial safety verification.
- The approval path must not call `EnsureOutwardMoneyMovementAllowedAsync`; unresolved outward-transfer ambiguity should block new payouts/refunds, not ledger recognition of cash already received by the platform.

Relevant implementation:
- `PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPaymentsService.cs`

Regression coverage:
- `PointOfSale.Tests/CommerceHub/CommerceHubVendorPaymentsServiceTests.cs`
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

### Affiliate payout readiness authority

Problem:
- Affiliate payout readiness was checked before entering the serializable payout transaction, leaving a time-of-check/time-of-use window before withdrawal, commission paid lifecycle, payout request, and accounting authority committed.

Production rule:
- The public affiliate payout entrypoint must not run cross-system readiness before the serializable retry boundary.
- `ExecutePayoutCoreAsync` owns the payout authority boundary.
- The payout core sequence must be:
  1. Begin `IsolationLevel.Serializable`.
  2. Run `EnsureAffiliateOutwardMoneyMovementReadyAsync`.
  3. Validate available balance.
  4. Allocate external transfer evidence.
  5. Create withdrawal ledger entry.
  6. Create paid commission lifecycle entries.
  7. Post accounting and reconcile payout request.
  8. Commit.
- Serialization failures must retry the full authority sequence.
- Readiness failures must fail closed without creating payout mutation.

Relevant implementation:
- `PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs`

Regression coverage:
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

### Affiliate payout transfer evidence global cash-rail authority

Problem:
- The later model-sync migration had drifted `AffiliatePayoutTransferEvidence` uniqueness and payout-allocation idempotency back to tenant scope.
- That allowed one real external bank or wallet transfer reference to be recognized independently inside multiple store-tenant payout authorities before the legacy paid-request checks could observe the conflict.

Production rule:
- Affiliate payout transfer evidence is platform cash-rail proof, not per-tenant duplicate proof.
- `RailNamespace + NormalizedPaymentReference` must remain globally unique across affiliate payout evidence.
- Payout-allocation idempotency must remain global per payout reference, not tenant-scoped, because a single payout reference cannot safely authorize multiple local withdrawals in different store tenants.
- `AffiliateSettlementOrchestrator` must allocate or reuse the global evidence row inside the same serializable payout boundary before local withdrawal, paid commission lifecycle, payout-request reconciliation, and accounting writes are committed.
- Any migration that restores this stronger global uniqueness after a tenant-scoped drift window must first preflight duplicate live evidence/allocation rows and fail with explicit operator action, rather than blindly recreating the unique indexes over possibly conflicting proof.

Hardening note (2026-05-11):
- `RestoreGlobalAffiliatePayoutEvidenceAuthority` runs that duplicate preflight before dropping the tenant-scoped indexes and recreating the global uniqueness constraints. If duplicated proof already exists, rollout fails closed with an explicit reconciliation error instead of restoring the stronger boundary half-blind.
- The EF runtime configuration and `AffiliateSettlementOrchestrator` lookup must match the migration snapshot: evidence uniqueness and lookup are by `RailNamespace + NormalizedPaymentReference`, not by tenant plus rail/reference. Tenant scope belongs on allocation rows and downstream payout/accounting effects, not on duplicate external transfer proof.

Relevant implementation:
- `PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs`
- `PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliatePayoutTransferEvidenceConfiguration.cs`
- `PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliatePayoutTransferAllocationConfiguration.cs`
- `PointOfSale.Infrastructure/Migrations/20260510190000_RestoreGlobalAffiliatePayoutEvidenceAuthority.cs`

Regression coverage:
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

### Affiliate partial-refund commission settlement authority

Problem:
- Affiliate refund reversal authority is proportional per order item, but payout selection must not treat the existence of any reversal row as a full settlement blocker. That creates drift between available balance, payout eligibility, paid commission lifecycle rows, payout events, and accounting.

Production rule:
- Affiliate payout planning must calculate payable commission from SQL authority as `Approved - Reversed` per order item, matching `AffiliatePayoutService.GetAvailableBalanceAsync`.
- Partial reversals reduce `CommissionLedger.Paid` amount through the net payable remainder.
- Fully reversed order items are excluded from payout eligibility.
- Already paid order items remain excluded by the paid lifecycle idempotency key.
- If summed reversal base or commission exceeds the approved authority amount, payout fails closed with an explicit corruption error instead of creating paid rows.
- Exact FIFO payout matching must use net payable amounts, not original approved amounts.

Relevant implementation:
- `PointOfSale.Core/Entities/Affiliate/AffiliateCommissionLedger.cs`
- `PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs`
- `PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs`

Regression coverage:
- `PointOfSale.Tests/Affiliate/AffiliateSettlementTests.cs`
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

### Affiliate malformed marketplace backlog readiness authority

Problem:
- Affiliate payout readiness scanned ambiguous marketplace aggregate backlog, but the global scan still prefiltered outbox payloads by the literal `MarketplaceOrderId` marker.
- CommerceHub readiness had already removed that prefilter so malformed or wrapper-shaped critical payloads reached fail-closed ambiguity detection. The affiliate asymmetry could allow affiliate payouts while CommerceHub money movement correctly blocked on the same unresolved critical backlog.

Production rule:
- Affiliate payout readiness must scan unhealthy critical authoritative outbox, MassTransit bus outbox, and marketplace outbox rows without tenant or literal payload-marker prefilters before tenant-scoped backlog checks.
- `AffectedTenantScopeResolver.RequiresGlobalFailClosedForAmbiguousMarketplaceAggregate` owns scope ambiguity detection.
- Malformed critical marketplace payloads or payloads that cannot prove affected tenant scope must fail closed before affiliate withdrawal, paid commission lifecycle rows, payout-request reconciliation, or payout accounting are committed.
- Large global scans may be logged for operator visibility, but scan cost must not be reduced by filtering on `MarketplaceOrderId` or another optional payload marker.

Relevant implementation:
- `PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs`
- `PointOfSale.Core/Events/AffectedTenantScopeResolver.cs`

Regression coverage:
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

### CommerceHub outward-money readiness authority

Problem:
- CommerceHub outward-money readiness did not run the global ambiguous marketplace aggregate scan used by checkout/payment intake, so a failed multi-store aggregate whose base `TenantId` belonged to one vendor and whose payload lacked explicit affected-store scope could be skipped when authorizing another affected vendor.
- Some outward-money paths read readiness before the serializable transaction that recorded refund/payout authority, leaving a time-of-check/time-of-use window before refund processing claims, payout reservations, completed payout evidence, earning allocation, and ledger effects committed.
- Super Admin payout queue summaries also collapsed vendor payable totals across different currencies and the frontend rendered the mixed total as `EGP`, creating a false operator-visible payout number even though underlying vendor and ledger authority remained currency-scoped.

Production rule:
- `EnsureOutwardMoneyMovementAllowedAsync` must run `EnsureAmbiguousMarketplaceAggregateBackboneHealthyAsync` before tenant-prefiltered accounting, compensation, unresolved-cash, reconciliation, and integration checks.
- CommerceHub outward-money readiness must run inside the final serializable authority boundary for any path that creates payout authority, claims refund execution, recognizes completed payout evidence, allocates earnings, or posts ledger effects.
- Idempotent replay of already committed terminal state may return without reauthorizing new outward money because it does not create new authority.
- Completion of already externally handed-off `Processing` or `ReconciliationRequired` payouts remains a reconciliation action and must continue to use evidence checks rather than brand-new payout readiness or vendor-governance authorization.
- Failure of already externally handed-off payouts may release earnings only after durable non-execution proof, and that reconciliation path must not be blocked by governance changes that occurred after handoff.
- Super Admin completed-payout recognition records externally confirmed vendor money movement into SQL authority. It still needs final readiness, evidence uniqueness, allocation, and ledger verification, but vendor suspension/governance is not a valid reason to keep local financial truth behind the bank/provider outcome.
- Super Admin payout queue/read models must surface the same payout reconciliation blockers that the final readiness gate enforces. Subtracting `Pending`/`Processing`/`ReconciliationRequired` amounts from payable math is not enough if `ReconciliationRequired` or stale `Processing` still blocks every new outward-money mutation for that vendor.
- The queue contract itself must carry backend-derived execution authority (`CanRecordCompletedPayout`, block code, and block reason) so the frontend cannot drift by reconstructing enablement from payable math or partial risk-flag checks.
- Super Admin payout queue/read models must also surface pagination authority explicitly. If the API paginates, the frontend must consume `page`/`pageSize`/`totalCount`/`totalPages` and must not present first-page aggregates as if they represent the full payout queue.
- Global payout queue summary cards must come from backend-computed totals across the full queue, not from the currently rendered page slice.
- Global payout queue summary cards must also remain currency-scoped. Backend authority may expose a single aggregate total only when the full queue is homogeneous in one known currency; otherwise it must expose per-currency summaries and fail closed on a fabricated mixed-currency total.
- Current `EGP`-only settlement guards belong only to Super Admin and central outward-money execution surfaces. They must not be reused to coerce POS Admin, storefront cart/checkout, or tenant settings currency authority without an explicit FX authority model and approved product rule change.
- Super Admin payout completion UI must transmit immutable execution evidence required by backend authority. A human `TransferReference` alone is not sufficient to recognize completed external payout truth.

Relevant implementation:
- `PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialReadinessGate.cs`
- `PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutReadService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubRefundService.cs`
- `PointOfSale.API/SuperAdmin/DTOs/SuperAdminPayoutDtos.cs`
- `PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutsService.cs`
- `PointOfSale.Web/src/super-admin/payouts/payoutsService.ts`
- `PointOfSale.Web/src/super-admin/payouts/PayoutsPage.tsx`

Regression coverage:
- `PointOfSale.Tests/CommerceHub/CommerceHubVendorPayoutReadServiceTests.cs`
- `PointOfSale.Tests/CommerceHub/SuperAdminPayoutsServiceTests.cs`
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

### CommerceHub Paymob partial session authority

Problem:
- Paymob order creation returns a reusable external order ID before payment-key creation returns the customer-facing session token.
- A failure after Paymob order acceptance but before payment-key materialization must not be downgraded into a normal local initiation failure, because a retry could create a second provider order for the same local payment authority.

Production rule:
- Persist the durable `PROVIDER_SESSION_REINIT_FENCE` before any external provider call.
- Run deterministic local Paymob payment-key prerequisites before creating the Paymob order.
- After Paymob returns an order ID, store that ID on local payment authority immediately.
- If payment-key creation fails, times out, or returns an untrustworthy response after order acceptance, keep the payment pending with `PROVIDER_SESSION_OUTCOME_UNKNOWN` and the accepted Paymob order ID.
- Retrying must recover by creating a payment key for the accepted Paymob order; it must not create a replacement provider order while the accepted order remains unresolved.

Relevant implementation:
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`

Regression coverage:
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

### Authoritative event and direct mutation regression guardrails

Problem:
- The remaining cross-system audit risk was future regression: new `ExecuteUpdate`, raw SQL, or direct MassTransit publish paths could bypass EF lifecycle/tenant guards or central authoritative event validation.
- `ProviderTrustConsumer` published `ProviderTrustUpdatedEvent` through `ConsumeContext.Publish` after saving the provider-trust aggregate, bypassing the central event publisher's aggregate ordering and tenant-scope validation path.

Production rule:
- Authoritative integration events must be staged through `IEventPublisher` before the source mutation commit.
- Authoritative event contracts must prove aggregate ordering. `ProviderTrustUpdatedEvent` uses `ProviderProfileId` as its authoritative aggregate.
- Event types that carry affected-tenant arrays must implement `IAffectedTenantScopedIntegrationEvent`.
- API and infrastructure application/business code must not inject `IPublishEndpoint`, `IBus`, or `ISendEndpointProvider`, and must not publish business-truth events directly through raw MassTransit publishers.
- Raw MassTransit publishing is reserved for reviewed event-bus/outbox infrastructure: `MassTransitEventPublisher`, `MarketplaceEventOutboxProcessor`, `StockSyncOutboxProcessor`, and `OutboxPublisherJob`.
- New direct SQL or EF bulk mutation files must be explicitly reviewed and allowlisted in architecture tests before merge.

Relevant implementation:
- `PointOfSale.Core/Events/IntegrationEventAuthority.cs`
- `PointOfSale.Core/Events/ProviderTrustUpdatedEvent.cs`
- `PointOfSale.Infrastructure/EventBus/MassTransitEventPublisher.cs`
- `PointOfSale.Infrastructure/EventBus/InMemoryEventPublisher.cs`
- `PointOfSale.Infrastructure/Eventing/MarketplaceEventOutboxProcessor.cs`
- `PointOfSale.Infrastructure/Eventing/StockSyncOutboxProcessor.cs`
- `PointOfSale.Infrastructure/Jobs/OutboxPublisherJob.cs`
- `PointOfSale.Infrastructure/Consumers/ProviderTrustConsumer.cs`

Regression coverage:
- `PointOfSale.Tests/Architecture/CrossSystemAuthorityRegressionGuardTests.cs`
- `PointOfSale.Tests/Architecture/EventOutboxConsistencyArchitectureTests.cs`

## Recently Hardened Boundary

### CHECKOUT-CON-12 partial financial lineage accounting authority

Hardened guarantee:
- Partial capture/refund/settlement lineage remains SQL-authoritative after checkout/payment finalization.
- Immediate post-commit accounting shortcuts for collection, refund, gateway settlement, and COD settlement now re-enter SQL authority before ledger mutation. They clear stale EF tracking, open a lifecycle transaction, reload locked SQL order/refund or settlement-batch truth, reload the staged accounting outbox row, collapse already-processed replay, and only then post accounting and mark the outbox processed.
- Refund ledger context is rebuilt from the current SQL `OrderRefund`; settlement ledger input is derived from the current SQL `PaymentSettlementBatch`; collection ledger posting verifies current SQL payment status before posting.

Production rule:
- Provider settlement payloads, payout payloads, cached settlement state, stale reconciliation payloads, replayed callbacks, and stale accounting rebuild inputs are evidence only.
- Before capture, refund, settlement, payout, accounting, commission, reconciliation, or fulfillment-financial mutation, services must reload current SQL payment/order/refund/fulfillment/accounting truth under the appropriate lock or transaction and collapse stale replay before side effects.
- Durable accounting outbox/recovery may discover work from staged messages, but posting authority must come from SQL rows at mutation time.

Relevant implementation:
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`
- `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs`
- `PointOfSale.Infrastructure/Payments/CommerceHubPayoutService.cs`

Regression coverage:
- `PointOfSale.Tests/Architecture/FinancialIntegrityRiskHardeningTests.cs`
- Focused validation passed on 2026-05-20 with `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore -nologo -v minimal -m:1`, `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~FinancialIntegrityRiskHardeningTests -nologo -v minimal`, and a 68-test deterministic financial/refund/payout/refund-execution/ledger/checkout-concurrency slice.
- A broader adjacent integration slice was attempted but blocked by existing local schema/fixture drift, not by a newly observed CHECKOUT-CON-12 assertion failure.

### CHECKOUT-CON-11 delayed provider callback authority safety

Hardened guarantee:
- Provider callbacks, redirect returns, settlement payloads, cached payment state, and provider reconciliation payloads are external evidence only.
- Payment lifecycle success/failure handling now recomposes locked SQL payment/order truth before provider-driven mutation. Stale provider success cannot resurrect terminal failed/cancelled/refunded/mismatched SQL truth; stale provider failure/cancel cannot regress terminal SQL success/refund truth.
- CommerceHub provider webhook outcome application now uses a serializable SQL boundary and explicit provider-specific row locks for the current `CommerceHubPayment` and marketplace order aggregate before provider identity, amount, state, financial workflow, fulfillment, accounting, or order-state mutation.
- Duplicate or delayed callbacks that conflict with current SQL truth collapse to no-op replay or manual-review/reconciliation instead of recreating fulfillment eligibility, payout eligibility, accounting mutation, or payment lineage.

Production rule:
- External provider outcome signals may confirm or challenge an execution attempt, but they do not own internal payment/order/accounting truth.
- Before payment, capture, refund, fulfillment, payout, accounting, reconciliation, or order-state mutation, provider-driven paths must reload current SQL authority under the appropriate lock/transaction and reject or collapse stale authority before side effects.
- Rebuild/recovery tools must derive mutation authority from SQL rows. Provider history, redirect payloads, caches, sessions, and webhook replay data may only supply evidence for reconciliation.

Relevant implementation:
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentRecoveryAuthorityService.cs`
- `PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs`

Regression coverage:
- `PointOfSale.Tests/Architecture/FinancialIntegrityRiskHardeningTests.cs`
- `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs`
- `PointOfSale.Tests/CommerceHub/CommerceHubPaymentServiceTests.cs`

### CHECKOUT-CON-08 out-of-order checkout visibility safety

Hardened guarantee:
- Checkout visibility remains derivative across storefront, CommerceHub, affiliate, payment-method, shipping, coupon/promotion, cache/search/category/banner, and session/recovery surfaces.
- Coupon delete visibility now writes an inactive tombstone even when the previous Mongo projection is missing, so a newer V11 delete/disable event that arrives first still leaves a freshness fence that blocks a later stale V10 create/update replay.
- Existing CHECKOUT-CON-04 through CHECKOUT-CON-07 runtime paths continue to recompose SQL order, cart, pricing, promotion, affiliate, reservation, stock, payment, shipping, and fulfillment authority before checkout finalization, payment initiation/recovery, reusable provider-session recovery, fulfillment/delivery commit, or reservation reuse.
- Existing Tier 1 visibility consumers retain monotonic freshness/version filters, SQL reloads, `ProjectionEventFence`/`ProjectionConsumerEventFence`, `ProjectionAuthorityValidator`, `ProjectionTombstoneGuard`, `ProductSearchSqlAuthorityOverlay`, and `SqlTruthRebuildGuard` where migrated. Remaining paths are intentionally incremental and must not be treated as authority.

Production rule:
- Out-of-order projection delivery, delayed cache hydration, stale rebuilds, delayed marketplace/affiliate callbacks, stale checkout sessions, and recovery payloads may trigger visibility repair only. They must never overwrite newer SQL checkout, reservation, price, promotion, payment eligibility, fulfillment eligibility, or marketplace availability authority.
- Before checkout finalization, reservation reuse, payment recovery, fulfillment recovery, or checkout-session recovery, the service must re-read current PostgreSQL truth and fail closed when the derivative visibility is stale.

Relevant implementation:
- `PointOfSale.Infrastructure/Consumers/CouponProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/PromotionProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/PaymentMethodProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/ShippingMethodProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/StorefrontConfigProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/TenantCurrencyProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProductPolicyProjectionConsumer.cs`
- `PointOfSale.Infrastructure/ReadModels/Marketplace/MarketplaceReadModelProjectionServices.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs`

Regression coverage:
- `PointOfSale.Tests/Architecture/CheckoutPromotionVisibilityTests.cs`
- `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs`

### CHECKOUT-CON-06 affiliate checkout price authority

Hardened guarantee:
- Affiliate-facing storefront/search/category/banner/cache/session visibility remains derivative only.
- Storefront affiliate checkout finalization now revalidates affiliate identity authority inside the final serializable checkout transaction after transaction-scoped SQL cart/pricing/coupon/promotion/payment-method recomposition.
- The final transaction row-locks current affiliate account authority, promotion-link authority, and affiliate-user bridge authority before affiliate commission policy snapshots, payment-intent placeholder creation, inventory reservation commit, order creation, inline attribution creation, or order-item commission snapshot persistence.
- If an affiliate account becomes inactive/deleted, a promotion link becomes inactive/deleted, a link no longer belongs to the checkout tenant/product set, or the link no longer resolves to the same affiliate account, checkout fails closed with `AFFILIATE_AUTHORITY_STALE` or `AFFILIATE_PROMOTION_LINK_STALE`.

Production rule:
- Affiliate checkout payloads, cached totals, affiliate promo overlays, affiliate session snapshots, search/category/banner visibility, and commission projections are not financial checkout authority.
- Final affiliate checkout must compose money from SQL product/variant/coupon/promotion/tax/shipping/payment authority and compose affiliate economics from current SQL affiliate account/link/policy authority in the same final checkout transaction.
- Affiliate commission mutation remains subordinate to SQL order-item commission snapshots captured at checkout; stale affiliate projections must never mint, double-consume, or resurrect commission/payout authority.

Relevant implementation:
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs`

Regression coverage:
- `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs`

### Checkout payment initiation active-reservation authority

Hardened guarantee:
- Storefront payment-intent creation, Stripe payment initiation, Paymob payment initiation, checkout idempotency replay, and checkout payment-intent recovery now revalidate active SQL reservation authority before creating, resuming, or returning provider payment authority for checkout/cart orders.
- `CheckoutInventoryAuthorityGuard` locks SQL order/reservation/stock authority, verifies tenant-owned product/variant linkage, requires unexpired active reservations for stock-tracked non-backorder order lines, and fails closed with `CHECKOUT_INVENTORY_AUTHORITY_LOST` when reservation truth no longer covers the order.
- CommerceHub payment initiation now rechecks `EnsureMarketplaceOrderReservationsActiveAsync(...)` after acquiring the payment lock, not only before lock acquisition, so cleanup/release timing between the first read and provider session selection cannot become payment authority.

Production rule:
- Payment intent generation/replay/recovery is not inventory authority. Provider session creation and reusable-session replay must happen only after current PostgreSQL reservation and stock truth still prove the order owns the reserved quantity.
- Stale checkout sessions, stale carts, cached stock visibility, affiliate/CommerceHub/storefront visibility, and previous payment-intent replay payloads must fail closed rather than recreate payment side effects after reservation loss.
- Existing external provider outcomes that arrive after local reservation loss remain reconciliation/refund/manual-review concerns; they must not resurrect inventory reservation authority.

Relevant implementation:
- `PointOfSale.API/Services/Payments/CheckoutInventoryAuthorityGuard.cs`
- `PointOfSale.API/Services/Payments/PaymentService.cs`
- `PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`

Regression coverage:
- `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs`

### Storefront serializable checkout retry idempotency binding

Hardened guarantee:
- The final storefront checkout transaction now preserves the durable replay bind even when a prior serializable/deadlock retry cleared EF tracking.
- `CheckoutService.CreateOrderAsync(...)` no longer writes the retry-attempt bind directly onto a possibly detached `OrderRequestIdempotency` object. It routes the bind through `CheckoutRequestIdempotencyCoordinator.BindOrderCreatedForCommitAsync(...)` before the transaction `SaveChangesAsync()` and `CommitAsync()` boundary.
- The coordinator reuses a tracked claim row when present, or reloads the owned row by id under claim-token validation when tracking was cleared. The durable `OrderId` / `CompletedAtUtc` bind is therefore committed in the same SQL transaction as order, reservation, payment-intent, coupon/promotion, and outbox authority.

Production rule:
- Any checkout retry path that calls `ChangeTracker.Clear()` must reload or explicitly reattach the claimed `OrderRequestIdempotency` row, re-prove claim-token ownership, and persist the durable `OrderId` / `CompletedAtUtc` bind inside the same SQL transaction that commits order, reservation, payment-intent, coupon/promotion, and outbox authority.
- Post-commit replay payload persistence may enrich the stored response, but it must not be the first durable step that binds a successful checkout to its idempotency claim.
- Regression coverage must simulate the serialization-retry path and assert the idempotency row is durably bound before the final authority transaction commits after tracker reset.

Relevant implementation:
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Checkout/CheckoutRequestIdempotencyCoordinator.cs`

Current evidence:
- Focused checkout idempotency regression coverage passed on 2026-05-11 after the hardening update, including `CheckoutRequestIdempotencyCoordinatorTests`, `CheckoutConcurrencyHardeningSourceTests`, `VerifiedSafeFlowsArchitectureTests.StorefrontOrderCreation_MustKeepCheckoutAuthoritativeWithoutCreatingShipmentSideEffects`, and `CheckoutCouponCqrsConsistencyTests.CheckoutService_MustBindIdempotencyRecordToOrderBeforeTransactionCommit`.

### CHECKOUT-CON-07 marketplace reservation replay authority

Hardened guarantee:
- Marketplace reservations remain SQL-authoritative only. CommerceHub/session/projection/cart availability is derivative visibility until SQL order, reservation, stock, payment, and fulfillment authority recomposes successfully.
- `EnsureMarketplaceOrderReservationsActiveAsync(...)` now requires active unexpired SQL reservations to exactly match current SQL CommerceHub order-item authority by tenant, order item, and variant. Missing, expired, released, consumed, over-reserved, or foreign-owner active reservation rows fail closed.
- CommerceHub payment initiation still rechecks reservation authority after the payment lock, and now recomposes reservation authority again immediately before provider-session creation. Payment recovery also recomposes before returning a reusable provider session or reconstructing an incomplete provider session.
- Customer payment-retry authority now consults current SQL reservations, SQL catalog/variant activity, and backing stock reservation rows before advertising retryability. If reservation authority is gone, recovery returns manual restart guidance instead of stale payment authority.

Production rule:
- Duplicate marketplace checkout replay may return an already committed idempotent result, but it must not mint extra active reservations or hide over-reservation behind an "at least required quantity" check.
- Stale payment recovery, reusable provider sessions, delayed callbacks, and provider-session reconstruction are subordinate to exact active SQL reservation authority.
- Fulfillment/delivery commit may consume only active unexpired SQL reservations for the current order item, and already-applied duplicate fulfillment events may collapse idempotently without additional stock mutation.
- Rebuild/recovery/reconciliation paths may repair derivative visibility from SQL truth, but must never recreate reservation authority from Mongo, Redis, search, marketplace payloads, or stale checkout sessions.

Relevant implementation:
- `PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentRecoveryAuthorityService.cs`

Regression coverage:
- `PointOfSale.Tests/CommerceHub/InventoryConsistencyServiceTests.cs`
- `PointOfSale.Tests/CommerceHub/CommerceHubPaymentRecoveryAuthorityServiceTests.cs`
- `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs`

Validation:
- Passed focused CHECKOUT-CON-07 and adjacent CommerceHub payment/checkout slices on 2026-05-20.
- API build passed on 2026-05-20.
- PostgreSQL-backed STOCK/PRICE/VAR integration smoke slices were attempted but blocked by the configured PostgreSQL endpoint refusing connections; no repeated TestSprite/MCP retries were run.

### CommerceHub checkout final authority composition locks

Problem:
- CommerceHub checkout recomputed current listing/product/variant price, promo, shipping, payment-method, stock, and reservation truth from SQL/services before marketplace order creation, but the final composition reads for listings, variants, products, tenants, and payment methods did not explicitly row-lock those SQL authority rows.
- That left CommerceHub less strongly fenced than storefront checkout during concurrent catalog, variant-attribute, store-availability, or payment-method mutation.

Production rule:
- CommerceHub checkout sessions, cart JSON, marketplace listing projections, affiliate/marketplace state, and cached totals are derivative visibility only.
- The final CommerceHub order transaction must reload and lock SQL authority rows for CommerceHub listings, product variants, variant attributes, products, tenants, and tenant payment methods before promo validation, shipping recalculation, marketplace order snapshot creation, inventory reservation, event staging, and idempotency response persistence.
- Duplicate checkout submit must replay the persisted idempotency response without re-reserving inventory, re-consuming promo, or re-publishing marketplace order side effects.

Relevant implementation:
- `PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs`

Regression coverage:
- `PointOfSale.Tests/CommerceHub/CommerceHubCheckoutFinancialControlTests.cs`
- `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs`

### CHECKOUT-CON-05 marketplace checkout visibility authority

Hardened guarantee:
- CommerceHub marketplace availability remains derivative visibility only. Payment initiation/recovery and fulfillment commit must recompose current SQL reservation, stock, catalog, shipping, and payment truth before creating provider authority or decrementing inventory.
- `EnsureMarketplaceOrderReservationsActiveAsync(...)` now rejects stale marketplace checkout/payment replay unless each reservable online order item still has active, unexpired SQL reservations, SQL product/variant authority, active shipping authority, and backing `ProductVariantStock.ReservedQuantity` coverage.
- `CommitDeliveryAsync(...)` now rejects stale delivery/fulfillment replay unless SQL order/item/suborder authority still matches the request, payment authority permits fulfillment, catalog/shipping authority is still active, and the reservation being consumed is active and unexpired.
- Marketplace product read-model rebuild now explicitly declares SQL authority with `SqlTruthRebuildGuard` for stock/variant/pricing/media/tombstone overlays, preventing stale rebuild entrypoints from silently treating marketplace visibility as authority.

Production rule:
- CommerceHub availability payloads, cached marketplace availability, checkout sessions, affiliate/search/category visibility, and delayed marketplace projections are never inventory authority.
- Duplicate marketplace replay may reuse already-applied idempotent results, but it must not mint duplicate reservations, payment intents, stock deductions, fulfillment requests, shipment requests, or visibility propagation storms after SQL authority is lost.
- Rebuild/recovery/reconciliation flows that repair marketplace product availability must derive visibility from current PostgreSQL truth, not from cache, Mongo, CommerceHub payloads, session state, search, or affiliate visibility.

Relevant implementation:
- `PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.Infrastructure/ReadModels/Marketplace/MarketplaceReadModelProjectionServices.cs`

Regression coverage:
- `PointOfSale.Tests/CommerceHub/InventoryConsistencyServiceTests.cs`
- `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs`

## Documentation Updates

The mitigated risks are also recorded in:
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`

### GLOBAL-REL-CON storefront product projection writer governance

Problem:
- Storefront `ProductDocument` mutation was already SQL-recomposed on the hardened product create/update, lifecycle, and admin repair paths, but each caller still hand-rolled its own overlay/tombstone/upsert sequence.
- `ProductCreatedEventConsumer` and `ProductUpdatedEventConsumer` still exposed an optional `ApplicationDbContext`-less constructor path that could silently fall back to event-payload-driven Mongo mutation if a future registration, test harness, or repair tool instantiated the consumer without SQL context.

Production rule:
- Storefront product projection writes are derivative visibility only and must recompose current SQL authority every time.
- Product create/update lifecycle consumers and admin product rebuild tooling must route `ProductDocument` writes through the shared `IProductSqlAuthorityProjectionWriter`.
- Optional non-SQL product projection fallbacks are forbidden.

Implemented hardening:
- Added `ProductSqlAuthorityProjectionWriter` as the shared writer for SQL overlay, event-version stamping, freshness-aware tombstones, and Mongo upsert convergence.
- Removed the optional `ApplicationDbContext` fallback branch from `ProductCreatedEventConsumer` and `ProductUpdatedEventConsumer`.
- Routed `ProductVisibilityLifecycleProjectionConsumer` and `AdminSyncController` through the same shared writer and updated architecture tests to enforce that governance rule.

Current evidence:
- `PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs`
- `PointOfSale.Infrastructure/Consumers/ProductCreatedEventConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/ProductUpdatedEventConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/ProductVisibilityLifecycleProjectionConsumer.cs`
- `PointOfSale.API/Controllers/AdminSyncController.cs`
- `PointOfSale.Tests/Architecture/GlobalReliabilityInfrastructureExtractionTests.cs`
- `PointOfSale.Tests/Architecture/EventOutboxConsistencyArchitectureTests.cs`

### GLOBAL-REL-CON follow-up storefront projection completeness and guarded writer enforcement

Problem:
- The extracted shared storefront SQL overlay/writer preserved category/media/variant/stock/promotion/tombstone recomposition, but it still missed current SQL `ProductUnits` / `Units`.
- Storefront runtime continued to depend on `ProductDocument.DefaultUnit`, `ProductDocument.Units`, and simple-product purchasability semantics, so unit mutation replay or rebuild could drift even while the shared writer appeared hardened.
- `PromotionProjectionConsumer` still had one inspected direct full `ProductDocument` replacement path during promotion refresh, which could bypass shared writer governance and preserve stale in-memory unit/default-unit state.

Production rule:
- Storefront simple-product `DefaultUnit` and `Units` are authority-critical derived fields. Shared storefront writers, rebuilds, search overlays, and product-shaped visibility repairs must recompose them from current SQL `ProductUnits` + `Units` before mutating public visibility.
- A simple product with no current SQL default unit is non-purchasable on storefront-facing price helpers.
- Direct full `ProductDocument` replacements are forbidden outside `ProductReadRepository` internals, `IProductSqlAuthorityProjectionWriter`, or `IStorefrontProductProjectionMutationBoundary`; overlay-only refresh paths must use field-scoped updates.

Implemented hardening:
- `ProductSearchSqlAuthorityOverlay` now reloads `ProductUnits` with current SQL unit rows, rebuilds `ProductDocument.DefaultUnit` and `Units`, and also recomposes purchase price plus structured storefront metadata (`Features`, `Faqs`, `Tags`, `AiMetadata`) from SQL.
- `ProductDocument.StorefrontEffectivePrice` now fails closed to `0` when `DefaultUnit` is missing, while `StorefrontVisiblePrice` preserves variant-first storefront/search visibility pricing.
- Storefront detail/list/category/homepage/cart and CommerceHub detail fallback pricing now consume the storefront unit-aware helper rather than legacy product-level price on simple products.
- `PromotionProjectionConsumer.RefreshProductPromotionProjectionAsync(...)` now uses overlay-only Mongo updates instead of full `ProductDocument` replacement.
- Focused architecture/source-guard tests now fail if the shared overlay stops recomposing units/metadata, if storefront simple-product paths regress to legacy price helpers, if product promotion refresh reintroduces `_productCollection.ReplaceOneAsync(...)`, or if runtime code starts calling `IProductReadRepository.UpsertAsync` / `TombstoneIfFreshAsync` outside `StorefrontProductProjectionMutationBoundary`.

Current evidence:
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs`
- `PointOfSale.Infrastructure/Consumers/PromotionProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Documents/ProductSearchDocumentMapper.cs`
- `PointOfSale.API/Services/Storefront/StorefrontProductService.cs`
- `PointOfSale.API/Services/Storefront/ProductSearchService.cs`
- `PointOfSale.API/Services/Storefront/StorefrontHomeService.cs`
- `PointOfSale.API/Services/Cart/CartService.cs`
- `PointOfSale.Tests/Infrastructure/ProductSqlAuthorityProjectionWriterTests.cs`
- `PointOfSale.Tests/Architecture/GlobalReliabilityInfrastructureExtractionTests.cs`

## Follow-On Audit Guidance

When auditing future cross-system changes, verify:
- new money, inventory, payout, order, or accounting authority does not rely on Mongo/cache/read models;
- replay paths are separated from new authority creation paths;
- authoritative decisions are made inside the same transaction that commits dependent state;
- event consumers are version-aware and cannot allow stale events to override newer authority;
- affiliate payout transfer evidence remains platform-global per rail/reference instead of becoming tenant-scoped duplicate proof;
- customer payment handoff surfaces support provider-specific initiation artifacts such as redirect URLs and client secrets instead of assuming every online provider redirects immediately;
- fail-closed behavior exists when authority cannot be verified.

## 2026-05-21 Final Architecture Maturity Review

Result:
- The final maturity review did not validate a new catastrophic stale-authority corruption gap in the inspected Tier-1 checkout/payment/fulfillment or product/search/publication slices.
- The strongest remaining issues are governance and operational maturity gaps around already-hardened primitives, not a failed SQL authority model.

New durable conclusions:
- `CrossSystemAuthorityRegressionGuardTests` is now part of the architecture truth, not just hygiene. If the raw-SQL source guard is red, future sessions must treat that as an active governance defect and not assume CI is still enforcing reviewed exceptions.
- The final governance sweep restored that enforcement credibility: `CrossSystemAuthorityRegressionGuardTests` now explicitly governs reviewed direct-SQL/bulk mutation files, high-risk Tier-1 `ReplaceOneAsync` paths, raw Elasticsearch client writes, and Redis pattern invalidation fanout with allowlists plus dedicated reviewed-pattern assertions.
- Direct SQL locking/idempotency helpers and direct full-document Mongo replacements remain specialized local tools. They are acceptable only with explicit freshness/authority controls and must not be reintroduced casually on new Tier-1 flows.
- Approved bypasses must stay narrow. Current reviewed exceptions include raw SQL idempotency/locking in `ProductVariantsController`, `SalesReturnsController`, `CommerceHubFinanceReconciliationJob`, `ProjectionEventFence`, and `TestAuthController`; a reviewed raw-ES discovery projection in `ServiceDiscoveryProjectionConsumer`; and a development-only CommerceHub seed path that may full-replace listing documents only inside `PointOfSale.API.Development` while still using `ICommerceHubSearchIndexService`.
- Promotion refresh, selected CommerceHub policy/promotion branches, and broader recommendation/feed/export tooling remain incremental maturity territory. They should be hardened only where concrete authority risk justifies it.
- Normal CQRS lag, cache lag, and reconciliation-required states remain acceptable design tradeoffs. Future engineers must not over-engineer them into synchronous global consistency or a universal rebuild framework.

## 2026-05-21 PRODUCT-CON-11 Affiliate/Public Storefront-Pricing Convergence

Problem:
- Storefront runtime already relied on SQL-authoritative unit-aware pricing, `DefaultUnit`-aware simple-product purchasability, and storefront-visible price semantics.
- Affiliate/public visibility still had mixed runtime paths that used legacy product-level `EffectivePrice`, event payload `OnlinePrice`/`SalePrice`, and caller-optional stock filtering.
- That allowed semantic split-brain where storefront-visible price or purchasability could diverge from affiliate/public cards, discovery, detail, or category counts.

Production rule:
- Affiliate/public price visibility is derivative only and must converge from the same storefront-authority semantics used by storefront-visible pricing.
- Simple products require current SQL `ProductUnits` plus a current default unit. Missing default unit means no online purchasability and therefore no affiliate/public price visibility.
- Variant products require current active storefront-visible priced variants. Legacy product-level `EffectivePrice` is not sufficient authority for affiliate/public runtime pricing.
- Replay/rebuild/reindex of affiliate/public projections must recompose from current SQL-backed storefront authority and fail closed when price or stock authority cannot prove purchasability.

Implemented hardening:
- `ProductDocument` now exposes shared `TryResolveAffiliatePublicPricing(...)` and `TryResolveAffiliateDiscoveryBasePrice(...)` helpers so affiliate/public paths reuse storefront-aligned pricing semantics instead of local shortcuts.
- `AffiliateProductPolicyProjectionConsumer` now loads SQL affiliate policy authority, applies `ProductSearchSqlAuthorityOverlay`, requires `IsOnlineVisible`, and deletes public affiliate documents when storefront-authority pricing/stock fails closed instead of indexing legacy `EffectivePrice`.
- `AffiliateProductIntelligenceProjectionConsumer` now recomposes base price from current storefront authority on product create/update and allows `ProductUpdatedEvent` replay to upsert a missing intelligence document instead of silently no-oping.
- `AffiliateProductDiscoveryService`, `PublicAffiliateProductsController`, and hardened `AffiliatePortalController` surfaces now require in-stock visibility on inspected public/affiliate discovery, card, category-count, landing, and detail paths.

Current evidence:
- `PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs`
- `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProductPolicyProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProductIntelligenceProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/AffiliateProductDiscoveryService.cs`
- `PointOfSale.API/Controllers/PublicAffiliateProductsController.cs`
- `PointOfSale.API/Controllers/AffiliatePortalController.cs`
- `PointOfSale.Tests/Infrastructure/AffiliateStorefrontPricingSnapshotTests.cs`
- `PointOfSale.Tests/Architecture/GlobalReliabilityInfrastructureExtractionTests.cs`

Residual debt:
- This is targeted affiliate/public convergence hardening, not a universal pricing normalization rewrite.
- CommerceHub listing/search pricing remains SQL-authoritative on its own listing semantics and is not yet converged onto the new storefront helper model.
- Future affiliate/public recommendation exports, feed writers, custom repair scripts, or direct Mongo/search rebuild tooling can still regress if they bypass the shared storefront-authority helpers.

## 2026-05-21 GOVERNANCE-CON-01 Storefront Product Mutation Boundary Extraction

Problem:
- The shared storefront product SQL writer already recomposed product/category/media/variant/unit/stock/promotion/publication authority before mutation, but it still directly invoked `IProductReadRepository.UpsertAsync` and `TombstoneIfFreshAsync`.
- Those repository methods are necessary dangerous primitives because they contain the Mongo full-document upsert and freshness-aware tombstone write, but keeping them directly in the writer made the approved mutation boundary less explicit and easier to copy into future rebuild or repair code.
- Storefront-visible pricing helpers existed on `ProductDocument`, but the actual storefront/affiliate/public pricing authority was not named as a standalone primitive.

Production rule:
- Storefront `ProductDocument` mutation has two explicit runtime layers: SQL recomposition through `IProductSqlAuthorityProjectionWriter`, then reviewed low-level mutation through `IStorefrontProductProjectionMutationBoundary`.
- Direct storefront product repository upsert/tombstone calls are allowed only inside `StorefrontProductProjectionMutationBoundary`.
- Full `ProductDocument` upsert must be typed as `ReviewedFullDocumentUpsert`, include tenant/product/event authority and a review reason, and carry an active, online-visible SQL-recomposed projection. Non-visible SQL authority must use `ReviewedVisibilityTombstone`.
- Storefront-visible pricing and affiliate/public pricing must go through `ProductStorefrontVisibilityAuthority`-backed helpers instead of raw product-level price fields.

Implemented hardening:
- Added `StorefrontProductProjectionMutationBoundary` and DI registration for `IStorefrontProductProjectionMutationBoundary`.
- Changed `ProductSqlAuthorityProjectionWriter` to call the mutation boundary for `ReviewedFullDocumentUpsert` and `ReviewedVisibilityTombstone` instead of directly calling `IProductReadRepository`.
- Added `ProductStorefrontVisibilityAuthority` and delegated `ProductDocument.StorefrontEffectivePrice`, `StorefrontVisiblePrice`, `TryResolveAffiliatePublicPricing`, and `TryResolveAffiliateDiscoveryBasePrice` to that named helper.
- Updated architecture guards so direct product repository upsert/tombstone calls are allowed only in `StorefrontProductProjectionMutationBoundary`, with focused assertions for the typed mutation kinds and visible-projection validation.

Current evidence:
- `PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionMutationBoundary.cs`
- `PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs`
- `PointOfSale.Infrastructure/MongoDB/Documents/ProductStorefrontVisibilityAuthority.cs`
- `PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs`
- `PointOfSale.Infrastructure/MongoDB/MongoDbServiceCollectionExtensions.cs`
- `PointOfSale.Tests/Infrastructure/ProductSqlAuthorityProjectionWriterTests.cs`
- `PointOfSale.Tests/Architecture/GlobalReliabilityInfrastructureExtractionTests.cs`
- `PointOfSale.Tests/Architecture/EventOutboxConsistencyArchitectureTests.cs`

Residual debt:
- This is targeted storefront product governance, not a universal Mongo replacement/upsert framework.
- `ProductReadRepository` still contains the actual Mongo dangerous primitives by design, and other document families still use local reviewed mutation patterns.
- CommerceHub pricing remains governed by CommerceHub listing/search SQL authority rather than the storefront pricing helper.

## 2026-05-22 PRODUCT-CON-11A Archived / Detached Variant Resurrection Hardening

Problem:
- Variant identity, SKU/barcode ownership, media linkage, publication, and pricing visibility were already hardened in adjacent sweeps, but runtime availability paths could still reason from projected variant stock or listing state after SQL lifecycle changed.
- A stale Mongo product variant, Elasticsearch document, Redis-influenced cart DTO, CommerceHub listing, affiliate intelligence document, stock event, price event, clone/import payload, or rebuild retry could therefore preserve public purchasability or search/listing visibility after the parent product was archived/unpublished/deleted or after the variant lost valid parent lineage.
- CommerceHub variant update handling did not actively reconcile listing/search visibility from variant lifecycle, so a stale listing could remain externally visible until some unrelated product/listing event repaired it.

Production rule:
- Public variant lifecycle is SQL authority. A variant is publicly usable only when current SQL proves same-tenant parent lineage, non-deleted rows, active variant state, non-empty parent `ProductId`, and active/online/published parent product state.
- `ProductVariantLifecycleAuthority` is the shared runtime helper for this predicate. Projection, cart, checkout, search, affiliate, CommerceHub, rebuild, repair, retry, import/clone, and bulk paths must either use it or document an equivalent SQL recomposition boundary.
- Stale replay is a recomposition trigger only. If SQL no longer proves public lifecycle, public result must be no stock, no purchasability, deleted/tombstoned search/storefront visibility, unpublished CommerceHub listing visibility, or no-op.

Implemented hardening:
- Added `ProductVariantLifecycleAuthority` with SQL query helpers for public variant lineage and current visibility.
- Storefront cart stock authority now uses SQL lifecycle recomposition and returns `0` when the SQL variant lineage snapshot is missing, instead of falling back to stale Mongo projected stock.
- CommerceHub cart and checkout validation now require active non-deleted variants whose parent products are active, online-visible, published, non-deleted, and same-tenant before accepting cart or checkout items.
- Product search/storefront recomposition now fails closed for variant products with no active priced valid-lineage variant, and product-variant projection search sync deletes the search document when the SQL overlay can no longer prove visibility.
- `ProductVariantProjectionConsumer` now uses parent-visible SQL lineage when loading variant authority and stock authority, so stale stock or variant replay cannot restore public availability after the parent lifecycle is no longer public.
- `CommerceHubProjectionConsumer` now handles `ProductVariantUpdatedEvent` by reconciling SQL listing publication with current SQL variant lifecycle and syncing Mongo/search visibility, instead of leaving variant lifecycle replay as a no-op.
- CommerceHub promotion price refresh, CommerceHub stock/price authority loads, and affiliate intelligence stock recomposition now filter through current SQL public variant lineage.
- `ProductStorefrontVisibilityAuthority` now ignores inactive variants when resolving variant-product visible price and fails closed when no active priced variant remains.

Current evidence:
- `PointOfSale.Infrastructure/Reliability/ProductVariantLifecycleAuthority.cs`
- `PointOfSale.API/Services/Cart/CartService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs`
- `PointOfSale.Infrastructure/Consumers/ProductVariantProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProductIntelligenceProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Infrastructure/MongoDB/Documents/ProductStorefrontVisibilityAuthority.cs`
- `PointOfSale.Tests/Infrastructure/ProductVariantLifecycleAuthorityTests.cs`

Residual debt:
- This is targeted runtime lifecycle convergence, not a universal recommendation/feed/export/import framework.
- Future bulk import, clone, merge/split, standalone autocomplete/suggestion, personalized recommendation, CDN/cache warmer, direct repair, feed/export, or external marketplace sync code can still regress if it creates or mutates variant-shaped visibility without current SQL lifecycle recomposition.
- Variant hard-delete rows that are no longer recoverable from SQL still depend on normal tombstone/delete propagation or an owning event carrying enough identity for deletion. Soft-delete/archive/unpublish/detach cases are covered on the inspected recomposition paths.

## 2026-05-22 PRODUCT-CON-11B Variant Clone / Import Replay Divergence Hardening

Problem:
- The runtime lifecycle predicate prevented public resurrection, but normal create/update surfaces still needed a single authority for clone/import-style identity and inheritance payloads.
- A copied or replayed payload could propose an already-owned SKU, product-level simple SKU drift, barcode carryover, stale media linkage, or duplicate initial stock rows unless every caller implemented the same SQL ownership checks.
- No dedicated CSV/Excel/ERP/supplier import service or named clone endpoint was present in the inspected backend, so the practical convergence points are product create/update, variant create/update, and default-variant synchronization.

Production rule:
- Clone/import/copy/template payloads are intent only. Current PostgreSQL product and variant rows own SKU and barcode semantic identity, current `ProductMedia` owns variant media inheritance, and current variant lifecycle authority owns public visibility.
- SKU creation may deterministically regenerate to a free SQL-owned value; barcode conflicts must fail closed; media inheritance must prove current same-product image media; initial stock imports must contain one row per warehouse; external mappings and publication visibility must be recomposed through existing SQL-authoritative paths rather than copied.

Implemented hardening:
- Added `ProductVariantCloneImportAuthority` for SQL semantic ownership checks, deterministic SKU regeneration, media inheritance validation, and simple-product default-variant identity mirroring.
- Product create/update now resolves simple-product SKU through the helper, rejects barcode ownership conflicts across live products and variants, and mirrors product-level simple identity from the default variant after synchronization.
- Variant create now resolves cloned/imported SKU through the helper, rejects barcode carryover conflicts, validates media through the helper, and rejects duplicate initial stock rows per warehouse before stock mutation fanout.
- Variant update now uses the helper for SKU/barcode ownership rejection and media inheritance validation while retaining existing idempotency, SQL unique-index conflict collapse, and stock mutation support.

Current evidence:
- `PointOfSale.Infrastructure/Reliability/ProductVariantCloneImportAuthority.cs`
- `PointOfSale.API/Controllers/ProductsController.cs`
- `PointOfSale.API/Controllers/ProductVariantsController.cs`
- `PointOfSale.API/Services/ProductVariantService.cs`
- `PointOfSale.Tests/Infrastructure/ProductVariantCloneImportAuthorityTests.cs`
- Focused validation passed on 2026-05-22 with `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~ProductVariantCloneImportAuthorityTests|FullyQualifiedName~ProductVariantLifecycleAuthorityTests" -nologo -v minimal` and `dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore -nologo -v minimal -m:1`.

Residual debt:
- This is targeted mutation-surface hardening, not a new universal import framework.
- Future explicit CSV/Excel/ERP/supplier import jobs, clone endpoints, template duplication, external marketplace sync writers, feed importers, or bulk catalog ingestion workers must adopt the same helper, add replay/idempotency tests, and avoid direct read-model or external-mapping carryover.

## 2026-05-22 PRODUCT-CON-11C Variant Merge / Split Semantic Divergence Hardening

Problem:
- The inspected backend had no named product variant merge/split orchestration endpoint, but ordinary variant update/delete/stock/rebuild paths could still be used by future duplicate consolidation, catalog normalization, supplier deduplication, or bulk cleanup work.
- Without durable SQL semantic lifecycle metadata, a merged source or split parent could remain indistinguishable from a current variant to stale jobs, rebuilds, stock reservation paths, SKU/barcode ownership checks, CommerceHub listing visibility, product search recomposition, or barcode lookup.
- That created a future corruption class where replayed merge/split work, delayed fanout, partial projection convergence, stale inventory redistribution, orphan reservations, or external mapping carryover could resurrect a detached semantic owner.

Production rule:
- Current SQL `ProductVariants.SemanticLifecycleState` plus replacement/lineage/operation metadata owns merge/split semantic lifecycle.
- Only `Current` semantic owners may own public lifecycle, SKU/barcode, stock mutation, inventory reservation creation, pricing/media/publication inheritance, CommerceHub/search visibility, external mapping/feed identity, or purchasability.
- Merge/split replay is a collapse/recomposition operation. A same operation id may no-op against already-applied SQL state; a divergent stale operation must fail closed.
- Open reservations must be rejected or deliberately migrated before a variant is superseded.

Implemented hardening:
- Added `ProductVariantSemanticLifecycleState` and SQL columns on `ProductVariants` for semantic state, replacement variant, lineage root, operation id/kind, and supersession timestamp, plus indexes and database checks.
- Added `ProductVariantMergeSplitAuthority` for current-owner predicates, SQL semantic snapshots, merge/split apply helpers, idempotent replay collapse, stale replay rejection, source SKU/barcode/media clearing, split-parent retirement, lineage stamping, and open-reservation rejection.
- `ProductVariantLifecycleAuthority` now requires current semantic ownership before public variant lineage can pass.
- `ProductVariantCloneImportAuthority` ignores superseded variants as SKU/barcode owners, while still treating current product/variant rows as SQL ownership authority.
- Admin product/variant mutation, default-variant alignment, barcode lookup, product search SQL recomposition, POS stock mutation support, storefront checkout reservation stock repository, CommerceHub marketplace reservation authority, CommerceHub payment recovery, and CommerceHub vendor visibility now fail closed for semantically superseded variants.

Current evidence:
- `PointOfSale.Core/Enums/ProductVariantSemanticLifecycleState.cs`
- `PointOfSale.Core/Entities/ProductVariant.cs`
- `PointOfSale.Infrastructure/Reliability/ProductVariantMergeSplitAuthority.cs`
- `PointOfSale.Infrastructure/Reliability/ProductVariantLifecycleAuthority.cs`
- `PointOfSale.Infrastructure/Reliability/ProductVariantCloneImportAuthority.cs`
- `PointOfSale.Infrastructure/Data/Configurations/ProductVariantConfiguration.cs`
- `PointOfSale.Infrastructure/Migrations/20260522143000_VariantMergeSplitSemanticAuthority.cs`
- `PointOfSale.API/Controllers/ProductVariantsController.cs`
- `PointOfSale.API/Controllers/ProductsController.cs`
- `PointOfSale.API/Services/ProductVariantService.cs`
- `PointOfSale.API/Services/Inventory/ProductVariantStockMutationSupport.cs`
- `PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs`
- `PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentRecoveryAuthorityService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubVendorVisibilityService.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Tests/Infrastructure/ProductVariantMergeSplitAuthorityTests.cs`

Residual debt:
- This is semantic lifecycle hardening and authority containment, not a full admin merge/split workflow.
- No dedicated supplier deduplication, automatic duplicate cleanup, or bulk merge/split orchestration path was present in the inspected backend.
- Future physical inventory redistribution and reservation migration during explicit merge/split still require transaction-scoped implementation, deterministic stock locks, exact reservation lineage migration tests, and post-commit product/variant/stock/listing/cache/search fanout validation.

## 2026-05-22 PRODUCT-CON-11D Variant Inventory / Reservation Lineage Convergence Hardening

Problem:
- Variant semantic lifecycle hardening blocked superseded variants from new reservations, but inventory consumption and stock propagation still needed a single SQL lineage predicate for current variant/product/warehouse/stock/reservation authority.
- Active reservation rows could be discovered by delayed payment, delivery, or recovery work after the backing stock, warehouse, product lineage, or variant semantic owner changed.
- Stock-sync outbox replay could publish stale availability if the original stock event was older than current SQL variant mutation authority or if warehouse/stock lineage had been detached after the event was staged.

Production rule:
- PostgreSQL is the only inventory lineage authority. A consumable reservation must be active, unexpired, tenant-owned, and backed by current same-tenant variant semantic ownership, active parent product lineage, active warehouse ownership, and a non-deleted stock row with reserved stock.
- Reservation replay is collapse, not resurrection. Expired, released, superseded, orphaned, inactive-warehouse, deleted-stock, or detached-product reservations may be released or repaired, but they must not drive payment, checkout, fulfillment, or stock consumption.
- Stock mutation and stock visibility fanout must fail closed unless current SQL proves variant/product/warehouse/stock ownership. Replayed stock-sync messages are triggers only and must suppress stale/detached fanout instead of restoring availability.

Implemented hardening:
- Added `ProductVariantInventoryLineageAuthority` for SQL inventory-reservable variant predicates, current stock-owner filtering, stock-mutation owner validation, and current consumable reservation filtering.
- `ProductVariantStockRepository` now rejects reservation creation unless current inventory lineage is valid, filters stock rows before reservation, and filters reservations before commit/order consumption so expired or superseded reservation replay collapses to no-op.
- `ProductVariantStockMutationSupport` now requires current variant/product/warehouse inventory lineage before stock mutation, preventing superseded variants or inactive warehouse links from receiving stock changes.
- `CheckoutInventoryAuthorityGuard` and `InventoryConsistencyService` now filter active reservations through current inventory lineage before checkout, CommerceHub payment, delivery, and provider side effects. CommerceHub distinguishes missing reservations from reservation rows that lost backing stock authority.
- `StockSyncOutboxProcessor` now suppresses stale/detached stock fanout when current SQL variant lifecycle, warehouse stock ownership, or event freshness no longer proves availability authority.

Current evidence:
- `PointOfSale.Infrastructure/Reliability/ProductVariantInventoryLineageAuthority.cs`
- `PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs`
- `PointOfSale.API/Services/Inventory/ProductVariantStockMutationSupport.cs`
- `PointOfSale.API/Services/Payments/CheckoutInventoryAuthorityGuard.cs`
- `PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs`
- `PointOfSale.Infrastructure/Eventing/StockSyncOutboxProcessor.cs`
- `PointOfSale.Tests/Infrastructure/ProductVariantMergeSplitAuthorityTests.cs`
- `PointOfSale.Tests/CommerceHub/InventoryConsistencyServiceTests.cs`
- Focused validation passed on 2026-05-22 with `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~ProductVariantMergeSplitAuthorityTests|FullyQualifiedName~ProductVariantLifecycleAuthorityTests|FullyQualifiedName~InventoryConsistencyServiceTests|FullyQualifiedName~CheckoutConcurrencyHardeningSourceTests" -nologo -v minimal`.

Residual debt:
- This is runtime lineage convergence, not a full physical inventory migration framework.
- Future explicit merge/split stock transfer, reservation migration, warehouse reassignment, external stock-feed/export writers, bulk inventory importers, and specialized reconciliation jobs must use the helper or an equivalent SQL transaction boundary with deterministic stock locks, replay collapse, cache/search/feed suppression, and rebuild tests.
- CommerceHub, affiliate, search, feed, and analytics availability remain derivative. Their future stock-specific writers must continue to converge from current SQL stock lineage rather than treating suppressed outbox messages, Mongo state, Redis keys, or external feed snapshots as authority.

## 2026-05-22 PRODUCT-CON-11E External Feed / Export / Projection Replay Convergence Hardening

Problem:
- The inspected backend has concrete export-like public derivative surfaces even though no standalone product feed writer was found: CommerceHub search/reindex/recovery, autocomplete/suggestion overlay reads, marketplace product and homepage read-model rebuilds, and storefront homepage cached/recommended product cards.
- Those paths could recompute visibility from a live SQL product while still treating local active variant or raw stock rows as enough evidence. A stale rebuild seed, delayed fanout, cache hydration, or recovery job could therefore keep a superseded variant id or detached stock availability visible in search, curated homepage recommendation-like sections, or marketplace availability.
- Reindex expected-count logic also needed to use the same public variant semantics as the overlay that actually emits CommerceHub documents so rebuild recovery did not verify against a looser stale visibility rule.

Projection/export authority rules:
- SQL owns export eligibility. Publication, product tenancy, variant public lifecycle, semantic current ownership, listing/category/store eligibility, pricing/media recomposition, and inventory stock ownership are proved from current PostgreSQL before derivative emission.
- `ProductVariantLifecycleAuthority` is the shared public variant predicate for both tenant-scoped and self-consistent global derivative rebuilds. Loaded aggregate paths use the same authority helper rather than a local `IsActive` check.
- `ProductVariantInventoryLineageAuthority` owns variant stock eligibility for outward availability. A stock row attached to an inactive warehouse, superseded variant, detached product, or deleted stock owner is not outward availability even if a stale projection still carries quantity.

Replay suppression and rebuild/recovery semantics:
- Replay is a current-authority recomposition trigger. Delayed outbox delivery, queue retry, dead-letter replay, old Mongo/search/cache seed, cache hydration, reindex, and partial-fanout recovery collapse to current visibility.
- Current visibility may mean upsert current SQL state, delete/tombstone stale derivative state, scrub a curated listing id, return no autocomplete/suggestion card after overlay revalidation, or expose zero stock. Historical public visibility is never restored because the payload or snapshot once contained it.
- Rebuilds remain eventually consistent and operationally pragmatic. They do not force distributed transactions; they must converge deterministically by using the same SQL authority filters at document-build and verification boundaries.

Implemented hardening:
- `ProductVariantLifecycleAuthority` now exposes a global self-consistent public-lineage query and a loaded aggregate check used by derivative rebuild code.
- `CommerceHubSearchSqlAuthorityOverlay` applies public variant lifecycle authority before creating or refreshing search documents and queries current inventory-lineage stock owners before emitting `InStock` or `AvailableStock`.
- `CommerceHubReindexService` verifies expected SQL visible listing count through the same public variant lifecycle filter that the overlay uses during search rebuilding.
- Marketplace product rebuilds now require published product visibility, filter rebuilt variants through lifecycle authority, delete variant-product projections when no current public semantic owner remains, and filter rebuilt availability through current inventory stock ownership.
- Marketplace homepage rebuilds scrub curated listing ids through public variant lifecycle authority, so a replayed superseded CommerceHub listing cannot remain in recommendation-like sections.
- Storefront homepage cached featured/new/best-seller/curated cards recompose variant visibility from SQL lifecycle authority before return and ignore stock rows rejected by inventory lineage authority.

Derivative fail-closed semantics:
- Superseded, detached, orphaned, inactive, unpublished, or invalid-lineage variants must end as no derivative card/listing/search/homepage visibility on the hardened paths.
- Invalid stock lineage must end as zero outward availability, not stale inventory replay.
- Cache/card/snapshot material may suggest a candidate id only; current SQL decides whether a derivative card can be returned.

Current evidence:
- `PointOfSale.Infrastructure/Reliability/ProductVariantLifecycleAuthority.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs`
- `PointOfSale.Infrastructure/ReadModels/Marketplace/MarketplaceReadModelProjectionServices.cs`
- `PointOfSale.API/Services/Storefront/StorefrontHomeService.cs`
- `PointOfSale.Tests/Infrastructure/ProductVariantLifecycleAuthorityTests.cs`
- `PointOfSale.Tests/Infrastructure/MarketplaceReadModelProjectionTests.cs`
- `PointOfSale.Tests/Architecture/GlobalReliabilityInfrastructureExtractionTests.cs`

Intentional remaining debt:
- No direct affiliate feed, supplier export, marketplace export, external catalog sync, inventory feed, pricing feed, or reporting/export product writer was present to harden in the inspected runtime. This change does not claim universal exporter coverage.
- Future dedicated exporters, cache warmers that hydrate visibility, personalized recommendation engines, standalone autocomplete indexes, analytics/reporting public projections, external sync workers, bulk repair tools, and CDN-side warmers must adopt these SQL recomposition and replay-collapse rules explicitly.
- Product search stock recomposition outside this concrete CommerceHub/marketplace/homepage slice still has local specialized behavior and must be reviewed when a new outward feed or cache warmer depends on it.

## 2026-05-22 ORDER-CON-01 Order / Fulfillment Semantic Resurrection Hardening

Problem:
- Shipment-triggered order status sync and storefront shipment-facet projection could treat an event payload as status/tracking authority after provider delay, queue replay, detached order lineage, or a later SQL shipment transition.
- Rebuild already had SQL-first order repair, but shipment visibility during repair was derived from order timestamps and old order stamps rather than live shipment status when a current SQL shipment existed.
- That left a concrete semantic resurrection path where stale shipment status, tracking, or carrier data could leak into order visibility or status fanout even though shipment/carrier mutation services had already converged SQL truth.

Production rule:
- Shipment payloads are triggers only. SQL `Orders` and `Shipments` own order/shipment lineage, status handoff eligibility, tracking visibility, and rebuild convergence.
- Current SQL lineage must prove non-deleted same-tenant shipment ownership before shipment events may mutate order status or storefront shipment visibility.
- Payment, refund, reservation settlement, fulfillment status, and carrier registration retain their existing authority owners; this hardening governs the shipment handoff and projection recomposition boundary instead of creating duplicate lifecycle semantics.

Implemented hardening:
- Added `OrderFulfillmentLineageAuthority` to load current order/shipment lineage and current live shipment visibility from SQL, with a single mapping for projection shipping status.
- `OrderStatusSyncService.SyncFromShipmentAsync(...)` now locks the order then reloads shipment lineage, rejects detached/deleted/wrong-order shipment evidence, and maps current SQL shipment status when event payload status is stale.
- `OrderProjectionConsumer` now recomposes `ShipmentStatusChangedEvent` storefront status/tracking/carrier mutation from SQL shipment lineage and rehydrates from SQL order authority instead of applying missing-lineage payloads.
- Storefront order projection snapshot loading and rebuilding now overlay live SQL shipment status, tracking, carrier, and delivered timestamp where current shipment lineage exists.
- Runtime tests cover live SQL lineage, soft-deleted/detached shipment rejection, stale delivered replay collapsing to current cancellation truth, and rebuild visibility honoring SQL shipment status. Architecture guards require status sync, projection, and rebuild loader to retain the shared lineage helper.

Current evidence:
- `PointOfSale.Infrastructure/Reliability/OrderFulfillmentLineageAuthority.cs`
- `PointOfSale.API/Services/OrderStatusSyncService.cs`
- `PointOfSale.Infrastructure/Consumers/OrderProjectionConsumer.cs`
- `PointOfSale.Infrastructure/MongoDB/Projection/StorefrontOrderProjectionAuthoritySnapshotLoader.cs`
- `PointOfSale.Infrastructure/MongoDB/Projection/StorefrontOrderProjectionBuilder.cs`
- `PointOfSale.Tests/Infrastructure/OrderFulfillmentLineageAuthorityTests.cs`
- `PointOfSale.Tests/Services/OrderStatusSyncShipmentLineageTests.cs`
- `PointOfSale.Tests/Architecture/OrderFulfillmentArchitectureTests.cs`

Shipment/payment semantic lineage:
- Shipment execution and carrier registration still converge through shipment/carrier services and existing reconciliation-required states.
- Payment capture/recovery/refund semantics still converge through payment lifecycle and payment recovery jobs. A shipment replay cannot promote payment settlement, and stale payment evidence must still pass payment authority before fulfillment progression.
- Reservation settlement and release still converge through current SQL inventory/reservation lineage. A shipment replay cannot make a stale reservation consumable.

Intentional remaining debt:
- This sweep does not claim universal warehouse, export, notification, support dashboard, analytics/reporting, or external provider coverage. The live storefront shipment projection and shipment-to-order status boundary are hardened here; future derivative shipment visibility surfaces must adopt the helper or add an equivalent reviewed SQL lineage boundary.
- `OrderShippedEvent`, `OrderDeliveredEvent`, and legacy status projection handlers still retain their existing event freshness and SQL order repair rules. When they are next broadened for non-storefront exports, prefer SQL recomposition over payload-owned visibility and keep timeline-only payload fields non-authoritative.

## 2026-05-22 PAYMENT-CON-01 Payment / Callback / Settlement Resurrection Hardening

Problem:
- Refund and settlement methods mutated SQL `Order.PaymentStatus` through the lifecycle owner but emitted only refund/settlement-specific fanout. Storefront and affiliate payment-status visibility consume canonical `OrderPaymentStatusChangedEvent`, so replay-safe SQL refund or settlement could leave derivative status stale until repair.
- Storefront order payment-facet projection used payment event `NewStatus` as the visible value. A delayed or replayed payment event could therefore attempt to republish stale collected/settled/completion visibility after current SQL had already moved to refund, failure, expiration, cancellation, or deleted order lineage.

Production rule:
- SQL payment lifecycle truth owns payment, refund, and settlement status. Provider callbacks, webhook retry timing, recovery jobs, settlement/reconciliation jobs, outbox replay, caches, exports, notifications, and projected order documents are triggers or evidence only.
- Accepted refund and COD/gateway settlement status transitions must fan out canonical payment-status visibility from `PaymentLifecycleService` in the same lifecycle transaction and must preserve refund-id/settlement-id replay collapse.
- Payment facet replay must reload current SQL order payment lineage before mutating storefront visibility. Missing SQL order lineage fails closed by deleting tenant-scoped derivative order/cache visibility, and stale event status may repair from SQL without appending payload-derived timeline narrative.

Implemented hardening:
- `PaymentLifecycleSource.RefundAuthority` and `PaymentLifecycleSource.SettlementAuthority` make the internal SQL fanout origin explicit without creating a second lifecycle owner.
- `PaymentLifecycleService.RecordRefundAsync`, `RecordCodSettlementAsync`, and `RecordGatewaySettlementAsync` now stage canonical `OrderPaymentStatusChangedEvent` with deterministic refund/settlement idempotency lineage when SQL payment status changes. Duplicate refund and settlement replay returns the pre-existing SQL result before staging duplicate payment-status fanout.
- `OrderPaymentLineageAuthority` reloads current non-deleted same-tenant SQL order payment visibility. `OrderProjectionConsumer` collapses mismatched replay payloads to that SQL status without appending conflicting payload-derived timeline narrative, and deletes tenant-scoped derivative order/cache visibility when current SQL order lineage is gone.
- SQL-first order rebuild already maps `Order.PaymentStatus`; payment facet replay now follows the same rebuild/recovery convergence rule instead of trusting stale event status.

Current evidence:
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`
- `PointOfSale.Infrastructure/Reliability/OrderPaymentLineageAuthority.cs`
- `PointOfSale.Infrastructure/Consumers/OrderProjectionConsumer.cs`
- `PointOfSale.Tests/Integration/EcommerceRefundLifecycleTests.cs`
- `PointOfSale.Tests/Infrastructure/OrderPaymentLineageAuthorityTests.cs`

Intentional remaining debt:
- This sweep hardens the concrete lifecycle fanout and storefront payment-facet replay gap found in the runtime. CommerceHub payment read models, affiliate/accounting/reporting exports, support/admin caches, customer notification retry workers, provider settlement/reconciliation exports, and future callback adapters must adopt the same SQL payment/refund/settlement lineage rule if they start projecting status from payloads.
- Focused lifecycle integration verification is presently blocked before test bodies by the repository SQLite schema creation error in `EcommerceRefundLifecycleTests` (`EnsureCreatedAsync` reports `near "' \t\n\r'": syntax error`). The new SQL-lineage helper tests run in-memory, and the runtime service/projection changes compile in the focused build.

## 2026-05-22 ACCOUNTING-CON-01 Accounting / Ledger / Financial Projection Semantic Drift Hardening

Problem:
- The accounting outbox already provided durable enqueue, leases, aggregate ordering, idempotency, critical-message retention, and non-terminal retry for financial truth events.
- The remaining inspected gap was semantic: order collection, refund, and settlement dispatch could still pass replay payload fields into final journal generation after loading the source aggregate. A delayed payment/refund/settlement outbox message could therefore try to post stale revenue, stale refund amount/context, stale COD delivery recognition, or stale settlement gross/fees unless every local path carried the same SQL authority checks.
- Financial projections, exports, dashboards, and recovery jobs remain derivative; if they were later built from old accounting payloads instead of SQL ledger/reconciliation state, they could rehydrate stale financial visibility even after SQL truth had converged.

Production rule:
- SQL ledger/accounting state is authoritative. `JournalEntries`, `LedgerEntries`, accounting outbox idempotency, current `Orders`, current `OrderRefunds`, current `PaymentSettlementBatches`, and explicit reconciliation state decide accounting truth.
- Accounting outbox payloads are triggers only. They may identify a candidate order/refund/settlement, but they cannot decide current payment recognition, COD delivery eligibility, refund amount/context, settlement totals, provider ownership, or financial export visibility.
- Replay is deterministic convergence: post current SQL truth once, return the existing idempotent journal, collapse stale no-longer-authoritative evidence to no-op, or remain retryable for operator reconciliation when required SQL authority is missing or internally inconsistent.

Implemented hardening:
- Added `AccountingLedgerAuthority` as the shared runtime helper for inspected ledger replay semantics.
- `AccountingOutboxProcessor` now evaluates current SQL order collection authority before online payment, wallet, and COD delivery posting. Terminal non-recognition states and missing COD delivery proof collapse stale replay before journal generation.
- E-commerce refund posting now reloads current `OrderRefund` lineage joined to current order payment truth, then posts the current SQL refund amount and settlement context instead of replay payload fields.
- Gateway and COD settlement posting now reload current `PaymentSettlementBatch` authority and post current SQL gross/fees/provider/settlement id. Payload settlement amounts and fees are no longer journal authority.
- Focused tests cover stale terminal payment replay collapse, refund amount recomposition from SQL, settlement amount recomposition from SQL, existing idempotency behavior, critical-message retry retention, stale POS mutation collapse, and co-ownership-correct accounting test fixtures.

Current evidence:
- `PointOfSale.Infrastructure/Reliability/AccountingLedgerAuthority.cs`
- `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs`
- `PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs`
- `PointOfSale.Tests/Accounting/AccountingOutboxProcessorTests.cs`
- Focused validation passed on 2026-05-22 with `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~AccountingOutboxProcessorTests -nologo -v minimal`.

Settlement/reconciliation convergence:
- Settlement accounting converges from current `PaymentSettlementBatch`, not from replay payloads or provider export snapshots.
- Refund accounting converges from current `OrderRefund`, including post-settlement refund context.
- Missing settlement/refund/order authority is not silently converted into ledger truth. It remains retryable where operator reconciliation is required, or collapses only when current SQL proves the candidate is stale/non-authoritative.

Rebuild/recovery behavior:
- Accounting outbox replay, failed-message retry, dead-letter recovery, and rebuild/recovery jobs may re-enter the processor, but they must use the same SQL authority checks before journal generation.
- Reporting, dashboard, export, tax, payout, and analytics rebuilds are derivative. They should rebuild from SQL ledger/reconciliation truth and fail closed when current accounting authority cannot prove visibility.

Intentional remaining debt:
- This pass does not rewrite every financial read model or export surface.
- CommerceHub vendor finance dashboards, affiliate accounting exports, provider reconciliation exports, tax/export feeds, payout export files, BI/reporting aggregates, support/admin financial caches, and future accounting cache warmers must add explicit SQL ledger/accounting recomposition before they generate or expose financial truth from snapshots.
- No universal financial projection engine was introduced; financial lifecycle and reconciliation paths remain specialized because external money evidence and operator review states are domain-specific.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 4 Settlement Lineage Accounting Certification

Problem:
- The ORDER-CON-01, PAYMENT-CON-01, and ACCOUNTING-CON-01 hardening left the inspected financial boundaries mostly aligned, but settlement accounting still trusted one stale derivative shortcut.
- `PaymentSettlementBatch.OrderCount` is a denormalized recorded snapshot. Accounting replay and the immediate settlement-accounting fast path still treated that header value as sufficient settlement-membership proof instead of revalidating the current `PaymentSettlementOrders` mappings.
- That meant current SQL amount/provider truth was reloaded, but current SQL settlement membership truth was not fully reloaded before posting.

Production rule:
- Settlement accounting authority is not header-only.
- Current non-deleted `PaymentSettlementBatch` owns settlement identity, gross, fees, provider, currency, and settled timestamp, while current `PaymentSettlementOrders` own live settlement membership lineage.
- Settlement posting must retry or escalate when current settlement-order lineage is empty or when recorded batch count disagrees with current live settlement-order mappings.
- Post-settlement refund creation and refund accounting must also revalidate that the refunding order still belongs to the current settlement batch lineage before bank-account refund posting is allowed.

Implemented hardening:
- `AccountingLedgerAuthority.LoadCurrentSettlementAsync(...)` now loads both `RecordedOrderCount` and `CurrentOrderCount`.
- `AccountingLedgerAuthority.LoadCurrentSettlementAsync(...)` now rejects soft-deleted settlement batches as missing current SQL authority.
- `AccountingLedgerAuthority.EvaluateSettlementPosting(...)` now rejects empty current lineage and retries on recorded-versus-current count drift with `SettlementOrderLineageMismatch` telemetry.
- Reviewed gateway/COD settlement recording now rejects non-positive gross amounts and fees outside `[0, gross]` before settlement persistence or payment-status mutation.
- `PaymentLifecycleService` immediate gateway/COD settlement posting now reloads `AccountingLedgerAuthority.LoadCurrentSettlementAsync(...)` and enforces `EvaluateSettlementPosting(...)` before ledger mutation so the fast path and replay path share the same settlement gross/fees/lineage authority gate.
- `ResolveRefundAccountingContextAsync(...)`, `PostCommittedRefundAccountingFromSqlTruthAsync(...)`, and `AccountingLedgerAuthority.LoadCurrentRefundAsync(...)` now require current settlement-batch membership for post-settlement refunds and retry when that lineage drifts or the order falls out of the current batch.
- Focused tests were added for current-lineage recomposition, deleted settlement-batch rejection, settlement-count drift retry, invalid settlement amount/fee rejection, post-settlement refund lineage retry, and source-level fast-path freeze assertions.
- Storefront payment projection replay now deletes its tenant-scoped Mongo order/cache visibility when current SQL order payment lineage is absent, records the delete as a missing-SQL-authority convergence decision, and suppresses payload-derived timeline additions when replayed status conflicts with current SQL payment truth.

Current evidence:
- `PointOfSale.Infrastructure/Reliability/AccountingLedgerAuthority.cs`
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`
- `PointOfSale.Infrastructure/Instrumentation/OperationalConvergenceTelemetry.cs`
- `PointOfSale.Tests/Infrastructure/AccountingLedgerAuthorityTests.cs`
- `PointOfSale.Tests/Accounting/AccountingOutboxProcessorTests.cs`
- `PointOfSale.Tests/Payments/PaymentLifecycleSettlementAuthorityTests.cs`
- `PointOfSale.Tests/Architecture/FinancialIntegrityRiskHardeningTests.cs`

Intentional remaining debt:
- This closes the settlement-membership replay gap on the inspected ledger paths; it does not convert the platform into a universal ledger/reconciliation framework.
- Future settlement repair/import/export/reporting paths must keep current `PaymentSettlementOrders` lineage authoritative and must not regress to header-only settlement membership assumptions or stale post-settlement refund context reuse.

## 2026-05-22 GOVERNANCE-CON-01 Dangerous Primitive & Authority Boundary Governance Hardening

Problem:
- The platform already had strong SQL-first authority helpers, projection fences, tombstone guards, and source-guard allowlists, but approved dangerous primitives still varied in how explicitly they documented runtime intent.
- A reviewed direct Mongo full-document replacement or cache mutation could rely on local freshness logic and SQL recomposition while not carrying a reusable runtime declaration that the primitive had SQL authority, freshness protection, fail-closed behavior, event identity, and a reviewed reason.
- Storefront footer projection was a concrete approved high-risk replacement path with SQL recomposition and freshness filtering, but it did not use the durable `ProjectionEventFence` before Mongo side effects.
- Storefront config projection already recomposed SQL and fenced replay, but its full `StorefrontConfigDocument` replacement and Redis cache invalidation still did not declare the approved authority scope at the primitive boundary.

Production rule:
- Dangerous primitives are not authority models. Direct Mongo replacement, raw Elasticsearch visibility writes, Redis mutation fanout, direct SQL/bulk mutation, and raw bus publishing must be contained behind named authority or infrastructure boundaries.
- A reviewed derivative mutation that cannot use a richer domain writer must explicitly declare its primitive, boundary, event identity, authority kind, freshness guard, fail-closed behavior, SQL authority where required, and review reason before executing.
- Replay remains convergence: stale payloads may trigger recomposition, but duplicate active delivery collapses before side effects, stale events fail freshness filters, and missing authority must produce no-op, tombstone/delete, or explicit repair/reconciliation rather than resurrection.

Implemented hardening:
- Added `DerivativeProjectionMutationGuard` and `ReviewedDerivativeProjectionMutation` in `PointOfSale.Infrastructure.Reliability`.
- Added named derivative authority kinds for storefront config/footer, checkout, and CommerceHub listing scopes.
- `StorefrontFooterProjectionConsumer` now acquires `ProjectionEventFence` before SQL-to-Mongo side effects, then calls `DerivativeProjectionMutationGuard.RequireReviewedMutation` before the reviewed `StorefrontFooterDocument` replacement.
- `StorefrontConfigProjectionConsumer` now calls the same guard before the reviewed full `StorefrontConfigDocument` replacement and before Redis store-config cache invalidation, declaring `StorefrontConfig`, `Checkout`, and `StoreMetadata` authority after SQL store config and tenant currency recomposition.
- `CrossSystemAuthorityRegressionGuardTests` now asserts that the storefront footer/config replacements and config cache mutation keep the fence, freshness filter, guard call, primitive declaration, authority kind, and fence completion.
- `DerivativeProjectionMutationGuardTests` cover rejection of missing SQL authority for full replacement, missing freshness guard, and non-fail-closed mutation intent.

Current evidence:
- `PointOfSale.Infrastructure/Reliability/DerivativeProjectionMutationGuard.cs`
- `PointOfSale.Infrastructure/Reliability/SqlAuthorityOverlay.cs`
- `PointOfSale.Infrastructure/Consumers/StorefrontFooterProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/StorefrontConfigProjectionConsumer.cs`
- `PointOfSale.Tests/Infrastructure/DerivativeProjectionMutationGuardTests.cs`
- `PointOfSale.Tests/Storefront/StorefrontConfigProjectionConsumerTests.cs`
- `PointOfSale.Tests/Architecture/CrossSystemAuthorityRegressionGuardTests.cs`
- Focused validation passed on 2026-05-22 with `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~DerivativeProjectionMutationGuardTests|FullyQualifiedName~StorefrontConfigProjectionConsumerTests|FullyQualifiedName~CrossSystemAuthorityRegressionGuardTests" -nologo -v minimal -m:1`.

Intentional remaining debt:
- This is not a giant governance framework and does not rewrite every specialized approved primitive.
- Storefront product mutation still uses the stronger `IProductSqlAuthorityProjectionWriter` plus `IStorefrontProductProjectionMutationBoundary`. Product/variant lifecycle, inventory, payment, fulfillment, and accounting paths keep their specific authority helpers.
- Existing approved Mongo/Elasticsearch/Redis/direct-SQL paths remain reviewed exceptions where they already carry domain-specific SQL recomposition, freshness, tombstone, lifecycle, outbox, or reconciliation protections. This pass does not retrofit coupon, payment-method, promotion, affiliate, CommerceHub, search, order repair, or repository internals unless they are changed. Future allowlist additions must not copy the primitive alone; they must add or reuse a runtime guard/boundary, focused tests, and documentation.

## 2026-05-22 TENANT-CON-01 Tenant Lineage & Cross-Tenant Boundary Hardening

Problem:
- Tenant-scoped replay/rebuild paths already used explicit tenant filters, but several repair and derivative paths still expressed tenant validation locally. In particular, storefront config projection could treat event tenant scope plus matching `StoreConfigId` as enough to keep a Mongo config visible even after the owning tenant became inactive, and admin search sync reparsed `X-Tenant-Id` locally instead of using trusted tenant context.

Production rule:
- Tenant lineage is current SQL authority. A tenant-scoped derivative write, cache invalidation, repair, or rebuild may use payload ids as candidates only; current PostgreSQL must prove the tenant is active and still owns the row before visibility is upserted.
- Trusted tenant resolution belongs to middleware and `ITenantContext`, not to endpoint-local parsing of raw tenant headers.
- Replay convergence is fail-closed: missing, inactive, deleted, moved, or cross-tenant SQL lineage must delete/scrub/no-op derivative visibility and tenant-scoped caches rather than preserving stale visibility.

Implemented hardening:
- Added `TenantLineageAuthority` for active-tenant proof and current tenant-owned row validation.
- Storefront config projection now loads SQL `StoreConfig` joined to the current active owning `Tenant` before Mongo replacement or Redis cache invalidation.
- Storefront config replay with missing/cross-tenant/inactive tenant lineage now deletes the tenant-scoped Mongo config projection and invalidates the tenant's versioned store-config Redis key.
- Admin search sync now resolves tenant scope from `ITenantContext.RequiredTenantId` and no longer treats locally parsed `X-Tenant-Id` as repair authority.

Current evidence:
- `PointOfSale.Infrastructure/Reliability/TenantLineageAuthority.cs`
- `PointOfSale.Infrastructure/Consumers/StorefrontConfigProjectionConsumer.cs`
- `PointOfSale.API/Controllers/AdminSearchSyncController.cs`
- `PointOfSale.Tests/Infrastructure/TenantLineageAuthorityTests.cs`
- `PointOfSale.Tests/Storefront/StorefrontConfigProjectionConsumerTests.cs`
- Focused validation passed on 2026-05-22 with `dotnet build PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj -nologo -v minimal -m:1`, `dotnet build PointOfSale.API\PointOfSale.API.csproj -nologo -v minimal -m:1`, and `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~TenantLineageAuthorityTests|FullyQualifiedName~StorefrontConfigProjectionConsumerTests" -nologo -v minimal`.

Intentional remaining debt:
- This is Phase 1 tenant lineage hardening, not a full tenant-governance framework.
- CMS page projection, homepage projection, affiliate discovery/intelligence projections, CommerceHub read-model repair, notification caches, analytics caches, support/admin caches, direct Mongo/search repair scripts, and future cache warmers still need explicit tenant-lineage adoption when their mutation or rebuild paths are expanded.
- `TenantLineageAuthority` is intentionally lightweight. Future domain-specific helpers should extend SQL recomposition at the owning boundary rather than creating parallel tenant authority semantics.

## 2026-05-22 TENANT-CON-01 Phase 2 Derivative Projection & Cache Isolation

Problem:
- Phase 1 established SQL tenant lineage on storefront config and admin search scope, but additional derivative surfaces still had tenant isolation expressed locally.
- Storefront homepage categories were returned directly from Redis/Mongo cache hydration without the same SQL tenant/category recomposition already used for product cards.
- CommerceHub marketplace product/homepage rebuilds could preserve tenant-scoped derivative visibility when stale replay targeted an inactive tenant.
- Public affiliate product projection used product-only Mongo/Elasticsearch mutation keys in several places and profile/currency replay used event payload values as derivative fanout input.

Production rule:
- Tenant-scoped derivative visibility must converge from current active SQL tenant lineage before public projection upsert, search indexing, cache hydration response, or repair/reindex mutation.
- Tenant-owned derivative document ids and mutation filters must include tenant scope unless the surface is explicitly global and documented.
- Store profile/currency/homepage/recommendation/cache replay payloads are triggers only. Current SQL tenant/profile/currency/product/category/listing authority decides whether to upsert, delete, scrub, no-op, or return empty visibility.

Implemented hardening:
- Added `TenantScopedDerivativeKey` for deterministic tenant+entity ids in derivative stores that do not have relational ownership constraints.
- Storefront homepage product and category cache hydration now checks active SQL tenant lineage and category/product SQL visibility before returning Redis/Mongo results.
- CommerceHub marketplace product rebuild and tenant homepage rebuild now delete/scrub instead of upserting when SQL tenant lineage is inactive or missing.
- Public affiliate product projection now validates `TenantLineageAuthority.ValidateCurrentTenantOwnerAsync<Product>` before public product mutation, uses `(TenantId, ProductId)` Mongo filters, writes tenant-scoped Elasticsearch ids, and deletes unsafe legacy product-id search documents during mutation.
- Public affiliate store-profile and tenant-currency replay now reload current active SQL tenant profile/currency; inactive tenant lineage deletes public affiliate products/stores and associated tenant-scoped search documents.
- The AF16E affiliate repair/reindex script now uses active SQL tenant lineage, active-tenant affiliate visibility authority, tenant-scoped search ids, and legacy stale search cleanup.

Current evidence:
- `PointOfSale.Infrastructure/Reliability/TenantScopedDerivativeKey.cs`
- `PointOfSale.API/Services/Storefront/StorefrontHomeService.cs`
- `PointOfSale.Infrastructure/ReadModels/Marketplace/MarketplaceReadModelProjectionServices.cs`
- `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProductPolicyProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Scripts/AF16E_ReindexAffiliateProductsWithStorefrontUrl.cs`
- `PointOfSale.Tests/Infrastructure/TenantLineageAuthorityTests.cs`
- `PointOfSale.Tests/Infrastructure/MarketplaceReadModelProjectionTests.cs`
- Focused validation passed on 2026-05-22 with `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -nologo -v minimal --filter "FullyQualifiedName~TenantLineageAuthorityTests|FullyQualifiedName~MarketplaceReadModelProjectionTests"`.

Intentional remaining debt:
- This is not a repo-wide cache framework. CMS page caches, SEO caches, notification/support caches, analytics hot/cold stores, marketplace localization/review caches, WhatsApp/session infrastructure, and payment/provider idempotency caches were not rewritten in this phase.
- Public affiliate Mongo still carries historical `ProductId` as the BSON id, so runtime mutation filters are tenant-scoped and fail closed on collision rather than silently replacing another tenant's document. A future schema migration can move the BSON id to a tenant+product composite key if production data requires parallel same-product-id rows.
- Existing safe global surfaces remain global by design. New tenant-scoped recommendation engines, cache warmers, search repair tools, feed writers, and direct projection scripts must adopt tenant lineage proof, tenant-scoped derivative ids, fail-closed hydration, and replay/rebuild tests before rollout.

## 2026-05-24 TENANT-CON-01 Phase 3 Tenant Authorization, Admin Scope & Operational Control Governance

Problem:
- Tenant resolution and tenant lineage had been hardened, but POS/admin role authorization still treated JWT role claims as enough for many privileged tenant-bound endpoints after normal token validation.
- A stale admin session could keep an old `Admin`/`Owner` role until token expiry after the SQL user was disabled, role-downgraded, moved, or after the tenant became inactive.
- Some operational tooling still expressed repair scope through the compatibility `X-Tenant-Id` header or exposed all-tenant/global operational primitives from tenant-admin routes.

Production rule:
- Tenant admin authority is SQL-backed actor authority. JWT claims identify a candidate actor and tenant; current SQL `Users`, current SQL `Tenant`, trusted tenant context, and endpoint role metadata decide whether privileged tenant work may proceed.
- Tenant-scoped repair/rebuild/reindex/cache operations must consume `ITenantContext.RequiredTenantId` and current SQL tenant lineage. Request headers, query tenant ids, frontend selections, repair payloads, and stale support dashboard state are not operational authority.
- Global operational primitives cannot be reached through tenant-admin scope. Tenant admins may trigger tenant-scoped recomposition; global search index control, all-tenant repair fanout, and platform recovery remain explicit platform/runbook authority.

Implemented hardening:
- Added `AdminTenantAuthority`, which reloads the current SQL user and active tenant before allowing a tenant-bound privileged endpoint. It rejects missing SQL actor authority, inactive/deleted users, stale role replay, tenant-claim mismatch, current actor tenant mismatch, and inactive/deleted tenants.
- Added `AdminTenantAuthorityMiddleware` after ASP.NET authorization so stale JWT role claims are collapsed against current SQL actor authority before controller code runs.
- `AdminDataSyncController`, `AdminOrdersController`, `AdminShipmentsController`, `AdminInventoryController`, `AdminStoreConfigController`, and `AdminCmsPagesController` no longer parse `X-Tenant-Id` as local tenant authority; tenant-scoped mutation and repair scope comes from `ITenantContext.RequiredTenantId`.
- Storefront configuration and CMS/publication admin controllers now require `Owner,Admin` SQL actor authority after middleware recomposition instead of accepting manager-scope publication/configuration control.
- `SearchReindexController` blocks global Elasticsearch index deletion/recreation from tenant-admin scope with `GLOBAL_SEARCH_CONTROL_FORBIDDEN`; tenant-scoped SQL-first reindex and cache invalidation remain available.
- `AF16E_ReindexAffiliateProductsWithStorefrontUrl` now exposes `ReindexTenantAsync`, and the admin controller calls only that tenant-scoped path. The all-tenant method remains available only as an internal/platform primitive.

Current evidence:
- `PointOfSale.API/Security/AdminTenantAuthority.cs`
- `PointOfSale.API/Middleware/AdminTenantAuthorityMiddleware.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.API/Controllers/AdminDataSyncController.cs`
- `PointOfSale.API/Controllers/AdminOrdersController.cs`
- `PointOfSale.API/Controllers/AdminShipmentsController.cs`
- `PointOfSale.API/Controllers/AdminInventoryController.cs`
- `PointOfSale.API/Controllers/AdminStoreConfigController.cs`
- `PointOfSale.API/Controllers/AdminCmsPagesController.cs`
- `PointOfSale.API/Controllers/SearchReindexController.cs`
- `PointOfSale.API/Controllers/Admin/AF16E_ReindexController.cs`
- `PointOfSale.Infrastructure/Scripts/AF16E_ReindexAffiliateProductsWithStorefrontUrl.cs`
- `PointOfSale.Tests/Security/AdminTenantAuthorityTests.cs`
- Focused validation passed on 2026-05-24 with `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore -nologo -v minimal -m:1 -p:UseSharedCompilation=false`, `dotnet build PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore -nologo -v minimal -m:1 -p:UseSharedCompilation=false`, `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter FullyQualifiedName~AdminTenantAuthorityTests -nologo -v minimal`, and `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter "FullyQualifiedName~AdminStoreConfigControllerHardeningTests|FullyQualifiedName~AdminCmsConflictControllerTests" -nologo -v minimal`.

Rebuild/recovery behavior:
- Tenant-scoped rebuild/reindex/cache repair triggered through admin endpoints now has two convergence checks: current SQL actor authority at request time and current SQL tenant/domain recomposition inside the repair path.
- Stale privileged replay collapses before operational side effects. A delayed or replayed admin request cannot retain tenant control after current SQL actor or tenant authority rejects it.

Intentional remaining debt:
- This is not an enterprise IAM redesign and does not introduce organization federation, ABAC, external SSO, or a full delegated-administration model.
- Support impersonation/elevation ceremonies, tenant switching UX, global platform runbooks, and future support/admin caches still need explicit actor-authority design if they are expanded.
- Bare `[Authorize]` endpoints without tenant admin role metadata are not governed by this middleware unless they are converted to explicit role/policy boundaries. Future tenant-admin mutation surfaces must carry explicit role metadata or an equivalent actor-authority boundary.

## 2026-05-24 OPERATIONS-CON-01 Replay / Drift / Rebuild Observability & Diagnostics

Problem:
- The platform had strong SQL-first replay/rebuild convergence, but many successful protections were operationally quiet.
- Duplicate replay collapse, active event overlap, stale projection rejection, fail-closed lineage rejection, rebuild authority declaration, stale stock fanout suppression, accounting replay collapse, stale admin-session rejection, and drift detection could require reading subsystem-specific logs or inferring behavior from absence of side effects.
- Ad-hoc telemetry risked entropy: different subsystems could invent different replay reason strings, log raw ids, or emit high-cardinality labels.

Production rule:
- Semantic convergence must be observable through a shared low-cardinality operational boundary.
- Replay/no-op/suppression/fail-closed/delete/tombstone/retry/rebuild/drift outcomes must be classified by stable phase, decision, authority, and reason.
- Telemetry must not expose raw tenant ids, payment/customer data, document keys, callback payloads, or free-form exception text as labels.
- Metrics and traces are diagnostics, not authority. PostgreSQL and the existing lineage/domain authorities still decide whether to mutate, suppress, delete, retry, or no-op.

Implemented hardening:
- Added `OperationalConvergenceTelemetry` with meter `PointOfSale.Operational.Convergence`.
- Added stable classifications: `OperationalConvergencePhase`, `OperationalConvergenceDecision`, `OperationalConvergenceAuthority`, and `OperationalConvergenceReason`.
- Added counters for semantic replay decisions, authority failures, rebuild diagnostics, dangerous primitive governance, and drift diagnostics. Trace events are attached when an `Activity` is active.
- `ProjectionEventFence` now records duplicate completed replay collapse, active delivery overlap collapse, duplicate completion races, and successful fence completion.
- `ProjectionAuthorityValidator` records stale replay, future/corrupted version, missing SQL timestamp authority, and same-version timestamp drift rejections.
- `SqlTruthRebuildGuard` records SQL rebuild authority declarations and invalid/missing authority rejection.
- `DerivativeProjectionMutationGuard` records accepted and rejected dangerous primitive governance decisions.
- `TenantLineageAuthority`, `AdminTenantAuthority`, `ProductVariantLifecycleAuthority`, `ProductVariantInventoryLineageAuthority`, `OrderFulfillmentLineageAuthority`, `OrderPaymentLineageAuthority`, and `AccountingLedgerAuthority` now emit fail-closed authority diagnostics at shared boundary decisions.
- Stock-sync stale fanout suppression records inventory-lineage suppression reasons, and drift checks now record Mongo projection drift and Elasticsearch reconciliation diagnostics.
- The API OpenTelemetry meter pipeline now includes the operational convergence meter.

Current evidence:
- `PointOfSale.Infrastructure/Instrumentation/OperationalConvergenceTelemetry.cs`
- `PointOfSale.Infrastructure/Reliability/ProjectionEventFence.cs`
- `PointOfSale.Infrastructure/Reliability/ProjectionAuthorityValidator.cs`
- `PointOfSale.Infrastructure/Reliability/SqlTruthRebuildGuard.cs`
- `PointOfSale.Infrastructure/Reliability/DerivativeProjectionMutationGuard.cs`
- `PointOfSale.Infrastructure/Reliability/TenantLineageAuthority.cs`
- `PointOfSale.API/Security/AdminTenantAuthority.cs`
- `PointOfSale.Infrastructure/Reliability/ProductVariantLifecycleAuthority.cs`
- `PointOfSale.Infrastructure/Reliability/ProductVariantInventoryLineageAuthority.cs`
- `PointOfSale.Infrastructure/Reliability/OrderFulfillmentLineageAuthority.cs`
- `PointOfSale.Infrastructure/Reliability/OrderPaymentLineageAuthority.cs`
- `PointOfSale.Infrastructure/Reliability/AccountingLedgerAuthority.cs`
- `PointOfSale.Infrastructure/Eventing/StockSyncOutboxProcessor.cs`
- `PointOfSale.Infrastructure/HealthChecks/ProductionHealthChecks.cs`
- `PointOfSale.Infrastructure/Jobs/ElasticsearchDriftReconciliationJob.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.Tests/Infrastructure/OperationalConvergenceTelemetryTests.cs`

Validation:
- Focused validation passed on 2026-05-24 with `dotnet build PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj -nologo -v minimal -m:1`, `dotnet build PointOfSale.API\PointOfSale.API.csproj -nologo -v minimal -m:1`, `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj -nologo -v minimal --filter FullyQualifiedName~OperationalConvergenceTelemetryTests`, and `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj -nologo -v minimal --filter "FullyQualifiedName~DerivativeProjectionMutationGuardTests|FullyQualifiedName~TenantLineageAuthorityTests|FullyQualifiedName~OrderPaymentLineageAuthorityTests|FullyQualifiedName~OrderFulfillmentLineageAuthorityTests|FullyQualifiedName~AccountingOutboxProcessorTests"`.

Operational semantics:
- Replay collapse telemetry answers "what happened and why" without making replay mutation-authoritative.
- Rebuild diagnostics record the SQL authority declaration and invalid authority rejection; final convergence still belongs to the domain writer or rebuild service.
- Drift diagnostics record detection/reconciliation state for Mongo and Elasticsearch, but repair must still recompose from SQL.
- Dangerous primitive telemetry records whether a reviewed primitive was accepted or rejected by runtime governance.

Intentional remaining debt:
- This is not a universal observability platform, durable audit-event store, SIEM redesign, or distributed tracing rollout.
- Existing specialized paths outside the shared guards may still require local adoption when they add new convergence behavior.
- Future cache warmers, feed/export writers, report rebuilders, support/admin caches, notification caches, global repair runbooks, and external sync workers must add semantic convergence telemetry or document an equivalent reviewed diagnostic boundary with tests.

## 2026-05-24 STRATEGIC-REVIEW-01 Storefront Navigation Replay / Rebuild Hardening

Problem:
- Strategic review of replay pressure, rebuild behavior, tenant isolation, operational tooling, and derivative mutation governance found that `NavigationProjectionConsumer` was SQL-reloading `StoreConfig` but still allowed duplicate replay amplification.
- `BuildFreshNavigationEventFilter` admitted the same `EventId`, so repeated delivery could execute another full `StorefrontNavigationDocument` replacement and Redis navigation/menu/header/footer invalidation.
- The path had not yet adopted the shared `ProjectionEventFence`, active tenant/store-config lineage check, or runtime dangerous-primitive guard used by adjacent storefront config/footer projections.

Production rule:
- Storefront navigation projection is derivative visibility. Navigation events are triggers only; current SQL `StoreConfig` plus current active owning tenant decide whether navigation can exist.
- Duplicate completed and active overlapping replay must collapse before MongoDB or Redis side effects.
- Missing, inactive, moved, or cross-tenant store-config lineage must fail closed to delete/scrub/no-op plus cache invalidation rather than preserving stale navigation.
- Full navigation document replacement and Redis cache invalidation are reviewed dangerous primitives and must declare SQL authority, freshness, fail-closed behavior, and the named `StorefrontNavigation` authority scope.

Implemented hardening:
- `NavigationProjectionConsumer` now acquires `ProjectionEventFence` before SQL-to-Mongo/Redis side effects and completes the fence after either successful projection or deterministic stale/fail-closed convergence.
- The projection now calls `TenantLineageAuthority.LoadCurrentStoreConfigProjectionAuthorityAsync(...)` and fails closed by deleting the tenant-scoped navigation document and invalidating known tenant/slug navigation keys when current SQL lineage is not proven.
- The Mongo freshness filter now rejects duplicate `EventId` instead of admitting it as a fresh write.
- `DerivativeProjectionMutationGuard` now governs the reviewed `StorefrontNavigationDocument` full replacement and Redis navigation cache invalidation under `StorefrontNavigation`, `StorefrontConfig`, and `StoreMetadata` authority scopes.
- `SqlAuthorityOverlayKind` now includes `StorefrontNavigation`.
- `CrossSystemAuthorityRegressionGuardTests` now allowlist navigation replacement only with dedicated assertions for event fencing, tenant lineage, fail-closed deletion, runtime dangerous-primitive governance, authority-kind declarations, and duplicate-EventId freshness rejection.

Current evidence:
- `PointOfSale.Infrastructure/Consumers/NavigationProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Reliability/SqlAuthorityOverlay.cs`
- `PointOfSale.Tests/Architecture/CrossSystemAuthorityRegressionGuardTests.cs`

Strategic review findings:
- Validated: existing product/search/homepage, payment, fulfillment, accounting, tenant-lineage, admin-actor, and observability hardening establishes strong SQL-first convergence on inspected Tier 1 paths.
- Found: navigation was a narrower derivative drift gap, not a new business-authority primitive gap. Its failure mode was replay amplification and stale tenant visibility preservation risk, not SQL corruption.
- Hardened: duplicate replay collapse, fail-closed tenant lineage, dangerous primitive governance, and source-guard regression coverage for navigation.
- Remaining blind spots: CMS page projection, SEO/navigation-adjacent caches, smart-offer overlays, online-customer admin projections, analytics, notification caches, support/admin caches, global runbooks, and future cache warmers/exporters still require path-specific review before claiming the same convergence guarantees.

Validation:
- Focused source governance validation should include `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~CrossSystemAuthorityRegressionGuardTests -nologo -v minimal`.

Intentional remaining debt:
- This is not a universal derivative projection framework. Existing specialized paths remain valid where they carry local freshness/idempotency guarantees, but future full-document replacement or cache invalidation paths must either use shared guards or document an equivalent reviewed boundary with replay/rebuild tests.

## 2026-05-24 CHAOS-CON-01 Replay / Rebuild / Partial Failure Chaos Validation

Problem:
- The architecture now claims replay safety, rebuild safety, fail-closed convergence, tenant-safe derivative isolation, replay-safe operational recovery, and deterministic authority recomposition.
- Those claims needed pressure against realistic operational chaos: duplicate delivery, replay storms, stale events, interrupted rebuilds, cache/projection races, stale admin sessions, delayed callbacks, dead-letter replay, repair overlap, and reconciliation overlap.

Production rule:
- Chaos convergence is semantic, not transport-level. At-least-once delivery, retries, partial rebuilds, and delayed callbacks are expected.
- Current SQL authority and reviewed lineage helpers decide whether work applies, collapses, retries, deletes/tombstones, rehydrates, or fails closed.
- Operational diagnostics must classify the convergence decision through low-cardinality telemetry. Silent no-op replay is not enough for Tier 1 authority-adjacent paths.

Validation added:
- `SemanticConvergenceChaosValidationTests` simulates duplicate completed projection delivery, duplicate rebuild triggers, interrupted rebuild authority, stale dangerous full replacement, stale cache hydration, cross-tenant replay, inactive tenant replay, stale admin JWT role replay, disabled actor replay, inactive-tenant admin replay, stale payment/shipment replay, stale collection accounting replay, invalid refund replay, invalid settlement replay, and operational repair scope pressure.
- The tests drive runtime boundaries where possible: `ProjectionEventFence`, `SqlTruthRebuildGuard`, `DerivativeProjectionMutationGuard`, `TenantLineageAuthority`, `AdminTenantAuthority`, `OrderPaymentLineageAuthority`, `OrderFulfillmentLineageAuthority`, `AccountingLedgerAuthority`, and `OperationalConvergenceTelemetry`.
- Source-governance assertions cover tenant-scoped search repair, AF16E tenant repair, SQL-first discount/marketplace rebuild declarations, navigation replay fencing/dangerous primitive guards, and product cache duplicate fanout fencing.

Findings:
- Validated: duplicate completed projection replay collapses before side effects and records replay telemetry.
- Validated: duplicate rebuild triggers deterministically redeclare the same SQL authority instead of becoming rebuild truth.
- Validated: interrupted/malformed rebuild input fails closed when SQL authority kinds are missing.
- Validated: dangerous derivative full replacement rejects missing SQL authority, and cache mutation rejects missing freshness.
- Validated: stale tenant replay, cross-tenant derivative ownership, inactive tenant replay, stale admin role replay, disabled actor replay, and inactive tenant admin replay fail closed.
- Validated: payment and fulfillment projection replay recomposes current SQL order payment/shipment lineage; stale detached shipment evidence fails closed.
- Validated: accounting collection/refund/settlement replay collapses or retries from current SQL accounting authority instead of accepting payload amounts or terminal stale status.
- Weakness found: AF16E affiliate repair/reindex uses active SQL tenant filters and `TenantScopedDerivativeKey`, but does not call `TenantLineageAuthority` directly. This is an equivalent local control on the inspected path, not observed leakage, but future docs must not overstate shared-helper adoption.

Current evidence:
- `PointOfSale.Tests/Infrastructure/SemanticConvergenceChaosValidationTests.cs`
- `PointOfSale.Infrastructure/Reliability/ProjectionEventFence.cs`
- `PointOfSale.Infrastructure/Reliability/SqlTruthRebuildGuard.cs`
- `PointOfSale.Infrastructure/Reliability/DerivativeProjectionMutationGuard.cs`
- `PointOfSale.Infrastructure/Reliability/TenantLineageAuthority.cs`
- `PointOfSale.API/Security/AdminTenantAuthority.cs`
- `PointOfSale.Infrastructure/Reliability/OrderPaymentLineageAuthority.cs`
- `PointOfSale.Infrastructure/Reliability/OrderFulfillmentLineageAuthority.cs`
- `PointOfSale.Infrastructure/Reliability/AccountingLedgerAuthority.cs`
- `PointOfSale.Infrastructure/Scripts/AF16E_ReindexAffiliateProductsWithStorefrontUrl.cs`

Validation:
- Focused validation passed on 2026-05-24 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj -nologo -v minimal --filter FullyQualifiedName~SemanticConvergenceChaosValidationTests`.

Intentional remaining debt:
- This is not a distributed chaos framework and does not claim universal path coverage.
- CMS page projection, SEO caches, notification caches, analytics/reporting rebuilds, support/admin caches, external feed/export writers, future cache warmers, and global platform runbooks still require path-specific replay/rebuild/tenant/actor/convergence validation when changed or expanded.

## 2026-05-24 STABILIZATION-CON-01 Operational Maturity / Throughput Controls

Problem:
- Operational scalability review found that marketplace event outbox, marketplace projection outbox, and stock sync outbox workers were not fully governed by runtime throughput options.
- Poll cadence, batch draining, and claim lease behavior were partially hard-coded, making large backlog recovery and tenant hotspot tuning less ergonomic even though correctness still depended on SQL ordering, leases, retries, and idempotency.

Production rule:
- High-volume outbox/replay workers must expose bounded operational controls for batch size, max batches per poll, inter-batch delay, poll interval, and claim lease.
- Throughput tuning must not bypass SQL authority, tenant scope, aggregate ordering blockers, retry delay, compensation, duplicate completion collapse, or fail-closed lineage checks.

Implemented hardening:
- Added `EventOutboxBatchSize`, `ProjectionOutboxBatchSize`, `MaxBatchesPerPoll`, and `InterBatchDelayMilliseconds` to `MarketplaceEventBackboneOptions` with startup validation.
- Added `BatchSize`, `MaxBatchesPerPoll`, and `InterBatchDelayMilliseconds` to `StockSyncOutboxOptions` with startup validation.
- `MarketplaceEventOutboxWorker`, `MarketplaceProjectionOutboxWorker`, and `StockSyncOutboxWorker` now drain bounded configured batches per poll and yield between batches.
- `MarketplaceEventOutboxProcessor`, `MarketplaceProjectionOutboxProcessor`, and `StockSyncOutboxProcessor` now use configured claim leases.

Current evidence:
- `PointOfSale.Infrastructure/Eventing/MarketplaceEventBackboneOptions.cs`
- `PointOfSale.Infrastructure/Eventing/MarketplaceEventOutboxWorker.cs`
- `PointOfSale.Infrastructure/Eventing/MarketplaceProjectionOutboxProcessor.cs`
- `PointOfSale.Infrastructure/Eventing/StockSyncOutboxOptions.cs`
- `PointOfSale.Infrastructure/Eventing/StockSyncOutboxWorker.cs`
- `PointOfSale.Infrastructure/Eventing/StockSyncOutboxProcessor.cs`
- `PointOfSale.Tests/Infrastructure/MarketplaceProjectionOutboxOperationalScalabilityTests.cs`
- `docs/architecture/operational-maturity-scalability-findings.md`

Operational semantics:
- Operators can tune recovery throughput without redefining business truth or replay semantics.
- Bounded batch draining improves backlog recovery ergonomics while preserving worker cancellation/restart behavior.
- Duplicate projection completion remains collapse/cleanup, not redispatch.

Intentional remaining debt:
- No adaptive throttling or tenant-specific worker scheduling was introduced.
- No synthetic enterprise throughput SLO is claimed from unit tests.
- Future operational workers for CMS, SEO, notification, analytics, support caches, exports, reporting rebuilds, cache warmers, and global runbooks still need equivalent path-specific scalability controls and tests.

## 2026-05-24 PERFORMANCE-CON-01 Replay / Rebuild / Projection Throughput Optimization

Problem:
- Follow-up throughput review found the payment-cancellation outbox worker and processor still used hard-coded batch size, worker cadence, retry threshold, and retry backoff cap.
- Stock-sync options already existed, but API startup did not pass configuration into `AddStockSyncOutbox`, which made production tuning less direct than the operational-scalability governance required.

Production rule:
- Recovery throughput controls are pacing mechanisms only. Payment-cancellation recovery must continue to route through `IPaymentLifecycleService`; marketplace, stock-sync, accounting, and payment-cancellation outboxes must stay SQL/lifecycle authoritative.
- High-volume operational workers must bind bounded options from configuration and preserve retry, idempotency, cancellation, and fail-closed behavior.

Implemented hardening:
- Added `PaymentCancellationOutboxOptions` with bounded batch size, max batches per poll, inter-batch delay, poll interval, idle interval, error delay, max retry attempts, and max retry delay.
- `PaymentCancellationOutboxWorker` now drains bounded configured batches per poll and yields between batches.
- `PaymentCancellationOutboxProcessor` now uses configured batch size, retry threshold, and retry backoff cap.
- API startup now binds both `PaymentCancellationOutbox` and `StockSyncOutbox` configuration sections.

Current evidence:
- `PointOfSale.Infrastructure/Payment/PaymentCancellationOutboxOptions.cs`
- `PointOfSale.Infrastructure/Payment/PaymentCancellationOutboxWorker.cs`
- `PointOfSale.Infrastructure/Payment/PaymentCancellationOutboxProcessor.cs`
- `PointOfSale.Infrastructure/Extensions/PaymentCancellationOutboxExtensions.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.Tests/Infrastructure/PaymentCancellationOutboxOperationalScalabilityTests.cs`
- `docs/architecture/operational-maturity-scalability-findings.md`

Validation:
- Focused validation passed on 2026-05-24 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj -nologo -v minimal --filter FullyQualifiedName~PaymentCancellationOutboxOperationalScalabilityTests`.

Intentional remaining debt:
- This pass does not add adaptive throttling, tenant-specific scheduling, or synthetic recovery SLOs.
- Payment-cancellation outbox does not yet have processing-lease columns. Current correctness relies on lifecycle idempotency and SQL authority; if active duplicate processing becomes a measured production issue, add a lease migration and focused restart/overlap tests rather than bypassing lifecycle authority.

## 2026-05-24 OPERATIONS-RUNBOOKS-01 Operational Recovery, Replay & Rebuild Playbooks

Problem:
- The platform had strong SQL-first replay/rebuild convergence and observability, but safe recovery also depends on consistent human behavior during incidents.
- The remaining risk was operational: replay panic, rebuild misuse, manual cache/projection mutation, unsafe tenant override, replay amplification, fail-closed deletion panic, manual financial repair, and undocumented dangerous shortcuts.

Production rule:
- Operators must treat replay/rebuild inputs, projections, caches, search indexes, dashboards, and provider payloads as evidence only.
- Recovery procedures must preserve SQL authority, replay collapse, fail-closed semantics, tenant scope, actor/platform authority, lifecycle/reconciliation ownership, bounded throughput controls, and low-cardinality diagnostics.
- Dangerous operational primitives require reviewed authority boundaries and documented runbook scope.

Implemented hardening:
- Added `docs/operations/operational-recovery-runbooks.md` with production runbooks for replay recovery, rebuild/reindex recovery, queue backlog recovery, cache/projection recovery, tenant isolation incidents, payment/accounting/fulfillment incidents, drift investigation, telemetry interpretation, escalation boundaries, operational anti-patterns, and remaining debt.
- Added `docs/architecture/dangerous-operational-primitives.md` to govern direct SQL/bulk mutation, full Mongo replacement, raw Elasticsearch writes, Redis pattern/manual key mutation, raw bus publishing, global/all-tenant repair, tenant override/support impersonation, and manual financial repair.
- Added `OperationalRecoveryRunbookArchitectureTests` to ensure the recovery runbook keeps required sections, SQL-first replay/rebuild semantics, dangerous shortcut prohibitions, escalation boundaries, and intentionally lightweight remaining-debt statements.

Current evidence:
- `docs/operations/operational-recovery-runbooks.md`
- `docs/architecture/dangerous-operational-primitives.md`
- `PointOfSale.Tests/Architecture/OperationalRecoveryRunbookArchitectureTests.cs`

Operational semantics:
- Replay-safe no-op, duplicate collapse, fail-closed delete/tombstone, retry, and SQL rehydrate are documented as expected operator-visible outcomes.
- Rebuild interruption and overlap are documented as normal convergence conditions when each run redeclares SQL authority.
- Tenant-admin repair remains tenant-scoped; all-tenant/global repair is platform runbook scope.
- Payment/accounting/fulfillment ambiguity must route through lifecycle or reconciliation owners rather than manual projection, cache, or ledger edits.

Intentional remaining debt:
- No enterprise IAM, support impersonation ceremony, adaptive throttling, fixed recovery SLO, SIEM, durable audit store, or universal projection framework was introduced.
- Future CMS, SEO, notification, analytics, support-cache, feed/export, reporting rebuild, cache warmer, and global runbook paths still need path-specific replay/rebuild/tenant/actor/telemetry validation when expanded.

## 2026-05-24 TENANT-CERTIFICATION-01 Tenant Isolation Certification & Regression Freeze

Problem:
- Prior hardening established trusted tenant resolution, SQL tenant lineage, SQL actor authority, tenant-scoped derivative keys, replay/rebuild chaos validation, and operational runbooks, but tenant isolation certification needed a durable findings artifact and a freeze boundary that covered remaining public derivative surfaces.
- Static review found `CmsPageProjectionConsumer` had SQL reload, tenant filters, and freshness checks, but unlike adjacent navigation/config/footer projections it did not yet use `ProjectionEventFence` or `DerivativeProjectionMutationGuard` around full `StorefrontPageDocument` replacement.

Production rule:
- Tenant-scoped public derivative visibility must be SQL-recomposed, tenant-scoped, replay-collapsed before dangerous side effects, fail-closed when current SQL authority rejects visibility, and documented honestly.
- Regression-freeze tests must make future raw tenant authority, unscoped derivative identity, unsafe tenant-admin global repair, dangerous primitive bypass, and undocumented certification drift difficult to introduce silently.

Implemented hardening:
- `CmsPageProjectionConsumer` now fences publish/delete replay with `ProjectionEventFence` before MongoDB side effects.
- CMS publish replay now checks active SQL tenant lineage and tombstones existing tenant-scoped visibility when SQL page or tenant authority is missing.
- CMS full `StorefrontPageDocument` replacement now calls `DerivativeProjectionMutationGuard` and declares `SqlAuthorityOverlayKind.StorefrontCms`, `StoreMetadata`, and `Tombstone` authority.
- `CrossSystemAuthorityRegressionGuardTests` now treats `StorefrontPageDocument` replacement as a reviewed high-risk primitive and asserts CMS replay fence, tenant-lineage, fail-closed, runtime-guard, freshness-filter, and fence-completion markers.
- `TenantIsolationCertificationFreezeTests` freeze trusted tenant resolution, tenant-admin trusted tenant context, global search-control rejection, affiliate/CommerceHub/CMS tenant-scoped derivative controls, and certification documentation.
- Certification findings are documented in `docs/architecture/tenant-isolation-certification-findings.md`; dangerous primitive and runbook docs now include TENANT-CERTIFICATION-01 regression-freeze guidance.

Findings:
- Validated: inspected trusted tenant resolution strips raw inbound `X-Tenant-Id` and only re-adds it after secure resolution.
- Validated: tenant-admin operational controls consume `ITenantContext.RequiredTenantId` and block global search index mutation from tenant-admin scope.
- Validated: affiliate public projection and repair use active SQL tenant/product visibility plus tenant-scoped derivative search ids.
- Validated: CommerceHub marketplace product/homepage rebuilds use SQL authority declarations, active tenant checks, and delete/scrub convergence.
- Validated: replay/rebuild/authority/dangerous-primitive decisions remain diagnosable through convergence telemetry.
- Weakness hardened: storefront CMS page projection now has the replay and dangerous-primitive guardrails previously documented as remaining debt.

Intentional remaining debt:
- This is certification and targeted hardening, not a universal tenant framework. Other CMS-adjacent, SEO, smart-offer, online-customer, notification, analytics, support-cache, feed/export, report rebuild, cache warmer, and global runbook paths still need path-specific certification before universal tenant-isolation claims.
- Some paths continue to use equivalent local SQL checks rather than shared helpers; future docs and tests must distinguish actual helper adoption from equivalent controls.

## 2026-05-24 SUPERADMIN-CON-01 Platform Authority Certification & Dangerous Global Primitive Freeze

Problem:
- Super-admin/platform-admin authority is intentionally global and allowed to run marketplace-wide moderation, tenant management, platform recovery, and dangerous operational primitives.
- The authentication boundary already used a dedicated JWT scheme and SQL-backed login/refresh, but normal protected requests validated super-admin JWT claims without recomposing current SQL `SuperAdmins` and governance state.
- A stale access token could therefore remain valid until expiry after logout, refresh-token replay revocation, password change, account deletion, or governance restriction.

Production rule:
- Super-admin JWT claims are candidate evidence only. Current SQL `SuperAdmins`, current `TokenVersion`, and current super-admin governance status decide platform authority for every `RequireSuperAdmin` endpoint.
- Global/all-tenant operations remain allowed only as explicit platform authority or documented runbook scope. Tenant-admin tooling must not silently widen to global mutation.
- Dangerous global primitives must still name SQL authority, preserve replay/freshness/fail-closed behavior, emit diagnostics, and update source/docs/tests when introduced or expanded.

Implemented hardening:
- `SuperAdminPlatformAuthority` reloads current SQL platform actor authority and rejects invalid claims, missing SQL rows, stale token versions, and non-active governance profiles.
- Super-admin access tokens now include `SuperAdminSecurityConstants.TokenVersionClaim`.
- `SuperAdminPlatformAuthorityMiddleware` guards every endpoint with `RequireSuperAdmin`, including legacy platform-policy controllers outside `/api/super-admin/*`.
- `SuperAdminManagementService.UpdatePasswordAsync` now clears refresh tokens and increments token version, collapsing stale access-token replay.
- `SuperAdminAuthService.RefreshAsync(...)` now re-runs current governance login eligibility before issuing replacement platform tokens; governance-restricted refresh attempts clear stored refresh authority and fail closed.
- `UserGovernanceService.ApplyActionAsync(...)` now revokes stored platform refresh authority when a super-admin moves out of `Active`, so governance actions converge directly into refresh/session invalidation.
- `OperationalConvergenceAuthority.PlatformAdmin` classifies platform-authority failures in low-cardinality convergence telemetry.
- `SuperAdminPlatformAuthorityTests` and `SuperAdminPlatformAuthorityCertificationTests` freeze runtime behavior, middleware registration, token-version claims, documentation, and platform-authority markers.
- Findings are documented in `docs/architecture/super-admin-platform-authority-certification-findings.md`; runbooks and dangerous primitive guidance now include SUPERADMIN-CON-01 procedures.

Findings:
- Validated: tenant-admin global search index deletion/recreation remains blocked.
- Validated: inspected dangerous primitive source guards and derivative runtime guards remain active.
- Validated: global/all-tenant repair remains intentionally flexible under platform/runbook scope.
- Weakness hardened: stale super-admin access-token reuse after current SQL authority changed now fails closed.

Intentional remaining debt:
- This is not enterprise IAM, SSO, ABAC, break-glass support impersonation, SIEM, universal audit queue adoption, adaptive global scheduling, or fixed rebuild SLO certification.
- Older platform-policy endpoints outside `/api/super-admin/*` are actor-recomposed by the new middleware, but some still rely on structured logs rather than durable `ISuperAdminAuditService` records.
- Future global repair scripts, cache warmers, feed/export/report rebuilders, support tools, and emergency primitives need path-specific authority, dangerous-primitive, telemetry, and documentation tests when changed or expanded.

## 2026-05-24 SUPERADMIN-CON-01 Phase 2 Marketplace Governance & Cross-Tenant Visibility Integrity

Problem:
- Marketplace product read-model rebuilds were SQL-product-authoritative but did not require a current published SQL `CommerceHubListing`. A replayed product event could therefore preserve marketplace product visibility even after listing unpublish/delete or moderation removed listing publication authority.
- Homepage projection used a processed-event check and wrote the processed marker after projection side effects. Duplicate active delivery could repeat homepage rebuild side effects before the duplicate marker existed.
- CommerceHub search writes used listing-only Elasticsearch document ids, which made tenant ownership implicit in the SQL GUID rather than explicit in derivative search identity.
- Super-admin homepage authoring validated curated listing references against active product/listing/category rows but did not use the full current public variant lineage or active tenant lineage predicate.

Production rule:
- CommerceHub marketplace aggregation is global, but visibility is tenant-owned. Current SQL `CommerceHubListings`, tenant, product, variant, category, stock, price, media, and publication authority decide public marketplace visibility.
- Homepage/recommendation references are governance references only. Rebuilds and public reads must remove stale references when SQL no longer proves visibility.
- CommerceHub search document ids for tenant-owned listings must include tenant scope for new writes, and replay/reindex cleanup must scrub legacy listing-only ids.
- Duplicate homepage/recommendation replay must collapse before Mongo, Redis, or search side effects.

Implemented hardening:
- `MarketplaceProductReadModelProjectionService` now requires a live published SQL listing for a current public variant before upserting marketplace product read-model visibility. Missing listing authority deletes the tenant-scoped product read model if fresh.
- Marketplace read-model category identity now comes from the live SQL CommerceHub category attached to the listing.
- `HomepageProjectionConsumer` now acquires `ProjectionConsumerEventFence` before rebuild side effects and completes it after successful projection.
- Super-admin homepage validation, homepage read hydration, and homepage projection rebuilds now filter curated CommerceHub listing references through current public variant lineage and active tenant lineage.
- CommerceHub Elasticsearch new writes use tenant-scoped document ids, and tenant-aware delete/reindex paths delete both tenant-scoped and legacy listing-only ids.

Current evidence:
- `PointOfSale.Infrastructure/ReadModels/Marketplace/MarketplaceReadModelProjectionServices.cs`
- `PointOfSale.Infrastructure/Consumers/Marketplace/HomepageProjectionConsumer.cs`
- `PointOfSale.API/SuperAdmin/Services/SuperAdminHomepageSectionManagementService.cs`
- `PointOfSale.API/Services/Homepage/HomepageSectionReadService.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Documents/CommerceHubProductSearchDocument.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchIndexService.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchReadService.cs`
- `docs/architecture/marketplace-governance-certification-findings.md`

Validation:
- Focused validation passed on 2026-05-24 with `dotnet build PointOfSale.Tests/PointOfSale.Tests.csproj -nologo -v minimal -m:1`.
- Focused validation passed on 2026-05-24 with `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-build -nologo -v minimal --filter "FullyQualifiedName~MarketplaceReadModelProjectionTests|FullyQualifiedName~CommerceHubSearchIndexServiceCompatibilityTests"`.

Intentional remaining debt:
- This is not a universal marketplace projection framework, personalized recommendation engine, enterprise moderation workflow, SIEM, adaptive rebuild SLO, or path-specific certification for every review/localization/feed/cache-warmer/global-runbook path.
- Legacy listing-only search documents may remain until tenant-aware replay/reindex/delete paths scrub them.

## 2026-05-24 SUPERADMIN-CON-01 Phase 3 Replay / Rebuild / Recovery Safety

Problem:
- Tenant-admin CommerceHub search recovery invoked the same global CommerceHub reindex service used for all-listing/platform recovery.
- CommerceHub search SQL overlay recomposed listing state, but its explicit rebuild authority declaration did not name `CommerceHubListing`.
- CommerceHub reindex pacing used fixed constants, making operational recovery pressure less tunable than adjacent outbox workers.

Production rule:
- Tenant-admin recovery must stay tenant-scoped. Global/all-tenant rebuilds, forced replay, global cache/search repair, and alias promotion remain platform/runbook primitives.
- CommerceHub search recovery must be SQL-listing-authoritative. Product/variant/category authority alone cannot restore marketplace search visibility.
- Rebuild interruption, restart, and overlap must converge by redeclaring SQL authority and using bounded pacing.

Implemented hardening:
- `AdminCommerceHubController` now calls `ReindexTenantAsync` using `ITenantContext.RequiredTenantId`.
- `CommerceHubReindexService` adds tenant-scoped SQL-first reindex over tenant-owned Mongo documents and tenant-owned SQL listing seeds. Global `ReindexAsync` remains available for platform/runbook scope.
- `CommerceHubSearchSqlAuthorityOverlay` includes `SqlAuthorityOverlayKind.CommerceHubListing`.
- `CommerceHubReindexOptions` provides bounded parallel batch size, search-status batch size, and inter-batch delay controls.
- Tests freeze tenant/global recovery separation, listing-authority declaration, pacing controls, dead-letter reset behavior, and documentation coverage.

Current evidence:
- `PointOfSale.API/Controllers/AdminCommerceHubController.cs`
- `PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs`
- `PointOfSale.Infrastructure/Jobs/CommerceHubReindexOptions.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Tests/Architecture/SuperAdminRecoverySafetyCertificationTests.cs`
- `PointOfSale.Tests/Infrastructure/MarketplaceEventOutboxProcessorTests.cs`

Intentional remaining debt:
- No adaptive global scheduler, fixed rebuild SLO, universal dead-letter UI, SIEM-grade audit store, or universal certification for feed/export/ranking/cache-warmer/global script paths was introduced.

## 2026-05-24 SUPERADMIN-CON-01 Phase 4 Auditability, Escalation & Emergency Governance

Problem:
- Prior platform authority hardening made every `RequireSuperAdmin` endpoint current-SQL backed, but older platform-policy mutation endpoints outside `/api/super-admin/*` still depended on local structured logs unless their domain explicitly called `ISuperAdminAuditService`.
- Emergency recovery and support-governance documentation needed a durable findings artifact covering actor attribution, emergency override semantics, support/impersonation boundaries, rollback governance, and remaining auditability debt.

Production rule:
- Non-read `RequireSuperAdmin` operations must leave request-level operational audit after current SQL platform actor authority succeeds.
- Emergency/global operations remain intentionally flexible under platform authority, but must name SQL authority, preserve replay/rebuild convergence, expose diagnostics, and avoid derivative-state rollback.
- Support impersonation/tenant override remains a dangerous primitive until a reviewed SQL actor authority, tenant lineage, reason/reference, revocation, audit, and replay-safe ceremony exists.

Implemented hardening:
- Added `SuperAdminOperationalAuditMiddleware` and registered it immediately after `SuperAdminPlatformAuthorityMiddleware`.
- The middleware audits `POST`, `PUT`, `PATCH`, and `DELETE` requests that carry the `RequireSuperAdmin` policy, including legacy platform-policy controllers outside `/api/super-admin/*`.
- Audit records use `SuperAdminPlatformAuthority.SuperAdminId`, `Email`, and `TokenVersion` context items, so actor attribution comes from current SQL recomposition rather than JWT claims alone.
- Added `SuperAdminOperationalAuditMiddlewareTests` and `EmergencyGovernanceCertificationTests` for runtime audit behavior, middleware ordering, legacy platform-policy endpoint authority, and documentation coverage.
- Added `docs/architecture/emergency-governance-certification-findings.md` and updated dangerous primitive/runbook/super-admin/marketplace documentation.

Current evidence:
- `PointOfSale.API/SuperAdmin/Infrastructure/SuperAdminOperationalAuditMiddleware.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.Tests/SuperAdmin/SuperAdminOperationalAuditMiddlewareTests.cs`
- `PointOfSale.Tests/Architecture/EmergencyGovernanceCertificationTests.cs`
- `docs/architecture/emergency-governance-certification-findings.md`

Intentional remaining debt:
- No enterprise IAM/SSO/ABAC, support break-glass ceremony, SIEM-grade immutable audit lake, request-body audit capture, or universal domain-specific audit records for every older platform endpoint was introduced.
- Request-level platform audit complements domain audit and telemetry; it does not replace financial ledgers, reconciliation records, domain-specific audit tables, or convergence diagnostics.

## 2026-05-24 SUPERADMIN-CON-01 Phase 5 Super-Admin Operational Chaos Certification

Problem:
- Final certification needed to validate the platform authority layer under combined replay storms, rebuild overlap, stale emergency sessions, marketplace visibility chaos, moderation/review replay, dangerous primitive misuse, and emergency recovery pressure.
- Review-triggered marketplace search sync still had a narrow resurrection gap: review aggregates were SQL-derived, but the search sync mapped an existing `CommerceHubProductDocument` directly into Elasticsearch rather than first applying current SQL CommerceHub listing authority.

Production rule:
- Super-admin chaos conditions are convergence pressure, not authority. Current SQL platform actor authority gates global operations, and current SQL CommerceHub listing/product/variant/category/tenant authority gates marketplace visibility.
- Review, moderation, homepage, recommendation, rebuild, reindex, and recovery inputs are evidence only. They may trigger recomposition, but missing SQL authority must delete/tombstone/no-op/fail closed instead of restoring visibility from derivative state.

Implemented hardening:
- `MarketplaceProductReviewProjectionService.SyncSearchAsync` now calls `CommerceHubSearchSqlAuthorityOverlay.ApplyAsync` before review-triggered CommerceHub search indexing.
- Missing current SQL listing authority now calls tenant-scoped `ICommerceHubSearchIndexService.DeleteAsync(tenantId, listingId)` instead of indexing the stale Mongo document.
- Review-triggered search sync status updates now filter by both listing id and tenant id.
- `SemanticConvergenceChaosValidationTests` now covers stale/revoked/governance-restricted super-admin sessions, duplicate replay collapse after fence completion, deterministic rebuild overlap declarations, and dangerous primitive misuse rejection.
- `SuperAdminOperationalChaosCertificationTests` freezes source and documentation invariants for moderation/review replay SQL overlay use, SQL-first CommerceHub reindex, normal dead-letter retry, request audit ordering, dangerous primitive fail-closed behavior, and Phase 5 findings documentation.

Current evidence:
- `PointOfSale.Infrastructure/Services/MarketplaceReviews/MarketplaceProductReviewBackboneServices.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Tests/Infrastructure/SemanticConvergenceChaosValidationTests.cs`
- `PointOfSale.Tests/Architecture/SuperAdminOperationalChaosCertificationTests.cs`
- `docs/architecture/super-admin-operational-chaos-certification-findings.md`

Intentional remaining debt:
- This pass does not introduce enterprise IAM, support break-glass ceremony, SIEM-grade audit, adaptive global scheduling, fixed rebuild SLOs, or a universal chaos framework.
- Marketplace review search convergence is hardened, but broader review exports, localization, personalized ranking, feeds, cache warmers, support tools, reporting/analytics, and future global scripts still need path-specific certification when expanded.

## 2026-05-24 POS-ADMIN-CERTIFICATION-01 PASS 1 Authentication, Session And Authority Certification

Problem:
- POS refresh credentials were stored as raw token material and ordinary POS bearer authentication did not bind accepted access tokens to current SQL session state.
- Logout cleared refresh state but an unexpired signed POS access token did not have a deterministic current-SQL revocation check outside the role-protected admin middleware boundary.

Production rule:
- POS access tokens, roles, tenant claims, and refresh credentials are evidence only. Current SQL user/session/tenant/governance authority must accept the session before POS operation.
- Session rotation and logout must converge deterministically to one current POS session binding per user; stale session evidence fails closed.

Implemented hardening:
- Added `PosSessionAuthority` and wired it into default POS JWT `OnTokenValidated`.
- `AuthController`, tenant-owner onboarding issuance, and idempotent onboarding-session restoration now store hash-only refresh session bindings and issue `pos_session` access-token claims; refresh rotates the binding and logout revokes it even without a refresh cookie.
- Configured `Users.RefreshToken` as an EF concurrency token so overlapping rotation/revocation attempts cannot silently restore stale authority.
- Added telemetry classifications, runtime tests, architecture freeze tests, and `docs/architecture/admin-authentication-session-certification-findings.md`.
- Re-certified on 2026-05-26 that legacy POS access tokens without `pos_session` and expired SQL-bound current sessions fail closed before tenant-admin authority can proceed.
- The same 2026-05-26 re-certification also hardened cached onboarding replay: replayed registration success now revalidates current SQL owner activity, tenant ownership, active tenant lineage, and tenant governance before restoring session cookies.

Intentional remaining debt:
- The POS model remains one current session per user, without per-device inventory or refresh-family compromise revocation.
- No enterprise IAM/SSO/MFA/ABAC, production support impersonation, or POS password-reset/session-management ceremony was introduced.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 2 Tenant Isolation And Admin Scope Certification

Problem:
- Tenant-admin Commerce AI, AI assistance, abandoned-checkout visibility, storefront footer, and abandoned-cart recovery surfaces still derived tenant scope from controller-local parsing of the middleware compatibility `X-Tenant-Id` header.
- The abandoned-cart recovery-log endpoint has a legitimate external callback mode, but its authenticated admin mode skipped callback-key validation while accepting the body tenant without binding it to trusted request context.

Production rule:
- Tenant-admin operational scope must come from `ITenantContext.RequiredTenantId` after current session and actor authority validation; compatibility headers are not an authority API.
- An external callback may identify tenant scope only under its tenant-specific callback-key contract. An authenticated tenant actor cannot use that external shape to cross tenant boundaries.

Implemented hardening:
- Migrated the inspected admin AI, Commerce AI, abandoned-checkout, storefront footer, and abandoned-cart recovery administration paths to trusted tenant context.
- Added fail-closed `admin_tenant_scope_mismatch` rejection for authenticated recovery-log requests whose payload tenant differs from current trusted context.
- Blocked tenant-admin CommerceHub alias finalization and migration catch-up behind `GLOBAL_SEARCH_CONTROL_FORBIDDEN`; tenant-admin CommerceHub search repair now exposes only tenant-scoped reindex while global alias promotion/catch-up remain platform/runbook primitives.
- Migrated inspected tenant-admin accounting, storefront-auth-provider, wishlist-analytics, pixel-tracking, online-customer-session, online-customer, and payment-method controllers to `ITenantContext.RequiredTenantId`.
- Added explicit tenant predicates on the inspected sensitive online-customer and payment-method SQL paths so certification-critical customer/session/wishlist/payment-method operations do not depend on ambient query filters alone.
- Tenant-admin WhatsApp operational tooling now uses `ITenantContext.RequiredTenantId`, and the inspected account/template lookup, enable/disable, and delete service methods bind tenant scope explicitly in SQL instead of depending on ambient query filters.
- Expanded `TenantIsolationCertificationFreezeTests`, strengthened `SuperAdminRecoverySafetyCertificationTests`, and added `AdminCommerceHubScopeTests` runtime coverage.

Replay/rebuild and remaining debt:
- No replay/rebuild, cache/search, or marketplace visibility authority was loosened; existing SQL-first recomposition and tenant-scoped derivative guarantees remain in force.
- Anonymous callback operation remains intentionally available behind tenant-specific API-key verification and idempotency handling.
- Non-admin storefront/checkout/webhook paths and future support/analytics/cache-warmer/report/export tooling remain path-specific certification debt.

2026-05-26 follow-up hardening:
- `AdminAffiliateController` no longer parses JWT `tenant_id` claims to choose tenant scope. The reviewed affiliate admin surface now uses `ITenantContext.RequiredTenantId`, preserving the existing SQL relationship checks while removing claim-derived tenant override drift.
- `CouponsController` tenant-admin list/detail/update/delete/toggle paths now bind trusted tenant scope explicitly in SQL and no longer use `FindAsync(...)` for coupon ids. Foreign-tenant coupon ids now fail closed with `404` instead of depending on ambient query filters or key-lookup behavior.
- `AuditLogsController` and `VendorPayoutMethodsController` now consume required trusted tenant context on the reviewed admin paths. `TenantIsolationCertificationFreezeTests`, `AdminAffiliateTenantScopeTests`, and `CouponsControllerCqrsConsistencyTests` freeze the new guarantees.
- Additional 2026-05-26 PASS 2 hardening closed three adjacent scope gaps on the same certification boundary: `CategoriesController` now binds tenant scope explicitly for list/detail/create/update/delete and tenant-local default-category seeding; `WhatsAppAccountService.UpdateAccountAsync(...)` and `UpdateTemplateAsync(...)` now require both `Id` and `TenantId`; and `AdminAffiliateCategoryController` global taxonomy mutation now requires `RequireSuperAdmin` policy instead of tenant-owner scope.
- Additional 2026-05-26 PASS 2 reporting hardening closed the reviewed dashboard/reporting drift on the same certification boundary: `DashboardController` now consumes `ITenantContext.RequiredTenantId` and binds tenant scope explicitly in SQL for sales/product/customer/supplier/stock/payment aggregates, while `FinancialReportsController` now uses trusted required tenant context consistently before invoking the ledger-backed report service boundary.
- Additional 2026-05-26 PASS 2 finance/shipping hardening closed another tenant-owned admin SQL drift cluster: `BanksController`, `ExpenseCategoriesController`, `FinancialAccountsController`, and `ShippingMethodsController` now consume trusted tenant context and bind reviewed row selection explicitly in SQL for list/detail/update/delete/toggle/statement/opening-balance/parent-account/shipping-calculation paths.
- Bank statements now require tenant-owned bank authority plus tenant-owned transaction rows, financial-account parent validation is tenant-local, and shipping update/delete/calculation cannot publish events or compute prices for foreign-tenant shipping rows.
- Focused validation also passed on 2026-05-26 with `DashboardControllerTenantScopeTests`, `FinancialReportsControllerTenantScopeTests`, and the expanded `TenantIsolationCertificationFreezeTests`.
- Focused validation passed on 2026-05-26 with `AdminTenantOperationalScopeTests` and the expanded `TenantIsolationCertificationFreezeTests`.
- Focused validation passed on 2026-05-26 with `CategoriesControllerTenantScopeTests`, `WhatsAppAccountServiceTenantScopeTests`, `AdminAffiliateCategoryGovernanceTests`, `TenantIsolationCertificationFreezeTests`, and `SuperAdminPlatformAuthorityCertificationTests`.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 3 Product / Inventory / Reservation Certification

Problem:
- Prior hardening already established current variant semantic ownership, inventory lineage authority, checkout authority guards, stock-sync stale fanout suppression, and fail-closed reservation consumption. Certification found two narrower residual gaps on the marketplace payment/recovery boundary.
- `InventoryConsistencyService.EnsureMarketplaceOrderReservationsActiveAsync(...)` filtered active reservations through a single tenant derived from the first marketplace order item. That was incorrect for marketplace orders spanning multiple tenant-owned suborders.
- `CommerceHubPaymentRecoveryAuthorityService` duplicated inventory authority checks locally and could treat raw active reservations plus raw stock rows as sufficient payment-retry evidence without reusing the shared current-lineage filter or current stock-owner filter.

Production rule:
- Marketplace reservation/payment recovery authority must converge from the same SQL-first inventory lineage model as checkout and fulfillment consumption.
- Marketplace reservation validation must support multi-tenant marketplace order composition explicitly and must require exact current SQL order-item ownership plus exact reserved quantity.
- Raw reservation rows, raw stock rows, CommerceHub order state, and public stock visibility are evidence only until current SQL variant/product/warehouse/stock/reservation lineage accepts them.

Implemented hardening:
- Added `ProductVariantInventoryLineageAuthority.FilterCurrentConsumableReservationsAcrossTenantsAsync(...)` so reviewed marketplace paths can validate current consumable reservation lineage per tenant without weakening tenant-scoped inventory authority.
- `InventoryConsistencyService.EnsureMarketplaceOrderReservationsActiveAsync(...)` now uses that cross-tenant filter, rejects foreign-owner active reservations, and raises a distinct fail-closed authority-loss error when raw active reservation rows exist but current SQL lineage no longer accepts them.
- `CommerceHubPaymentRecoveryAuthorityService` now requires current SQL product publication visibility, current variant semantic ownership, current consumable reservation lineage across tenants, and current stock ownership through `WhereCurrentInventoryStockOwner(...)` before allowing payment retry.
- `InventoryConsistencyService.ReserveOrderItemAsync(...)` now filters candidate stock rows through current stock-owner lineage before accepting availability and fails closed with `MARKETPLACE_RESERVATION_STOCK_AUTHORITY_LOST` when raw stock still exists only on stale warehouse or stock-owner lineage. Its stock-row lock ordering is now deterministic by `WarehouseId`/`Id`.
- `InventoryConsistencyService.ReceiveReturnAsync(...)` now requires current stock ownership before applying returned resellable or damaged quantities, blocking inactive-warehouse and superseded-variant return replay from mutating SQL stock.
- `InventoryConsistencyService.CommitDeliveryAsync(...)` now checks current SQL inventory lineage directly instead of reusing the reservation-creation publication guard, so marketplace unpublish/offline drift cannot block stock consumption for an already-authorized order item.
- PASS 3 addendum on 2026-05-27 closes a remaining legacy availability and derivative-total gap on the reviewed surfaces. `ProductVariantStockRepository`, `ProductVariantStockMutationSupport`, `InventoryConsistencyService`, `ProductVariantProjectionConsumer`, and `CommerceHubProjectionConsumer` now recompose reviewed availability or total stock only through current stock-owner lineage rather than summing undeleted stock rows directly.
- PASS 3 addendum on 2026-05-27 also closes a reviewed SQL fallback/storefront-search residual gap. `ProductVariantReadRepository` SQL fallback now uses `WherePubliclyVisibleLineage(...)` plus `WhereCurrentInventoryStockOwner(...)`, and `ProductSearchSqlAuthorityOverlay` now includes warehouse lineage when recomposing variant stock totals for storefront/search visibility repair.
- This means reviewed cart/checkout availability, stock-sync/event fanout totals, storefront variant projection totals, and CommerceHub variant projection totals can no longer be inflated by inactive-warehouse or stale stock-owner lineage that survived only as raw SQL stock rows.
- This also means Mongo-miss SQL fallback checkout enrichment and search/read-model rebuild overlay cannot restore superseded/default-stale variants or inflate storefront/search stock from inactive-warehouse rows simply because raw SQL variant/stock rows still exist.
- Added focused regression tests for inactive warehouse retry rejection, cross-tenant marketplace reservation acceptance, return receipt rejection on inactive warehouse/superseded variant, and source-freeze assertions that reviewed reservation, recovery, and return paths keep using the shared lineage/current stock-owner filters.

Current evidence:
- `PointOfSale.Infrastructure/Reliability/ProductVariantInventoryLineageAuthority.cs`
- `PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentRecoveryAuthorityService.cs`
- `PointOfSale.Tests/CommerceHub/CommerceHubPaymentRecoveryAuthorityServiceTests.cs`
- `PointOfSale.Tests/CommerceHub/InventoryConsistencyServiceTests.cs`
- `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs`
- `docs/architecture/product-inventory-reservation-certification-findings.md`

Validation:
- Focused validation passed on 2026-05-25 with `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj -nologo -v minimal --filter "FullyQualifiedName~CommerceHubPaymentRecoveryAuthorityServiceTests|FullyQualifiedName~InventoryConsistencyServiceTests|FullyQualifiedName~CheckoutConcurrencyHardeningSourceTests.CheckoutCon07_MarketplaceReservationReplay_MustFenceOwnershipOverReservationAndRecovery"`.

Intentional remaining debt:
- No universal physical stock-transfer, reservation-migration, warehouse-reassignment, bulk inventory import, ERP stock writeback, or generalized inventory migration framework was introduced.
- Future specialized inventory repair/reconciliation/export paths must still define SQL transaction boundary, lock order, replay/rebuild semantics, dangerous-primitive governance, and diagnostics in the same change.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 5 Marketplace / Public Visibility Certification

Problem:
- CommerceHub search already validated CommerceHub category parent lineage before indexing, but adjacent marketplace product read-model rebuilds, homepage/recommendation projection, homepage public reads, super-admin homepage validation, CommerceHub query category/suggestion filtering, and reindex verification still had leaf-category `IsActive` checks in places.
- That mismatch could allow stale homepage/recommendation or marketplace read-model visibility to survive through an active leaf category whose parent lineage was inactive, missing, self-referential, or cyclic, even while search failed closed.
- CommerceHub product-detail reads still trusted a published Mongo listing document plus Mongo storefront product detail data without reapplying `CommerceHubSearchSqlAuthorityOverlay`. A stale listing document could therefore continue serving stale CommerceHub category identity, stale SQL-deleted visibility, or legacy variant image strings on the detail surface even after search/read-model rebuild paths had converged fail closed.

Production rule:
- Current SQL `CommerceHubListings` plus current tenant/product/variant/stock/price/media/publication and live CommerceHub category parent lineage decide public marketplace visibility.
- CommerceHub category ids in listing documents, homepage JSON, recommendations, search hits, cached suggestions, and repair inputs are evidence only.
- Invalid CommerceHub category lineage must converge to delete, tombstone, scrub, validation rejection, no-op, or reindex-count exclusion rather than public visibility preservation.

Implemented hardening:
- Added `CommerceHubCategoryLineageAuthority` as the shared SQL parent-chain authority for CommerceHub categories.
- CommerceHub search overlay and listing projection now use the shared helper instead of local duplicated lineage walks.
- Marketplace product read-model rebuild now deletes visibility when the selected live SQL listing points at invalid CommerceHub category lineage.
- Homepage/recommendation projection, public homepage reads, and super-admin homepage validation now scrub or reject curated listing/category references through the shared category lineage helper.
- CommerceHub query suggestion/category filtering and CommerceHub reindex verification now use the same category lineage semantics as search mutation.
- CommerceHub search auto-healing now deletes stale visibility with tenant-scoped search identity when SQL authority is missing and still rebuilds only through `CommerceHubSearchSqlAuthorityOverlay`.
- CommerceHub search analytics popularity replay now recomposes SQL listing/category/product visibility through `CommerceHubSearchSqlAuthorityOverlay` before any Elasticsearch index/delete mutation and fails closed with tenant-scoped delete when SQL no longer proves visibility.
- CommerceHub search overlay, marketplace product read-model rebuild, and product-update listing fanout now retain public images only when current SQL `ProductMedia`/`ProductVariants.MediaId` proves the image. Legacy product/variant image strings and derivative image fields no longer repopulate public visibility after SQL media deletion.
- CommerceHub search overlay now recomposes `AverageRating` and `ReviewCount` from current approved, non-deleted SQL `ProductReviews`, so moderation/review/reindex overlap cannot preserve a stale derivative review summary in public search.
- `CommerceHubQueryService.GetProductByIdAsync(...)` now re-applies `CommerceHubSearchSqlAuthorityOverlay` before serving Mongo-backed detail data, uses the SQL-overlaid listing category identity instead of storefront product category drift, and no longer falls back to legacy variant `ImageUrl` strings when authoritative media is absent.
- Runtime tests cover marketplace product read-model deletion and homepage curated category scrubbing when a leaf category remains active under an inactive parent. Architecture tests freeze helper use on search, homepage, and marketplace read-model boundaries.

Current evidence:
- `PointOfSale.Infrastructure/Reliability/CommerceHubCategoryLineageAuthority.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs`
- `PointOfSale.Infrastructure/ReadModels/Marketplace/MarketplaceReadModelProjectionServices.cs`
- `PointOfSale.API/Services/Homepage/HomepageSectionReadService.cs`
- `PointOfSale.API/SuperAdmin/Services/SuperAdminHomepageSectionManagementService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubSearchAnalyticsBackgroundService.cs`
- `PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs`
- `PointOfSale.Tests/Infrastructure/MarketplaceReadModelProjectionTests.cs`
- `PointOfSale.Tests/Infrastructure/ProductVariantLifecycleAuthorityTests.cs`
- `PointOfSale.Tests/Architecture/MarketplaceGovernanceCertificationTests.cs`
- `PointOfSale.Tests/Architecture/GlobalReliabilityInfrastructureExtractionTests.cs`
- `docs/architecture/marketplace-governance-certification-findings.md`

Intentional remaining debt:
- This pass does not introduce personalized ranking, feed/export/cache-warmer certification, enterprise moderation workflow, adaptive rebuild SLOs, SIEM-grade audit, support impersonation, or a universal marketplace projection framework.
- Normal derivative propagation lag and derivative popularity/click scoring remain intentional; this pass does not promote those signals to visibility authority.
- Future ranking, feed/export, localization, review-export, support, analytics/reporting, cache-warmer, and global script paths must adopt SQL listing/category/media/moderation recomposition with replay/rebuild tests before public visibility mutation.

2026-05-27 PASS 5 suggestion-term follow-up:
- Follow-up review found one narrower residual proof on the already-hardened CommerceHub suggestion boundary. Product suggestion cards already revalidated through `CommerceHubSearchSqlAuthorityOverlay`, but the final “term still live?” probe for popular/similar/autocomplete terms still accepted raw SQL candidate matches with only active-variant checks.
- That meant a superseded variant or a vendor suspended from new orders could keep a text suggestion alive after product-card visibility had already converged fail closed.
- `CommerceHubQueryService.HasLiveCommerceHubSuggestionTermAsync(...)` now treats text-term survival as a public-visibility decision: it loads bounded SQL listing candidates, suppresses governance-hidden and vendor-financial-suspension candidates, and requires at least one candidate to survive `CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(...)` before the term is returned.
- Added focused runtime regression tests for superseded-variant term suppression and vendor-suspension term suppression, plus source-freeze coverage that the query service keeps using the shared SQL overlay and vendor-visibility loaders on the inspected suggestion paths.

2026-05-27 PASS 5 public vendor-visibility follow-up:

Problem:
- CommerceHub search and suggestion responses already suppressed governance-hidden vendors and vendors suspended from new orders, but adjacent public marketplace reads did not apply the same runtime vendor-visibility proof consistently.
- Marketplace product list/detail reads, homepage product sections, and super-admin homepage curated-reference validation could therefore preserve stale public visibility for a vendor that search had already failed closed.

Production rule:
- Current vendor suppression is part of CommerceHub public visibility authority, not a search-only heuristic.
- Marketplace product list/detail reads, homepage/recommendation reads, curated-reference validation, and equivalent rebuild/recovery verification must suppress vendors hidden by current governance or suspended from new orders, even when listing/category/product SQL authority still exists.

Implemented hardening:
- Added `CommerceHubPublicVendorVisibilityService` as the shared runtime boundary for marketplace public vendor suppression using current SQL governance rows plus current vendor-financial "suspended from new orders" evidence.
- `MarketplaceReadController.GetProducts(...)` now version-keys its cache by current marketplace visibility state, loads matching vendor ids, suppresses hidden/suspended vendors before paging, and keeps public pagination tenant-safe under replay/rebuild overlap.
- `MarketplaceReadController.GetProduct(...)` now rechecks current vendor suppression before serving a cached/public detail response and fails closed with `NotFound` when the vendor is no longer publicly visible.
- `MarketplaceReadController.GetHomepage(...)`, `HomepageSectionReadService`, and `SuperAdminHomepageSectionManagementService` now scrub or reject curated product references through the same current vendor-visibility boundary, so stale homepage JSON or cached sections cannot keep a hidden/suspended vendor public.
- Added focused runtime tests for suppressed-vendor product-list filtering, detail fail-closed behavior, homepage curated-product scrubbing, and super-admin curated-reference rejection, plus architecture freeze assertions that the public marketplace controller and homepage services keep using the shared vendor-visibility boundary.

Current evidence:
- `PointOfSale.API/Services/CommerceHub/CommerceHubPublicVendorVisibilityService.cs`
- `PointOfSale.API/Controllers/MarketplaceReadController.cs`
- `PointOfSale.API/Services/Homepage/HomepageSectionReadService.cs`
- `PointOfSale.API/SuperAdmin/Services/SuperAdminHomepageSectionManagementService.cs`
- `PointOfSale.Infrastructure/MongoDB/Services/Marketplace/MarketplaceProductReadRepository.cs`
- `PointOfSale.Tests/Infrastructure/MarketplaceReadControllerTests.cs`
- `PointOfSale.Tests/SuperAdmin/SuperAdminHomepageSectionManagementServiceTests.cs`
- `PointOfSale.Tests/Architecture/MarketplaceGovernanceCertificationTests.cs`
- `docs/architecture/marketplace-governance-certification-findings.md`

Intentional remaining debt:
- This closes the reviewed public product/homepage/curation gap, not every future ranking, review-export, feed/export, localization, analytics, cache-warmer, support, or global-script path that may preserve marketplace visibility.
- Vendor suppression remains a visibility gate, not a replacement for SQL listing/category/product/media/stock/publication authority. Future public surfaces must keep both classes of checks.

2026-05-27 PASS 5 search-read suppression follow-up:
- Follow-up review found one narrower read-surface drift after the public vendor-visibility hardening. CommerceHub browse/search item shaping and term validation already suppressed hidden/suspended vendors, but the upstream search/suggestion/autocomplete request shaping and cached suggestion-product revalidation still handled those suppression modes inconsistently.
- That mismatch let public totals, facet inputs, and cached suggestion/autocomplete product payloads temporarily reflect vendors that the final product-card layer would later strip, which is a public-visibility integrity defect even without cross-tenant bleed.
- `CommerceHubQueryService` now loads the current publicly suppressed vendor set before browse/search/suggestion/autocomplete requests, uses that combined set in the query/cache boundary, and reuses the same combined suppression when revalidating cached or projected suggestion/autocomplete products.
- Added focused runtime regression tests for suspended-vendor exclusion on global browse/search requests plus governance-hidden-vendor suppression on suggestion/autocomplete product revalidation, and source-freeze coverage that the query service keeps using the shared suppressed-vendor loader on the inspected boundary.
- Remaining debt stays explicit: this closes the reviewed CommerceHub query/read drift, not every search-index maintenance or reindex verification path. Future index-maintenance certification should bring the same hidden/suspended-vendor proof into overlay-governed mutation/rebuild verification where required.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 6 Operational Tooling & Dangerous Primitive Certification

Problem:
- `AdminSearchSyncController.SyncProducts(...)` still acted like legacy baseline repair tooling. It rebuilt storefront product search visibility from partial SQL rows and direct Elasticsearch bulk writes instead of redeclaring the current SQL search overlay.
- Under recovery pressure that path could restore incomplete search visibility while skipping unit-authoritative storefront price, fail-closed category-lineage suppression, and shared SQL search enrichment.
- `AdminSyncController` had already moved product projection rebuild through `IProductSqlAuthorityProjectionWriter`, but post-rebuild cache clearing still enumerated and deleted Redis keys directly from the controller instead of using the reviewed tenant-scoped product cache boundary.

Production rule:
- Tenant-admin storefront product search repair is a SQL-first convergence tool, not a partial baseline visibility writer.
- Operational search repair must recompose through `ProductSearchSqlAuthorityOverlay` and map search documents from the shared SQL-authoritative `ProductDocument` shape before Elasticsearch mutation.
- Missing current SQL search authority must converge by delete/tombstone, not by indexing partial rows.
- Product read-model repair must route derivative product cache invalidation through `IProductCacheService`; a tenant-admin controller must not become a direct Redis mutation primitive.

Implemented hardening:
- `AdminSearchSyncController` now loads product ids only, applies `ProductSearchSqlAuthorityOverlay.ApplyAsync(...)` per product, and maps index documents through `ProductSearchDocumentMapper.MapFromProductDocument(...)`.
- Products whose current SQL authority is missing or no longer publicly visible now delete stale search documents instead of indexing baseline documents.
- `AdminSyncController` now delegates bulk and single-product cache invalidation to `IProductCacheService.InvalidateAllProductsAsync(...)` and `InvalidateProductAsync(...)`, removing its direct Redis server/key operations while preserving SQL-authoritative product projection repair.
- `AdminDataSyncController` category and shipping-method sync now declare `SqlTruthRebuildGuard.RequireSqlAuthority(...)` and delete tenant-scoped stale Mongo documents that are missing from the current SQL authority set after the bulk upsert phase, so repeated sync runs converge instead of preserving removed SQL rows as hidden derivative state.
- Added runtime tests proving unit-authoritative storefront price is preserved during admin search repair and invalid SQL category lineage fails closed by delete.
- Added focused runtime tests proving category and shipping-method sync carry current authoritative ids into the stale-delete filter and report the stale-delete result in the operational response payload.
- Added architecture/doc freeze coverage for search-overlay recomposition, reviewed product cache invalidation delegation, and PASS 6 documentation.

Current evidence:
- `PointOfSale.API/Controllers/AdminSearchSyncController.cs`
- `PointOfSale.API/Controllers/AdminSyncController.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Documents/ProductSearchDocumentMapper.cs`
- `PointOfSale.Infrastructure/Redis/Services/IProductCacheService.cs`
- `PointOfSale.Tests/Admin/AdminSearchSyncControllerTests.cs`
- `PointOfSale.Tests/Admin/AdminSyncControllerOperationalTests.cs`
- `PointOfSale.Tests/Architecture/OperationalToolingCertificationFreezeTests.cs`
- `docs/architecture/operational-tooling-certification-findings.md`

Intentional remaining debt:
- This hardens one reviewed tenant-admin storefront search repair surface, not every future search/cache/CMS/SEO/export/support rebuild tool.
- `AdminSearchSyncController.SyncCmsPages(...)` remains intentionally unimplemented rather than serving as a partial recovery primitive; if implemented later it must declare SQL authority, replay behavior, and dangerous primitive governance in the same change.

2026-05-27 PASS 6 stale-Mongo-sync follow-up:
- Follow-up review found one narrower rebuild-convergence gap adjacent to the already-hardened PASS 6 tooling. `AdminDataSyncController` category and shipping-method sync rebuilt current Mongo rows from SQL, but it did not scrub tenant-scoped Mongo documents whose SQL rows had been hard-deleted or otherwise removed from the current authority set.
- That made the endpoint a one-way additive repair primitive: repeated sync runs could preserve stale category or shipping-method visibility even after SQL authority had already removed the row.
- Runtime hardening now treats reviewed category/shipping sync as a convergence tool rather than a migration helper. The controller declares SQL rebuild authority explicitly and deletes stale tenant-scoped Mongo documents after the authoritative bulk upsert phase.
- Added focused runtime tests for stale-delete filter scoping plus source-freeze coverage that PASS 6 tooling keeps the rebuild guard and stale-delete helpers on the inspected boundary.
- Remaining debt stays explicit: this closes the reviewed category/shipping sync slice, not every future tenant-scoped Mongo rebuild, cache warmer, CMS, SEO, export, or support repair path.

2026-05-27 PASS 6 category-search stale-visibility follow-up:
- Follow-up review found one narrower operational-search convergence gap adjacent to the already-hardened PASS 6 tooling. `AdminSearchSyncController.SyncCategories(...)` rebuilt current tenant category search documents from the SQL lineage snapshot and deleted invalid-lineage categories it still saw in SQL, but it did not scrub tenant-scoped Elasticsearch category documents whose source SQL rows were gone entirely.
- That made the endpoint a one-way additive search repair primitive: repeated sync runs could preserve stale tenant-scoped category search visibility even after SQL authority had already removed the row.
- Runtime hardening now treats reviewed tenant-admin category search sync as a convergence tool rather than a row-present-only reindex. The controller declares SQL rebuild authority explicitly, and `IUnifiedSearchIndexService` now deletes stale tenant-scoped category search documents after the authoritative sync pass.
- Added focused runtime tests for stale-search-delete filter scoping plus source-freeze coverage that PASS 6 tooling keeps the rebuild guard and stale-search-delete helpers on the inspected boundary.
- Remaining debt stays explicit: this closes the reviewed tenant-admin category search sync slice, not every future category/search/CMS/SEO/export/support repair path that may mutate Elasticsearch visibility.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 7 Replay / Rebuild / Chaos Certification

Problem:
- Payment-cancellation outbox recovery had bounded batch/retry controls but no active processing lease. Under worker restart or duplicate processor overlap, the same unprocessed row could invoke `IPaymentLifecycleService` more than once before `ProcessedAtUtc` was saved.
- The payment lifecycle service remained the payment authority, but the recovery outbox itself still lacked the active-overlap collapse expected from adjacent accounting, stock-sync, marketplace event, and marketplace projection outboxes.

Production rule:
- Recovery outboxes must declare worker-restart semantics. Duplicate active workers should collapse through a claim lease or equivalent fence before lifecycle/reconciliation side effects.
- Payment cancellation recovery must remain a trigger into `IPaymentLifecycleService`; it must not become a direct payment-status mutation path.

Implemented hardening:
- `PaymentCancellationOutboxMessage` now includes `ClaimedUntilUtc`, `TryClaim`, `MarkProcessed`, and `ClearClaim`.
- `PaymentCancellationOutboxProcessor` filters active claims, claims rows before lifecycle work, uses an order-scoped PostgreSQL advisory transaction lock where available, clears claims on success/manual-review/failure, and records active-overlap collapse telemetry for both lease-visible overlap and row-version claim collisions.
- `PaymentCancellationOutboxOptions.ProcessingLeaseSeconds` and startup validation bound crash pickup and active-overlap behavior.
- Runtime tests cover active-claim skipping and claim clearing after lifecycle convergence; architecture tests freeze PASS 7 source and documentation markers.

2026-05-27 storefront footer replay follow-up:
- Follow-up review found one narrower public-content replay gap adjacent to the certified CMS/navigation/config surfaces. `StorefrontFooterProjectionConsumer` fenced duplicate delivery and recomposed footer rows from SQL, but did not revalidate active tenant lineage before full Mongo replacement.
- A delayed footer event could therefore preserve tenant-scoped public footer visibility after current SQL tenant authority was inactive, deleted, or missing.
- Runtime hardening now checks `TenantLineageAuthority.IsActiveTenantAsync(...)` after replay fencing and before stale-event suppression or footer replacement. Missing tenant authority deletes the tenant-scoped footer document, records tenant-lineage authority failure plus derivative-deletion convergence telemetry, completes the replay fence, and refuses to preserve stale footer visibility from old events or existing Mongo documents.
- Regression protection now freezes the footer replay boundary around tenant-lineage revalidation, fail-closed deletion, and convergence diagnostics.

2026-05-29 Redis invalidation replay follow-up:
- Follow-up review found adjacent cache-side replay amplification on `RedisInvalidationConsumer`. Product cache invalidation already used a durable event fence, but homepage, CMS, footer, marketplace product/order, and marketplace review cache invalidation could repeat Redis key deletion and pattern scans for duplicate or active replay.
- Runtime hardening now acquires `ProjectionEventFence` before those Redis side effects and completes the fence only after invalidation succeeds. Marketplace-domain cache events use the tenant-aware fence overload so duplicate and active-overlap telemetry remains scoped where the event carries tenant evidence.
- This is amplification containment only. Redis remains derivative infrastructure and the fence does not make cache state visibility authority.
- Regression protection now freezes the Redis invalidation consumer around processed-event fencing, duplicate replay collapse, marketplace tenant telemetry, and PASS 7 documentation.

2026-05-29 marketplace projection duplicate-completion telemetry follow-up:
- Follow-up review found that `MarketplaceProjectionOutboxProcessor` already cleaned residual outbox rows after a processed-event unique-key race, but the post-dispatch duplicate-completion branch was still log-only.
- Runtime hardening now records collapsed `OperationalConvergenceTelemetry` when that processed-event unique key proves another worker already completed the same logical projection replay. Residual outbox cleanup remains the replay-safe outcome; the change closes an auditability gap, not a business-authority gap.
- Regression protection now freezes the reviewed processor around duplicate-completion race telemetry and PASS 7 documentation.

Current evidence:
- `PointOfSale.Core/Entities/PaymentCancellationOutboxMessage.cs`
- `PointOfSale.Infrastructure/Payment/PaymentCancellationOutboxProcessor.cs`
- `PointOfSale.Infrastructure/Payment/PaymentCancellationOutboxOptions.cs`
- `PointOfSale.Infrastructure/Consumers/StorefrontFooterProjectionConsumer.cs`
- `PointOfSale.API/Consumers/RedisInvalidationConsumer.cs`
- `PointOfSale.Infrastructure/Reliability/ProjectionEventFence.cs`
- `PointOfSale.Infrastructure/Data/Configurations/PaymentCancellationOutboxMessageConfiguration.cs`
- `PointOfSale.Tests/Infrastructure/PaymentCancellationOutboxOperationalScalabilityTests.cs`
- `PointOfSale.Tests/CommerceHub/MarketplaceReviewInfrastructureTests.cs`
- `PointOfSale.Tests/Architecture/ReplayRebuildChaosCertificationTests.cs`
- `docs/architecture/replay-rebuild-chaos-certification-findings.md`

Intentional remaining debt:
- This pass does not add adaptive throttling, a universal chaos framework, fixed rebuild SLOs, support impersonation, SIEM audit, or universal certification for CMS/SEO/analytics/feed/export/reporting/cache-warmer/global-script paths.

## 2026-05-26 POS-ADMIN-CERTIFICATION-01 PASS 8 Operational Survivability Certification

Problem:
- Several reviewed runtime telemetry surfaces already existed but were not exported through the process OpenTelemetry pipeline, which made long-running operational diagnosis weaker than the code implied.
- Several reviewed exported telemetry surfaces still emitted raw tenant identifiers or unnormalized free-form labels, creating silent cardinality risk under enterprise multi-tenant scrape pressure.
- `CommerceHubReindexOptions` was configuration-bound but not startup-validated, leaving one reviewed recovery pacing surface dependent on unchecked incident configuration.

Production rule:
- Reviewed operational meters are part of the survivability boundary only when they are exported from the process and use bounded dimensions.
- `tenant_bucket` is the approved exported tenant dimension on PASS 8 hardened surfaces; raw tenant ids remain logs/traces/audit evidence only.
- Recovery pacing options on reviewed rebuild/recovery paths must be bounded and startup-validated.

Implemented hardening:
- `Program.cs` now exports reviewed affiliate, search, reservation, and checkout-lock meters in addition to the previously reviewed operational meters.
- Added shared `OperationalTelemetryDimensions` helpers for tenant bucketing and tag normalization.
- Hardened `AffiliateTelemetry`, `AbandonedCartRecoveryTelemetry`, `StorefrontPublishTelemetry`, `MarketplaceTelemetry`, `AnalyticsReliabilityTelemetry`, `SearchTelemetry`, `InventoryReservationTelemetry`, and reviewed `CommerceReliabilityTelemetry` currency/recovery diagnostics to use bounded reviewed labels on exported paths.
- Startup now validates `CommerceHubReindexOptions` bounds for parallel batch size, search-status batch size, and inter-batch delay.
- Added focused runtime cardinality tests plus architecture freeze coverage for meter registration, reindex validation, and PASS 8 documentation.

Current evidence:
- `PointOfSale.API/Program.cs`
- `PointOfSale.Infrastructure/Instrumentation/OperationalTelemetryDimensions.cs`
- `PointOfSale.Infrastructure/Instrumentation/AffiliateTelemetry.cs`
- `PointOfSale.Infrastructure/Instrumentation/AbandonedCartRecoveryTelemetry.cs`
- `PointOfSale.Infrastructure/Instrumentation/StorefrontPublishTelemetry.cs`
- `PointOfSale.Infrastructure/Instrumentation/MarketplaceTelemetry.cs`
- `PointOfSale.Infrastructure/Instrumentation/AnalyticsReliabilityTelemetry.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/SearchTelemetry.cs`
- `PointOfSale.Infrastructure/Instrumentation/InventoryReservationTelemetry.cs`
- `PointOfSale.API/Services/Checkout/CheckoutLockTelemetry.cs`
- `PointOfSale.Tests/Infrastructure/OperationalTelemetryCardinalityTests.cs`
- `PointOfSale.Tests/Architecture/OperationalSurvivabilityCertificationTests.cs`
- `docs/architecture/operational-survivability-certification-findings.md`

Intentional remaining debt:
- This pass does not claim repo-wide telemetry normalization, a dashboard product, SIEM, adaptive throttling, fixed rebuild SLOs, or a universal observability framework.
- Older specialized metrics outside the reviewed PASS 8 surfaces still require path-specific review before they are promoted to long-lived production dashboards.

## 2026-05-27 POS-ADMIN-CERTIFICATION-01 PASS 6 Service-Marketplace Recovery Follow-Up

Problem:
- The tenant-admin service-marketplace reindex endpoint had been split into tenant/global scope, but adjacent legacy service search replay and drift reconciliation still used raw Elasticsearch mutation from service event payloads or raw active service ids.
- That allowed stale service replay or global drift repair pressure to restore `marketplace_services` visibility without proving current active tenant lineage and current same-tenant active provider-profile authority.

Production rule:
- Legacy service-marketplace search documents are derivative visibility. Current SQL service, tenant, and provider-profile authority decides restore/delete outcomes.
- Raw service event payloads and raw active service ids are evidence only.

Implemented hardening:
- `ServiceDiscoveryProjectionConsumer` now reloads current SQL service, tenant, and provider-profile authority for create/update/activation replay, and deletes stale search documents when current authority is missing.
- Trust and statistics updates now target only currently live service documents selected through active service, active tenant, and same-tenant active profile joins.
- `ElasticsearchDriftReconciliationJob` now declares global SQL search authority through `SqlTruthRebuildGuard.RequireGlobalSqlAuthority(...)` and builds its live set through the same service/tenant/profile authority.
- `SqlAuthorityOverlayKind.ServiceMarketplace` records the reviewed authority marker for this legacy search boundary.

Current evidence:
- `PointOfSale.Infrastructure/Consumers/ServiceDiscoveryProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Jobs/ElasticsearchDriftReconciliationJob.cs`
- `PointOfSale.Infrastructure/Reliability/SqlTruthRebuildGuard.cs`
- `PointOfSale.Infrastructure/Reliability/SqlAuthorityOverlay.cs`
- `PointOfSale.Tests/Architecture/MarketplaceGovernanceCertificationTests.cs`
- `docs/architecture/operational-tooling-certification-findings.md`

Intentional remaining debt:
- This does not add CommerceHub-grade listing/category/media/moderation governance or a universal service projection framework. Future service ranking, localization, feed/export, support, cache-warmer, profile-governance, and global-script paths require path-specific certification.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 9.1 CMS Public Visibility

Problem:
- Public storefront CMS reads could return Redis/Mongo published-scope data without current SQL publication revalidation.
- SEO sitemap generation could return cached sitemap data before CMS SQL recomposition, creating a deleted-page SEO resurrection path once CMS URLs are present.
- Unified search returned CMS Elasticsearch hits directly, so stale `cmspages` documents could survive public search after SQL unpublish/delete.
- Tenant-admin CMS search sync was intentionally not implemented, leaving no reviewed convergent repair path to remove stale CMS search documents.

Production rule:
- Storefront CMS public visibility is SQL-authoritative. Current active tenant lineage plus same-tenant, non-deleted, published, non-draft SQL `CmsPages` with `PublishedAt != null` must accept every public CMS page, CMS SEO metadata entry, CMS sitemap URL, CMS slug resolution, and CMS search hit.
- Redis, MongoDB, Elasticsearch, sitemap cache entries, SEO state, and replayed CMS events are derivative evidence only and must fail closed when SQL no longer proves visibility.

Implemented hardening:
- `CmsPageReadService` now serves public published page/detail and list reads from current SQL authority instead of cache/projection authority.
- `SeoService` now supports CMS SEO metadata from current SQL, adds SQL-authoritative CMS sitemap URLs, rejects cached CMS slug resolution when SQL no longer proves publication, and no longer returns cached sitemap data before recomposition.
- `StorefrontSearchController` now filters CMS Elasticsearch hits through current SQL tenant/page publication authority before returning unified search results.
- `AdminSearchSyncController.SyncCmsPages(...)` now declares `SqlAuthorityOverlayKind.StorefrontCms`, indexes current SQL-published CMS pages, and deletes tenant-scoped stale CMS search documents through `IUnifiedSearchIndexService.DeleteStaleCmsPagesAsync(...)`.
- Regression protection added `CmsPageWorkflowTests.GetPublishedPageAsync_DoesNotLetCacheResurrectUnpublishedSqlPage` and `CmsPublicVisibilityCertificationTests`.

Current evidence:
- `PointOfSale.API/Services/Cms/CmsPageReadService.cs`
- `PointOfSale.API/Services/Storefront/SeoService.cs`
- `PointOfSale.API/Controllers/StorefrontSearchController.cs`
- `PointOfSale.API/Controllers/AdminSearchSyncController.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/UnifiedSearchIndexService.cs`
- `PointOfSale.Tests/Cms/CmsPageWorkflowTests.cs`
- `PointOfSale.Tests/Architecture/CmsPublicVisibilityCertificationTests.cs`
- `docs/architecture/cms-public-visibility-certification-findings.md`

Intentional remaining debt:
- Marketplace CMS, scheduled CMS publication, CMS approve/reject/archive workflows, CMS cache warmers, CDN purge jobs, localization, feed/export, support tooling, and global scripts require path-specific certification before they are treated as resurrection-resistant public visibility infrastructure.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 9.2 Navigation / Category Lineage / Storefront Structure

Problem:
- Storefront category tree and category-by-slug reads still used Redis/Mongo category projections as return authority.
- Category SEO metadata, category sitemap entries, category breadcrumbs, and category slug resolution still relied on projected category documents.
- Storefront navigation could return cached or Mongo-backed category menu links without proving that the linked category slug still survived current SQL category lineage.

Production rule:
- PostgreSQL `Categories` own storefront category hierarchy, category route eligibility, category SEO visibility, category breadcrumb ancestry, and category navigation eligibility.
- Navigation JSON, Redis, MongoDB, Elasticsearch, breadcrumb DTOs, sitemap caches, and replay/rebuild payloads are derivative evidence only.
- Missing, deleted, orphaned, self-referential, cyclic, or wrong-tenant SQL category lineage must fail closed on reviewed public structure surfaces.

Implemented hardening:
- `StorefrontCategoryService` now recomposes category tree and category-by-slug reads from current SQL category lineage plus active tenant lineage.
- `SeoService` now derives category SEO metadata, sitemap URLs, breadcrumb ancestry, and category slug-cache acceptance from current SQL category lineage.
- `StorefrontNavigationController` now filters category navigation links through current SQL category lineage before returning cached or Mongo-backed navigation DTOs.
- `NavigationCategoryStructureCertificationTests` freezes the reviewed public category/navigation/SEO/breadcrumb boundaries.

Current evidence:
- `PointOfSale.API/Services/Storefront/StorefrontCategoryService.cs`
- `PointOfSale.API/Controllers/StorefrontNavigationController.cs`
- `PointOfSale.API/Services/Storefront/SeoService.cs`
- `PointOfSale.API/Services/Catalog/CategoryLineageAuthority.cs`
- `PointOfSale.Tests/Architecture/NavigationCategoryStructureCertificationTests.cs`
- `docs/architecture/category-lineage-certification-findings.md`

Intentional remaining debt:
- SQL category slugs are still generated from category names because the SQL entity lacks a first-class slug field.
- Category localization and promotion banner fields remain derivative presentation overlays, not visibility authority.
- Future feed/export/cache-warmer/support/localization/global-script category surfaces require path-specific certification.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 9.3 Product Detail / Media / Public Projection

Problem:
- Storefront product detail/list reads, product SEO metadata, product breadcrumbs, product slug resolution, product sitemap URLs, and storefront search/discovery hydration still treated Redis/Mongo/Elasticsearch candidates as enough public return authority on some paths.
- `ProductMediaChangedEventConsumer` already reloaded SQL media authority, and product search mutation already used `ProductSearchSqlAuthorityOverlay`, but read-time product detail/SEO/search hydration could preserve stale product/media visibility until projection convergence.
- The shared SQL overlay also still copied legacy `ProductVariant.ImageUrl` into refreshed variant projection fields even though current `ProductMedia` plus `ProductVariants.MediaId` is the media authority.

Production rule:
- Storefront product public detail, media, variant, search hydration, discovery card, SEO, breadcrumb, slug, and sitemap visibility are SQL-authoritative.
- MongoDB `ProductDocument`, Redis product/SEO caches, Elasticsearch product documents, CDN URLs, thumbnails, replay payloads, and rebuild payloads are evidence only.
- Public product reads may use derivative stores as candidate selectors, but current SQL product/category/media/variant/unit/stock/promotion/publication and tenant authority must accept the product before return or cache population.

Implemented hardening:
- `StorefrontProductService` now applies `ProductSearchSqlAuthorityOverlay` to product-by-slug, product-by-id, and Mongo-backed list candidates, and requires active/online-visible SQL overlay output before returning DTOs.
- `StorefrontProductsController` now applies the same SQL overlay to the direct category-tree Mongo list path.
- `ProductSearchService` now revalidates Elasticsearch/Mongo hydrated search results through current SQL overlay before response mapping.
- `ProductDiscoveryService` now recomposes Elasticsearch discovery hits from current SQL before returning public product cards.
- `SeoService` now uses current SQL-overlaid products for product SEO metadata, breadcrumbs, slug resolution, cached product slug validation, and product sitemap URLs.
- `ProductSearchSqlAuthorityOverlay` now requires active tenant lineage and clears legacy variant image strings so variant image visibility resolves only through current SQL media linkage.

Current evidence:
- `PointOfSale.API/Services/Storefront/StorefrontProductService.cs`
- `PointOfSale.API/Controllers/StorefrontProductsController.cs`
- `PointOfSale.API/Services/Storefront/ProductSearchService.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs`
- `PointOfSale.API/Services/Storefront/SeoService.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Tests/Architecture/ProductDetailMediaPublicProjectionCertificationTests.cs`
- `docs/architecture/product-detail-media-public-projection-certification-findings.md`

Intentional remaining debt:
- Discovery facets and pre-overlay total counts remain Elasticsearch-derived; public returned items fail closed, but exact facet/count recomposition is future search-read work.
- Future product structured data, feeds, exports, cache warmers, CDN purge jobs, support tooling, analytics/reporting, and global scripts require path-specific SQL authority checks and replay/rebuild tests.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 9.4 Homepage / Discovery / Recommendations

Problem:
- Storefront homepage and CommerceHub homepage paths were already SQL-filtered on the current worktree, but affiliate discovery and public affiliate product reads still returned Elasticsearch affiliate documents directly.
- A stale affiliate search document could therefore preserve public discovery visibility after SQL product unpublish/delete, affiliate-policy disablement, tenant deactivation, category invalidation, pricing/stock failure, or storefront URL authority loss until reindex caught up.

Production rule:
- Homepage, discovery, recommendation-like, featured, trending, popular, new-arrival, curated, affiliate discovery, and CommerceHub homepage visibility are SQL-authoritative.
- Search engines, homepage projections, Redis caches, analytics rankings, and curated JSON may choose candidate order only. Current SQL product/listing/category/media/variant/unit/stock/price/publication, tenant, affiliate-policy, and vendor-governance authority decides return eligibility.

Implemented hardening:
- `AffiliateProductDiscoveryService` now injects scoped SQL authority and recomposes ES candidate hits through `ProductSearchSqlAuthorityOverlay`, current `AffiliateProductPolicies`, active tenant authority, storefront URL authority, and storefront affiliate pricing/stock semantics before returning items.
- `PublicAffiliateProductsController` now applies the same read-time SQL recomposition to public affiliate product list and slug reads.
- Affiliate discovery page totals now reflect SQL-visible returned items instead of stale ES total visibility on the inspected page.
- `IAffiliateProductDiscoveryService` lifetime is scoped because the service now consumes `ApplicationDbContext`.
- `HomepageDiscoveryRecommendationCertificationTests` freezes reviewed homepage, CommerceHub homepage, affiliate discovery, public affiliate products, and homepage cache replay-collapse markers.

Current evidence:
- `PointOfSale.API/Services/Storefront/StorefrontHomeService.cs`
- `PointOfSale.API/Services/Homepage/HomepageSectionReadService.cs`
- `PointOfSale.Infrastructure/ReadModels/Marketplace/MarketplaceReadModelProjectionServices.cs`
- `PointOfSale.API/Controllers/MarketplaceReadController.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/AffiliateProductDiscoveryService.cs`
- `PointOfSale.API/Controllers/PublicAffiliateProductsController.cs`
- `PointOfSale.Infrastructure/Elasticsearch/ElasticsearchServiceCollectionExtensions.cs`
- `PointOfSale.Tests/Architecture/HomepageDiscoveryRecommendationCertificationTests.cs`
- `docs/architecture/homepage-discovery-recommendation-certification-findings.md`

Intentional remaining debt:
- This pass does not add a personalized recommendation engine, ranking redesign, full SQL-recomputed all-page search counts/facets, feed/export/cache-warmer/CDN purge certification, or universal analytics/reporting certification.
- Future recommendation engines must treat recommendations as ranking candidates only and call current SQL authority before response and rebuild mutation.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 9.5 Search / SEO / Public Indexes

Problem:
- Storefront unified/header search still had paths where Elasticsearch product/category hits could be returned without current SQL product/category recomposition.
- Reviewed search/product discovery and CommerceHub list/search responses could expose pre-overlay index counts or aggregations after stale documents were filtered from returned items.
- Legacy affiliate portal public discovery/landing/detail endpoints still read Mongo `AffiliateProductIntelligence` directly even after the dedicated public affiliate search controller/service had been SQL-revalidated.

Production rule:
- Search engines, autocomplete indexes, suggestion stores, Mongo intelligence docs, Redis SEO/sitemap caches, and rebuild payloads are candidate/cache evidence only.
- Current SQL product/category/CMS/listing/vendor/tenant/affiliate-policy authority decides public search, autocomplete, count, facet, SEO, sitemap, structured-data, and public-index visibility.

Implemented hardening:
- `StorefrontSearchController` now recomposes unified product hits through `ProductSearchSqlAuthorityOverlay`, category hits through `CategoryLineageAuthority`, CMS hits through current SQL publication authority, and header autocomplete products through current SQL product/media/pricing/stock authority.
- `ProductSearchService`, `ProductDiscoveryService`, and `CommerceHubQueryService` now report reviewed public search counts from SQL-visible returned items rather than raw Elasticsearch/Mongo totals.
- Raw product discovery facets and CommerceHub raw public aggregations are suppressed where SQL-visible aggregation is not available; CommerceHub category/price facets are rebuilt from SQL-visible returned listings.
- `AffiliatePortalController` now revalidates legacy public affiliate landing pages, category counts, product discovery, and detail reads through current SQL affiliate policy, product overlay, tenant, storefront pricing, and stock authority.
- `SearchSeoPublicIndexCertificationTests` freezes reviewed search/autocomplete/facet/count/SEO/sitemap/reindex/public-affiliate boundaries.

Current evidence:
- `PointOfSale.API/Controllers/StorefrontSearchController.cs`
- `PointOfSale.API/Services/Storefront/ProductSearchService.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs`
- `PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs`
- `PointOfSale.API/Controllers/AffiliatePortalController.cs`
- `PointOfSale.API/Services/Storefront/SeoService.cs`
- `PointOfSale.Tests/Architecture/SearchSeoPublicIndexCertificationTests.cs`
- `docs/architecture/search-seo-public-index-certification-findings.md`

Intentional remaining debt:
- Full all-page SQL-recomputed facets/counts, product and organization JSON-LD, product feeds/marketplace feeds, cache warmers, CDN purge jobs, support/global scripts, and analytics/reporting indexes remain future path-specific certification work.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 9.6 Storefront Recovery, Cache & Projection Rebuild

Problem:
- Normal public reads had been hardened through PASS 9.1-PASS 9.5, but recovery/rebuild/repair/cache/startup paths still needed explicit certification because those paths often run outside request-time public visibility controls.
- CommerceHub auto-healing used listing-id-only in-flight dedupe, startup/background recovery grouping, and batch Mongo lookup even though final search mutation was SQL-overlaid. This was a tenant-identity weakness on the recovery coordination layer.

Production rule:
- Recovery may restore derivative state, but it may never restore visibility. Current PostgreSQL product/category/CMS/listing/media/unit/stock/vendor/publication authority wins over MongoDB, Redis, Elasticsearch, replay payloads, repair snapshots, and startup state.
- Tenant identity must participate in derivative recovery identity where tenant evidence is available.

Implemented hardening:
- `CommerceHubAutoHealingService` now uses `{ TenantId, ListingId }` as the recovery identity for in-flight dedupe and active processing.
- Startup/background CommerceHub auto-healing groups Mongo recovery candidates by tenant and enqueues tenant-scoped recovery tasks.
- Batch Mongo document resolution is tenant-scoped for tenant-scoped queued listings, and ambiguous unscoped duplicate listing ids are not promoted as a specific document.
- Added `StorefrontRecoveryRebuildCertificationTests` and `docs/architecture/storefront-recovery-rebuild-certification-findings.md`.

Current evidence:
- `PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs`
- `PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs`
- `PointOfSale.API/Controllers/SearchReindexController.cs`
- `PointOfSale.API/Services/ReadModels/DiscountReadModelRepairService.cs`
- `PointOfSale.Tests/Architecture/StorefrontRecoveryRebuildCertificationTests.cs`
- `docs/architecture/storefront-recovery-rebuild-certification-findings.md`

Intentional remaining debt:
- This pass does not create a universal recovery framework or certify future feed/export/CDN purge/support/global-script/cache-warmer paths. Those remain path-specific certification work before public visibility mutation.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 10.1 Event Propagation & Projection Freshness

Problem:
- PASS 9 certified SQL-authoritative public visibility, but the propagation layer still needed an explicit certification of how quickly and reliably derivative systems converge toward SQL truth.
- Projection and MassTransit outbox readiness checks exposed lag through health data and logs, but did not emit the same low-cardinality convergence telemetry used by reviewed replay/rebuild/fail-closed surfaces.

Production rule:
- The platform guarantees observable eventual consistency, not strong consistency.
- SQL changes must be staged into an outbox or reviewed SQL-backed processor path, consumers must be idempotent and stale-event resistant, and lag must be observable before stale derivative systems can silently persist.
- MongoDB, Redis, Elasticsearch, CommerceHub projections, homepage/discovery projections, and search projections remain derivative and recoverable from SQL.

Implemented hardening:
- `ProjectionOutboxHealthCheck` now records bounded `OperationalConvergenceTelemetry` for critical lag, warning lag, healthy tolerance, and health-check failure.
- `MassTransitBusOutboxReadinessHealthCheck` now records the same convergence telemetry for EF bus outbox delivery lag and health-check failure.
- `OperationalConvergenceReason` now includes `ProjectionLagDetected`, `ProjectionLagWithinTolerance`, and `OutboxReadinessCheckFailed`.
- `EventPropagationProjectionFreshnessCertificationTests` freezes the reviewed outbox, ordering, lag-detection, telemetry, and documentation boundaries.

Current evidence:
- `PointOfSale.Infrastructure/HealthChecks/ProductionHealthChecks.cs`
- `PointOfSale.Infrastructure/Instrumentation/OperationalConvergenceTelemetry.cs`
- `PointOfSale.Tests/Architecture/EventPropagationProjectionFreshnessCertificationTests.cs`
- `docs/architecture/event-propagation-projection-freshness-certification-findings.md`

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 10.3 Mongo Projection Convergence

Problem:
- MongoDB is one of the platform's primary read models, and previous passes certified SQL public visibility, event propagation, and Redis cache freshness, but Mongo projection convergence needed a dedicated certification so lag, repair, rebuild, and drift handling could not be mistaken for projection authority.

Production rule:
- PostgreSQL remains source of truth. MongoDB projections are derivative and may lag, but reviewed public-visibility Mongo state must converge through SQL-derived consumers, rebuilds, repairs, tombstones/deletes, and SQL-filtered reads.
- Mongo repair/rebuild paths must not be additive-only when they touch public visibility. Missing SQL authority must delete, tombstone, suppress, or fail closed rather than resurrect stale projected documents.
- Mongo projection freshness must stay measurable through event timestamps, versions, mutation versions, processed-event receipts, tombstones, sync timestamps, search-sync metadata, readiness checks, or bounded convergence telemetry.

Implemented hardening:
- Added Mongo projection convergence certification documentation covering product, category, CMS, homepage, CommerceHub, affiliate, analytics/support, rebuild, repair, drift, startup/recovery, and chaos findings.
- Added `MongoProjectionConvergenceCertificationTests` to freeze reviewed inventory markers, SQL-derived product/category/CMS/homepage/CommerceHub/affiliate mutation boundaries, stale-delete/tombstone behavior, freshness metadata, and documentation/governance updates.
- Updated `.ai-context` truth, risk, project context, and CODEX freeze rules so future Mongo projection work inherits PASS 10.3 constraints.

Current evidence:
- `PointOfSale.Tests/Architecture/MongoProjectionConvergenceCertificationTests.cs`
- `docs/architecture/mongo-projection-convergence-certification-findings.md`
- `PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs`
- `PointOfSale.Infrastructure/Consumers/CmsPageProjectionConsumer.cs`
- `PointOfSale.Infrastructure/ReadModels/Marketplace/MarketplaceReadModelProjectionServices.cs`
- `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProductIntelligenceProjectionConsumer.cs`

Intentional remaining debt:
- No universal Mongo-vs-SQL row scanner, adaptive lag SLO, strong consistency layer, or centralized projection chaos framework was introduced. Feed/export/CDN purge/support/reporting/global-script projections still need path-specific certification before they shape public visibility.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 10.4 Elasticsearch Freshness & Search Convergence

Problem:
- Elasticsearch is a derivative discovery/search system, and prior passes certified public visibility, event propagation, Redis freshness, and Mongo convergence, but search indexes needed a dedicated certification so index documents, autocomplete fields, suggestions, facets, counts, and alias targets could not become hidden authority.
- `CommerceHubSearchSyncHealthCheck` exposed search sync ratio against Mongo published projection count, which was useful for Mongo-to-ES lag but not sufficient for SQL-to-ES convergence certification.

Production rule:
- PostgreSQL remains source of truth. Elasticsearch may accelerate search, ranking, autocomplete, discovery, faceting, and aggregation, but reviewed public search mutation and response paths must converge from current SQL authority and delete/suppress stale documents when SQL authority is missing.
- Counts, facets, suggestions, autocomplete, popular terms, zero-result assistance, and alias promotion are not authority. They must be SQL-filtered, SQL-count verified, or explicitly suppressed where SQL-visible aggregation is unavailable.
- Search drift and freshness must be observable through timestamps, search sync metadata, health checks, auto-healing metrics, search telemetry, or bounded convergence diagnostics.

Implemented hardening:
- `CommerceHubSearchSyncHealthCheck` now computes expected freshness from current SQL-visible CommerceHub listings, including product publication, active tenant, public variant lineage, and live CommerceHub category lineage.
- The same health check now emits bounded `OperationalConvergenceTelemetry` search-drift diagnostics for drift within tolerance and drift detected.
- Added Elasticsearch freshness/search convergence certification documentation covering inventory, consumers, freshness, product/category/CMS/search, suggestions/autocomplete, facets/counts, reindex, repair, drift, cross-system convergence, startup/recovery, chaos, monitoring, governance, and remaining debt.
- Added `ElasticsearchFreshnessSearchConvergenceCertificationTests` to freeze reviewed SQL-derived search mutation/reindex/repair, stale-delete behavior, SQL-visible health checks, search telemetry, and documentation/governance updates.

Current evidence:
- `PointOfSale.API/HealthChecks/CommerceHubSearchSyncHealthCheck.cs`
- `PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs`
- `PointOfSale.Infrastructure/Jobs/ElasticsearchDriftReconciliationJob.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchIndexService.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/UnifiedSearchIndexService.cs`
- `PointOfSale.Tests/Architecture/ElasticsearchFreshnessSearchConvergenceCertificationTests.cs`
- `docs/architecture/elasticsearch-freshness-search-convergence-certification-findings.md`

Intentional remaining debt:
- This does not introduce strong consistency, synchronous indexing, a universal SQL-vs-Elasticsearch parity scanner, full SQL all-page facet/count recomputation, or certification for every future feed/export/CDN purge/cache-warmer/support/reporting/search-analytics/global-script path. Those remain path-specific convergence work.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 10.5 Cross-System Convergence

Problem:
- PASS 10.1 through PASS 10.4 certified event propagation, Redis, MongoDB, and Elasticsearch independently, but whole-system convergence still needed one explicit inventory and governance boundary tying PostgreSQL, MongoDB, Redis, and Elasticsearch together.
- Without a whole-system certification, future work could harden one derivative subsystem while leaving another derivative subsystem permanently stale or invisible to operators.

Production rule:
- PostgreSQL remains the only internal business authority. MongoDB, Redis, Elasticsearch, outboxes, event payloads, repair snapshots, cache warmers, startup state, and search aliases are convergence evidence only.
- Reviewed public-visibility paths must make cross-system drift bounded, observable, and recoverable across all derivative systems they touch.
- Repair, replay, reindex, cache recovery, and startup recovery may restore derivative state, but they may not restore visibility that current SQL authority rejects.

Implemented hardening:
- Added cross-system convergence certification documentation mapping product, category, CMS, homepage, discovery, search, CommerceHub, and affiliate paths from SQL source to Mongo projection, Redis cache, and Elasticsearch document.
- Added `CrossSystemConvergenceCertificationTests` to freeze reviewed SQL-first convergence markers, drift/repair/startup/freshness/SLO/chaos controls, and `.ai-context` governance updates.
- Updated project context, architecture truth, known risks, and Codex rules with PASS 10.5 whole-system convergence constraints.

Current evidence:
- `PointOfSale.Tests/Architecture/CrossSystemConvergenceCertificationTests.cs`
- `docs/architecture/cross-system-convergence-certification-findings.md`
- `PointOfSale.Infrastructure/HealthChecks/ProductionHealthChecks.cs`
- `PointOfSale.API/HealthChecks/CommerceHubSearchSyncHealthCheck.cs`
- `PointOfSale.Infrastructure/Instrumentation/OperationalConvergenceTelemetry.cs`

Intentional remaining debt:
- This does not introduce strong consistency, a read-your-write guarantee across derivative stores, a universal SQL-vs-Mongo-vs-Redis-vs-Elasticsearch parity scanner, a centralized chaos framework, or certification for future feed/export/CDN purge/support/reporting/search-analytics/global-script paths. Those remain path-specific convergence work.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 10.6 Chaos, Lag & Operational Survivability

Problem:
- PASS 10.1 through PASS 10.5 certified propagation and convergence, but operational survivability still needed an explicit chaos boundary for component outage, lag, restart, replay storm, repair failure, reindex failure, startup recovery, multi-system lag, and tenant-isolated recovery behavior.

Production rule:
- PostgreSQL remains authority during derivative outage and lag. MongoDB, Redis, Elasticsearch, RabbitMQ, MassTransit, outbox rows, cache entries, search aliases, replay payloads, repair snapshots, and startup state are not business truth.
- Recovery may discover derivative candidates, but final public visibility mutation must revalidate current SQL authority, tenant scope, freshness, and idempotency/replay controls.
- Lag and degraded recovery must be observable through readiness checks, health checks, bounded telemetry, freshness metadata, processed-event receipts, search sync metadata, repair/rebuild reports, or an equivalent reviewed operator signal.

Implemented hardening:
- Added PASS 10.6 certification documentation with the required failure inventory, Mongo/Redis/Elasticsearch/consumer/outbox/replay/startup/multi-system lag/tenant isolation/telemetry/SLO/chaos findings, guarantees, and remaining debt.
- Added `ChaosLagOperationalSurvivabilityCertificationTests` to freeze reviewed outage survivability markers across readiness checks, derivative recovery controls, replay/lease/fence behavior, startup auto-healing, tenant-scoped recovery identity, and `.ai-context` governance entries.

Current evidence:
- `PointOfSale.Tests/Architecture/ChaosLagOperationalSurvivabilityCertificationTests.cs`
- `docs/architecture/chaos-lag-operational-survivability-certification-findings.md`
- `PointOfSale.Infrastructure/HealthChecks/ProductionHealthChecks.cs`
- `PointOfSale.API/HealthChecks/CommerceHubSearchSyncHealthCheck.cs`
- `PointOfSale.Infrastructure/Instrumentation/OperationalConvergenceTelemetry.cs`

Intentional remaining debt:
- This does not add zero downtime, strong consistency, read-your-write across derivative stores, a universal chaos harness, a universal cross-store parity scanner, adaptive scheduling, an authoritative dashboard bundle, or path-specific certification for future feed/export/CDN purge/cache-warmer/support/reporting/search-analytics/global-script paths.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 11.2 Global Scope & Tenant Boundary

Problem:
- PASS 11.1 certified super-admin identity and session authority, but global scope governance still needed an explicit certification tying platform routes, tenant routes, marketplace governance, recovery/replay/reindex, diagnostics, service-layer enforcement, tenant context, auditability, and chaos behavior together.

Production rule:
- Global operations must be intentionally global and guarded by current SQL platform authority.
- Tenant operations must remain tenant-bound through trusted tenant context, current SQL tenant actor authority, active tenant lineage, tenant filters, and SQL authority overlays.
- Replay, recovery, repair, reindex, and diagnostics may discover candidates from derivative systems, but final behavior must preserve explicit global or tenant scope.

Implemented hardening:
- Added PASS 11.2 certification documentation covering scope inventory, RequireSuperAdmin, tenant boundary, marketplace governance, replay/recovery, reindex, diagnostics, service layer, tenant context, auditability, chaos, monitoring, governance, and remaining debt.
- Added `GlobalScopeTenantBoundaryCertificationTests` to freeze reviewed platform/tenant middleware separation, global route policy use, tenant global-operation denial markers, tenant-scoped reindex/recovery/diagnostics markers, service-layer SQL authority overlays, event-scope validation, and documentation/governance updates.
- Updated project context, architecture truth, known risks, and Codex rules with PASS 11.2 scope-governance constraints.

Current evidence:
- `PointOfSale.Tests/Architecture/GlobalScopeTenantBoundaryCertificationTests.cs`
- `docs/architecture/global-scope-tenant-boundary-certification-findings.md`
- `PointOfSale.API/SuperAdmin/Infrastructure/SuperAdminPlatformAuthorityMiddleware.cs`
- `PointOfSale.API/Middleware/AdminTenantAuthorityMiddleware.cs`
- `PointOfSale.API/Security/AdminTenantAuthority.cs`
- `PointOfSale.API/Controllers/MarketplaceController.cs`
- `PointOfSale.API/Controllers/SearchReindexController.cs`

Intentional remaining debt:
- No generated scope registry, immutable SIEM, universal diagnostic parity scanner, or centralized global-operation dashboard was introduced. Future feed/export/CDN purge/support/reporting/search-analytics/global-script paths require path-specific scope certification before they are treated as safe global or tenant tooling.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 11.3 Marketplace Governance Authority

Problem:
- PASS 11.1 and PASS 11.2 certified platform actor authority and scope boundaries, but marketplace governance still needed a dedicated certification proving listing approval, vendor suspension/reactivation, homepage curation, recommendation/discovery, featured/promoted placement, replay/recovery, and audit paths cannot become visibility authority.
- Vendor-governance reactivation restored `RestorePublicationOnActivate` listings after checking current variant and basic product activity, but did not require full current product publication, active tenant, or live CommerceHub category lineage before flipping listing publication back on.

Production rule:
- Marketplace governance is decision authority only. Current SQL listing/product/vendor/category/tenant authority decides public visibility.
- Listing publish/update and governance restore must not create or restore publication when current SQL product publication, variant lineage, category lineage, tenant lineage, or vendor governance rejects the listing.
- Homepage, recommendation, discovery, featured, promoted, replay, and recovery paths must treat curated/ranking state as candidates and revalidate current SQL authority before public return or derivative mutation.

Implemented hardening:
- Tenant-admin CommerceHub publish/update now requires live CommerceHub category lineage and current public product/variant publication authority before approval/update.
- `CommerceHubVendorVisibilityService` now restores governance-hidden listings only when current tenant, product publication/online visibility, public variant lineage, and CommerceHub category lineage still prove eligibility; otherwise it clears stale restore state.
- Added `MarketplaceGovernanceAuthorityCertificationTests` and PASS 11.3 certification documentation.

Current evidence:
- `PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubVendorVisibilityService.cs`
- `PointOfSale.Tests/Architecture/MarketplaceGovernanceAuthorityCertificationTests.cs`
- `PointOfSale.Tests/SuperAdmin/UserGovernanceServiceTests.cs`
- `docs/architecture/marketplace-governance-authority-certification-findings.md`

Intentional remaining debt:
- No enterprise moderation workflow, SIEM-grade immutable audit store, generated marketplace governance registry, universal SQL-vs-derivative parity scanner, personalized recommendation framework, or adaptive rebuild SLO was introduced. Future feed/export, ranking, localization, review-export, support, analytics, cache-warmer, and global-script paths require path-specific certification before shaping marketplace visibility.

## 2026-05-30 POS-ADMIN-CERTIFICATION-01 PASS 11.4 Global Search, Reindex & Recovery

Problem:
- PASS 10.4, PASS 10.6, PASS 11.2, and PASS 11.3 established SQL-first search convergence, survivability, tenant scope, and marketplace governance, but global operational authority still needed one explicit certification across reindex, repair, recovery, replay, auto-healing, startup, auditability, and chaos.
- CommerceHub same-index and tenant reindex reconciled Mongo candidates and SQL-missing seeds through SQL overlays, but lacked a CommerceHub stale Elasticsearch orphan sweep equivalent to category/CMS stale-delete repair.

Production rule:
- Operational tooling may rebuild, replay, repair, recover, reindex, and heal derivative systems only from current SQL authority.
- Operational tooling may not create visibility, publication, governance authority, or tenant scope. Missing current SQL authority must converge by delete, tombstone, suppression, no-op, manual review, retry, or fail-closed behavior.
- CommerceHub search reindex and repair must delete Elasticsearch documents absent from current SQL-visible CommerceHub listing authority.

Implemented hardening:
- Added `ICommerceHubSearchIndexService.DeleteStaleAsync(...)` to scan CommerceHub search documents and delete documents missing from a SQL-visible `{ TenantId, ListingId }` identity set.
- Added SQL-visible CommerceHub listing identity recomposition to `CommerceHubReindexService`.
- Wired stale CommerceHub search deletion into global, target-index, and tenant reindex flows before completion or alias count verification.
- Added `GlobalSearchReindexRecoveryCertificationTests` and `docs/architecture/global-search-reindex-recovery-certification-findings.md`.

Current evidence:
- `PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchIndexService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs`
- `PointOfSale.Tests/Architecture/GlobalSearchReindexRecoveryCertificationTests.cs`
- `docs/architecture/global-search-reindex-recovery-certification-findings.md`

Intentional remaining debt:
- No universal parity scanner, immutable SIEM, central global-operation dashboard, universal chaos harness, adaptive scheduler, or path-specific certification for future feed/export/CDN purge/cache-warmer/support/reporting/search-analytics/global-script surfaces was introduced.

## 2026-05-30 POS-ADMIN-CERTIFICATION-01 PASS 11.5 Emergency Operations & Break-Glass

Problem:
- PASS 11.1 through PASS 11.4 certified platform identity/session, global-vs-tenant scope, marketplace governance, and global search/recovery, but emergency operations needed one explicit authority boundary tying disable/suspend/freeze/recovery/repair/reindex/replay/shutdown/diagnostics, break-glass pressure, revocation, auditability, runbooks, and chaos together.
- The older SUPERADMIN-CON emergency certification predated the current PASS 11 chain and needed to be restated against current SQL platform authority, marketplace governance, and SQL-derived recovery controls.

Production rule:
- Emergency authority is operational authority only. It may stop, suspend, freeze, isolate, recover, repair, reindex, replay, shut down, or diagnose, but it may not create visibility, publication, tenant, governance, financial, inventory, or SQL authority.
- Break-glass work must be explicit, scoped, auditable, revocable, SQL-authoritative, and runbook-backed.
- Emergency recovery and marketplace emergency controls must converge from current SQL authority and delete, tombstone, suppress, no-op, retry, block, or route to reconciliation when SQL authority is absent.

Implemented hardening:
- Added PASS 11.5 certification documentation covering emergency inventory, break-glass authority, marketplace emergency actions, recovery, search, tenant isolation, revocation, auditability, runbooks, chaos, monitoring, governance, and remaining debt.
- Extended `EmergencyGovernanceCertificationTests` to freeze current SQL platform authority, request-level operational audit, disabled dangerous reset behavior, development-only diagnostics, marketplace restore safeguards, SQL-derived reindex/stale-delete behavior, tenant-scoped auto-healing identity, replay leases, dangerous primitive telemetry, docs, runbooks, and `.ai-context` updates.
- Updated project context, architecture truth, known risks, Codex rules, dangerous primitive guidance, and operational runbooks with PASS 11.5 emergency-governance constraints.

Current evidence:
- `PointOfSale.Tests/Architecture/EmergencyGovernanceCertificationTests.cs`
- `docs/architecture/emergency-governance-certification-findings.md`
- `PointOfSale.API/SuperAdmin/Infrastructure/SuperAdminPlatformAuthority.cs`
- `PointOfSale.API/SuperAdmin/Infrastructure/SuperAdminOperationalAuditMiddleware.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubVendorVisibilityService.cs`
- `PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs`

Intentional remaining debt:
- No enterprise IAM/SSO/MFA/ABAC, delegated support break-glass ceremony, immutable SIEM, central emergency dashboard, universal parity scanner, universal chaos harness, adaptive emergency scheduler, or production support impersonation endpoint was introduced. Future support, feed/export, CDN purge, cache-warmer, reporting, analytics, search-analytics, and global-script emergency surfaces require path-specific certification before rollout.

## 2026-05-30 POS-ADMIN-CERTIFICATION-01 PASS 12.1 Merchant Publication Intent Authority

Problem:
- PASS 11.3 certified marketplace governance as decision authority, but merchant publication intent needed a dedicated boundary proving tenant publish/update/unpublish requests cannot become visibility authority or survive governance/product/category/tenant authority removal.
- Tenant unpublish previously used the same vendor listing governance gate as publish/update, which could block a suspended vendor from withdrawing publication intent or clearing stale restore intent.

Production rule:
- Publication intent is tenant-owned request authority only. Current SQL listing/product/variant/category/tenant/vendor governance and marketplace governance decide public visibility.
- Publish, republish, and update require current governance and SQL product/category/variant authority. Unpublish may only reduce visibility and must stay available as tenant-owned withdrawal of intent.
- Replay, recovery, reindex, search, homepage/discovery, and auto-healing may discover candidates from derivative stores, but final mutation must revalidate current SQL authority and delete/suppress/no-op/fail closed when authority is absent.

Implemented hardening:
- Removed `EnsureVendorListingAllowedAsync` from `UnpublishFromCommerceHubHandler` while preserving trusted tenant isolation and SQL listing mutation.
- Added PASS 12.1 certification documentation covering publication inventory, lifecycle, merchant intent, governance, product authority, ownership, replay/recovery, search/discovery, auditability, chaos, monitoring, governance updates, and remaining debt.
- Added `PublicationIntentAuthorityCertificationTests` to freeze reviewed publication-intent boundaries and `.ai-context` governance updates.

Current evidence:
- `PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs`
- `PointOfSale.Core/Entities/CommerceHubListing.cs`
- `PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs`
- `PointOfSale.Tests/Architecture/PublicationIntentAuthorityCertificationTests.cs`
- `docs/architecture/publication-intent-authority-certification-findings.md`

Intentional remaining debt:
- The listing lifecycle remains boolean-based rather than a full Draft/Pending/Submitted/Approved/Rejected/Suspended/Hidden/Deleted state machine. No immutable publication ledger, generated publication registry, universal parity scanner, universal chaos harness, or certification for future bulk/feed/export/support/ranking/global-script publication paths was introduced.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 12.2 Marketplace Category Mapping & Taxonomy Authority

Problem:
- PASS 12.1 certified publication intent, but CommerceHub taxonomy needed a dedicated boundary proving category selection, category mapping, category filters, attribute schemas, search documents, homepage categories, and recovery cannot become visibility authority.
- `CommerceHubCategoriesController.GetCategoryTree` returned active category rows without proving full live lineage, and `GetCategoryAttributes` / `CommerceHubAttributeService` could expose or validate attributes for a category whose parent lineage was no longer live.

Production rule:
- CommerceHub category mapping is classification authority only. Current SQL listing/product/variant/category/tenant/vendor authority still decides marketplace visibility.
- SQL `CommerceHubCategories` owns taxonomy lineage, SQL `CommerceHubCategoryAttributes` owns category-bound attribute schema, and SQL `CommerceHubListings.CommerceHubCategoryId` owns listing classification.
- Category pickers, attribute forms, publish/update, search overlays, homepage/discovery, suggestions/autocomplete, reindex, health, and auto-healing must recompose or filter through current SQL category lineage and fail closed for invalid lineage.

Implemented hardening:
- `CommerceHubCategoriesController.GetCategoryTree` now filters active rows through `CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync`.
- `CommerceHubCategoriesController.GetCategoryAttributes` now rejects non-live category lineage with `404`.
- `CommerceHubAttributeService.ValidateAndNormalizeAsync` now fails closed when category lineage is not live.
- Added PASS 12.2 certification documentation and `MarketplaceCategoryTaxonomyCertificationTests`.

Current evidence:
- `PointOfSale.API/Controllers/CommerceHubCategoriesController.cs`
- `PointOfSale.API/CommerceHub/Services/CommerceHubAttributeService.cs`
- `PointOfSale.Infrastructure/Reliability/CommerceHubCategoryLineageAuthority.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Tests/Architecture/MarketplaceCategoryTaxonomyCertificationTests.cs`
- `docs/architecture/marketplace-category-taxonomy-certification-findings.md`

Intentional remaining debt:
- No dedicated taxonomy admin workflow, immutable taxonomy ledger, generated taxonomy registry, universal SQL-vs-derivative parity scanner, or deep PASS 12.3 attribute-poisoning certification was introduced. Future taxonomy import, feed/export, support, ranking, analytics, cache-warmer, localization, and global-script paths remain path-specific certification debt.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 12.3 Marketplace Attribute & Filter Governance

Problem:
- PASS 12.2 certified category taxonomy lineage, but attributes and filters needed a dedicated certification proving merchant-provided attributes, stored listing attributes, facet generation, search filters, indexing, replay, and recovery cannot become unbounded metadata authority.
- Unknown submitted attributes were ignored rather than rejected, public filters could target arbitrary normalized attribute keys, and stored listing attributes could carry deleted schema keys into derivative projection/index paths.

Production rule:
- SQL `CommerceHubCategoryAttributes` owns the current category-bound attribute schema. Attributes are classification/search metadata only, not visibility authority.
- Merchant input must validate against live SQL category lineage and current SQL attribute definitions; unsupported keys and values fail closed.
- Filters, facets, Mongo attributes, Elasticsearch attributes, replay, recovery, and reindex metadata must derive from current SQL category attributes and strip deleted/unsupported keys.

Implemented hardening:
- `CommerceHubAttributeService` now rejects unknown merchant attribute keys as validation errors.
- `CommerceHubQueryService` now derives facet definitions from live SQL category lineage and returns no results for unsupported public attribute filters.
- `CommerceHubSearchSqlAuthorityOverlay` now normalizes stored listing attributes against current SQL category attributes before assigning derivative Mongo/Elasticsearch attribute fields.
- Added PASS 12.3 certification documentation and `MarketplaceAttributeFilterGovernanceCertificationTests`.

Current evidence:
- `PointOfSale.API/CommerceHub/Services/CommerceHubAttributeService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Tests/Architecture/MarketplaceAttributeFilterGovernanceCertificationTests.cs`
- `docs/architecture/marketplace-attribute-filter-governance-certification-findings.md`

Intentional remaining debt:
- No immutable attribute schema ledger, generated registry, admin schema migration workflow, historical SQL JSON backfill, universal parity scanner, or full chaos harness was introduced. Future feed/export/support/analytics/ranking/cache-warmer/global-script paths require path-specific PASS 12.3 certification.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 12.4 Marketplace Pricing Authority

Problem:
- PASS 12.1 through PASS 12.3 certified publication intent, taxonomy, and attributes, but CommerceHub pricing needed a dedicated certification proving final price, overrides, variant price changes, promotion interaction, search pricing, replay, and recovery cannot create duplicate or hidden price authority.
- CommerceHub price overrides were already disabled and SQL-constrained, but CommerceHub promotion refresh acquired a projection fence without completing it after successful SQL-derived refresh, and SQL-rejected variant-price events returned before completing their fence.

Production rule:
- Current SQL product variant pricing owns CommerceHub item price. Final item price is positive `ProductVariant.OnlinePrice` when present, otherwise `ProductVariant.Price`.
- Merchant listing price overrides, MongoDB, Elasticsearch, Redis, client snapshots, affiliate projections, and storefront promotion overlays are not final CommerceHub item price authority.
- Replay, recovery, projection, search, and reindex paths must recompose current SQL listing/product/variant/category/tenant/stock/media/pricing authority and collapse duplicate price/promotion delivery after successful apply, no-op, or fail-closed decisions.

Implemented hardening:
- `CommerceHubProjectionConsumer` now completes the variant-price projection fence when current SQL authority rejects a price event.
- `CommerceHubProjectionConsumer` now completes the promotion projection fence after SQL-derived base-price refresh.
- Added PASS 12.4 certification documentation and `MarketplacePricingAuthorityCertificationTests`.

Current evidence:
- `PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs`
- `PointOfSale.Core/Entities/CommerceHubListing.cs`
- `PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Tests/Architecture/MarketplacePricingAuthorityCertificationTests.cs`
- `docs/architecture/marketplace-pricing-authority-certification-findings.md`

Intentional remaining debt:
- Legacy `CommerceHubPrice` and `CommerceHubPriceOverride` compatibility fields remain present but disabled/cleared. No immutable pricing ledger, universal price parity scanner, dedicated marketplace pricing dashboard, full chaos harness, or path-specific certification for future feed/export/support/ranking/global-script pricing surfaces was introduced.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 12.5 Variant & Inventory Marketplace Certification

Problem:
- PASS 12.1 through PASS 12.4 certified publication, taxonomy, attributes, and pricing, but CommerceHub variant and inventory authority needed a dedicated certification proving stock, availability, cart, checkout, search, replay, recovery, and merchant variant management cannot become duplicate authority.
- Cart and checkout variant reads carried most public product/variant predicates inline but did not explicitly use the shared public variant lifecycle helper on those purchase boundaries.

Production rule:
- Variant authority is current SQL `ProductVariants` plus SQL semantic lifecycle, SKU/barcode ownership, variant attributes, and parent product lineage.
- Inventory authority is current SQL `ProductVariantStocks`, active `Warehouses`, `InventoryReservations`, and `StockMovements` filtered through current inventory lineage.
- CommerceHub cart, checkout, search, projection, reindex, replay, recovery, and discovery paths must recompose current SQL variant and inventory lineage before preserving availability or accepting purchase.

Implemented hardening:
- CommerceHub cart pricing and sanitation now call `WherePubliclyVisibleLineage` before accepting variants.
- CommerceHub checkout variant authority loading now calls `WherePubliclyVisibleLineage` before price, stock, and order-line validation.
- Added PASS 12.5 certification documentation and `MarketplaceVariantInventoryCertificationTests`.

Current evidence:
- `PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs`
- `PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs`
- `PointOfSale.Infrastructure/Reliability/ProductVariantInventoryLineageAuthority.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Tests/Architecture/MarketplaceVariantInventoryCertificationTests.cs`
- `docs/architecture/marketplace-variant-inventory-certification-findings.md`

Intentional remaining debt:
- No universal SQL-vs-derivative stock parity scanner, immutable inventory ledger, generated variant registry, full chaos harness, or path-specific certification for future feed/export/support/reporting/global-script inventory surfaces was introduced.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 12.6 Merchant vs Marketplace Governance Precedence Certification

Problem:
- PASS 12.1 through PASS 12.5 certified publication intent, taxonomy, attributes, pricing, variants, and inventory, but governance precedence needed one explicit pass proving merchant request authority cannot override marketplace decision authority.
- CommerceHub public query paths filtered governance-hidden vendors before return, and vendor suspension normally hid listings, but shared search overlay and SQL-visible reindex identity scans did not independently suppress current suspended/banned vendor governance if a stale listing still had `IsPublished = true`.

Production rule:
- Merchant authority is request authority. Marketplace governance is decision authority.
- Current SQL governance wins over merchant update, stale listing publication, MongoDB, Redis, Elasticsearch, replay, reindex, recovery, auto-healing, homepage, and discovery evidence.
- Restore intent is conditional evidence only and may restore visibility only while current SQL tenant/product/variant/category/listing/vendor authority accepts it.

Implemented hardening:
- `CommerceHubSearchSqlAuthorityOverlay` now fails closed for current SQL vendor governance profiles with `Suspended` or `Banned` status.
- `CommerceHubReindexService` now excludes suspended or banned vendor governance from SQL-visible listing identities used for stale-search deletion and count verification.
- Added PASS 12.6 certification documentation and `MerchantMarketplaceGovernancePrecedenceCertificationTests`.

Current evidence:
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs`
- `PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubVendorVisibilityService.cs`
- `PointOfSale.Tests/Architecture/MerchantMarketplaceGovernancePrecedenceCertificationTests.cs`
- `docs/architecture/merchant-marketplace-governance-precedence-certification-findings.md`

Intentional remaining debt:
- No full listing moderation state machine, immutable governance ledger, universal SQL-vs-derivative parity scanner, universal chaos harness, SIEM-grade audit sink, generated governance registry, or future feed/export/support/ranking/global-script marketplace visibility certification was introduced.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 13.3 CommerceHubPriceAuthority Resolver

Problem:
- PASS 13.2 froze the future CommerceHub price decision as `CommerceHubListing.CommerceHubPrice ?? ProductVariant.EffectiveOnlinePrice`, but overrides remain disabled. Existing paths still carried distributed direct variant price calculations.

Production rule:
- `CommerceHubPriceAuthority` is now the only CommerceHub item price resolver boundary.
- Until a future certified pass enables overrides, runtime final price remains exactly `ProductVariant.EffectiveOnlinePrice`.
- The resolver cannot create visibility or bypass listing/product/variant/category/tenant/vendor/inventory governance.

Implemented hardening:
- Added `ICommerceHubPriceAuthority`, `CommerceHubPriceAuthority`, and `CommerceHubPriceResolution`.
- Routed publish/update/unpublish, cart, checkout, Mongo projection, variant-price replay, promotion-triggered base-price refresh, and search SQL overlay pricing through the resolver.
- Preserved disabled overrides through `OverridesEnabled = false`, existing listing nulling/constraint behavior, and derivative `CommerceHubPriceOverride = null`.
- Added `CommerceHubPriceAuthorityCertificationTests` and `docs/architecture/commercehub-price-authority-resolver-certification-findings.md`.

Current evidence:
- `PointOfSale.Core/Pricing/CommerceHubPriceAuthority.cs`
- `PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs`
- `PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Tests/Architecture/CommerceHubPriceAuthorityCertificationTests.cs`
- `docs/architecture/commercehub-price-authority-resolver-certification-findings.md`

Intentional remaining debt:
- Override activation, UI, persistence semantics, migration/backfill, merchant auditability, and pricing parity dashboards remain future PASS 13.4 work.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 13.4 CommerceHub Price Override Persistence

Problem:
- PASS 13.3 centralized the resolver while intentionally leaving override activation disabled. Future override activation still needed durable SQL attribution fields, additive migration strategy, derivative metadata, and regression coverage proving preparation did not change customer-visible prices.

Production rule:
- Override persistence and audit metadata may exist, but override pricing remains inactive until a future certified activation pass. Current SQL `ProductVariant.EffectiveOnlinePrice` through `CommerceHubPriceAuthority` remains final CommerceHub item price.
- `CommerceHubListings.CommerceHubPrice` must remain rejected, cleared, and SQL-constrained to null. Audit metadata is attribution evidence only and must not create visibility, publication, cart, checkout, or search price authority.

Implemented hardening:
- Added nullable SQL audit metadata to `CommerceHubListing`: `CommerceHubPriceOverrideUpdatedAtUtc`, `CommerceHubPriceOverrideUpdatedBy`, and `CommerceHubPriceOverrideReason`.
- Added rollout-safe migration `20260531134000_CommerceHubPriceOverridePersistenceAudit`, including re-clearing legacy non-null price values, revalidating the null-only price constraint, and adding a partial tenant/time audit index.
- Added derivative CommerceHub Mongo and Elasticsearch metadata `PriceSource` and `OverridePresent`, with current projection/search paths emitting `ProductVariantEffectiveOnlinePrice` and `false`.
- Added `CommerceHubPriceOverridePersistenceCertificationTests` and `docs/architecture/commercehub-price-override-persistence-audit-certification-findings.md`.

Current evidence:
- `PointOfSale.Core/Entities/CommerceHubListing.cs`
- `PointOfSale.Infrastructure/Data/Configurations/CommerceHubListingConfiguration.cs`
- `PointOfSale.Infrastructure/Migrations/20260531134000_CommerceHubPriceOverridePersistenceAudit.cs`
- `PointOfSale.Infrastructure/MongoDB/Documents/CommerceHubProductDocument.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Documents/CommerceHubProductSearchDocument.cs`
- `PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Tests/Architecture/CommerceHubPriceOverridePersistenceCertificationTests.cs`
- `docs/architecture/commercehub-price-override-persistence-audit-certification-findings.md`

Intentional remaining debt:
- Controlled activation still requires merchant manage permission, marketplace audit/emergency-disable permissions, merchant mutation workflow, admin UI, immutable audit/event trail, activation feature flag, migration preflight/backfill, cart/checkout/projection/search parity certification, operational dashboards, alerting, rollback, and updated `.ai-context` freeze rules before `CommerceHubPriceAuthority.OverridesEnabled` can change.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 13.6 CommerceHub Price Override Controlled Activation

- Reviewed the controlled activation gate and stopped activation because required controls remain incomplete.
- Preserved current runtime behavior: SQL variant online price remains authoritative; override writes remain rejected, cleared, and SQL-constrained to null; derivative override state remains cleared.
- Added `CommerceHubPriceOverrideActivationCertificationTests` to freeze the stop decision and prevent accidental partial activation across resolver, persistence, cart, checkout, projection, and search paths.
- Adjacent verification initially found seven existing `CommerceHubCheckoutFinancialControlTests` scenarios failing closed as `VARIANT_NOT_FOUND` before their intended assertions. PASS 13.5 repaired that fixture drift by seeding published online products; the green baseline must remain preserved before activation.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 13.5 CommerceHub Price Override Governance

### Authority decision

- Override activation remains prohibited. PASS 13.5 freezes future operational controls only.
- Tenant merchants own override authority. Marketplace governance owns visibility, suppression, suspension, emergency disable, audit, rollback, and marketplace integrity only. It may not approve, reject, create, or edit merchant prices.
- Marketplace approval is prohibited. Merchant mutation is versioned through optional `Draft`, then `Saved`, `Updated`, or `Removed`.

### Future activation controls

- Required permissions are `CommerceHubPriceOverrideManage`, `CommerceHubPriceOverrideAudit`, and `CommerceHubPriceOverrideEmergencyDisable`.
- Required runtime controls are a default-off global emergency kill switch, default-empty tenant allowlist, optional cohorts, immutable events, parity monitoring, and non-destructive fallback to `ProductVariant.EffectiveOnlinePrice`.
- Required rollout is staged from resolver-only monitoring through internal tenant, limited tenants, broader cohorts, and general availability. No big-bang deployment is allowed.

### Checkout blocker investigation

- The seven PASS 13.6 `CommerceHubCheckoutFinancialControlTests` failures were fixture drift after checkout adopted `WherePubliclyVisibleLineage`: seeded products were active but defaulted to draft and offline.
- Test fixtures now seed published online products. The baseline must remain green, and future active-override checkout parity coverage is still an activation prerequisite.

### Regression freeze

- Added `CommerceHubPriceOverrideGovernanceCertificationTests` and `docs/architecture/commercehub-price-override-governance-rollout-certification-findings.md`.
- Recorded blockers and explicit rollback, governance, replay, checkout, and search answers in `docs/architecture/commercehub-price-override-controlled-activation-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 13.5B CommerceHub Price Override Business Model Correction

Problem:
- PASS 13.5 incorrectly assigned merchant CommerceHub price override decisions to marketplace governance and introduced approval-centric permissions and workflow assumptions.

Production rule:
- Tenant merchants own CommerceHub price override decisions.
- Marketplace governance owns visibility, suppression, vendor suspension, tenant suspension, emergency disable, auditability, and marketplace integrity only.
- Marketplace governance may not approve, reject, create, or edit merchant prices. Audit must not become approval.

Implemented hardening:
- Corrected PASS 13.4, PASS 13.5, PASS 13.6, and `.ai-context` ownership language.
- Preserved the default-off rollout design, tenant rollout design, kill switch, emergency disable, non-destructive rollback, immutable audit expectations, and SQL-first replay/recovery behavior.
- Added `CommerceHubPriceOverrideBusinessModelCertificationTests` and `docs/architecture/commercehub-price-override-business-model-correction-certification-findings.md`.

Current evidence:
- `PointOfSale.Tests/Architecture/CommerceHubPriceOverrideBusinessModelCertificationTests.cs`
- `docs/architecture/commercehub-price-override-business-model-correction-certification-findings.md`

Intentional remaining debt:
- Overrides remain inactive. Merchant mutation API/UI, persistence activation, immutable events, rollout flags, parity monitoring, support tooling, drills, and active-state certification still remain future work.
## PASS 13.7 CommerceHub Price Override Implementation

- Persist positive merchant-owned CommerceHub overrides with actor, timestamp, reason, and mutation version.
- Resolve active marketplace price only through `CommerceHubPriceAuthority`.
- Keep runtime activation default-off with a reloadable global kill switch, tenant allowlist, and optional cohorts.
- Preserve stored overrides during rollback and recompose MongoDB and Elasticsearch from current SQL authority.
- Keep marketplace governance visibility-only.
## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 14.1 CommerceHub Product Publication Lifecycle Certification

- Certified the complete CommerceHub listing journey across POS Admin UI, handlers, SQL persistence, category and attribute governance, PASS 13.7 price overrides, variant and stock lineage, MongoDB projection, Elasticsearch search, public query/detail/discovery, cart, checkout, governance suppression, restore, replay, reindex, auto-healing, health checks, and telemetry.
- Fixed a withdrawal lifecycle gap: `UnpublishFromCommerceHubHandler` previously required an active, undeleted variant before clearing tenant listing intent. It now resolves the tenant listing first and permits visibility-reducing withdrawal after variant deletion or semantic replacement.
- Added `CommerceHubPublicationLifecycleCertificationTests` and `docs/architecture/commercehub-publication-lifecycle-certification-findings.md`.
- Remaining debt is explicit: boolean listing state, no tenant bulk workflow, no immutable general publication ledger, no universal parity scanner, no generated lifecycle registry, no full chaos harness, and no deployment-specific dashboard/alert pack.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 14.2 Merchant CommerceHub Operations Certification

Problem:
- PASS 14.1 certified listing lifecycle authority, but daily merchant operations still needed an explicit frontend, API, dashboard, filter, notification, analytics, bulk-operation, performance, monitoring, and chaos review.
- The settings page discarded actionable backend listing validation messages and always showed a generic publication failure.

Implemented hardening:
- `CommerceHubSettings.jsx` now prefers backend `response.data.message`, then transport `error.message`, then translated fallback text for load, attribute-load, publish/update, and unpublish failures.
- Added `MerchantCommerceHubOperationsCertificationTests` and `docs/architecture/merchant-commercehub-operations-certification-findings.md`.

Intentional remaining debt:
- Merchant listing search, operational filters, suppression and orphan cleanup visibility, authoritative listing KPI APIs, bulk workflows, listing analytics, dedicated notifications, and parity/drift monitoring remain missing. Merchant operations are not production-certified.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 14.3 Merchant Listing Operations Console Certification

Problem:
- PASS 14.2 preserved actionable errors but left the merchant listing surface as an active-catalog editor without server-side listing search, operational filters, suppression/orphan visibility, SQL KPIs, bulk workflows, analytics, or notifications.

Production rule:
- Merchant listing operations must derive from current SQL authority. Frontend summaries, MongoDB, Elasticsearch, Redis, and replay payloads are not listing operations authority.
- Merchant bulk operations must remain tenant-scoped and return per-row outcomes. Visibility-increasing actions must fail closed for suppressed or orphaned rows; unpublish may reduce visibility for stale listing cleanup.
- Suppression visibility is diagnostic only. Marketplace governance remains visibility authority.

Implemented hardening:
- Added SQL-backed listing search/filter rows, KPI, analytics, notification, and bulk endpoints to `AdminCommerceHubController`.
- Added operations DTOs and a `CommerceHubSettings.jsx` operations console for search, filters, KPIs, suppression/orphan visibility, notifications, analytics, and bulk unpublish/override-removal.
- Updated `MerchantCommerceHubOperationsCertificationTests` and `docs/architecture/merchant-commercehub-operations-certification-findings.md`.

Intentional remaining debt:
- SQL-vs-derivative parity scanner, dedicated alert dashboards, bulk attribute editing, immutable notification inbox, and deeper analytics segmentation remain operational maturity work.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.1 Marketplace Governance Console Certification

Problem:
- Super Admin governance had vendor/user governance and scattered marketplace operations, but no dedicated SQL-authoritative marketplace governance console for global listing search, suppression visibility, tenant governance, dashboard counts, and frontend/backend aligned controls.

Production rule:
- Super Admin marketplace governance is visibility authority only. It may suppress or restore listing visibility and suspend or restore tenant marketplace authority, but it may not mutate merchant-owned publication intent, category selection, attributes, or CommerceHub price overrides.
- Listing restore must revalidate current SQL tenant, vendor governance, product, variant, and CommerceHub category authority before visibility can return.

Implemented hardening:
- Added `SuperAdminMarketplaceGovernanceController` with SQL-backed dashboard, global listing search/filters, listing suppress/restore, and tenant suspend/restore.
- Added CommerceHub auto-healing verification enqueue for listing and tenant governance actions.
- Added `MarketplaceGovernancePage.tsx` and service/routes/navigation for a dedicated Super Admin governance console.
- Added `MarketplaceGovernanceConsoleCertificationTests` and `docs/architecture/marketplace-governance-console-certification-findings.md`.

Intentional remaining debt:
- SQL-vs-derivative governance parity dashboard, governance telemetry alert pack, immutable operator notification inbox, vendor-governance shortcuts from listing detail, and production query-plan/index review remain operational maturity work.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.2 Marketplace Operations Console Certification

Problem:
- PASS 15.1 certified governance visibility controls, but Super Admin still lacked a dedicated operations console for CommerceHub reindex, search sync diagnostics, projection recovery, repair enqueue, and auto-healing observability.

Production rule:
- Marketplace operations are derivative convergence controls only. Reindex, recovery, repair, replay, and auto-healing may discover candidates from MongoDB, Elasticsearch, dashboards, search sync status, and queue state, but must recompose current SQL authority before derivative mutation and fail closed when SQL visibility is absent.

Implemented hardening:
- Added `SuperAdminMarketplaceOperationsController` with SQL/Mongo/Elasticsearch/auto-healing diagnostics, global reindex, tenant reindex, catch-up reindex, listing verification, and tenant verification.
- Added reason validation and Super Admin audit records for operations actions.
- Added `MarketplaceOperationsPage.tsx`, service bindings, route registration, and navigation entry.
- Added `MarketplaceOperationsConsoleCertificationTests` and `docs/architecture/marketplace-operations-console-certification-findings.md`.

Intentional remaining debt:
- Dedicated parity drill reports, alert packs, immutable operator notifications, long-running operation history, production query-plan review, and broader non-search CommerceHub recovery dashboards remain operational maturity work.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.4 CommerceHub Emergency Operations

Problem:
- PASS 15.1 through PASS 15.3 certified governance, operations, and finance, but incident-day certification needed explicit proof that operators can stop visibility, stop outward money movement, stop payment intake, diagnose incidents, rollback safely, and avoid unsafe break-glass shortcuts.
- Outward money movement was already readiness-gated by SQL/ledger/reconciliation health, but there was no explicit default-off operator stop switch for payment intake, payouts, and refunds.

Production rule:
- Emergency authority is operational authority only. It may stop, suppress, suspend, isolate, reindex, verify, diagnose, and recover; it cannot create SQL, ledger, publication, visibility, payment, payout, refund, or inventory authority.
- Finance emergency stops must live inside the existing financial readiness/lifecycle boundary and fail closed before external money movement.

Implemented hardening:
- Added `CommerceHubEmergencyStopOptions` bound to `CommerceHub:EmergencyStops`.
- `CommerceHubFinancialReadinessGate` now blocks CommerceHub payment intake when `CommerceIntakeStopped` or `PaymentIntakeStopped` is active.
- The same readiness gate blocks outward money movement when `OutwardMoneyMovementStopped`, `PayoutsStopped`, or `RefundsStopped` is active.
- Added `MarketplaceEmergencyOperationsCertificationTests` and `docs/architecture/commercehub-emergency-operations-certification-findings.md`.

Current evidence:
- `PointOfSale.API/Configuration/CommerceHubEmergencyStopOptions.cs`
- `PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialReadinessGate.cs`
- `PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceGovernanceController.cs`
- `PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceOperationsController.cs`
- `PointOfSale.Tests/Architecture/MarketplaceEmergencyOperationsCertificationTests.cs`
- `docs/architecture/commercehub-emergency-operations-certification-findings.md`

Intentional remaining debt:
- No support impersonation ceremony, universal durable worker pause/resume, central emergency dashboard, immutable notification inbox, SIEM-grade audit lake, production alert pack, or full PostgreSQL-backed chaos harness was introduced.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.5 Marketplace Audit, Compliance & Forensics

Problem:
- PASS 15.1 through PASS 15.4 exposed useful audit, governance, operations, finance, and emergency evidence, but evidence presence did not prove historical reconstruction, retention, or tamper resistance.
- Super Admin records were still routed through the generic in-memory audit queue, and several evidence tables lacked central tracked EF update/delete rejection.

Production rule:
- Critical evidence is append-only SQL history. Super Admin operator evidence must persist durably. Transient outboxes, logs, telemetry, and dashboards may support an investigation but cannot replace durable lifecycle evidence.

Implemented hardening:
- Added `GuardForensicEvidenceImmutability()` to protect generic audit, CommerceHub finance audit, vendor-payment audit, accounting audit, store-config audit, core ledger-entry, and marketplace dead-letter archive rows.
- Changed `SuperAdminAuditService` to persist through an isolated SQL scope instead of `IAuditQueue`.
- Added `MarketplaceAuditForensicsCertificationTests` and `docs/architecture/marketplace-audit-compliance-forensics-certification-findings.md`.

Intentional remaining debt:
- Generic tenant audit still needs durable outbox/retry semantics. Merchant listing lifecycle and background recovery operations need immutable history. Retention schedules, unified incident search, database-role restrictions, external immutable export, SIEM integration, and evidence-parity alerts remain required before production forensic certification.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.5B Forensic Evidence Hardening

- Added append-only SQL merchant lifecycle and CommerceHub recovery-operation ledgers.
- Wired merchant single-listing, bulk, governance suppression/restore, Super Admin reindex/verification, and background auto-healing evidence.
- Changed generic tenant audit to isolated SQL-first persistence with retry fallback and backlog health visibility.
- Added Super Admin forensic search, export-transfer, retention-inventory, and health endpoints.
- Remaining blockers are bounded deployment controls: PostgreSQL evidence roles, external WORM/SIEM retention, alert rules, SQL audit outbox fallback for PostgreSQL-outage process crashes, and non-CommerceHub recovery-ledger adoption.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.6 Marketplace Production Readiness Certification

- Final verdict: **NOT READY** for public production launch tomorrow.
- Re-certified authority, security, merchant, governance, operations, finance, incident response, audit, compliance, scalability, observability, chaos, deployment, and remaining-debt domains.
- No new runtime authority inversion was validated, so PASS 15.6 performs no runtime code hardening.
- Launch blockers remain PostgreSQL evidence-table roles, external WORM/SIEM retention and automated delivery, outage-surviving tenant-audit handoff, verified launch alert packs and dashboards, and deployment backup/restore/operator rehearsal evidence.
- Added `MarketplaceProductionReadinessCertificationTests` and `docs/architecture/marketplace-production-readiness-certification-findings.md`.

## 2026-06-01 PRODUCTION READINESS CLOSURE 1 Evidence Database Hardening

Problem:
- PASS 15.6 identified EF-only append-only enforcement as an A-class launch blocker because privileged runtime SQL and operational mistakes could bypass application guards.

Production rule:
- Certified forensic evidence tables are PostgreSQL-enforced append-only stores. Runtime identities may insert and read evidence but may not update, delete, truncate, alter, drop, or own evidence tables.

Implemented hardening:
- Added irreversible migration `20260601190000_EvidenceDatabaseHardening` with PostgreSQL rejection triggers for the certified evidence inventory.
- Added `ProvisionForensicEvidenceRoles.sql` for separate owner, writer, reader, migration, and maintenance roles.
- Added `VerifyForensicEvidenceDatabaseHardening.sql` for ACL, inventory, trigger, and direct-SQL rejection verification.
- Added `EvidenceDatabaseHardeningCertificationTests` and `docs/architecture/evidence-database-hardening-certification-findings.md`.

Remaining deployment work:
- Apply role provisioning and capture verification evidence in each environment.
- Install PostgreSQL forensic-violation alerts.
- Preserve the remaining PASS 15.6 launch blockers for external WORM/SIEM, outage-surviving audit handoff, alert packs, and backup/restore/operator rehearsal evidence.
## 2026-06-01 PASS 16.3 Cart Certification

- Certified the inspected CommerceHub cart journey across guest cart, authenticated cart, guest-to-customer adoption, add/remove/update/clear, pricing preview, inventory validation, session ownership, multi-tab/device conflicts, frontend UX, checkout handoff, auditability, chaos, and monitoring.
- Verified that cart persistence/pricing/checkout remain subordinate to current SQL listing, product, variant, tenant, inventory, pricing, promo, shipping, governance, readiness, and customer ownership authority.
- Runtime hardening performed: cart sanitization (`SanitizeAsync`) product visibility check now enforces `IsOnlineVisible` and `PublishStatus == Published` in addition to `IsActive`, achieving parity with `CalculateAsync` and checkout authority. Products that are active but offline or draft are removed during sanitization with an `ITEM_REMOVED_UNAVAILABLE` notice.
- Added regression freezes: `CommerceHubCartCertificationTests`, `CommerceHubCartPricingCertificationTests`, and `CommerceHubCartInventoryCertificationTests`. Findings: `docs/architecture/commercehub-cart-certification-findings.md`.

## 2026-06-02 PASS 16.5B Settlement & Merchant Balance Certification

- Certified CommerceHub settlement, merchant balance, commission debt, COD lifecycle, online-payment payable lifecycle, settlement records, vendor commission payment requests, payment-method ownership, reconciliation, refund financial impact, dashboards, replay/recovery, auditability, chaos posture, and monitoring posture against SQL ledger authority.
- Runtime hardening performed: `CommerceHubVendorDebtService` now treats `amountOwed >= threshold` as immediate suspension from new CommerceHub orders. The default threshold remains `1000m` EGP; grace-period fields are operator metadata only.
- Added regression freezes: `CommerceHubSettlementCertificationTests`, `CommerceHubMerchantBalanceCertificationTests`, and `CommerceHubFinancialReconciliationCertificationTests`. Updated vendor financial-control tests for immediate hard-limit suspension. Findings: `docs/architecture/commercehub-settlement-merchant-balance-certification-findings.md`.

## 2026-06-02 PASS 16.5C COD Risk & Debt Control Certification

- Certified the complete inspected COD risk-control lifecycle across debt accumulation, debt reduction, thresholds, suspension, reactivation, checkout, discovery, existing orders, concurrency posture, recovery/replay, dashboards, auditability, chaos posture, and monitoring posture.
- Runtime hardening performed: `CommerceHubVendorDebtService` now records threshold-crossing evidence when effective debt reaches the hard limit (`effectiveDebt >= threshold`), aligning `ThresholdCrossedAtUtc` with `amountOwed >= threshold` suspension.
- Added regression freezes: `CommerceHubCodRiskCertificationTests`, `CommerceHubVendorDebtCertificationTests`, and `CommerceHubVendorSuspensionCertificationTests`. Findings: `docs/architecture/commercehub-cod-risk-control-certification-findings.md`.

## 2026-06-02 PASS 16.5D Payout & Reconciliation Certification

- Certified the inspected payout lifecycle across merchant payout identities, Super Admin payout visibility/execution, payout requests, processing handoff, completion evidence, failure/non-execution proof, merchant balances, settlements, refunds, reconciliation, recovery/replay, dashboards, auditability, chaos posture, and monitoring posture.
- No new runtime code hardening was required. Existing inspected controls include trusted-tenant vendor payout identity ownership, Super Admin vendor/method execution binding, serializable payout transactions, earning row locks, ledger confirmation, one processing/reconciliation payout slot per vendor, external handoff tokens, globally unique transfer evidence, completion fingerprints, refund/payout overlap blocking, reconciliation quarantine, and payout external-truth readiness checks.
- Added regression freezes: `CommerceHubPayoutCertificationTests` and `CommerceHubPayoutIdentityCertificationTests`; extended `CommerceHubFinancialReconciliationCertificationTests`. Findings: `docs/architecture/commercehub-payout-reconciliation-certification-findings.md`.

## 2026-06-02 PASS 16.5E Refund & Financial Reversal Certification

- Certified the inspected refund and reversal lifecycle across customer request creation, refund evidence, Super Admin approval/rejection/retry/execution, POS Admin return receipt, return transitions, refund execution, COD reversal, online-payment reversal, payout interaction, settlement interaction, reconciliation, recovery/replay, auditability, dashboards, chaos posture, and monitoring posture.
- No runtime code hardening was required. Existing inspected controls include customer/order-item ownership checks, Super Admin actor/evidence checks, trusted-tenant POS return receipt checks, serializable refund/return/execution transactions, transition policies, idempotency, outward-money readiness gates, payout-overlap blocking, financial state validation, cumulative refund validation, ledger fingerprint idempotency, and reconciliation-required states.
- Added regression freezes: `CommerceHubRefundCertificationTests`, `CommerceHubFinancialReversalCertificationTests`, and `CommerceHubReturnAuthorityCertificationTests`. Findings: `docs/architecture/commercehub-refund-financial-reversal-certification-findings.md`.

## 2026-06-02 PASS 16.5F-A CommerceHub Currency Authority Certification

- Certified CommerceHub currency authority across publication, listing persistence, price overrides, search projections, replay/recovery, cart, checkout, orders, settlements, payouts, refunds, ledgers, and frontend/backend implications.
- Runtime hardening performed: CommerceHub cart pricing/sanitation/persistence, checkout order/sub-order creation, Mongo projection, Elasticsearch SQL authority overlay, tenant-currency replay, vendor repayment default/display, vendor financial-control default, and payout queue fallback now use `FinancialCurrencyGuard.PlatformBaseCurrencyCode` instead of tenant/storefront currency.
- Added regression freezes: `CommerceHubCurrencyAuthorityCertificationTests`, `CommerceHubPublicationCurrencyCertificationTests`, and `CommerceHubPriceOverrideCurrencyCertificationTests`. Findings: `docs/architecture/commercehub-currency-authority-certification-findings.md`.

## 2026-06-02 PASS 16.5F-B CommerceHub Publication Currency Policy Enforcement

- Enforced the Egypt-marketplace publication rule: only EGP-priced catalog products may enter CommerceHub publication workflows.
- Runtime hardening performed: publish, republish, listing update, bulk publish, bulk listing update, and bulk override removal now reject non-EGP product price authority; unpublish remains available for visibility reduction.
- Replay/recovery hardening aligned reindex expected live identities with `CommerceHubSearchSqlAuthorityOverlay` so non-EGP listings are not counted or resurrected as live search authority.
- Frontend hardening added row-level policy messaging and disabled publish/update for blocked products.
- Added regression freezes: `CommerceHubPublicationCurrencyPolicyCertificationTests`, `CommerceHubPublicationCurrencyValidationTests`, and `CommerceHubBulkPublicationCurrencyTests`. Findings: `docs/architecture/commercehub-publication-currency-policy-certification-findings.md`.

## 2026-06-02 PASS 16.1B-A Authentication & Session Authority Certification

- Certified CommerceHub customer authentication across registration, verification, login, logout, password reset, email change, session creation/destruction, protected routes, frontend auth storage, backend enforcement, auditability, and chaos posture.
- Runtime hardening performed: `GET /api/customers/auth/me` now explicitly requires `CustomerJwt` plus the `Customer` role.
- Verified that registration and verification never issue JWTs or cookies, unverified customers cannot login, password reset/email change/password change/revoke-all advance durable session cutoff, and logout clears browser cookie authority while revoking the current JTI when available.
- Added regression freezes: `CommerceHubAuthenticationAuthorityCertificationTests`, `CommerceHubEmailVerificationCertificationTests`, and `CommerceHubSessionCreationCertificationTests`. Findings: `docs/architecture/commercehub-authentication-authority-certification-findings.md`.
## PASS S1-A Service Marketplace Listing Authority

- Service marketplace listing/search/detail paths must remain SQL-authority recomposed before public visibility.

## PASS F-S1-A Service Marketplace Frontend Discovery Authority

- Service marketplace frontend discovery must remain a derivative presentation layer over backend-qualified service listing DTOs and cannot use URL state, cache, filters, sorting, pagination, or realtime refresh as visibility authority.

## PASS F-S1-C Service Marketplace Frontend Service Detail Presentation Authority

- Service marketplace frontend detail presentation must remain bounded to backend visibility-qualified DTO authority.

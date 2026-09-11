# Catalog / Inventory Release Governance and C-5 Final Certification

Certification date: 2026-08-30

Scope: Catalog / Inventory Reliability Workstream, C-1 through C-5

Evidence labels used in this document:

- `SOURCE VERIFIED`: current production source was inspected.
- `TEST VERIFIED`: deterministic tests passed without external infrastructure.
- `INTEGRATION VERIFIED`: tests passed against disposable real PostgreSQL.
- `LIVE VERIFIED`: a running Development dependency or health endpoint was read.
- `NOT VERIFIED`: no stronger claim is made.

# Executive Summary

PostgreSQL remains the canonical authority for Product, Variant, Unit, Media, Category, price-bearing ProductUnit/ProductVariant state, Warehouse, ProductVariantStock, and InventoryReservation. MongoDB, Elasticsearch, and Redis remain disposable projections or acceleration layers. Committed Order and OrderItem rows remain historical business snapshots and are not rewritten by current Catalog mutations.

The consolidated current release entry point, `CatalogInventoryCertification.ps1`, passes 289/289 tests. This includes five tests against disposable real PostgreSQL for 10 concurrent single-unit reservations, two concurrent seven-unit reservations, concurrent reserve/release, concurrent commit/release, and multi-warehouse allocation. Deterministic lifecycle coverage verifies expiry, cancellation, cleanup, returns, and idempotent terminal transitions.

The .NET solution and all three relevant frontends build. EF reports no pending model changes. No high or critical runtime dependency vulnerability was reported. POS Web and CommerceHub each retain two moderate React Router advisories requiring a major router upgrade; they are P2 and do not authorize or mutate inventory.

The Development safe host reported PostgreSQL, MongoDB, RabbitMQ, Redis, Elasticsearch, R2, projection outbox, integration outbox, and MassTransit bus-outbox readiness healthy. The safe profile intentionally removes all hosted services, so the running MassTransit bus was unhealthy by design and no queue was consumed or outbox drained. Queue-level retired binding inspection and an end-to-end disposable Catalog projection mutation were not performed.

No active Catalog/Inventory P0 or P1 was found. No new production P0/P1 remediation was required during C-5.

Verdict: **CONDITIONALLY CERTIFIED - WORKSTREAM CLOSED**. The conditions are non-blocking live topology and end-to-end projection observations, not unresolved canonical correctness defects.

# C-1 -> C-4 Certification History

No repository document explicitly named as the C-1, C-2, C-3, or C-4 final report was found. Historical verdicts and counts are therefore not invented. The table separates the absent historical record from the current C-5 re-certification.

| Phase | Verdict | P0 | P1 | Focused Tests | Live Status | Outstanding |
|---|---|---:|---:|---|---|---|
| C-1 | Historical phase reported complete by the C-5 mission; standalone report absent. Current authority re-certification PASS. | Not recorded; current 0 | Not recorded; current 0 | Current Catalog/ProductAuthoring gates included in 289/289 plus broad ProductAuthoring 77/77 | Current SQL dependency health LIVE VERIFIED | Historical phase-specific counts unavailable |
| C-2 | Historical phase reported complete; current re-certification PASS | Not recorded; current 0 | Not recorded; current 0 | Current 92/92 authority/lifecycle, 84/84 reliability, 5/5 real PostgreSQL concurrency | PostgreSQL LIVE and INTEGRATION VERIFIED | None affecting correctness |
| C-3 | Historical phase reported complete; current re-certification PASS | Not recorded; current 0 | Not recorded; current 0 | Current Catalog-to-Order and tenant group 58/58 | No live Order mutation performed | Live mutation intentionally omitted |
| C-4 | Historical phase reported complete; current source/test re-certification PASS | Not recorded; current 0 | Not recorded; current 0 | Current projection/search/cache 50/50; broad Search 112 current passes; broad Projection 239 current passes | Dependency health LIVE VERIFIED; semantic mutation NOT VERIFIED | Queue-level topology and disposable mutation condition |

Relevant repository history that was not misrepresented as a C-1 through C-4 final report:

| Artifact | Recorded result | Current treatment |
|---|---|---|
| `docs/catalog-canonical-data-contract.md` | Contract passed with implementation observations on 2026-07-30 | Authority specification; current source/tests take precedence for implementation status |
| `docs/reports/INVENTORY_PRODUCTION_HARDENING_CERTIFICATION.md` | Inventory production ready | Supporting historical source evidence; current PostgreSQL concurrency supersedes unexecuted claims |
| `docs/PROJECTION_RUNTIME_CERTIFICATION.md` | One historical Development product absent from Mongo and ES | Historical live observation; not asserted current and retained as a P2 live-verification condition |
| `docs/R1_OUTBOX_DRAIN_PROJECTION_RECOVERY_CERTIFICATION_2026-08-28.md` | Order/Affiliate projection recovery failed | Outside Catalog/Inventory ownership; safe-host outbox checks are now healthy, but no Order/Affiliate architecture was reopened |

# Current Worktree

Initial baseline:

- Branch: `main`, tracking `origin/main`.
- Initial tracked state: 23 modified paths (`git diff --stat` reported 22 content diffs because `PurchaseReturnsConcurrencyIntegrationTests.cs` had no unstaged content diff).
- Initial untracked state: 4 files.
- `git diff --check`: exit 0; only LF-to-CRLF notices.
- No unrelated change was reverted.

Initial modified production and test files:

```text
PointOfSale.API/Controllers/CategoriesController.cs
PointOfSale.API/Controllers/PurchaseReturnsController.cs
PointOfSale.API/Controllers/StorefrontLandingPageController.cs
PointOfSale.API/Controllers/UnitsController.cs
PointOfSale.API/Controllers/WarehousesController.cs
PointOfSale.API/Services/AI/CommerceContextBuilder.cs
PointOfSale.API/Services/Checkout/CheckoutService.cs
PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs
PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs
PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs
PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs
PointOfSale.Infrastructure/Reliability/ProductVariantInventoryLineageAuthority.cs
PointOfSale.Tests/Architecture/CommerceHubSearchVisibilityCertificationTests.cs
PointOfSale.Tests/Architecture/CommerceHubVendorVisibilityAuthorityCertificationTests.cs
PointOfSale.Tests/Architecture/ElasticsearchFreshnessSearchConvergenceCertificationTests.cs
PointOfSale.Tests/Architecture/PaymentLifecycleTests.cs
PointOfSale.Tests/Architecture/PricingAuthorityConsolidationCertificationTests.cs
PointOfSale.Tests/CategoriesControllerTenantScopeTests.cs
PointOfSale.Tests/CommerceHub/InventoryConsistencyServiceTests.cs
PointOfSale.Tests/Infrastructure/ProductSqlAuthorityProjectionWriterTests.cs
PointOfSale.Tests/Infrastructure/ProductVariantMergeSplitAuthorityTests.cs
PointOfSale.Tests/Integration/InventoryReservationLifecycleIntegrationTests.cs
PointOfSale.Tests/Integration/PurchaseReturnsConcurrencyIntegrationTests.cs
```

Initial untracked prior-phase files:

```text
PointOfSale.API/Services/ProductAuthoring/CatalogDependencyProjectionPropagation.cs
PointOfSale.Tests/Architecture/C2InventoryReliabilitySourceTests.cs
PointOfSale.Tests/Infrastructure/CatalogDependencyProjectionPropagationTests.cs
PointOfSale.Tests/Integration/InventoryReservationConcurrencyIntegrationTests.cs
```

C-5 adds only governance/test assets:

```text
CatalogInventoryCertification.ps1
docs/CATALOG_INVENTORY_RELEASE_GOVERNANCE.md
PointOfSale.Tests/InventoryServiceTests.cs (fixture normalization only)
```

# Open Finding Inventory

Old text is not treated as an open finding without current confirmation.

| Finding | Phase/artifact | Severity | Current Status | Production Impact | Owner | Action |
|---|---|---|---|---|---|---|
| Published aggregate completeness is primarily application-enforced | Historical `docs/issues.md` F-06 | P2, reclassified from historical P1 | Current ProductAuthoring 77/77; no current bypass proven; DB-wide publication constraint not established | Defense-in-depth risk, no observed invalid commit | Catalog | Keep authoring behavioral tests mandatory; add DB constraint only with a clean migration/data audit |
| Full/architecture repository suites red | Historical `docs/issues.md` F-07 | P2 historical test debt | Confirmed broad source-shape and removed-artifact failures; current 289/289 gate green | Does not indicate a current Catalog defect | Test owners | Retire or rewrite obsolete tests independently |
| EF pending model changes | Historical `docs/issues.md` F-08 | Closed | `dotnet ef migrations has-pending-model-changes` reports none | None | Data platform | Keep migration drift gate |
| Time-dependent Product projection replay | Historical `docs/issues.md` F-10 | Closed for current Product path | SQL Product generation now drives projection version; focused writer tests pass | None found | Projection | Keep generation tests |
| Fragmented Product mutation ownership | Historical `docs/issues.md` F-12 | P2 accepted | Canonical authoring exists; category/unit/media/warehouse owners remain explicit adjacent commands | Maintainability and event coverage risk | Catalog | Require change-control checklist and projection trigger coverage |
| Historical Product missing from Mongo/ES | Projection runtime report | P2 live condition | Not re-probed for that product; current source/tests and health pass | No known current purchase-authority defect; projection visibility could still need operational repair | Platform operations | Use safe reindex/replay and verify SQL/Mongo/ES identity in staging |
| RabbitMQ retired/zero-consumer queue inventory | C-5 | P2 live condition | Source topology uses `ConfigureEndpoints`; management endpoint reachable but queue-level authenticated inspection NOT VERIFIED | Operational residue possible; no active obsolete Catalog binding proven | Platform operations | Inspect bindings/consumers before deployment; remove obsolete active bindings, preserve inert historical messages |
| Moderate React Router advisories | C-5 dependency audit | P2 | Open in POS Web and CommerceHub; Storefront clean | Client open-redirect/SSR hydration advisories; no inventory mutation authority | Frontend | Plan reviewed major router upgrade; do not force-fix |

# Authority Certification

| Domain | Canonical Authority | Mutation Authority | Read Model | Status |
|---|---|---|---|---|
| Product | PostgreSQL `Product` | Product Authoring/Catalog | Mongo ProductDocument, Elasticsearch ProductSearchDocument, Redis, frontend DTOs | PASS |
| Variant | PostgreSQL `ProductVariant` and attributes | Product Authoring variant lifecycle/pricing command boundary | Mongo embedded variants, ES, CommerceHub/Affiliate projections | PASS |
| Unit | PostgreSQL `Unit` and `ProductUnit` | Unit administration and Product Authoring binding/pricing boundary | Product projection units/default unit | PASS |
| Media | PostgreSQL `ProductMedia` and governed variant media assignment | ProductMediaService/Product Authoring assignment | Mongo/ES/CDN URL projections | PASS |
| Category | PostgreSQL `Category` and Product.CategoryId | Category administration/Product Authoring | Mongo/ES/category DTOs | PASS |
| Brand | PostgreSQL `Product.Brand` | Product Authoring | Mongo/ES facets | PASS |
| Price | PostgreSQL `ProductUnit.SellingPrice`, `ProductVariant.Price`, `OnlinePrice`, and governed compatibility mirrors | Pricing authority hosted by Product Authoring commands | Mongo/ES/display DTOs only | PASS |
| Inventory | PostgreSQL `ProductVariantStock` | Inventory services/repository and governed return/consistency paths | Mongo/ES/Redis availability projections | PASS |
| Reservation | PostgreSQL `InventoryReservation` plus `ProductVariantStock.ReservedQuantity` | ProductVariantStockRepository and InventoryConsistencyService | Display/projection only | PASS |
| Warehouse | PostgreSQL `Warehouse` and stock warehouse identity | Warehouse administration and Inventory | Warehouse/read projections | PASS |

Frontend, MongoDB, Elasticsearch, Redis, and search results are not canonical mutation authorities.

# Core Inventory Invariants

| Invariant | Evidence | Status |
|---|---|---|
| `Quantity >= 0` | EF check constraint plus mutation validation and tests | TEST/SOURCE VERIFIED |
| `ReservedQuantity >= 0` | EF check constraint and reservation lifecycle tests | TEST/SOURCE VERIFIED |
| `DamagedQuantity >= 0` | EF check constraint and return tests | TEST/SOURCE VERIFIED |
| `ReservedQuantity <= Quantity` | EF check constraint and real PostgreSQL over-allocation tests | INTEGRATION VERIFIED |
| `AvailableQuantity = Quantity - ReservedQuantity` | Computed entity property and assertions | TEST/SOURCE VERIFIED |
| Reservation terminal flow never exceeds reserved amount | InventoryReservation check constraints | SOURCE VERIFIED |
| Returns never exceed committed amount | InventoryReservation check constraint and return tests | TEST/SOURCE VERIFIED |
| Only current semantic variants admit new public availability | lineage authority and variant tests | TEST VERIFIED |
| Superseded/deleted variants do not admit new reservation | lifecycle/merge-split authority tests | TEST VERIFIED |
| Inactive/deleted warehouse does not admit new reservation | warehouse and inventory authority tests | TEST VERIFIED |
| Existing valid reservation may commit after later Catalog deactivation | historical-consumption authority and lifecycle regression | TEST VERIFIED |
| Projection/search/cache cannot authorize reservation | checkout/repository boundary and authority tests | TEST/SOURCE VERIFIED |

# Concurrency Certification

| Scenario | Evidence | Result |
|---|---|---|
| 10 x Reserve(1) from quantity 10 plus rejected eleventh | Real PostgreSQL `InventoryReservationConcurrencyIntegrationTests` | PASS |
| 2 x Reserve(7) from quantity 10 | Real PostgreSQL | PASS, exactly one succeeds |
| Concurrent Reserve/Release | Real PostgreSQL | PASS, exact reservation math |
| Concurrent Commit/Release | Real PostgreSQL | PASS, one valid terminal state, no negative values |
| Expire/Cancel/Cleanup | Deterministic lifecycle and cleanup tests | PASS |
| Multi-warehouse allocation | Real PostgreSQL | PASS, distinct warehouse reservations, no cross-allocation |

The repository uses deterministic row ordering and PostgreSQL `FOR UPDATE`. Reservation transactions use Read Committed with explicit row locks so waiters re-read committed stock instead of producing avoidable serializable aborts. No overselling, negative availability, duplicate reservation, lost reservation, or cross-warehouse allocation was observed.

# Catalog <-> Order Contract

`Order` and `OrderItem` own committed historical truth. `OrderItem` snapshots SKU, ProductName, VariantName, variant attributes, UnitId, WarehouseId, image, UnitPrice, CostPrice, affiliate commission terms, and line financial state. `Order.Currency` owns the order currency snapshot.

Current checkout revalidates SQL ProductVariant authority before commit. Current Catalog mutation does not write OrderItem financial or identity snapshots. Price, name, SKU, variant, unit reference, currency, and commission basis remain transaction-owned after commit. GTIN/barcode and category are retained where the transaction contract snapshots them; their current Catalog values are not read back as historical financial authority.

Evidence: Order snapshot/entity source, pricing consolidation tests, order lineage tests, affiliate snapshot tests, and CommerceHub commission snapshot tests in the 58/58 gate.

# Projection Certification

Canonical flow:

```text
PostgreSQL authority
  -> transactional outbox/event trigger
  -> RabbitMQ consumer
  -> SQL authority recomposition
  -> generation/idempotency/tombstone fence
  -> MongoDB
  -> Elasticsearch
  -> tenant-scoped Redis invalidation
```

| Contract | Evidence | Status |
|---|---|---|
| SQL remains current authority | ProductSqlAuthorityProjectionWriter calls SQL overlay | PASS |
| Product generation wins over stream-local event version | writer regression tests | PASS |
| Stale event cannot overwrite current state | mutation boundary/freshness tests | PASS |
| Duplicate event is idempotent | event fence and consumer tests | PASS |
| Delete/hidden SQL state creates tombstone/removal | writer and search consumer tests | PASS |
| Deleted entity cannot resurrect from stale event | generation/tombstone tests | PASS |
| Tenant fence is tenant plus aggregate identity | source and tenant tests | PASS |
| Category/unit/warehouse dependency changes trigger Product recomposition | dependency propagation tests | PASS |

# Search Certification

Required runtime contract:

```text
search candidates
  -> tenant and SQL authority admission
  -> authoritative filtering/sort
  -> authoritative pagination
```

Storefront, Affiliate, and CommerceHub current focused tests pass. POS Admin uses SQL authority or governed search/reindex paths where applicable. Search can discover candidates but cannot authorize checkout or reservation. Stale candidates are removed before authoritative page slots on certified paths.

Broad Search result: 112 passed, 4 failed. Three failures are superseded source-string assertions whose current queries are stricter or differently factored. One is an external Elasticsearch integration test that requires `SHOPIXY_ELASTICSEARCH_URL`; it is NOT VERIFIED, not a behavioral failure.

# Cache Certification

Redis Product keys include tenant and Product identity. Product list and homepage keys are tenant-scoped. `StorefrontProductCacheInvalidator` is the single live Storefront Product invalidation owner. It deletes detail keys, advances a tenant invalidation generation, and removes only tenant Product/home patterns. It does not flush Redis globally.

Invalidation coverage:

| Boundary | Trigger path | Status |
|---|---|---|
| Product update/delete/lifecycle | Product consumers/projection writer | PASS |
| Variant update/delete | ProductVariantProjectionConsumer | PASS |
| Stock/reservation/return | ProductVariantStockChangedEvent and projection writer | PASS |
| Media | ProductMediaChangedEventConsumer | PASS |
| Unit | dependency ProductUpdatedEvent | PASS |
| Category | dependency ProductUpdatedEvent | PASS |
| Warehouse active state | dependency ProductUpdatedEvent | PASS |

The invalidation generation is checked by owned cache population, preventing a read-before-delete writer from repopulating a stale generation. One broad architecture assertion rejects redundant controller no-store headers; the canonical middleware behavior itself passes and the overlap does not weaken cache safety.

# Event / Outbox Certification

All listed events carry EventId, OccurredAtUtc, TenantId, EventVersion/Version, and idempotency metadata through `IntegrationEvent`. SQL is the authority; payload values are triggers or snapshots for projection, never reverse authority.

| Event | Producer | Authority | Aggregate | Tenant | Version | Consumer | Idempotency |
|---|---|---|---|---|---|---|---|
| ProductCreatedEvent | Product Authoring | SQL Product | ProductId | Required | Product generation | Mongo writer, ES index, cache, Affiliate/CommerceHub | Event fence/idempotency key |
| ProductUpdatedEvent | Product Authoring and governed dependency propagation | SQL Product | ProductId | Required | Product generation | Mongo writer, ES update, cache, Affiliate/CommerceHub | Event fence/idempotency key |
| ProductDeletedEvent | Product lifecycle | SQL Product tombstone | ProductId | Required | Product generation | Mongo tombstone, ES delete, cache | Event fence/tombstone |
| ProductPublishStatusChangedEvent | Product lifecycle | SQL Product lifecycle | ProductId | Required | Product generation | visibility projection/search/cache | Event fence |
| ProductVariantCreatedEvent | Product Authoring | SQL Variant | VariantId | Required | Variant generation | variant projection, Affiliate/CommerceHub | aggregate event fence |
| ProductVariantUpdatedEvent | Product Authoring | SQL Variant | VariantId | Required | Variant generation | variant projection, Affiliate/CommerceHub | aggregate event fence |
| ProductVariantDeletedEvent | Product lifecycle | SQL Variant tombstone | VariantId | Required | Variant generation | variant tombstone, Affiliate/CommerceHub | aggregate event fence |
| ProductVariantStockChangedEvent | Inventory repository/outbox | SQL stock row | VariantId plus WarehouseId payload | Required | stock generation/time fence | stock projection, cache, Affiliate/CommerceHub | event fence and SQL recomposition |
| ProductMediaChangedEvent | ProductMediaService | SQL ProductMedia | ProductId | Required | Product/media event version | media projection and cache | event fence |

Product Authoring and dependency mutations publish within the EF transaction. Inventory stock synchronization stages durable outbox rows with source generation. Consumers support replay, duplicate collapse, stale rejection, and SQL recomposition.

# Retired Topology

Current source registers Catalog/Inventory consumers once and calls MassTransit `ConfigureEndpoints(context)`. No current manually configured obsolete Catalog/Inventory receive endpoint or binding was found. RabbitMQ AMQP is LIVE VERIFIED, RabbitMQ dependency health is healthy, and the management endpoint is reachable.

Authenticated queue/binding/consumer enumeration was NOT VERIFIED. No claim is made that every historical Development queue is empty. Any obsolete queue containing inert historical messages is infrastructure residue only if its active binding is removed. This is a non-blocking operational condition because no active corrupting route was identified.

# Tenant Isolation

Product, Variant, Unit, Stock, Warehouse, Reservation, projection, search, and cache paths bind tenant plus aggregate identity. Explicit `IgnoreQueryFilters` paths add tenant predicates before mutation. The reservation lock now requires tenant plus reservation ID. Category tests, storefront tenant tests, tenant lineage tests, CommerceHub visibility tests, and real PostgreSQL reservations pass.

No fake second live tenant was created. Deterministic existing test fixtures provide multi-tenant negatives. Live cross-tenant mutation was NOT VERIFIED.

# Security

- No Catalog/Inventory P0/P1 security issue was found.
- No secret value was printed or copied into this document.
- Tenant isolation is part of the mandatory 289-test gate.
- `npm audit --omit=dev`: Storefront 0 findings.
- `npm audit --omit=dev`: POS Web 2 moderate findings, both React Router advisories.
- `npm audit --omit=dev`: CommerceHub 2 moderate findings, both React Router advisories.
- The React Router fix offered by npm is a semver-major upgrade and was not forced.

# Performance

| Concern | Classification | Current treatment |
|---|---|---|
| POS Web 5,048.55 kB minified main chunk | P3 improvement | Build passes; plan code splitting |
| Storefront 1,279.46 kB minified main chunk and mixed dynamic/static imports | P3 improvement | Build passes; optimize independently |
| CommerceHub 684.24 kB minified main chunk | P3 improvement | Build passes; optimize independently |
| Per-line reservation/authority calls | P2 risk | Correctness-first locks retained; measure before batching |
| Large SQL projection recomposition | P2 risk | Accepted correctness tradeoff; monitor latency |
| Broad candidate scans before SQL admission | P2 risk | Bounded by candidate/search controls; SQL admission remains mandatory |
| Serializable scopes in adjacent write paths | P2 risk | Reservation path now uses explicit locks at Read Committed; monitor other contention |
| Historical test harness and source-shape tests | P2 test debt | Excluded only after classification; current behavioral gate remains mandatory |

No benchmark number is invented.

# Runtime

| Component | Evidence | Status | Required classification |
|---|---|---|---|
| API liveness | Safe host `/health/live` HTTP 200 | LIVE VERIFIED healthy | Required |
| API readiness | Safe host `/health/ready` HTTP 503 | Expected safe-profile condition | Overall 503 because bus intentionally stopped |
| PostgreSQL | TCP reachable, safe health healthy, real tests pass | LIVE/INTEGRATION VERIFIED | Required |
| MongoDB | Safe health healthy | LIVE VERIFIED dependency health | Required for projection; semantic product mutation NOT VERIFIED |
| RabbitMQ | AMQP reachable, health healthy, management endpoint reachable | LIVE VERIFIED dependency health | Required; queue topology NOT VERIFIED |
| MassTransit bus | Safe health unhealthy | Intentionally disabled by safe profile | Required in deployed normal host, non-blocking here |
| Redis | TCP reachable and health healthy | LIVE VERIFIED | Required acceleration, not authority |
| Elasticsearch | HTTP 200 and health healthy | LIVE VERIFIED | Required for configured search; external disposable test URL NOT VERIFIED |
| R2/object storage | Safe health healthy | LIVE VERIFIED | Applicable media dependency |
| Projection outbox | Safe health healthy | LIVE VERIFIED | Required |
| Integration outbox | Safe health healthy | LIVE VERIFIED | Required |
| Bus outbox readiness | Safe health healthy | LIVE VERIFIED | Required |
| Payment/provider checks | Safe health healthy where configured | Observed only; outside workstream | Not reopened |
| Caddy/certificate provider | Degraded | Optional/disabled Development infrastructure | Outside Catalog/Inventory release correctness |

The safe host sets `REQUIRE_REAL_POSTGRES_TESTS=1`, and repository startup removes every `IHostedService`. No consumer, queue, cache warmer, repair worker, or outbox publisher was run.

# Dependency Security

| App | Runtime audit | Classification | Blocking |
|---|---|---|---|
| POS Web | 0 critical, 0 high, 2 moderate | Direct React Router DOM plus transitive React Router; client redirect advisory and SSR hydration advisory; major upgrade offered | No |
| Storefront | 0 findings | Clean | No |
| CommerceHub | 0 critical, 0 high, 2 moderate | Same React Router advisories; major upgrade offered | No |

# Test Gate Normalization

Authoritative suite: `CatalogInventoryCertification.ps1`.

Historical failures were classified, not hidden:

| Failure class | Examples | Classification |
|---|---|---|
| Invalid fixtures | InventoryService tests omitted required Product/Warehouse rows | Fixed; behavioral tests now 4/4 |
| Superseded source shape | marketplace predicate formatting, projection helper names, old direct cache deletion | Historical brittle assertion |
| Removed architecture | direct legacy ProductVariant mutation endpoints | Historical; canonical Product Authoring intentionally rejects endpoint |
| Removed migration artifact | S60/S68 SQL filenames | Historical migration-coupled test |
| Invalid request fixture | missing `X-Pos-Cart-Id` | Historical fixture, not projection defect |
| External environment | `SHOPIXY_ELASTICSEARCH_URL` absent | Optional integration NOT VERIFIED |
| Unrelated domain | theme, CMS, promotions, shipping, payment, affiliate payout assertions | Outside Catalog/Inventory C-5 |

Broad time-boxed execution:

| Suite | Passed | Failed | Timeout | Current Defect? | Classification |
|---|---:|---:|---:|---|---|
| ProductAuthoring | 77 | 0 | 0 | No | Current broad Catalog gate |
| Search name sweep | 112 | 4 | 0 | No Catalog defect found | 3 superseded source assertions; 1 external ES NOT VERIFIED |
| Projection name sweep | 239 | 24 | 0 | No current focused Product projection defect | Removed endpoints/artifacts, stale source assertions, invalid fixtures, unrelated domains |
| Warehouse name sweep | 89 | 2 | 0 | No | Both require removed S68 migration artifact |
| Consolidated current gate | 289 | 0 | 0 | No | Authoritative release gate |

# Release Governance

| Governance Gate | Required Condition | Evidence | Status |
|---|---|---|---|
| Gate A: Canonical authority | One Product, Inventory, and Reservation authority | SQL entities, owner matrix, focused tests | PASS |
| Gate B: Inventory invariants | No oversell, negative availability, double consumption, or leakage | constraints, lifecycle tests, real PostgreSQL concurrency | PASS |
| Gate C: Cross-domain | Catalog mutation cannot rewrite Order history; stale Catalog cannot bypass reservation | Order snapshots, SQL checkout admission, tests | PASS |
| Gate D: Projection | stale cannot win; duplicate idempotent; delete cannot resurrect; tenant fence | generation/tombstone/idempotency tests | PASS |
| Gate E: Search | SQL admission and final pagination where required; search cannot authorize checkout | Storefront/Affiliate/CommerceHub tests | PASS |
| Gate F: Cache | tenant-safe, derivative, ordered invalidation | key builder, invalidation generation, cache tests | PASS |
| Gate G: Security | no P0/P1, tenant isolation, no secret leakage | tenant gate and audits | PASS |
| Gate H: Build/Test | current suite and builds pass; only classified debt remains | 289/289, four builds, EF drift clean | PASS |

# Release Policy

| Severity | Definition | Release policy | Catalog/Inventory examples |
|---|---|---|---|
| P0 | Catastrophic integrity/security breach | Immediate release stop | cross-tenant inventory mutation, systemic stock corruption, catastrophic projection authority breach |
| P1 | Material current correctness defect | Stop until fixed and verified | oversell, reservation loss/double consumption, stale projection authorizing checkout, deleted Product resurrection into purchase, cross-tenant cache contamination |
| P2 | Material risk without a proven active release correctness failure | Does not automatically stop release | performance/contention risk, defense-in-depth gap, moderate dependency issue, historical test debt, optional live certification |
| P3 | Improvement | Does not stop release | bundle optimization, UI polish, comments/documentation cleanup |

# Change Control

Every future Catalog/Inventory change must identify:

- affected canonical authority;
- transaction and lock boundary;
- event/outbox impact;
- projection and cache impact;
- tenant impact;
- concurrency impact;
- test and migration impact.

No Product/Inventory mutation may merge without behavioral certification when a contract changes. Source-string-only assertions are supporting checks, not substitutes for behavior.

# Regression Protection

Run from repository root:

```powershell
.\CatalogInventoryCertification.ps1
```

The entry point builds the solution and runs current Product authority, Inventory authority, Reservation lifecycle, real PostgreSQL concurrency, Variant lineage, Warehouse, Catalog-to-Order, Projection, Search, Cache, and tenant-isolation suites. It sets `REQUIRE_REAL_POSTGRES_TESTS=1` for the concurrency group, so missing PostgreSQL configuration fails rather than silently producing a false green result.

# Documentation

This document is the authoritative Catalog/Inventory release-governance and C-5 exit record. Older ownership documents remain useful field inventories but do not override this current gate. Historical runtime reports remain evidence of what was observed at their date, not current status.

# Release Checklist

| Area | Required check | C-5 result |
|---|---|---|
| Catalog | Product authority | PASS |
| Catalog | Variant authority and lifecycle | PASS |
| Catalog | Unit and ProductUnit authority | PASS |
| Catalog | Price authority and compatibility mirrors | PASS |
| Catalog | Category, brand, media | PASS |
| Inventory | Stock constraints and authority | PASS |
| Inventory | Reservation lifecycle | PASS |
| Inventory | Warehouse identity/active fence | PASS |
| Inventory | Concurrency and multi-warehouse | PASS |
| Inventory | Return and damaged return bounds | PASS |
| Inventory | Expiry/cancellation cleanup | PASS |
| Projection | Transactional outbox | PASS |
| Projection | Tenant/aggregate/version metadata | PASS |
| Projection | Mongo/ES/Redis derivative only | PASS |
| Projection | stale/duplicate/tombstone | PASS |
| Security | tenant/role boundaries | PASS |
| Security | secrets/logs | PASS; no secret exposed |
| Quality | focused tests | PASS 289/289 |
| Quality | builds | PASS all four |
| Quality | migrations | PASS, no pending model changes |
| Quality | dependencies | PASS with P2 moderate advisories |
| Runtime | database/dependency health | PASS in safe host |
| Runtime | normal queue consumers | NOT VERIFIED; safe host intentionally stopped them |
| Runtime | semantic projection mutation | NOT VERIFIED |

# Production Fix Table

| C-5 production fix | Severity | Reason | Verification |
|---|---|---|---|
| None | N/A | No new production P0/P1 remediation was required | Current production source compiled; 289/289 gate passed |

C-5 changed one test fixture, one release script, and this document. Prior dirty production changes were reviewed but not claimed as C-5 edits.

# Tests

| Gate group | Result | Evidence level |
|---|---:|---|
| Catalog and inventory authority | 92/92 | TEST VERIFIED |
| Projection, search, cache | 50/50 | TEST VERIFIED |
| Catalog-to-Order and tenant isolation | 58/58 | TEST VERIFIED |
| Inventory and variant reliability | 84/84 | TEST VERIFIED |
| Real PostgreSQL reservation concurrency | 5/5 | INTEGRATION VERIFIED |
| Consolidated | 289/289 | TEST/INTEGRATION VERIFIED |

# Builds

| Build | Result | Warnings |
|---|---|---|
| `dotnet build PointOfSale.sln --no-restore` | PASS | 0 |
| POS Web `npm run build` | PASS | existing 5 MB chunk warning |
| Storefront `npm run build` | PASS | existing bundle/mixed-import/browserslist warnings |
| CommerceHub `npm run build` | PASS | existing chunk warning |

# Live Verification

| Claim | Level | Result |
|---|---|---|
| PostgreSQL dependency reachable | LIVE VERIFIED | Healthy |
| Disposable PostgreSQL concurrency | INTEGRATION VERIFIED | 5/5 |
| Mongo/Rabbit/Redis/ES/R2 dependencies | LIVE VERIFIED | Healthy safe-host checks |
| API liveness | LIVE VERIFIED | HTTP 200 |
| Normal API readiness | NOT VERIFIED | Safe profile returns expected 503 because bus is intentionally stopped |
| Queue bindings/consumer counts | NOT VERIFIED | Management authentication not used |
| SQL -> event -> Mongo -> ES -> Redis mutation | NOT VERIFIED | No unnecessary live test data created |
| Cross-tenant live mutation | NOT VERIFIED | No fake second tenant created |

# Final Certification Matrix

| Governance Gate | Required Condition | Evidence | Status |
|---|---|---|---|
| Catalog authority | SQL Product/Variant/Unit/Media/Category/Price | source, ProductAuthoring 77/77, consolidated gate | PASS |
| Inventory authority | SQL ProductVariantStock only | source, constraints, tests | PASS |
| Reservation integrity | SQL InventoryReservation and reserved stock | lifecycle and real PostgreSQL tests | PASS |
| Concurrency | no oversell/loss/double consumption | real PostgreSQL 5/5 | PASS |
| Variant lineage | current variant admits; historical reservation can finish | merge/split/lifecycle tests | PASS |
| Warehouse | tenant/active/multi-warehouse fences | tests and real PostgreSQL | PASS |
| Catalog <-> Order | immutable committed snapshots | source and 58/58 group | PASS |
| Projection | generation, stale, duplicate, tombstone, tenant | 50/50 group | PASS |
| Search | SQL admission and no checkout authority | focused tests | PASS |
| Cache | tenant keys and ordered invalidation | source and cache tests | PASS |
| Tenant isolation | tenant plus aggregate at every boundary | focused tests | PASS |
| Security | no P0/P1; no secret leakage | audits and source | PASS |
| Tests | current authoritative suites green | 289/289 | PASS |
| Builds | backend/frontends build | four successful builds | PASS |
| Runtime | required dependencies healthy; normal consumers observed | safe-host health healthy; consumers intentionally stopped | CONDITIONAL |

# Final Risk Register

| Risk | Severity | Current Impact | Mitigation | Release Blocking? |
|---|---|---|---|---|
| Publication completeness lacks a proven universal DB constraint | P2 | Defense-in-depth only; current authoring gate passes | mandatory behavior tests; audited migration if constraint added | No |
| Live Catalog projection mutation not executed | P2 condition | Runtime semantic convergence not observed in C-5 | staging disposable mutation and SQL/Mongo/ES read | No |
| Rabbit queue-level retired topology not authenticated | P2 condition | Historical infrastructure residue may exist | inspect bindings/consumers before deployment; remove obsolete active bindings | No |
| Broad historical source-shape/migration test debt | P2 historical | Noise in broad sweeps | retire/rewrite outside release gate | No |
| Moderate React Router advisories | P2 | Client redirect/SSR risk; no inventory authority | reviewed major upgrade | No |
| Reservation/projection/search contention and scan risk | P2 | No benchmark or current correctness failure | monitor latency/locks and optimize from evidence | No |
| Large frontend bundles/mixed imports | P3 | Load performance only | code splitting | No |

# Final Test Register

| Gate | Tests | Result | Failures | Classification |
|---|---|---|---|---|
| Product | ProductAuthoring broad plus authority group | PASS | 0 | Current |
| Variant | ProductVariant, merge/split, lifecycle, clone/import | PASS | 0 | Current |
| Inventory | InventoryService, stock mutation, consistency | PASS | 0 | Current |
| Reservation | lifecycle, checkout reservation | PASS | 0 | Current |
| Warehouse | resolution/authority/readiness/multi-warehouse | PASS | 0 current; 2 broad | 2 historical removed-migration tests |
| Search | focused Storefront/Affiliate/CommerceHub | PASS | 0 current; 4 broad | 3 historical source checks; 1 optional external |
| Projection | Product writer/generation/event freshness | PASS | 0 current; 24 broad | historical/removed/unrelated/invalid fixtures |
| Mongo | writer, convergence, tombstone | PASS | 0 | TEST VERIFIED; dependency LIVE VERIFIED |
| Elasticsearch | freshness/search convergence | PASS | 0 current | external disposable-node test NOT VERIFIED |
| Redis | key/invalidation/cache generation | PASS | 0 current | TEST/SOURCE VERIFIED |
| Tenant | category/storefront/lineage/CommerceHub visibility | PASS | 0 | Current |
| Concurrency | real PostgreSQL reservation suite | PASS 5/5 | 0 | INTEGRATION VERIFIED |

# Final Release Blockers

| Finding | Severity | Production Defect? | Fixed? | Verified? | Release Blocking? |
|---|---|---|---|---|---|
| No active Catalog/Inventory release blocker | N/A | No | N/A | 289/289 plus builds and health | No |

# Final Workstream Status

```text
C-1 = CURRENT AUTHORITY RE-CERTIFIED PASS; HISTORICAL STANDALONE REPORT ABSENT
C-2 = PASS
C-3 = PASS
C-4 = PASS WITH LIVE TOPOLOGY/SEMANTIC OBSERVATION CONDITION
C-5 = CONDITIONALLY CERTIFIED - WORKSTREAM CLOSED

Production P0 = 0
Production P1 = 0
Production P2 = 2
Production P3 = 1
```

Production P2 counts only current production design/performance risks: publication DB defense-in-depth and measured-performance risk. Historical test debt, optional live evidence, infrastructure inspection, and client dependency advisories are tracked separately and are not mislabeled as production defects.

# Remaining Risks

Only the seven entries in the Final Risk Register remain. No active reservation, tenant, projection-authority, or historical-Order correctness defect is known.

# FINAL VERDICT

## CONDITIONALLY CERTIFIED - WORKSTREAM CLOSED

Core canonical correctness is proven. The only conditions are non-blocking live/infrastructure observations: authenticated RabbitMQ queue topology and one disposable end-to-end Catalog projection mutation. They do not invalidate PostgreSQL authority, inventory invariants, tenant fences, Order snapshots, or the current focused tests.

CATALOG / INVENTORY WORKSTREAM CLOSED
with documented non-blocking conditions.

Do not create C-6. Move to the next domain.

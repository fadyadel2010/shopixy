# Targeted Release Certification - 2026-08-28

Date: 2026-08-28  
Scope: checkout concurrency, order fulfillment lifecycle, storefront and affiliate projection convergence, Affiliate Admin presentation, readiness, RabbitMQ topology, isolation, builds, architecture gates, and production dependency audit  
Method: direct HTTP and read-only PostgreSQL, MongoDB, RabbitMQ, source, test, and package inspection; no browser automation, direct database mutation, RabbitMQ acknowledgement/purge, or real payment, payout, or carrier rail

# Verdict

## Final verdict: NO-GO

The targeted application paths, current architecture gate, production builds, product authoring repair, and SQL-authoritative projection rebuild pass. The release remains blocked by Cloudflare R2 readiness, retained legacy RabbitMQ topology, unresolved shared MongoDB ownership/disposition, two moderate React Router findings in each of POS Web and CommerceHub, and unavailable live AffiliateOnly, second-tenant, and payout-sandbox evidence.

No credential, token, provider secret, webhook signature, or raw configuration value is included in this report.

# Defects Fixed

| Defect | Classification | Remediation | Verification |
|---|---|---|---|
| PostgreSQL `40001` checkout retry retained stale EF relationship-fixup references | P1 | Clear retained order navigation state before retry reload | Checkout concurrency certification passed |
| Fulfillment-owned serializable transaction could fail without bounded retry | P1 | Retry only PostgreSQL serialization failures, with rollback, disposal, tracker clear, delay, and reload; caller-owned transactions unchanged | Fulfillment authority certification passed |
| Storefront and Affiliate projections omitted fulfillment terminal events | P1 | Consume terminal events and rebuild from current SQL authority | Fresh failed-delivery/returned convergence passed |
| Affiliate Admin orders omitted lifecycle and paid-amount evidence | P1 presentation defect | Map lifecycle metadata from Mongo and paid amount from the SQL ledger snapshot | Data-authority regression passed; API build passed |
| Disabled optional Fawry provider forced readiness unhealthy | P1 readiness defect | Return Healthy when Fawry execution is disabled | Focused readiness tests passed 6/6 |
| DNS readiness queried a TXT name with unsuitable answer behavior | P1 readiness defect | Query `_dmarc.google.com` through the configured resolver | Focused DNS tests passed 6/6; runtime readiness log reports recovery |
| Five published simple products lacked valid default-unit authority | P1 projection blocker | Use the supported product-unit authoring endpoint with existing SQL price/unit facts, row versions, and idempotency | Five HTTP 201 responses; projection rebuild completed with zero errors |
| Current architecture gate asserted deleted migrations and stale source markers | Certification contract debt | Assert current model snapshot and canonical payment, outbox, payout, and marketplace authority | `VerifiedSafeFlowsArchitectureTests` passed 12/12 |

# Runtime Lifecycle Matrix

| Fixture | SQL authority | Storefront projection | Affiliate projection | Replay | Result |
|---|---|---|---|---|---|
| `ORD-83B3-2026-01027` | `FailedDelivery` | `ShippingStatus=Failed` | `FulfillmentStatus=FailedDelivery` | Repeated terminal call HTTP 200 | PASS |
| `ORD-83B3-2026-01028` | `Returned` | `ShippingStatus=Returned` | `FulfillmentStatus=Returned` | Repeated terminal call HTTP 200 | PASS |

The fresh host registered `order-projection-v7`. PostgreSQL remains canonical and MongoDB remains derivative. Canonical order-status semantics were not changed to force fulfillment terminal states into the order-status field.

# Product Projection Convergence

| Check | Evidence | Result |
|---|---|---|
| Supported default-unit authoring | Five target products returned HTTP 201 | PASS |
| Supported SQL-authoritative rebuild | 17 processed, 9 applied, 8 safely skipped, 0 errors, `success=true` | PASS |
| Current SQL products represented in MongoDB | 17 SQL products, 17 SQL-backed Mongo documents, zero missing | PASS |
| Storefront admission/tombstones | 15 documents match direct SQL flags; two products were explicitly rejected by the SQL overlay and tombstoned at matching generations | PASS |
| Shared MongoDB documents | 58 documents belong to tenant IDs absent from current SQL; no orphan belongs to the current SQL tenant | PRESERVED |
| Total MongoDB product documents | 75: 17 SQL-backed plus 58 unknown shared-tenant documents | INFORMATIONAL |

The two apparent direct-flag differences are not stale projection failures. Runtime evidence records `OverlayResult=Null`, `VisibilityDecision=RejectedBySqlOverlay`, and successful tombstone writes for products `85ed1165-04ce-3267-0a3f-4277f7c4e98e` and `ba96c31c-4f26-5221-44a5-d97c885466a2`. The probe's simple SQL-flag comparison does not model category, variant, pricing, unit, and publication admission.

The baseline digest for the 58 foreign documents was `f350da78258d121ec23d323702aaa5c32b3cd04960fa5c272b1ca38edac01b3f`. The post-authoring probe preserved the same 58-document population and all repair authority was scoped to the current tenant, but it did not produce a directly comparable post-repair full-document digest. No foreign document was approved for deletion or repair.

During concurrent product and variant events, stale writes produced duplicate-key warnings before the mutation fence classified them as `SkippedStaleUpsert`. Later/higher-version writes applied, search retries recovered, and the supported rebuild completed with zero errors. This remains an operational noise/race observation, not evidence of missing current projections.

# Financial and Outbox Evidence

| Check | Evidence | Result |
|---|---|---|
| Baseline affiliate commission | Earned EGP 40, Approved EGP 40, Reversed EGP 40, net EGP 0 | PASS |
| Custom outbox | 2 total, 0 pending, 0 reconciliation for baseline fixture | PASS |
| Accounting outbox | 3 total, 0 pending, 0 failed | PASS |
| Journals | 1 posted COD and 2 posted refund journals | PASS |
| Projection/integration outbox health | Pending 0, repeated failures 0, critical lag 0 in captured evidence | PASS |

# Health Matrix

| Check | Observed state | Classification |
|---|---|---|
| `/health/live` | HTTP 200 Healthy after projection repair | PASS |
| `/health/ready` | HTTP 503 | BLOCKER |
| Cloudflare R2 | Unhealthy: not fully configured | Deployment configuration blocker |
| DNS resolver | Runtime recovery succeeded after the `_dmarc.google.com` fix | PASS |
| Fawry optional provider | Disabled-provider behavior fixed and focused tests pass | PASS |
| Certificate provider | Disabled, Degraded | Deployment decision required |
| Caddy edge | Disabled, Degraded | Deployment decision required |
| Raw full-health projection count | PostgreSQL 17 versus MongoDB 75, MongoDB ahead by 58 shared documents | Ownership/disposition blocker |

The captured readiness request logged R2 as the only Unhealthy readiness check. Certificate-provider and Caddy checks were Degraded. Health checks were not weakened to obtain a passing response.

# RabbitMQ Matrix

| Queue | Ready | Unacknowledged | Consumers at read-only probe | Classification |
|---|---:|---:|---:|---|
| `order-projection` | 23 | 0 | 0 | Retired legacy queue |
| `order-projection-v4` | 23 | 0 | 0 | Retired legacy queue |
| `order-projection-v5` | 23 | 0 | 0 | Retired legacy queue |
| `order-projection-v6` | 10 | 0 | 0 | Retired previous queue |
| `order-projection-v7` | 0 | 0 | 0 | Current endpoint; API was stopped when probed |

The four retired queues retain 79 ready messages. Exchange-level inspection confirms that event exchanges remain bound to each retired endpoint exchange, so queue-only inspection would understate continued routing. Certification did not acknowledge, purge, unbind, or delete any queue. Removal requires operator approval, export/backup evidence, and a fresh-event proof that only `order-projection-v7` receives and drains traffic without error-queue growth.

# Test Matrix

| Gate | Result |
|---|---|
| Current `VerifiedSafeFlowsArchitectureTests` | 12/12 passed |
| Fawry operational readiness certification | 6/6 passed |
| DNS resolver focused tests | 6/6 passed |
| Affiliate role, tenant scope, portal contract, payout authority, and isolation matrix | Prior focused evidence 68/68 passed |
| Broad architecture selection | 163 passed, 51 failed, 214 total |

The 51 broad architecture failures remain historical migration and source-shape debt outside the corrected current 12-test gate. They prevent a whole-repository green claim and must not be represented as failures introduced by the targeted repair.

# Build Matrix

| Application | Result | Notes |
|---|---|---|
| `PointOfSale.API` | PASS | 0 warnings, 0 errors |
| `PointOfSale.Web` | PASS | Existing oversized main-chunk warning |
| `PointOfSale.Storefront` | PASS | Existing mixed static/dynamic import, stale Browserslist data, and chunk-size warnings |
| `PointOfSale.CommerceHub` | PASS | Existing chunk-size warning |
| `PointOfSale.WhatsApp.Baileys` | PASS | TypeScript build passed |

# Production Dependency Audit

| Application | `npm audit --omit=dev` | Release classification |
|---|---|---|
| POS Web | 2 moderate: React Router | Upgrade/disposition required |
| Storefront | 0 vulnerabilities | PASS |
| CommerceHub | 2 moderate: React Router | Upgrade/disposition required |
| WhatsApp Baileys | 0 vulnerabilities | PASS |

The remaining advisories are React Router open-redirect and SSR hydration constructor-injection findings. The available automated fix upgrades `react-router-dom` to 7.18.3 and is a semantic major change for the current Router 6 consumers, so it was not applied without dedicated migration and regression review. No critical or high production dependency advisory remains in the four audited applications.

# Isolation and Payout Scope

| Check | Result |
|---|---|
| POS Admin trusted tenant context and role boundaries | PASS through focused tests |
| Affiliate API `AffiliateOnly` policy/source guards | PASS through focused tests |
| Affiliate payout authority and execution boundaries | PASS through focused tests |
| Live AffiliateOnly session | NOT VERIFIED; approved credentials unavailable |
| Live second-tenant negative isolation | NOT VERIFIED; second tenant unavailable |
| Live payout sandbox execution | NOT VERIFIED; approved sandbox rail unavailable |
| Real payment, payout, or carrier action | NOT EXECUTED by design |

# Release Blockers

| Blocker | Required closure evidence |
|---|---|
| `/health/ready` remains HTTP 503 because R2 is not fully configured | Configure R2 through deployment secrets and obtain HTTP 200 Healthy |
| 58 shared MongoDB product documents lack current SQL ownership | Owner-approved disposition or a comparable immutable full-document preservation proof; do not delete unknown shared data |
| Four retired projection queues retain 79 messages and active exchange bindings | Approval plus backup/export, remove retired topology, then prove only v7 receives and drains a fresh event |
| POS Web and CommerceHub each retain two moderate React Router findings | Migrate to a fixed Router 7 release with regression coverage, or formally accept/mitigate the risk |
| Broad architecture selection retains 51 historical failures | Resolve or formally scope the historical migration/source-shape debt before claiming whole-repository green |
| Live AffiliateOnly, second-tenant, and payout evidence unavailable | Provide approved credentials, fixtures, and sandbox rail, then rerun negative and payout checks |

# Re-Certification Conditions

1. Obtain HTTP 200 Healthy from `/health/ready` with R2 correctly configured.
2. Formally disposition or prove immutable preservation of the 58 foreign MongoDB documents without direct mutation.
3. Remove retired RabbitMQ topology under operator approval and prove `order-projection-v7` alone drains a fresh event.
4. Resolve or formally accept the remaining moderate React Router advisories and rerun audits/builds.
5. Resolve or explicitly scope the 51 broad architecture failures.
6. Run live AffiliateOnly authorization, second-tenant denial, and approved payout-sandbox checks.

Until all six conditions are evidenced, the release verdict remains **NO-GO**.

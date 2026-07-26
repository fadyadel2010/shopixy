# SUPERADMIN-CON-01 Phase 5 Super-Admin Operational Chaos Certification Findings

Date: 2026-05-24

## Scope

This final certification pass reviewed the CommerceHub super-admin and global operations layer under replay storms, rebuild overlap, marketplace visibility chaos, moderation replay, dangerous primitive misuse, stale emergency authority, and operational recovery pressure.

The certification target is deterministic convergence under chaos. It does not claim impossible single-delivery behavior, universal helper adoption, fixed rebuild SLOs, or an unhackable platform.

## Chaos Scenarios Certified

- Duplicate replay floods and dead-letter retry overlap collapse through SQL-backed event fences or outbox retry paths before side effects.
- Rebuild interruption, restart, and overlap redeclare SQL authority through `SqlTruthRebuildGuard` or the CommerceHub SQL search overlay.
- Tenant-admin recovery remains tenant-scoped through trusted tenant context and cannot silently widen into global CommerceHub reindex or global search index mutation.
- Cross-tenant marketplace visibility is recomposed from current SQL tenant, listing, product, variant, category, stock, price, and media authority before public search or read-model visibility is preserved.
- Stale homepage, recommendation, and curated references are scrubbed from current SQL visibility during public reads and projection rebuilds.
- Stale moderation/review replay no longer reindexes CommerceHub search from an existing Mongo listing document without first applying `CommerceHubSearchSqlAuthorityOverlay`.
- Unsafe raw search writes, full replacement without SQL authority, missing freshness guards, unsupported derivative rollback, and direct dead-letter suppression remain forbidden dangerous primitive patterns.
- Stale super-admin token versions, deleted super-admin rows, and governance-restricted super-admin profiles fail closed through current SQL platform authority before emergency operations proceed.
- Non-read platform operations remain request-audited after current SQL platform actor recomposition.

## Replay/Rebuild Convergence Guarantees

- Replay payloads, old Mongo documents, Redis keys, Elasticsearch hits, queue payloads, and repair inputs are candidate evidence only.
- Current PostgreSQL authority decides apply, delete, tombstone, no-op, retry, or fail-closed outcomes.
- Duplicate completed replay is healthy when it records a collapsed convergence decision and does not repeat Mongo, Redis, Elasticsearch, finance, fulfillment, or marketplace visibility side effects.
- Overlapping rebuilds are safe only because each run recomposes from SQL authority again. They are not allowed to resume from derivative snapshots.
- CommerceHub search rebuild requires current SQL `CommerceHubListing` authority in addition to product, variant, category, stock, price, media, and tenant authority.
- Marketplace review aggregate replay now uses the same CommerceHub SQL search overlay before search mutation; if current SQL listing authority is gone, search visibility is deleted.
- PASS 5 extension: public image replay/rebuild cannot retain legacy or derivative image fields when current SQL `ProductMedia`/`ProductVariants.MediaId` no longer supplies media.
- PASS 5 extension: search replay/rebuild recomposes `AverageRating` and `ReviewCount` from current approved, non-deleted SQL reviews rather than retaining stale derivative review summaries.

## Targeted Runtime Hardening Added

- `MarketplaceProductReviewProjectionService.SyncSearchAsync` now calls `CommerceHubSearchSqlAuthorityOverlay.ApplyAsync` before review-triggered CommerceHub search indexing.
- When review aggregate replay encounters stale or unpublished listing authority, it calls tenant-scoped `ICommerceHubSearchIndexService.DeleteAsync(tenantId, listingId)` instead of indexing the stale Mongo document.
- Review-triggered search sync status updates now filter by both listing id and tenant id.
- POS-ADMIN-CERTIFICATION-01 PASS 5 also removes legacy/derivative public image fallback from the CommerceHub overlay and public product projection paths, and makes overlay review decorations rebuild from approved SQL moderation truth.

## Operational Misuse Findings

- Dangerous primitives remain necessary for platform operations, but they must not become authority shortcuts. The final freeze rejects missing SQL authority, missing freshness, missing fail-closed behavior, unsupported rollback, raw search bypass, and dead-letter suppression.
- Dead-letter retry remains a reset into normal ordered processing, not direct publish or processed-row marking.
- Rebuild pressure is handled by bounded pacing and SQL recomposition, not unbounded parallelism or bypassing aggregate blockers.
- Fail-closed deletion, tombstone, and no-op outcomes are valid incident-recovery results when current SQL rejects stale visibility.
- Operators can still run global/all-tenant work under platform authority, but the operation must name SQL authority, leave audit/telemetry, and stay inside runbook scope.

## Marketplace Chaos-Convergence Findings

- Product-only replay cannot restore marketplace public visibility without a current published SQL CommerceHub listing.
- Homepage/recommendation curation remains governance metadata. Curated ids are retained only while current SQL proves tenant/listing/category visibility.
- CommerceHub search document identity remains tenant-scoped for new writes, and recovery paths scrub legacy listing-only ids when encountered.
- Review and localization surfaces remain intentionally specialized. The review-triggered search resurrection gap found in this pass is hardened, but this does not certify every future review export, ranking, localization, feed, or cache warmer.
- Public image deletion and approved-review aggregate replay now fail closed/recompose deterministically on the reviewed CommerceHub overlay and product read-model boundaries.

## Emergency Authority Findings

- Super-admin JWT role/auth claims are evidence only. Current SQL `SuperAdmins`, current token version, and current governance status decide emergency platform authority.
- Stale emergency sessions fail closed after logout, refresh-token revocation, password change, token rotation, account deletion, or governance restriction.
- Request-level operational audit covers non-read `RequireSuperAdmin` operations after current SQL platform actor recomposition.
- No support impersonation or tenant override API is certified. Future support tooling must add current SQL platform/support actor authority, target tenant lineage, reason/reference capture, revocation semantics, durable audit, and replay-safe convergence.

## Remaining Chaos-Risk Debt

- This pass does not introduce enterprise IAM, SSO, ABAC, SIEM-grade immutable audit, request-body capture, adaptive global scheduling, fixed rebuild SLOs, or a universal chaos framework.
- Marketplace reviews are now safer for search convergence, but broader review exports, localization, personalized ranking, feeds, support caches, analytics/reporting, SEO/CMS-adjacent paths, and global scripts still require path-specific chaos certification when expanded.
- Some inspected paths use equivalent local SQL checks instead of shared helpers. That remains acceptable only when documentation and tests state the actual boundary precisely.
- The platform still accepts normal read-model lag. The guarantee is deterministic convergence and diagnosability, not synchronous derivative freshness.

## Regression Freeze Invariants

- Do not index, rebuild, or serve marketplace public visibility from Mongo, Redis, Elasticsearch, queue payloads, curated JSON, or review aggregates without current SQL listing/tenant/product/variant/category authority.
- Do not retain public images or search rating/count decorations from derivative state when current SQL media linkage or approved-review moderation truth no longer proves them.
- Do not let tenant-admin routes trigger global/all-tenant repair, global search index deletion/recreation, forced replay, or dead-letter suppression.
- Do not add non-read `RequireSuperAdmin` operations without current SQL platform authority and request-level operational audit coverage.
- Do not add dangerous full replacement, raw search write, Redis mutation, direct SQL/bulk repair, raw bus publish, or global primitive paths without source/runtime governance, telemetry, docs, and tests.
- Do not mark a replay-safe no-op invisible. Collapse, suppression, delete, tombstone, retry, fail-closed, and rebuild authority declarations must stay diagnosable.
- Do not document emergency rollback from derivative state. Rollback must recompose from SQL or route through lifecycle/reconciliation owners.

## 2026-05-24 POS Session Replay Addendum

- Tenant-admin session replay now converges through current SQL POS session authority before admin role middleware. Stale access tokens after refresh, logout, actor disable/delete, role drift, tenant lineage invalidation, or governance restriction reject rather than retaining operational authority until JWT expiry.
- Refresh replay and the attempted substitution of the access-token-visible session binding as a refresh credential are regression-tested fail-closed behaviors.
- This addendum does not claim multiple-device session management, token-family compromise detection, enterprise IAM, or support impersonation governance; those remain explicit future debt.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 7 Replay / Rebuild / Chaos Addendum

- Platform and tenant operational chaos certification now includes payment-cancellation worker restart overlap. The recovery outbox must claim `PaymentCancellationOutboxMessage` rows before invoking `IPaymentLifecycleService`, skip active claims, and resume after bounded lease expiry.
- This addendum does not change payment authority: payment cancellation still converges through `IPaymentLifecycleService`, not direct payment-status mutation or manual outbox completion.
- PASS 7 findings live in `docs/architecture/replay-rebuild-chaos-certification-findings.md`.

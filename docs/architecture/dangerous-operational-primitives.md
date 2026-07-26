# Dangerous Operational Primitive Governance

Date: 2026-05-25

## Purpose

Dangerous operational primitives are low-level tools that can repair or damage derivative state faster than normal business workflows. They are not business authority. They may be used only when a reviewed authority boundary, runbook, or platform escalation explicitly allows them.

## Primitive Catalog

| Primitive | Why dangerous | Required boundary |
|---|---|---|
| Direct SQL or bulk mutation | Can bypass lifecycle services, interceptors, tenant co-ownership, outbox staging, and version guards | Existing authority service or reviewed allowlist with tests |
| Full Mongo document replacement | Can overwrite SQL-recomposed fields with stale projection state | Domain writer, repository boundary, or `DerivativeProjectionMutationGuard` |
| Raw Elasticsearch visibility write | Can publish stale or cross-tenant search visibility | SQL overlay plus reviewed search index service |
| Redis pattern invalidation/manual key edit | Can amplify replay storms or hide stale authority assumptions | Approved cache boundary with replay collapse/freshness |
| Raw MassTransit publish/send | Can bypass aggregate ordering, affected-tenant validation, and transaction boundaries | Central event publisher or reviewed outbox infrastructure |
| Global/all-tenant repair | Can turn a tenant incident into platform-wide mutation | Platform authority and explicit runbook scope |
| Tenant override/support impersonation | Can bypass trusted tenant and actor authority | Reviewed platform/support ceremony with SQL actor authority |
| Manual financial repair | Can corrupt payment, refund, settlement, payout, or ledger truth | Lifecycle/reconciliation owner only |

## Approval Checklist

Before an operator or engineer uses a dangerous primitive, all of the following must be true:

- The current SQL authority owner is named.
- Tenant scope is explicit and tenant-local unless platform/global scope is approved.
- Current SQL actor authority is checked for request-driven tenant operations.
- Replay collapse, idempotency, freshness, lease, or ordering behavior remains intact.
- Fail-closed behavior is documented: delete, tombstone, no-op, retry, block, or reconciliation-required.
- Operational convergence telemetry or equivalent diagnostics will classify the result.
- The rollback path is derivative-safe: invalidate, rebuild, reindex, delete, or replay from SQL; never copy stale derivative state.

## Forbidden Shortcuts

- Do not edit Mongo, Redis, or Elasticsearch to make a screen look correct when SQL authority disagrees.
- Do not use provider callback payloads to force local payment success after SQL reached a terminal incompatible state.
- Do not manually insert, update, or delete ledger rows to clear accounting queues.
- Do not mark dead letters or outbox rows processed to hide a poison authority failure.
- Do not copy tenant-scoped documents, cache keys, or search documents across tenants.
- Do not run global index deletion/recreation from tenant-admin tooling.
- Do not disable `ProjectionEventFence`, freshness filters, `SqlTruthRebuildGuard`, `TenantLineageAuthority`, `AdminTenantAuthority`, or `DerivativeProjectionMutationGuard` during incidents.

## TENANT-CERTIFICATION-01 Regression Freeze

- Tenant-scoped derivative writes must include tenant scope in the document id, mutation filter, or both.
- Full Mongo replacement for tenant-scoped public visibility must either use a domain writer or call `DerivativeProjectionMutationGuard` with current SQL authority, freshness, fail-closed behavior, and a named authority kind.
- Tenant-admin repair must stay tenant-scoped through trusted tenant context and current SQL actor authority.
- All-tenant repair, global search index mutation, global cache purge, tenant override, support impersonation, and manual financial repair remain platform/runbook primitives only.
- Certification findings live in `docs/architecture/tenant-isolation-certification-findings.md`; operator recovery behavior lives in `docs/operations/operational-recovery-runbooks.md`.

## SUPERADMIN-CON-01 Platform Authority Freeze

- Super-admin access tokens are candidate evidence only. Current SQL `SuperAdmins`, current `TokenVersion`, and current super-admin governance status decide whether global platform work may proceed.
- Every endpoint with the `RequireSuperAdmin` policy must pass through `SuperAdminPlatformAuthorityMiddleware`, including platform-policy controllers outside `/api/super-admin/*`.
- Stale super-admin access-token reuse after logout, refresh-token replay revocation, password change, account deletion, or governance restriction must fail closed.
- Global/all-tenant repair, global cache/search mutation, platform recovery, marketplace-wide moderation, and emergency operational primitives remain allowed only as explicit platform authority or runbook scope.
- Dangerous primitive use under super-admin scope still requires the same SQL authority, replay/freshness, fail-closed, telemetry, source-guard, and documentation rules as tenant-scoped dangerous primitives.
- Certification findings live in `docs/architecture/super-admin-platform-authority-certification-findings.md`.

## SUPERADMIN-CON-01 Phase 2 Marketplace Governance Freeze

- Marketplace public product visibility must not be rebuilt from product SQL alone. A current SQL `CommerceHubListing` plus tenant/product/variant/category authority is required before Mongo or Elasticsearch public marketplace visibility is restored.
- Homepage/recommendation rebuilds and cache hydration must scrub curated references that no longer resolve to current SQL-visible listings or categories, including active CommerceHub category parent lineage.
- Public marketplace reads and curated homepage references must also reapply current vendor suppression. Governance-hidden vendors and vendors suspended from new orders are not public visibility authority even when listing/category/product rows still exist.
- CommerceHub search writes for tenant-owned listings must use tenant-scoped document ids; listing-only legacy ids are cleanup targets only.
- Duplicate homepage/recommendation replay must be fenced before full Mongo replacement, search indexing, or Redis fanout.
- Marketplace-wide moderation and global discovery primitives remain platform scope under current SQL super-admin authority or documented runbook scope.
- Certification findings live in `docs/architecture/marketplace-governance-certification-findings.md`.

## POS-ADMIN-CERTIFICATION-01 PASS 5 Public Visibility Freeze

- CommerceHub category ids in listings, homepage JSON, search hits, suggestions, and cached recommendation cards are evidence only. Use `CommerceHubCategoryLineageAuthority` or an equivalent SQL parent-chain check before preserving public listing, search, homepage, recommendation, or curated category visibility.
- An active CommerceHub leaf category with inactive, missing, self-referential, or cyclic parent lineage is not public visibility authority. Public projection repair should delete, tombstone, scrub, reject, or exclude it from verification counts rather than preserve stale visibility.
- CommerceHub product-detail reads must reapply `CommerceHubSearchSqlAuthorityOverlay` before serving Mongo-backed detail data. A cached/published listing document, storefront product category name, or legacy variant `ImageUrl` is not sufficient authority for public detail visibility.
- Marketplace product list/detail reads, homepage product sections, and curated-reference validation must also reapply current vendor suppression. Stale Mongo documents, Redis caches, Elasticsearch hits, or homepage JSON are not allowed to keep a governance-hidden vendor or an order-suspended vendor public.
- CommerceHub public image reindex, rebuild, repair, and replay must retain images only from current SQL `ProductMedia`/`ProductVariants.MediaId`; legacy `Images`, legacy variant `ImageUrl`, Mongo documents, and Elasticsearch fields are not fallback authority.
- Review-driven search sync and public search rebuild must recompute `AverageRating` and `ReviewCount` from current approved, non-deleted SQL `ProductReviews`; cached or projected review summaries are not moderation-derived search authority.
- Popular, similar, autocomplete, and zero-result text-term suggestions are not allowed to use raw SQL product/category LIKE matches, raw active variant rows, or pre-overlay candidate listings as final public-visibility proof. A term is only public when at least one current SQL candidate survives the CommerceHub search SQL overlay and current vendor-visibility suppression.
- Direct public projection restoration from derivative image or review-summary state is an unsafe operational shortcut and requires fail-closed rejection, telemetry, tests, and documented SQL authority before any future exception is considered.
- Service-marketplace search reindex is also a public projection primitive. Tenant-admin service-marketplace repair must stay tenant-scoped, must not delete/recreate the global `marketplace_services` index, and must index only current active non-deleted SQL service rows. Global service-marketplace index recreation remains super-admin/platform scope.
- Future marketplace ranking, feed/export, localization, cache warmer, support, review-export, analytics, or global script paths must add listing/category/media/moderation recomposition, replay/rebuild tests, and documentation in the same change.

## SUPERADMIN-CON-01 Phase 3 Recovery Freeze

- Tenant-admin recovery endpoints must not invoke global/all-listing rebuilds. CommerceHub tenant-admin search recovery must use trusted tenant context and tenant-scoped reindex.
- Global CommerceHub reindex, all-tenant repair, global cache purge, global search alias promotion, and forced dead-letter replay remain platform/runbook primitives.
- CommerceHub search recovery must declare current SQL listing authority in addition to product, variant, category, stock, price, media, tenant, and tombstone authority.
- Dead-letter retry must reset failed rows into the normal bounded processor path; direct publish, direct processed marking, or queue suppression is a forbidden shortcut.
- Rebuild interruption/restart and replay/rebuild overlap are valid only when each run redeclares SQL authority, respects tenant scope, preserves legacy-id cleanup, and emits diagnostics.
- Operational pacing options are safety controls. Do not replace them with unbounded parallelism or long-running uncancellable loops during recovery pressure.

## SUPERADMIN-CON-01 Phase 4 Emergency Governance Freeze

- Non-read `RequireSuperAdmin` operations must have request-level operational audit after current SQL platform actor authority succeeds. `SuperAdminOperationalAuditMiddleware` is the runtime boundary for this coarse platform-operation audit.
- Domain audit remains required where the domain already owns a richer record, such as refunds, payouts, vendor finance, user governance, accounting, store config, and marketplace management.
- Support impersonation and tenant override remain dangerous primitives. A future implementation must define SQL platform/support actor authority, target tenant lineage, reason/reference capture, replay-safe convergence, audit records, and revocation semantics before it can mutate tenant-scoped operational state.
- Emergency rollback must recompose from SQL authority or route through lifecycle/reconciliation owners. Do not rollback by copying Mongo, Redis, Elasticsearch, queue payload, dashboard, export, or support-tool state into business truth.
- Certification findings live in `docs/architecture/emergency-governance-certification-findings.md`.

## SUPERADMIN-CON-01 Phase 5 Chaos Certification Freeze

- Replay storms, rebuild overlap, dead-letter retry, stale moderation/review replay, stale emergency-session reuse, and dangerous primitive misuse are expected operational pressure conditions.
- Marketplace search mutation from review, moderation, rebuild, or recovery paths must revalidate current SQL CommerceHub listing authority before indexing. If SQL authority is missing, delete tenant-scoped search visibility instead of indexing from Mongo.
- Raw search writes, full Mongo replacement, Redis mutation, direct SQL/bulk repair, raw bus publish, direct dead-letter suppression, and derivative rollback remain forbidden unless a reviewed authority boundary, telemetry, audit/runbook scope, and regression tests are added in the same change.
- Duplicate replay collapse, no-op, delete, tombstone, retry, fail-closed, and SQL rebuild declarations must remain visible through convergence telemetry or a documented equivalent.
- Certification findings live in `docs/architecture/super-admin-operational-chaos-certification-findings.md`.

## POS-ADMIN-CERTIFICATION-01 PASS 1 Identity And Session Freeze

- Raw POS refresh-token persistence and raw JWT claim trust are dangerous identity shortcuts. New POS sessions must store only the current hashed refresh binding and must pass `PosSessionAuthority` before POS bearer access is accepted.
- Login, tenant-owner onboarding issuance/restoration, refresh, and logout intentionally rotate or clear the one current POS SQL session binding. Do not restore an older binding, bypass the concurrency boundary, or keep an access token active merely because cryptographic expiry has not elapsed.
- Future password change/reset, user disable/delete, tenant override, delegated escalation, or support impersonation tooling must explicitly invalidate or recompose POS session authority, attribute the effective actor, and add replay/revocation tests and findings documentation.
- Certification findings live in `docs/architecture/admin-authentication-session-certification-findings.md`.

## POS-ADMIN-CERTIFICATION-01 PASS 2 Tenant Scope Freeze

- Controller-local parsing of `X-Tenant-Id` for tenant-admin operational work is an unsafe scope primitive even when middleware currently emits a verified compatibility header. Tenant-admin code must use `ITenantContext.RequiredTenantId`.
- Mixed callback/admin endpoints are an escalation boundary: authenticated tenant actors must not use payload tenant ids to bypass tenant scope merely because external callbacks need that payload shape.
- Tenant-admin search recovery endpoints must not co-host or invoke platform/global alias promotion, migration catch-up, all-tenant rebuild, or global cache/search primitives.
- Certification-critical tenant-admin SQL reads and mutations must bind trusted tenant scope explicitly in SQL on sensitive customer/payment/admin repair paths; ambient EF query filters are not enough to document or freeze operational scope integrity.
- Tenant-owned category admin paths are part of that same freeze. `CategoriesController` list/detail/create/update/delete and default-category seeding must bind the trusted tenant explicitly in SQL and must not reintroduce `FindAsync(...)` or global-database seeding checks as the scope contract.
- Tenant-owned finance and shipping configuration paths are part of that same freeze. `BanksController`, `ExpenseCategoriesController`, `FinancialAccountsController`, and `ShippingMethodsController` must bind the trusted tenant explicitly in SQL for reviewed configuration reads/mutations, bank statement/opening-balance operations, financial-account parent validation, and shipping calculation/update/delete paths. Primary-key-only lookup is not an approved tenant-admin scope primitive on these rows.
- Tenant-owned WhatsApp account/template update is also part of the freeze. Reviewed service-layer update must require both `Id` and `TenantId`; controller-level tenant lookup alone is not enough once a shared service exists.
- Global affiliate taxonomy mutation is not tenant-admin scope. `AdminAffiliateCategoryController` create/update/activate/deactivate remains platform authority behind `RequireSuperAdmin`, even though read-only taxonomy browsing may remain tenant-admin-visible.
- The abandoned-cart recovery callback remains an approved external operation only under tenant-specific callback-key validation and idempotency; authenticated tenant-admin mismatches fail closed before persistence.
- Findings and regression protections live in `docs/architecture/tenant-isolation-certification-findings.md` and `TenantIsolationCertificationFreezeTests`.

## POS-ADMIN-CERTIFICATION-01 PASS 3 Inventory / Reservation Freeze

- Raw `InventoryReservations`, `ProductVariantStocks`, CommerceHub order state, public stock projections, and stock-sync payloads are dangerous evidence shortcuts on reviewed payment/recovery/consumption paths. Current SQL inventory lineage must still accept them.
- Marketplace reservation validation must not assume a single tenant for a marketplace order. Cross-tenant marketplace suborders require per-tenant current reservation lineage recomposition before aggregate acceptance.
- Payment retry, fulfillment reuse, and reviewed inventory consumption paths must keep using `ProductVariantInventoryLineageAuthority` and current stock-owner filtering. Do not reintroduce raw row checks that bypass current warehouse, product publication, variant ownership, or stock-owner authority.
- Reviewed availability reads and reviewed derivative stock-total recomposition are also frozen under the same rule. Do not sum undeleted `ProductVariantStocks` rows directly for cart/checkout availability, stock-sync/event totals, storefront variant stock totals, or CommerceHub variant stock totals outside current stock-owner lineage.
- Reviewed SQL fallback checkout reads and reviewed storefront/search SQL overlays are frozen under the same rule. Do not let `ProductVariantReadRepository` fallback or `ProductSearchSqlAuthorityOverlay` accept default-variant or stock authority from raw undeleted rows alone; they must recompose current public variant lineage plus current stock-owner lineage.
- Reviewed storefront cart SQL snapshots, affiliate intelligence SQL snapshots, and stock-compensation replay totals are frozen under the same rule. Do not preserve cart quantity, affiliate stock visibility, or replayed stock totals from raw undeleted stock rows; recompose through current stock-owner lineage first.
- Return receipt replay must also keep current stock-owner filtering. A historical committed reservation is not enough to restore stock when current SQL warehouse activity, variant semantic ownership, or stock ownership has been lost.
- Warehouse reassignment, stock transfer, reservation migration, bulk inventory import, and manual stock repair remain dangerous primitives requiring explicit SQL transaction boundaries, replay semantics, diagnostics, and path-specific certification before rollout.
- Findings and regression protections live in `docs/architecture/product-inventory-reservation-certification-findings.md`.

## POS-ADMIN-CERTIFICATION-01 PASS 4 Financial Integrity Freeze

- Manual settlement, refund, payment-status, or ledger repair remains forbidden outside the owning lifecycle/reconciliation path even when a batch header, provider export, or dashboard appears internally consistent.
- Reviewed gateway/COD settlement recording must reject non-positive gross amounts and fees outside `[0, gross]` before settlement persistence or payment-status mutation. Provider exports or dashboard totals are not authority to carry invalid fee math into SQL.
- `PaymentSettlementBatch.OrderCount` is dangerous as a standalone operator shortcut. Settlement accounting and settlement recovery must recompose current `PaymentSettlementOrders` lineage before treating a settlement batch as postable authority.
- Soft-deleted settlement batches are not posting authority. Historical `PaymentSettlementOrders` rows cannot resurrect settlement accounting once current batch authority is gone.
- Post-settlement refund snapshot fields on `OrderRefund` are not posting authority. Refund repair and refund accounting must revalidate current settlement-batch membership for the refunding order before bank-account refund posting.
- Existing post-settlement refund history remains settlement authority evidence as well. Once any current refund proves an order has already refunded after settlement, later refunds must continue revalidating live settlement-batch membership even if `Order.PaymentStatus` has already moved to `PartiallyRefunded`.
- Accounting replay and immediate settlement posting must keep the same SQL-first convergence rule. Do not bypass `AccountingLedgerAuthority` or fall back to a weaker fast-path-only settlement check to clear retries faster.
- Retaining or manually restoring storefront payment/order projection visibility after current SQL order payment authority is gone is a dangerous derivative shortcut. Reviewed payment replay must delete stale projection/cache visibility and must not retain conflicting payload-derived timeline text as current financial evidence.
- Settlement-lineage drift is a reconciliation signal, not a license for manual forced posting. Preferred outcomes remain retry, no-op, block, or reconciliation-required.
- Findings and regression protections live in `docs/architecture/orders-payments-refunds-accounting-certification-findings.md`.

## POS-ADMIN-CERTIFICATION-01 PASS 6 Operational Tooling Freeze

- Partial SQL-row search rebuild is a dangerous operational shortcut. Tenant-admin storefront search repair must not index Elasticsearch documents from minimal SQL field copies, raw `SalePrice`, stale repair snapshots, or derivative read models.
- `AdminSearchSyncController.SyncProducts(...)` is now a reviewed tenant-admin operational boundary. It must retain `ProductSearchSqlAuthorityOverlay.ApplyAsync(...)`, `ProductSearchDocumentMapper.MapFromProductDocument(...)`, tenant-scoped delete convergence, and fail-closed suppression when current SQL search authority is missing.
- If current SQL storefront/category/unit/publication/media/promotion/search authority does not prove product visibility, the reviewed repair path must delete stale search visibility rather than manufacture a “baseline” search document.
- `AdminSearchSyncController.SyncCategories(...)` is now also a reviewed tenant-admin operational boundary. It must declare SQL rebuild authority and must delete tenant-scoped stale Elasticsearch category documents that are missing from current SQL authority after the category sync pass.
- `AdminDataSyncController` category and shipping-method Mongo sync is a reviewed bulk-mutation primitive. It must declare SQL rebuild authority and must delete tenant-scoped stale Mongo documents that are missing from current SQL authority; one-way Mongo sync is not an approved rebuild mode.
- `AdminSyncController` product projection rebuild must retain `IProductSqlAuthorityProjectionWriter` for SQL-authoritative Mongo convergence and must route cache invalidation through tenant-scoped `IProductCacheService`; direct Redis server enumeration or key deletion in that controller is not an approved repair primitive.
- `AF16E_ReindexAffiliateProductsWithStorefrontUrl` is a reviewed dangerous primitive for raw Mongo/Elasticsearch affiliate repair. It must require current SQL tenant storefront metadata plus current SQL `Tenant.Currency`, redeclare SQL repair authority through `SqlTruthRebuildGuard`, and fail closed when either authority is missing.
- Affiliate repair must not synthesize fallback currency. Silent `USD` injection or stale affiliate visibility preservation when tenant storefront/currency authority is missing is an operational-governance violation; the correct recovery outcome is stale affiliate visibility deletion.
- Future tenant-admin search/cache/CMS/SEO/support/export rebuild primitives must either reuse the shared SQL overlay or document an equivalent SQL-first authority boundary, dangerous primitive guard, and replay/rebuild tests in the same change.
- Legacy service-marketplace search projection and drift reconciliation may still use raw Elasticsearch mutation, but only after current SQL proves active service, active tenant, and same-tenant active provider-profile authority. Replayed service events, raw active service ids, missing profiles, inactive tenants, or cross-tenant provider profiles are not public service-search authority; stale documents must be deleted.
- `PaymentCancellationOutboxProcessor` is approved for direct SQL only to acquire a transaction-scoped PostgreSQL advisory lock during message claim. That approval is limited to `SELECT pg_advisory_xact_lock` inside the claim transaction with a stable tenant/order key plus normal lifecycle-service processing; it does not permit direct payment/order mutation or manual outbox processed-state edits.

## POS-ADMIN-CERTIFICATION-01 PASS 7 Replay/Rebuild/Chaos Freeze

- Payment cancellation outbox recovery is a lifecycle trigger, not payment authority. It must claim rows through `ClaimedUntilUtc` before calling `IPaymentLifecycleService`.
- Marketplace projection outbox recovery must retain row-versioned claim collapse in addition to `ClaimedUntilUtc`/processed-event completion semantics. Do not reduce it to lease-only in-memory claim checks.
- Active worker overlap, duplicate processor execution, and restart replay must collapse by skipping currently claimed payment-cancellation rows. Do not remove the lease because payment lifecycle idempotency exists.
- Claim leases must clear on success, manual-review completion, and retry/failure paths so crash recovery stays deterministic after lease expiry.
- Payment cancellation claim duration is an operational pressure control. Tune `PaymentCancellationOutbox:ProcessingLeaseSeconds`; do not replace it with unbounded parallelism, direct payment-status mutation, or manual processed-row marking.
- Marketplace projection duplicate-completion cleanup must emit convergence telemetry when the processed-event unique key proves another worker already completed the same logical replay. Do not leave that collapse branch as log-only behavior.
- Storefront footer full-document replacement remains a reviewed derivative primitive only after duplicate replay fencing, freshness checks, and current SQL tenant-lineage acceptance. If current tenant authority is gone, replay must delete tenant-scoped footer visibility and emit convergence diagnostics instead of preserving Mongo footer state.
- Homepage, CMS, footer, marketplace product/order, and marketplace review cache invalidation in `RedisInvalidationConsumer` remains an approved Redis mutation primitive only while it is protected by `ProjectionEventFence`. Duplicate replay must collapse before `RemoveByPatternAsync` or key deletion fanout.
- New replay/rebuild/recovery primitives must document their worker-restart semantics, amplification bounds, convergence telemetry, and fail-closed behavior before rollout.

## POS-ADMIN-CERTIFICATION-01 PASS 8 Operational Survivability Freeze

- OpenTelemetry meter registration for reviewed operational telemetry is part of the runtime survivability boundary. Do not add a reviewed metric and then leave it unexported from the process-wide meter pipeline.
- Reviewed exported operational metrics must use bounded dimensions. `tenant_bucket` is the approved exported tenant dimension on PASS 8 hardened surfaces; order-correlated metrics may expose bounded presence flags, but raw tenant ids and raw order ids remain logs/traces/audit evidence only.
- Free-form reasons, event types, search types, stages, actions, and sources must be normalized before metric emission. Do not emit queue payload fragments, document keys, exception text, or other unbounded values as labels.
- Reviewed exported `CommerceReliabilityTelemetry` provider, endpoint, surface, carrier, validation-method, projection, storage-provider, stock-sync, and isolation-level labels are part of that same freeze. The telemetry boundary must normalize them itself; caller discipline is not sufficient governance.
- Reviewed accounting telemetry is part of that same freeze. `PointOfSale.Accounting` must stay exported with bounded `tenant_bucket` plus normalized operation/gateway/error labels, and its singleton fallback counters must stay bounded by the same reviewed dimensions instead of raw tenant/provider/error keys.
- Reviewed Redis cache telemetry is part of the same bounded-dimension freeze. `PointOfSale.Redis.Performance` may expose cache and pattern families, but it must not export raw Redis keys, raw invalidation patterns, tenant ids, product ids, order ids, or other entity-specific cache material as metric labels.
- `CommerceHubReindexOptions` is an operational safety control. Recovery pacing values must stay startup-validated and bounded rather than relying on unchecked configuration during incident pressure.
- Payment-cancellation readiness is part of that same survivability boundary. `payment_cancellation_outbox_readiness` must remain a reviewed readiness surface for backlog age, repeated failure, terminal failure, active claim, and lease diagnostics; do not collapse it into log-only evidence during future incident tooling changes.
- Backup/restore remains a dangerous platform primitive. PostgreSQL is the only reviewed restore authority; do not restore MongoDB, Redis, Elasticsearch, RabbitMQ, or dashboard/export snapshots as business truth, and do not present derivative snapshot restore as a substitute for SQL-first rebuild/reconciliation.

## POS-ADMIN-CERTIFICATION-01 PASS 11.1 Super Admin Identity / Session Freeze

- Platform-operator authority must be current SQL `SuperAdmins` plus current governance state plus current token version. JWTs, cookies, refresh credentials, session cache state, Redis state, and raw role strings are evidence only.
- Legacy operational endpoints must use `RequireSuperAdmin` for global/platform work. Do not add tenant-admin routes that branch on `SuperAdmin` role strings to reach global reindex, repair, cache, search, finance, or governance primitives.

## POS-ADMIN-CERTIFICATION-01 PASS 11.5 Emergency Operations / Break-Glass Freeze

- Emergency authority is operational authority only. It may stop, suspend, freeze, isolate, recover, repair, reindex, replay, shut down, or diagnose, but it must not create visibility, publication, tenant, governance, financial, inventory, or SQL authority.
- Break-glass actions must be explicit, scoped, auditable, revocable, and runbook-backed. Do not add silent operator bypass, support impersonation, tenant override, stale-session elevation, role-string platform authority, or undocumented emergency mutation paths.
- Emergency rollback must recompose from SQL authority or route through lifecycle/reconciliation owners. Do not rollback by copying MongoDB, Redis, Elasticsearch, queue payloads, dashboards, exports, support-tool state, or old repair snapshots into business truth.
- Emergency marketplace actions may hide, suspend, suppress, unpublish, scrub, or reindex. They must not resurrect listings, vendors, homepage entries, recommendations, search documents, or publication intent when current SQL listing/product/vendor/category/tenant/variant authority is gone.
- Future emergency support, incident-response, global repair, shutdown, cache-warmer, feed/export, reporting, analytics, search-analytics, global-script, or dangerous-primitive tooling must update `EmergencyGovernanceCertificationTests`, emergency certification findings, runbooks, and `.ai-context` PASS 11.5 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 15.4 CommerceHub Emergency Operations Freeze

- CommerceHub emergency payment, payout, and refund stops must remain inside `CommerceHubFinancialReadinessGate` through default-off `CommerceHub:EmergencyStops` configuration.
- Do not implement a second finance stop path that bypasses refund, payout, payment, ledger, or reconciliation authority.
- Marketplace visibility stops may suppress listings, suspend tenants, apply vendor governance, and run SQL-derived verification/reindex. They must not restore or create visibility without current SQL tenant/vendor/product/variant/category authority.
- Recovery-stop and rollback procedures must remain SQL-first and cancellation/pacing aware. Do not mark outbox rows processed, disable replay fences, or copy derivative state into truth to end an incident quickly.

## Safe Use Patterns

- Prefer replay through the existing outbox/queue after the authority issue is fixed.
- Prefer tenant-scoped rebuild/reindex from SQL over global repair.
- Prefer Redis invalidation over manual cache value mutation.
- Prefer Mongo delete/tombstone plus SQL-backed rehydrate over full replacement from snapshots.
- Prefer search reindex through SQL overlays over raw Elasticsearch indexing.
- Prefer reconciliation-required states over guessing payment, refund, payout, shipment, or ledger outcomes.

## Documentation Duty

Every new approved primitive or global operational tool must update:

- The source guard or allowlist test that governs the primitive.
- The local architecture/risk document that explains the authority boundary.
- `docs/operations/operational-recovery-runbooks.md` if operator behavior changes.
- `.ai-context/KNOWN_RISKS.md` when risk posture or remaining debt changes.

Static approval is not enough for Tier 1 recovery paths. A new primitive needs runtime guard behavior or a documented equivalent, focused tests, and clear operator guidance in the same change.

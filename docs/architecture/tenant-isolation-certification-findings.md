# TENANT-CERTIFICATION-01 Tenant Isolation Certification Findings

Date: 2026-05-24

## Scope

This certification reviewed tenant isolation resilience and regression-freeze boundaries across trusted tenant resolution, tenant lineage, admin actor authority, derivative projections, replay/rebuild convergence, dangerous operational primitives, and operational diagnostics.

The canonical recovery runbook remains `docs/operations/operational-recovery-runbooks.md`. `docs/architecture/operational-recovery-runbooks.md` is an architecture pointer to that canonical runbook for older references.

## Boundaries Audited

- Tenant resolution: inbound `X-Tenant-Id` is stripped by `TenantResolutionMiddleware`; tenant scope is resolved from authenticated tenant claims or verified host/custom-domain evidence, then re-added only as a compatibility output.
- Admin scope: role-protected tenant admin operations use `AdminTenantAuthority` plus `ITenantContext.RequiredTenantId`; tenant-admin search repair blocks global index delete/recreate with `GLOBAL_SEARCH_CONTROL_FORBIDDEN`.
- Tenant lineage: replay/rebuild paths treat event tenant ids, Mongo documents, Redis keys, Elasticsearch ids, and repair inputs as candidate evidence only. Current SQL tenant lineage decides apply, delete/tombstone, no-op, retry, or fail-closed behavior.
- Derivative identity: affiliate public search ids and inspected repair paths use `TenantScopedDerivativeKey` or tenant+entity mutation filters. CommerceHub marketplace product/order documents use tenant+entity composite ids.
- Rebuild/reindex: inspected product search, CommerceHub marketplace, homepage, discount, and tenant-admin repair paths redeclare SQL authority through `SqlTruthRebuildGuard` or a documented SQL recomposition boundary.
- Dangerous primitives: direct SQL/bulk mutation, full Mongo replacement, raw Elasticsearch writes, Redis pattern invalidation, raw bus publishing, global repair, tenant override, and manual financial repair remain governed by source guards, runtime guards, or documented platform runbook scope.
- Observability: shared replay, authority, rebuild, dangerous primitive, and drift decisions emit low-cardinality convergence telemetry without raw tenant ids or document keys as metric labels.

## Chaos Scenarios Validated

- Duplicate completed projection delivery collapses before derivative side effects.
- Active overlapping replay suppresses the duplicate worker.
- Stale tenant replay and inactive tenant replay fail closed.
- Cross-tenant derivative ownership is rejected by current SQL tenant lineage.
- Stale admin role/session replay fails closed against current SQL actor authority.
- Tenant-admin repair remains tenant-scoped and cannot trigger global search index mutation.
- Stale cache/rebuild seeds cannot replace SQL authority.
- Dangerous full replacement rejects missing SQL authority or freshness guards.
- Payment, fulfillment, and accounting replay recomposes from current SQL lineage or collapses/retries.
- CMS page projection now follows the adjacent navigation/config/footer pattern: duplicate publish/delete replay is fenced, inactive or missing SQL tenant/page authority tombstones existing visibility, and full page replacement is runtime-reviewed as a dangerous derivative primitive.

## Verified Guarantees

- Trusted tenant context is the request authority; raw tenant headers are not controller-local mutation authority.
- Current SQL tenant lineage decides tenant-scoped projection existence.
- Current SQL actor authority decides tenant-admin privileged operation eligibility.
- Tenant-scoped derivative identifiers or filters are required for tenant-owned public visibility outside SQL.
- Replay and rebuild are convergence operations, not authority sources.
- Fail-closed deletion/tombstone/no-op behavior is a valid healthy outcome when SQL no longer proves visibility.
- Dangerous operational primitives remain implementation tools, not business authority.
- Tenant incident recovery is diagnosable through convergence telemetry and documented runbooks.

## Runtime Hardening Added

- `CmsPageProjectionConsumer` now uses `ProjectionEventFence` for publish and delete replay collapse before MongoDB side effects.
- CMS publish replay now checks active current SQL tenant lineage and fails closed by tombstoning existing tenant-scoped page visibility when SQL page/tenant authority is missing.
- CMS full `StorefrontPageDocument` replacement now calls `DerivativeProjectionMutationGuard` with `SqlAuthorityOverlayKind.StorefrontCms`, `StoreMetadata`, and `Tombstone` authority declarations.
- `SqlAuthorityOverlayKind.StorefrontCms` names the CMS derivative boundary for future guard/test documentation.

## Regression Freeze Invariants

- Do not parse `X-Tenant-Id`, query tenant ids, frontend tenant selectors, or repair payload tenant ids as tenant-admin authority.
- Do not add tenant-scoped derivative writes with product-only, listing-only, page-only, or slug-only document ids unless a tenant predicate and SQL lineage proof are present.
- Do not add Tier 1 full Mongo replacement, raw Elasticsearch visibility writes, Redis pattern mutation, direct SQL/bulk mutation, or raw bus publish paths without runtime/source governance and docs in the same change.
- Do not add tenant-admin endpoints that run all-tenant repair, global search index deletion/recreation, global cache purge, or platform-wide projection mutation.
- Do not rebuild from Mongo, Redis, Elasticsearch, event payloads, dashboards, exports, or old repair snapshots when SQL authority rejects visibility.
- Do not claim shared-helper adoption when a path uses an equivalent local SQL check; document the difference.

## Operational Confidence

The inspected Tier 1 tenant boundaries show production-grade tenant isolation confidence: trusted request resolution, SQL tenant lineage, SQL actor recomposition, tenant-scoped derivative keys, replay fences, SQL-first rebuild declarations, dangerous primitive guards, and convergence telemetry reinforce each other. The guarantee is deterministic convergence and diagnosability under replay/rebuild pressure, not theoretical perfection or universal path coverage.

## Remaining Tenant Risks

- CMS page projection is now hardened, but other CMS-adjacent, SEO, smart-offer, online-customer, notification, analytics, support-cache, feed/export, report rebuild, cache warmer, and global runbook paths still require path-specific certification before universal tenant-isolation claims.
- Some inspected paths use equivalent local SQL-lineage checks instead of always calling `TenantLineageAuthority`; this is acceptable when documented and tested, but remains harder to audit than shared-helper adoption.
- Public affiliate product Mongo documents still carry historical product-id physical ids; runtime filters and search ids are tenant-scoped, and a future data migration can change the physical id if operational evidence justifies it.
- Global/platform operations remain intentionally flexible but must stay behind platform authority and runbook scope.
- No fixed tenant rebuild SLO, adaptive tenant scheduler, enterprise IAM/SSO/ABAC, support impersonation ceremony, SIEM, or durable audit-event store was introduced in this certification.

## 2026-05-24 Marketplace Governance Certification Addendum

- SUPERADMIN-CON-01 Phase 2 validated the intentionally global CommerceHub marketplace surface without redefining it as tenant-isolated discovery.
- Marketplace product read-model visibility now requires current SQL `CommerceHubListing` authority in addition to current tenant/product/variant/category authority.
- Homepage/recommendation references are scrubbed through active tenant and current public listing/category lineage on super-admin validation, public reads, and projection rebuilds.
- CommerceHub Elasticsearch new writes use tenant-scoped listing ids while legacy listing-only ids are treated as cleanup targets.
- Detailed findings live in `docs/architecture/marketplace-governance-certification-findings.md`.

## 2026-05-24 POS Admin Authentication And Session Addendum

- POS-ADMIN-CERTIFICATION-01 PASS 1 closes a session-layer dependency of tenant isolation: default POS access-token authentication now recomposes current SQL user/session/tenant authority before role-protected `AdminTenantAuthority` evaluation.
- Login, tenant-owner onboarding issuance/restoration, refresh, and logout rotate or clear a hash-only SQL session binding, so stale access tokens cannot retain a former tenant-admin role or tenant identity merely until JWT expiry.
- Current inactive tenant lineage and current vendor governance ban reject POS session use; raw tenant headers and JWT tenant claims remain evidence only.
- Detailed findings and identity-specific debt are recorded in `docs/architecture/admin-authentication-session-certification-findings.md`.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 2 Admin Scope Addendum

### Confirmed Risk Hardened

- Several tenant-admin operational controllers still parsed the compatibility `X-Tenant-Id` output locally instead of consuming `ITenantContext`. The middleware strips inbound values before repopulating that header, but controller-local scope selection remained an unnecessary hidden override dependency.
- `AdminAbandonedCartRecoveryController.LogRecoveryResult` accepts external automation callbacks with a body tenant authenticated by that tenant's callback key. Its authenticated admin branch previously skipped the key while also accepting the body tenant, allowing a tenant admin to submit a recovery-result mutation for another tenant.
- `AdminCommerceHubController.FinalizeSearchMigration` remained reachable from tenant-admin scope even though alias promotion and global catch-up are platform/global search recovery primitives rather than tenant-scoped repair.
- `OnlineCustomersAdminController` and `PaymentMethodsAdminController` still carried governance drift on sensitive admin SQL paths: several reads and mutations depended on optional tenant context or ambient EF tenant filtering instead of explicit trusted tenant binding plus explicit tenant predicates.

### Runtime Hardening Added

- Admin Commerce AI toggle/status, AI product generation, AI explanation/help, abandoned-checkout dashboard reads, storefront footer mutation, and abandoned-cart recovery settings/statistics now obtain tenant scope from `ITenantContext.RequiredTenantId`.
- The recovery-log callback retains external API-key operation. When the caller is an authenticated tenant admin, its `request.TenantId` must equal trusted tenant context or the request fails closed with `403` and an attributed `admin_tenant_scope_mismatch` diagnostic.
- Tenant-admin CommerceHub search reindex remains available, but tenant-admin alias finalization now fails closed with `GLOBAL_SEARCH_CONTROL_FORBIDDEN`; global alias promotion and catch-up stay platform/runbook scope.
- Admin accounting, storefront auth-provider admin, wishlist analytics, pixel-tracking admin, online-customer session admin, payment-method admin, and online-customer admin controllers now consume `ITenantContext.RequiredTenantId` instead of optional tenant scope.
- Online-customer and payment-method admin queries now explicitly bind tenant scope in SQL for the inspected sensitive paths. This includes customer lookup/mutation, active-session counts, wishlist eligibility checks, refresh-token counts, and payment-method sync reads.
- Tenant-admin WhatsApp operational tooling now consumes `ITenantContext.RequiredTenantId` consistently, and its account/template lookup, enable/disable, and delete service paths bind tenant scope explicitly in SQL instead of depending on ambient query filters alone.

### Replay, Rebuild And Cross-Tenant Guarantees

- This pass preserves the existing SQL-first replay/rebuild, reindex, cache, Mongo, and Elasticsearch authority model. It narrows tenant-admin operational scope without creating a second tenant-resolution path.
- Tenant-admin CommerceHub search recovery now has a deterministic split: tenant-scoped `ReindexTenantAsync` is allowed, while alias promotion and post-finalization catch-up remain explicit platform recovery operations.
- Recovery telemetry rows are tenant-scoped operational evidence; an authenticated tenant-admin action can no longer write another tenant's recovery evidence by selecting a body tenant.
- Explicit SQL tenant predicates on the inspected admin customer/payment surfaces reduce replay/rebuild drift risk by removing dependence on ambient query-filter behavior for certification-critical scope binding.
- External callback tenant selection remains intentionally flexible only behind the tenant-specific callback-key check and existing idempotency key behavior.

### Regression Freeze And Remaining Debt

- `TenantIsolationCertificationFreezeTests` now includes the newly migrated admin surfaces and rejects reintroduction of `X-Tenant-Id` request parsing for tenant-admin scope.
- `AdminAbandonedCartRecoveryScopeTests` now proves authenticated cross-tenant recovery-log mutation is rejected before settings or persistence work begins and emits tenant-attributed `admin_tenant_scope_mismatch` telemetry.
- `SuperAdminRecoverySafetyCertificationTests` and `AdminCommerceHubScopeTests` now freeze that tenant-admin CommerceHub search recovery cannot call global alias-finalization or catch-up primitives and must return `GLOBAL_SEARCH_CONTROL_FORBIDDEN` instead.
- `TenantIsolationCertificationFreezeTests` now freezes explicit tenant predicates on the hardened online-customer and payment-method admin SQL paths.
- Storefront/checkout/webhook tenant-input paths, analytics/support/cache-warmer/report/export paths, and future operational endpoints still require path-specific certification; this addendum does not claim universal request-scope migration.
- Some tenant-admin surfaces outside this inspected set, such as future support/analytics/reporting or other mixed operational endpoints, still need path-specific review before broader universal admin-scope claims.
- `AbandonedCartRecoveryTelemetryTests` currently cannot execute its SQLite-backed fixture because the existing PostgreSQL-specific `AffiliatePayoutTransferEvidenceConfiguration` check constraint uses `btrim(..., E' \t\n\r')`, which SQLite rejects during full-model `EnsureCreated()`. The new scope/telemetry regression test is model-independent; provider-compatible full-fixture coverage remains test-harness debt.

## 2026-05-26 POS-ADMIN-CERTIFICATION-01 PASS 2 Tenant SQL Scope Addendum

### Confirmed Risk Hardened

- `AdminAffiliateController` no longer derives tenant scope from the JWT `tenant_id` claim. The controller now consumes `ITenantContext.RequiredTenantId`, so tenant-admin affiliate reads and settlement-adjacent operations follow the same trusted tenant binding as the rest of the inspected admin surface.
- `CouponsController` tenant-admin read and mutation endpoints no longer depend on ambient EF tenant filters or primary-key `FindAsync(...)` behavior. List, detail, update, delete, and toggle now bind the trusted tenant explicitly in SQL and fail closed with `404` for foreign-tenant coupon ids.
- `AuditLogsController` and `VendorPayoutMethodsController` now consume `ITenantContext.RequiredTenantId` on the reviewed tenant-admin paths instead of optional tenant accessors, removing additional admin-scope drift inside audit and vendor-finance tooling.

### Cross-Tenant Protection Semantics

- Trusted request tenant context remains the only reviewed tenant-admin scope authority. JWT tenant claims are evidence for authentication/middleware recomposition only and are not a controller-local scope selector.
- Foreign-tenant coupon ids now converge to `404` on admin detail/update/delete/toggle. This preserves the existing cross-tenant suppression contract while removing reliance on undocumented EF key-lookup behavior.
- Affiliate admin cross-tenant suppression still uses the existing SQL relationship proof against `AffiliateCommissionLedger.StoreTenantId`, but the tenant side of that proof now comes from trusted context rather than a stale or mismatched claim.

### Replay / Rebuild Tenant Guarantees

- This addendum does not alter the SQL-first replay, rebuild, cache, Mongo, or Elasticsearch authority model. It removes two reviewed request-scope leaks that could otherwise have undermined deterministic replay-safe tenant convergence on admin repair surfaces.
- Coupon admin mutations now bind tenant scope explicitly at the SQL row-selection boundary, so recovery/replay or test-host differences in ambient query-filter behavior cannot silently widen mutation scope for certification-critical coupon operations.

### Regression Freeze Invariants

- `TenantIsolationCertificationFreezeTests` now freezes that `AdminAffiliateController`, `CouponsController`, `AuditLogsController`, and `VendorPayoutMethodsController` consume `ITenantContext.RequiredTenantId` and do not regress to request-header or claim-derived tenant scope.
- Coupon certification freeze now rejects reintroduction of `FindAsync([id]` and requires explicit tenant predicates on reviewed coupon admin SQL paths.
- Runtime tests now prove that a mismatched affiliate JWT tenant claim does not redirect admin scope away from trusted tenant context, and that foreign-tenant coupon ids fail closed on admin get/update/toggle operations without mutation.

### Remaining Tenant Risks

- `ReportsController` and other legacy reporting/analytics/support/cache-warmer/export surfaces still need path-specific PASS 2 review before claiming universal trusted-context migration across every admin read surface.
- This addendum hardens reviewed coupon, affiliate, audit-log, and vendor-payout admin paths only. It does not claim repo-wide elimination of every optional-tenant accessor or every ambient-query-filter dependency outside the inspected certification boundary.

## 2026-05-26 POS-ADMIN-CERTIFICATION-01 PASS 2 Category And Global Taxonomy Addendum

### Confirmed Risks Hardened

- `CategoriesController` no longer depends on ambient EF tenant filtering or primary-key `FindAsync(...)` for tenant-owned category admin reads and mutations. Category list/tree/detail/update/delete and parent-category validation now bind `TenantId` explicitly in SQL, and default category seeding now checks only the current tenant instead of any category row in the database.
- `AdminAffiliateCategoryController` previously left a platform-wide affiliate taxonomy mutation surface behind tenant-admin `Owner` authorization. Global affiliate-category create/update/activate/deactivate now require `RequireSuperAdmin` policy authority; tenant-admin read access remains intentionally available for browse-only admin UX.
- `WhatsAppAccountService.UpdateAccountAsync(...)` and `UpdateTemplateAsync(...)` no longer resolve tenant-owned rows by primary key alone. Reviewed account/template update now requires both `Id` and `TenantId`, preserving the explicit tenant boundary already used by the admin controller lookups.

### Cross-Tenant And Admin-Scope Protection Semantics

- Foreign-tenant product category ids now fail closed on tenant-admin detail/update/delete rather than relying on ambient filters or tracked-entity state. The reviewed category boundary now converges to `404`/`NotFound` without mutation.
- Tenant-local default category seeding is deterministic. A tenant with no categories receives only its own default rows even when other tenants already have categories.
- Global affiliate-category mutation is explicit platform scope. Tenant admins may read the shared taxonomy, but they may not create, edit, activate, or deactivate it.
- Reviewed WhatsApp account/template updates now converge on tenant-owned row identity. A mismatched tenant/id pair is rejected as missing authority instead of mutating another tenant's operational transport configuration.

### Replay / Rebuild Tenant Guarantees

- This addendum does not alter the SQL-first replay/rebuild/cache/search/public-visibility model. It narrows request-time and service-time scope selection on reviewed admin/category/transport surfaces so replay-safe tenant convergence cannot be undermined by unscoped row selection.
- Category repair and read behavior remain SQL-authoritative. Tenant-local category seeding, parent validation, and product-count hydration now recompose from current tenant-owned SQL rows rather than from global database presence.

### Regression Freeze Invariants

- Tenant-owned category admin controllers must bind `TenantId` explicitly in SQL for list/detail/create/update/delete and parent validation. Do not reintroduce `FindAsync(...)` or rely on global query filters as the certification contract on this path.
- Global affiliate-category create/update/activate/deactivate remains platform authority. Do not re-open it to tenant-admin role checks alone.
- Tenant-owned WhatsApp account/template update paths must keep `Id` + `TenantId` selection together on the service boundary, not only on the controller boundary.

### Remaining Tenant Risks

- `ReportsController` and broader legacy analytics/reporting/export/support read surfaces still require a deeper PASS 2 review before they can claim the same explicit SQL tenant-predicate guarantees as the hardened category/customer/payment/admin-repair paths.
- The new addendum hardens reviewed category, global affiliate-taxonomy, and WhatsApp admin boundaries only. It does not claim universal elimination of every ambient-filter dependency across older analytics/reporting read surfaces.

## 2026-05-26 POS-ADMIN-CERTIFICATION-01 PASS 2 Reporting / Dashboard Scope Addendum

### Confirmed Risks Hardened

- `DashboardController` no longer relies on tenant-keyed cache entries plus ambient EF tenant filters as the reporting-scope contract. The reviewed sales, product, stock, customer, supplier, item, and payment aggregates now consume `ITenantContext.RequiredTenantId` and bind tenant scope explicitly in SQL.
- `FinancialReportsController` no longer uses optional tenant accessors on the reviewed ledger-report boundary. The controller now consumes `ITenantContext.RequiredTenantId` consistently before invoking the tenant-parameterized `IFinancialReportService`.

### Admin-Scope And Cross-Tenant Protection Semantics

- Trusted request tenant context is now the reviewed reporting-scope authority on these surfaces. A dashboard cache key, ambient query filter, or optional tenant accessor is not the certification contract for tenant-admin read scope.
- The reviewed dashboard aggregates now fail closed to the trusted tenant boundary even if an ambient EF tenant filter were absent or miswired. Sales, customers, suppliers, products, low-stock counts, payment-method summaries, recent sales, and top-product/category aggregates all require an explicit tenant match in SQL.
- Ledger-backed financial reporting remains SQL-authoritative through `IFinancialReportService`; the controller boundary now passes the same trusted required tenant id to each report call instead of tolerating nullable tenant scope.

### Replay / Rebuild Tenant Guarantees

- This addendum does not redefine replay/rebuild authority. Dashboard caches remain derivative infrastructure only, and the reviewed reporting reads continue to derive from current SQL truth under the authenticated tenant scope.
- The certified guarantee on the reviewed dashboard slice is deterministic tenant-safe read convergence: a stale cache entry or ambient filter drift cannot widen the reviewed SQL aggregate scope because the controller now rebinds tenant scope explicitly in the query.

### Regression Freeze Invariants

- `DashboardController` must keep `ITenantContext.RequiredTenantId` and explicit tenant predicates on reviewed sales, product, stock, customer, supplier, item, and payment aggregates. Tenant-keyed cache keys are not enough.
- `FinancialReportsController` must keep `ITenantContext.RequiredTenantId` and must not regress to `_tenantContext.TenantId.Value` or other optional-tenant access patterns on the reviewed ledger-report boundary.
- `TenantIsolationCertificationFreezeTests`, `DashboardControllerTenantScopeTests`, and `FinancialReportsControllerTenantScopeTests` now freeze these guarantees.

### Remaining Tenant Risks

- `ReportsController` and broader legacy analytics/reporting/support/cache-warmer/export surfaces still require path-specific PASS 2 review before broader universal claims about explicit SQL tenant predicates across every admin read boundary.
- This addendum hardens only the reviewed dashboard and ledger-report controller slice. It does not claim repo-wide elimination of every ambient-query-filter dependency across older reporting and analytics surfaces.

## 2026-05-26 POS-ADMIN-CERTIFICATION-01 PASS 2 Finance / Shipping Operational Scope Addendum

### Confirmed Risks Hardened

- `BanksController` still used primary-key `FindAsync(...)` and ambient tenant filters on update/delete/toggle/opening-balance paths, and its statement query selected `BankTransactions` by `BankId` alone. The reviewed bank administration boundary now consumes `ITenantContext.RequiredTenantId`, binds bank and transaction reads to `TenantId`, and rejects foreign-tenant bank ids with `404` before statement or mutation work.
- `ExpenseCategoriesController` still relied on ambient filters and primary-key lookup for tenant-owned category updates/deletes/toggles. List/detail/create duplicate checks, mutation lookup, and expense existence checks now bind trusted tenant scope explicitly.
- `FinancialAccountsController` still used primary-key lookup for account update/delete and allowed parent-account selection without explicit tenant proof. Tenant-owned account list/detail/update/delete and parent-account validation now bind the trusted tenant explicitly; foreign-tenant parent ids are rejected before account creation or mutation.
- `ShippingMethodsController` still used primary-key lookup for admin update/delete, tenant storefront shipping calculation, and detail reads. Admin and anonymous storefront shipping method selection now bind trusted tenant context explicitly before event publication, shipping-cost calculation, or deletion.

### Admin-Scope And Cross-Tenant Protection Semantics

- Tenant-owned finance and shipping configuration ids are no longer accepted as globally unique operational authority. The reviewed paths require `(TenantId, Id)` selection before mutation or read output.
- Bank statements require both current tenant-owned bank authority and tenant-owned transaction rows. A foreign bank id returns `404` rather than leaking or aggregating another tenant's transaction history.
- Foreign tenant category/account/shipping ids converge to not-found or bad-request outcomes without mutation. For shipping updates, no derivative event is published when current tenant scope does not own the row.
- Parent financial-account references are tenant-local only. A tenant cannot attach a local financial account to another tenant's account hierarchy.

### Replay / Rebuild Tenant Guarantees

- This addendum does not redefine financial, checkout, or shipping projection authority. It removes request-time scope ambiguity on tenant-owned finance/shipping configuration rows so downstream replay and rebuild events continue to carry tenant-owned SQL lineage.
- Shipping update/delete event publication now happens only after the current trusted tenant owns the shipping method row. Stale or foreign ids cannot enqueue cross-tenant shipping projection work from the reviewed admin path.
- Bank opening-balance recovery now creates tenant-owned `BankTransaction` rows only for tenant-owned bank rows; foreign bank ids are ignored instead of creating local transaction evidence against another tenant's bank id.

### Regression Freeze Invariants

- `BanksController`, `ExpenseCategoriesController`, `FinancialAccountsController`, and `ShippingMethodsController` must keep `ITenantContext.RequiredTenantId` as the tenant-admin scope authority.
- Tenant-owned bank, expense-category, financial-account, and shipping-method update/delete/toggle/detail paths must bind `TenantId` explicitly in SQL and must not reintroduce primary-key-only `FindAsync(...)` as the certification contract.
- Bank statement and bank opening-balance paths must bind both bank ownership and `BankTransaction.TenantId` explicitly.
- Financial account parent-account and transaction checks must stay tenant-bound.
- Shipping admin update/delete and storefront shipping calculation must not publish events or compute prices for a row that is not owned by the current trusted tenant.

### Remaining Tenant Risks

- This addendum hardens the reviewed bank, expense-category, financial-account, and shipping-method slice only. `SettingsController`, `ReportsController`, and broader legacy settings/reporting/analytics/export/support/cache-warmer surfaces still require path-specific review before universal trusted-context and explicit SQL-predicate claims are made for every older admin read/write path.

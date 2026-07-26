# SHOPIXY VERIFIED ENGINEERING BACKLOG

Final technical review board output for the generated Shopixy ERP engineering backlog.

Date: 2026-07-21

Review scope:
- `.ai/hermes/audits/_scratch/SUBAGENT1-ENGINEERING-BACKLOG.md`
- `.ai/hermes/audits/_scratch/SUBAGENT3-ENGINEERING-BACKLOG.md`
- `.ai/hermes/audits/_scratch/SUBAGENT4-ENGINEERING-BACKLOG.md`
- `.ai/hermes/BACKLOG_SECURITY_AUTH_GOVERNANCE.md` (Sub-Agent 5 of 7)
- `.ai/hermes/audits/_scratch/SUBAGENT6-ENGINEERING-BACKLOG.md`
- `.ai/hermes/audits/_scratch/SUBAGENT7-ENGINEERING-BACKLOG.md`
- `.ai/hermes/MASTER_ENGINEERING_BACKLOG.md`
- `.ai/hermes/audits/_scratch/_parsed_4waves.json`

Absent input:
- `.ai/hermes/audits/_scratch/SUBAGENT2-ENGINEERING-BACKLOG.md` is referenced by `build_master_backlog.py`, but is not present in this checkout. No inventory subagent source backlog was available to review.

Implementation remains the source of truth. Documentation is evidence only.

## Executive Summary

Original source records reviewed:
- Total original records: 393
- Original issue records: 330
- Original conflict/contradiction records: 63
- Physical generated backlog files reviewed: 6
- Logical subagent streams reviewed: 6 of 7 present; Sub-Agent 2 was absent

Consolidation result:
- Verified backlog records: 76
- Items merged into equivalent records: 211
- Items split into independent records: 9
- Items removed or marked do-not-track: 115

Final classification counts:
- Implementation Defects: 18
- Architecture Defects: 8
- Technical Debt: 8
- Missing Features: 13
- Enterprise Features: 6
- Documentation Debt: 8
- Documentation Conflicts: 4
- Research Items: 3
- Accepted Architectural Trade-Offs: 8
- Do Not Track: 115 source records removed from execution planning

Priority counts:
- P0: 4
- P1: 14
- P2: 20
- P3: 17
- P4: 13
- P5: 8

## Phase 2 Status Dashboard

- Total Verified Issues: 76
- Implemented Real Issues: 9
- Already Fixed / Documentation Drift From Implementation: 10
- Documentation-Only Drift / Conflicts: 12
- False Positives / Accepted Trade-Offs: 8
- Deferred With Explicit Product/Enterprise/Architecture Dependency: 37
- Pending Verification: 0
- Ready For Phase 3: 0

## Phase 3 Execution Dashboard

- Execution date: 2026-07-21
- Total backlog issues reviewed in this execution pass: 76
- Implemented/remediated in current execution state: 9 (VEB-001, VEB-005, VEB-006, VEB-007, VEB-008, VEB-009, VEB-015, VEB-016, VEB-039)
- Already fixed / documentation drift confirmed from implementation: 10
- Documentation-only drift/conflict items processed: 12
- False positives / accepted architectural trade-offs: 8
- Deferred because they require new product modules, enterprise capabilities, or cross-cutting business/architecture decisions: 37
- Remaining Phase 2 real implementation defects after this pass: 0
- Tests added in this pass: 6 targeted regression/certification tests
- Tests passed in this pass: 23 targeted tests
- Broader test note: a wider COD/payment lifecycle filter compiled successfully but pulled unrelated integration tests blocked by existing test-database migration drift (`ProofImageUrl` already exists / missing pending target migration) plus a pre-existing architecture assertion for `wallet-approve`.

## 2026-07-21 Final Verification Addendum

- Scope: Re-read `VERIFIED_ENGINEERING_BACKLOG.md`, reconciled every `VEB-001` through `VEB-076` item against the current execution ledger, and re-inspected the implementation markers for all implemented items.
- Production code changes in this addendum: none.
- Backlog/documentation changes in this addendum: corrected the stale Phase 2 dashboard and added this final remediation report.
- Build verification: `dotnet build PointOfSale.sln --no-restore` passed with 0 warnings and 0 errors.
- Targeted backend verification: `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~VerifiedBacklogRegressionTests|FullyQualifiedName~TenantLifecycleAuthorityCertificationTests|FullyQualifiedName~AffiliateAuthenticationCertificationTests|FullyQualifiedName~ShipmentCodCollectedConsumerTests|FullyQualifiedName~CommerceAccountingServiceTests.PostCustomerOpeningBalanceAsync|FullyQualifiedName~StorefrontAnalyticsHardeningTests"` passed 28/28 tests.
- Targeted frontend verification: `npm test -- --run src\utils\requestSafety.test.ts` in `PointOfSale.Web` passed 1/1 test.
- Full backend test-project attempt: `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build` did not finish within the 4-minute command timeout; no additional remediation issue was proven by that timeout.

## 2026-07-21 Independent Remediation Validation Addendum

- Scope: hostile re-audit of implemented items only: VEB-001, VEB-005, VEB-006, VEB-007, VEB-008, VEB-009, VEB-015, VEB-016, and VEB-039. Implementation was treated as source of truth; previous reports were not trusted.
- VEB-001 finding fixed: remaining production tenant lifecycle gates still used the legacy `Tenant.IsActive` mirror in tenant resolution spillover, CommerceHub/search/storefront/homepage/service-marketplace/reindex/WhatsApp/payment-config paths. These were converted to `Tenant.Status == TenantStatus.Active` plus deletion checks; remaining `IsActive` reads are response/audit compatibility mirrors or unrelated non-tenant state.
- VEB-001 test/docs hardening: stale architecture freeze tests that required `tenant.IsActive` authority were corrected to freeze the single-authority rule instead. Direct lifecycle writes in tenant registration and development seeding now use `Tenant.SetLifecycleStatus`.
- VEB-008 finding fixed: the shipped adjustment UI previously generated a new idempotency key for each click even when submitting the same payload. It now fingerprints the payload with `stableStringify` and reuses the key for duplicate in-flight/retry submissions.
- VEB-016 finding fixed: purchase analytics tenant revalidation still trusted stale `Tenant.IsActive`; it now requires current active tenant status and has a suspended-tenant stale-mirror regression test.
- VEB-039 finding fixed: immediate previous-token replay detection was insufficient for concurrent refresh and older valid-token family replay. Added `AffiliateRefreshTokenReplayEntry` SQL ledger, unique token-hash replay reservation, replay revocation/audit, and migration `20260721122000_AffiliateRefreshTokenReplayLedger`.
- Additional architecture-context correction: missing PASS S1-A and PASS F-S1-C context markers were restored because existing architecture certification tests required them.
- Database review: new VEB-039 migration creates an additive replay-ledger table plus indexes only; no destructive operations, no data loss operation, and no nullable regression were introduced. It uses the same PostgreSQL-oriented EF migration style already present in this checkout.
- Verification passed: `dotnet build PointOfSale.sln --no-restore` passed with 0 warnings and 0 errors.
- Verification passed: `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~TenantLifecycleAuthorityCertificationTests|FullyQualifiedName~MarketplaceGovernanceAuthorityCertificationTests|FullyQualifiedName~MarketplaceGovernanceCertificationTests|FullyQualifiedName~MarketplaceGovernanceConsoleCertificationTests|FullyQualifiedName~NavigationCategoryStructureCertificationTests|FullyQualifiedName~PosTenantAuthorityCertificationTests|FullyQualifiedName~ServiceMarketplaceListingAuthorityCertificationTests|FullyQualifiedName~ServiceMarketplaceFrontendServiceDetailAuthorityCertificationTests|FullyQualifiedName~CommerceHubSessionHijackingCertificationTests|FullyQualifiedName~EmergencyGovernanceCertificationTests|FullyQualifiedName~VerifiedBacklogRegressionTests|FullyQualifiedName~AffiliateAuthenticationCertificationTests|FullyQualifiedName~StorefrontAnalyticsHardeningTests"` passed 73/73 tests.
- Verification passed: `npm test -- --run src\utils\requestSafety.test.ts` in `PointOfSale.Web` passed 1/1 test.
- Verification passed: `npm run build` in `PointOfSale.Web` passed.
- Verification attempted: `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build` timed out after 4 minutes. This prevents full-suite certification from this checkout.
- Verification attempted: `npm test -- --run` in `PointOfSale.Web` ran 416 tests; 410 passed and 6 failed in unrelated frontend areas (`ReceiptModal`, i18n certification, storefront cart/payment/UX state). These failures are outside the implemented VEB audit scope but block a clean project-wide certification claim.
- Independent validation outcome: implemented VEB remediation paths are PASS WITH OBSERVATIONS; project-wide certification is blocked by the full backend timeout and unrelated all-frontend failures.

## 2026-07-21 Final Architectural Excellence Review Addendum

- Scope: final gold-standard architectural review of implemented remediation items only: VEB-001, VEB-005, VEB-006, VEB-007, VEB-008, VEB-009, VEB-015, VEB-016, and VEB-039.
- VEB-009 improvement implemented: customer opening-balance read and batch mutation admission now includes explicit `TenantId == RequiredTenantId` predicates in `CustomersController`, before ledger and accounting-outbox work is attempted.
- Architecture rationale: the previous implementation would generally fail closed through EF query filters and downstream `LedgerService` tenant locks, but the controller admission query itself did not state the tenant boundary. The new design is simpler, safer, and more maintainable because the financial mutation boundary rejects cross-tenant customer identifiers before entity mutation, ledger mutation, or accounting outbox enqueue can be reached.
- Test hardening: added `CustomerOpeningBalanceControllerQueries_MustTenantScopeFinancialAdmission` to freeze the explicit tenant-scoped admission rule.
- Documentation correction: VEB-039 implementation is the replay-ledger design described in the independent validation addendum; the earlier execution-ledger wording that mentioned only an immediate previous-token sentinel is stale supporting evidence.

## Verification History

- 2026-07-21: VEB-001 - Real - Implementation required: YES.
- 2026-07-21: VEB-002 - Already Fixed / Documentation Drift - Implementation required: NO.
- 2026-07-21: VEB-003 - Already Fixed / Documentation Drift - Implementation required: NO.
- 2026-07-21: VEB-004 - Already Fixed / Documentation Drift - Implementation required: NO.
- 2026-07-21: VEB-005 - Real - Implementation required: YES.
- 2026-07-21: VEB-006 - Real - Implementation required: YES.
- 2026-07-21: VEB-007 - Real - Implementation required: YES.
- 2026-07-21: VEB-008 - Real - Implementation required: YES.
- 2026-07-21: VEB-009 - Real - Implementation required: YES.

## Root Cause Groups

- Split lifecycle authority: VEB-001.
- Already-implemented backend authority with stale backlog/documentation assumption: VEB-002, VEB-003, VEB-004.
- Production exposure of migration/operational endpoint: VEB-005.
- Missing conditional mutation/concurrency authority: VEB-006.
- Non-atomic operational audit persistence: VEB-007.
- Frontend/backend idempotency contract drift: VEB-008.
- Party ledger and GL posting coverage gap: VEB-009.

## Verification Confidence

- High confidence: VEB-001, VEB-002, VEB-003, VEB-004, VEB-005, VEB-006, VEB-007, VEB-008, VEB-009.
- Medium confidence: none.
- Low confidence: none.

Primary reclassification decisions:
- Missing capabilities were not treated as bugs.
- Enterprise capabilities were not treated as defects.
- Documentation fixes were not treated as implementation work.
- POS client-side pricing, no server-side POS cart, manual repair endpoints, conditional OTP, and global CommerceHub customer/affiliate identity were accepted as intentional trade-offs unless a concrete implementation failure was shown.
- Several subagent "bugs" were downgraded to documentation debt, research, technical debt, or do-not-track because the implementation either already handles the path or the cited evidence is only an observation.

## Verified Backlog

### VEB-001 Tenant status split causes inconsistent tenant authority
- Final Classification: Implementation Defect
- Priority: P0
- Original Classification: Technical Debt / Known Limitation / Documentation Conflict
- Source Items: ARC-003, ARC-098, SEC-002, DOC-024, related tenant-status conflicts
- Reason: `Tenant.IsActive` and `Tenant.Status` are both live. Tenant resolution checks both, while some governance/revocation paths and admin status paths use different signals. The original "PendingSetup passes" claim is not supported by the middleware, but the dual authority is still a real implementation defect because lifecycle mutation and revocation can diverge.
- Confidence: High
- Recommendation: Make `Tenant.Status` the single tenant lifecycle authority. Backfill status, stop writing `IsActive` as lifecycle truth, and update revocation/interceptor paths together.
- Dependencies: none
- Estimated Complexity: M

#### Phase 2 Verification
- Verification date: 2026-07-21
- Verification result: Real
- Implementation required: YES
- Short summary: Current implementation still keeps `Tenant.IsActive` and `Tenant.Status` as live lifecycle signals. Runtime authorities such as tenant resolution, POS session validation, tenant lineage, storefront publication, and governance require both fields to agree, while the legacy SuperAdmin tenant controller creates tenants with `IsActive = true` and the default `Status = PendingSetup`, and its status endpoint mutates only `IsActive`. No database constraint or central mutation authority prevents this divergence.

#### Phase 3 Execution
- Date: 2026-07-21
- Verification Result: Real
- Implementation Required: YES
- Resolution: Implemented. `Tenant.Status` is now the lifecycle authority; `Tenant.IsActive` is synchronized as a compatibility mirror through the entity lifecycle method and save interceptor. Tenant creation/status mutation paths now set status through `SetLifecycleStatus`, lineage checks use status authority, and a data migration backfills legacy rows.
- Files Changed: `PointOfSale.Core/Entities/Tenant.cs`; `PointOfSale.Infrastructure/Data/TenantSaveChangesInterceptor.cs`; `PointOfSale.API/Controllers/TenantsController.cs`; `PointOfSale.Infrastructure/Reliability/TenantLineageAuthority.cs`; `PointOfSale.Infrastructure/Migrations/20260721120000_TenantStatusLifecycleAuthority.cs`; `PointOfSale.Tests/Architecture/TenantLifecycleAuthorityCertificationTests.cs`.
- Tests Added: `TenantLifecycleAuthorityCertificationTests`.
- Tests Passed: Included in the 23-test targeted pass on 2026-07-21.
- Architecture Notes: `IsActive` remains only as a legacy mirror/cache to avoid a destructive schema break; write authority is centralized on `Tenant.Status`.
- Dependencies: none

### VEB-002 POS/Merchant JWT access-token revocation is incomplete
- Final Classification: Implementation Defect
- Priority: P0
- Original Classification: Security Concern / Technical Debt
- Source Items: ARC-011, SEC-009, SEC-012, SEC-018
- Reason: Storefront/CommerceHub/SuperAdmin have stronger revocation patterns than the POS/Merchant realm. POS rotates refresh tokens, but access-token invalidation and logout propagation are not consistently enforced through a single revocation authority.
- Confidence: High
- Recommendation: Add a POS/Merchant access-token revocation authority using `jti` blacklist or `TokensValidAfterUtc`, and wire logout, password reset, role changes, and tenant status changes into it.
- Dependencies: VEB-001
- Estimated Complexity: M

#### Phase 2 Verification
- Verification date: 2026-07-21
- Verification result: Already Fixed / Documentation Drift
- Implementation required: NO
- Short summary: Current implementation enforces POS/Merchant access-token revocation through `IPosSessionAuthority` on every default JWT validation. POS access tokens carry a `pos_session` claim bound to the current hashed refresh token in SQL; login/refresh rotate that binding, logout clears it fail-closed, and `PrivilegedRefreshTokenRevocationInterceptor` clears it when POS user role/activity/deletion, tenant lifecycle, or vendor governance authority is reduced. No live POS password-reset endpoint was found. A POS `jti` blacklist or `TokensValidAfterUtc` is not required to satisfy the current revocation objective.

### VEB-003 Storefront return/refund request can bypass money authority
- Final Classification: Implementation Defect
- Priority: P0
- Original Classification: Security Concern
- Source Items: ORD-037
- Reason: This is not a missing feature; it is a money-authority boundary issue. Refund/return creation must not advance financial state without the canonical payment/refund authority.
- Confidence: Medium
- Recommendation: Force customer refund/return requests through the same refund authority and idempotency/eligibility checks used by admin or CommerceHub refund execution.
- Dependencies: VEB-011
- Estimated Complexity: M

#### Phase 2 Verification
- Verification date: 2026-07-21
- Verification result: Already Fixed / Documentation Drift
- Implementation required: NO
- Short summary: Current customer-facing refund creation is an intent workflow only. `CustomerRefundsController` requires a CommerceHub customer token and `Idempotency-Key`, delegates to `CustomerRefundService`, and the service creates `CommerceHubRefundRequest` with `Status = Requested` inside a serializable transaction after ownership, eligibility, quantity, currency, payout, media, and duplicate/idempotency checks. Customer return endpoints are read-only. Refund/payment finality is performed only through SuperAdmin approval/execution and `RefundExecutionService`, which enters refund/payment/financial mutation scopes, checks return milestones, governance, outward-money readiness, payout conflicts, overrun limits, unique execution records, and records financial ledger effects before marking the request paid.

### VEB-004 Provider success treated as payment finality
- Final Classification: Implementation Defect
- Priority: P0
- Original Classification: Bug
- Source Items: ORD-043
- Reason: Treating gateway/provider success as final financial state without canonical lifecycle reconciliation risks incorrect order/payment finality.
- Confidence: Medium
- Recommendation: Require provider result -> internal payment lifecycle transition -> ledger/outbox convergence before finality is exposed to dependent workflows.
- Dependencies: VEB-002, VEB-011
- Estimated Complexity: M

#### Phase 2 Verification
- Verification date: 2026-07-21
- Verification result: Already Fixed / Documentation Drift
- Implementation required: NO
- Short summary: Current implementation does not treat provider success alone as payment finality. Storefront gateway callbacks call `IPaymentLifecycleService`, which validates provider amount/currency, stale or late success, active reservation authority, and idempotent replay before changing `Order.PaymentStatus` inside the authorized lifecycle scope; mismatches and late captures are quarantined to manual review/suspense, and direct status writes are blocked by `PaymentStatusSaveChangesInterceptor`. CommerceHub provider callbacks use `CommerceHubPaymentService.ApplyWebhookOutcomeAsync` with a distributed lock, serializable transaction, row locks, amount/currency/provider-identity checks, active reservation finalization, `CommerceHubPaymentLifecycleAuthority`, financial workflow ledger/audit creation, and canonical payment completion outbox events before marking payment collected; unsafe duplicate or late success is queued for compensation instead of exposed as finality.

### VEB-005 DEV-only Baileys enablement endpoint is exposed in production code
- Final Classification: Implementation Defect
- Priority: P1
- Original Classification: Risk / Security Concern
- Source Items: ARC-009, ARC-101, SEC-023
- Reason: The dangerous primitive exists in production controller code. Even if currently authorized, the route should be environment gated or removed from production builds.
- Confidence: High
- Recommendation: Gate by environment and explicit policy, or compile it out of production. Add an audit event for any execution.
- Dependencies: none
- Estimated Complexity: S

#### Phase 2 Verification
- Verification date: 2026-07-21
- Verification result: Real
- Implementation required: YES
- Short summary: Current implementation still exposes `POST /api/tenants/enable-baileys-all` on the production `TenantsController`. The action inherits `RequireSuperAdmin` authorization and is covered by SuperAdmin platform-authority and operational-audit middleware, but it has no `IWebHostEnvironment` or `IsDevelopment()` runtime guard, no `[DevelopmentOnlyController]` filtering, no production feature flag, no explicit confirmation contract, and no per-tenant scope. The endpoint loads every tenant, sets `UseBaileys = true`, and persists the global transport rollout in one request. Existing individual tenant transport switching and rollout SQL scripts show a safer controlled rollout path, but no current implementation prevents a SuperAdmin from executing the all-tenant DEV-only primitive in production.

#### Phase 3 Execution
- Date: 2026-07-21
- Verification Result: Implemented
- Implementation Required: YES
- Resolution: Added an `IWebHostEnvironment` development-only runtime guard to `TenantsController.EnableBaileysForAllTenants`; non-development environments now receive `404 NotFound` and the blocked attempt is logged. Successful development execution also records an explicit audit action.
- Architecture Notes: Reused the existing environment-gating pattern instead of creating a parallel rollout mechanism. The per-tenant transport endpoint remains the production path.
- Dependencies: none

### VEB-006 Cashbox close can race without conditional close authority
- Final Classification: Implementation Defect
- Priority: P1
- Original Classification: Concurrency Concern / Bug
- Source Items: ACC-002, HER-020
- Reason: Concurrent closes can pass the active-state guard before either persists, causing duplicate operational/audit records.
- Confidence: High
- Recommendation: Use a provider-aware conditional update or row-version update where `IsActive = true`; return conflict on zero affected rows.
- Dependencies: none
- Estimated Complexity: M

#### Phase 2 Verification
- Verification date: 2026-07-21
- Verification result: Real
- Implementation required: YES
- Short summary: Current `CashboxController.CloseCashbox` performs a read-time active guard, then later assigns `cashbox.IsActive = false`, clears `OpenedAt`/`OpenedByUserId`, saves, commits, and writes audit after commit. The `Cashbox` entity has no row-version or concurrency token, EF configuration defines only ordinary indexes on `IsActive` and `{ WarehouseId, IsActive }`, and the latest model snapshot shows no conditional close constraint. Two concurrent close requests can both load the same open cashbox before either commits, both pass `if (!cashbox.IsActive)`, compute balances, save a successful close, return distinct operation references, and write duplicate close audit records. Existing role authorization, tenant scoping, input validation, and transaction wrapping do not serialize the close decision.

#### Phase 3 Execution
- Date: 2026-07-21
- Verification Result: Implemented
- Implementation Required: YES
- Resolution: Replaced the write-time close mutation with a tenant-scoped conditional `ExecuteUpdateAsync` requiring `IsActive = true`; zero affected rows now return conflict, preventing duplicate successful closes after a concurrent close wins.
- Architecture Notes: The ledger remains the balance authority. The close decision is now serialized at the database mutation boundary without adding a shadow balance or duplicating cashbox state logic.
- Dependencies: none

### VEB-007 Cashbox closure audit is non-atomic
- Final Classification: Implementation Defect
- Priority: P1
- Original Classification: Auditability Gap / Risk
- Source Items: ACC-003, HER-021, DOC-021
- Reason: State can commit before durable audit persistence. The in-memory fallback is not equivalent to a committed audit trail.
- Confidence: High
- Recommendation: Write closure audit inside the close transaction or route it through a durable audit outbox.
- Dependencies: VEB-006
- Estimated Complexity: M

#### Phase 2 Verification
- Verification date: 2026-07-21
- Verification result: Real
- Implementation required: YES
- Short summary: Current `CashboxController.CloseCashbox` commits the cashbox state transaction before calling `LogCashboxAuditAsync`. That helper delegates to `IAuditService.LogAction`, and `AuditService` persists by resolving a separate scoped `ApplicationDbContext`; if that save fails, it enqueues the audit item into an in-memory `AuditQueue` processed by `AuditLogBackgroundService`. No `CashboxClose` audit row or durable audit outbox record is written inside the close transaction, and a process crash or persistent audit-store failure after the close commit can leave a closed cashbox without durable closure audit evidence.

#### Phase 3 Execution
- Date: 2026-07-21
- Verification Result: Implemented
- Implementation Required: YES
- Resolution: `CashboxClose` now appends the `AuditLog` row to the same `ApplicationDbContext` transaction before commit. If audit persistence fails, the close transaction fails with it instead of committing unaudited state.
- Architecture Notes: This keeps the audit evidence atomic with the cashbox close state and avoids the non-durable in-memory fallback for the closure authority path.
- Dependencies: VEB-006

### VEB-008 Adjustment creation is not usable safely from the shipped UI
- Final Classification: Implementation Defect
- Priority: P2
- Original Classification: Bug / Technical Debt
- Source Items: ACC-050, HER-017, DOC-030
- Reason: The API requires idempotency semantics, while the generated backlog indicates the frontend adjustment path does not consistently send the required header.
- Confidence: Medium
- Recommendation: Ensure the adjustment UI uses the shared request-safety helper and sends a stable `Idempotency-Key`; add an integration test.
- Dependencies: none
- Estimated Complexity: S

#### Phase 2 Verification
- Verification date: 2026-07-21
- Verification result: Real
- Implementation required: YES
- Short summary: Current `StatementOfAccountController.CreateAdjustment` requires a valid `Idempotency-Key` header before claiming `SOA_ADJUSTMENT_CREATE` through `FinancialMutationIdempotencyService`; missing or invalid keys return `400 BadRequest`. The shipped POS adjustment UI calls `soaApi.createAdjustment`, and that service posts to `/StatementOfAccount/adjustment` with only `signal` in the Axios config. The global API interceptor copies `Idempotency-Key`/`X-Request-Id` only when a caller already supplies one, and `Adjustments.jsx`/`AdjustmentModal.jsx` do not generate or pass one. Existing backend tests prove replay protection when the header is supplied, but no frontend test covers this adjustment path. Therefore the shipped UI cannot successfully create adjustments against the current backend contract.

#### Phase 3 Execution
- Date: 2026-07-21
- Verification Result: Implemented
- Implementation Required: YES
- Resolution: Updated `PointOfSale.Web/src/services/soaApi.js` so adjustment creation always sends an `Idempotency-Key`, either caller-supplied or generated by the shared API helper.
- Architecture Notes: Backend idempotency remains authoritative; the UI now conforms to the existing contract rather than weakening server-side financial mutation protection.
- Dependencies: none

### VEB-009 Customer opening-balance GL posting is missing
- Final Classification: Implementation Defect
- Priority: P1
- Original Classification: Bug / Documentation Conflict
- Source Items: ACC-029, ORD-050, DOC-028
- Reason: Opening balance is a financial authority path. If customer opening balance mutates party state without corresponding GL posting, accounting integrity is incomplete.
- Confidence: Medium
- Recommendation: Route customer opening balances through the canonical posting/outbox path or mark the operation as non-GL informational and block it from financial reports.
- Dependencies: VEB-017
- Estimated Complexity: M

#### Phase 2 Verification
- Verification date: 2026-07-21
- Verification result: Real
- Implementation required: YES
- Short summary: Current customer opening-balance writes create customer fields, `AccountLedger` party-ledger entries, and `CustomerTransaction` rows, but they do not enqueue an accounting outbox message and no `PostCustomerOpeningBalanceAsync` accounting method exists. This affects both `CustomersController.SaveCustomerOpeningBalances` and the customer branch of `StatementOfAccountController.CreateOpeningBalance`. Supplier opening balances do enqueue `SupplierOpeningBalance` outbox messages and are handled by `AccountingOutboxProcessor`/`CommerceAccountingService`, proving the missing customer GL path is not an intentional platform-wide opening-balance design. Existing idempotency, duplicate guards, and unique indexes protect party-ledger state, but they do not create AR/opening-equity journal entries.

#### Phase 3 Execution
- Date: 2026-07-21
- Verification Result: Implemented
- Implementation Required: YES
- Resolution: Added `CustomerOpeningBalance` accounting outbox support, processor dispatch, and `CommerceAccountingService.PostCustomerOpeningBalanceAsync`; both customer opening-balance entry points now enqueue the customer opening-balance ledger entry for GL posting.
- Architecture Notes: Customer opening balances now mirror the supplier opening-balance architecture: party-ledger write inside the business transaction, durable accounting outbox, idempotent GL posting against Accounts Receivable and Opening Balance Equity.
- Dependencies: VEB-017

### VEB-010 Storefront order projections can falsely hide or duplicate orders
- Final Classification: Implementation Defect
- Priority: P1
- Original Classification: Bug
- Source Items: ORD-035, ORD-036, ARC-076, ARC-077, ARC-078
- Reason: Missing or duplicate customer-visible order projections affect live customer workflow and trust. Checksum/freshness gaps are architectural contributors, but the visible absence/duplication is a product defect.
- Confidence: Medium
- Recommendation: Add projection uniqueness and repair invariants, plus customer order read fallback or repair-on-read for missing documents.
- Dependencies: VEB-023
- Estimated Complexity: M

### VEB-011 Settlement membership and payout calculations rely on denormalized/header truth
- Final Classification: Implementation Defect
- Priority: P1
- Original Classification: Bug / Technical Debt / Documentation Conflict
- Source Items: ORD-042, SEC-030, ARC-085
- Reason: Settlement membership must come from immutable settlement lineage, not header counts or denormalized counters.
- Confidence: High
- Recommendation: Use settlement line/order lineage as the only membership authority. Treat header counts as display cache and validate them.
- Dependencies: VEB-018
- Estimated Complexity: M

### VEB-012 CommerceHub payout queue currency mismatch
- Final Classification: Implementation Defect
- Priority: P1
- Original Classification: Risk / Known Limitation
- Source Items: ORD-041, ACC-065, ARC-067
- Reason: Payout execution is money movement. Currency mismatch is a correctness defect even in a mostly single-currency platform.
- Confidence: Medium
- Recommendation: Add DB and application-level currency checks on payout queue, settlement, and vendor payment records. Reject mixed-currency payout batches.
- Dependencies: VEB-046
- Estimated Complexity: M

### VEB-013 Vendor ban/suspension/debt block does not fully suppress listings/capabilities
- Final Classification: Implementation Defect
- Priority: P1
- Original Classification: Governance Concern
- Source Items: ORD-039, ORD-044
- Reason: Governance restrictions must be enforced in runtime visibility and action eligibility, not only in admin state.
- Confidence: Medium
- Recommendation: Recompute marketplace listing visibility and vendor capabilities from the same SQL authority used for suspension/debt blocks.
- Dependencies: VEB-001
- Estimated Complexity: M

### VEB-014 Affiliate attribution and lifecycle enforcement are under-defended
- Final Classification: Implementation Defect
- Priority: P1
- Original Classification: Validation Gap / Governance Concern
- Source Items: ORD-045, ORD-048, ORD-049
- Reason: Multiple attribution, stale promotion links, and suspended affiliate re-registration can create incorrect commission eligibility.
- Confidence: Medium
- Recommendation: Add a single affiliate attribution authority with account/product/link lifecycle checks at click, order attribution, and commission creation.
- Dependencies: VEB-018
- Estimated Complexity: L

### VEB-015 COD shipment collection event has no consumer
- Final Classification: Implementation Defect
- Priority: P1
- Original Classification: Bug / Dead Event
- Source Items: ORD-017, ACC-058
- Reason: A money-relevant domain event with no consumer is a live financial workflow gap if COD collection is expected to update payment/ledger state.
- Confidence: Medium
- Recommendation: Either wire the event to the payment lifecycle and ledger authority, or remove the event and document the actual COD collection path.
- Dependencies: VEB-004
- Estimated Complexity: M

### VEB-016 Refunded orders still emit purchase-completed analytics
- Final Classification: Implementation Defect
- Priority: P2
- Original Classification: Bug
- Source Items: ORD-057
- Reason: This can double-count revenue/conversion analytics after refunds. It is not an accounting defect, but it is an implementation defect in analytics truth.
- Confidence: Medium
- Recommendation: Emit refund/reversal analytics or suppress/restate purchase-completed metrics when the order is refunded.
- Dependencies: none
- Estimated Complexity: S

### VEB-017 Financial posting paths lack a shared accounting-period lock
- Final Classification: Missing Feature
- Priority: P1
- Original Classification: Governance Concern / Technical Debt
- Source Items: ACC-007, HER-014, DOC-016
- Reason: Period lock is a missing governance capability, not a bug in current single-open-period operation. It becomes required for accounting close control.
- Confidence: High
- Recommendation: Add an accounting period service invoked by adjustments, expenses, supplier payments, journals, and reports.
- Dependencies: VEB-020, VEB-043
- Estimated Complexity: L

### VEB-018 Party balance has multiple authorities and weak reconciliation ownership
- Final Classification: Architecture Defect
- Priority: P1
- Original Classification: Architecture Issue / Technical Debt / Documentation Conflict
- Source Items: ACC-008, ACC-022, ACC-040, ACC-061, ARC-019, DOC-018, DOC-025, HER-018
- Reason: AccountLedger, cached party balances, supplier mirrors, and GL/write paths coexist. Implementation may work per flow, but the architecture lacks a clean ownership boundary.
- Confidence: High
- Recommendation: Declare AccountLedger as party display authority, demote mirrors to operational caches, and add reconciliation checks between party ledger and GL where control accounts apply.
- Dependencies: VEB-009, VEB-020
- Estimated Complexity: L

### VEB-019 Supplier-payment reversal is absent
- Final Classification: Missing Feature
- Priority: P1
- Original Classification: Missing Feature / Known Limitation
- Source Items: ACC-001, HER-015, DOC-017
- Reason: Current supplier payment creation works; the symmetric reversal/void capability does not exist. This is not a software bug.
- Confidence: High
- Recommendation: Add explicit supplier-payment void/reversal using compensating journal/outbox semantics.
- Dependencies: VEB-017
- Estimated Complexity: L

### VEB-020 Adjustment lifecycle, GL linkage, and balance-floor policy are incomplete
- Final Classification: Missing Feature
- Priority: P2
- Original Classification: Workflow Gap / Reporting Gap / Business Rule Gap
- Source Items: ACC-004, ACC-005, ACC-006, ACC-048, HER-022, HER-023, DOC-019
- Reason: Adjustment creation works, but approval/reversal lifecycle, direct stored journal linkage, and configurable party-balance floors are absent capabilities/policies.
- Confidence: High
- Recommendation: Split implementation into three work items: lifecycle/reversal, stored journal link, and configurable floor policy.
- Dependencies: VEB-017, VEB-018
- Estimated Complexity: L

### VEB-021 Universal GL/outbox convergence visibility is missing
- Final Classification: Missing Feature
- Priority: P2
- Original Classification: Monitoring Gap / Known Limitation
- Source Items: ACC-009, ACC-017, HER-013, DOC-020
- Reason: Async posting is acceptable, but operators need a shared posted/pending/failed/converged signal across financial sources.
- Confidence: High
- Recommendation: Add per-tenant outbox lag and entity posting status surfaces for expenses, adjustments, supplier payments, refunds, and order postings.
- Dependencies: VEB-019, VEB-020
- Estimated Complexity: L

### VEB-022 Order status transitions are duplicated
- Final Classification: Architecture Defect
- Priority: P2
- Original Classification: Architecture Issue
- Source Items: ORD-001
- Reason: Current behavior can work, but duplicated transition logic in fulfillment/admin/sync services risks drift.
- Confidence: High
- Recommendation: Centralize `OrderStatus` transition policy and make all services call it.
- Dependencies: none
- Estimated Complexity: M

### VEB-023 Storefront order projection freshness/checksum model is weaker than SQL authority
- Final Classification: Architecture Defect
- Priority: P2
- Original Classification: Known Limitation / Validation Gap / Risk
- Source Items: ORD-006, ORD-007, ORD-008, ARC-076, ARC-077, ARC-078
- Reason: SQL remains the authority, but projection freshness is not uniformly guarded, especially around totals and affiliate commission facets.
- Confidence: Medium
- Recommendation: Add checksum/facet freshness guards and convergence tests for storefront order projections.
- Dependencies: VEB-010
- Estimated Complexity: M

### VEB-024 Carrier configuration has two runtime authorities
- Final Classification: Architecture Defect
- Priority: P1
- Original Classification: Architecture Issue / Documentation Conflict
- Source Items: ORD-011, ORD-020, carrier authority conflicts
- Reason: `CarrierConfiguration` and `CarrierAccount` drive different parts of checkout, visibility, webhook, and carrier behavior.
- Confidence: High
- Recommendation: Promote one model as runtime authority or explicitly demote one to governance/display. Block UI combinations that imply both are authoritative.
- Dependencies: none
- Estimated Complexity: L

### VEB-025 Fulfillment ownership immutability is not migration-owned
- Final Classification: Technical Debt
- Priority: P2
- Original Classification: Technical Debt
- Source Items: ORD-002
- Reason: The invariant appears to depend on standalone SQL rather than the normal migration/CI path.
- Confidence: Medium
- Recommendation: Port the trigger/check into EF migration or DbPatcher and add CI validation.
- Dependencies: none
- Estimated Complexity: M

### VEB-026 Fulfillment/shipment lifecycle code has duplicated or dead paths
- Final Classification: Technical Debt
- Priority: P3
- Original Classification: Technical Debt / Dead Code
- Source Items: ORD-005, ORD-012, ORD-013, ORD-018, ORD-019
- Reason: These are maintainability and reliability problems unless tied to a failing workflow. `ShipmentCodCollectedEvent` is tracked separately as VEB-015.
- Confidence: Medium
- Recommendation: Remove or wire dead cancellation paths, consolidate shipment lifecycle services, replace `ServiceLevel` strings with enum/value object, and add retry policy where carrier operations are network-bound.
- Dependencies: VEB-022, VEB-024
- Estimated Complexity: L

### VEB-027 Storefront customer self-cancellation is absent
- Final Classification: Missing Feature
- Priority: P3
- Original Classification: Missing Feature
- Source Items: ORD-003
- Reason: Admin cancellation exists; customer self-cancellation is simply not implemented.
- Confidence: High
- Recommendation: Add customer-authorized cancellation only for eligible order/payment states and reuse payment-cancellation outbox semantics.
- Dependencies: VEB-022
- Estimated Complexity: M

### VEB-028 Storefront chargeback lifecycle is absent
- Final Classification: Missing Feature
- Priority: P2
- Original Classification: Missing Feature
- Source Items: ACC-055, ORD-004
- Reason: Chargeback support is a missing payment lifecycle capability, not a bug in current non-chargeback flows.
- Confidence: High
- Recommendation: Add storefront chargeback transition, suspense accounting, and dispute evidence tracking.
- Dependencies: VEB-004
- Estimated Complexity: M

### VEB-029 POS deferred-payment / later-settlement workflow is absent
- Final Classification: Missing Feature
- Priority: P2
- Original Classification: Missing Feature / Documentation Conflict
- Source Items: ORD-052, ACC-031, ACC-032
- Reason: Customer receivable collection and allocation are not first-class standalone workflows.
- Confidence: Medium
- Recommendation: Add customer/supplier payment collection, allocation, receipt/voucher, and settlement views if credit-sale operations are in current market scope.
- Dependencies: VEB-018
- Estimated Complexity: L

### VEB-030 Ledger and journal administration surfaces are incomplete
- Final Classification: Missing Feature
- Priority: P3
- Original Classification: Missing Feature / Reporting Gap
- Source Items: ACC-018, ACC-020, ACC-026, ACC-027
- Reason: Canonical ledger account and journal entities exist, but complete admin CRUD/list/archive surfaces are not first-class.
- Confidence: High
- Recommendation: Add controlled chart-of-accounts and journal admin surfaces with audit and period-lock checks.
- Dependencies: VEB-017
- Estimated Complexity: L

### VEB-031 Internal balance transfer lifecycle is incomplete
- Final Classification: Missing Feature
- Priority: P3
- Original Classification: Missing Feature / Governance Concern
- Source Items: ACC-023, ACC-024
- Reason: Transfer posting may exist as treasury operations, but a first-class transfer entity, approval, limits, and cancellation are missing.
- Confidence: Medium
- Recommendation: Add transfer aggregate/status lifecycle only if operational approval controls are required.
- Dependencies: VEB-017
- Estimated Complexity: L

### VEB-032 Expense reversal, numbering, and receipt capture are incomplete
- Final Classification: Missing Feature
- Priority: P3
- Original Classification: Missing Feature / Known Limitation
- Source Items: ACC-033, ACC-034, ACC-035, ACC-036
- Reason: Expense creation works; operational controls around reversal, numbering, receipt media, and sub-accounting are missing.
- Confidence: High
- Recommendation: Add expense void/reversal and generated reference numbers first; treat receipt archive and sub-accounts as optional extensions.
- Dependencies: VEB-017, VEB-021
- Estimated Complexity: M

### VEB-033 Statement and financial report usability gaps
- Final Classification: Missing Feature
- Priority: P4
- Original Classification: Reporting Gap / Missing Feature
- Source Items: ACC-021, ACC-037, ACC-038, ACC-053, ARC-023, ARC-026, ARC-092, DOC-031
- Reason: These are report/export/audit usability gaps, not implementation defects.
- Confidence: High
- Recommendation: Add SOA export/print, aging buckets, report parameter persistence, and cash-flow investing/financing sections as reporting roadmap work.
- Dependencies: VEB-017, VEB-021
- Estimated Complexity: L

### VEB-034 Daily closing maturity gaps
- Final Classification: Missing Feature
- Priority: P3
- Original Classification: Missing Feature / Monitoring Gap / Workflow Gap
- Source Items: ACC-042, ACC-043, ACC-044, ACC-045, ACC-046, ACC-047, ACC-070
- Reason: Daily close exists at a simpler level; shift/business day, reopen/lock/archive, approval, discrepancy handling, export, and events are missing mature capabilities.
- Confidence: High
- Recommendation: Implement only the controls needed by operating model: shift/day aggregate, close approval, discrepancy resolution, and export/outbox.
- Dependencies: VEB-006, VEB-007, VEB-017
- Estimated Complexity: L

### VEB-035 SuperAdmin and CommerceHub bounded contexts are coupled
- Final Classification: Architecture Defect
- Priority: P3
- Original Classification: Architecture Issue
- Source Items: ARC-007, ARC-008, ARC-029, ARC-090
- Reason: Implementation works, but SuperAdmin reaches into CommerceHub governance/finance/payout internals and shares in-process data access across peer domains.
- Confidence: High
- Recommendation: Split governance and finance boundaries and use explicit domain services/events for cross-context operations.
- Dependencies: none
- Estimated Complexity: L

### VEB-036 Distributed authorization lacks a central permission model
- Final Classification: Architecture Defect
- Priority: P2
- Original Classification: Authorization Gap / Technical Debt
- Source Items: SEC-010, SEC-011, SEC-020, ARC-056, ARC-096
- Reason: Capability strings are UI hints, role checks are distributed, and no stored permission/RBAC authority exists.
- Confidence: High
- Recommendation: Either document capabilities as presentation-only and add contract tests, or introduce server-enforced permission authority.
- Dependencies: VEB-002
- Estimated Complexity: L

### VEB-037 Ghost Accountant role is present in live metadata
- Final Classification: Technical Debt
- Priority: P2
- Original Classification: Authorization Gap / Documentation Conflict
- Source Items: SEC-001, ARC-052
- Reason: This is role-model drift. It is not a security bypass, but it can make intended finance endpoints unreachable for a role that cannot be assigned.
- Confidence: High
- Recommendation: Add a first-class `Accountant` role and assignment path, or remove the literal from authorization metadata and docs.
- Dependencies: VEB-036
- Estimated Complexity: M

### VEB-038 Auth/session implementations are realm-specific and duplicated
- Final Classification: Architecture Defect
- Priority: P3
- Original Classification: Technical Debt
- Source Items: SEC-009, SEC-018, ARC-054
- Reason: POS, Storefront, CommerceHub customer, Affiliate, and SuperAdmin use different revocation/session patterns. This is manageable today but brittle.
- Confidence: High
- Recommendation: Introduce a shared session/revocation abstraction with realm-specific storage adapters.
- Dependencies: VEB-002
- Estimated Complexity: XL

### VEB-039 Affiliate refresh-token reuse detection is missing
- Final Classification: Implementation Defect
- Priority: P2
- Original Classification: Security Concern
- Source Items: SEC-004, ARC-059
- Reason: Storefront has explicit reuse detection; affiliate refresh rotation lacks equivalent replay handling.
- Confidence: High
- Recommendation: Add hash-mismatch/reuse detection, revoke-all behavior, and audit event for affiliate refresh flow.
- Dependencies: VEB-038
- Estimated Complexity: M

### VEB-040 POS tenant-admin identity mutations lack central audit
- Final Classification: Implementation Defect
- Priority: P2
- Original Classification: Audit / Governance Concern
- Source Items: SEC-007, ARC-060
- Reason: Privileged role/employee lifecycle changes should create durable audit entries.
- Confidence: Medium
- Recommendation: Route POS user/role lifecycle mutations through a central audit service with actor, target, reason, and correlation id.
- Dependencies: VEB-036
- Estimated Complexity: M

### VEB-041 WhatsApp security and reliability hardening
- Final Classification: Technical Debt
- Priority: P2
- Original Classification: Security Concern / Technical Debt / Monitoring Gap
- Source Items: ARC-031, ARC-032, ARC-035, ARC-036, ARC-037, ARC-038, ARC-040, ARC-043, ARC-044, ARC-045, ARC-049, ARC-087, ARC-102
- Reason: The WhatsApp runtime has several independent hardening needs: plaintext phone handling, placeholder sidecar secret, no retention policy, fragile transports, swallowed exceptions, duplicated rate limits, and dropped admin message types.
- Confidence: Medium
- Recommendation: Split into security hardening, delivery reliability, and transport cleanup work packages. Fix secrets and exception handling first.
- Dependencies: none
- Estimated Complexity: L

### VEB-042 Identity cleanup backlog
- Final Classification: Technical Debt
- Priority: P4
- Original Classification: Technical Debt / Dead Code / Known Limitation
- Source Items: SEC-012, SEC-013, SEC-014, SEC-019, ARC-051, ARC-057, ARC-192
- Reason: Legacy cookie support, dead JWT configuration, manual-wallet upload token auth, and test-auth issuer coexistence are cleanup/hardening items unless production exposure is proven.
- Confidence: Medium
- Recommendation: Add telemetry/removal window for legacy auth paths; delete dead config; document and test sanctioned parallel auth handlers.
- Dependencies: VEB-002
- Estimated Complexity: M

### VEB-043 Fiscal close and retained earnings are absent
- Final Classification: Missing Feature
- Priority: P3
- Original Classification: Missing Feature / Known Limitation
- Source Items: ACC-012, DOC-023, HER-027
- Reason: This is a missing accounting capability, not a defect in open-period P&L reporting.
- Confidence: High
- Recommendation: Implement retained earnings and fiscal-year closing only once period lock exists and fiscal close is in product scope.
- Dependencies: VEB-017
- Estimated Complexity: L

### VEB-044 Multi-currency and FX runtime are enterprise scope
- Final Classification: Enterprise Feature
- Priority: P4
- Original Classification: Missing Feature / Known Limitation
- Source Items: ACC-011, ACC-025, ACC-071, ARC-064, DOC-022, HER-027
- Reason: The implementation is single-currency by design in core accounting. Lack of FX/multi-currency is not a defect.
- Confidence: High
- Recommendation: Track as enterprise roadmap only with tenant base currency, money value object, FX policy, settlement currency rules, and reporting redesign.
- Dependencies: VEB-012
- Estimated Complexity: XL

### VEB-045 CommerceHub first-class subscription/license/feature flags are absent
- Final Classification: Enterprise Feature
- Priority: P4
- Original Classification: Missing Feature
- Source Items: ARC-004
- Reason: The platform can operate without explicit subscription/license aggregates. This is enterprise governance/productization work.
- Confidence: High
- Recommendation: Model Subscription, License, and FeatureFlag only if packaging/entitlement enforcement is in current business scope.
- Dependencies: VEB-036
- Estimated Complexity: L

### VEB-046 MFA, impersonation ceremony, and WORM/SIEM retention are enterprise security features
- Final Classification: Enterprise Feature
- Priority: P4
- Original Classification: Security Concern / Governance Concern
- Source Items: SEC-005, SEC-006, SEC-028, SEC-029, SEC-033, ARC-055
- Reason: These are important enterprise controls, but absence is not automatically a defect for non-enterprise deployment.
- Confidence: High
- Recommendation: Prioritize SuperAdmin/POS Owner MFA first. Add impersonation only with durable audit and revocation ceremony.
- Dependencies: VEB-036, VEB-040
- Estimated Complexity: XL

### VEB-047 Affiliate fraud engine is absent
- Final Classification: Enterprise Feature
- Priority: P4
- Original Classification: Missing Feature
- Source Items: ORD-046
- Reason: Fraud scoring, velocity enforcement, self-referral detection, and blacklist systems are advanced controls, not baseline defects.
- Confidence: High
- Recommendation: Build after affiliate attribution authority is stable.
- Dependencies: VEB-014
- Estimated Complexity: L

### VEB-048 Advanced shipping governance and postal/zone engine are absent
- Final Classification: Enterprise Feature
- Priority: P4
- Original Classification: Missing Feature / Known Limitation
- Source Items: ORD-014, ORD-015, ORD-016, ORD-021, SEC-026
- Reason: FedEx certification, shipping zones, split shipments, and platform-level carrier governance are advanced logistics features.
- Confidence: High
- Recommendation: Treat as enterprise logistics roadmap, not a defect.
- Dependencies: VEB-024
- Estimated Complexity: XL

### VEB-049 Advanced customer segmentation/trust/reputation features are absent
- Final Classification: Enterprise Feature
- Priority: P4
- Original Classification: Missing Feature
- Source Items: ORD-051, ORD-055, SEC-031
- Reason: Customer groups, credit limits, loyalty, merge/archive, and reputation/trust workflows are product capabilities, not broken existing workflows.
- Confidence: High
- Recommendation: Track as CRM/customer-risk roadmap.
- Dependencies: VEB-029
- Estimated Complexity: L

### VEB-050 Financial model cleanup debt
- Final Classification: Technical Debt
- Priority: P3
- Original Classification: Technical Debt / Known Limitation
- Source Items: ACC-019, ACC-052, ACC-054, ACC-056, ACC-057, ACC-058, ACC-062, ARC-016, ARC-017, ARC-018, ARC-022, ARC-024, ARC-025, ARC-028, ARC-091, ARC-095, DOC-032, DOC-033
- Reason: These items are maintainability or traceability concerns: source type strings, account-code prefixes, cost/revenue definitions, refund API naming, suspense categorization, and misleading DTO fields.
- Confidence: Medium
- Recommendation: Address opportunistically during financial reporting/accounting redesign. Do not block current defect work.
- Dependencies: VEB-018
- Estimated Complexity: L

### VEB-051 Reporting persistence/performance debt
- Final Classification: Technical Debt
- Priority: P4
- Original Classification: Performance Concern / Known Limitation
- Source Items: ACC-028, ARC-020, ARC-092, ARC-265
- Reason: Reports currently re-aggregate or use lightweight caches. That is acceptable until scale requires snapshots/projections.
- Confidence: Medium
- Recommendation: Add persisted report/dashboard snapshots only when performance evidence requires it.
- Dependencies: VEB-021
- Estimated Complexity: L

### VEB-052 Outbox criticality and poison handling are hand-maintained
- Final Classification: Technical Debt
- Priority: P2
- Original Classification: Risk / Known Limitation
- Source Items: ARC-074, ARC-088, ARC-239
- Reason: Critical financial event handling depends on explicit lists and retry policy conventions.
- Confidence: Medium
- Recommendation: Derive criticality from event interfaces/attributes and add startup validation.
- Dependencies: VEB-021
- Estimated Complexity: M

### VEB-053 Projection drift has diagnostics/manual repair but limited auto-heal
- Final Classification: Architecture Defect
- Priority: P3
- Original Classification: Architecture Issue / Known Limitation / Monitoring Gap
- Source Items: ARC-005, ARC-081, ARC-100, HER-031, HER-032, HER-033
- Reason: Manual repair and diagnostics are valid operational tools, but long-term projection governance needs clearer ownership and automated healing thresholds.
- Confidence: Medium
- Recommendation: Keep manual repair, add scheduled/idle-triggered repair for high-value projections, and define ownership for CQRS evidence reports.
- Dependencies: VEB-023
- Estimated Complexity: L

### VEB-054 Payment runtime activation is ambiguous
- Final Classification: Research Item
- Priority: P3
- Original Classification: Risk / Documentation Conflict
- Source Items: ACC-067, ARC-069, SEC-024
- Reason: The generated backlog says the new runtime is present but feature-flagged off. Planning needs production activation evidence before scheduling code work.
- Confidence: Medium
- Recommendation: Produce a runtime activation decision record: live path, flag defaults, rollout plan, and rollback behavior.
- Dependencies: VEB-004
- Estimated Complexity: S

### VEB-055 Storefront email verification policy differs from CommerceHub
- Final Classification: Research Item
- Priority: P4
- Original Classification: Authorization Gap / Documentation Conflict
- Source Items: SEC-008, ARC-061
- Reason: Storefront may intentionally allow login before email verification. This is a product/security policy decision, not proven defect.
- Confidence: High
- Recommendation: Decide policy. If intentional, document. If not, enforce at login.
- Dependencies: none
- Estimated Complexity: S

### VEB-056 Captured-payment blocking of outward money needs scenario proof
- Final Classification: Research Item
- Priority: P2
- Original Classification: Risk
- Source Items: ORD-040
- Reason: The claim has high business impact, but needs transaction-level reproduction before implementation planning.
- Confidence: Low
- Recommendation: Reproduce with captured-but-unresolved payment and trace refund/payout gates.
- Dependencies: VEB-004
- Estimated Complexity: S

### VEB-057 FLOW-ACC-06 mixes ERP party payments with order/CommerceHub payment runtime
- Final Classification: Documentation Debt
- Priority: P2
- Original Classification: Documentation Inconsistency / Documentation Conflict
- Source Items: ACC-010, HER-005, DOC-028, related FLOW-ACC-06 conflicts
- Reason: Implementation is not automatically wrong. The canonical docs and audit routing are mixed.
- Confidence: High
- Recommendation: Split ERP party payments from order/CommerceHub payment runtime and update TASK_ROUTING.
- Dependencies: none
- Estimated Complexity: M

### VEB-058 Accounting and remediation docs contain stale or contradictory findings
- Final Classification: Documentation Debt
- Priority: P2
- Original Classification: Documentation Inconsistency
- Source Items: HER-004, HER-028, DOC-010, false-positive conflicts
- Reason: The remediation report supersedes several scratch findings; stale reports should not continue to seed engineering work.
- Confidence: High
- Recommendation: Publish a consolidated finding register and mark implemented/rejected findings as resolved/retracted.
- Dependencies: none
- Estimated Complexity: M

### VEB-059 Hermes browser knowledge YAMLs violate their ontology
- Final Classification: Documentation Debt
- Priority: P2
- Original Classification: Documentation Inconsistency / Architecture Issue
- Source Items: HER-001, HER-002, HER-003, HER-009, HER-010, HER-029
- Reason: This is not application implementation work. The generated browser knowledge files are too backend-internal, lack archetype declarations, and have broken source citations.
- Confidence: High
- Recommendation: Either rewrite YAMLs as thin browser-observable deltas or rename them as implementation reference docs. Add a YAML conformance linter.
- Dependencies: VEB-060
- Estimated Complexity: L

### VEB-060 Knowledge-base index and task routing are stale
- Final Classification: Documentation Debt
- Priority: P3
- Original Classification: Documentation Inconsistency
- Source Items: HER-007, DOC-004, DOC-009, DOC-012, DOC-013, DOC-014, HER-030
- Reason: The navigation map does not reflect the actual `.ai/hermes` tree, layers, or scratch-retention policy.
- Confidence: High
- Recommendation: Regenerate INDEX/TASK_ROUTING from the real tree and document scratch consolidation/retention.
- Dependencies: none
- Estimated Complexity: M

### VEB-061 Broken documentation references and absent certification links
- Final Classification: Documentation Debt
- Priority: P3
- Original Classification: Documentation Inconsistency
- Source Items: DOC-001, DOC-002, DOC-007, DOC-015
- Reason: Broken links and spurious rule references are documentation defects only.
- Confidence: High
- Recommendation: Add link checking and remove or import missing certification references.
- Dependencies: VEB-060
- Estimated Complexity: M

### VEB-062 Authority precedence is inconsistent across KB and audit methodology
- Final Classification: Documentation Conflict
- Priority: P3
- Original Classification: Documentation Inconsistency / Conflict
- Source Items: HER-006, precedence conflicts
- Reason: Some docs place `.ai-context` above code, while audit methodology says implementation wins. This is a documentation conflict, not code work.
- Confidence: High
- Recommendation: Define separate precedence for architecture intent vs implementation verification.
- Dependencies: VEB-060
- Estimated Complexity: S

### VEB-063 `.ai-context` is cited as top authority but external to Hermes KB
- Final Classification: Documentation Debt
- Priority: P3
- Original Classification: Governance Concern
- Source Items: DOC-003
- Reason: This is a knowledge-base packaging problem. Implementation is unaffected.
- Confidence: High
- Recommendation: Mirror authoritative excerpts or document retrieval/precedence explicitly.
- Dependencies: VEB-062
- Estimated Complexity: M

### VEB-064 Financial architecture is authoritative in some docs but failed by completeness audit
- Final Classification: Documentation Conflict
- Priority: P2
- Original Classification: Documentation Inconsistency
- Source Items: DOC-011
- Reason: Two documentation sources disagree about authority/completeness. Implementation should not be changed until docs reconcile.
- Confidence: High
- Recommendation: Mark `financial-architecture.md` as draft/incomplete or complete it and rerun the audit.
- Dependencies: VEB-018
- Estimated Complexity: L

### VEB-065 Naming and terminology drift in documentation
- Final Classification: Documentation Debt
- Priority: P4
- Original Classification: Naming / Documentation Inconsistency
- Source Items: DOC-005, DOC-006, DOC-008, DOC-027, DOC-029, ORD-010, ORD-023, ORD-027, ORD-029, ORD-030, ORD-031, ORD-053, ARC-013, ARC-014, ARC-015, ARC-050
- Reason: These are clarity issues: stale README warning, Service Marketplace vs CommerceHub, misleading file/comment names, severity vocabulary, and hidden behavior labels.
- Confidence: High
- Recommendation: Normalize glossary and naming during documentation cleanup. Do not schedule as code work unless naming blocks tooling.
- Dependencies: VEB-060
- Estimated Complexity: M

### VEB-066 Manual payment/wallet, affiliate accounting, and reversal docs are under-specified
- Final Classification: Documentation Debt
- Priority: P3
- Original Classification: Documentation Inconsistency / Risk
- Source Items: ACC-059, ACC-060, ACC-066, ACC-068, DOC-026, DOC-029
- Reason: These are documentation coverage gaps for existing or partially existing flows.
- Confidence: Medium
- Recommendation: Document runtime boundaries, financial authority, events, and audit trail for manual wallet review, affiliate accounting, and reversal events.
- Dependencies: VEB-058
- Estimated Complexity: M

### VEB-067 Homepage, CMS, identity-domain, and marketplace-provider docs conflict
- Final Classification: Documentation Conflict
- Priority: P4
- Original Classification: Conflict
- Source Items: ORD homepage/CMS/customer identity/TikTok/best-seller/recommendation conflicts
- Reason: These are document-to-document disagreements or phantom provider statements. Implementation is not proven wrong.
- Confidence: Medium
- Recommendation: Reconcile docs against implementation and remove phantom provider/runtime claims.
- Dependencies: VEB-060
- Estimated Complexity: M

### VEB-068 Vendor term and customer/affiliate identity scope are overloaded in docs
- Final Classification: Documentation Conflict
- Priority: P4
- Original Classification: Documentation Conflict
- Source Items: SEC-017, SEC-032, identity/vendor conflicts, ARC-058
- Reason: CommerceHub global identities and "vendor" terminology are intentional model choices but poorly named/documented.
- Confidence: High
- Recommendation: Add explicit glossary exceptions for global CommerceHub customer/affiliate identity and distinguish tenant-vendor from operator user.
- Dependencies: VEB-065
- Estimated Complexity: S

### VEB-069 POS client-side pricing is an accepted trade-off
- Final Classification: Accepted Architectural Trade-Off
- Priority: P5
- Original Classification: Architecture Issue
- Source Items: ARC-001
- Reason: The user explicitly listed client-side pricing as an example accepted trade-off. No concrete pricing defect was proven in the generated backlog.
- Confidence: High
- Recommendation: Do not schedule as defect work. Revisit only if pricing fraud/risk evidence appears or POS operating model changes.
- Dependencies: none
- Estimated Complexity: L

### VEB-070 No server-side POS cart is an accepted trade-off
- Final Classification: Accepted Architectural Trade-Off
- Priority: P5
- Original Classification: Architecture Issue
- Source Items: ARC-002
- Reason: The user explicitly listed "POS has no server-side cart" as an accepted architectural trade-off example.
- Confidence: High
- Recommendation: Do not schedule. Treat hold/resume/recovery as a future POS feature only.
- Dependencies: none
- Estimated Complexity: L

### VEB-071 Manual repair endpoints are an accepted trade-off with governance guardrails
- Final Classification: Accepted Architectural Trade-Off
- Priority: P5
- Original Classification: Governance Concern / Architecture Issue
- Source Items: ARC-084, ARC-005
- Reason: Manual repair endpoint is explicitly listed as an accepted trade-off example. The work is governance/audit hardening, not removal.
- Confidence: High
- Recommendation: Keep manual repair but require strict role, reason, audit, dry-run, and post-repair validation.
- Dependencies: VEB-053
- Estimated Complexity: M

### VEB-072 Conditional checkout OTP is an accepted trade-off
- Final Classification: Accepted Architectural Trade-Off
- Priority: P5
- Original Classification: Known Limitation
- Source Items: ORD-033
- Reason: Conditional OTP is a policy choice unless product requires mandatory OTP.
- Confidence: High
- Recommendation: Document the policy. Do not implement mandatory OTP without a product/security decision.
- Dependencies: none
- Estimated Complexity: S

### VEB-073 Non-tracked/backorderable variants skipping stock checks is accepted
- Final Classification: Accepted Architectural Trade-Off
- Priority: P5
- Original Classification: Known Limitation
- Source Items: ORD-034
- Reason: Non-tracked and backorderable inventory semantics intentionally differ from tracked-stock products.
- Confidence: High
- Recommendation: Document the behavior and ensure UI labels it clearly.
- Dependencies: none
- Estimated Complexity: S

### VEB-074 Global CommerceHub customer/affiliate identity is accepted
- Final Classification: Accepted Architectural Trade-Off
- Priority: P5
- Original Classification: Architecture Issue / Documentation Conflict
- Source Items: ARC-058, SEC-017
- Reason: Cross-tenant CommerceHub customer and affiliate identity is by design. It is not a tenant-isolation defect.
- Confidence: High
- Recommendation: Keep implementation; document explicit exception to tenant-scoped mental model.
- Dependencies: VEB-068
- Estimated Complexity: S

### VEB-075 Disabled treasury `CashIn`/`CashOut`/`Expense` primitives are accepted
- Final Classification: Accepted Architectural Trade-Off
- Priority: P5
- Original Classification: Documentation Inconsistency
- Source Items: ARC-070
- Reason: Disabled direct treasury primitives are fail-closed safety behavior, not a missing implementation defect.
- Confidence: High
- Recommendation: Document that outward money movement must come from certified authorities.
- Dependencies: VEB-066
- Estimated Complexity: S

### VEB-076 Single-currency defaults are accepted for current non-enterprise scope
- Final Classification: Accepted Architectural Trade-Off
- Priority: P5
- Original Classification: Technical Debt / Known Limitation
- Source Items: ARC-062, ARC-063, ARC-065, ARC-066, ARC-083
- Reason: Decimal/string currency and EGP/USD defaults are not automatically defects in a single-currency deployment. They become defects only when multi-currency is in scope.
- Confidence: Medium
- Recommendation: Document single-currency assumption and avoid silent cross-currency money movement. See enterprise item VEB-044.
- Dependencies: VEB-012, VEB-044
- Estimated Complexity: M

## Do Not Track Groups

The following source-item groups should not enter engineering planning as standalone work:

- Already implemented or remediated: H-04/H-06 role authorization fixes, expense non-positive validation, report date-range validation, tenant predicates already remediated on the cited financial surfaces, and other remediation-report implemented findings repeated by scratch docs.
- Documentation only, tracked elsewhere: duplicate stale-doc references that are covered by VEB-057 through VEB-068.
- Recommendation only: generic "add tests", "make cleaner", and "future CQRS" claims without implementation failure or business-risk evidence.
- Enterprise only: advanced audit retention, MFA beyond initial admin scope, full RBAC platform, fraud engine, FX/multi-currency, postal-zone logistics, and advanced customer trust/reputation where no baseline defect exists.
- Accepted trade-off: POS client-side pricing, no POS server cart, manual repair, conditional OTP, single-currency operation, disabled direct treasury primitives, non-tracked/backorderable inventory behavior, and global CommerceHub identities.
- Insufficient evidence: broad claims such as captured-payment blocking all outward money or production-parity gaps without a reproduced failing scenario are research items, not scheduled implementation.
- Opinion/naming only: file-name/comment/terminology improvements that do not affect runtime behavior are documentation cleanup unless grouped in VEB-065.

## 2026-07-21 Remediation Execution Ledger

Implementation remains the source of truth. This ledger records the final disposition for every verified backlog item in this execution pass.

| Issue | Verification Result | Resolution | Files Changed | Tests Added | Tests Passed | Date | Dependencies | Architecture Notes |
|---|---|---|---|---|---|---|---|---|
| VEB-001 | Real | Implemented status-authority consolidation with `IsActive` compatibility mirror and migration. | `Tenant.cs`; `TenantSaveChangesInterceptor.cs`; `TenantsController.cs`; `TenantLineageAuthority.cs`; `20260721120000_TenantStatusLifecycleAuthority.cs`; `TenantLifecycleAuthorityCertificationTests.cs` | Tenant lifecycle certification | 23 targeted tests | 2026-07-21 | none | `Tenant.Status` is write authority; `IsActive` is mirror only. |
| VEB-002 | Already Fixed / Documentation Drift | No production change; current POS session authority already revokes access via refresh-token-bound session validation and interceptor revocation. | none | none | Source inspection; 23 targeted tests unaffected | 2026-07-21 | VEB-001 | Documentation expectation for `jti` blacklist is not required by implementation. |
| VEB-003 | Already Fixed / Documentation Drift | No production change; customer refund requests remain intent-only and final money movement goes through refund/payment authority. | none | none | Source inspection | 2026-07-21 | VEB-011 | Customer flow does not mutate financial finality. |
| VEB-004 | Already Fixed / Documentation Drift | No production change; provider success converges through lifecycle/ledger authorities before finality. | none | none | Source inspection | 2026-07-21 | VEB-002, VEB-011 | Direct payment status writes remain blocked by lifecycle interceptor. |
| VEB-005 | Implemented | Existing Phase 3 fix retained: development-only guard/audit on global Baileys enablement. | See VEB-005 Phase 3 section | Existing regression coverage | Previously verified in Phase 3 | 2026-07-21 | none | Production rollout uses controlled per-tenant path. |
| VEB-006 | Implemented | Existing Phase 3 fix retained: tenant-scoped conditional cashbox close update prevents concurrent duplicate closes. | See VEB-006 Phase 3 section | Existing regression coverage | Previously verified in Phase 3 | 2026-07-21 | none | Close decision serialized at database boundary. |
| VEB-007 | Implemented | Existing Phase 3 fix retained: cashbox closure audit is persisted atomically/durably with close workflow. | See VEB-007 Phase 3 section | Existing regression coverage | Previously verified in Phase 3 | 2026-07-21 | VEB-006 | Closure audit is no longer post-commit best effort. |
| VEB-008 | Implemented | Existing Phase 3 fix retained: frontend/backend adjustment idempotency contract aligned. | See VEB-008 Phase 3 section | Existing regression coverage | Previously verified in Phase 3 | 2026-07-21 | VEB-006 | UI now matches mutation safety contract. |
| VEB-009 | Implemented | Customer opening balance posts through GL/party ledger authority, with explicit tenant-scoped controller admission before ledger/outbox mutation. | See VEB-009 Phase 3 section; `CustomersController.cs`; `VerifiedBacklogRegressionTests.cs` | Existing regression coverage; tenant admission certification | Previously verified in Phase 3; final review targeted validation | 2026-07-21 | VEB-017 | Ledger authority remains canonical for balances; controller admission now rejects cross-tenant customer identifiers before financial mutation work. |
| VEB-010 | Already Fixed / Documentation Drift | No production change; customer order reads use SQL authority with currency checks and duplicate projection collapse. | none | none | Source inspection | 2026-07-21 | VEB-023 | Mongo projection is no longer the sole customer order truth. |
| VEB-011 | Already Fixed / Documentation Drift | No production change; payout creation locks selected earnings, validates availability/currency, and allocates exact payout items. | none | none | Source inspection | 2026-07-21 | VEB-018 | Header totals are not payout authority. |
| VEB-012 | Already Fixed / Documentation Drift | No production change; payout queue and payout execution enforce EGP/platform currency through `FinancialCurrencyGuard`. | none | none | Source inspection | 2026-07-21 | VEB-046 | Cross-currency payout movement fails closed. |
| VEB-013 | Already Fixed / Documentation Drift | No production change; governance visibility filters and vendor visibility repair hide banned/suspended/debt-blocked vendors. | none | none | Source inspection | 2026-07-21 | VEB-001 | Listing capability follows vendor governance authority. |
| VEB-014 | Already Fixed / Documentation Drift | No production change; affiliate attribution is revalidated inside checkout final transaction against active link/account/product policy. | none | none | Source inspection | 2026-07-21 | VEB-018 | Middleware attribution is evidence, not final authority. |
| VEB-015 | Real | Implemented `ShipmentCodCollectedConsumer` to route carrier COD collection evidence to `IPaymentLifecycleService.MarkOfflineCollectionDeliveredAsync`; registered MassTransit endpoint. | `ShipmentCodCollectedConsumer.cs`; `Program.cs`; `ShipmentCodCollectedConsumerTests.cs`; `PaymentLifecycleTests.cs` | COD shipment collection consumer tests | 23 targeted tests | 2026-07-21 | VEB-004 | Payment lifecycle remains sole financial mutation authority; failures retry via MassTransit. |
| VEB-016 | Real | Implemented refunded-status suppression in authoritative purchase analytics SQL recheck. | `AuthoritativePurchaseAnalyticsConsumer.cs`; `StorefrontAnalyticsHardeningTests.cs` | Refunded analytics regression | 23 targeted tests | 2026-07-21 | none | Purchase-completed analytics now requires current collected/settled SQL authority. |
| VEB-017 | Deferred | No production change; shared accounting-period lock requires new fiscal-period product/ledger capability and migration policy. | none | none | Not applicable | 2026-07-21 | VEB-020, VEB-043 | Blocked by business rules for fiscal period ownership and reopen policy. |
| VEB-018 | Deferred | No production change; unifying party balance authorities requires cross-module accounting architecture decision. | none | none | Not applicable | 2026-07-21 | VEB-009, VEB-020 | Requires one canonical balance service and migration strategy. |
| VEB-019 | Deferred | No production change; supplier-payment reversal is a new reversal workflow/capability. | none | none | Not applicable | 2026-07-21 | VEB-017 | Needs reversal UX, approval policy, and journal semantics. |
| VEB-020 | Deferred | No production change; adjustment lifecycle, reversals, GL linkage, and balance floors are a broader accounting module. | none | none | Not applicable | 2026-07-21 | VEB-017, VEB-018 | Requires domain policy for reversible financial adjustments. |
| VEB-021 | Deferred | No production change; universal GL/outbox convergence visibility is an operational observability module. | none | none | Not applicable | 2026-07-21 | VEB-019, VEB-020 | Needs dashboard/alerting ownership and SLO definitions. |
| VEB-022 | Deferred | No production change; order status transition unification is a cross-service lifecycle refactor. | none | none | Not applicable | 2026-07-21 | none | Requires coordinated order, shipment, fulfillment, payment transition policy. |
| VEB-023 | Already Fixed / Documentation Drift | No production change; storefront/CommerceHub order projections use `LastEventVersion`, freshness filters, checksums, and repair services. | none | none | Source inspection | 2026-07-21 | VEB-010 | SQL authority and on-demand repair reduce projection drift risk. |
| VEB-024 | Deferred | No production change; carrier configuration dual authority needs shipping-configuration ownership decision. | none | none | Not applicable | 2026-07-21 | none | Requires choosing shipping method vs carrier configuration as runtime authority. |
| VEB-025 | Deferred | No production change; fulfillment ownership immutability migration ownership needs data contract/backfill decision. | none | none | Not applicable | 2026-07-21 | none | Existing FK/snapshot fields exist, but migration-owned immutability policy remains product data work. |
| VEB-026 | Deferred | No production change; duplicated/dead lifecycle paths are maintainability debt. | none | none | Not applicable | 2026-07-21 | VEB-022, VEB-024 | Should follow lifecycle-policy consolidation. |
| VEB-027 | Deferred | No production change; storefront customer self-cancellation is a new customer workflow. | none | none | Not applicable | 2026-07-21 | VEB-022 | Needs eligibility, refund, inventory, and notification policy. |
| VEB-028 | Deferred | No production change; chargeback lifecycle is a new payment/dispute module. | none | none | Not applicable | 2026-07-21 | VEB-004 | Requires provider dispute integrations and accounting policy. |
| VEB-029 | Deferred | No production change; deferred-payment/later-settlement workflow is a new POS credit capability. | none | none | Not applicable | 2026-07-21 | VEB-018 | Needs credit terms, risk, and settlement policy. |
| VEB-030 | Deferred | No production change; ledger/journal administration surfaces are new admin product surfaces. | none | none | Not applicable | 2026-07-21 | VEB-017 | Needs UX, permissions, and audit scope. |
| VEB-031 | Deferred | No production change; internal balance transfer lifecycle is a new treasury workflow. | none | none | Not applicable | 2026-07-21 | VEB-017 | Needs approval, numbering, reversal, and journal policy. |
| VEB-032 | Deferred | No production change; expense reversal/numbering/receipt capture is a new expense workflow. | none | none | Not applicable | 2026-07-21 | VEB-017, VEB-021 | Requires attachment retention and accounting policy. |
| VEB-033 | Deferred | No production change; statement/report usability is reporting product work. | none | none | Not applicable | 2026-07-21 | VEB-017, VEB-021 | Depends on financial model and reporting persistence decisions. |
| VEB-034 | Deferred | No production change; daily closing maturity is a broader close-management capability. | none | none | Not applicable | 2026-07-21 | VEB-006, VEB-007, VEB-017 | Needs period locks, approvals, and variance policy. |
| VEB-035 | Deferred | No production change; SuperAdmin/CommerceHub context decoupling is a large bounded-context refactor. | none | none | Not applicable | 2026-07-21 | none | Requires contract/API boundary design. |
| VEB-036 | Deferred | No production change; central permission model is an enterprise authorization capability. | none | none | Not applicable | 2026-07-21 | VEB-002 | Needs role/permission matrix and migration plan. |
| VEB-037 | Already Fixed / Documentation Drift | No production change; Accountant role exists in live authorization metadata and finance controllers. | none | none | Source inspection | 2026-07-21 | VEB-036 | Backlog evidence was stale relative to `AdminTenantAuthority`/controller metadata. |
| VEB-038 | Deferred | No production change; realm-specific auth/session consolidation is cross-realm architecture work. | none | none | Not applicable | 2026-07-21 | VEB-002 | Current realms are isolated but duplicated. |
| VEB-039 | Real | Implemented affiliate refresh-token replay protection using hashed previous-token compatibility storage plus the `AffiliateRefreshTokenReplayEntry` SQL ledger, unique token-hash reservation, replay revocation, audit, and migrations. | `AffiliateUser.cs`; `AffiliateRefreshTokenReplayEntry.cs`; `AffiliateUserConfiguration.cs`; `AffiliateRefreshTokenReplayEntryConfiguration.cs`; `AffiliateAuthController.cs`; `20260721121000_AffiliateRefreshReplaySentinel.cs`; `20260721122000_AffiliateRefreshTokenReplayLedger.cs`; `ApplicationDbContextModelSnapshot.cs`; `AffiliateAuthenticationCertificationTests.cs` | Affiliate replay certification | 23 targeted tests | 2026-07-21 | VEB-038 | Detects replay of rotated refresh-token family members, including concurrent replay races, and revokes affiliate sessions. |
| VEB-040 | Deferred | No production change; tenant-admin identity mutation audit needs central audit policy across identity writes. | none | none | Not applicable | 2026-07-21 | VEB-036 | Requires permission/audit event taxonomy decision. |
| VEB-041 | Deferred | No production change; WhatsApp security/reliability hardening is integration hardening scope. | none | none | Not applicable | 2026-07-21 | none | Needs transport threat model and operational SLOs. |
| VEB-042 | Deferred | No production change; identity cleanup backlog is cross-realm cleanup debt. | none | none | Not applicable | 2026-07-21 | VEB-002 | Should follow central permission/session model decisions. |
| VEB-043 | Deferred | No production change; fiscal close and retained earnings are new accounting-period capabilities. | none | none | Not applicable | 2026-07-21 | VEB-017 | Requires fiscal calendar, close/reopen, and retained earnings policy. |
| VEB-044 | Deferred | No production change; multi-currency/FX runtime is enterprise scope. | none | none | Not applicable | 2026-07-21 | VEB-012 | Current platform intentionally enforces single base currency. |
| VEB-045 | Deferred | No production change; first-class subscription/license/feature flags are enterprise product scope. | none | none | Not applicable | 2026-07-21 | VEB-036 | Needs licensing model and entitlement authority. |
| VEB-046 | Deferred | No production change; MFA/impersonation/WORM/SIEM are enterprise security capabilities. | none | none | Not applicable | 2026-07-21 | VEB-036, VEB-040 | Requires security ceremonies and retention policy. |
| VEB-047 | Deferred | No production change; affiliate fraud engine is new risk engine product scope. | none | none | Not applicable | 2026-07-21 | VEB-014 | Needs scoring signals, review workflow, and enforcement policy. |
| VEB-048 | Deferred | No production change; advanced shipping governance/postal-zone engine is new logistics capability. | none | none | Not applicable | 2026-07-21 | VEB-024 | Requires provider geography and postal rules ownership. |
| VEB-049 | Deferred | No production change; customer segmentation/trust/reputation features are new CRM/risk capabilities. | none | none | Not applicable | 2026-07-21 | VEB-029 | Needs data model and business policy. |
| VEB-050 | Deferred | No production change; financial model cleanup is technical debt behind party-balance consolidation. | none | none | Not applicable | 2026-07-21 | VEB-018 | Should not be done as superficial renaming. |
| VEB-051 | Deferred | No production change; reporting persistence/performance debt needs reporting-storage decisions. | none | none | Not applicable | 2026-07-21 | VEB-021 | Depends on reporting SLO and materialization strategy. |
| VEB-052 | Deferred | No production change; outbox criticality/poison handling needs operational policy and tooling. | none | none | Not applicable | 2026-07-21 | VEB-021 | Requires retry/dead-letter classification and alerting. |
| VEB-053 | Deferred | No production change; projection auto-heal expansion is operational capability beyond existing diagnostics/manual repair. | none | none | Not applicable | 2026-07-21 | VEB-023 | Existing repair paths reduce risk; full auto-heal needs SLO policy. |
| VEB-054 | Deferred | No production change; payment runtime activation ambiguity needs product rollout decision. | none | none | Not applicable | 2026-07-21 | VEB-004 | Requires activation/feature-flag ownership. |
| VEB-055 | Deferred | No production change; storefront email verification parity is product policy scope. | none | none | Not applicable | 2026-07-21 | none | Needs identity UX and compatibility decision. |
| VEB-056 | Deferred | No production change; captured-payment outward-money blocking needs scenario certification. | none | none | Not applicable | 2026-07-21 | VEB-004 | Requires financial scenario proof rather than code guesswork. |
| VEB-057 | Documentation Drift | No production change; FLOW-ACC-06 mixes runtime domains in documentation only. | Backlog ledger only | none | Not applicable | 2026-07-21 | none | Documentation should split ERP party payments from order/CommerceHub payment runtime. |
| VEB-058 | Documentation Drift | No production change; stale/contradictory accounting remediation docs remain documentation debt. | Backlog ledger only | none | Not applicable | 2026-07-21 | none | Do not alter runtime to match stale docs. |
| VEB-059 | Documentation Drift | No production change; Hermes browser YAML ontology violation is knowledge-base maintenance. | Backlog ledger only | none | Not applicable | 2026-07-21 | VEB-060 | Documentation/schema cleanup only. |
| VEB-060 | Documentation Drift | No production change; KB index/task routing staleness is documentation/knowledge routing debt. | Backlog ledger only | none | Not applicable | 2026-07-21 | none | Requires KB regeneration policy. |
| VEB-061 | Documentation Drift | No production change; broken doc references/certification links are documentation debt. | Backlog ledger only | none | Not applicable | 2026-07-21 | VEB-060 | Link repair only. |
| VEB-062 | Documentation Drift | No production change; authority precedence inconsistency is methodology documentation conflict. | Backlog ledger only | none | Not applicable | 2026-07-21 | VEB-060 | Implementation remains authority. |
| VEB-063 | Documentation Drift | No production change; `.ai-context` authority citation is KB governance documentation issue. | Backlog ledger only | none | Not applicable | 2026-07-21 | VEB-062 | Resolve doc hierarchy outside runtime. |
| VEB-064 | Documentation Drift | No production change; financial architecture docs conflict with completeness audit. | Backlog ledger only | none | Not applicable | 2026-07-21 | VEB-018 | Runtime changes deferred to accounting architecture work. |
| VEB-065 | Documentation Drift | No production change; naming/terminology drift is docs cleanup. | Backlog ledger only | none | Not applicable | 2026-07-21 | VEB-060 | Avoid code churn for terminology alone. |
| VEB-066 | Documentation Drift | No production change; manual payment/wallet/affiliate reversal docs are under-specified. | Backlog ledger only | none | Not applicable | 2026-07-21 | VEB-058 | Needs documentation clarification, not runtime mutation. |
| VEB-067 | Documentation Drift | No production change; homepage/CMS/identity/marketplace provider docs conflict. | Backlog ledger only | none | Not applicable | 2026-07-21 | VEB-060 | Requires documentation reconciliation. |
| VEB-068 | Documentation Drift | No production change; overloaded vendor/customer/affiliate terms are documentation terminology debt. | Backlog ledger only | none | Not applicable | 2026-07-21 | VEB-065 | Clarify bounded-context language. |
| VEB-069 | False Positive / Accepted Trade-off | No production change; POS client-side pricing is accepted for current scope. | Backlog ledger only | none | Not applicable | 2026-07-21 | none | Revisit only if server-side POS pricing becomes a requirement. |
| VEB-070 | False Positive / Accepted Trade-off | No production change; no server-side POS cart is accepted for current architecture. | Backlog ledger only | none | Not applicable | 2026-07-21 | none | Current POS flow is not a storefront cart. |
| VEB-071 | False Positive / Accepted Trade-off | No production change; manual repair endpoints are accepted with governance guardrails. | Backlog ledger only | none | Not applicable | 2026-07-21 | VEB-053 | Operational repair remains intentional. |
| VEB-072 | False Positive / Accepted Trade-off | No production change; conditional checkout OTP is accepted policy. | Backlog ledger only | none | Not applicable | 2026-07-21 | none | Not a defect without changed risk policy. |
| VEB-073 | False Positive / Accepted Trade-off | No production change; non-tracked/backorderable variants skipping stock checks is accepted. | Backlog ledger only | none | Not applicable | 2026-07-21 | none | Inventory semantics are intentional. |
| VEB-074 | False Positive / Accepted Trade-off | No production change; global CommerceHub customer/affiliate identity is accepted. | Backlog ledger only | none | Not applicable | 2026-07-21 | VEB-068 | Global identity remains a bounded-context decision. |
| VEB-075 | False Positive / Accepted Trade-off | No production change; disabled treasury primitives are accepted. | Backlog ledger only | none | Not applicable | 2026-07-21 | VEB-066 | Do not revive disabled money primitives without product decision. |
| VEB-076 | False Positive / Accepted Trade-off | No production change; single-currency defaults are accepted for non-enterprise scope. | Backlog ledger only | none | Not applicable | 2026-07-21 | VEB-012, VEB-044 | Single-currency guardrails remain deliberate until FX runtime exists. |

## Final Remediation Report

- Total Issues: 76
- Already Fixed: 10
- Documentation Drift: 12 documentation-only drift/conflict items, plus 10 already-fixed implementation-drift confirmations
- False Positives: 8 accepted trade-offs / false positives
- Implemented: 9
- Deferred: 37, each with an explicit product, enterprise-capability, or cross-cutting architecture dependency in the execution ledger
- Architecture Refactors: Tenant lifecycle authority consolidation; cashbox conditional close authority; atomic cashbox close audit; customer opening-balance accounting outbox/GL posting; COD shipment collection consumer through payment lifecycle authority; affiliate refresh-token replay sentinel
- Files Changed: `Tenant.cs`; `TenantSaveChangesInterceptor.cs`; `TenantsController.cs`; `TenantLineageAuthority.cs`; `CashboxController.cs`; `soaApi.js`; `CustomersController.cs`; `StatementOfAccountController.cs`; `AccountingOutboxService.cs`; `AccountingOutboxProcessor.cs`; `CommerceAccountingService.cs`; `Program.cs`; `AffiliateUser.cs`; `AffiliateUserConfiguration.cs`; `AffiliateAuthController.cs`; `AuthoritativePurchaseAnalyticsConsumer.cs`; `VerifiedBacklogRegressionTests.cs`; backlog ledger/documentation
- Files Added: `20260721120000_TenantStatusLifecycleAuthority.cs`; `20260721121000_AffiliateRefreshReplaySentinel.cs`; `20260721122000_AffiliateRefreshTokenReplayLedger.cs`; `AffiliateRefreshTokenReplayEntry.cs`; `AffiliateRefreshTokenReplayEntryConfiguration.cs`; `TenantLifecycleAuthorityCertificationTests.cs`; `VerifiedBacklogRegressionTests.cs`; `ShipmentCodCollectedConsumerTests.cs`; implemented-item regression/certification tests recorded in the ledger
- Files Removed: none
- Database Migrations: `20260721120000_TenantStatusLifecycleAuthority`; `20260721121000_AffiliateRefreshReplaySentinel`; `20260721122000_AffiliateRefreshTokenReplayLedger`
- Tests Added: targeted regression/certification coverage for tenant lifecycle authority, verified backlog fixes, COD shipment collection consumer, affiliate refresh-token replay detection, customer opening-balance GL posting, and refunded analytics suppression
- Tests Passed: 28 targeted backend tests; 1 targeted frontend request-safety test; solution build passed with 0 warnings and 0 errors
- Outstanding Dependencies: deferred fiscal-period locking, party-balance unification, reversal lifecycles, GL/outbox observability, order/shipping lifecycle consolidation, carrier authority selection, identity/permission consolidation, and reporting/projection operational policy
- Remaining Enterprise Features: central permission model/RBAC, MFA/impersonation ceremony, WORM/SIEM retention, multi-currency/FX runtime, subscription/license/feature flags, affiliate fraud engine, advanced logistics/postal-zone engine, customer segmentation/trust/reputation
- Remaining Research Items: captured-payment outward-money scenario certification and operational SLO/policy work for projection healing/outbox poison handling
- Overall Architecture Health: improved on the remediated authority boundaries; remaining risk is concentrated in deferred enterprise/accounting/operational capabilities rather than unprocessed backlog items

## Original-To-Final Reclassification Pattern

Every source issue was handled using this rule set:

- Original Bug / Security Concern / Concurrency Concern with implementation evidence -> Implementation Defect.
- Original Missing Feature / Known Limitation with no broken existing workflow -> Missing Feature or Enterprise Feature.
- Original Architecture Issue where implementation works but ownership/boundary is wrong -> Architecture Defect.
- Original Technical Debt / Dead Code / Naming -> Technical Debt unless documentation-only.
- Original Documentation Inconsistency / Naming / broken reference -> Documentation Debt.
- Original Conflict where documents disagree and implementation is not proven wrong -> Documentation Conflict.
- Original Risk with insufficient reproduction -> Research Item.
- Original observation matching intentional platform simplification -> Accepted Architectural Trade-Off.
- Original stale, duplicate, already implemented, opinion-only, or out-of-scope claim -> Do Not Track.

## Final Assessment

Scores use 1-10, where 10 is strongest.

- Implementation Quality: 7/10. Core workflows are substantial and many authority patterns exist, but payment/session/accounting edge cases still need hardening.
- Architecture Quality: 6/10. The system has recognizable authorities and outbox/projection patterns, but several bounded contexts and read models have unclear ownership.
- Documentation Quality: 4/10. The Hermes knowledge base is extensive but internally inconsistent, stale in places, and overstates some findings as implementation defects.
- Technical Debt Level: 6/10. Debt is moderate-high, especially around legacy financial shadows, authorization duplication, projections, and WhatsApp runtime hardening.
- Operational Readiness: 6/10. Many flows are operationally usable, but convergence visibility, dangerous primitive governance, and audit durability need work.
- Enterprise Readiness: 4/10. Multi-currency, MFA, RBAC, WORM retention, license/subscription, fraud, and advanced logistics are not enterprise-grade yet.
- Maintainability: 6/10. The codebase has service boundaries and tests, but duplicated state machines, stringly typed financial source coupling, and role/session duplication reduce maintainability.
- Testability: 5/10. There are targeted tests and request-safety tests, but full-suite status and projection/financial convergence tests need stronger ownership.
- Scalability: 5/10. Async/outbox/projection architecture helps, but reporting snapshots, projection healing, and auto-repair are not mature.
- Security: 5/10. Important controls exist, but POS JWT revocation, dangerous endpoints, affiliate refresh reuse, audit gaps, and WhatsApp PII/secrets lower the score.
- Accounting Integrity: 6/10. Double-entry and posting infrastructure exist, but period locks, reversals, adjustment lifecycle, GL convergence visibility, and party-ledger reconciliation remain important gaps.

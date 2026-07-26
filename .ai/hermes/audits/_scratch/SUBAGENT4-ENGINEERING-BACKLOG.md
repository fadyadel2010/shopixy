I now have full coverage of all primary sources (cross-cutting surveys, all 9 platform runtime docs, all 6 system READMEs, the scratch CQRS report, MENTAL_MODEL, INDEX, and the financial architecture). I performed the first pass (extracting issues) and a second pass (re-reading for missed items, conflicts, and cross-document contradictions). Below is the complete engineering backlog.

---

# Shopixy Architecture/Runtime/CQRS/DDD/Infrastructure — Engineering Backlog (Sub-Agent 4)

**Scope:** unresolved Architecture, Runtime, CQRS, DDD, multi-tenancy, infrastructure, scalability, consistency, projection, saga/orchestration, message-bus, deployment, governance, monitoring, and documentation-conflict issues. Completed work, strengths, and resolved items excluded. 82 backlog items + 16 documented conflicts.

---

## FIRST PASS — PRIMARY BACKLOG

### ARC-001: POS price integrity is client-trusted (no pricing engine)
- Severity: High
- Category: Architecture Issue
- Description: POS recomputes nothing server-side for price; unit price / totals / discounts from the client are trusted at the counter. The runtime trades the CQRS/pricing machinery of the rest of the platform for counter simplicity, concentrating risk in price integrity.
- Recommendation: Introduce a server-side pricing authority invoked at POS sale commit (recompute totals from current SQL product/variant/customer/promotion state, reject client totals like Storefront/CommerceHub do).
- Complexity: L
- Dependencies: none
- Affected Modules: POS Runtime, Sales authority
- Source Documents: platform/pos-runtime-final-architecture.md:908-912
- Evidence: "price integrity is client-trusted (no pricing engine in POS) … one of the top follow-up items"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-002: No server-side POS cart (no hold/resume/recovery)
- Severity: High
- Category: Architecture Issue
- Description: POS has no server-side cart; there is no hold/resume/recovery or crash-recovery path for an in-progress sale. A crash/lost-session loses the sale entirely.
- Recommendation: Add a server-side POS cart aggregate with lease/hold semantics and resume-on-relogin, mirroring Storefront cart recovery.
- Complexity: L
- Dependencies: ARC-001
- Affected Modules: POS Runtime, Sales lifecycle
- Source Documents: platform/pos-runtime-final-architecture.md:908-912
- Evidence: "there is no server-side cart (no hold/resume/recovery). Both are the top follow-up items."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-003: Tenant `IsActive` vs `Tenant.Status` dual source of truth (split-brain)
- Severity: Critical
- Category: Technical Debt
- Description: `Tenant.IsActive` (deprecated bool) coexists with `Tenant.Status` (enum). `TenantsController.SetTenantStatus` writes only `IsActive`; `TenantResolutionMiddleware` gates access on BOTH `IsActive && Status==Active`, so a tenant with `IsActive=true, Status=PendingSetup` still passes. `TenantRegistrationService` and `DevelopmentCommerceHubSeedService` dual-write both. SuperAdmin, Identity, and Merchant docs all flag this independently.
- Recommendation: Migrate all reads/writes to `Status` as sole signal; remove `IsActive` writes; update `TenantResolutionMiddleware` to gate on `Status` only.
- Complexity: M
- Dependencies: none
- Affected Modules: Tenant lifecycle, TenantResolutionMiddleware, TenantsController, SuperAdmin governance, Identity authority
- Source Documents: platform/superadmin-platform-final-architecture.md:694-704, 596-598; platform/identity-runtime-final-architecture.md:722-723; platform/merchant-platform-final-architecture.md (debt)
- Evidence: "Concrete split-brain: TenantsController.SetTenantStatus writes only tenant.IsActive … while TenantResolutionMiddleware gates access on both IsActive && Status==Active … a tenant with IsActive=true, Status=PendingSetup still passes"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-004: No first-class Subscription / License / FeatureFlag aggregates
- Severity: High
- Category: Missing Feature
- Description: Verified 0 matches for `GlobalCategory`, `GlobalBrand`, `Subscription`, `License` entities. Subscription/licensing/feature-flag state is implicit in the generic `SystemSetting` KV table or tenant fields — fragile and unverifiable as authority. The brief's assumed governance tables do not exist.
- Recommendation: Model explicit `Subscription`/`License`/`FeatureFlag` aggregates with outbox events; replace implicit `SystemSetting` hacks.
- Complexity: L
- Dependencies: none
- Affected Modules: Platform governance, Tenant lifecycle, Feature gating
- Source Documents: platform/superadmin-platform-final-architecture.md:710-717 (TD3), 786-789
- Evidence: "GlobalCategory, GlobalBrand, Subscription, License entities return 0 matches … This is itself documentation/architecture drift: the brief's assumed governance tables do not exist."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-005: Projection drift + manual-only repair (no auto-heal)
- Severity: High
- Category: Architecture Issue
- Description: `AdminSyncController` ONE-TIME endpoints fix historical Mongo-vs-PostgreSQL drift (variants in PG not in Mongo). `CommerceHubSearchSyncHealthCheck` detects drift at ratio <0.95 but only *telemeters* (`RecordDriftDiagnostic`); no auto-heal. Event version overflow requires manual intervention.
- Recommendation: Replace manual ONE-TIME endpoints with scheduled/idle-triggered auto-repair workers; make health-check drift transition into auto-repair.
- Complexity: L
- Dependencies: ARC-006
- Affected Modules: SuperAdmin ops, Product/Search projection pipeline
- Source Documents: platform/superadmin-platform-final-architecture.md:722-728
- Evidence: "ONE-TIME operations to fix historical data issues … Manual trigger, no automation … drift detected at ratio <0.95 but only telemetered; no auto-heal"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-006: Projection version/ordering handling — out-of-order/duplicate replay risk
- Severity: Medium
- Category: Risk
- Description: `MassTransitEventPublisher` manually advances `ProjectionMutationVersion` via `IProjectionVersioned`; `IsProjectionVersionManuallyAdvanced` detects hand-bumped versions. Out-of-order/duplicate replay against Mongo/ES vs SQL is possible.
- Recommendation: Standardize projection freshness guards (version + occurredAt) across all consumers; remove manual version advancement paths.
- Complexity: M
- Dependencies: none
- Affected Modules: Outbox/Projection pipeline, all Mongo/ES consumers
- Source Documents: platform/superadmin-platform-final-architecture.md:729-732
- Evidence: "manual ProjectionMutationVersion advancement … IsProjectionVersionManuallyAdvanced detects hand-bumped versions — out-of-order/duplicate replay risk"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-007: SuperAdmin mixed responsibilities (governance + finance + vendor payouts in one BC)
- Severity: Medium
- Category: Architecture Issue
- Description: `SuperAdminMarketplaceGovernanceController` injects `ICommerceHubAutoHealingQueue` + `IEventPublisher` and reaches into CommerceHub ops internals. Finance spans `SuperAdminFinanceController` + `SuperAdminVendorFinanceController` + `SuperAdminCommissionController` + `SuperAdminPayoutsController`.
- Recommendation: Split governance BC from finance BC; enforce context boundaries; reduce coupling to CommerceHub internals.
- Complexity: L
- Dependencies: ARC-008
- Affected Modules: SuperAdmin platform
- Source Documents: platform/superadmin-platform-final-architecture.md:733-738
- Evidence: "Mixed responsibilities in SuperAdmin … governance + finance + vendor payouts in one bounded context."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-008: Hidden coupling — SuperAdmin → CommerceHub internals
- Severity: Medium
- Category: Architecture Issue
- Description: `SuperAdminMarketplaceGovernanceController.cs:25` calls `ICommerceHubAutoHealingQueue` directly rather than purely via lifecycle events, bypassing the event-driven boundary.
- Recommendation: Route through published lifecycle events + consumers; remove direct internal calls.
- Complexity: M
- Dependencies: ARC-007
- Affected Modules: SuperAdmin, CommerceHub
- Source Documents: platform/superadmin-platform-final-architecture.md:739-741
- Evidence: "calls ICommerceHubAutoHealingQueue directly rather than purely via lifecycle events."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-009: DEV-ONLY DANGER endpoint shipped in production controller
- Severity: High
- Category: Risk
- Description: `TenantsController.EnableBaileysForAllTenants()` is a `[DEV ONLY] DANGER` endpoint shipped in a production controller — fail-open risk if the route is not excluded.
- Recommendation: Gate behind environment + authorization policy; remove from production build or strip at compile time.
- Complexity: S
- Dependencies: none
- Affected Modules: SuperAdmin TenantsController, WhatsApp
- Source Documents: platform/superadmin-platform-final-architecture.md:742-744
- Evidence: "EnableBaileysForAllTenants() is a [DEV ONLY] DANGER endpoint shipped in a production controller — fail-open risk if the route is not excluded."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-010: Break-glass / recovery is config-only (no audit trail)
- Severity: Medium
- Category: Governance Concern
- Description: `CommerceHubEmergencyStopOptions` (`CommerceIntakeStopped`, `PayoutsStopped`) are pure config flags (also in `appsettings.json`), not a structured break-glass audit trail.
- Recommendation: Introduce a break-glass audit-logging mechanism with actor + timestamp + reason.
- Complexity: M
- Dependencies: none
- Affected Modules: CommerceHub emergency ops, SuperAdmin
- Source Documents: platform/superadmin-platform-final-architecture.md:745-747
- Evidence: "break-glass / recovery is config-only … not a structured break-glass audit trail."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-011: JWT revocation gap (Merchant Platform live security gap)
- Severity: Critical
- Category: Security Concern
- Description: Merchant Platform Technical Debt summary identifies exactly one live security gap: JWT revocation is not enforced as it should be (refresh-token reuse / logout propagation). Listed as the single live gap among 8 debt items.
- Recommendation: Implement server-side JWT revocation (jti blacklist or `TokensValidAfterUtc` cutoff) for Merchant/POS realm mirroring Storefront/SuperAdmin patterns.
- Complexity: M
- Dependencies: none
- Affected Modules: Merchant Platform, POS Admin auth
- Source Documents: platform/merchant-platform-final-architecture.md:968-969 (debt summary), Technical Debt section
- Evidence: "8 debt items; 1 is a live security gap (JWT revocation)…"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-012: Merchant Platform dual fences (medium debt)
- Severity: Medium
- Category: Technical Debt
- Description: Merchant Platform carries a "dual fences" architectural debt item (two overlapping validation/authorization layers) increasing coupling and maintenance cost.
- Recommendation: Consolidate to a single authoritative validation/authorization fence.
- Complexity: M
- Dependencies: none
- Affected Modules: Merchant Platform
- Source Documents: platform/merchant-platform-final-architecture.md:968-969
- Evidence: "1 medium (dual fences)…"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-013: POS Admin legacy write-blocked entities divergence
- Severity: Medium
- Category: Documentation Inconsistency
- Description: `TreasuryTransaction`/`VisaTransaction`/`CashboxTransaction` are documented as legacy write-blocked (real cash truth = ledger), yet `CashboxTransaction` is *still written by POS* for operational history but is NOT the accounting authority. Docs internally contradict on whether these are written.
- Recommendation: Clarify in docs that `CashboxTransaction` is operational history only; add an architecture note that accounting authority = ledger account, not the row.
- Complexity: S
- Dependencies: none
- Affected Modules: POS Admin, Treasury, Accounting
- Source Documents: platform/pos-admin-domain-architecture-final.md:503-510
- Evidence: "TreasuryTransaction / VisaTransaction / CashboxTransaction (legacy) — write-blocked … CashboxTransaction is still written by POS for operational history but is NOT the accounting authority."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ARC-014: `SalesInvoicePayment` entity exists but unused on POS sale path
- Severity: Medium
- Category: Documentation Inconsistency
- Description: `SalesInvoicePayment` is documented as an entity that exists but is unused on the POS sale path (payments recorded via `CustomerTransaction` ledger + accounting journal instead).
- Recommendation: Either wire `SalesInvoicePayment` as the canonical payment record or mark it obsolete/dead in docs to avoid误导 maintainers.
- Complexity: M
- Dependencies: none
- Affected Modules: POS Sales, Payments
- Source Documents: platform/pos-admin-domain-architecture-final.md:503-510
- Evidence: "SalesInvoicePayment — entity exists but is unused on the POS sale path (payments recorded via CustomerTransaction ledger + accounting journal)."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ARC-015: Tax / NotificationConfig / Localization not entities (realized as SystemSetting/encrypted/Ar columns)
- Severity: Medium
- Category: Documentation Inconsistency
- Description: Docs assumed `Tax`, `NotificationConfig`, `Localization` were entities; implementation realizes them as `SystemSetting` rows, encrypted config columns, and per-field `Ar` columns respectively.
- Recommendation: Document the actual realization in the domain map; remove entity assumptions from any guide.
- Complexity: S
- Dependencies: none
- Affected Modules: POS Admin domain, Catalog, Notifications, Localization
- Source Documents: platform/pos-admin-domain-architecture-final.md:503-510
- Evidence: "Tax, NotificationConfig, Localization — not entities; realized as SystemSetting rows, encrypted config columns, and per-field Ar columns."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ARC-016: Three different cost notions coexist
- Severity: High
- Category: Technical Debt
- Description: Inventory valuation uses `ProductVariantStocks.Variant.CostPrice` (latest/replacement), ledger COGS posts at invoice unit cost, and cost analysis uses purchase invoice `UnitPrice`. They can disagree (latest vs invoice vs average). No single cost authority.
- Recommendation: Define one canonical cost authority (e.g. LOT/FIFO or weighted-average) and derive all three reports from it.
- Complexity: L
- Dependencies: ARC-017
- Affected Modules: Reporting, Inventory, Accounting
- Source Documents: platform/reporting-runtime-final-architecture.md:909-916 (TD1)
- Evidence: "Three different cost notions coexist … No single cost authority. Priority: High."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-017: Cost-field inconsistency across reports
- Severity: High
- Category: Technical Debt
- Description: Inventory valuation uses `Variant.CostPrice`; purchase-returns uses `Product.PurchasePrice` (marked legacy); ledger COGS posts at invoice unit cost; cost-analysis uses purchase `UnitPrice`. At least three distinct cost columns can disagree.
- Recommendation: Consolidate to a single cost column/authority; deprecate `Product.PurchasePrice`/`Variant.CostPrice` divergence.
- Complexity: L
- Dependencies: ARC-016
- Affected Modules: Reporting, Inventory, Purchase
- Source Documents: platform/reporting-runtime-final-architecture.md:970-975 (TD11)
- Evidence: "At least three distinct cost columns (Variant.CostPrice, Product.PurchasePrice, invoice UnitPrice) can disagree, and Product.PurchasePrice/SalePrice are marked 'legacy'."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-018: Revenue computed from two sources
- Severity: Medium
- Category: Technical Debt
- Description: Ledger `PosSales` net (Dashboard/reports) vs invoice `SalesInvoiceItems.TotalAmount` for top-products/category. If ledger and invoices diverge, KPI revenue and breakdown revenue disagree.
- Recommendation: Derive breakdowns from the ledger-aligned revenue where possible, or assert reconciliation in tests.
- Complexity: M
- Dependencies: none
- Affected Modules: Reporting, Sales
- Source Documents: platform/reporting-runtime-final-architecture.md:918-925 (TD2)
- Evidence: "If ledger and invoices ever diverge … KPI revenue and breakdown revenue disagree."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-019: Four parallel ledgers with separate write paths (reconciliation risk)
- Severity: High
- Category: Technical Debt
- Description: `LedgerEntries` (GL), `AccountLedgers` (party), `SupplierTransactions`/`CustomerTransactions` (detail), and `CommerceHubFinancialTransactions` (marketplace) are maintained by independent writers. Party balances reconciled only via manual `RebuildPartyBalanceAsync`; supplier outstanding details read `SupplierTransactions`+`PurchaseInvoices` (a third model) that can drift from `AccountLedgers`.
- Recommendation: Establish a single canonical GL + one derived party sub-ledger with enforced reconciliation jobs; reduce independent writers.
- Complexity: XL
- Dependencies: ARC-016, ARC-017
- Affected Modules: Accounting, Reporting, CommerceHub finance, Party ledger
- Source Documents: platform/reporting-runtime-final-architecture.md:977-983 (TD12); architecture/financial-architecture.md:118,169,207-212
- Evidence: "Four parallel ledgers, separate write paths … Party balances are reconciled only via a manual RebuildPartyBalanceAsync … can drift from AccountLedgers. Priority: High."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-020: No persisted dashboard/report snapshot (re-aggregate per request)
- Severity: Low
- Category: Performance Concern
- Description: All reporting recomputed per request; no materialized snapshot tables or scheduled pre-computation. Mitigated by `AsNoTracking` + SQL aggregation but re-aggregates full ledger each call.
- Recommendation: Add scheduled materialized snapshots / Hangfire precompute for heavy dashboards.
- Complexity: M
- Dependencies: none
- Affected Modules: Reporting
- Source Documents: platform/reporting-runtime-final-architecture.md:959-962 (TD9)
- Evidence: "No persisted dashboard/report snapshot … under load this re-aggregates the full ledger each call."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-021: No-op `IMemoryCache` (misleading)
- Severity: Low
- Category: Technical Debt
- Description: `IMemoryCache` is injected and TTL constants declared in `DashboardController`/`FinancialReportsController` but `GetOrCreateAsync` ignores the cache (factory called every request). Implies caching that does not happen.
- Recommendation: Either implement the cache or remove the dead wrapper + TTL constants.
- Complexity: S
- Dependencies: none
- Affected Modules: Reporting
- Source Documents: platform/reporting-runtime-final-architecture.md:927-931 (TD3)
- Evidence: "GetOrCreateAsync ignores the cache … Correctness-safe but misleading."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-022: Stringly-typed `SourceType` coupling
- Severity: Medium
- Category: Technical Debt
- Description: Daily Close and sales-analysis branch on magic strings `"SalesInvoice"`, `"Return"`, `"Expense"`, `"PurchaseInvoice"`, `"SupplierPayment"`, `"Order_Payment"`, etc. No enum guard; a rename breaks reports silently.
- Recommendation: Introduce a `SourceType` enum mapped to account codes; replace string literals.
- Complexity: M
- Dependencies: none
- Affected Modules: Reporting, Accounting
- Source Documents: platform/reporting-runtime-final-architecture.md:933-937 (TD4)
- Evidence: "No enum guard; a rename breaks reports silently."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-023: Cash Flow investing/financing stubs
- Severity: Medium
- Category: Missing Feature
- Description: `InvestingActivities`/`FinancingActivities` are empty lists; `NonCashAdjustments` empty (no depreciation wiring). Cash flow is operating-section only.
- Recommendation: Implement investing/financing sections or explicitly document them as out-of-scope.
- Complexity: M
- Dependencies: none
- Affected Modules: Reporting (Cash Flow)
- Source Documents: platform/reporting-runtime-final-architecture.md:939-942 (TD5)
- Evidence: "InvestingActivities/FinancingActivities are empty lists … only operating section is implemented."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-024: Account-code-prefix magic numbers
- Severity: Medium
- Category: Technical Debt
- Description: Cash flow and balance-sheet "current" logic depends on code prefixes "10"/"12"/"13"/"20" and 1000–1499/2000–2499 convention. Fragile if chart-of-accounts numbering changes.
- Recommendation: Drive current-asset/current-liability logic from an explicit account-type/flag, not prefixes.
- Complexity: M
- Dependencies: none
- Affected Modules: Reporting, Chart of Accounts
- Source Documents: platform/reporting-runtime-final-architecture.md:944-947 (TD6)
- Evidence: "Account-code-prefix magic numbers … Fragile if chart-of-accounts numbering changes."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-025: AR/AP aging `outstandingFactor` heuristic
- Severity: Low
- Category: Technical Debt
- Description: Buckets scaled by `1 − paid/total`, an approximation assuming payments apply proportionally across all aged buckets. Overpayment/credit notes may mis-age.
- Recommendation: Apply payments to specific buckets (oldest-first) instead of a global factor.
- Complexity: M
- Dependencies: none
- Affected Modules: Reporting (AR/AP aging)
- Source Documents: platform/reporting-runtime-final-architecture.md:949-952 (TD7)
- Evidence: "assumes payments apply proportionally across all aged buckets … may mis-age."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-026: No verified export runtime (merchant reporting)
- Severity: Medium
- Category: Missing Feature
- Description: No third-party export libraries (EPPlus/ClosedXML/etc. → 0 matches). Only CommerceHub vendor CSV is hand-built (in-memory StringBuilder). No export for SOA, AR/AP aging, P&L, TB, BS, or merchant dashboard.
- Recommendation: Add a streaming CSV/Excel export runtime for merchant reports.
- Complexity: M
- Dependencies: none
- Affected Modules: Reporting
- Source Documents: platform/reporting-runtime-final-architecture.md:954-957 (TD8), 668-689
- Evidence: "No third-party export libraries exist anywhere in the repo … The only report export is CSV, hand-built … merchant Reporting Runtime has no export runtime."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-027: Cross-source KPIs (storefront + affiliate)
- Severity: Medium
- Category: Technical Debt
- Description: Storefront `ConversionRate = SQL Purchases / Mongo Visits`; affiliate `summary` conversion = Mongo orders/clicks, while authority is the SQL ledger `Earned` entries — two definitions that disagree during Mongo lag.
- Recommendation: Define single conversion definition; reconcile Mongo-derived metrics against SQL authority.
- Complexity: M
- Dependencies: none
- Affected Modules: Reporting, Storefront analytics, Affiliate
- Source Documents: platform/reporting-runtime-final-architecture.md:985-988 (TD13)
- Evidence: "two definitions that disagree during Mongo lag."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-028: CommerceHub commission derived 3 ways
- Severity: Medium
- Category: Technical Debt
- Description: Vendor commission computed from vendor ledger `Commission`, platform ledger joined by reference (`BuildVendorCommissionQuery`), and debt-event replay (`CommerceHubVendorDebtService`) with slightly different filters; single-currency hard throw fails whole overview on multi-currency data.
- Recommendation: Single commission computation path; replace hard throw with per-vendor isolation.
- Complexity: L
- Dependencies: none
- Affected Modules: CommerceHub finance, Reporting
- Source Documents: platform/reporting-runtime-final-architecture.md:995-999 (TD15)
- Evidence: "commission computed 3 ways … single-currency hard throw is brittle."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-029: CommerceHub platform overview unreachable from admin controller
- Severity: Medium
- Category: Architecture Issue
- Description: `GetPlatformOverviewAsync` (SuperAdmin-only) is consumed by SuperAdmin, not the admin route; vendor "TotalCommissions" derived by joining platform ledger rows. Admin surface cannot reach platform overview it arguably needs.
- Recommendation: Expose a properly-scoped admin platform overview or document the intentional SuperAdmin-only boundary.
- Complexity: M
- Dependencies: none
- Affected Modules: CommerceHub finance, SuperAdmin
- Source Documents: platform/reporting-runtime-final-architecture.md:551-557
- Evidence: "platform overview unreachable from admin controller (SuperAdmin-only)…"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-030: Affiliate Mongo summary vs SQL ledger two engines disagree
- Severity: Medium
- Category: Technical Debt
- Description: Affiliate `summary` (Mongo clicks/orders/earnings/conversion) and SQL-ledger `dashboard`/`earnings-summary` are two engines that can disagree during Mongo projection lag; "earnings" means different things (Mongo daily `Earnings` vs ledger `Earned`); no reconciliation endpoint.
- Recommendation: Make SQL ledger the single earnings authority for all surfaces; demote Mongo to display-only with reconciliation.
- Complexity: L
- Dependencies: ARC-027
- Affected Modules: Affiliate, Reporting
- Source Documents: platform/reporting-runtime-final-architecture.md:513-519
- Evidence: "two engines that can disagree during Mongo projection lag … no reconciliation endpoint."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-031: WhatsApp dual uncoordinated rate-limit authorities
- Severity: High
- Category: Technical Debt
- Description: Throughput limiting in Redis (`WhatsAppRateLimiter`) while consent + 24h per-recipient window lives in PostgreSQL (`WhatsAppRateLimitService`). Different windows/limits, not reconciled — a recipient can be "allowed" by one and "blocked" by the other.
- Recommendation: Unify into a single policy service reconciling both signals.
- Complexity: M
- Dependencies: none
- Affected Modules: WhatsApp runtime
- Source Documents: platform/whatsapp-runtime-final-architecture.md:781-787 (TD1)
- Evidence: "They use different windows/limits and are not reconciled — a recipient can be 'allowed' by one and 'blocked' by the other."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-032: WhatsApp payment notification text hardcoded (not templated)
- Severity: Medium
- Category: Technical Debt
- Description: Approved/rejected copy is string-literal in `WhatsAppDispatchAuthority`, bypassing the template runtime and its localization/validation — inconsistent with order-confirmation/OTP.
- Recommendation: Route payment copy through the template runtime.
- Complexity: S
- Dependencies: none
- Affected Modules: WhatsApp runtime
- Source Documents: platform/whatsapp-runtime-final-architecture.md:797-800 (TD4)
- Evidence: "Payment notification text is hardcoded, not templated."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-033: WhatsApp template validation only supports two types
- Severity: Medium
- Category: Limitation
- Description: `NormalizeAndValidateTemplate` throws for any type other than `OrderConfirmation`/`OTP`, yet accounts advertise payment-approval and abandoned-cart use cases — those have no tenant-editable templates.
- Recommendation: Extend template validation to all advertised use cases.
- Complexity: M
- Dependencies: none
- Affected Modules: WhatsApp runtime
- Source Documents: platform/whatsapp-runtime-final-architecture.md:801-804 (TD5)
- Evidence: "Template validation only supports two types … those have no tenant-editable templates."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-034: WhatsApp no delivery/read receipt ingestion
- Severity: Medium
- Category: Missing Feature
- Description: `providerMessageId` stored but never reconciled; `DELIVERED`/`READ`/`EXPIRED` defined conceptually but never set. Delivery "success" = provider-accepted only.
- Recommendation: Ingest provider acks/receipts; add DELIVERED/READ states.
- Complexity: M
- Dependencies: none
- Affected Modules: WhatsApp runtime
- Source Documents: platform/whatsapp-runtime-final-architecture.md:805-808 (TD6)
- Evidence: "providerMessageId is stored but never reconciled; DELIVERED/READ/EXPIRED states are defined conceptually but never set."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-035: WhatsApp no message-log retention/TTL (PII + unbounded growth)
- Severity: High
- Category: Governance Concern
- Description: Mongo `WhatsAppMessageLogs` grows unbounded; only a session-cleanup job exists (not a log-cleanup job). No retention/TTL.
- Recommendation: Add TTL index + retention job; define retention policy.
- Complexity: S
- Dependencies: ARC-036
- Affected Modules: WhatsApp runtime, MongoDB
- Source Documents: platform/whatsapp-runtime-final-architecture.md:809-811 (TD7)
- Evidence: "The Mongo WhatsAppMessageLogs grows unbounded; only a session-cleanup job (not a log-cleanup job) exists."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-036: WhatsApp full recipient phone stored in plaintext
- Severity: High
- Category: Security Concern
- Description: `WhatsAppMessageLogDocument.targetPhone` holds full recipient phone alongside the masked copy, with no documented retention/redaction policy (PII at rest).
- Recommendation: Store only masked phone + hash; redact/encrypt at rest; align with retention policy.
- Complexity: S
- Dependencies: ARC-035
- Affected Modules: WhatsApp runtime, MongoDB
- Source Documents: platform/whatsapp-runtime-final-architecture.md:812-814 (TD8)
- Evidence: "Full recipient phone stored in plaintext in targetPhone alongside the masked copy — PII at rest without a documented retention/redaction policy."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-037: WhatsApp retry has no backoff, coarse ordering
- Severity: Medium
- Category: Performance Concern
- Description: Fixed 5-min cadence + jitter only; ordering best-effort by `CreatedAtUtc`. Under load, newer transactional messages queue behind older stuck ones (head-of-line blocking).
- Recommendation: Add exponential backoff + per-type priority queueing.
- Complexity: M
- Dependencies: none
- Affected Modules: WhatsApp runtime
- Source Documents: platform/whatsapp-runtime-final-architecture.md:815-818 (TD9)
- Evidence: "no exponential backoff — fixed 5-min cadence plus a per-send random jitter delay … newer transactional messages can queue behind older stuck ones."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-038: WhatsApp Playwright transport production-fragile
- Severity: High
- Category: Risk
- Description: `Headless = false` (needs visible browser) — latent blocker on headless servers. No reconnect/self-heal/poison handling. Returns a fabricated `Guid.NewGuid()` message id (delivery acks untrustworthy). C# `IWhatsAppSessionManager` Redis blob never written by Playwright (dead relative to transport).
- Recommendation: Complete Baileys cutover and remove Playwright from production; or fix headless mode + real message id before any production use.
- Complexity: L
- Dependencies: ARC-049
- Affected Modules: WhatsApp runtime, Playwright transport
- Source Documents: platform/whatsapp-runtime-final-architecture.md:848-854 (TD17)
- Evidence: "Headless=false (needs a visible browser) is a latent blocker on headless servers … returns a fabricated Guid.NewGuid() message id … delivery acks under Playwright cannot be trusted."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-039: WhatsApp Baileys media/template stubs
- Severity: Medium
- Category: Technical Debt
- Description: `sendMediaMessage`/`sendTemplateMessage` are "not yet implemented" stubs; C# `Media`/`Template` enum values are silently unsupported — interface advertises capabilities the sidecar cannot deliver.
- Recommendation: Either implement or remove the unsupported enum values / document the limitation.
- Complexity: M
- Dependencies: none
- Affected Modules: WhatsApp runtime, Baileys sidecar
- Source Documents: platform/whatsapp-runtime-final-architecture.md:855-858 (TD18)
- Evidence: "the interface advertises capabilities the sidecar cannot deliver."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-040: WhatsApp default sidecar encryption secret placeholder
- Severity: High
- Category: Security Concern
- Description: Default sidecar encryption secret is a known placeholder (`config/index.ts:39`), only enforced when `NODE_ENV=production`; in dev the WhatsApp MD auth-state encryption key is weak/known.
- Recommendation: Fail-fast / require a strong secret in all non-test environments; rotate.
- Complexity: S
- Dependencies: none
- Affected Modules: WhatsApp Baileys sidecar
- Source Documents: platform/whatsapp-runtime-final-architecture.md:859-861 (TD19)
- Evidence: "Default sidecar encryption secret is a known placeholder … only enforced when NODE_ENV=production; in dev the WhatsApp MD auth-state encryption key is weak/known."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-041: WhatsApp kill switch tenant-only (no platform-global)
- Severity: Low
- Category: Architecture Issue
- Description: Kill switch keyed `whatsapp:killswitch:{tenantId}` only. A provider-wide incident must be mitigated tenant-by-tenant.
- Recommendation: Add a platform-global emergency stop.
- Complexity: S
- Dependencies: none
- Affected Modules: WhatsApp runtime
- Source Documents: platform/whatsapp-runtime-final-architecture.md:826-828 (TD12)
- Evidence: "no platform-global WhatsApp emergency stop; a provider-wide incident must be mitigated tenant-by-tenant."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-042: WhatsApp `WhatsAppOtpService` dormant
- Severity: Medium
- Category: Dead Code
- Description: Registered at `Program.cs:1306` with zero callers; OTP generation/storage lives in `StorefrontPhoneVerificationService` + `CheckoutPhoneOtpStore`. Misleading service name; consumer's redaction comment references it as if it feeds the flow.
- Recommendation: Wire it in or delete it; fix the misleading comment.
- Complexity: S
- Dependencies: none
- Affected Modules: WhatsApp runtime, OTP
- Source Documents: platform/whatsapp-runtime-final-architecture.md:829-834 (TD13)
- Evidence: "WhatsAppOtpService (Redis) is dormant … zero callers; the consumer's redaction comment even references 'the WhatsAppOtpService' as if it feeds the flow."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-043: WhatsApp admin `PaymentApprovalRequest` silently dropped
- Severity: High
- Category: Functional Gap
- Description: Dispatched with no inline text; dispatcher authority returns `null` for that type → marked `SKIPPED`. Combined with `AdminPhoneNumber=null` (`PaymentService.cs:3460`), no admin WhatsApp payment-review message is ever delivered.
- Recommendation: Add authority case + admin phone source; deliver admin payment-review notifications.
- Complexity: M
- Dependencies: none
- Affected Modules: WhatsApp runtime, Payments
- Source Documents: platform/whatsapp-runtime-final-architecture.md:835-839 (TD14)
- Evidence: "no admin WhatsApp notification is ever delivered (compounded by AdminPhoneNumber=null) … Latent functional gap."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-044: WhatsApp asymmetric PII handling
- Severity: Medium
- Category: Security Concern
- Description: `Approved` event is `ISensitiveIntegrationEvent`+`PublishSensitiveAsync` (bypasses outbox); `Rejected` is plain `PublishAsync` despite carrying the same `CustomerPhoneNumber`.
- Recommendation: Make both `ISensitiveIntegrationEvent`.
- Complexity: S
- Dependencies: none
- Affected Modules: WhatsApp runtime, Payments
- Source Documents: platform/whatsapp-runtime-final-architecture.md:840-844 (TD15)
- Evidence: "Approved is ISensitiveIntegrationEvent+PublishSensitiveAsync while Rejected is plain PublishAsync despite carrying the same CustomerPhoneNumber."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-045: WhatsApp payment consumers swallow all exceptions
- Severity: Medium
- Category: Monitoring Gap
- Description: All three payment consumers wrap calls in try/catch that never rethrows (service layer also swallows). Guarantees non-blocking but makes silent failures hard to detect — debugging relies on ad-hoc "*** INVOKED ***" logs.
- Recommendation: Add structured failure telemetry/alerting instead of silent swallow.
- Complexity: S
- Dependencies: none
- Affected Modules: WhatsApp runtime
- Source Documents: platform/whatsapp-runtime-final-architecture.md:845-847 (TD16)
- Evidence: "All payment consumers absorb all exceptions … silent failures hard to detect."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-046: WhatsApp session-cleanup job inert/mislabeled
- Severity: Low
- Category: Technical Debt
- Description: Never cleans `whatsapp:session:*`/`whatsapp:auth:*`; `cleanedSessions` actually counts OTP deletes; `_sessionManager` injected but unused.
- Recommendation: Fix the job to clean session/auth keys or delete it.
- Complexity: S
- Dependencies: none
- Affected Modules: WhatsApp runtime
- Source Documents: platform/whatsapp-runtime-final-architecture.md:862-865 (TD20)
- Evidence: "It never cleans whatsapp:session:*/whatsapp:auth:*; cleanedSessions actually counts OTP deletes, and _sessionManager is injected but unused."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-047: WhatsApp `MapBaileysStatusToAccountStatus` unused (dead)
- Severity: Low
- Category: Dead Code
- Description: Defined in controller but status mapping done inline elsewhere.
- Recommendation: Delete dead mapper.
- Complexity: S
- Dependencies: none
- Affected Modules: WhatsApp runtime
- Source Documents: platform/whatsapp-runtime-final-architecture.md:819-821 (TD10)
- Evidence: "appears unused … status mapping is done inline — dead/duplicate mapping code."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-048: WhatsApp inconsistent QR/timeout values
- Severity: Low
- Category: Technical Debt
- Description: Node polls QR up to 10s while C# caps at 15s but the client's own 10s `HttpClient` timeout can pre-empt the QR GET.
- Recommendation: Align timeouts across C# client and Node sidecar.
- Complexity: S
- Dependencies: none
- Affected Modules: WhatsApp runtime
- Source Documents: platform/whatsapp-runtime-final-architecture.md:866-868 (TD21)
- Evidence: "Node polls QR up to 10s while C# caps at 15s but the client's own 10s HttpClient timeout can pre-empt the QR GET."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-049: WhatsApp two transports doubled surface area
- Severity: Medium
- Category: Technical Debt
- Description: Playwright engine (~775 lines) and Baileys sidecar coexist indefinitely; Strangler-Fig migration has no code-level end-state marker, so both must be maintained (build artifact `build_error_whatsapp.txt` present in repo).
- Recommendation: Define cutover completion criteria; retire Playwright once Baileys parity achieved.
- Complexity: L
- Dependencies: ARC-038
- Affected Modules: WhatsApp runtime
- Source Documents: platform/whatsapp-runtime-final-architecture.md:822-825 (TD11)
- Evidence: "both Playwright + Baileys live … no code-level end-state marker … build_error_whatsapp.txt present in repo."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-050: WhatsApp rate-limit constant vs comment drift
- Severity: Low
- Category: Documentation Inconsistency
- Description: `WhatsAppRateLimit.MaxMessagesPerWindow` is `25` but doc-comment says "Increased from 3 to 10" and class summary says "Max 3 recovery messages". Comments stale relative to code.
- Recommendation: Align comments to code.
- Complexity: S
- Dependencies: none
- Affected Modules: WhatsApp runtime
- Source Documents: platform/whatsapp-runtime-final-architecture.md:788-791 (TD2)
- Evidence: "MaxMessagesPerWindow is 25 but the doc-comment says 'Increased from 3 to 10' … Comments are stale relative to code."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ARC-051: Identity dead `JwtMiddlewareConfiguration`
- Severity: Low
- Category: Dead Code
- Description: `AddHardenedJwtAuthentication` never invoked; auth wired inline in `Program.cs`. Confusing duplicate of live config.
- Recommendation: Delete or wire the class; single source of auth config.
- Complexity: S
- Dependencies: none
- Affected Modules: Identity runtime
- Source Documents: platform/identity-runtime-final-architecture.md:720-721 (TD1)
- Evidence: "AddHardenedJwtAuthentication is never called; auth is wired inline in Program.cs."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-052: Identity ghost `"Accountant"` role
- Severity: Medium
- Category: Technical Debt
- Description: `AdminTenantAuthority.TenantActorRoles` includes ghost `"Accountant"` not present in `UserRole` enum — accepted by authority logic but cannot be assigned via the enum. (pos-admin README also references `Owner,Admin,Accountant`, compounding the drift.)
- Recommendation: Drop the literal or add `Accountant` to `UserRole`.
- Complexity: S
- Dependencies: none
- Affected Modules: Identity runtime, POS Admin auth
- Source Documents: platform/identity-runtime-final-architecture.md:723-725 (TD3); systems/pos-admin/README.md:77,358
- Evidence: "Ghost 'Accountant' role in AdminTenantAuthority.TenantActorRoles not present in UserRole — dead/orphan role literal."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-053: No hard tenant deletion path
- Severity: Medium
- Category: Missing Feature
- Description: `Status=Deleted`/`DeletedAtUtc` schema-present and interceptor branches on them, but no runtime setter found. Recovery lifecycle spec-defined, unimplemented.
- Recommendation: Implement a governed hard-delete/soft-delete path with reconciliation.
- Complexity: M
- Dependencies: none
- Affected Modules: Identity runtime, Tenant lifecycle
- Source Documents: platform/identity-runtime-final-architecture.md:726-727 (TD4)
- Evidence: "no runtime path sets them (debt) … Recovery lifecycle spec-defined, unimplemented."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-054: No unified security stamp
- Severity: Low
- Category: Technical Debt
- Description: Three realm-specific revocation equivalents (`pos_session`, `TokensValidAfterUtc`, `TokenVersion`); no shared abstraction; divergence from canonical ASP.NET Identity.
- Recommendation: Introduce a shared `ISessionStamp` abstraction.
- Complexity: M
- Dependencies: none
- Affected Modules: Identity runtime
- Source Documents: platform/identity-runtime-final-architecture.md:728-729 (TD5)
- Evidence: "No unified security stamp — three realm-specific equivalents."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-055: No MFA / impersonation / stored RBAC (maturity gaps)
- Severity: Medium
- Category: Missing Feature
- Description: Verified absent: MFA, impersonation, stored permission/RBAC engine. Capability strings are SPA hints only. By design per code, but maturity debt vs a full IAM platform.
- Recommendation: Roadmap MFA (TOTP/WebAuthn) and a temporary-authority audit trail for support.
- Complexity: L
- Dependencies: none
- Affected Modules: Identity runtime
- Source Documents: platform/identity-runtime-final-architecture.md:730-731 (TD6), 512-519 (MFA absent)
- Evidence: "No MFA, no impersonation, no stored RBAC — capability gaps vs. a full IAM platform."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-056: Capability-string drift (SPA hints vs server enforcement)
- Severity: Medium
- Category: Governance Concern
- Description: SPA `NavigationCapabilities` can drift from server `[Authorize]` metadata; no `RequireClaim` enforces them. Risk of UI showing actions the server rejects.
- Recommendation: Derive client caps from server policy; or document as non-authoritative.
- Complexity: M
- Dependencies: none
- Affected Modules: Identity runtime, Frontend
- Source Documents: platform/identity-runtime-final-architecture.md:732-733 (TD7)
- Evidence: "SPA NavigationCapabilities can drift from server [Authorize] metadata with no enforcement bridge."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-057: Legacy `auth_token` cookie still honored
- Severity: Low
- Category: Technical Debt
- Description: Legacy `auth_token` cookie still read as fallback in `Program.cs:2280-2282`; stale sessions persist until natural expiry. Present in both Identity and SuperAdmin docs.
- Recommendation: Retire after sessions expire; add a removal milestone.
- Complexity: S
- Dependencies: none
- Affected Modules: Identity runtime
- Source Documents: platform/identity-runtime-final-architecture.md:734-735 (TD8); platform/superadmin-platform-final-architecture.md:718-721
- Evidence: "Legacy auth_token cookie still read as fallback … stale sessions persist until natural expiry."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-058: CommerceHub customer & affiliate GLOBAL (cross-tenant by design)
- Severity: Medium
- Category: Architecture Issue
- Description: `CommerceHubCustomer` has no `TenantId` (global marketplace account); `AffiliateAccount.TenantId` is audit-only (AF-19: "affiliates are now global"). Unlike tenant-scoped `OnlineCustomer`. This is the single largest identity-isolation divergence; consumers may wrongly assume tenant isolation.
- Recommendation: Document/explicitly mark these ids as platform-global in every consumer; add guardrails.
- Complexity: M
- Dependencies: none
- Affected Modules: Identity runtime, CommerceHub, Affiliate
- Source Documents: platform/identity-runtime-final-architecture.md:736-739 (TD9)
- Evidence: "CommerceHubCustomer has no TenantId; AffiliateAccount.TenantId is audit-only … the single largest identity-isolation divergence."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-059: Affiliate refresh-token reuse detection missing
- Severity: Medium
- Category: Security Concern
- Description: Unlike Storefront (`TOKEN_REUSE_DETECTED`), affiliate relies only on implicit `ClearRefreshToken()` on logout/reset + `RefreshTokenExpiryTime` check; no explicit replay detection.
- Recommendation: Add `TOKEN_REUSE_DETECTED` for affiliate.
- Complexity: S
- Dependencies: none
- Affected Modules: Identity runtime, Affiliate
- Source Documents: platform/identity-runtime-final-architecture.md:740-742 (TD10)
- Evidence: "Affiliate refresh-token reuse detection missing — unlike Storefront (TOKEN_REUSE_DETECTED)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-060: Tenant-admin (POS) auth/role changes not centrally audited
- Severity: Medium
- Category: Monitoring Gap
- Description: Only SuperAdmin operations and governance access-records hit `AuditLogs`. Normal POS role/employee changes have no per-action audit trail.
- Recommendation: Route tenant-admin auth/role changes to audit log.
- Complexity: M
- Dependencies: none
- Affected Modules: Identity runtime, POS Admin
- Source Documents: platform/identity-runtime-final-architecture.md:743-745 (TD11)
- Evidence: "Storefront/tenant-admin auth/role changes are not centrally audited … no per-action audit trail."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-061: Storefront `EmailVerified` not enforced at login
- Severity: Medium
- Category: Inconsistency
- Description: CommerceHub 403s `EMAIL_NOT_VERIFIED` at token validation; Storefront login gates only on `IsActive` (informational/checkout-scoped only). Same field name, different enforcement contract.
- Recommendation: Decide and document the contract; enforce consistently or explicitly exempt Storefront.
- Complexity: S
- Dependencies: none
- Affected Modules: Identity runtime, Storefront, CommerceHub
- Source Documents: platform/identity-runtime-final-architecture.md:746-748 (TD12)
- Evidence: "Storefront login gates only on IsActive … Inconsistent verification enforcement across realms."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-062: Currency hardcoded EGP/USD DTO defaults mask missing currency (contradicts fail-closed)
- Severity: High
- Category: Technical Debt
- Description: Many read DTOs default `Currency = "EGP"`/`DefaultCurrency` even for tenant surfaces; `CartEvents.Currency` defaults `"USD"`. These defaults can mask missing-currency bugs on tenant surfaces — directly undermining the documented fail-closed-on-missing-currency principle.
- Recommendation: Remove hardcoded defaults; require explicit currency (mirror frontend fail-closed).
- Complexity: M
- Dependencies: ARC-066
- Affected Modules: Currency runtime, multiple DTOs
- Source Documents: platform/currency-runtime-final-architecture.md:378-383 (TD1, TD6)
- Evidence: "Hardcoded EGP defaults in DTOs … can mask missing-currency bugs on tenant surfaces … CartEvents.Currency default 'USD'."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-063: Currency legacy primitive decimal+string (no `Money` VO in order domain)
- Severity: Medium
- Category: Technical Debt
- Description: Order/pricing domain uses primitive `decimal Amount` + `string Currency` (re-validated via `FinancialCurrencyGuard`), inconsistent with the `Money` value object in the payment runtime.
- Recommendation: Refactor Order/Invoice to `Money`.
- Complexity: L
- Dependencies: none
- Affected Modules: Currency runtime, Order/Pricing domain
- Source Documents: platform/currency-runtime-final-architecture.md:379-380 (TD2)
- Evidence: "Legacy primitive decimal + string currency in order/pricing domain instead of Money value object."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-064: No FX conversion runtime
- Severity: Low
- Category: Missing Feature
- Description: Cross-currency is rejected, never converted. If a non-EGP merchant's CommerceHub payout is needed, no conversion path — only EGP pinning.
- Recommendation: Design conversion service if multi-currency payout is required.
- Complexity: L
- Dependencies: none
- Affected Modules: Currency runtime, CommerceHub finance
- Source Documents: platform/currency-runtime-final-architecture.md:380-381 (TD3)
- Evidence: "No FX conversion runtime. Cross-currency is rejected, never converted."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-065: Currency ES/SQL overlay hardcodes EGP (undocumented second authority)
- Severity: Medium
- Category: Technical Debt
- Description: `CommerceHubSearchSqlAuthorityOverlay.cs:204` hardcodes EGP — correct but undocumented as a second EGP authority site; risk if constant renamed.
- Recommendation: Centralize the EGP constant reference; document all authority sites.
- Complexity: S
- Dependencies: none
- Affected Modules: Currency runtime, Search
- Source Documents: platform/currency-runtime-final-architecture.md:382-383 (TD5)
- Evidence: "ES/SQL overlay hardcodes EGP — correct but undocumented as a second EGP authority site."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-066: Currency `CartEvents.Currency` default `"USD"` relies on snapshot override
- Severity: Medium
- Category: Technical Debt
- Description: `CartEvents.cs:58` defaults `"USD"` on an event that later hits `RequireMatch`; relies on snapshot override to avoid mismatch.
- Recommendation: Remove default; populate from authoritative snapshot at publish.
- Complexity: S
- Dependencies: ARC-062
- Affected Modules: Currency runtime, Cart/Checkout
- Source Documents: platform/currency-runtime-final-architecture.md:383-384 (TD6)
- Evidence: "CartEvents.Currency default 'USD' … a non-EGP default on an event that later hits RequireMatch; relies on snapshot override."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-067: Currency missing DB-level check constraints on CommerceHub financial currency
- Severity: High
- Category: Technical Debt
- Description: Only app-level `RequireEgpCurrency` guards CommerceHub finance columns; no `CHECK (Currency='EGP')` at DB level. Defense-in-depth gap.
- Recommendation: Migration adding `CHECK (Currency='EGP')` on finance tables.
- Complexity: S
- Dependencies: none
- Affected Modules: Currency runtime, CommerceHub finance, DB schema
- Source Documents: platform/currency-runtime-final-architecture.md:384-385 (TD7)
- Evidence: "Missing DB-level check constraints on CommerceHub financial currency columns … only app-level RequireEgpCurrency guards."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-068: Currency no production parity chaos proof
- Severity: Medium
- Category: Validation Gap
- Description: No PostgreSQL-backed mixed-currency chaos proof in production parity; certification is unit/manual only.
- Recommendation: Add scheduled SQL/Mongo/ES comparison alerting for non-EGP; mixed-currency chaos tests.
- Complexity: M
- Dependencies: ARC-067
- Affected Modules: Currency runtime, QA
- Source Documents: platform/currency-runtime-final-architecture.md:385-386 (TD8), 397 (follow-up)
- Evidence: "No PostgreSQL-backed mixed-currency chaos proof in production parity."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-069: New payment runtime disabled by feature flags (defaults false)
- Severity: Medium
- Category: Risk
- Description: `PaymentRuntimeFeatureFlags` defaults (`NewPaymentRuntime`, `NewPolicyEngine`, `DualRunPayments`, `ReplayEngine`, `ProviderAdapterV2`) all `false`. The deterministic state-machine code exists and is "authoritative when invoked" but is inactive in production. Documented payment lifecycle = older `PaymentLifecycleService`.
- Recommendation: Define rollout/validation plan and dual-run validation before activating; document which runtime is live.
- Complexity: L
- Dependencies: none
- Affected Modules: Payments, Payment runtime
- Source Documents: architecture/financial-architecture.md:18,307; platform/financial-architecture.md (§8.13)
- Evidence: "The new payment runtime exists as deterministic domain code … but is feature-gated by PaymentRuntimeFeatureFlags defaults … default false."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-070: Treasury `CashIn`/`CashOut`/`Expense` disabled (hidden behavior)
- Severity: Medium
- Category: Documentation Inconsistency
- Description: `TreasuryOperationService.RecordExpenseAsync/CashInAsync/CashOutAsync` throw — disabled by design. Docs say "treasury operations journal everything"; the caveat is only in code comments, not user docs.
- Recommendation: Document the disabled paths in user-facing docs; surface a clear error/UX.
- Complexity: S
- Dependencies: none
- Affected Modules: Treasury, Docs
- Source Documents: architecture/financial-architecture.md:453 (hidden behavior #1)
- Evidence: "TreasuryOperationService.RecordExpenseAsync/CashInAsync/CashOutAsync throw — disabled by design … only in code comments, not in user docs."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ARC-071: Cashbox shadow balances obsolete (`CurrentBalance`/`ExpectedBalance`)
- Severity: Medium
- Category: Documentation Inconsistency
- Description: `Cashbox.CurrentBalance`/`ExpectedBalance` are `[Obsolete]` "shadow balances"; any code/doc relying on them is wrong. Use the ledger account instead.
- Recommendation: Remove usages; document that ledger is the only cash authority.
- Complexity: S
- Dependencies: none
- Affected Modules: Cashbox, Treasury, Docs
- Source Documents: architecture/financial-architecture.md:454 (hidden behavior #2)
- Evidence: "Cashbox.CurrentBalance/ExpectedBalance are [Obsolete] shadow fields; any code/doc relying on them is wrong."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: yes

### ARC-072: Affiliate commissions global (no `TenantId`, `StoreTenantId` instead)
- Severity: Medium
- Category: Documentation Inconsistency
- Description: Affiliate commissions are global (no `TenantId`, keyed by `StoreTenantId` — the store that owes). A doc implying "affiliate balance is tenant-scoped" is wrong.
- Recommendation: Correct any doc that scopes affiliate balance by tenant; document `StoreTenantId` semantics.
- Complexity: S
- Dependencies: none
- Affected Modules: Affiliate, Docs
- Source Documents: architecture/financial-architecture.md:455 (hidden behavior #3)
- Evidence: "Affiliate commissions are global (no TenantId, StoreTenantId instead) … a doc implying 'affiliate balance is tenant-scoped' is wrong."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ARC-073: Affiliate `CommissionLedger.Paid` does NOT reduce available balance
- Severity: Medium
- Category: Documentation Inconsistency
- Description: Only `AffiliateBalanceLedger.Withdrawal` reduces available balance; `CommissionLedger.Paid` is a lifecycle flag only. Easy to mis-model in reporting/payout logic.
- Recommendation: Document the distinction prominently; add a drift-detection signal (`Paid` without matching `Withdrawal` = anomaly).
- Complexity: S
- Dependencies: none
- Affected Modules: Affiliate, Docs
- Source Documents: architecture/financial-architecture.md:456 (hidden behavior #4)
- Evidence: "AffiliateCommissionLedger.Paid does NOT reduce available balance; only AffiliateBalanceLedger.Withdrawal does."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ARC-074: Outbox critical-type list hand-maintained (drift risk)
- Severity: High
- Category: Risk
- Description: `AccountingOutboxProcessor._criticalFinancialTruthEventTypes` is hand-maintained. Adding a money-posting event type without registering it risks silent loss on failure.
- Recommendation: Derive critical types from event interfaces/markers; add a startup assertion that all posting events are registered.
- Complexity: M
- Dependencies: none
- Affected Modules: Accounting outbox, Payments
- Source Documents: architecture/financial-architecture.md:479 (hidden #8), 498 (risk)
- Evidence: "Outbox critical-type list is hand-maintained … adding a money event without registering it in _criticalFinancialTruthEventTypes risks silent loss on failure."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-075: CommerceHub vendor reconciliation complex/fragile
- Severity: High
- Category: Risk
- Description: CommerceHub vendor reconciliation depends on per-tenant suspense allocation + hold-release hosted workers (`CommerceHubFinanceReconciliationJob`, `CommerceHubPayoutReconciliationHostedService`). Fragile area; depends on workers running.
- Recommendation: Harden reconciliation workers; add idempotency/alerting; document recovery SLA.
- Complexity: L
- Dependencies: none
- Affected Modules: CommerceHub finance, Payments
- Source Documents: architecture/financial-architecture.md:499 (risk)
- Evidence: "CommerceHub vendor reconciliation is complex … a fragile area; depends on hosted workers running."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-076: Order projection `Total` vs SQL `Total` no runtime invariant
- Severity: Medium
- Category: Validation Gap
- Description: No code computes or asserts `OrderDocument.Total == SQL Order.Total`. Only the 30s reconciliation sweep + on-demand `Ensure*Projected` converge; no compile-time/runtime invariant enforces projection total == SQL total. Event-path bug could drift silently.
- Recommendation: Add a convergence test / checksum on `Total` for storefront orders (mirroring CommerceHub doc checksum).
- Complexity: M
- Dependencies: ARC-077
- Affected Modules: Order projection pipeline
- Source Documents: _scratch/order-cqrs-crossdomain.md:94
- Evidence: "No compile-time or runtime invariant enforces projection total == SQL total."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-077: Storefront `orders` doc has no checksum (only CommerceHub doc has)
- Severity: Medium
- Category: Technical Debt
- Description: Only `CommerceHubOrderProjectionChecksum.Compute` exists; storefront `OrderDocument` has no checksum. Repair relies on `ComputeAuthorityFingerprint` JSON equality that excludes freshness fields, so two docs with same business state but different event versions re-upsert.
- Recommendation: Add a storefront projection checksum for integrity verification.
- Complexity: M
- Dependencies: ARC-076
- Affected Modules: Order projection pipeline
- Source Documents: _scratch/order-cqrs-crossdomain.md:95
- Evidence: "Storefront orders doc has no checksum; only CommerceHub doc has one."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-078: Commission double-source edge race (event vs SQL repair)
- Severity: Medium
- Category: Risk
- Description: `OrderDocument.AffiliateCommissionAmount` set both by `AffiliateCommissionEarnedEvent` projection AND by SQL-authority snapshot on repair. Repair path uses whole-doc `BuildFreshnessGuard` (not commission facet); a stale repair could reset commission if its `sourceVersion` > doc's `LastEventVersion`.
- Recommendation: Use commission-facet freshness guard on repair path; add race test.
- Complexity: M
- Dependencies: none
- Affected Modules: Order/Affiliate projection
- Source Documents: _scratch/order-cqrs-crossdomain.md:96
- Evidence: "potential edge race where a stale repair could reset commission if its sourceVersion > doc's LastEventVersion."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-079: Marketplace read model lingers after storefront order tombstone
- Severity: Medium
- Category: Technical Debt
- Description: `MarketplaceOrderReadModelProjectionService.RebuildAsync` returns `NotFound` if the storefront Order is deleted/soft-deleted, and the marketplace doc is NOT auto-deleted (unlike product/homepage paths which tombstone). Marketplace order read model can linger after storefront order tombstone.
- Recommendation: Tombstone/delete the marketplace read model on storefront order deletion.
- Complexity: M
- Dependencies: none
- Affected Modules: Order projection, CommerceHub
- Source Documents: _scratch/order-cqrs-crossdomain.md:97
- Evidence: "if a storefront Order is deleted … the marketplace doc is NOT auto-deleted … Divergence."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-080: Tenant lifecycle has three owners, no coordinator
- Severity: Medium
- Category: Architecture Issue
- Description: `TenantsController` (SuperAdmin) PATCHes `Tenant.IsActive`; `TenantRegistrationService` (Platform) controls `Status`; `ServiceDiscoveryProjectionConsumer` reads governance `Status` to derive vendor servability. Three owners of one concept, no single lifecycle service coordinating them.
- Recommendation: Introduce a single `TenantLifecycleService` as the sole mutation authority.
- Complexity: L
- Dependencies: ARC-003
- Affected Modules: Tenant lifecycle, SuperAdmin, Platform, ServiceDiscovery
- Source Documents: platform/superadmin-platform-final-architecture.md:705-709 (TD2)
- Evidence: "Three owners of one concept, no single lifecycle service coordinating them."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-081: Mongo/ES vs SQL projection counts can diverge (diagnostics only)
- Severity: Medium
- Category: Monitoring Gap
- Description: Search/projection counts can diverge; surfaced as diagnostics gap, repaired manually via reindex. `SearchIndexStatus` is the only signal; no automated convergence alerting.
- Recommendation: Add automated drift alerting + auto-repair triggers on threshold breach.
- Complexity: M
- Dependencies: ARC-005
- Affected Modules: SuperAdmin monitoring, Search/Projection
- Source Documents: platform/superadmin-platform-final-architecture.md:601-603 (divergence)
- Evidence: "Mongo/ES vs SQL: search/projection counts can diverge; surfaced as a diagnostics gap, repaired manually via reindex."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-082: Currency production-parity scanner absent
- Severity: High
- Category: Monitoring Gap
- Description: No scheduled SQL/Mongo/ES comparison alerting for non-EGP currency leakage in production. Manual cert only.
- Recommendation: Add a production parity currency scanner job.
- Complexity: M
- Dependencies: ARC-068
- Affected Modules: Currency runtime, Observability
- Source Documents: platform/currency-runtime-final-architecture.md:396 (follow-up)
- Evidence: "Production parity currency scanner — Manual cert … Catch drift."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-083: Duplicate currency formatting logic (backend vs frontend drift)
- Severity: Low
- Category: Technical Debt
- Description: Backend `ResolveDisplayCurrencySymbol` and frontend `currency.ts` symbol maps can drift; EGP symbol differs (`E£` backend vs `EGP ` frontend).
- Recommendation: Single source of symbol map (shared contract); CI check.
- Complexity: S
- Dependencies: none
- Affected Modules: Currency runtime, Frontend
- Source Documents: platform/currency-runtime-final-architecture.md:381-382 (TD4)
- Evidence: "Duplicate formatting logic … EGP symbol differs (E£ backend vs EGP frontend)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-084: Manual financial repair is a dangerous primitive
- Severity: High
- Category: Governance Concern
- Description: Direct SQL/bulk mutation, full Mongo replace, raw ES writes, global repair all require reviewed boundaries; drift here can corrupt ledger truth. (Explicitly flagged in `.ai-context`.)
- Recommendation: Enforce reviewed/runbook-gated boundaries; audit all dangerous primitives.
- Complexity: L
- Dependencies: ARC-010
- Affected Modules: SuperAdmin dangerous ops, Ledger
- Source Documents: architecture/financial-architecture.md:497 (risk)
- Evidence: "Manual financial repair is a dangerous primitive … drift here can corrupt ledger truth."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-085: Settlement header denormalized (`OrderCount`) vs live lineage
- Severity: Medium
- Category: Technical Debt
- Description: `PaymentSettlementBatch.OrderCount` is denormalized; posting now requires live `PaymentSettlementOrders` lineage (`SettlementOrderLineageMismatch` telemetry). Docs that trust the header alone are wrong.
- Recommendation: Treat header as metadata only; enforce live lineage on posting.
- Complexity: M
- Dependencies: none
- Affected Modules: Settlement, Payments
- Source Documents: architecture/financial-architecture.md:461 (drift note)
- Evidence: "Settlement header drift: PaymentSettlementBatch.OrderCount is denormalized; posting now requires live PaymentSettlementOrders lineage."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-086: Legacy shadow fields trap (`PaymentStatus.Paid`, `Cashbox.CurrentBalance`)
- Severity: Medium
- Category: Risk
- Description: `PaymentStatus.Paid` is `[Obsolete]` → maps to `Collected`; `Cashbox.CurrentBalance`/`ExpectedBalance` obsolete. Both remain in the model as traps for new code.
- Recommendation: Remove or clearly obsolete-guard usages; add analyzers.
- Complexity: S
- Dependencies: ARC-071
- Affected Modules: Payments, Cashbox
- Source Documents: architecture/financial-architecture.md:458,500 (risks)
- Evidence: "Legacy shadow fields (Cashbox.CurrentBalance, PaymentStatus.Paid) remain in the model — a trap for new code."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

---

## SECOND PASS — ADDITIONAL / MISSED ITEMS

### ARC-087: WhatsApp transactional-bypass type list string-fragile
- Severity: Medium
- Category: Technical Debt
- Description: Cooldown bypass compares against hardcoded `{ORDER_CONFIRMATION, SHIPMENT_*}` list with ad-hoc underscore normalization, while message types elsewhere are `OTP`, `OrderConfirmation`, `PAYMENTAPPROVED`, etc. Mismatched naming risks a transactional message being wrongly cooled down.
- Recommendation: Centralize message-type constants/enums; unify naming.
- Complexity: S
- Dependencies: none
- Affected Modules: WhatsApp runtime
- Source Documents: platform/whatsapp-runtime-final-architecture.md:792-796 (TD3)
- Evidence: "Mismatched naming conventions risk a transactional message being wrongly cooled down."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-088: Outbox critical poison kept on 15-min slow-replay; non-critical poison terminal
- Severity: Low
- Category: Known Limitation
- Description: Critical poison (exhausted retries) flagged for operator reconciliation but kept on 15-min slow-replay; non-critical poison is terminal-marked processed. Operator reconciliation path is manual.
- Recommendation: Document SLA; add operator dashboard for `RequiresOperatorReconciliation`.
- Complexity: S
- Dependencies: none
- Affected Modules: Outbox
- Source Documents: platform/superadmin-platform-final-architecture.md:529-552 (outbox)
- Evidence: "sets RequiresOperatorReconciliation on critical poison after MaxRetries=5 … kept on a 15-min slow-replay cadence."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-089: SuperAdmin publishes only 7 event types, 0 inbound consumers
- Severity: Low
- Category: Architectural Improvement
- Description: SuperAdmin is a pure authority writer; verified zero inbound consumers. Intentional, but means no cross-domain event-driven reaction TO SuperAdmin decisions except via direct DbContext writes + outbox.
- Recommendation: Document the intentional design; consider event-driven notification to peers.
- Complexity: S
- Dependencies: none
- Affected Modules: SuperAdmin, Event bus
- Source Documents: platform/superadmin-platform-final-architecture.md:611-615
- Evidence: "publishes only 7 event types … and has 0 inbound consumers … a pure authority writer."
- Implementation Required: no
- Architecture Only: yes
- Documentation Only: no

### ARC-090: SuperAdmin writes peer tables in-process (shared DbContext)
- Severity: Medium
- Category: Architecture Issue
- Description: SuperAdmin runs in-process against the single shared `ApplicationDbContext` and writes peer tables (`CommerceHubListings`, `UserGovernanceProfiles`, etc.) *directly* rather than via peer contexts over HTTP/gRPC. Powerful but bypasses peer domain services for some mutations.
- Recommendation: Where possible route through peer domain services / events; restrict direct writes to governance overlays.
- Complexity: L
- Dependencies: ARC-008
- Affected Modules: SuperAdmin, CommerceHub, Merchant
- Source Documents: platform/superadmin-platform-final-architecture.md:607-615
- Evidence: "SuperAdmin runs in-process against the single shared ApplicationDbContext … writes peer tables directly."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-091: Reporting cash-flow relies on stringly-typed `SourceType` for cashbox classification
- Severity: Medium
- Category: Technical Debt
- Description: Daily Close classifies cashbox/clearing/bank/AR by `SourceType` string (e.g. `SalesInvoice`, `Return`, `Expense`, `PurchaseInvoice`, `SupplierPayment`); a rename silently breaks cash reconciliation. (Overlaps ARC-022 but specifically affects financial cash truth.)
- Recommendation: Same as ARC-022 — enum + account-code mapping.
- Complexity: M
- Dependencies: ARC-022
- Affected Modules: Reporting (Daily Close), Accounting
- Source Documents: platform/reporting-runtime-final-architecture.md:325-326
- Evidence: "classification by SourceType string (see debt: stringly-typed source types)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-092: No persisted snapshot tables for financial/operational reports
- Severity: Low
- Category: Performance Concern
- Description: No `KpiSnapshot`/`ReportSnapshot`/`DashboardSnapshot` tables; no scheduled pre-computation (only WhatsApp/Shipping Hangfire jobs). Historical point-in-time re-aggregated per read.
- Recommendation: Add snapshot tables for heavy historical reports.
- Complexity: M
- Dependencies: ARC-020
- Affected Modules: Reporting
- Source Documents: platform/reporting-runtime-final-architecture.md:656-662
- Evidence: "no persisted snapshot tables … no scheduled pre-computation. Hangfire jobs exist only for WhatsApp and Shipping."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-093: Storefront vs merchant revenue account split (PosSales vs EcommerceSales)
- Severity: Low
- Category: Known Limitation
- Description: Merchant revenue reads `AccountCodes.PosSales`; storefront reads `AccountCodes.EcommerceSales`. Two accounts, two code paths; cross-channel consolidation not evidenced in primary reads.
- Recommendation: Document the split; provide a consolidated revenue view if needed.
- Complexity: S
- Dependencies: none
- Affected Modules: Reporting
- Source Documents: platform/reporting-runtime-final-architecture.md:964-968 (TD10)
- Evidence: "Two accounts, two code paths; cross-channel consolidation is not evidenced."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: no

### ARC-094: SOA opening-balance double representation (UI/consistency ambiguity)
- Severity: Low
- Category: Known Limitation
- Description: In `GetStatementOfAccount`, opening-balance transactions appear both in the OpeningBalance card and the paginated list (excluded from Debit/Credit totals only) — acknowledged UI/consistency ambiguity.
- Recommendation: Clarify the representation; avoid double appearance.
- Complexity: S
- Dependencies: none
- Affected Modules: Reporting (SOA)
- Source Documents: platform/reporting-runtime-final-architecture.md:990-993 (TD14)
- Evidence: "acknowledged UI/consistency ambiguity."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: no

### ARC-095: CommerceHub finance "NetProfit"/"commission" mean different things in platform vs vendor context
- Severity: Medium
- Category: Technical Debt
- Description: `CommerceHubFinancialTransaction` computes platform vs vendor partitions; "NetProfit"/"commission" definitions differ between platform (`CommerceHubFinancialTransaction.cs:203-205`) and vendor (`CommerceHubVendorDebtService.cs:333`) contexts, computed via three different methods (ARC-028).
- Recommendation: Single definition service for commission/net-profit per context.
- Complexity: M
- Dependencies: ARC-028
- Affected Modules: CommerceHub finance
- Source Documents: platform/reporting-runtime-final-architecture.md:551-557
- Evidence: "NetProfit/commission mean different things in platform vs vendor context."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-096: Identity capability strings returned to SPA are UI hints only, never server-enforced
- Severity: Medium
- Category: Governance Concern
- Description: `NavigationCapabilities` (e.g. `super-admin.finance.view`) emitted at login; backend issues them but there is NO server-side `RequireClaim` policy enforcing them (only `Affiliate` role gated). Clients may render actions the server will reject.
- Recommendation: Either enforce server-side or document explicitly as non-authoritative.
- Complexity: M
- Dependencies: ARC-056
- Affected Modules: Identity, Frontend
- Source Documents: platform/identity-runtime-final-architecture.md:269-281 (Permission Runtime)
- Evidence: "The only RequireClaim in the codebase gates the Affiliate role … Server enforcement is exclusively scheme + role-claim + SQL authority + governance gates."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-097: Storefront/CommerceHub customer "role" claim is lookup key only, not authority
- Severity: Low
- Category: Known Limitation
- Description: Customer `role=Customer` claim used only as SQL key; no server-enforced permissions. Intentional but must not be mistaken for authorization.
- Recommendation: Document explicitly in customer-domain guides.
- Complexity: S
- Dependencies: none
- Affected Modules: Identity, Storefront, CommerceHub
- Source Documents: platform/identity-runtime-final-architecture.md:628-630 (divergence #4)
- Evidence: "customers have no role-claim authority; CustomerId is only a SQL key."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ARC-098: Tenant `IsActive` read by interceptor for user revocation while governance keys off `Status`
- Severity: High
- Category: Technical Debt
- Description: `PrivilegedRefreshTokenRevocationInterceptor` keys user revocation off `IsActive` (`:147`) while governance keys off `Status` — two signals, two code paths, compounding ARC-003 split-brain. A tenant can be `IsActive=true, Status=PendingSetup` and still revoke/validate inconsistently.
- Recommendation: Unify on `Status` (per ARC-003); remove `IsActive` from revocation logic.
- Complexity: M
- Dependencies: ARC-003
- Affected Modules: Identity, Tenant lifecycle
- Source Documents: platform/identity-runtime-final-architecture.md:624-627 (divergence #3)
- Evidence: "interceptor keys off IsActive for user revocation (:147) while governance keys off Status — two signals, two code paths."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-099: Affiliate Mongo `summary` (earnings) vs SQL ledger `Earned` — no reconciliation endpoint (detail)
- Severity: Medium
- Category: Technical Debt
- Description: Conversion-rate defined twice (Mongo orders/clicks vs SQL ledger `Earned`); "earnings" means different things (Mongo daily `Earnings` vs ledger `Earned` commission). No reconciliation endpoint exists. (Detail expansion of ARC-030.)
- Recommendation: Single earnings authority (SQL ledger); demote Mongo to display; add reconciliation endpoint.
- Complexity: M
- Dependencies: ARC-030, ARC-027
- Affected Modules: Affiliate, Reporting
- Source Documents: platform/reporting-runtime-final-architecture.md:513-519
- Evidence: "Conversion-rate is defined twice … 'earnings' means different things … no reconciliation endpoint."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-100: No automated projection healing (SuperAdmin)
- Severity: Medium
- Category: Known Limitation
- Description: SuperAdmin has no automated self-heal; recovery is reindex/replay/manual. `CommerceHubEmergencyStopOptions` is config-only. Contradicts the "repair-on-read/replay" pattern elsewhere.
- Recommendation: Add scheduled auto-heal for known divergence classes.
- Complexity: L
- Dependencies: ARC-005, ARC-081
- Affected Modules: SuperAdmin, Projection pipeline
- Source Documents: platform/superadmin-platform-final-architecture.md:790-791 (Missing capabilities)
- Evidence: "no automated projection healing; no SuperAdmin notification ownership; no SuperAdmin inbound event consumption."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-101: `enableBaileysForAllTenants` DEV endpoint + Playwright default — production exposure
- Severity: High
- Category: Security Concern
- Description: Combined with ARC-009 and ARC-038/ARC-049: a `[DEV ONLY]` endpoint toggles Baileys for all tenants, while the production-default transport selection has no auto-fallback and Playwright is production-fragile. Misconfiguration could route production traffic through the broken Playwright path.
- Recommendation: Enforce environment gating on the DEV endpoint; default transport selection must be safe in production.
- Complexity: M
- Dependencies: ARC-009, ARC-038, ARC-049
- Affected Modules: WhatsApp, SuperAdmin
- Source Documents: platform/superadmin-platform-final-architecture.md:742-744; platform/whatsapp-runtime-final-architecture.md:247-248
- Evidence: "DEV ONLY DANGER endpoint … no automatic fallback (Strangler-Fig)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ARC-102: Payment consumers non-blocking swallow masks production incidents
- Severity: Medium
- Category: Monitoring Gap
- Description: WhatsApp payment consumers (and service layer) never rethrow; combined with ARC-045, failures are invisible to monitoring. Operator has no signal when payment notifications systematically fail.
- Recommendation: Emit failure metrics/alerts; add a dead-letter/health signal for notification delivery.
- Complexity: M
- Dependencies: ARC-045
- Affected Modules: WhatsApp, Payments, Observability
- Source Documents: platform/whatsapp-runtime-final-architecture.md:351-358, 845-847
- Evidence: "All three events are INonAuthoritativeIntegrationEvent — WhatsApp failures never affect the payment lifecycle … never rethrows."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

---

## DOCUMENTATION CONFLICTS (cross-document and doc-vs-implementation)

### CONFLICT: C-01 — Tenant `IsActive` vs `Status` authority disagreement
- Conflicting Documents: platform/superadmin-platform-final-architecture.md (TD1, divergence) vs platform/identity-runtime-final-architecture.md (TD2, divergence #3) vs platform/merchant-platform-final-architecture.md (debt) vs `TenantResolutionMiddleware` behavior
- Explanation: All three canonical docs flag `IsActive`/`Status` as a split-brain, yet `TenantResolutionMiddleware` gates on BOTH, and `TenantsController.SetTenantStatus` writes only `IsActive`. The docs describe the problem but the implementation still enforces an inconsistent gate (a tenant with `IsActive=true, Status=PendingSetup` passes).
- Possible Cause: Partial migration from bool to enum never completed; governance and resolution code evolved separately.
- Recommended Resolution: Make `Status` the sole authority (ARC-003/ARC-098); update middleware and all writers; add a migration/backfill.

### CONFLICT: C-02 — "Subscription/License/FeatureFlag/GlobalCategory/GlobalBrand" entities assumed but absent
- Conflicting Documents: platform/superadmin-platform-final-architecture.md (TD3, "verified 0 matches") vs any brief/guide implying these governance tables exist
- Explanation: The SuperAdmin doc states the brief's assumed governance tables do not exist (verified 0 matches in `PointOfSale.Core`). Any documentation treating them as entities is wrong.
- Possible Cause: Documentation drift from an earlier design brief.
- Recommended Resolution: Remove references to these as entities; track ARC-004 (model explicitly or confirm intentional absence).

### CONFLICT: C-03 — POS Admin legacy cash entities: "write-blocked" yet "still written by POS"
- Conflicting Documents: platform/pos-admin-domain-architecture-final.md:503-510 (internal statement)
- Explanation: Same paragraph says `TreasuryTransaction`/`VisaTransaction`/`CashboxTransaction` are "write-blocked (real cash truth = ledger)" but then "CashboxTransaction is still written by POS for operational history." Contradictory on whether it is written.
- Possible Cause: Partial refactor; doc captured both old and new behavior.
- Recommended Resolution: Clarify: ledger account is authority; `CashboxTransaction` is operational history only (not written for accounting). See ARC-013.

### CONFLICT: C-04 — Currency "fail-closed on missing currency" vs hardcoded EGP/USD DTO defaults
- Conflicting Documents: platform/currency-runtime-final-architecture.md §5 Principle 4 ("Silent currency fallback forbidden", `[Obsolete(error:true)]`) vs TD1/TD6 (hardcoded `EGP`/`USD` defaults in DTOs and `CartEvents`)
- Explanation: The runtime's stated invariant is fail-closed (missing currency blocks), but multiple DTOs and `CartEvents.Currency` default to `"EGP"`/`"USD"`, which can mask missing-currency bugs — directly contradicting the principle.
- Possible Cause: Defaults added defensively; never reconciled with the fail-closed principle.
- Recommended Resolution: Remove hardcoded defaults; require explicit currency (ARC-062/ARC-066).

### CONFLICT: C-05 — "Single General Ledger" narrative vs four parallel ledgers
- Conflicting Documents: MENTAL_MODEL.md / architecture/financial-architecture.md (§1, "PostgreSQL = single system of record", "immutable double-entry Ledger") vs reporting-runtime TD12 / financial-architecture §9 (four separate ledgers: GL, party sub-ledger, Customer/Supplier transactions, CommerceHub financial)
- Explanation: The mental model asserts one SQL authority, but the financial architecture documents four independently-written ledgers with manual reconciliation (`RebuildPartyBalanceAsync`). "SQL is authority" is true per-table, but there is no single reconciled financial truth.
- Possible Cause: Ledgers evolved per-domain; reconciliation is manual.
- Recommended Resolution: Establish enforced reconciliation (ARC-019); document the four-ledger reality explicitly.

### CONFLICT: C-06 — Affiliate identity isolation: "global/cross-tenant" vs tenant-scoped mental model
- Conflicting Documents: platform/identity-runtime-final-architecture.md (TD9: CommerceHubCustomer & AffiliateAccount are GLOBAL, no TenantId) vs MENTAL_MODEL.md / Storefront guide (tenant isolation mandatory)
- Explanation: The mental model states tenant isolation is mandatory, but two identity realms (CommerceHub customer, affiliate) are explicitly cross-tenant by design. Consumers may wrongly assume tenant scoping.
- Possible Cause: Marketplace requires global customer/affiliate identity; not reflected as an explicit exception in the isolation rule.
- Recommended Resolution: Add explicit exception to the "tenant isolation mandatory" rule for these global identities (ARC-058).

### CONFLICT: C-07 — "No stored permissions/RBAC" vs `Accountant` role referenced in auth + pos-admin README
- Conflicting Documents: platform/identity-runtime-final-architecture.md (Permission Runtime: "VERIFIED ABSENT", ghost `Accountant` TD3) vs systems/pos-admin/README.md (references `Owner,Admin,Accountant` roles) vs AdminTenantAuthority `TenantActorRoles` (includes `"Accountant"`)
- Explanation: Permissions are verified absent, yet a ghost `Accountant` role is accepted by authority logic and referenced in the pos-admin guide. The guide implies a role that cannot be assigned via the enum.
- Possible Cause: Role added to authority set but never added to `UserRole` enum; README followed the authority set.
- Recommended Resolution: Reconcile role inventory (ARC-052); either add `Accountant` to enum or remove from authority + docs.

### CONFLICT: C-08 — Affiliate commission "earnings": Mongo summary vs SQL ledger two definitions
- Conflicting Documents: platform/reporting-runtime-final-architecture.md (affiliate debt: Mongo `summary` earnings vs SQL ledger `Earned`; two conversion-rate definitions) vs affiliate-system README (SQL-authoritative commission)
- Explanation: The affiliate system guide asserts SQL ledger is authority, but the reporting doc shows the Mongo `summary` uses different definitions and there is no reconciliation endpoint — two money authorities that disagree during Mongo lag.
- Possible Cause: Display projection diverges from ledger; no reconciliation.
- Recommended Resolution: Single SQL-ledger earnings authority; demote Mongo to display (ARC-030/ARC-099).

### CONFLICT: C-09 — Payment lifecycle "canonical" vs new runtime disabled by feature flags
- Conflicting Documents: MENTAL_MODEL.md / architecture/financial-architecture.md (Payment Lifecycle Service = canonical authority) vs financial-architecture §8.13 (new runtime code present but `PaymentRuntimeFeatureFlags` defaults all false)
- Explanation: Docs describe the payment lifecycle as the canonical path, but the newer deterministic runtime is implemented and feature-gated OFF. It is ambiguous which runtime is live in production.
- Possible Cause: Incremental rollout not completed; docs not updated to state activation status.
- Recommended Resolution: Document activation status explicitly; define rollout plan (ARC-069).

### CONFLICT: C-10 — WhatsApp doc references `WhatsAppOtpService` as feeding OTP flow, but it is dormant
- Conflicting Documents: platform/whatsapp-runtime-final-architecture.md (TD13: `WhatsAppOtpService` registered, zero callers; consumer comment references it) vs actual OTP flow (`StorefrontPhoneVerificationService` + `CheckoutPhoneOtpStore`)
- Explanation: A code comment in the consumer implies `WhatsAppOtpService` feeds the flow, but it is dead. Misleading for maintainers.
- Possible Cause: Service renamed/refactored; comment not updated.
- Recommended Resolution: Fix comment; delete or wire the service (ARC-042).

### CONFLICT: C-11 — Treasury "journals everything" vs `CashIn`/`CashOut`/`Expense` disabled
- Conflicting Documents: architecture/financial-architecture.md (hidden behavior #1: "treasury operations journal everything" caveat only in code) vs user docs implying treasury operations post freely
- Explanation: User-facing docs imply treasury journals all operations, but `RecordExpenseAsync/CashInAsync/CashOutAsync` throw (disabled by design). Outflows must come from certified authorities.
- Possible Cause: Safety hardening not reflected in user docs.
- Recommended Resolution: Document disabled paths (ARC-070).

### CONFLICT: C-12 — `Cashbox.CurrentBalance` as cash authority vs obsolete shadow field
- Conflicting Documents: architecture/financial-architecture.md (hidden behavior #2: `[Obsolete]` shadow fields; use ledger) vs any code/doc reading `Cashbox.CurrentBalance`/`ExpectedBalance`
- Explanation: The field is obsolete but remains in the model; docs/debug guides may still reference it as the cash balance.
- Possible Cause: Backward-compat field not removed.
- Recommended Resolution: Remove or guard; document ledger as sole cash authority (ARC-071/ARC-086).

### CONFLICT: C-13 — Affiliate balance "tenant-scoped" vs global `StoreTenantId`
- Conflicting Documents: architecture/financial-architecture.md (hidden behavior #3: commissions global, `StoreTenantId`, not `TenantId`) vs any doc implying affiliate balance is tenant-scoped
- Explanation: Affiliate commission ledger is keyed by `StoreTenantId` (the store that owes), not the affiliate's home tenant. Docs implying tenant-scoping are wrong.
- Possible Cause: Multi-tenant affiliate model; doc simplification.
- Recommended Resolution: Correct docs; document `StoreTenantId` semantics (ARC-072).

### CONFLICT: C-14 — `CommissionLedger.Paid` reduces balance vs only `Withdrawal` does
- Conflicting Documents: architecture/financial-architecture.md (hidden behavior #4) vs naive reading of payout lifecycle
- Explanation: `CommissionLedger.Paid` is a lifecycle flag and does NOT reduce available balance; only `AffiliateBalanceLedger.Withdrawal` does. Easy to mis-model payout math.
- Possible Cause: Two-ledger design (commission vs balance) not surfaced in higher-level docs.
- Recommended Resolution: Document the distinction; add drift detection (ARC-073).

### CONFLICT: C-15 — CommerceHub finance "platform overview" SuperAdmin-only vs admin controller need
- Conflicting Documents: platform/reporting-runtime-final-architecture.md (platform overview unreachable from admin controller, SuperAdmin-only) vs systems/commercehub/README.md (admin finance surfaces exist)
- Explanation: The CommerceHub admin finance controller exposes only vendor `dashboard`/`statement`/CSV; `GetPlatformOverviewAsync` is SuperAdmin-only, so admin operators cannot see platform overview despite having finance surfaces.
- Possible Cause: Intentional SuperAdmin boundary; not stated in admin guide.
- Recommended Resolution: Document the boundary or expose a scoped admin overview (ARC-029).

### CONFLICT: C-16 — "Outbox critical events never dropped" vs hand-maintained critical-type list
- Conflicting Documents: MENTAL_MODEL.md / architecture (outbox critical messages stay live for retry) vs architecture/financial-architecture.md (hidden behavior #8: `_criticalFinancialTruthEventTypes` hand-maintained; unregistered types risk silent loss)
- Explanation: The guarantee that critical financial events are never dropped depends on a manually-maintained list. A new money event not added to the list is treated as non-critical and can be terminal-dropped on failure.
- Possible Cause: List not derived from event markers.
- Recommended Resolution: Derive criticality from interfaces; add startup assertion (ARC-074).

---

## Backlog Summary

| Severity | Count |
|---|---|
| Critical | 3 (ARC-003, ARC-011, ARC-098) |
| High | 22 |
| Medium | 46 |
| Low | 15 |
| Architectural Improvement / Future Enhancement | 2 |
| **Total** | **102 items** + **16 conflicts** |

**Top priorities (Critical/High):** ARC-003 (tenant status split-brain), ARC-011 (JWT revocation), ARC-001/002 (POS price/cart trust), ARC-004 (subscription/license aggregates), ARC-005 (manual-only projection repair), ARC-016/017/019 (cost & four-ledger reconciliation), ARC-031/035/036/038/040/043 (WhatsApp security & functional gaps), ARC-062/067/082 (currency fail-closed & DB constraints), ARC-074/075/084 (outbox critical-list, CommerceHub reconciliation, dangerous repair), ARC-098 (interceptor split-brain).

**Note:** All issues above are extracted as *unresolved* per the task. The source knowledge base is implementation-backed (`file:line` cited); no API keys, tokens, or secrets appear in any cited material (any such would be `[REDACTED]`). Completed work, strengths, certifications, and "resolved" items were deliberately excluded.

**Files read:** 02-08 cross-cutting surveys; 9 platform runtime docs (pos, pos-admin-domain, merchant, superadmin, reporting, whatsapp, identity, currency, financial-architecture); 6 system READMEs (super-admin, commercehub, affiliate, storefront, pos-admin, service-marketplace); `_scratch/order-cqrs-crossdomain.md`; `MENTAL_MODEL.md`; `INDEX.md`.
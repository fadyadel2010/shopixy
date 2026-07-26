# PASS P1-EG POS Financial Reporting Authority Certification

Date: 2026-06-08

## Verdict

POS Financial Reporting Authority is production-certifiable for the inspected runtime after
hardening. Reports do not own financial truth. Trial balance, general ledger, P&L, balance sheet,
cash flow, clearing aging, account summaries, sales totals, treasury totals, receivables,
payables, inventory valuation, and financial dashboard cards derive from current tenant-owned SQL
authority.

## Trial Balance Findings

- Trial balance and general ledger use posted `LedgerEntries` and tenant-owned accounts only.
- General-ledger account lookup requires the trusted tenant and guessed foreign account ids fail.
- Balances are recomputed from debit and credit history rather than `Account.CurrentBalance`.
- Inactive accounts retain historical report visibility; account deactivation cannot suppress
  posted history.

## Profit And Loss Findings

- P&L revenue, COGS, and expenses are recomputed from posted ledger lines by account type.
- Sales reports use the posted POS sales and COGS accounts for financial totals.
- Business tables may provide dimensions such as product, category, or payment method, but cannot
  replace the ledger amount.

## Balance Sheet Findings

- Assets, liabilities, and equity are recomputed from posted ledger history as of the requested
  date.
- Inactive accounts are not dropped from historical balances.
- `Account.CurrentBalance`, party balances, and cached totals do not feed the balance sheet.

## Cash Flow Findings

- Cash flow uses posted ledger P&L and opening/closing ledger snapshots.
- Working-capital changes derive from ledger account ranges.
- Treasury transfers cannot double count cash because the report compares consolidated opening
  and closing ledger balances rather than summing transfer DTOs or legacy transaction rows.

## Receivables Findings

- Receivable aging and dashboard receivables derive from tenant-scoped `AccountLedgers`.
- `Customer.Balance` cannot influence inspected reporting paths.
- SOA, outstanding balances, aging totals, and dashboard receivables share the same party-ledger
  authority.

## Payables Findings

- Payable aging and dashboard payables now derive from tenant-scoped `AccountLedgers`.
- `Supplier.Balance`, purchase invoice totals, and supplier transaction compatibility rows no
  longer decide payable aging or dashboard totals.

## Inventory Valuation Findings

- Valuation derives from current `ProductVariantStocks` and current variant cost.
- Active warehouse, active current variant, active non-deleted tracked product lineage is required.
- Total physical valuation includes sellable plus damaged physical stock at current variant cost.
- Low-stock reporting uses available sellable quantity (`Quantity - ReservedQuantity`).
- Sale price and legacy `WarehouseStocks` cannot inflate valuation.

## Dashboard Findings

- Financial dashboard cards and trends recompute posted ledger revenue on every request.
- Receivables and payables use party ledgers.
- Inventory value uses inventory stock and cost authority.
- Financial and stock dashboard aggregate caches are bypassed; stale cached totals cannot become
  report authority.
- Recent document and dimensional lists remain SQL-derived presentation detail.

## Currency Findings

- POS ledger reports operate in the tenant's single base/ledger currency.
- The ledger schema does not carry a currency per journal line, so mixed-currency aggregation is
  not a supported reporting mode.
- CommerceHub financial authority remains EGP-only under its certified boundary and is not
  collapsed into POS tenant reports.
- Adding multi-currency POS ledgering requires currency on authoritative journals/lines,
  conversion authority, and per-currency report buckets before it can be certified.

## Concurrency Findings

- Financial report service and report controller multi-query reads use repeatable-read SQL
  transactions when relational storage is active.
- Reruns over the same committed ledger and date boundary produce the same result.
- Concurrent commits may appear only in a later snapshot; one report execution cannot combine
  pre-commit and post-commit totals.
- Posted journal idempotency and immutable ledger history prevent replay inflation.

## Adversarial Findings

- Tenant guessing fails through trusted tenant context, explicit tenant predicates, and EF tenant
  filters.
- DTO and filter values only narrow report dimensions; they do not supply authoritative totals.
- Date filters cannot create financial entries and only select committed SQL history.
- Route account guessing cannot expose foreign tenant ledger accounts.
- Browser or cache manipulation cannot replace backend SQL recomputation.

## Runtime Hardening Performed

- Replaced dashboard invoice-total revenue with posted ledger revenue.
- Replaced dashboard `Supplier.Balance` payables with supplier `AccountLedgers`.
- Replaced sale-price inventory value with current physical stock at variant cost.
- Hardened low-stock calculations to use available sellable stock and active warehouse lineage.
- Replaced payable aging purchase/supplier-transaction reconstruction with `AccountLedgers`.
- Included the complete selected as-of day in receivable and payable aging.
- Preserved inactive-account history in balance sheet, clearing aging, account summaries, and
  point-in-time balance snapshots.
- Disabled financial dashboard aggregate cache authority.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/DashboardController.cs`
- `PointOfSale.API/Controllers/FinancialReportsController.cs`
- `PointOfSale.API/Controllers/ReportsController.cs`
- `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/PosFinancialReportingAuthorityCertificationTests.cs`
- Existing `LedgerIsolationTests`, `FinancialReportsControllerTenantScopeTests`, and
  `DashboardControllerTenantScopeTests` were revalidated.

## Verification

- API build passed with zero errors.
- Infrastructure build passed with zero errors.
- Focused certification run passed: 15 tests, 0 failures.

## Remaining Debt

- Aging currently allocates aggregate reductions proportionally across age buckets because
  `AccountLedgers` does not persist invoice-payment allocation lineage. Totals match SOA authority,
  but exact invoice-level aging requires a certified allocation table or FIFO allocation policy.
- POS multi-currency ledger reporting is not implemented. The current certified boundary is one
  tenant base currency per ledger.
- Dashboard product/category revenue dimensions still require business-table allocation around
  ledger totals for exact net-return attribution.
- PostgreSQL-backed report concurrency and large-ledger performance evidence remains operational
  readiness work.

## Required Answers

- Can reports diverge from ledger truth? **No for certified financial totals; dimensional detail
  cannot replace ledger totals.**
- Can `Customer.Balance` influence reports? **No.**
- Can `Supplier.Balance` influence reports? **No after this hardening.**
- Can dashboards use shadow totals? **No for certified financial and inventory cards.**
- Can tenant leakage occur? **No on inspected report paths.**
- Can mixed currencies corrupt reports? **Not within the certified single-base-currency POS ledger
  boundary; unsupported mixed-currency POS aggregation must not be introduced.**
- Can inventory valuation diverge from stock truth? **No on the inspected valuation path.**
- Can reruns produce inconsistent results? **No for the same committed SQL state and date
  boundary.**
- Were runtime code changes required? **Yes.**
- Is Financial Reporting Authority production-certifiable? **Yes, for the inspected single-base-
  currency POS reporting scope after this hardening.**

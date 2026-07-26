# PASS C7-B Settlement Calculation Currency Authority

Date: 2026-06-23

## Certification Result

Settlement Calculation Currency Authority is production-certifiable after runtime hardening. CommerceHub owns settlement calculation, totals, balances, aggregates, ledger, payout eligibility, net settlement, refund/return impact, projection, export, and cache recovery currency truth. The only accepted currency is `EGP`.

## Runtime Findings

- Settlement calculations derive from CommerceHub SQL order, sub-order, refund, earning, payout, settlement, and financial transaction authority, never POS, storefront, vendor, tenant, browser, route, DTO, cache, projection, or formatter currency.
- Gross earnings, net payable, eligible payout amount, holdback, completed payouts, pending payouts, refunds, return/refund reversals, commission reversals, platform fees, settlement adjustments, balances, statement totals, running balances, and ledger entries require EGP evidence before they can be returned or used for outward payout eligibility.
- `CommerceHubVendorPayoutReadService` uses platform EGP as authority, rejects non-EGP ledger currencies, sets authority-conflict risk flags, and zeros payout eligibility when operational, refund, payout, or ledger currency evidence conflicts.
- `CommerceHubPayoutService` requires selected earnings to resolve to EGP, nets negative EGP adjustments only, requires ledger confirmation, and creates EGP payouts only.
- `CommerceHubFinancialLedgerService` requires EGP before order, refund, payout, commission, adjustment, settlement, and mirrored ledger entries can be persisted.
- `CommerceHubFinancialDashboardService` validates EGP before statement summaries, opening balances, running balances, CSV exports, finance activity, refund-impact rows, and dashboard currency can be accepted.
- `SuperAdminVendorFinanceService` normalizes write currency to EGP and now rejects non-EGP audit projection rows instead of echoing raw ledger currency.

## Runtime Hardening Performed

- Added statement calculation currency proof before opening/running balance and CSV export calculations.
- Added per-row CSV currency validation before running-balance accumulation.
- Hardened Super Admin vendor finance audit mapping to normalize/reject raw ledger currency.

## Exact Runtime Files Modified

- `PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialDashboardService.cs`
- `PointOfSale.API/SuperAdmin/Services/SuperAdminVendorFinanceService.cs`

## Regression Freezes Added

- `CommerceHubSettlementCalculationCurrencyAuthorityCertificationTests`

## Architecture Updates

Settlement calculation currency truth is CommerceHub-owned and fixed to EGP. Settlement totals, balances, aggregates, ledger balances, payout eligibility, refund/return impacts, statement/export projections, repair/replay, cache recovery, and frontend presentation may validate and withhold EGP backend evidence only; they must never calculate, aggregate, preserve, restore, or display non-EGP settlement truth.

## Explicit Certification Answers

- Who owns Settlement Calculation Currency Truth? CommerceHub backend/platform currency authority, fixed to `EGP`.
- Can settlement totals use USD? No.
- Can settlement totals use EUR? No.
- Can settlement totals use SAR? No.
- Can mixed-currency settlement calculations exist? No accepted calculation can; conflicts fail closed or block eligibility.
- Can earnings currency differ from settlement currency? No accepted earnings/settlement authority can differ from EGP.
- Can settlement aggregate non-EGP earnings? No.
- Can refund currency differ from settlement currency? No.
- Can return currency differ from settlement currency? No.
- Can ledger balances contain non-EGP currency? No accepted ledger balance/projection can; persistence and read guards reject it.
- Can payout eligibility be calculated from non-EGP balances? No.
- Can tenant currency affect settlement calculations? No.
- Can vendor currency affect settlement balances? No.
- Can projections contain mixed-currency settlement calculations? No accepted projection/export path can.
- Can cache restore non-EGP settlement calculations? No.
- Can forged currency become settlement calculation truth? No.
- Were runtime changes required? Yes.
- Is Settlement Calculation Currency Authority production-certifiable? Yes, for the inspected runtime paths.

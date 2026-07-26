# PASS C6 CommerceHub Vendor Earnings Currency Authority

Date: 2026-06-23

## Certification Result

CommerceHub Vendor Earnings Currency Authority is production-certifiable after runtime hardening. CommerceHub owns vendor earnings, finance, statement, payout, dashboard, projection, and presentation currency truth. The only accepted currency is `EGP`.

## Earnings Findings

Vendor earnings dashboard totals, earning rows, payable totals, holdbacks, completed payouts, pending payouts, and payout history now require `FinancialCurrencyGuard.RequireEgpCurrency` before DTO presentation. A single forged USD/EUR/SAR sub-order, payout, or payout snapshot currency fails closed instead of becoming display truth.

## Finance Findings

Vendor finance dashboard, platform overview, refund impacts, settlement evidence, current balances, commission totals, refund totals, payout totals, and recent activity require EGP. Super Admin vendor finance list rows no longer copy `Tenant.Currency`; they emit the CommerceHub authority currency.

## Statement Findings

Statement summary currency, opening/closing/current balances, credits, debits, running balances, transaction rows, and CSV source rows remain backend ledger-derived and are EGP-only. Mixed-currency and single non-EGP statement evidence is rejected before presentation.

## Payout Findings

Payout creation, payout readiness, payout completion, payout DTO mapping, payout queue snapshots, payout summaries, reserved balances, completed payouts, pending payouts, and holdbacks require EGP. A payout cannot diverge from earnings currency, and payout currency cannot derive from payment, tenant, vendor, or storefront currency.

## Formatter Findings

POS vendor earnings and finance pages already used the CommerceHub display-currency helper. Super Admin payout and finance formatters now block non-EGP currencies instead of passing them to `Intl.NumberFormat`. Browser locale may affect number formatting only; it cannot select currency.

## Projection Findings

Durable CommerceHub financial rows now have service/entity guards plus EF/database EGP constraints for payouts, financial transactions, vendor settlements, and vendor payment requests. Replay, repair, or projection-shaped writes cannot persist non-EGP CommerceHub vendor earnings currency.

## Cache Findings

React Query, browser history, BFCache, refresh, reconnect, local storage, and session storage remain presentation candidates only. Non-EGP DTOs are blocked by frontend admission/formatting and backend re-entry.

## Tenant Currency Findings

POS currency, storefront currency, tenant preferences, and vendor settings cannot affect vendor earnings currency. Super Admin vendor finance no longer copies tenant currency into vendor finance rows.

## Failure Findings

401/403 remove or withhold authority. 404/409/422 do not create currency fallback. Missing, malformed, forged, mixed, USD, EUR, SAR, tenant, storefront, or vendor currency evidence fails closed.

## Adversarial Findings

Forged DTO currency, dashboard tampering, projection tampering, payout evidence tampering, statement row tampering, browser locale changes, and cached non-EGP evidence cannot become earnings truth.

## Runtime Hardening Performed

- Required EGP in `CommerceHubEarningsService` DTO mapping.
- Required EGP in `CommerceHubFinancialDashboardService` overview, dashboard, statement, activity, and refund-impact mapping.
- Required EGP in `CommerceHubVendorPayoutReadService` snapshot currency normalization.
- Required EGP in `CommerceHubPayoutService` creation, readiness, completion, ledger confirmation, and DTO mapping.
- Required EGP in CommerceHub financial ledger order/refund/payout validation, financial safety capture, refund financial decisions, and vendor payment-control lookups.
- Required EGP in financial transaction, vendor settlement, and vendor payment request entity factories.
- Added EGP check constraints for payouts, financial transactions, vendor settlements, and vendor payment requests.
- Hardened Super Admin payout and finance frontend formatters/adapters to reject non-EGP presentation.

## Exact Runtime Files Modified

- `PointOfSale.API/Services/CommerceHub/CommerceHubEarningsService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs`
- `PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialDashboardService.cs`
- `PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialSafetyGuardService.cs`
- `PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorFinancialControlService.cs`
- `PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPaymentsService.cs`
- `PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutReadService.cs`
- `PointOfSale.API/Services/CommerceHub/FinancialLedger/CommerceHubFinancialLedgerService.cs`
- `PointOfSale.API/Services/CommerceHub/RefundFinancialDecisionEngine.cs`
- `PointOfSale.API/SuperAdmin/Services/SuperAdminVendorFinanceService.cs`
- `PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubFinancialTransaction.cs`
- `PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubVendorPaymentRequest.cs`
- `PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubVendorSettlement.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Data/Configurations/CommerceHubFinancialTransactionConfiguration.cs`
- `PointOfSale.Infrastructure/Data/Configurations/CommerceHubVendorPaymentRequestConfiguration.cs`
- `PointOfSale.Infrastructure/Data/Configurations/CommerceHubVendorSettlementConfiguration.cs`
- `PointOfSale.Infrastructure/Migrations/20260623150000_CommerceHubVendorEarningsCurrencyAuthorityHardening.cs`
- `PointOfSale.Web/src/super-admin/finance/FinanceDashboardPage.tsx`
- `PointOfSale.Web/src/super-admin/finance/FinanceAlertsPanel.tsx`
- `PointOfSale.Web/src/super-admin/finance/FinanceCharts.tsx`
- `PointOfSale.Web/src/super-admin/finance/FinanceKpiCards.tsx`
- `PointOfSale.Web/src/super-admin/finance/FinancePerformanceTables.tsx`
- `PointOfSale.Web/src/super-admin/finance/FinanceReconciliationPanel.tsx`
- `PointOfSale.Web/src/super-admin/finance/VendorFinanceWorkspace.tsx`
- `PointOfSale.Web/src/super-admin/payouts/PayoutsPage.tsx`
- `PointOfSale.Web/src/super-admin/payouts/payoutFinancialAuthority.ts`

## Regression Freezes Added

- `CommerceHubVendorEarningsCurrencyAuthorityCertificationTests`
- `payoutFinancialAuthority.test.ts` non-EGP queue rejection coverage

## Architecture Updates

CommerceHub vendor earnings currency truth is owned by the CommerceHub backend through `FinancialCurrencyGuard.PlatformBaseCurrencyCode` (`EGP`). Frontend formatters, dashboard adapters, caches, browser storage, tenant settings, storefront settings, and vendor settings may not select, infer, preserve, repair, or display another currency for CommerceHub vendor earnings.

## Explicit Certification Answers

- Who owns CommerceHub Vendor Earnings Currency Truth? CommerceHub backend platform currency authority, fixed to `EGP`.
- Can earnings display USD? No.
- Can earnings display EUR? No.
- Can earnings display SAR? No.
- Can tenant currency affect earnings currency? No.
- Can storefront currency affect earnings currency? No.
- Can vendor currency override earnings currency? No.
- Can formatter override CommerceHub currency? No.
- Can browser locale affect currency selection? No.
- Can statements display non-EGP currency? No.
- Can payouts display non-EGP currency? No.
- Can payout currency diverge from earnings currency? No.
- Can projections contain non-EGP currency? No accepted or persisted authority path can.
- Can cache restore non-EGP earnings state? No.
- Can forged currency become earnings truth? No.
- Were runtime changes required? Yes.
- Is CommerceHub Vendor Earnings Currency Authority production-certifiable? Yes, for the inspected runtime paths.

# PASS C7-C Settlement Visibility Currency Authority

Settlement Visibility Currency Authority is production-certifiable after runtime hardening.

## Certification Finding

CommerceHub owns Settlement Visibility Currency, Settlement Presentation Currency, Settlement Report Currency, Settlement Export Currency, Settlement Projection Currency, Settlement History Currency, and Settlement Detail Currency. The only accepted visible settlement currency is `EGP`.

POS currency, storefront currency, vendor currency, tenant preferences, browser locale, DTO currency fields, React state, React Query cache, local/session storage, route/history state, BFCache, projections, replay, repair, export code, and formatter defaults cannot select or restore settlement visibility currency.

## Runtime Findings

- Vendor finance settlement dashboard, recent settlements, statements, statement rows, payout history, payment history, refund impacts, and earning rows pass through `commerceHubVendorFinancialParticipationAuthority.js`, which rejects non-`EGP` and mixed-currency candidates before presentation.
- Vendor finance currency formatting uses `resolveCommerceHubDisplayCurrency`; unsupported CommerceHub currencies render as blocked presentation rather than USD/EUR/SAR.
- Super Admin vendor finance list, statement drawer, settlement dialogs, audit timeline, compact debt/payout history, and action payloads use `EGP` as the only visible/actionable settlement currency.
- Super Admin financial reports now prove every aggregate currency through `FinancialCurrencyGuard.RequireEgpCurrency` before returning overview, trends, promo, vendor performance, refund, and integrity report DTOs.
- Vendor finance alerts now normalize row currency before composing alert text and DTO currency, so forged payout or alert candidates cannot interpolate a foreign currency into visible settlement-adjacent messages.
- Vendor statement CSV export proves statement-scope EGP before balance accumulation and proves every CSV row currency before export output.

## Runtime Hardening Performed

- `SuperAdminFinancialDashboardService.EnsureSingleCurrency` now requires EGP for each aggregate/report currency, not merely a single coherent currency.
- `SuperAdminVendorFinanceService.MapAlertItem` now normalizes alert currency before both message construction and DTO projection.
- Added `CommerceHubSettlementVisibilityCurrencyAuthorityCertificationTests` to freeze settlement visibility, export, report, formatter, projection, cache, and tenant-currency authority rules.

## Exact Runtime Files Modified

- `PointOfSale.API/SuperAdmin/Services/SuperAdminFinancialDashboardService.cs`
- `PointOfSale.API/SuperAdmin/Services/SuperAdminVendorFinanceService.cs`
- `PointOfSale.Tests/Architecture/CommerceHubSettlementVisibilityCurrencyAuthorityCertificationTests.cs`

## Certification Answers

- Settlement screens can display USD: No.
- Settlement screens can display EUR: No.
- Settlement screens can display SAR: No.
- Mixed-currency settlement views can exist: No.
- Exports can contain non-EGP settlement currency: No.
- Reports can display non-EGP settlement currency: No.
- Formatter can override settlement currency: No.
- Tenant currency can affect settlement visibility: No.
- Vendor currency can affect settlement visibility: No.
- Browser locale can affect currency selection: No; locale may affect date/number rendering only, not currency code.
- Projections can display non-EGP settlement currency: No.
- Cache can restore non-EGP settlement visibility: No.
- Forged currency can become visible settlement truth: No.
- Runtime changes were required: Yes, to harden Super Admin report and alert visibility boundaries.

Settlement Visibility Currency Authority is production-certifiable.

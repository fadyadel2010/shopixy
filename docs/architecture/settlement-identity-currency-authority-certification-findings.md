# PASS C7-A Settlement Identity Currency Authority

Date: 2026-06-23

## Certification Result

Settlement Identity Currency Authority is production-certifiable after frontend runtime hardening. CommerceHub owns settlement currency truth, fixed to `FinancialCurrencyGuard.PlatformBaseCurrencyCode` (`EGP`). POS currency, storefront currency, vendor currency, tenant currency, payout currency candidates, DTO currency fields, cache, browser history, BFCache, replay, repair, projections, and exports cannot select or restore settlement identity currency.

## Settlement Identity Findings

- `CommerceHubVendorSettlement.Create` creates settlement identity with `FinancialCurrencyGuard.RequireEgpCurrency`, stores `Id`, `VendorId`, `SettlementType`, `Direction`, amount, EGP currency, idempotency key, external reference, actor evidence, and creation timestamp.
- `CommerceHubVendorSettlementConfiguration` enforces `CK_CommerceHubVendorSettlements_Currency_EGP`, positive amounts, nonblank idempotency/external reference, unique vendor idempotency, and unique non-legacy external reference.
- `ApplicationDbContext.GuardCommerceHubCurrencySovereignty` blocks added or modified CommerceHub currency-bearing entities unless the value is EGP, and `GuardCommerceHubVendorSettlementImmutability` makes settlement records append-only.
- Super Admin settlement writes normalize missing currency to EGP and reject non-EGP input with `CURRENCY_NOT_SUPPORTED`. Platform-to-vendor settlement is not a settlement write path; it must use the certified payout workflow.

## Lineage Findings

Order, payment, refund, return, earnings, payout, and settlement lineage remain SQL evidence only. Settlement identity is not derived from lineage currency. If lineage carries non-EGP historical/corrupt evidence, backend EGP guards, ledger checks, payout authority conflicts, and frontend admission boundaries fail closed instead of restoring that currency as settlement truth.

## Settlement-Earnings Findings

Payout creation requires selected earnings to resolve to EGP before payout identity can be created. `CommerceHubEarningsService`, `CommerceHubVendorPayoutReadService`, `CommerceHubPayoutService`, and financial dashboard mappings require EGP across earnings rows, payout snapshots, payout history, statements, and settlement evidence. Non-EGP earnings cannot become settlement identity currency or payout currency.

## Projection Findings

Durable settlement, payout, financial transaction, and vendor payment request writes are fenced by service/entity guards and database constraints. Statement/dashboard/export DTOs are backend ledger-derived and mapped through EGP validation. Projection, repair, replay, or export code can withhold or fail, but cannot persist or display non-EGP settlement identity.

## Recovery And Cache Findings

React Query, browser history, BFCache, reconnect, local storage, session storage, selected rows, dialogs, and mutation responses are untrusted presentation candidates. This pass hardened `commerceHubVendorFinancialParticipationAuthority.js` so all vendor finance, settlement, refund-impact, earning, payout, payment, and statement currency evidence must be exactly `EGP`; a coherent all-USD cache candidate now fails closed.

## Tenant Currency Findings

Tenant, vendor, storefront, and POS currency do not feed settlement identity. Super Admin vendor finance list rows emit the CommerceHub default currency and do not copy `Tenant.Currency`; settlement write currency normalization is EGP-only.

## Failure And Adversarial Findings

401/403 remove or withhold authority. 404/409/422 do not provide currency fallback. Missing, malformed, forged, mixed-currency, USD, EUR, SAR, tenant, vendor, storefront, payout, projection, replay, and stale-cache currency evidence cannot become settlement truth. Idempotency replay must match original SQL payload and currency; different payloads fail closed.

## Runtime Hardening Performed

- Replaced the POS vendor financial frontend boundary's generic three-letter currency acceptance with explicit `EGP` authority checks.
- Added regression coverage rejecting coherent non-EGP finance dashboard settlement evidence, earnings/payout evidence, payment history, and statement evidence.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js`
- `PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.test.ts`

## Regression Freezes Added

- `CommerceHubSettlementIdentityCurrencyAuthorityCertificationTests`
- Focused `commerceHubVendorFinancialParticipationAuthority.test.ts` non-EGP settlement/cache presentation coverage

## Architecture Updates

Settlement identity currency truth is owned by CommerceHub backend/platform EGP authority. Frontend settlement, finance, payout, earning, payment, statement, cache, recovery, and export boundaries may validate and withhold EGP evidence only; they must never accept another currency as settlement identity truth.

## Explicit Certification Answers

- Who owns Settlement Currency Truth? CommerceHub backend/platform currency authority, fixed to `EGP`.
- Can settlement identity carry USD? No.
- Can settlement identity carry EUR? No.
- Can settlement identity carry SAR? No.
- Can settlement lineage diverge from EGP? No accepted settlement truth can diverge; corrupt lineage is rejected or withheld.
- Can earnings currency differ from settlement currency? No accepted earnings/settlement authority can differ from EGP.
- Can settlement be created from non-EGP earnings? No.
- Can tenant currency affect settlement identity? No.
- Can vendor currency override settlement identity? No.
- Can projections contain non-EGP settlement identity? Not as accepted or persisted authority.
- Can cache restore non-EGP settlement identity? No.
- Can forged currency become settlement truth? No.
- Can mixed-currency lineage become settlement truth? No.
- Were runtime changes required? Yes, frontend admission hardening was required.
- Is Settlement Identity Currency Authority production-certifiable? Yes, for the inspected runtime paths.

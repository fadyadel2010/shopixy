# Settlement & Payout Reconciliation Currency Authority Certification Findings

PASS C7-F certifies Settlement & Payout Reconciliation Currency Authority.

CommerceHub owns reconciliation, recovery, replay, repair, batch rebuild, ledger verification, execution reconciliation, projection, and cache-recovery currency truth. The only authoritative reconciliation currency is `EGP`.

## Runtime Findings

- Reconciliation findings: payout reconciliation now proves `commercehub.payout.reconciliation.currency` is EGP before mutating stale processing payouts into `ReconciliationRequired`; finance reconciliation counts non-EGP ledger reference groups as anomalies and does not let coherent foreign groups reconcile as healthy.
- Recovery findings: unresolved-payment recovery requires both provider webhook currency and local payment currency to prove EGP before matching captured provider cash to local payment authority.
- Replay findings: payout execution replay and DTO projection already prove EGP; reconciliation replay cannot use tenant, vendor, provider, DTO, or cached currency as authority.
- Repair findings: CommerceHub projection repair and projection rebuild paths derive from SQL CommerceHub rows, while persistence and entity factories reject non-EGP CommerceHub financial rows.
- Batch rebuild findings: payout queue/readiness rebuild uses `CommerceHubVendorPayoutReadService`, which blocks eligibility on ledger, payout, refund, or operational currency conflicts and emits only EGP authority currency.
- Ledger findings: `CommerceHubFinancialTransaction.Create`, `CommerceHubVendorSettlement.Create`, ledger service validation, `GuardCommerceHubCurrencySovereignty`, and EGP database constraints keep ledger/settlement/payout durable rows EGP-only.
- Projection findings: reconciliation/report/projection DTOs must either prove EGP or exclude/block foreign rows; adjustment-flow reconciliation now carries grouped currency into in-memory validation before mismatch evaluation.
- Cache findings: frontend payout and vendor finance admission boundaries require exact `EGP`; React Query, browser history, BFCache, local storage, and session storage can only replay candidates that fail closed when non-EGP.
- Tenant currency findings: POS, storefront, vendor, and tenant currency do not participate in reconciliation currency authority; CommerceHub reconciliation uses `FinancialCurrencyGuard.PlatformBaseCurrencyCode`.
- Failure findings: malformed DTOs, 401/403/404/409/422 failures, failed refreshes, and mutation errors do not create reconciliation truth; backend guards reject or withhold foreign currency before persistence/projection.
- Adversarial findings: forged reconciliation rows, forged recovery rows, forged replay rows, replay tampering, repair tampering, and mixed-currency rebuilds cannot become authority because non-EGP durable rows are rejected and non-EGP ledger groups are counted as reconciliation anomalies.

## Runtime Hardening Performed

- Added service-level EGP proof before payout reconciliation status mutation.
- Added EGP proof for unresolved-payment recovery webhook/payment currency matching.
- Restricted finance reconciliation balances to EGP ledger rows and made non-EGP reference groups reconciliation anomalies.
- Preserved adjustment-flow grouped currency so settlement/adjustment repair-style rows cannot be validated under the default EGP value.

## Runtime Files Modified

- `PointOfSale.API/Services/CommerceHub/CommerceHubPayoutReconciliationHostedService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationJob.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationReadService.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/CommerceHubReconciliationCurrencyAuthorityCertificationTests.cs`

## Explicit Answers

- Who owns Reconciliation Currency Truth? CommerceHub.
- Can reconciliation accept USD? No.
- Can reconciliation accept EUR? No.
- Can reconciliation accept SAR? No.
- Can reconciliation restore foreign currency? No.
- Can recovery restore non-EGP payout currency? No.
- Can recovery restore non-EGP settlement currency? No.
- Can replay restore foreign currency? No.
- Can replay override execution currency? No.
- Can repair mutate settlement currency? No.
- Can repair mutate payout currency? No.
- Can rebuild restore foreign currency? No.
- Can rebuild aggregate mixed currency? No.
- Can ledger verification accept non-EGP? No.
- Can ledger repair restore foreign currency? No.
- Can projections contain foreign currency after reconciliation? No.
- Can cache restore foreign currency after reconciliation? No.
- Can tenant currency affect reconciliation? No.
- Can vendor currency affect recovery? No.
- Can forged currency become reconciliation truth? No.
- Were runtime changes required? Yes.
- Is Settlement & Payout Reconciliation Currency Authority production-certifiable? Yes.

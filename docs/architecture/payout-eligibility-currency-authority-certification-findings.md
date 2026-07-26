# PASS C7-D Payout Eligibility Currency Authority Certification

Date: 2026-06-23

## Verdict

Payout Eligibility Currency Authority is production-certifiable after runtime hardening. CommerceHub owns payout eligibility currency truth and the only accepted eligibility currency is `EGP`.

## Eligibility findings

- `CommerceHubVendorPayoutReadService` determines payout eligibility from CommerceHub SQL sub-orders, refund rows, payout rows, and payable ledger rows only.
- Ledger currencies are normalized with `FinancialCurrencyGuard.RequireEgpCurrency`; malformed, missing, non-EGP, or split ledger currency evidence sets `LedgerCurrencySplit`, zeroes payable/eligibility, and blocks payout authority.
- Operational sub-orders, refunds, and prior payouts participate only when their currency equals the EGP authority currency. Any mismatch sets an authority-conflict risk and forces `EligibleAmount = 0`.

## Balance findings

- Available/eligible, pending/reserved payout, payable, holdback, and completed-payout balances are computed under one EGP authority currency.
- `CommerceHubFinancialSafetyGuardService` captures platform cash, vendor payable, and vendor debt only after `FinancialCurrencyGuard.RequireEgpCurrency` and queries ledger rows by that EGP currency.
- Non-EGP balances cannot participate in eligibility; balance currency cannot diverge from settlement/ledger currency without blocking eligibility.

## Threshold findings

- Minimum/maximum payout submission is bounded by current backend `EligibleAmount`, `TotalPayable`, ledger-backed `VendorPayable`, and platform cash, all under EGP.
- Delay thresholds (`PayoutDelayDays`, refund window, processing-payout lag, large-payable alert delay/amount) are time/amount thresholds only; they do not carry or select currency.
- Tenant or vendor currency cannot affect threshold currency.

## Readiness findings

- `CommerceHubFinancialReadinessGate` must pass before outward money movement.
- Super Admin execution then revalidates snapshot currency, safety-guard currency, eligible amount, payable amount, platform cash, active refund/return locks, payout-method scope, and authority-conflict risk flags.
- Readiness cannot be granted to non-EGP balances and cannot survive ledger, refund, payout, or operational currency conflict.

## Settlement-eligibility findings

- Payout eligibility is downstream of CommerceHub settlement/ledger authority and uses EGP-only settlement calculation evidence.
- Mixed or foreign settlement/payable ledger evidence is quarantined by the payout read service and cannot aggregate into eligibility.

## Projection findings

- Vendor earnings and Super Admin payout DTOs expose payout eligibility only after backend EGP validation.
- C7-D hardening now requires Super Admin payout queue snapshots to pass `FinancialCurrencyGuard.RequireEgpCurrency` before queue items, currency summaries, aggregate currency, eligible amount, holdback amount, and readiness flags are projected.
- Frontend Super Admin payout queue and execution adapters reject non-EGP queue items, summaries, aggregate currency, and execution responses before React Query/presentation admission.

## Recovery findings

- Idempotency replay reloads the persisted payout and validates amount/method/evidence; persisted payout currency remains EGP-guarded by execution and persistence controls.
- Repair/replay/projection candidates cannot restore foreign-currency eligibility because runtime reads re-enter the EGP snapshot and queue-admission boundaries.

## Cache findings

- React Query, browser history, BFCache, local storage, and session storage do not own payout eligibility. The inspected payout queue service admits only EGP DTOs and execution responses.
- Stale or forged non-EGP queue evidence fails closed in `payoutFinancialAuthority.ts`.

## Tenant currency findings

- POS, storefront, vendor, and tenant currencies are not payout eligibility authorities.
- `CommerceHubVendorPayoutReadService` selects vendor row currency as `FinancialCurrencyGuard.PlatformBaseCurrencyCode`; it does not copy `Tenant.Currency` into eligibility.

## Failure findings

- 401/403/404/409/422, malformed DTOs, missing currency, forged USD/EUR/SAR currency, mixed ledger rows, mismatched refund currency, mismatched payout currency, and mismatched operational sub-order currency fail closed or zero eligibility with authority-conflict flags.
- Super Admin queue projection now returns a controlled `PAYOUT_ELIGIBILITY_CURRENCY_INVALID` failure instead of emitting non-EGP eligibility evidence.

## Adversarial findings

- Forged currency cannot become eligibility truth; payout execution request DTOs carry no currency field.
- Mixed-currency balances cannot become eligible because authority-conflict risk forces `EligibleAmount = 0` and execution rejects authority-blocking risks.
- Threshold/readiness tampering cannot exceed current backend eligible authority because frontend and backend both bind execution amount to current `EligibleAmount`.

## Runtime hardening performed

- Super Admin payout queue projection now requires each payout snapshot currency to be EGP before eligible, available/payable, holdback, summary, aggregate, and readiness DTOs are emitted.
- Super Admin payout queue controller now converts payout eligibility currency failures into fail-closed API errors.

## Exact runtime files modified

- `PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutsService.cs`
- `PointOfSale.API/SuperAdmin/Controllers/SuperAdminPayoutsController.cs`

## Regression freezes added

- `PointOfSale.Tests/Architecture/CommerceHubPayoutEligibilityCurrencyAuthorityCertificationTests.cs`

## Architecture updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/payout-eligibility-currency-authority-certification-findings.md`

## Explicit certification answers

| Question | Answer |
| --- | --- |
| Who owns Payout Eligibility Currency Truth? | CommerceHub backend runtime, fixed to `FinancialCurrencyGuard.PlatformBaseCurrencyCode` (`EGP`). |
| Can eligible balances use USD? | No. |
| Can eligible balances use EUR? | No. |
| Can eligible balances use SAR? | No. |
| Can mixed-currency balances become eligible? | No. |
| Can non-EGP balances participate in eligibility? | No. |
| Can balance currency diverge from settlement currency? | No; divergence blocks eligibility. |
| Can threshold calculations use non-EGP currency? | No. |
| Can readiness be granted to non-EGP balances? | No. |
| Can readiness survive currency conflict? | No. |
| Can settlement currency differ from eligibility currency? | No. |
| Can eligibility aggregate non-EGP settlements? | No. |
| Can projections contain non-EGP eligibility? | No after C7-D queue projection hardening and existing frontend admission. |
| Can cache restore non-EGP eligibility? | No. |
| Can tenant currency affect eligibility? | No. |
| Can vendor currency affect readiness? | No. |
| Can forged currency become eligibility truth? | No. |
| Were runtime changes required? | Yes, for backend Super Admin payout queue projection fail-closed hardening. |
| Is Payout Eligibility Currency Authority production-certifiable? | Yes, for the inspected runtime paths. |

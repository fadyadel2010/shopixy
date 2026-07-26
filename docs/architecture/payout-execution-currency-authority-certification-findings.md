# PASS C7-E Payout Execution Currency Authority Certification

Date: 2026-06-23

## Verdict

Payout Execution Currency Authority is production-certifiable after runtime hardening. CommerceHub owns payout execution currency truth. The only currency that can leave CommerceHub through payout execution is `EGP`.

## Findings

- Execution: Super Admin payout execution accepts amount, payout method, external evidence, and idempotency only; it has no request currency field. Execution now resolves currency only from the current EGP payout snapshot/state authority, persists EGP, records EGP ledger movement, and maps replayed execution DTOs only after EGP validation.
- Provider: provider, bank, gateway, transfer reference, external reference, bank transaction id, handoff token, and completion fingerprint are evidence only. They carry no accepted currency field and cannot override payout currency.
- Batch: payout queue summaries and execution batches are EGP-only. Mixed or non-EGP queue evidence fails closed before execution, and persistence enforces `CK_CommerceHubPayouts_Currency_EGP`.
- Reconciliation: payout reconciliation quarantines stale processing payouts without changing currency. Finance reconciliation now excludes non-EGP payout rows from payout-derived mismatch aggregation, so legacy or forged foreign rows cannot restore payout execution truth.
- Recovery: idempotency replay reloads the persisted payout, compares amount/method/evidence, and now validates EGP before returning execution DTO evidence.
- Replay: replay, repair, and completion fingerprints include the persisted payout currency and cannot convert or replace it. Persistence guards reject added or modified CommerceHub payout rows unless currency is EGP.
- Projection: Super Admin payout queue, admin finance payout rows, execution responses, and payout DTOs require EGP before projection. Frontend payout admission rejects non-EGP queue summaries and execution responses before React Query presentation.
- Cache: React Query, browser history, BFCache, local storage, and session storage have no execution currency authority. Stale or forged non-EGP execution evidence fails frontend admission or backend re-entry.
- Tenant currency: POS, storefront, tenant, and vendor currencies cannot affect execution. Super Admin execution no longer consults vendor currency while resolving payout execution currency.
- Failure and adversarial: 401/403/404/409/422, malformed DTOs, forged provider responses, forged currency fields, mixed-currency batches, execution tampering, replay tampering, stale cache, and idempotency-key reuse cannot create non-EGP execution truth.

## Runtime Hardening Performed

- Removed vendor currency from Super Admin payout execution currency resolution; execution currency is now the current EGP payout snapshot/state authority only.
- Required EGP before Super Admin payout execution replay/DTO mapping.
- Required EGP before admin finance payout projection.
- Filtered payout reconciliation aggregation to EGP payout execution rows.

## Exact Runtime Files Modified

- `PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutsService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReadService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationReadService.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/CommerceHubPayoutExecutionCurrencyAuthorityCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/payout-execution-currency-authority-certification-findings.md`

## Explicit Certification Answers

| Question | Answer |
| --- | --- |
| Who owns Payout Execution Currency Truth? | CommerceHub backend runtime, fixed to `FinancialCurrencyGuard.PlatformBaseCurrencyCode` (`EGP`). |
| Can execution use USD? | No. |
| Can execution use EUR? | No. |
| Can execution use SAR? | No. |
| Can provider payloads use non-EGP currency? | No inspected CommerceHub payout provider payload accepts currency; provider evidence cannot set currency. |
| Can provider responses override EGP? | No. |
| Can execution currency diverge from eligibility currency? | No. Execution re-enters the EGP payout snapshot and state authority. |
| Can execution currency diverge from settlement currency? | No. Settlement, eligibility, ledger, payout persistence, and execution are EGP-only. |
| Can execution batches contain mixed currency? | No accepted queue/batch projection can; mixed evidence fails closed. |
| Can reconciliation accept non-EGP currency? | No accepted reconciliation authority can restore it; payout aggregation is EGP-filtered. |
| Can replay restore non-EGP execution? | No. Replay validates persisted EGP payout currency before DTO projection. |
| Can retry execute non-EGP currency? | No. Retry/idempotency reuses the persisted EGP payout and rejects conflicting payloads. |
| Can projections contain non-EGP execution currency? | No accepted projection path can. |
| Can cache restore non-EGP execution authority? | No. |
| Can tenant currency affect execution? | No. |
| Can vendor currency affect provider payloads? | No. |
| Can forged currency become execution truth? | No. |
| Were runtime changes required? | Yes. |
| Is Payout Execution Currency Authority production-certifiable? | Yes, for the inspected runtime paths. |

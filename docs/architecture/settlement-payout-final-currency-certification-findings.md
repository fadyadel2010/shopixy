# PASS C7-G Settlement & Payout Final Currency Certification

Date: 2026-06-23

Settlement & Payout Currency Sovereignty is production-certifiable after final runtime reinspection and projection hardening. CommerceHub owns all settlement and payout currency truth. The only authoritative settlement and payout currency is `EGP`.

## Final Findings

- Settlement identity, calculation, visibility, eligibility, execution, reconciliation, recovery, replay, repair, projection, export, ledger, and governance currency are CommerceHub-owned and EGP-only.
- Payout eligibility, execution, reconciliation, provider, batch, projection, recovery, replay, and governance currency are CommerceHub-owned and EGP-only.
- POS, storefront, vendor, and tenant currency remain outside CommerceHub settlement and payout authority.

## Runtime Hardening Performed

- Daily financial aggregate projection now requires EGP before merging any order/refund/ledger aggregate row, including refund metric rows sourced from `CommerceHubRefunds.Currency`.
- Daily aggregate persistence now has EGP check constraints for both `CurrencyCode` and `BaseCurrencyCode`.

## Exact Runtime Files Modified

- `PointOfSale.API/Services/CommerceHub/CommerceHubDailyFinancialAggregateService.cs`
- `PointOfSale.Infrastructure/Data/Configurations/CommerceHubDailyFinancialAggregateConfiguration.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/CommerceHubSettlementPayoutFinalCurrencyCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/settlement-payout-final-currency-certification-findings.md`

## Explicit Certification Answers

| Question | Answer |
| --- | --- |
| Who owns Settlement & Payout Currency Truth? | CommerceHub backend/platform authority, fixed to `EGP`. |
| Can settlement identity carry non-EGP currency? | No. |
| Can settlement calculations use non-EGP currency? | No. |
| Can settlement visibility display non-EGP currency? | No accepted visibility path can; foreign evidence fails closed. |
| Can non-EGP balances become eligible? | No. |
| Can payout execution use non-EGP currency? | No. |
| Can provider responses override EGP? | No. |
| Can reconciliation restore foreign currency? | No. |
| Can replay restore foreign currency? | No. |
| Can repair mutate currency? | No. |
| Can any domain diverge from EGP? | No accepted CommerceHub settlement or payout authority can diverge from EGP. |
| Can cache restore foreign currency? | No. |
| Can tenant currency affect settlement or payout authority? | No. |
| Can vendor currency affect execution currency? | No. |
| Can forged currency become authority? | No. |
| Were runtime changes required? | Yes, final projection hardening was required for daily financial aggregates. |
| Is Settlement & Payout Currency Sovereignty production-certifiable? | Yes, for the inspected runtime paths after hardening. |

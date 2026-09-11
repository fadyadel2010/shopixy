# POS Financial Certification - 2026-09-11

## Status

The corrected POS financial paths passed browser-based runtime certification and read-only database reconciliation for tenant `greatone` (`c43aebf2-4cc4-406a-a2db-4df06d40bce5`). The Balance Sheet is balanced and the Cash Flow report reconciles.

Historical inventory capitalization and pre-fix tax presentation require controlled accounting policy decisions. They are not safe candidates for automatic journal rewriting.

## Controls

- All business scenarios were executed through the real browser UI.
- PostgreSQL verification used repeatable-read, read-only transactions.
- No business data was edited directly with SQL.
- Migration `20260911190000_POS_TaxAccountsAndPartyBalanceRepair` was applied through EF Core.
- The migration seeds `1320 Input Tax Receivable` and `2050 Sales Tax Payable` and repairs chronological customer/supplier transaction projections.
- Immutable historical journals were not modified.

## Post-Fix Scenario

- Customer: `GFRC PostFix Customer 202609112210` (`1b6c7df2-d6fb-421b-b19e-c15257f6b797`).
- Supplier: `GFRC PostFix Supplier 202609112210` (`a9af3fe5-3f2e-439c-90ce-12e76827586a`).
- Credit sale: `INV-20260911-0031` (`35a836b5-4e37-4e08-8f08-36724e7ef3ad`), subtotal `40.00`, tax `2.00`, total `42.00`.
- Backdated mixed customer payment: `12.00`, cash `5.00`, card `7.00` (`a9c435b4-f3c0-4a74-93f7-e7a2a9b42593`).
- Sales return: `SR-20260911-1010` (`75a719aa-0c13-4844-8268-41311a150762`), subtotal `20.00`, tax `1.00`, account credit `21.00`.
- Credit purchase: `PUR-20260911-1009` (`ae66e6f3-ffbd-4931-ac91-5275aad8f130`), subtotal `30.00`, tax `1.50`, total `31.50`.
- Cash supplier payment: `11.00` (`f2c82f91-fcc7-4ae5-bcc2-d6cbd291c6c3`).
- Purchase return: `PR-20260911-1011` (`6c7888a1-f9f5-4e6b-8a6d-a0fbca999dbf`), subtotal `10.00`, tax `0.50`, total `10.50`.
- A supplier payment of `999999.00` was rejected with `INSUFFICIENT_FUNDS` and created no payment or outbox message.
- Same-key/same-payload payment replays returned the original `201` response. Changed-payload replays returned `400` and created no duplicate mutation.

## Reconciliation

| Control | Expected delta | Actual delta |
| --- | ---: | ---: |
| Cashbox `1010` | -6.00 | -6.00 |
| Visa clearing `1110` | +7.00 | +7.00 |
| Inventory `1310` | +10.00 | +10.00 |
| Input tax receivable `1320` | +1.00 | +1.00 |
| Accounts receivable `1410` | +9.00 | +9.00 |
| Supplier payables `2010` | +10.00 | +10.00 |
| Sales tax payable `2050` | +1.00 | +1.00 |
| POS sales `4010` | +20.00 | +20.00 |
| Cost of goods sold `5010` | +10.00 | +10.00 |
| Controlled variant physical stock | +1.00 | +1.00 |

- Customer shadow balance, account ledger, and transaction projection all close at `9.00`.
- Customer chronological `BalanceAfter` values are `-12.00`, `30.00`, and `9.00`.
- Supplier shadow balance, account ledger, and transaction projection all close at signed `-10.00`; the AP liability is `+10.00`.
- Supplier chronological `BalanceAfter` values are `-31.50`, `-20.50`, and `-10.00`.
- Accounting outbox: 54 total, 0 pending, 0 failed.
- Stock outbox: 62 total, 0 pending, 0 failed.
- Journals: 84 total, 83 posted, 1 historical reversal, 0 unbalanced.
- Ledger totals: debits `23,751.3400`, credits `23,751.3400`.

## Reports

- Balance Sheet: `BALANCED`; assets `2,763.00`, liabilities `118.50`, equity `2,644.50`.
- Current Earnings: `2,744.50` through synthetic report line `3999`.
- Cash Flow: `RECONCILED`; opening cash `0.00`, net change `5,248.84`, closing cash `5,248.84`.
- AR aging: `18.00`, including both controlled customers at `9.00` each.
- AP aging: `117.50`, including both controlled suppliers at `10.00` each.

Screenshots are under `.tmp/global-financial-audit/browser/artifacts/`, including the `scenario-postfix-*` workflow evidence and final report captures.

## Verification

- `dotnet build PointOfSale.API/PointOfSale.API.csproj -c FinancialAudit --no-restore`: passed with 0 warnings and 0 errors.
- Focused accounting, reporting, ledger safety, and account-seeder tests: 49 passed, 0 failed.
- EF migration discovery: passed.
- Transactional PostgreSQL migration-script generation: passed.
- Swagger endpoint after restart: HTTP 200.
- `/health`: HTTP 503 because optional infrastructure remains degraded; this was already present and is outside the financial posting corrections.

## Historical Remediation Boundaries

### Inventory

Historical opening-stock and adjustment movements do not have corresponding accounting journals. Some opening rows also lack an authoritative price snapshot. Automatically deriving and posting a capitalization amount would invent historical cost evidence and could materially misstate inventory and equity.

Required decision: approve a dated inventory valuation policy and controlled adjustment journal based on authoritative source documents. Do not backfill historical journals from current product cost.

### Legacy Tax Journals

Transactions posted before this correction embedded tax in gross sales and inventory balances. Those journals are immutable audit records and were intentionally not rewritten. The new tax accounts begin with migration-time zero balances and receive tax-separated postings for new transactions.

Required decision: if historical tax reclassification is required, approve explicit adjustment journals for the affected reporting period after tax review. Do not update historical ledger lines in place.

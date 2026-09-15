# Treasury T-05/T-06 Account Authority And 1020 Classification - 2026-09-12

## 1. Decision

**T-05 is confirmed and ready for a bounded metadata fix.** `FinancialReportService.GetAccountSummariesAsync` sets `IsTreasury` from `LedgerAccount.IsSystem`, but the operational Treasury authority is an active, non-deleted, same-tenant `TreasuryAccount` mapping to an eligible ledger account. For shopixy, the current projection returns 32 Treasury accounts when only six mappings exist, producing 26 false positives.

**T-06 is classified as an active accounting-control design defect, not a missing Treasury mapping.** Account `1020 POS Visa Account` is the hard-coded credit destination for supplier purchase card portions. Its EGP -3.80 is fully supported by immutable purchase journals, but an Asset account used only as a supplier-funding credit accumulates an unnatural negative balance. It must not be made Cashbox, Bank, or Clearing authority merely to make it appear in Treasury. The correct target depends on evidence that is not represented today: actual Bank for a debit/bank card, or a dedicated Liability for a credit card. Therefore the safe outcome is **reclassify future posting authority, journal-reclassify the existing balance from approved external evidence, then retire 1020 from new postings**.

No application fix or financial mutation was performed in this discovery task.

## 2. Scope

This report closes the discovery and classification work for T-05 and T-06 from the Phase 1 Treasury audit. It establishes:

- The canonical source of Treasury-account authority.
- Every current shopixy `IsSystem` versus Treasury-mapping mismatch.
- The full shopixy account catalog and report classification surface.
- The creation, posting, reversal, source-document, outbox, legacy, and cross-tenant provenance of account `1020`.
- The downstream report, API, workflow, and UI impact.
- A smallest-safe remediation sequence without implementing it.

T-03 and T-07 through T-09 remain outside this task.

## 3. Tenant Boundary

| Field | Value |
| --- | --- |
| Tenant | `shopixy` |
| Tenant ID | `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` |
| Currency | EGP |
| Database | `PosEcommerceDb_Baseline` |
| Tenant created | `2026-07-26T17:31:08.321050Z` |
| First ledger account created | `2026-07-26T17:31:11.546448Z` |
| Account count | 32 |
| Active Treasury mappings | 6 |

The known `greatone` browser session was not used as shopixy evidence. No authenticated shopixy browser-content certification is claimed.

## 4. Evidence Method

Database queries ran through an isolated Npgsql harness in a repeatable-read transaction. The harness executes `SET TRANSACTION READ ONLY`, accepts only SQL beginning with `SELECT` or `WITH`, and explicitly rolls back. Queries carried the shopixy tenant ID and inspected accounts, Treasury mappings, payment methods, journals, lines, outbox rows, purchase invoices, legacy finance tables, foreign keys, and cross-tenant account state.

Source evidence was read from the current working tree and checked with targeted `git blame`/`git log`. Existing uncommitted work from earlier Treasury tasks was preserved. The only repository artifact added by this task is this report.

## 5. Canonical Treasury Authority

The canonical operational rule is:

```text
Treasury authority exists when:
  TreasuryAccount.TenantId == requested tenant
  and TreasuryAccount.AccountId == LedgerAccount.Id
  and TreasuryAccount.IsActive
  and !TreasuryAccount.IsDeleted
  and LedgerAccount.TenantId == requested tenant
  and LedgerAccount.IsActive
  and !LedgerAccount.IsDeleted
  and LedgerAccount.AccountType == Asset
  and TreasuryAccount.Type is allowed for the requested operation
```

The composite foreign key in `TreasuryAccountConfiguration.cs:59-63` binds mapping tenant and account ID to the same ledger-account tenant. Runtime predicates are still required because application code uses `IgnoreQueryFilters()` and because type, active state, and operation eligibility are not database-FK properties.

Operation-specific type boundaries are:

| Operation | Allowed type |
| --- | --- |
| Treasury balance/movement/account list | Cashbox, Bank, or Clearing |
| Generic transfer | Cashbox or Bank on both sides |
| Bank deposit | Cashbox source, Bank destination |
| Provider/carrier settlement | Clearing source, Bank destination through settlement evidence authority |
| Clearing aging report | Ledger `IsClearing`; report classification, not permission to transfer |

## 6. Flag Semantics

The three concepts are independent:

| Concept | Meaning | Authority use |
| --- | --- | --- |
| `Account.IsSystem` | Seeded/protected chart infrastructure; account cannot be freely renamed/deleted (`Account.cs:142-148`) | Not Treasury authority |
| `Account.IsClearing` | Accounting classification for money in transit (`Account.cs:150-154`) | Clearing reporting; insufficient by itself for Treasury operations |
| `TreasuryAccount` mapping | Tenant-owned operational Cashbox/Bank/Clearing view over a ledger account (`TreasuryAccount.cs:7-13`) | Treasury read and operation authority |

`TenantAccountSeeder.SeedAccountsAsync` explicitly sets every seeded chart account to `IsSystem=true` (`TenantAccountSeeder.cs:101-110`). Only six selected accounts receive Treasury mappings (`TenantAccountSeeder.cs:235-312`). Equating the two flags is therefore structurally invalid, not a shopixy data anomaly.

## 7. Live Treasury Mapping Set

All six shopixy mappings are active, non-deleted, same-tenant Asset accounts. No active mapping to an `IsSystem=false` account currently exists in any tenant, but the model permits such custom Treasury accounts.

| Code | Ledger account | Treasury type | Mapping ID | Balance |
| --- | --- | --- | --- | ---: |
| 1010 | Cashbox Main | Cashbox | `019f9f7b-16d3-7256-b4ee-49d1169d372a` | 73.92 |
| 1030 | Bank Account | Bank | `019f9f7b-16e6-71fa-bc13-3f1c7ea93b5b` | 10,410.10 |
| 1110 | Visa Clearing | Clearing | `019f9f7b-16e6-7e1e-88b5-ac2846d979e2` | 55.00 |
| 1120 | Stripe Clearing | Clearing | `019f9f7b-16e6-7643-ad70-dec6fbc01d47` | 0.00 |
| 1130 | COD Clearing | Clearing | `019f9f7b-16e6-7a5a-b622-857a6f3cc2e9` | 0.00 |
| 1140 | Wallet Clearing | Clearing | `019f9f7b-16e6-7e75-b176-1cf31e0949f7` | 0.00 |

## 8. Full Shopixy Account Catalog

`Balance` is ledger-derived and agrees with the current cache for every row shown. `BS` means eligible for Balance Sheet and shown when nonzero. `PL` means eligible for Profit and Loss when period activity exists. Cash Flow classification reflects the current code, not a recommendation.

| Code | Name | Type/N | Parent | System | Clearing | Treasury | Balance | BS | Current Cash Flow class | PL |
| --- | --- | --- | --- | --- | --- | --- | ---: | --- | --- | --- |
| 1000 | Cash and Bank | Asset/D | - | Yes | No | - | 0.00 | BS | Cash equivalent | - |
| 1010 | Cashbox Main | Asset/D | 1000 | Yes | No | Cashbox | 73.92 | BS | Cash equivalent | - |
| 1020 | POS Visa Account | Asset/D | 1000 | Yes | No | - | -3.80 | BS | Cash equivalent | - |
| 1030 | Bank Account | Asset/D | 1000 | Yes | No | Bank | 10,410.10 | BS | Cash equivalent | - |
| 1040 | Cash Float Reserve | Asset/D | 1000 | Yes | No | - | 0.00 | BS | Cash equivalent | - |
| 1100 | Clearing Accounts | Asset/D | - | Yes | No | - | 0.00 | BS | Cash equivalent | - |
| 1110 | Visa Clearing | Asset/D | 1100 | Yes | Yes | Clearing | 55.00 | BS | Cash equivalent | - |
| 1120 | Stripe Clearing | Asset/D | 1100 | Yes | Yes | Clearing | 0.00 | BS | Cash equivalent | - |
| 1130 | COD Clearing | Asset/D | 1100 | Yes | Yes | Clearing | 0.00 | BS | Cash equivalent | - |
| 1140 | Wallet Clearing | Asset/D | 1100 | Yes | Yes | Clearing | 0.00 | BS | Cash equivalent | - |
| 1310 | Inventory | Asset/D | - | Yes | No | - | 5.11 | BS | Inventory working capital | - |
| 1320 | Input Tax Receivable | Asset/D | - | Yes | No | - | 0.00 | BS | Inventory working capital | - |
| 1410 | Accounts Receivable | Asset/D | - | Yes | No | - | -110.00 | BS | Receivables working capital | - |
| 1420 | Marketplace Vendor Receivable | Asset/D | - | Yes | No | - | 1,000.01 | BS | Receivables working capital | - |
| 2010 | Supplier Payables | Liability/C | - | Yes | No | - | 4.01 | BS | Liability working capital | - |
| 2020 | Customer Deposits | Liability/C | - | Yes | No | - | 0.00 | BS | Liability working capital | - |
| 2030 | Treasury Suspense | Liability/C | - | Yes | No | - | 10,410.10 | BS | Liability working capital | - |
| 2040 | Marketplace Vendor Payable | Liability/C | - | Yes | No | - | 0.00 | BS | Liability working capital | - |
| 2050 | Sales Tax Payable | Liability/C | - | Yes | No | - | 0.00 | BS | Liability working capital | - |
| 2105 | Affiliate Payable | Liability/C | - | Yes | No | - | 120.00 | BS | Liability working capital | - |
| 3010 | Opening Balance Equity | Equity/C | - | Yes | No | - | 0.00 | BS | Financing | - |
| 4010 | POS Sales | Revenue/C | - | Yes | No | - | 22.22 | - | Via net profit | PL |
| 4020 | E-commerce Sales | Revenue/C | - | Yes | No | - | 1,000.01 | - | Via net profit | PL |
| 4030 | Shipping Revenue | Revenue/C | - | Yes | No | - | 0.00 | - | Via net profit | PL |
| 5010 | Cost of Goods Sold | COGS/D | - | Yes | No | - | 6.00 | - | Via net profit | PL |
| 6010 | Gateway Fees | Expense/D | - | Yes | No | - | 0.00 | - | Via net profit | PL |
| 6020 | Shipping Fees | Expense/D | - | Yes | No | - | 0.00 | - | Via net profit | PL |
| 6030 | Bank Fees | Expense/D | - | Yes | No | - | 0.00 | - | Via net profit | PL |
| 6100 | General Expenses | Expense/D | - | Yes | No | - | 0.00 | - | Via net profit | PL |
| 6110 | Affiliate Marketing Expense | Expense/D | 6100 | Yes | No | - | 120.00 | - | Via net profit | PL |
| 6120 | Accounts Receivable Adjustments | Expense/D | 6100 | Yes | No | - | 0.00 | - | Via net profit | PL |
| 6130 | Accounts Payable Adjustments | Expense/D | 6100 | Yes | No | - | 0.00 | - | Via net profit | PL |

## 9. T-05 Mismatch Proof

The current `financialreports/accounts` projection labels all 32 rows above as Treasury because all are system accounts. The canonical mapping set contains six. The 26 shopixy false positives are:

| Codes | Accounting role |
| --- | --- |
| `1000`, `1100` | Chart hierarchy/control headers |
| `1020`, `1040` | Unmapped cash-family control accounts |
| `1310`, `1320`, `1410`, `1420` | Inventory, tax, and receivable assets |
| `2010`, `2020`, `2030`, `2040`, `2050`, `2105` | Operating liabilities and suspense |
| `3010` | Equity |
| `4010`, `4020`, `4030` | Revenue |
| `5010` | Cost of goods sold |
| `6010`, `6020`, `6030`, `6100`, `6110`, `6120`, `6130` | Expenses and expense hierarchy |

Every one has `IsSystem=true` and no active Treasury mapping. The same shape exists in all six tenants: 32 accounts, six mappings, and 26 system-without-mapping mismatches. There are currently no mapping-without-system rows, so the live defect presents as false positives; future custom mapped accounts could also become false negatives.

## 10. Authority Consumer Matrix

| Consumer | Current predicate/source | Assessment |
| --- | --- | --- |
| `FinancialReportService.GetAccountSummariesAsync` (`FinancialReportService.cs:602-637`) | `IsTreasury = a.IsSystem` | **Confirmed T-05 defect** |
| `TreasuryReadService.EnsureAccountExistsAsync`, balance, bulk balance, movements (`TreasuryReadService.cs:36-223`) | Active, non-deleted same-tenant mapping; same-tenant active Asset | Correct source; linked-account soft-delete is not explicit after `IgnoreQueryFilters()` |
| `TreasuryReadService.GetDashboardAsync` cash/bank (`TreasuryReadService.cs:245-283`) | Active mapping and same-tenant active Asset | Correct source; mapping/account soft-delete checks are missing after `IgnoreQueryFilters()` |
| `TreasuryReadService.GetDashboardAsync` pending clearing (`TreasuryReadService.cs:335-349`) | All active ledger accounts with `IsClearing` | Accounting classification, but bypasses Treasury mapping consistency |
| `TreasuryReadService.GetTreasuryAccountsAsync` (`TreasuryReadService.cs:355-395`) | Active same-tenant mapping and same-tenant active Asset | Correct source; mapping/account soft-delete checks are missing |
| `TreasuryOperationService.EnsureTreasuryAccountAsync` (`TreasuryOperationService.cs:298-324`) | Active, non-deleted mapping; Cashbox/Bank; same-tenant active Asset | Correct operational source; linked-account soft-delete is not explicit |
| `TreasuryTransferService.RequireTreasuryTypeAsync` (`TreasuryTransferService.cs:202-228`) | Active, non-deleted mapping; operation type; same-tenant active Asset | Correct operational source; linked-account soft-delete is not explicit |
| `FinancialPostingService` cash gates (`FinancialPostingService.cs:614-751`) | Active, non-deleted Cashbox/Bank mappings over request account IDs | Correct source for cash authority; account tenant/activity is checked elsewhere, but account soft-delete/type is not explicit in this predicate |
| `CashboxLedgerAccountService` | Cashbox mappings plus operational cashbox reference | Provisioning authority; several `IgnoreQueryFilters()` lookups omit soft-delete/account eligibility checks |
| `FinancialIntegrityService.ValidateLegacySharedCashAccountBalancesAsync` | Query-filtered active Cashbox mappings | Diagnostic consumer; tenant/delete filters are ambient rather than explicit |
| `TenantAccountSeeder` | Explicit fixed-code creation of six mappings | Provisioning producer, not runtime classification |

The mutation-critical transfer services already reject `1020` because it has no mapping. The broadest immediate behavioral defect is therefore incorrect API metadata, while predicate drift remains a hardening risk.

## 11. T-05 Exposure And Test Gap

`GET /api/financialreports/accounts` is an active endpoint (`FinancialReportsController.cs:225-238`) and returns the incorrect metadata. The frontend exports `getAccountSummaries` (`financialReportsApi.js:237-249`) but no current page calls it, limiting present UI exposure. External/API consumers can still trust the false flag.

No test asserts `AccountSummary.IsTreasury` against mappings. Existing tests prove tenant isolation and that an unmapped non-Treasury account cannot become balance/movement read authority, but they do not cover inactive/deleted mappings, deleted linked accounts, or a valid custom mapping with `IsSystem=false`.

The bounded correction should derive active Treasury account IDs from `TreasuryAccounts`, not change `IsSystem`. Required tests are: unmapped system account is false; mapped non-system Asset is true; inactive, deleted, foreign-tenant, inactive-account, deleted-account, and non-Asset mappings are false.

## 12. Account 1020 Identity And Creation Provenance

| Property | Value |
| --- | --- |
| Account ID | `019f9f7b-1567-701b-bc70-7deda2e00781` |
| Code/name | `1020 POS Visa Account` |
| Description | `POS card terminal transactions` |
| Parent | `1000 Cash and Bank` |
| Type/normal side | Asset/Debit |
| Flags | System=true, Clearing=false, PreventNegative=false, Active=true, Deleted=false |
| Created | `2026-07-26T17:31:11.591719Z` |
| Treasury mapping | None |
| PaymentMethod mapping | None |

`SystemAccountTemplates.PosVisaAccount` defines this exact Asset template (`SystemAccountTemplate.cs:104-115`) and `AccountCodes.PosVisaAccount` fixes its code as `1020` (`IAccountResolver.cs:44-54`). Both originate in initial commit `3fb3d5ccb94db256c8b98ea5e9449d97b02f352e` dated 2026-07-26. `TenantAccountSeeder.SeedAccountsAsync` creates it for each tenant and the startup seeder reruns account seeding for all active tenants (`ChartOfAccountsStartupSeeder.cs:33-64`). Tenant creation and registration invoke the full seeder (`TenantsController.cs:134-147`; `TenantRegistrationService.cs:509-522`).

## 13. Account 1020 Posting Authority

The active posting path is:

```text
Purchase create/update
  -> PurchasePaymentSplitter resolves only CashAmount/CardAmount
  -> AccountingOutboxService emits PosPurchase/PurchaseReversal
  -> AccountingOutboxProcessor reloads and locks the PurchaseInvoice
  -> CommerceAccountingService.PostPosPurchaseAsync
  -> credit AccountCodes.PosVisaAccount for CardAmount
  -> FinancialPostingService appends balanced immutable journal/lines
```

`PurchasePaymentSplitter` has no bank, card instrument, or liability account identifier (`PurchasePaymentSplitter.cs:23-106`). Bank Transfer is rejected for this purchase flow. `CommerceAccountingService.cs:312-353` resolves the single fixed `1020` account whenever card amount is positive and credits it. Consequently, the persisted word `Card` proves tender type but not whether the real source was a debit card tied to Bank or a credit-card liability.

No active customer sale, refund, gateway settlement, COD settlement, supplier payment, or CommerceHub posting uses `1020`. Customer card collection uses `1110 Visa Clearing`; certified settlement credits provider clearing and debits `1030 Bank`.

## 14. Complete 1020 Ledger Lineage

All eight shopixy lines are shown below. `Effect` is the normal Asset balance effect.

| Journal | Status | Source/reference | Line | Debit | Credit | Effect | BalanceAfter |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: |
| `JE-2026-000047` | Posted | PurchaseInvoice / `PUR-20260910-1000` | Card payment | 0.00 | 1.10 | -1.10 | -1.10 |
| `JE-2026-000048` | Reversed | PurchaseInvoice / `PUR-20260910-1001` | Card payment | 0.00 | 1.10 | -1.10 | -2.20 |
| `JE-2026-000051` | Posted | PurchaseInvoice / `PUR-20260910-1004` | Card payment | 0.00 | 1.10 | -1.10 | -3.30 |
| `JE-2026-000052` | Posted | PurchaseInvoice / `PUR-20260910-1005` | Card payment | 0.00 | 0.50 | -0.50 | -3.80 |
| `JE-2026-000053` | Reversed | PurchaseInvoice / `PUR-20260910-1001` | Card payment | 0.00 | 1.10 | -1.10 | -4.90 |
| `JE-2026-000054` | Posted | Reversal / `JE-2026-000048` | Reversal: Card payment | 1.10 | 0.00 | +1.10 | -3.80 |
| `JE-2026-000055` | Posted | Reversal / `JE-2026-000053` | Reversal: Card payment | 1.10 | 0.00 | +1.10 | -2.70 |
| `JE-2026-000056` | Posted | PurchaseInvoice / `PUR-20260910-1001` | Card payment | 0.00 | 1.10 | -1.10 | -3.80 |
| **Total** | | | | **2.20** | **6.00** | **-3.80** | **-3.80** |

Reversed journals remain in immutable history and are compensated by separate posted reversal journals, so both statuses are included in ledger-derived reporting. The cache and ledger-derived normal balance both equal EGP -3.80.

## 15. Source Documents And Outbox Proof

The current net balance corresponds exactly to four live supplier purchase card portions:

| Purchase | ID | Method | Total | Paid | Outbox split | Current net 1020 credit |
| --- | --- | --- | ---: | ---: | --- | ---: |
| `PUR-20260910-1000` | `9f507b53-faba-46ec-9f6e-0dec01130b80` | Mixed | 2.20 | 2.20 | Cash 1.10, Card 1.10 | 1.10 |
| `PUR-20260910-1001` | `a3137eca-32b1-48a1-b584-f2fe3960c5e5` | Mixed | 2.20 | 2.20 | Cash 1.10, Card 1.10 | 1.10 |
| `PUR-20260910-1004` | `58f26b9b-36c2-4a59-9b23-202ec2ecba19` | Card | 1.10 | 1.10 | Cash 0.00, Card 1.10 | 1.10 |
| `PUR-20260910-1005` | `94b43c19-4fba-42a5-9489-64a2e925c483` | Credit with partial Card payment | 1.10 | 0.50 | Cash 0.00, Card 0.50 | 0.50 |
| **Total** | | | | | | **3.80** |

Six processed outbox messages carry the four initial postings and two `PUR-20260910-1001` mutation/repost cycles. Their payloads explicitly carry the cash/card splits and their `JournalId` values link to the resulting posting journals. No failed or orphan source is needed to explain the balance.

The counterpart journals debit Inventory and, where applicable, credit Cashbox or Supplier Payables. Every journal is balanced. This is not opening state, cache-only residue, provider settlement, migration balance seed, or a legacy transaction.

## 16. Cross-Tenant And Legacy Evidence

All six tenants have an active system Asset account `1020` and no Treasury mapping. Only shopixy has any `1020` ledger activity or balance; the other five accounts have zero lines and zero balance.

Shopixy legacy finance state contains two zero-balance `FinancialAccounts`: `Visa Account` (`1200`, `VISA-001`) and `Cash` (`1100`, `CASH-001`). There are zero `FinancialTransactions`, `VisaTransactions`, and `TreasuryTransactions`. The active legacy-adapter endpoint maps any legacy FinancialAccount whose name contains `Visa` to ledger `1020` (`FinancialAccountsController.cs:334-358`), but the subsequent Treasury transfer service rejects `1020` because it lacks a Treasury mapping. This is reachable legacy compatibility code with a contradictory, nonfunctional Visa transfer path; it is not evidence that `1020` should be mapped.

There are also zero shopixy settlement batches/orders. No hidden FK maps `1020` to payment or settlement authority: ledger-account references are limited to hierarchy, ledger lines, payment-method target/clearing/fee links, and Treasury mappings.

## 17. Comparison With 1110 Visa Clearing

| Property | `1020 POS Visa Account` | `1110 Visa Clearing` |
| --- | --- | --- |
| Accounting use | Supplier purchase card funding credit | Customer card receivable pending settlement |
| Balance | -3.80 | 55.00 |
| Ledger direction | Operational credits; debits are reversals only | Sales debits and return/settlement credits |
| `IsClearing` | No | Yes |
| Treasury mapping | None | Active Clearing mapping |
| Payment method mapping | None | `CARD` clearing account |
| Provider metadata | None | Gateway `visa`, expected settlement 3 days |
| Correct Treasury role | None established | Clearing |

The system payment methods prove the current distinction: `CARD -> 1110`, `CASH -> 1010`, `BANK_TRANSFER -> 1030`, `COD -> 1130`, `STRIPE -> 1120`, and `WALLET -> 1140`. Mapping `1020` as another Visa Clearing account would duplicate and contradict this configured authority.

## 18. Report, API, Workflow, And UI Impact

| Surface | 1020 behavior | Impact |
| --- | --- | --- |
| Trial Balance | Included with debits 2.20, credits 6.00, balance -3.80 | Correctly preserves double-entry totals |
| General Ledger | All eight lines and running balances are available by account ID | Correct historical evidence |
| Balance Sheet | Included as current Asset -3.80 | Reduces total/current assets and affects current ratio; classification is economically questionable |
| Profit and Loss | Excluded because it is an Asset | No direct P&L line |
| Cash Flow | Included in opening/closing cash because codes starting `10` or `11` are cash equivalents (`FinancialReportService.cs:774-776`) | Closing cash is reduced by 3.80 even though 1020 is not proven liquid Treasury authority |
| Financial dashboard | Inherits Balance Sheet total assets/current ratio | Includes the -3.80 Asset classification |
| Clearing Aging | Excluded because `IsClearing=false` | Correct for current classification |
| Treasury account list/balances/movements | Excluded or returns no Treasury result because no mapping | Correct authority behavior |
| Treasury cash/bank dashboard | Excluded from mapped Cashbox/Bank totals | Correct authority behavior, inconsistent with Cash Flow boundary |
| Treasury Hub | Uses fixed mapped codes 1010, 1030, 1110-1140 | Does not expose 1020 |
| Account viewer | Can format a 1020 route name, but backend Treasury reads reject the unmapped code | No authoritative 1020 view |
| Generic transfer | Rejected by backend mapping/type gates | Correct; legacy Visa adapter is unusable |
| Daily Close | Card collection uses 1110/1120/1030, not 1020 | No 1020 impact |

The mismatch is therefore not missing history. It is inconsistent classification boundaries: financial reports treat `1020` as cash by code/type, while Treasury correctly excludes it by mapping.

## 19. Reference Classification

| Status | References | Meaning |
| --- | --- | --- |
| ACTIVE | `AccountCodes.PosVisaAccount`; `CommerceAccountingService.PostPosPurchaseAsync`; outbox purchase processing | Creates all future supplier card credits to 1020 |
| ACTIVE PROVISIONING | `SystemAccountTemplates.PosVisaAccount`; `TenantAccountSeeder`; startup/tenant seed callers | Creates 1020 for every tenant |
| ACTIVE LEGACY ADAPTER | `FinancialAccountsController.ResolveLedgerTransferAccountCodeAsync` | Maps legacy Visa name to 1020, then fails Treasury authority validation |
| ACTIVE REPORTING | Trial Balance, GL, Balance Sheet, Cash Flow, financial dashboard | Reports immutable 1020 activity and applies current Asset/code semantics |
| DEAD FRONTEND CONSTANT | `treasuryApi.ACCOUNT_CODES.VISA_ACCOUNT` and display name | Declared but no current component consumes this code |
| TEST FIXTURE | `TreasuryTransferServiceTests` and `FinancialPostingServiceTests` use numeric `1020` as an arbitrary Bank code | Misleading fixture, not production semantic authority |
| TEST GUARD | `GlobalFinancialIntegrityHardeningTests` forbids direct 1020 posting in `PurchaseFinancialIntegrityService` | Preserves outbox/commerce posting boundary; does not forbid active Commerce posting |
| CURRENT DOCUMENTATION | `.ai/hermes/business-flows/FLOW-04-PURCHASE-INVOICE.md` | Documents supplier purchase credit to 1020 |
| STALE/CONFLICTING DOCUMENTATION | `UNIFIED-FINANCIAL-ENGINE-ARCHITECTURE.md` | Describes 1020 as settled customer card proceeds, which active settlement code does not do |
| ARCHIVED SEED | Recovery SQL chart seed | Historical provisioning evidence only |

## 20. Remediation And Safety Plan

The smallest safe sequence is:

1. Fix only `AccountSummary.IsTreasury` to derive from the canonical active same-tenant mapping predicate. Add positive and negative predicate tests.
2. Normalize all `IgnoreQueryFilters()` Treasury consumers to explicitly reject deleted mappings and deleted/inactive/non-Asset linked accounts. Preserve operation-specific type restrictions.
3. Keep `1020` unmapped. Do not expose it as Cashbox, Bank, or Clearing, and do not add `PreventNegative` merely to mask its existing balance.
4. Require an explicit supplier card funding authority in the purchase model/workflow. A debit/bank card must resolve to an actual Bank Treasury account; a credit card must resolve to a dedicated Liability/control account. Do not infer this from the word `Card`.
5. Stop future postings to `1020` only after the replacement authority is deployed and replay/idempotency behavior is covered.
6. Obtain approved bank/card evidence for the four shopixy purchases. Reclassify EGP 3.80 through a dated balanced journal: debit 1020 and credit the evidenced Bank or card Liability. Do not edit existing journals, lines, or cached balance directly.
7. After every tenant has zero 1020 balance and no pending/replay path can post it, deactivate it for new entries and remove it from new-tenant seeding. Preserve the historical account and GL.
8. Replace or remove the legacy FinancialAccount Visa adapter and stale frontend/documentation references as part of the same compatibility decision.
9. Change Cash Flow cash-equivalent authority from broad `10xx`/`11xx` prefixes to an explicit accounting policy aligned with mapped liquid Cashbox/Bank accounts and separately justified clearing treatment.

No reclassification journal can be selected safely from current data alone. Choosing Bank versus Liability without instrument evidence would invent a financial fact.

## 21. Closure

| Gate | Result |
| --- | --- |
| Tenant and read-only boundary explicit | PASS |
| Full 32-account catalog captured | PASS |
| All 26 shopixy T-05 mismatches identified | PASS |
| Canonical Treasury authority established | PASS |
| 1020 creation and dependency provenance established | PASS |
| Every 1020 line and reversal traced | PASS |
| Cache equals immutable ledger for 1020 | PASS, -3.80 |
| Source documents/outbox explain balance | PASS |
| 1020 proven to be customer clearing or settled proceeds | NO |
| Mapping 1020 into Treasury safe | NO |
| Immediate retirement safe | NO, active purchase posting remains |
| Reclassification target determinable without external evidence | NO |
| Financial/business data mutated | NO |
| Application code changed | NO |

**T-05 discovery is closed:** `IsSystem` is not Treasury authority; active same-tenant `TreasuryAccount` mapping is.

**T-06 discovery is closed with an implementation dependency:** EGP -3.80 is valid ledger-backed supplier purchase card-funding activity, but the account is economically under-specified and structurally misclassified as a cash Asset. Keep it out of Treasury, establish the real funding instrument, redirect future posting, reclassify by journal, and only then retire it from active use.

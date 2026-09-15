# Treasury Phase 1 Architecture Audit - 2026-09-12

## 1. Executive Decision

**Verdict: FAIL.** The architecture has strong ledger, tenant, idempotency, and money-movement controls, but the live `shopixy` tenant does not satisfy the Treasury quality gate.

Confirmed blockers:

| Severity | Finding |
| --- | --- |
| Critical | Five `shopixy` accounts have `LedgerAccounts.CurrentBalance` values that do not equal their posted ledger balance. Total absolute drift is EGP 4,240.00. Treasury reads and insufficient-funds controls consume this cache. |
| High | Treasury reports EGP 2,095.00 pending clearing while ledger-derived Clearing Aging reports EGP 55.00. The EGP 2,040.00 difference is unjournaled COD and Wallet cache state. |
| High | `shopixy` Cash Flow returns `reconciles=false`; net cash change is EGP 10,415.22 against an actual closing movement of EGP 10,535.22. |
| High | Clearing Aging advertises settlement to Cashbox, but sends a clearing account through a generic transfer path that accepts only Cashbox/Bank accounts. The separate manual clearing endpoint is intentionally disabled. |
| Medium | Repeated authenticated hard loads left most Treasury/report pages in `Loading...` and emitted two `401` responses, although the session and some API calls remained valid. |

No financial mutation, settlement, transfer, journal rewrite, or direct SQL business-data change was made during this audit.

## 2. Scope And Method

The audit inspected implementation, EF schema and migrations, PostgreSQL records, authenticated APIs, health endpoints, frontend routes, localization, and browser behavior. Narrative documents were used only as supporting indexes.

Controls used:

| Control | Result |
| --- | --- |
| PostgreSQL access | Repeatable-read transaction with `SET TRANSACTION READ ONLY`; query allow-list accepted only `SELECT` or `WITH`. |
| API access | Read-only GETs with a development JWT scoped to `shopixy`. The development token issuer updates test-session identity state but no financial records. |
| Browser access | Real POS Admin cookie login for `greatone`; no submit, transfer, settlement, export, or mutation action was used. |
| Runtime | API `5010`, POS Web `5173`, Storefront `5174`, CommerceHub `5175`. |
| Database | `PosEcommerceDb_Baseline`. |
| Audit window | Records through 2026-09-12; report/API period 2026-07-01 through 2026-09-12 where stated. |

## 3. Tenant And Evidence Boundaries

Two tenants were observed and are not combined:

| Evidence set | Tenant | Tenant ID | Currency | Use |
| --- | --- | --- | --- | --- |
| Database and direct API | `shopixy` | `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` | EGP | Main accounting and Treasury audit. |
| POS Admin browser | `greatone` | `c43aebf2-4cc4-406a-a2db-4df06d40bce5` | EGP | UI, session, RTL, and responsive observation only. |

The supplied POS Admin identity belongs to `greatone`. Browser values from that session are not evidence for `shopixy` balances. Credentials are intentionally not retained in this report.

The database contains six tenants. Each has 32 ledger accounts and four clearing accounts. Cached-balance drift found by the audit is currently confined to `shopixy`.

## 4. Financial Source Of Truth

The intended hierarchy is sound:

1. Current SQL business rows authorize whether a financial fact may be posted.
2. `AccountingOutboxMessages` durably carries asynchronous posting intent where the source workflow uses the outbox.
3. `FinancialPostingService` creates immutable balanced journals and ledger lines.
4. `LedgerJournalEntries` and `LedgerLines` are internal accounting/reporting authority.
5. `LedgerAccounts.CurrentBalance` is a denormalized cache and is required by its entity invariant to equal the ledger sum.
6. `TreasuryAccounts` declares which asset accounts have physical Cashbox, Bank, or Clearing presentation/operation authority.
7. Reports, dashboards, exports, caches, and browser state are derivative.

Evidence: `Account` declares `CurrentBalance always equals the sum of ledger entries` and updates it only through posting/opening methods (`PointOfSale.Core/Entities/Accounting/Account.cs:8-16`, `:94-138`). Reports explicitly use the immutable ledger only (`PointOfSale.Infrastructure/Accounting/FinancialReportService.cs:13-24`).

## 5. Persistence And Schema Model

| Object | Responsibility |
| --- | --- |
| `LedgerAccounts` | Tenant-owned chart, classification, flags, hierarchy, clearing metadata, cached balance. |
| `LedgerJournalEntries` | Immutable journal header, source identity/reference, unique tenant idempotency key, reversal links. |
| `LedgerLines` | Debit/credit lines and posting-time `BalanceAfter`. |
| `TreasuryAccounts` | Active physical Treasury authority linked to a same-tenant asset account. |
| `PaymentMethods` | Immediate target or deferred clearing account plus optional fee account. |
| `AccountingOutboxMessages` | Durable asynchronous accounting intent, retry, processing, and failure state. |
| `PaymentSettlementBatches` / `PaymentSettlementOrders` | Durable gateway/COD settlement header and current order membership authority. |

Latest applied migration observed: `20260911190000_POS_TaxAccountsAndPartyBalanceRepair`.

Legacy `FinancialAccounts` still contains zero-balance `1100 Cash` and `1200 Visa Account`. Legacy `Cashboxes`, `CashboxTransactions`, `TreasuryTransactions`, and `VisaTransactions` contain zero rows. They are not current Treasury authority.

## 6. Shopixy Chart Of Accounts Catalog

All 32 accounts are active and system seeded. Arabic names are populated. `D` means debit normal and `C` means credit normal.

| Code | Name | Type/Normal | Parent | Flags | Cached | Ledger | Drift |
| --- | --- | --- | --- | --- | ---: | ---: | ---: |
| 1000 | Cash and Bank | Asset/D | - | Parent | 0.00 | 0.00 | 0.00 |
| 1010 | Cashbox Main | Asset/D | 1000 | Treasury Cashbox, no-negative | 73.92 | 73.92 | 0.00 |
| 1020 | POS Visa Account | Asset/D | 1000 | Unmapped legacy-style system account | -3.80 | -3.80 | 0.00 |
| 1030 | Bank Account | Asset/D | 1000 | Treasury Bank, no-negative | 10,410.10 | 10,410.10 | 0.00 |
| 1040 | Cash Float Reserve | Asset/D | 1000 | No-negative | 0.00 | 0.00 | 0.00 |
| 1100 | Clearing Accounts | Asset/D | - | Parent | 0.00 | 0.00 | 0.00 |
| 1110 | Visa Clearing | Asset/D | 1100 | Treasury Clearing, visa, 3 days | 55.00 | 55.00 | 0.00 |
| 1120 | Stripe Clearing | Asset/D | 1100 | Treasury Clearing, stripe, 7 days | 0.00 | 0.00 | 0.00 |
| 1130 | COD Clearing | Asset/D | 1100 | Treasury Clearing, cod, 14 days | 400.00 | 0.00 | **400.00** |
| 1140 | Wallet Clearing | Asset/D | 1100 | Treasury Clearing, wallet, 3 days | 1,640.00 | 0.00 | **1,640.00** |
| 1310 | Inventory | Asset/D | - | - | 5.11 | 5.11 | 0.00 |
| 1320 | Input Tax Receivable | Asset/D | - | - | 0.00 | 0.00 | 0.00 |
| 1410 | Accounts Receivable | Asset/D | - | - | -110.00 | -110.00 | 0.00 |
| 1420 | Marketplace Vendor Receivable | Asset/D | - | - | 1,000.01 | 1,000.01 | 0.00 |
| 2010 | Supplier Payables | Liability/C | - | - | 4.01 | 4.01 | 0.00 |
| 2020 | Customer Deposits | Liability/C | - | - | 0.00 | 0.00 | 0.00 |
| 2030 | Treasury Suspense | Liability/C | - | - | 10,410.10 | 10,410.10 | 0.00 |
| 2040 | Marketplace Vendor Payable | Liability/C | - | No-negative | 0.00 | 0.00 | 0.00 |
| 2050 | Sales Tax Payable | Liability/C | - | - | 0.00 | 0.00 | 0.00 |
| 2105 | Affiliate Payable | Liability/C | - | No-negative | 200.00 | 120.00 | **80.00** |
| 3010 | Opening Balance Equity | Equity/C | - | - | 0.00 | 0.00 | 0.00 |
| 4010 | POS Sales | Revenue/C | - | - | 22.22 | 22.22 | 0.00 |
| 4020 | E-commerce Sales | Revenue/C | - | - | 3,040.01 | 1,000.01 | **2,040.00** |
| 4030 | Shipping Revenue | Revenue/C | - | - | 0.00 | 0.00 | 0.00 |
| 5010 | Cost of Goods Sold | COGS/D | - | - | 6.00 | 6.00 | 0.00 |
| 6010 | Gateway Fees | Expense/D | - | - | 0.00 | 0.00 | 0.00 |
| 6020 | Shipping Fees | Expense/D | - | - | 0.00 | 0.00 | 0.00 |
| 6030 | Bank Fees | Expense/D | - | - | 0.00 | 0.00 | 0.00 |
| 6100 | General Expenses | Expense/D | - | Parent | 0.00 | 0.00 | 0.00 |
| 6110 | Affiliate Marketing Expense | Expense/D | 6100 | - | 200.00 | 120.00 | **80.00** |
| 6120 | Accounts Receivable Adjustments | Expense/D | 6100 | - | 0.00 | 0.00 | 0.00 |
| 6130 | Accounts Payable Adjustments | Expense/D | 6100 | - | 0.00 | 0.00 | 0.00 |

## 7. Treasury Account Authority

`shopixy` has exactly six active `TreasuryAccounts` mappings:

| Ledger code | Treasury display | Type | Default | Cached/returned balance |
| --- | --- | --- | --- | ---: |
| 1010 | Main Cashbox | Cashbox | Yes | 73.92 |
| 1030 | Primary Bank Account | Bank | Yes | 10,410.10 |
| 1110 | Visa Clearing | Clearing | No | 55.00 |
| 1120 | Stripe Clearing | Clearing | No | 0.00 |
| 1130 | COD Clearing | Clearing | No | 400.00 |
| 1140 | Wallet Clearing | Clearing | No | 1,640.00 |

Account `1020 POS Visa Account` is active, has a negative EGP 3.80 balance, and has no `TreasuryAccount`. It therefore appears in ledger reports and Cash Flow but not in Treasury accounts, balances, dashboard liquid cash, or account viewers.

`GetAccountSummariesAsync` labels `IsTreasury` from `Account.IsSystem`, not from `TreasuryAccounts` (`FinancialReportService.cs:610-635`). Since all baseline accounts are system accounts, the account-summary API misclassifies the entire chart as Treasury.

## 8. Payment Method Routing

The seeded routing is implementation-backed (`TenantAccountSeeder.cs:158-211`):

| Method | Recognition | Debit target | Fee account |
| --- | --- | --- | --- |
| `CASH` | Immediate | 1010 Cashbox | - |
| `CARD` | Deferred | 1110 Visa Clearing | 6010 Gateway Fees |
| `STRIPE` | Deferred | 1120 Stripe Clearing | 6010 Gateway Fees |
| `COD` | Deferred | 1130 COD Clearing | - |
| `WALLET` | Deferred | 1140 Wallet Clearing | 6010 Gateway Fees |
| `BANK_TRANSFER` | Immediate | 1030 Bank | - |

POS `PaymentMethod` enum values are Cash `0`, Card `1`, BankTransfer `2`, Mixed `3`, and Credit `4`. This enum is separate from Treasury transaction display filter integers in `TreasuryController`, where `1` means Bank, `2` Visa, `3` Supplier, and `4` Customer.

## 9. End-To-End Financial Graph

```text
Business workflow / payment lifecycle
          |
          | current tenant-owned SQL authority
          v
Accounting transaction boundary or accounting outbox
          |
          | replay-safe source identity + idempotency key
          v
AccountingOutboxProcessor -> CommerceAccountingService
          |
          v
FinancialPostingService
          |
          | serializable transaction
          | validate balanced lines and source authority
          | update CurrentBalance + append Journal + LedgerLines
          v
PostgreSQL immutable ledger
          |
          +--> Financial reports recompute from LedgerLines
          +--> Treasury reads use CurrentBalance for balances
          +--> Treasury movements use LedgerLines and BalanceAfter
          +--> POS Admin presentation and exports
```

This graph explains the live contradiction: report branches recompute from journal lines, while Treasury balance branches trust the drifted cache.

## 10. Posting Source Matrix

| Source type | Primary accounting effect | Current SQL authority | Shopixy evidence |
| --- | --- | --- | --- |
| SalesInvoice | Cash/clearing/AR to POS revenue/tax | Completed invoice and paid amount | 5 journals, including 1 reversed original |
| SalesInvoice_COGS | COGS to inventory | Sale lines/cost snapshot | 5 journals, including 1 reversed original |
| PurchaseInvoice | Inventory/tax to cash/clearing/AP | Completed purchase and paid amount | 8 journals, including 2 reversed originals |
| PurchaseReturn | AP/cash effect and inventory/tax reversal | Current purchase return | 1 posted |
| Return | Revenue/tax reversal to cash/card/customer credit | Current sales return and explicit refund allocation | 3 posted |
| Return_COGS | Inventory to COGS reversal | Current sales return cost evidence | 3 posted |
| CustomerPayment | Cash/card to AR | Current tenant customer payment amount | Present in ledger records reviewed separately |
| SupplierPayment | AP to cash/card | Current tenant supplier payment amount | Present in ledger records reviewed separately |
| Order_Payment | Cash/clearing recognition | Current order payment status | Supported by code |
| Order_COD | COD clearing to e-commerce revenue | Delivered/collected order | 3 posted, EGP 1,230.00 |
| Order_Wallet | Wallet clearing to e-commerce revenue | Current order payment state | Supported by code |
| Order_Refund | Revenue reversal/refund funding | Current `OrderRefund` rows | 4 posted, EGP 1,230.00 |
| GatewaySettlement | Bank + fees from gateway clearing | Current non-deleted batch and order lineage | No batch in `shopixy` |
| CODSettlement | Bank + fees from COD clearing | Current non-deleted batch and order lineage | No batch in `shopixy` |
| Expense | Expense to approved funding source | Current approved expense | Supported by code |
| AffiliateCommissionEarned | Affiliate expense to payable | Commission/order authority | 3 posted, EGP 120.00 |
| AffiliateCommissionPaid | Payable to cash | Current payout allocation | Supported by code |
| CommerceHubFinancialTransaction | CommerceHub cash/receivable/payable effects | Current immutable CommerceHub financial transaction | 14 posted, EGP 12,570.12 |
| Reversal | Exact opposite of original journal | Existing journal and reversal authorization | 4 posted reversal journals |

The `shopixy` source summary contains 53 journals: 49 currently posted and 4 original journals marked reversed. Each source sample inspected has a source reference and idempotency key.

## 11. Posting Atomicity And Controls

`FinancialPostingService` provides strong controls:

| Control | Evidence |
| --- | --- |
| Idempotency | Pre-transaction and in-transaction checks bind tenant, source, date, and exact line payload (`FinancialPostingService.cs:51-78`, `:103-121`, `:526-558`). |
| Isolation | New posting transactions use serializable isolation (`:81-98`). |
| Tenant scope | All account IDs must resolve within the request tenant (`:126-143`, `:443-458`). |
| Balance | Total debits must equal total credits (`:437-441`). |
| Account state | Inactive and missing accounts are rejected (`:138-152`, `:460-464`). |
| Negative prevention | Projected cached balance is checked for `PreventNegative` accounts (`:562-611`). |
| Atomic write | Cache update, journal header, ledger lines, and `BalanceAfter` are saved in one transaction (`:188-234`). |
| Reversal | Posted history is compensated with a new reversal journal; it is not edited in place. |

The critical qualification is that negative prevention uses `CurrentBalance`. A stale positive cache can authorize an outflow the ledger would not support; a stale low cache can reject a valid outflow.

## 12. Outbox, Replay, And Reversal

`shopixy` has 35 accounting outbox rows. All are processed and none are failed. The accounting readiness health check reports healthy.

The processor:

| Behavior | Result |
| --- | --- |
| Dispatch | Maps event type to the authoritative commerce/accounting posting method. |
| Replay | Checks journal idempotency before dispatch for supported events. |
| Concurrency | Uses relational row locking for source reload paths. |
| Mutation updates | Reverses prior active sale/purchase/expense journals before reposting approved source mutations. |
| Failure | Retains retry/failure state rather than silently deleting critical intent. |

No pending outbox explains the five cache discrepancies. The evidence instead indicates pre-ledger opening state: affected accounts were created on 2026-07-26, while their first journals begin on 2026-08-28 or later.

## 13. Money Movement Authority

The posting engine revalidates net physical-cash movements against current SQL (`FinancialPostingService.cs:614-733`, `:735-872`).

Authorized inflows include completed POS sales, collected/settled orders, customer payments, valid settlement batches, qualifying CommerceHub transactions, and reversals. Authorized outflows include completed purchases, supplier payments, sales returns, approved expenses, current order refunds, qualifying CommerceHub refund/payout transactions, affiliate payout allocations, and reversals.

Direct Treasury expense, cash-in, and cash-out service calls are disabled. `POST /api/treasury/operations/cash-in` returns `410 Gone`; expense and cash-out reject through `TreasuryOperationService`. This is intentional containment: operators cannot manufacture a journal by selecting arbitrary offset accounts.

Generic Cashbox/Bank transfer operations remain available to Owner/Admin/Manager and create balanced journals, but both sides must have active same-tenant Cashbox or Bank Treasury authority.

## 14. Transfer And Settlement Authority

There are two transfer services with different DTO styles:

| Path | Authority | Allowed accounts | Status |
| --- | --- | --- | --- |
| `/api/treasury/operations/transfer` | `TreasuryOperationService` | Cashbox or Bank on both sides | Active UI path |
| `/api/admin/accounting/treasury/transfer` | `TreasuryTransferService` | Cashbox or Bank on both sides | Admin-only |
| `/api/admin/accounting/treasury/deposit` | `TreasuryTransferService` | Cashbox to Bank | Admin-only |
| `/api/admin/accounting/treasury/settle` | Controller block | None | `410 SETTLEMENT_AUTHORITY_REQUIRED` |
| Payment lifecycle settlement | `PaymentSettlementBatch` + current `PaymentSettlementOrders` + accounting authority | Clearing to Bank with fees | Certified path |

`TreasuryTransferService.SettleClearingAsync` exists, but the public admin settlement endpoint does not invoke it. That containment is consistent with the architecture rule that manual assumptions cannot replace provider/order settlement evidence.

## 15. Clearing State Machines

Expected deferred-payment state machine:

```text
Order payment accepted/collected
  -> debit method clearing account
  -> credit revenue/tax/payable as applicable
  -> clearing is Pending
  -> provider/carrier settlement evidence accepted by PaymentLifecycleService
  -> persist PaymentSettlementBatch and current PaymentSettlementOrders
  -> debit Bank for gross less fees
  -> debit Gateway Fees when applicable
  -> credit Clearing for gross
  -> order payment state becomes Settled
  -> clearing returns toward zero
```

Refund behavior branches on whether settlement occurred. Pre-settlement refunds reverse the relevant clearing funding. Post-settlement refunds require current settlement-batch membership and fund from bank authority.

Current `shopixy` state:

| Clearing | Cached state | Ledger state | Settlement-batch evidence |
| --- | ---: | ---: | --- |
| Visa 1110 | Pending 55.00 | Pending 55.00 | None yet |
| Stripe 1120 | Clear 0.00 | Clear 0.00 | None |
| COD 1130 | Pending 400.00 | Clear 0.00 | None; cache includes unjournaled opening 400.00 |
| Wallet 1140 | Pending 1,640.00 | Clear 0.00 | None; no ledger lines at all |

The live UI settlement flow is internally inconsistent. `TreasuryClearingAging.jsx:50-77` confirms, then navigates to `/treasury/transfers` with clearing as source and Cashbox `1010` as target. `TreasuryOperationService.cs:298-324` rejects clearing because both generic-transfer accounts must be Cashbox/Bank. It would also settle to Cashbox, while the durable settlement service requires a Bank destination.

## 16. Financial Report Semantics

| Report | Source and calculation | Shopixy result |
| --- | --- | --- |
| Trial Balance | Posted and reversed-status journal lines in period; normal-balance direction by account type | Debits = credits = EGP 16,187.35; balanced |
| Profit And Loss | Revenue credit-minus-debit; COGS/expense debit-minus-credit | Revenue 1,022.23; COGS 6.00; expenses 120.00; net profit 896.23 |
| Balance Sheet | Ledger through as-of date plus synthetic `3999 Current Earnings` | Assets 11,430.34; liabilities 10,534.11; equity 896.23; balanced |
| Clearing Aging | Ledger lines on `IsClearing` accounts, bucketed by journal date | One account, Visa 1110, EGP 55.00, oldest 2 days |
| Cash Flow | Indirect P&L plus selected working-capital prefixes; cash equivalent is all `10xx` and `11xx` | Opening 0.00; closing 10,535.22; net change 10,415.22; not reconciled |
| Account Summary | Ledger aggregates plus account metadata | Ledger-correct balances, but `IsTreasury` is semantically wrong |

Cash Flow treats all `10xx` and `11xx` accounts as cash equivalents (`FinancialReportService.cs:773-775`). This includes `1020 POS Visa Account` and all clearing accounts, unlike Treasury's available Cashbox/Bank definition.

The EGP 120.00 Cash Flow variance is deterministic. Net profit includes EGP 120.00 affiliate expense, but the offset is `2105 Affiliate Payable`. Working-capital logic includes only `20xx` payables (`FinancialReportService.cs:458-467`), excluding `2105`; no cash moved. Cash Flow is therefore understated by EGP 120.00.

Clearing Aging buckets net period movements, not matched open settlement lots. Its oldest date is the oldest line on an account, not necessarily the oldest still-open amount. This can misstate age after partial settlements and should not be treated as provider-level settlement reconciliation.

## 17. API Contract And Parity

Authenticated direct GETs for `shopixy` returned HTTP 200 for Treasury accounts/dashboard and all tested financial reports.

Key parity comparison:

| Metric | Treasury/cache API | Financial/ledger API | Difference |
| --- | ---: | ---: | ---: |
| Cashbox + Bank | 10,484.02 | 10,484.02 for 1010 + 1030 | 0.00 |
| Pending clearing | 2,095.00 | 55.00 | **2,040.00** |
| Revenue | 1,022.23 | 1,022.23 | 0.00 |
| COGS | 6.00 | 6.00 | 0.00 |
| Expenses | 120.00 | 120.00 | 0.00 |
| Net profit | 896.23 | 896.23 | 0.00 |

Treasury account/balance/dashboard methods return `Account.CurrentBalance` (`TreasuryReadService.cs:64-123`, `:258-283`, `:335-349`, `:355-395`). Treasury movements use actual ledger lines. Financial reports recompute from ledger lines. This split is the direct implementation cause of the contradictory values.

Financial report endpoints consume `ITenantContext.RequiredTenantId`. Treasury read methods explicitly bind tenant and same-tenant linked accounts. Financial dashboard's declared 30-second cache is currently bypassed; `GetOrCreateCachedAsync` always invokes the factory (`FinancialReportsController.cs:303-311`).

## 18. Frontend And Browser Observation

The POS frontend has routes for Dashboard, Hub, account viewer, Trial Balance, P&L, Balance Sheet, Cash Flow, General Ledger, Clearing Aging, and Transfers. Legacy routes redirect into the Treasury/report area.

UI source observations:

| Surface | Behavior |
| --- | --- |
| Dashboard | Calls five financial-report endpoints in parallel and uses ledger-derived report values. |
| Dashboard cash card | Groups account codes `1000-1049` as cash and `1050-1099` as bank. Thus `1030` is displayed in the cash bucket and `1020` is included in total Cash & Bank. |
| Hub | Hardcodes six account cards and calls Treasury cached balances/dashboard. |
| Account Viewer | Uses Treasury balance plus ledger movements. A drifted account can show a header balance inconsistent with its movement close. |
| Clearing Aging | Uses ledger report, but its Settle action only prefills the incompatible generic-transfer form. |
| Transfers | Uses cached available balance for client validation, then posts a generic transfer with a random operation ID. |

Successful `greatone` observation before the repeated-load probe showed Cash & Bank EGP 5,234.84, Pending EGP 14.00, Revenue EGP 6,376.50, Net Profit EGP 2,744.50, balanced Trial Balance, and balanced Balance Sheet. These values align with the separate 2026-09-11 `greatone` financial certification and are not `shopixy` values.

Current read-only browser probe:

| Check | Result |
| --- | --- |
| Login/session | Authenticated as the supplied `greatone` POS Admin account. |
| Protected routes | URL authority retained; General Ledger rendered; most routes remained on `Loading...` during repeated hard loads. |
| Network | Treasury accounts returned HTTP 200; two unrelated/initial requests emitted HTTP 401; no financial POST was issued. |
| Arabic | `html lang=ar`, `dir=rtl`; Arabic loading text rendered. Some Help/common fallback labels remained English. |
| Mobile | 390px viewport showed zero document-level horizontal overflow on Treasury, Hub, and Transfers while still in loading state. |

Because most content did not reach a settled state in the final repeated-load probe, full English/Arabic/mobile content certification is not established.

## 19. Persisted Record Evidence

| Evidence | Observation |
| --- | --- |
| Journals | 53 total: 49 posted, 4 reversed originals; 4 posted reversal journals. |
| Accounting outbox | 35 total, 35 processed, 0 failed. |
| Settlement batches | 0 for `shopixy`. |
| COD sequence | Three `Order_COD` journals totaling EGP 1,230.00 and four `Order_Refund` journals totaling EGP 1,230.00 close ledger COD to zero. |
| Affiliate sequence | Three commission-earned journals total EGP 120.00 in 6110/2105, but each account cache retains an extra EGP 80.00 opening amount. |
| CommerceHub | Fourteen journals total EGP 12,570.12 per side, with source references and idempotency keys. |
| POS records | Mixed customer payments route Cash to 1010, Card to 1110, and credit settlement to 1410. Supplier payments use 2010 and the selected Treasury funding account. |
| Returns | Persisted allocation fields are `CashRefundAmount`, `CardRefundAmount`, and `CustomerCreditAmount`. |
| Legacy rows | Zero operational rows in legacy Treasury transaction tables. |

Representative current source references include `ORD-83B3-2026-01032`, `PUR-20260910-1001`, `SR-20260911-1004`, and CommerceHub transaction `e5e2703d-2301-4ccc-ad69-d9d696163ce4`.

## 20. Reconciliation Findings

Five account caches violate the declared invariant:

| Account | First ledger evidence | Inferred pre-ledger cached opening | Basis |
| --- | --- | ---: | --- |
| 1130 COD Clearing | 2026-08-28 | 400.00 | First EGP 410.00 debit has `BalanceAfter` 810.00. |
| 1140 Wallet Clearing | No lines | 1,640.00 | Cache exists with no ledger evidence. |
| 4020 E-commerce Sales | 2026-08-28 | 2,040.00 | First EGP 410.00 credit reaches 2,450.00. |
| 2105 Affiliate Payable | 2026-08-28 | 80.00 | Three EGP 40.00 credits close cached balance at 200.00 rather than ledger 120.00. |
| 6110 Affiliate Marketing Expense | 2026-08-28 | 80.00 | Three EGP 40.00 debits close cached balance at 200.00 rather than ledger 120.00. |

The drifted accounts were created on 2026-07-26. This is consistent with direct opening-balance initialization before the current journal history, not with a pending or failed accounting event.

Trial Balance and Balance Sheet remain balanced because both ignore the cache and aggregate balanced ledger lines. A balanced Trial Balance therefore does not certify that Treasury balances, cached controls, or Cash Flow definitions are correct.

## 21. Runtime Health

| Endpoint | HTTP | Overall | Relevant detail |
| --- | ---: | --- | --- |
| `/health/live` | 200 | Healthy | Process liveness passed. |
| `/health/ready` | 503 | Unhealthy | `payout_external_truth_readiness` unhealthy. Accounting outbox, PostgreSQL, Redis, MongoDB, Elasticsearch, RabbitMQ, and R2 healthy. |
| `/health` | 503 | Unhealthy | Same payout readiness failure plus `CommerceHubSearchSync` unhealthy. Projection drift, stale stock reservation, certificate provider, and Caddy are degraded. |

`accounting_outbox_readiness` is healthy: critical accounting outbox delivery is within threshold. No dedicated health check detects `LedgerAccounts.CurrentBalance` versus ledger drift or Cash Flow reconciliation failure. The overall unhealthy result is not proof that POS Treasury posting is unavailable, but the absence of a cache-parity check allows the confirmed Treasury defect to remain health-green.

## 22. Risk Register And Remediation Order

| ID | Class | Severity | Risk | Required response |
| --- | --- | --- | --- | --- |
| T-01 | Confirmed | Critical | Cached balances differ from ledger and feed Treasury presentation plus no-negative authorization. | Freeze affected manual Treasury outflows. Establish source-document provenance. If balances are valid, post approved dated opening/adjustment journals; otherwise recompute the derivative cache from ledger through a reviewed repair. Never edit ledger history or invent opening evidence. |
| T-02 | Confirmed | High | Treasury pending clearing is overstated by EGP 2,040.00. | Resolve T-01, then make Treasury current balances ledger-derived or enforce continuous cache parity. Add tenant-wide parity health/alerting. |
| T-03 | Confirmed | High | Cash Flow fails by EGP 120.00 because 2105 is outside the `20xx` payable range. | Classify working capital by account type plus explicit policy, or include all current operating liabilities such as 2105. Add a test requiring reconciliation for affiliate accrual without cash. |
| T-04 | Confirmed | High | Settle button leads to a transfer guaranteed to be rejected and targets Cashbox rather than the certified Bank settlement path. | Remove/disable the action until it invokes PaymentLifecycle settlement authority backed by batch/order evidence. Do not re-enable the manual settlement endpoint as a shortcut. |
| T-05 | Confirmed | Medium | `financialreports/accounts` equates system account with Treasury account. | Derive `IsTreasury` from an active same-tenant `TreasuryAccount` mapping. |
| T-06 | Confirmed | Medium | Active negative 1020 is included in reports/Cash Flow but absent from Treasury. | Decide whether 1020 is retired, mapped, or reclassified; reconcile it through normal journal authority. |
| T-07 | Confirmed | Medium | Clearing aging is account-flow aging, not matched open-settlement aging. | Label it accurately or derive aging from settlement/order lots and current batch lineage. |
| T-08 | Confirmed | Medium | Authenticated hard-route loads are intermittently stuck and emit 401s. | Trace the exact failing URLs and session refresh race, then certify settled English/Arabic/mobile content. |
| T-09 | Confirmed | Medium | Readiness is unhealthy for payout external truth; full health also has search sync failure. | Resolve through their owning CommerceHub reconciliation/search authorities; do not conflate with POS Treasury cache repair. |
| T-10 | Accepted tradeoff | Low | Direct cash-in/out/expense/manual clearing operations are disabled. | Preserve until source-owned, audited workflows exist. This reduces flexibility but prevents unsupported money creation. |

No broad rewrite is recommended. The smallest safe sequence is: quarantine affected cache-dependent operations, establish opening-balance provenance, reconcile cache/ledger through approved accounting authority, fix Cash Flow classification, remove the false settlement affordance, and add runtime parity monitoring.

## 23. Quality Gate And Final Verdict

| Gate | Result | Evidence |
| --- | --- | --- |
| Tenant scope explicit | PASS | SQL, API, and browser evidence labeled separately; service queries bind tenant. |
| Double-entry balance | PASS | Shopixy trial balance EGP 16,187.35 per side; no unbalanced journal observed. |
| Ledger immutability/reversal | PASS | Reversal journals compensate history; source statuses and links present. |
| Idempotency | PASS | Posting service and outbox use tenant/source-bound keys; representative records populated. |
| Outbox delivery | PASS | 35/35 processed, 0 failed; readiness healthy. |
| Treasury account authority | PASS WITH GAP | Six mappings valid, but 1020 remains outside Treasury and account-summary metadata is wrong. |
| Cached balance equals ledger | **FAIL** | Five accounts drift by absolute EGP 4,240.00. |
| Treasury/report parity | **FAIL** | Pending clearing EGP 2,095.00 versus EGP 55.00. |
| Trial Balance | PASS | Balanced. |
| Balance Sheet | PASS | Balanced at EGP 11,430.34. |
| Cash Flow | **FAIL** | `reconciles=false`, EGP 120.00 variance. |
| Settlement workflow | **FAIL** | UI advertises an operation rejected by the active endpoint; certified batch path has no operator wiring here. |
| Runtime health | **FAIL** | Readiness HTTP 503; no cache-parity health check. |
| Browser English/Arabic/mobile | PARTIAL | Authentication, RTL, routes, and shell responsiveness observed; most content did not settle in final probe. |
| Focused automated tests | PASS | 61 passed, 0 failed, 0 skipped. Existing compilation/analyzer warnings remain. |
| No financial mutation during audit | PASS | Read-only SQL/API/browser financial activity only. |

**Final Phase 1 Treasury Architecture quality gate: FAIL.**

The intended ledger-first architecture is materially stronger than the live `shopixy` state. Certification can be reconsidered only after T-01 through T-04 are corrected with authority-preserving changes, the affected tenant is re-reconciled from persisted records, Cash Flow reports `reconciles=true`, Treasury and report clearing values agree, and settled browser behavior is reproducible.

Supporting implementation maps: `.ai/hermes/accounts/FLOW-ACC-01-CHART-OF-ACCOUNTS.md` through `FLOW-ACC-08-FINANCIAL-REPORTS.md`. Prior certifications are historical evidence and do not supersede this dated live-state result.

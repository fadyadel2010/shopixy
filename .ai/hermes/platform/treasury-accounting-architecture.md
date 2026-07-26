# Treasury & Accounting — Implementation-Backed Architecture

> Source of truth: implementation only. Every claim below is grounded in code cited by path.
> No README, no markdown spec, no narrative doc was treated as authoritative. Where code
> contradicts narrative docs, the code wins.
>
> Status: REVERIFIED AGAINST CODE — HIGH CONFIDENCE. All sections read directly from source.

---

## 0. Executive Summary

Shopixy/PointOfSale runs a **double-entry tenant general ledger (GL)** as the authoritative
tenant accounting record, built on three core entities — `Account` (the chart of accounts node),
`JournalEntry` (an immutable, balanced double-entry document), and `LedgerEntry`
(one debit or credit line inside a journal). Tenant accounting effects — POS sales,
purchases, returns, expenses, payments, settlements, opening balances, CommerceHub
tenant-facing postings, storefront/online payments — are expressed as balanced journals posted
through one engine: `FinancialPostingService`.

**Audit caveat:** CommerceHub / SuperAdmin finance also has an operational financial ledger
(`CommerceHubFinancialTransaction`) written by `CommerceHubFinancialLedgerService`. That ledger is
not a balanced `JournalEntry` document; it records platform/vendor directional transactions, then
enqueues `AccountingOutboxMessage` rows with `EventType="CommerceHubLedgerPosting"` so the tenant
GL can receive balanced journals asynchronously. Therefore the correct implementation-backed
architecture is: tenant GL is double-entry and posts only through `FinancialPostingService`; the
CommerceHub operational finance ledger is a separate financial representation with reconciliation
and outbox-to-GL coverage, not itself the double-entry GL.

The Treasury is a **view layer on top of the ledger**, not a separate store of record.
`TreasuryAccount` is a lightweight mapping from a physical cashbox/bank/clearing account to a
backing `Account` row; `TreasuryOperationService` only performs treasury-internal
movements (deposit/withdraw/transfer) and explicitly **refuses** to create cash inflows
or outflows directly — inflow/outflow must originate from a certified payment, liability,
settlement, or approved-expense workflow. A PostgreSQL trigger set
(`PhaseTOJ_BlockLegacyTreasuryWrites.sql`) physically blocks direct INSERT/UPDATE on the
legacy `TreasuryTransactions`, `CashboxTransactions`, and `VisaTransactions` tables,
enforcing ledger-only writes at the database boundary.

Two parallel "party ledger" subsystems track customer/supplier balances:
- `AccountLedger` (the unified SQL party-ledger authority surfaced by
  `StatementOfAccountController`; GL control accounts remain the accounting authority), and
- `CustomerTransaction` / `SupplierTransaction` projection rows maintained by `LedgerService`
  for operational UI (statements, opening-balance screens).

Tenant accounting reporting is **ledger-only and live-SQL** (no Mongo/Redis/ES read models for the
tenant GL reports):
`FinancialReportService` and `TreasuryReadService` derive Trial Balance, P&L, Balance Sheet,
Cash Flow, General Ledger, Clearing Aging, and the Treasury Dashboard purely from
`LedgerEntries`/`JournalEntries`, inside `RepeatableRead`/`Serializable` transactions. The only
non-ledger read is the **Daily Close** report, which is intentionally computed from the GL
for the operating day and is treated as an operational cash-drawer reconciliation artifact.
SuperAdmin CommerceHub finance reports are the exception to the tenant-ledger reporting rule:
`SuperAdminFinanceOverviewService.GetCashflowAsync` and `GetProfitAndLossAsync` query
`CommerceHubFinancialTransactions` directly for platform cashflow/P&L.

The integration backbone is an **outbox**: `CommerceAccountingAudit` + `AccountingOutboxMessage`
rows are written by business operations and a background `AccountingOutboxProcessor` worker
dehydrates them into `CommerceAccountingService` posting calls. Every journal carries an
`IdempotencyKey`; the posting engine re-checks it inside a `Serializable` transaction and
re-verifies the request matches the original (preventing silent key reuse).

---

## 1. Accounting Model (verified)

**The tenant GL is a double-entry system.** Verified from:

- `Account.ApplyEntry` / `ReverseEntry` (`PointOfSale.Core/Entities/Accounting/Account.cs:112-138`)
  adjusts `CurrentBalance` using `NormalBalance` direction.
- `JournalEntry` invariants comment (`PointOfSale.Core/Entities/Accounting/JournalEntry.cs:11-17`):
  "Sum(LedgerEntries.DebitAmount) == Sum(LedgerEntries.CreditAmount)", "Must have at least 2
  ledger entries", "Status transitions: POSTED -> REVERSED only".
- `FinancialPostingService.ValidateJournalAsync` (`PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs:389-441`)
  enforces: ≥2 lines; exactly one side > 0 per line; no negative amounts; **debits == credits**
  (else `UnbalancedJournalException`); all accounts exist & active; idempotency key present.

Normal-balance rules (`AccountType` → `NormalBalance` via `GetNormalBalance()`,
`PointOfSale.Core/Enums/NormalBalance.cs`):

| AccountType | NormalBalance | Balance formula |
|---|---|---|
| Asset (1) | Debit | Debit − Credit |
| Liability (2) | Credit | Credit − Debit |
| Equity (3) | Credit | Credit − Debit |
| Revenue (4) | Credit | Credit − Debit |
| Expense (5) | Debit | Debit − Credit |
| CostOfGoodsSold (6) | Debit | Debit − Credit |

**Balances are STORED, not computed on the fly for the account header.** `Account.CurrentBalance`
(decimal 18,4) is denormalized and updated atomically inside the posting transaction via
`ApplyEntry` (`FinancialPostingService.cs:197`). Reports that need point-in-time balances
(General Ledger, Balance Sheet, Cash Flow) *recompute* from `LedgerEntries` up to a date
(`FinancialReportService.GetAccountBalanceAsOfAsync`, `:601-635`), which must equal the stored
`CurrentBalance`. The stored balance and the reconstructed balance are two views of the same
truth; divergence is a data-integrity bug, not a design.

`LedgerEntry.BalanceAfter` (decimal 18,4) snapshots the account balance immediately after
each line, giving a full point-in-time audit trail (`LedgerEntry.cs:58`).

---

## 2. Core Entities (inventory)

### 2.1 Account — Chart of Accounts node
`PointOfSale.Core/Entities/Accounting/Account.cs`

- `ITenantOwned`; `TenantId` NOT NULL.
- `AccountCode` (e.g. "1010", "4010"): unique per tenant, **IMMUTABLE after creation**
  (`private set` + `SetAccountCode` for EF materialization only).
- `AccountType` IMMUTABLE after creation.
- `ParentAccountId`/`SubAccounts`/`Level` — hierarchy (1=top, 2=sub). Cycles prevented by
  idempotency of seeding + parent resolution, not a hard FK cycle check in code.
- `CurrentBalance` (18,4) — denormalized, updated atomically with each ledger entry.
- Flags: `IsSystem` (no delete/rename — cashbox, visa, clearing), `IsClearing`
  (money in transit, should trend to zero, requires settlement), `PreventNegative`
  (debit-normal accounts cannot go below zero — cashbox, bank; enforced by
  `FinancialPostingService.ValidateNoNegativeBalances`, `:566-612`), `IsActive`
  (inactive accounts reject new entries).
- Clearing metadata: `ClearingSourceType` ("Gateway"/"Carrier"/"Wallet"),
  `ClearingSourceCode` ("stripe","bosta","vodafone"), `ExpectedSettlementDays`.
- `ApplyEntry(debit, credit)`: `CurrentBalance += (NormalBalance==Debit ? debit−credit : credit−debit)`.
- `ReverseEntry`: swaps debit/credit.
- `SetOpeningBalance(balance)`: only used for opening-balance setup.

### 2.2 JournalEntry — immutable double-entry document
`PointOfSale.Core/Entities/Accounting/JournalEntry.cs`

- `ITenantOwned`. `JournalNumber` = "JE-YYYY-NNNNNN" via `JournalNumberSequence`.
- `JournalDate` (business date, may differ from `PostedAtUtc`).
- Source tracking: `SourceType` (e.g. "SalesInvoice","Order_Payment","TreasuryTransfer",
  "Expense","SupplierPayment","Return","GatewaySettlement","CommerceHubFinancialTransaction",
  "Reversal"), `SourceId`, `SourceReference`.
- `IdempotencyKey` — unique per tenant, checked before AND inside the posting
  transaction (`FinancialPostingService.cs:56-73, 106-121`).
- `Status`: `Posted` (1) or `Reversed` (2). **Transitions: Posted → Reversed only.**
  Journals are never edited or deleted.
- `ReversedByJournalId` / `ReversesJournalId` — links the reversal pair.
- `LedgerEntries` — ≥2; `TotalDebits`/`TotalCredits`/`IsBalanced` are computed.

### 2.3 LedgerEntry — one debit/credit line
`PointOfSale.Core/Entities/Accounting/LedgerEntry.cs`

- `ITenantOwned`. `JournalEntryId`, `AccountId`.
- `DebitAmount` XOR `CreditAmount` > 0 (the other 0). `BalanceAfter` snapshot.
- `LineNumber` sequential within journal. `IsDebit`/`IsCredit`/`Amount` computed.
- **IMMUTABLE after creation** (factory methods only; no setters except EF materialization).

### 2.4 TreasuryAccount — view layer on the ledger
`PointOfSale.Core/Entities/Accounting/TreasuryAccount.cs`

- `ITenantOwned`. `AccountId` → backing `Account`. **The real balance lives on `Account`.**
- `Type`: `Cashbox` (1), `Bank` (2), `Clearing` (3).
- `IsDefault` (one per type per tenant), `IsActive`, `DisplayOrder`, `Location`,
  `ReferenceCode` (cashbox: the cashbox GUID; blank for the generic "Main Cashbox").
- `CurrentBalance => Account?.CurrentBalance ?? 0` — pure pass-through.

### 2.5 TreasuryTransaction — LEGACY, write-blocked
`PointOfSale.Core/Entities/TreasuryTransaction.cs`

- Old cash-movement table (`TransactionType` 1=Deposit,2=Withdrawal;
  `PaymentMethod` 1=Bank,2=Visa,3=Supplier,4=Customer). `BalanceAfter` denormalized.
- **Still read by `TreasuryController.GetTransactions` for historical display, but INSERT/UPDATE
  are blocked by DB triggers** (`PhaseTOJ_BlockLegacyTreasuryWrites.sql`, 6 triggers on
  `CashboxTransactions`, `VisaTransactions`, `TreasuryTransactions`). New money movement goes
  through the ledger; this table is read-only/legacy.

### 2.6 AccountLedger — unified party ledger (SOA/party-balance authority)
`PointOfSale.Core/Entities/AccountLedger.cs`

- `ITenantOwned`. `PartyType` (Customer/Supplier), `PartyId`, `TransactionDate`,
  `TransactionType` (Sale, SalesReturn, Purchase, PurchaseReturn, Payment, PaymentReceived,
  PaymentMade, CreditNote, DebitNote, CreditAdjustment, DebitAdjustment, OpeningBalance,
  Transfer), `DocumentNumber`, `DebitAmount`, `CreditAmount`, `RunningBalance`, `ReferenceId`.
- This is the table surfaced by `StatementOfAccountController` for receivables/payables party
  statements; GL control accounts remain the accounting authority for AR/AP totals.
- Has a uniqueness index `IX_AccountLedgers_CustomerOpeningBalance_Once` to prevent
  duplicate active customer opening-balance rows (`Migrations/20260608120000_PosCustomerSupplierAuthorityHardening.cs`).

### 2.7 CustomerTransaction / SupplierTransaction — operational projection
`PointOfSale.Core/Entities/CustomerTransaction.cs`

- Per-customer/supplier running ledger maintained by `LedgerService.ApplyCustomerTransaction`
  /`ApplySupplierTransaction` → `ApplyPartyTransactionAsync`. `BalanceAfter` denormalized.
- Used for the customer opening-balance screens and quick AR/AP lookups. Operates on the
  same business events as `AccountLedger` but is an operational convenience projection.

### 2.8 PartyLedgerAdjustment — durable adjustment intent
`PointOfSale.Core/Entities/PartyLedgerAdjustment.cs`

- `ITenantOwned`. `PartyType`, `PartyId`, `TransactionType` (DebitNote/CreditNote/…
  ), `AdjustmentDate`, `Amount`, `IdempotencyKey`, `ReferenceNumber`.
- This is the **durable intent record**; the actual GL mutation is enqueued to the outbox
  (`IAccountingOutboxService.EnqueuePartyLedgerAdjustmentAsync`) and posted by
  `CommerceAccountingService.PostPartyLedgerAdjustmentAsync` (debits/credits
  `AccountsReceivable`/`SupplierPayables` offset by `AccountsReceivableAdjustments`/
  `AccountsPayableAdjustments`).

### 2.9 PaymentMethod — ledger routing config
Referenced in `AccountResolver.GetPaymentAccountIdAsync`
(`PointOfSale.Infrastructure/Accounting/AccountResolver.cs:62-86`): each payment method
has `TargetAccountId` (direct funds, e.g. cash → cashbox) and/or `ClearingAccountId`
(cards/gateways → clearing), plus optional `FeesAccountId`. Seeded per tenant
(`TenantAccountSeeder.SeedPaymentMethodsAsync`).

### 2.10 Other ledger-support entities
- `JournalNumberSequence` — per-tenant JE numbering (`FinancialPostingService.GenerateJournalNumberAsync`).
- `AccountingOutboxMessage` — `PointOfSale.Core/Entities/Accounting/AccountingOutboxMessage.cs`
  (event type, business entity id, idempotency key, aggregate version, processed/claimed/retry
  state). Backbone for async GL feeding.
- `CommerceAccountingAudit` — `PointOfSale.Core/Entities/Accounting/CommerceAccountingAudit.cs`
  (business-type→journal forensic trail; `WasIdempotent` flag).
- `SystemAccountTemplate` — blueprint for seeding (not an entity).

---

## 3. Chart of Accounts (verified from `AccountCodes` + `SystemAccountTemplates`)

All seeded idempotently per tenant by `TenantAccountSeeder` / `ChartOfAccountsStartupSeeder`
(background `BackgroundService` that reseeds every active tenant on startup). Codes are
4-digit groupings.

### 3.1 Assets (1xxx)
| Code | Name | Sub | Clearing | PreventNeg |
|---|---|---|---|---|
| 1000 | Cash and Bank (parent) | — | — | — |
| 1010 | Cashbox Main | 1000 | — | YES |
| 1020 | POS Visa Account | 1000 | — | — |
| 1030 | Bank Account | 1000 | — | YES |
| 1040 | Cash Float Reserve | 1000 | — | YES |
| 1100 | Clearing Accounts (parent) | — | — | — |
| 1110 | Visa Clearing | 1100 | YES (Gateway/visa, 3d) | — |
| 1120 | Stripe Clearing | 1100 | YES (Gateway/stripe, 7d) | — |
| 1130 | COD Clearing | 1100 | YES (Carrier/cod, 14d) | — |
| 1140 | Wallet Clearing | 1100 | YES (Wallet/wallet, 3d) | — |
| 1310 | Inventory | — | — | — |
| 1410 | Accounts Receivable | — | — | — |
| 1420 | Marketplace Vendor Receivable (CommerceHub) | — | — | — |

### 3.2 Liabilities (2xxx)
| 2010 | Supplier Payables | — | — | — |
| 2020 | Customer Deposits | — | — | — |
| 2030 | Treasury Suspense | — | — | — |
| 2040 | Marketplace Vendor Payable (CommerceHub) | — | YES (PreventNeg) | — |
| 2105 | Affiliate Payable (AF-14) | — | — | YES |

### 3.3 Equity (3xxx)
| 3010 | Opening Balance Equity | — | — | — |

### 3.4 Revenue (4xxx)
| 4010 | POS Sales | — | — | — |
| 4020 | E-commerce Sales | — | — | — |
| 4030 | Shipping Revenue | — | — | — |

### 3.5 COGS (5xxx)
| 5010 | Cost of Goods Sold | — | — | — |

### 3.6 Expenses (6xxx)
| 6010 | Gateway Fees | — | — | — |
| 6020 | Shipping Fees | — | — | — |
| 6030 | Bank Fees | — | — | — |
| 6100 | General Expenses (parent) | — | — | — |
| 6110 | Affiliate Marketing Expense (AF-14) | 6100 | — | — |
| 6120 | Accounts Receivable Adjustments | 6100 | — | — |
| 6130 | Accounts Payable Adjustments | 6100 | — | — |

### 3.7 Sub-accounts (cashboxes)
Per cashbox, `CashboxLedgerAccountService.EnsureCashboxAccountAsync` creates a dedicated
system account with code `1010-<cashboxGuidN>` (e.g. `1010-<guid>`), parent `1010`,
`IsSystem=true`, `PreventNegative=true`. So a tenant can have many `1010-*` cashbox
accounts plus a generic "Main Cashbox" (`ReferenceCode` blank). The warehouse→cashbox
mapping is resolved at posting time (`ResolveWarehouseCashboxAccountCodeAsync`).

---

## 4. Ledger Architecture (verified)

### 4.1 Where balances originate
Balances originate **only** from `FinancialPostingService.PostJournalAsync`, which:
1. Idempotency check (before + inside tx).
2. Validates (≥2 lines, one-sided, non-negative, balanced, accounts exist/active).
3. Begins `Serializable` transaction (or joins ambient one; uses EF execution strategy for
   retries).
4. Re-checks idempotency inside tx (race guard).
5. Locks & loads accounts ordered by `Id` (deadlock prevention).
6. Enforces treasury inflow/outflow authority (§5).
7. Enforces `PreventNegative` projection (§6).
8. Generates `JournalNumber`.
9. Creates `JournalEntry` + `LedgerEntry`s; calls `account.ApplyEntry` to update
   `CurrentBalance` and records `BalanceAfter` per line.
10. Saves + commits.

Balances are **stored and updated atomically**; they are not recomputed from scratch on
every write. Reconstruction from `LedgerEntries` is available for point-in-time reports and as
an integrity cross-check.

### 4.1.1 SaveChanges-level ledger mutation guard
`ApplicationDbContext.GuardTreasuryCashAuthority` (`ApplicationDbContext.cs:6187-6291`) is a
second runtime barrier around the posting engine:
- `LedgerEntry` rows cannot be added unless `LedgerMutationScope.IsAuthorized` is true.
- `JournalEntry` rows cannot be added unless `LedgerMutationScope.IsAuthorized` is true.
- `JournalEntry` rows cannot be deleted; modified journals may only change reversal state and
  only inside the authorized mutation scope.
- `Account.CurrentBalance` cannot be modified unless `LedgerMutationScope.IsAuthorized` is true.
- `Account` tenant/code/type are immutable and `Account` rows cannot be deleted.
- `TreasuryAccount` rows cannot be deleted; tenant, backing `AccountId`, and type are immutable.

`FinancialPostingService.PostJournalInternalAsync` and `ReverseJournalInternalAsync` enter the
authorized scope via `LedgerMutationScope.Authorize()` before creating journals, ledger entries,
or changing reversal state. This means an arbitrary runtime path with `ApplicationDbContext`
cannot persist direct `JournalEntry`/`LedgerEntry`/`CurrentBalance` mutations even if it bypasses
the service abstraction.

### 4.2 How journals are posted
Single synchronous entry point `IFinancialPostingService.PostJournalAsync(PostingRequest)`.
`PostingRequest` (`PointOfSale.Core/Models/Accounting/PostingRequest.cs`): `TenantId`,
`JournalDate`, `SourceType`, `SourceId`, `SourceReference`, `Description`, `IdempotencyKey`,
`UserId`, `Lines` (≥2 `PostingLine`). `PostingLine.Debit/Credit(accountId, amount, desc)`.

### 4.3 How corrections / reversals work
`ReverseJournalAsync(tenantId, journalId, reason, userId)` (`FinancialPostingService.cs:256-359`):
- Loads original; rejects if already `Reversed`.
- Builds a `PostingRequest` with **swapped** debit/credit (`Debit=credit`, `Credit=debit`),
  `SourceType="Reversal"`, `IdempotencyKey="REVERSE:{tenantId}:{journalId}"`.
- Posts the reversal journal, then marks the original `MarkReversed(reversalId, reason)`.
- The original journal is **never modified**; `Status` flips Posted→Reversed for audit.
- Reversal journals carry `ReversesJournalId` for traceability.

### 4.4 Whether journals can be edited
**No.** `JournalEntry` and `LedgerEntry` expose no business setters (only EF
materialization setters). The only status mutation is `MarkReversed`. DB-level immutability is
reinforced by design; corrections are always via a new offsetting reversal journal.

### 4.5 Immutable journal guarantee
`Account.cs` doc: "Once posted, cannot be modified (immutable)". `JournalEntry.cs`
invariants: "Once posted, cannot be modified (immutable)", "Status transitions:
POSTED -> REVERSED only". `FinancialPostingService` summary: "Immutable journals (no
updates/deletes)".

---

## 5. Financial Posting — Runtime Authority (verified, critical)

`FinancialPostingService` embeds two authority validators that run **before any balance change**:

### 5.1 Treasury Inflow Authority — `ValidateTreasuryInflowAuthorityAsync` (`:614-722`)
If the posting would **increase** a cashbox/bank (`TreasuryAccount` of type Cashbox/Bank)
balance, the net increase must be backed by an authoritative source:
- `SalesInvoice` → `salesInvoices.PaidAmount` (must be `Completed` + `PaidAmount>0`).
- `Order_Payment` → `Orders.Total` (status Collected/Settled/PartiallyRefunded/Refunded).
- `GatewaySettlement` / `CODSettlement` → `PaymentSettlementBatches.GrossAmount`.
- `CommerceHubFinancialTransaction` → the transaction `Amount`.
- `Reversal` → inverse of the original journal's cash lines.

If the source type is not in this allow-list, or posted+new increase exceeds the authorized
amount, it throws: *"Treasury inflow requires certified payment or accounting SQL authority;
source '{SourceType}' is not authorized."* This means **you cannot credit a cashbox/bank with
arbitrary money** — every inflow must trace to a certified business event.

### 5.2 Treasury Outflow Authority — `ValidateTreasuryOutflowAuthorityAsync` (`:724-861`)
If the posting would **decrease** a cashbox/bank balance, it must be backed by:
- `PurchaseInvoice` → `PurchaseInvoices.PaidAmount`.
- `SupplierPayment` → `supplierPayments.Amount`.
- `Return` (SalesReturn) → `Returns.TotalAmount`.
- `Expense` → `Expenses.Amount` (and `!RequiresApproval || IsApproved`).
- `Order_Refund` → `OrderRefunds.Amount` (status Pending/Posted).
- `CommerceHubFinancialTransaction` (Debit + Cash bucket + Refund/Payout).
- `AffiliateCommissionPaid` → `AffiliatePayoutTransferAllocations.Amount`.
- `Reversal` → inverse of original cash lines.

Otherwise: *"Treasury outflow requires certified liability, refund, payout, or accounting SQL
authority; source '{SourceType}' is not authorized."*

### 5.3 Effect on TreasuryOperationService
`TreasuryOperationService.RecordExpenseAsync`, `CashInAsync`, `CashOutAsync` are
**deliberately hard-disabled** (`TreasuryOperationService.cs:242-290`):
- RecordExpense → *"Direct treasury expense posting is disabled. Outflows must originate
  from an approved SQL expense or other certified liability workflow."*
- CashIn → *"Direct treasury cash-in is disabled. Inflows must originate from certified
  payment collection or accounting settlement authority."*
- CashOut → *"Direct treasury cash-out is disabled. Outflows must originate from certified
  liability, payout, refund, or approved expense authority."*

Only `DepositAsync`, `WithdrawAsync`, `TransferAsync` are live — and they move money
**between** already-authorized treasury accounts (cashbox↔bank, etc.), never creating or
destroying it. The SQL triggers in `PhaseTOJ_BlockLegacyTreasuryWrites.sql` enforce this
at the database boundary for the legacy tables.

### 5.4 Who generates entries & where persisted
| Trigger | Posting service | Persisted to |
|---|---|---|
| POS sale | `CommerceAccountingService.PostPosSaleAsync` | `JournalEntries`/`LedgerEntries` |
| POS purchase | `PostPosPurchaseAsync` | GL |
| POS refund (sales return) | `PostPosRefundAsync` | GL |
| Purchase return | `PostPurchaseReturnAsync` | GL |
| Supplier payment | `PostSupplierPaymentAsync` | GL |
| Supplier opening bal | `PostSupplierOpeningBalanceAsync` | GL |
| Party ledger adjustment | `PostPartyLedgerAdjustmentAsync` | GL |
| POS expense | `PostPosExpenseAsync` | GL |
| Online payment captured | `PostOnlinePaymentCapturedAsync` | GL |
| Unresolved payment | `PostUnresolvedPaymentAsync` (→ Suspense) | GL |
| Resolved unresolved pay | `PostResolvedUnresolvedPaymentAsync` (Suspense→Sales) | GL |
| COD delivered | `PostCodDeliveredAsync` | GL |
| Wallet approved | `PostWalletApprovedAsync` | GL |
| E-commerce refund | `PostEcommerceRefundAsync` | GL |
| Gateway settlement | `PostGatewaySettlementAsync` | GL |
| COD settlement | `PostCodSettlementAsync` | GL |
| CommerceHub fin txn | `PostCommerceHubFinancialTransactionAsync` | GL |
| Treasury deposit/withdraw/transfer | `TreasuryOperationService` | GL |
| Clearing settlement / bank deposit | `TreasuryTransferService` | GL |

All persist to PostgreSQL `JournalEntries` + `LedgerEntries` only. No Mongo/Redis/ES write
for tenant GL financial posting.

### 5.5 CommerceHub operational finance ledger is a separate representation
`CommerceHubFinancialLedgerService` writes `CommerceHubFinancialTransactions` directly through
`AppendTransactionIfMissingAsync` (`PointOfSale.API/Services/CommerceHub/FinancialLedger/CommerceHubFinancialLedgerService.cs:673-756`).
Each row is a directional platform/vendor financial transaction with amount, bucket, reference,
idempotency key, and correlation id; it is **not** a `JournalEntry` and is not balanced inside the
row itself. In the same transaction, the service enqueues an `AccountingOutboxMessage` with
`EventType="CommerceHubLedgerPosting"`, `BusinessEntityId=transaction.Id`, and idempotency key
`COMMERCEHUB_TX:{tenant}:{transaction}` so `AccountingOutboxProcessor` can later post the
balanced tenant GL journal through `CommerceAccountingService.PostCommerceHubFinancialTransactionAsync`.

Runtime authority for this representation is therefore split:
- CommerceHub operational finance truth: `CommerceHubFinancialTransaction`.
- Tenant accounting truth: `JournalEntries`/`LedgerEntries` posted by `FinancialPostingService`.
- Synchronization mechanism: same-transaction accounting outbox row + background outbox posting.
- Failure mode: a `CommerceHubFinancialTransaction` can exist before its GL outbox message is
  processed; if the outbox fails or is delayed, SuperAdmin/CommerceHub finance reports and tenant
  GL reports can temporarily diverge.
- Mitigation found in code: `CommerceHubFinanceReconciliationJob` is registered hourly in
  `HangfireInitializationHostedService` and `FinancialReconciliationService` / 
  `CommerceHubFinanceReconciliationReadService` compute mismatch counts, orphan ledgers,
  payable/receivable mismatches, and flow mismatches.

---

## 6. Treasury (verified)

### 6.1 Cashboxes
- A `Cashbox` (operational entity) gets a **dedicated ledger account** `1010-<guid>`
  via `CashboxLedgerAccountService.EnsureCashboxAccountAsync`. The first dedicated
  cashbox becomes `IsDefault=true`; a prior generic "Main Cashbox" is demoted to
  `IsDefault=false`.
- `TreasuryReadService.ListCashboxAccountsAsync` unions dedicated (`ReferenceCode`=guid)
  and generic (blank `ReferenceCode`) cashbox accounts so balances are never lost.
- Warehouse→cashbox resolution: `ResolveWarehouseCashboxAccountAsync` picks the active
  cashbox for the warehouse; falls back to default.

### 6.2 Banks & Visa
- `BankAccount` (1030, PreventNegative) and `POS Visa Account` (1020) are seeded
  treasury accounts. Card/Visa sales post to `VisaClearing` (1110) — a clearing
  account — and settle later to the bank (§6.6).
- `TreasuryAccountType.Bank` and `.Cashbox` are the only account types that participate
  in treasury-inflow/outflow authority checks.

### 6.3 Transfers / Deposits / Withdrawals
- `TreasuryOperationService.TransferAsync` / `DepositAsync` / `WithdrawAsync`: balanced
  2-line journals (debit target, credit source). Idempotency keys
  `TREASURY_TRANSFER/DEPOSIT/WITHDRAW:{tenant}:{opId}`.
- `TreasuryTransferService`: `TransferAsync` (cashbox/bank↔cashbox/bank),
  `SettleClearingAsync` (clearing→bank), `DepositToBankAsync` (cashbox→bank).
  Permitted account-type pairs are enforced by `RequireTreasuryTypeAsync`.

### 6.4 Cash / Bank movement
Movement is always a GL journal. `TreasuryReadService.GetMovementsAsync` reads
`LedgerEntries` for treasury asset accounts in a date range, returning
`TreasuryMovementDto` (debit, credit, `BalanceAfter`, source, user).

### 6.5 Reconciliation
- **Clearing reconciliation** is explicit: `TreasuryTransferService.SettleClearingAsync`
  moves money from a `Clearing` account to a `Bank` account. Clearing accounts
  "should trend toward zero" (`Account.cs` doc) and `ExpectedSettlementDays` drives
  monitoring.
- **Clearing aging** report (`FinancialReportService.GetClearingAgingAsync`,
  `:460-550`) buckets pending clearing by age (Current/8-14/15-30/Over30) using
  `JournalDate` windows; flags accounts whose `BalanceAfter` (recomputed) ≠ 0.
- **Cash reconciliation** is the Daily Close (§9). Legacy `TreasuryTransaction.BalanceAfter`
  is retained historically but no longer written.

### 6.6 Settlement
Gateway/COD/wallet settlement is a journal that debits `BankAccount` (net =
gross−fees), credits the clearing account (gross), and debits a fees account
(`GatewayFees`/`ShippingFees`/`BankFees`) for the fee portion. Source
`GatewaySettlement`/`CODSettlement` are in the inflow-authority allow-list, so settlement
is the *only* legitimate way clearing balances drain to the bank.

### 6.7 Approval workflow / Authority
- Treasury write authority = ledger posting engine + the two inflow/outflow validators
  (§5) + DB triggers. No separate approval workflow for transfers/deposits/withdrawals
  in code (they require an `Owner/Admin/Manager` role at the controller, see §15).
- **Expenses** carry an approval workflow: `Expense.RequiresApproval`/`IsApproved`;
  the outflow validator only authorizes `Expense` postings where
  `!RequiresApproval || IsApproved` (`FinancialPostingService.cs:793`).

---

## 7. Opening Balances (verified)

Opening balances are posted as **regular balanced journals** against the `Opening Balance
Equity` (3010) offset account. They are not special-cased in the engine; they are
just journals with `SourceType` like `SupplierOpeningBalance`/`CustomerOpeningBalance`
and a matching `AccountLedger.TransactionType = OpeningBalance`.

### 7.1 Customer opening balance
`CustomersController.SaveCustomerOpeningBalances` (`PointOfSale.API/Controllers/CustomersController.cs:246-405`):
- Idempotency via `FinancialMutationIdempotency.TryClaimAsync` (operation
  `CUSTOMER_OPENING_BALANCE_BATCH`, request hash).
- Per customer: writes `Customer.OpeningBalanceAmount`/`BalanceType`/`OpeningBalanceDate`,
  then calls `LedgerService.ApplyCustomerTransaction(... OpeningBalance ...)` (operational
  `CustomerTransaction` projection) AND inserts an `AccountLedger` row
  (`PartyType=Customer`, `TransactionType=OpeningBalance`, `IsOpeningBalance=true`).
- A SQL unique index `IX_AccountLedgers_CustomerOpeningBalance_Once` blocks duplicate
  active opening-balance rows; the controller also pre-checks
  `customer.OpeningBalanceAmount.HasValue`. Duplicate → `DuplicateOpeningBalanceException`.
- Debit balance type → customer **owes** (AR increases); Credit → customer **overpaid**
  (liability/credit).

### 7.2 Supplier opening balance
`CommerceAccountingService.PostSupplierOpeningBalanceAsync` (`CommerceAccountingService.cs:423-503`):
- Validates `PartyType==Supplier` and `TransactionType==OpeningBalance`, exactly one of
  debit/credit > 0.
- Posts a balanced journal: debit/credit `SupplierPayables` (2010) offset by
  `OpeningBalanceEquity` (3010). Idempotency `SUPPLIER_OPENING_BALANCE:{tenant}:{id}`.

### 7.3 Cash / Bank opening balance
Cashboxes open via the **Cash Float Reserve** concept: `CashFloatReserve` (1040,
PreventNegative) is described as "Reserve account used to fund cashbox opening floats"
(`SystemAccountTemplate.CashFloatReserve`). Cash opening is performed through the
treasury journal path (deposit into the cashbox account) using the same balanced-journal
mechanism; there is no separate "CashOpeningBalance" entity — it is a treasury
movement journaled through `FinancialPostingService`.

### 7.4 Warehouse opening balance
`WarehouseOpeningStock` is an **inventory** opening, not a financial GL opening — it feeds
stock/COGS, not a treasury balance (`InventoryService` source `"WarehouseOpeningStock"`).
The financial effect of opening inventory value would be a journal debiting `Inventory` (1310)
offset by `OpeningBalanceEquity` (3010); opening stock quantity is a separate
inventory-domain concern.

### 7.5 Opening balance → ledger effect
Every opening balance **generates accounting entries** (a balanced journal). The GL
`CurrentBalance` of the affected control account (AR/AP) reflects it immediately. The
`AccountLedger`/`CustomerTransaction` rows are the party-level view; the GL is the
authority. `StatementOfAccountController` recomputes opening balance from
`AccountLedger` rows (`TransactionType==OpeningBalance`) and adds it to the running balance
(`StatementOfAccountController.cs:94-152`).

---

## 8. Payments (verified)

### 8.1 Customer payments (receipts)
- POS cash/card sales post revenue + (for mixed) split cash/card; card portion lands in
  `VisaClearing`.
- Online storefront payments → `PostOnlinePaymentCapturedAsync` (debit payment
  account via `PaymentMethod` routing, credit `EcommerceSales`). Unresolved captures go
  to `TreasurySuspense` (2030) then `PostResolvedUnresolvedPaymentAsync` clears
  suspense → `EcommerceSales`.
- Refunds: `PostPosRefundAsync` (reverse revenue, credit cash/card/customer-AR per
  allocation), `PostEcommerceRefundAsync` (debit `EcommerceSales`, credit payment
  account or bank for post-settlement refunds).

### 8.2 Supplier payments
`PostSupplierPaymentAsync`: debit `SupplierPayables` (2010), credit the funding account
(cashbox/bank/visa). Mixed/ credit funding is rejected. Idempotency
`SUPPLIER_PAYMENT:{tenant}:{id}`.

### 8.3 Payment allocation / partial / outstanding
- Allocation is explicit in refunds (`RefundAllocationSupport.Resolve`) and supplier payments
  (cash/card split must equal `PaidAmount`).
- Outstanding balances: customer/supplier outstanding = `AccountLedger` running balance
  (AR) / `SupplierPayables` GL control account (AP). `StatementOfAccountController`
  computes totals and closing balance from `AccountLedger`.

### 8.4 Manual payments
- Manual cash-in/out at treasury are **blocked** (§5.3). Legitimate manual effects flow
  through approved expenses (outflow) or certified payment collection (inflow).
- Party ledger adjustments (`PartyLedgerAdjustment`) are the sanctioned "manual correction"
  path, enqueued to the outbox and posted as offsetting AR/AP journals.

---

## 9. Daily Close (verified)

`ReportsController.GetDailyCloseReport` (`PointOfSale.API/Controllers/ReportsController.cs:347-…`)
returns a `DailyCloseReportDto` computed **from the GL for the operating day**:
- Opening/closing cash from cashbox-ledger accounts (prefix `1010-` + `VisaClearing`/
  `StripeClearing`/`BankAccount`/`AccountsReceivable`).
- Breakdown by `SourceType`: `SalesInvoice` debits = sales; `Return` credits = returns;
  expenses/purchases from their journals.
- `TotalTransactions` counted from `JournalEntries` with `SourceType=SalesInvoice`.

**Divergence note:** Daily Close is the one financial report computed from the ledger but
*shaped* as an operational cash-drawer reconciliation (cash vs card vs credit, expected
vs actual closing cash). It is not a GL report in the Trial-Balance sense; it is an
operational artifact. The Treasury Dashboard (`TreasuryReadService.GetDashboardAsync`)
is likewise GL-derived (cash+bank balances, P&L from period revenue/expense/COGS
ledger entries, pending clearing).

---

## 10. Receivables / Payables (verified)

### 10.1 Balance computation
- **AR** = `AccountLedger` running balance for `PartyType=Customer` (operational), and
  the `AccountsReceivable` (1410) GL control account (authoritative).
- **AP** = `SupplierPayables` (2010) GL control + `AccountLedger` for `PartyType=Supplier`.
- CommerceHub adds `MarketplaceVendorReceivable` (1420) and `MarketplaceVendorPayable`
  (2040, PreventNegative).

### 10.2 Statement of Account
`StatementOfAccountController.GetStatementOfAccount` (`StatementOfAccountController.cs:64-…`):
- Reads `AccountLedger` rows (party-filtered, date-filtered, paginated).
- Opening balance = last `RunningBalance` before start date + sum of `OpeningBalance`
  transactions.
- Aggregates `TotalDebit`/`TotalCredit` (excluding opening-balance rows) and computes
  `ClosingBalance`.
- Adjustments surface via `PartyLedgerAdjustments` (the outbox-fed intent records),
  enqueued through `IAccountingOutboxService.EnqueuePartyLedgerAdjustmentAsync`.

### 10.3 Aging buckets
- **Clearing aging** (§6.5) for in-transit money.
- **AR/AP aging** is not a separate dedicated report in the read path reviewed; aging is
  derivable from `AccountLedger.TransactionDate` per party (current/30/60/90 buckets are a
  UI concern over `AccountLedger`). The clearing-aging engine is the canonical aging
  implementation. (If AR/AP aging UI exists, it reads `AccountLedger` by date — same
  authority as SOA.)

### 10.4 Credits / Debits / Adjustments / Outstanding authority
- Credits/debits in `AccountLedger` are operational; the **GL control account is the
  authority** for the true outstanding amount.
- Adjustments: durable `PartyLedgerAdjustment` → outbox → GL journal (offset AR/AP with
  the adjustment sub-account 6120/6130). Replay-safe via `IdempotencyKey` + SQL unique
  index on `PartyLedgerAdjustments(tenant, idempotencyKey)`.

---

## 11. Financial Reporting (verified — tenant GL ledger-only, plus CommerceHub operational finance reports)

Tenant accounting reporting services read **exclusively** from `JournalEntries`/`LedgerEntries`
(`FinancialReportService` summary: *"CRITICAL: No business tables are read. Ledger is the
ONLY source of truth."* `TreasuryReadService`: *"ALL reads come from Ledger tables… NO
references to legacy treasury transaction tables."*). Reports run inside
`RepeatableRead` (reads) or `Serializable` (posting) transactions.

| Report | Source (`IFinancialReportService`) | Derivation |
|---|---|---|
| Trial Balance | `GetTrialBalanceAsync` | Group `LedgerEntries` by account in range; `Balance = NormalBalance==Debit ? Debits−Credits : Credits−Debits`; warns if `!IsBalanced`. |
| General Ledger | `GetGeneralLedgerAsync` | Per-account entries in range + opening/closing balance via `GetAccountBalanceAsOfAsync`. |
| Profit & Loss | `GetProfitAndLossAsync` | Revenue/COGS/Expense accounts in range; revenue=credit−debit, expense/debit−credit. |
| Balance Sheet | `GetBalanceSheetAsync` | Asset/Liability/Equity accounts ≤ asOfDate; computes current ratio (current assets / current liabilities by code prefix). |
| Cash Flow | `GetCashFlowAsync` | Net profit (P&L) + working-capital changes (receivables `1410`/`1420`, Inventory 13xx, AP 20xx prefix deltas) + opening/closing cash (10xx prefix). P&L and balance snapshots are derived with explicit ledger/account/journal joins. |
| Clearing Aging | `GetClearingAgingAsync` | Clearing accounts, bucketed by `JournalDate` age windows. |
| Account Summary | `GetAccountSummariesAsync` | All accounts + `CurrentBalance` as of now. |
| Treasury Dashboard | `TreasuryReadService.GetDashboardAsync` | Cash+bank from `TreasuryAccount.Account.CurrentBalance`; P&L from period entries; pending clearing. |
| Treasury Movements | `GetMovementsAsync` | `LedgerEntries` for treasury asset accounts. |
| Daily Close | `ReportsController` | GL-derived operational cash-drawer view (§9). |

**No Mongo, no Redis, no Elasticsearch read models for tenant GL finance.** Tenant accounting
reporting is live-SQL aggregation over the immutable ledger. (CommerceHub search sync uses ES,
but that is a search index, not a financial read model — see §13.)

### 11.1 SuperAdmin / CommerceHub finance reports are not tenant-GL reports
`SuperAdminFinanceOverviewService.GetCashflowAsync` and `GetProfitAndLossAsync`
(`PointOfSale.API/SuperAdmin/Services/SuperAdminFinanceOverviewService.cs:64-267`) query
`_dbContext.CommerceHubFinancialTransactions` directly. These reports summarize platform-level
cashflow/P&L from the CommerceHub operational finance ledger, not from
`JournalEntries`/`LedgerEntries`. They are still PostgreSQL-backed, but they are **not** ledger-only
in the tenant GL sense.

`SuperAdminFinanceOverviewService.GetReconciliationAsync` delegates to
`CommerceHubFinanceReconciliationReadService.GetSnapshotAsync`, which also reads
`CommerceHubFinancialTransactions` and business source tables (`CommerceHubSubOrders`,
`CommerceHubRefunds`, etc.) to compute mismatch counts. This is an implementation-backed
exception to the prior "all reports are ledger-only" claim.

---

## 12. CQRS / Event Integration (verified)

### 12.1 Write authority
- **Synchronous path:** business operations call `CommerceAccountingService` /
  `TreasuryOperationService` / `TreasuryTransferService` directly inside their
  transactions, which call `IFinancialPostingService.PostJournalAsync`.
- **Asynchronous outbox path:** business events write an `AccountingOutboxMessage`
  (+ `CommerceAccountingAudit`) and a background `AccountingOutboxProcessor`
  dehydrates them into `CommerceAccountingService` postings. This decouples GL writing
  from the originating request and provides retry/ordering.

### 12.2 Outbox mechanics (`AccountingOutboxProcessor`)
- `ProcessPendingMessagesAsync(batchSize)`: claims messages using per-tenant +
  per-business-entity **PostgreSQL advisory locks**
  (`pg_advisory_xact_lock(ComputeStableAdvisoryLockKey(tenantId, businessEntityId))`)
  to serialize processing per entity.
- **Aggregate version ordering:** `HasUnprocessedEarlierAggregateVersionAsync` defers a
  message if an earlier aggregate version exists, preserving causal order.
- **Critical financial-truth event types** (`_criticalFinancialTruthEventTypes`): PosSale,
  SaleReversal, PosPurchase, PurchaseReversal, SupplierPayment, SupplierOpeningBalance,
  PartyLedgerAdjustment, PosRefund, PurchaseReturn, PosExpense, ExpenseReversal,
  OnlinePayment, CodDelivered, WalletApproved, EcommerceRefund, UnresolvedOnlinePayment,
  ResolvedUnresolvedOnlinePayment, ResolvedUnresolvedCommerceHubPayment,
  GatewaySettlement, CodSettlement, CommerceHubLedgerPosting. These are never dropped
  even if `IsFailed` (they stay eligible until posted/reconciled).
- **Idempotency:** before posting, checks `ExistsByIdempotencyKeyAsync`; if already
  posted, marks the message processed (idempotent). After posting, `MarkProcessed(journalId)`.
- Exponential backoff on failures; metrics via `IAccountingMetrics`.

### 12.3 Events published / consumed
- The accounting outbox is **consumed internally** (by `AccountingOutboxProcessor`) —
  it is not a MassTransit/RabbitMQ event for external consumers. It is a DB-backed
  durable outbox within `ApplicationDbContext`.
- Events that feed it originate from POS/orders/payments/purchases/returns/expenses/
  CommerceHub financial transactions (the 20 critical types above).

### 12.4 Mongo / Redis / Elasticsearch / PostgreSQL
- **PostgreSQL:** sole system of record for the ledger (`JournalEntries`, `LedgerEntries`,
  `Accounts`, `TreasuryAccounts`, `AccountLedger`, `CustomerTransaction`, outbox, audit) and for
  CommerceHub operational finance (`CommerceHubFinancialTransactions`, reconciliation reports,
  daily aggregates).
- **Redis:** used for JWT/session blacklists and rate-limit counters (identity domain),
  **not** for financial read models. No ledger cache found.
- **Mongo:** used for catalog/affiliate/search read models — **not** for finance.
- **Elasticsearch:** CommerceHub product/search index — **not** a financial read model.

**Conclusion:** Treasury & Accounting tenant GL is **PostgreSQL-authoritative and ledger-only** for
tenant accounting reports. The implementation also contains a PostgreSQL CommerceHub operational
finance ledger (`CommerceHubFinancialTransactions`) with outbox synchronization to the tenant GL;
there are still no Mongo/Redis/Elasticsearch finance authorities.

---

## 13. Store / Domain Integration (verified)

### 13.1 POS
`CommerceAccountingService.PostPosSaleAsync` / `PostPosPurchaseAsync` / `PostPosRefundAsync`
post journals. Cash sales debit the warehouse cashbox account; card sales debit
`VisaClearing`; credit sales debit `AccountsReceivable`. COGS journals debit
`CostOfGoodsSold`, credit `Inventory` (using immutable `CostAmountSnapshot` when present).

### 13.2 Orders / Storefront / Online
`PostOnlinePaymentCapturedAsync`, `PostCodDeliveredAsync`, `PostWalletApprovedAsync`,
`PostEcommerceRefundAsync`, plus unresolved-payment suspense handling. Revenue →
`EcommerceSales` (4020); shipping revenue → `ShippingRevenue` (4030).

### 13.3 Payments
Payment-method routing (`AccountResolver.GetPaymentAccountIdAsync`) selects target
(cashbox) vs clearing (visa/stripe/cod/wallet) accounts. Settlements drain clearing
→ bank. The **outflow/inflow authority** (§5) is the gate that prevents payments
from minting or destroying treasury money outside certified events.

### 13.4 Shipping
Shipping fees post to `ShippingFees` (6020); COD settlement fees to `ShippingFees`;
carrier remittance is a COD clearing settlement.

### 13.5 CommerceHub
`PostCommerceHubFinancialTransactionAsync` maps CommerceHub financial transactions to GL
accounts via `ResolveCommerceHubPrimaryAccount` (vendor: payable/receivable/cash/bank;
platform: commission→`EcommerceSales`, promotion→`Expenses`, payout→vendor payable).
Suspense (`TreasurySuspense` 2030) is the offset until classified. CommerceHub does
own an operational platform/vendor finance ledger (`CommerceHubFinancialTransactions`) and emits
outbox messages that the tenant's GL consumes. It does **not** own the tenant double-entry GL.
(Per the Shopixy rules: CommerceHub is a separate bounded context; tenant accounting authority
for the money remains the tenant ledger, while CommerceHub owns marketplace platform/vendor
operational finance.)

### 13.6 Affiliate
AF-14 introduced `AffiliatePayable` (2105, PreventNegative) and `AffiliateMarketingExpense`
(6110). Affiliate commission payouts are in the outflow-authority allow-list
(`AffiliateCommissionPaid` → `AffiliatePayoutTransferAllocations`).

### 13.7 Inventory
Opening stock (`WarehouseOpeningStock`) is inventory-domain. Financial inventory value
lives in `Inventory` (1310); COGS in `CostOfGoodsSold` (5010). Inventory
adjustments **without SQL source authority are rejected**
(`CommerceAccountingService.PostInventoryAdjustmentAsync` → *"Inventory adjustment
accounting requires an authoritative SQL adjustment workflow."*).

---

## 14. Domain Boundaries (verified)

**Treasury & Accounting owns:**
- The double-entry GL (`Account`, `JournalEntry`, `LedgerEntry`).
- Chart of Accounts, treasury account mappings, journal numbering.
- Posting engine + inflow/outflow authority.
- AR/AP as GL control accounts + `AccountLedger` party ledger.
- Opening balances, expenses, adjustments, settlements, reversals.
- Tenant GL financial reports (trial balance, P&L, balance sheet, cash flow, GL, aging, SOA,
  dashboard, daily close). SuperAdmin CommerceHub finance reports belong to CommerceHub
  operational finance (§11.1).

**Treasury & Accounting does NOT own:**
- Inventory quantities / stock levels (Inventory domain; only the *valuation* posts to GL).
- Catalog / product pricing (Pricing domain).
- Order lifecycle / fulfillment (Orders domain; only the *financial posting* of a completed
  order is consumed).
- Payment gateway integration / PSP sessions (Payments domain; the *settled amount* is the
  authority feeding clearing→bank).
- Shipping carrier integration (Shipping domain; only shipping *fees* post to GL).
- CommerceHub marketplace ops / platform-vendor operational finance / affiliate recruitment
  (separate bounded contexts; CommerceHub operational finance emits financial events consumed by
  the tenant GL through the accounting outbox).
- Identity / JWT (separate; runtime authority re-checks SQL state, not the reverse).

---

## 15. Runtime Authority Matrix (verified)

| Concern | Runtime authority | Code |
|---|---|---|
| Ledger (journals) | `FinancialPostingService` (only writer) | `FinancialPostingService.cs` |
| Ledger DB mutation guard | `ApplicationDbContext.GuardTreasuryCashAuthority` + `LedgerMutationScope` | `ApplicationDbContext.cs:6187` |
| Journal immutability | No setters; `MarkReversed` only | `JournalEntry.cs` |
| Accounts (CoA) | `Account` + `TenantAccountSeeder` | `TenantAccountSeeder.cs` |
| Balances | Stored `Account.CurrentBalance`, updated atomically in posting tx | `Account.cs:112` |
| Treasury cash/bank | `TreasuryAccount` → backing `Account.CurrentBalance` | `TreasuryAccount.cs:76` |
| Treasury writes | `TreasuryOperationService` (deposit/withdraw/transfer only) | `TreasuryOperationService.cs` |
| Treasury inflow | `ValidateTreasuryInflowAuthorityAsync` allow-list | `FinancialPostingService.cs:614` |
| Treasury outflow | `ValidateTreasuryOutflowAuthorityAsync` allow-list | `FinancialPostingService.cs:724` |
| Cash/Visa direct post | **Blocked** (SQL triggers + disabled methods) | `PhaseTOJ_BlockLegacyTreasuryWrites.sql` |
| AR / AP (authority) | GL control accounts (1410/2010) | `CommerceAccountingService.cs` |
| AR / AP (operational) | `AccountLedger` / `CustomerTransaction` | `StatementOfAccountController.cs` |
| Expenses | `Expense` + approval gate in outflow validator | `FinancialPostingService.cs:793` |
| Payments | Certified payment/settlement events | §5 |
| Financial reports | `FinancialReportService` / `TreasuryReadService` (ledger-only) | `FinancialReportService.cs` |
| Statements | `StatementOfAccountController` (AccountLedger) | `StatementOfAccountController.cs:64` |
| Daily Close | `ReportsController` (GL-derived) | `ReportsController.cs:347` |
| Opening balances | `CommerceAccountingService` + `CustomersController` (journaled) | §7 |
| Outbox feeding | `AccountingOutboxProcessor` (advisory-lock serialized) | `AccountingOutboxProcessor.cs` |
| CommerceHub operational finance | `CommerceHubFinancialLedgerService` writes `CommerceHubFinancialTransactions` + GL outbox | `CommerceHubFinancialLedgerService.cs:673` |
| SuperAdmin finance reports | `CommerceHubFinancialTransactions` + reconciliation read service | `SuperAdminFinanceOverviewService.cs:64` |
| Cross-tenant | `TenantId` NOT NULL everywhere + global query filters | `ITenantOwned` |

**Controller role gates:** Treasury/Finance endpoints require `Owner,Admin,Manager`
(`TreasuryController`); customer/supplier opening balances require `Owner,Admin,Manager`
(`CustomersController.SaveCustomerOpeningBalances`); statement queries are
authenticated. All enforced after `TenantResolutionMiddleware` strips/validates `X-Tenant-Id`.

---

## 16. Runtime Authority Divergence (verified)

1. **GL control account vs `AccountLedger` running balance.** The GL (`AccountsReceivable`
   1410 / `SupplierPayables` 2010) is the authoritative outstanding amount;
   `AccountLedger.CustomerTransaction`/`SupplierTransaction` are operational projections.
   They must agree; the SOA controller recomputes from `AccountLedger`, not the GL
   control account, so a divergence between the two is a latent integrity risk if a
   posting hits the GL but not the party ledger (or vice versa).
2. **Stored `CurrentBalance` vs recomputed balance.** `Account.CurrentBalance` is
   denormalized and updated per posting; reports recompute from `LedgerEntries`. These
   are two views of one truth. `TrialBalanceReport.IsBalanced` and
   `BalanceSheetReport.IsBalanced` log warnings if they disagree — a built-in divergence alarm.
3. **Treasury vs Ledger.** `TreasuryAccount.CurrentBalance` is a pure pass-through to
   `Account.CurrentBalance`. The legacy `TreasuryTransaction.BalanceAfter` is historical
   only and no longer written (triggers block it).
4. **Daily Close vs GL reports.** Daily Close is GL-derived but shaped as an operational
   cash-drawer artifact, whereas Trial Balance / P&L are pure accounting views — same
   source, different intent.
5. **Reports vs operational data.** `FinancialReportService` reads **only** the ledger;
   `StatementOfAccountController` reads `AccountLedger` (party ledger), not the GL control
   account directly. Two read authorities for "the same" AR/AP number.
6. **Clearing accounts trend-to-zero expectation.** `IsClearing` accounts are expected to
   settle; the aging report flags non-zero balances, but nothing auto-settles — settlement
   is an explicit `SettleClearingAsync` driven by the settlement workflow.
7. **Suspense offset.** Unresolved/CH-unresolved payments land in `TreasurySuspense`
   (2030) and are later cleared to revenue/bank. Until cleared, the money is "in
   suspense" — a divergence between cash actually captured and revenue recognized, by design.
8. **CommerceHub operational finance vs tenant GL.** `CommerceHubFinancialTransactions` is a
   PostgreSQL financial ledger used by CommerceHub/SuperAdmin finance and reconciled against
   marketplace source tables. It is synchronized to tenant GL by `AccountingOutboxMessage`
   (`CommerceHubLedgerPosting`) and `AccountingOutboxProcessor`, not by an in-process balanced
   journal write. Until the outbox posts successfully, CommerceHub finance and tenant GL can be
   temporarily divergent.

---

## 17. Business Invariants (verified in code)

1. **Double-entry balance:** every journal has debits == credits
   (`ValidateJournalAsync`, `FinancialPostingService.cs:438`).
2. **≥2 lines, one-sided:** each line is debit XOR credit, positive
   (`JournalEntry.cs` invariants, `:389-418`).
3. **Immutable journals:** no update/delete; only Posted→Reversed
   (`JournalEntry.cs:11-17`, `:119-136`).
4. **Idempotency:** `IdempotencyKey` unique per tenant; re-posting returns the original
   journal and verifies line-for-line equality (`EnsureIdempotentRequestMatches`,
   `:526-559`).
5. **Account code/type immutability:** `AccountCode`/`AccountType` set once
   (`Account.cs:34,63`).
6. **PreventNegative:** cashbox/bank/`AffiliatePayable`/`MarketplaceVendorPayable` cannot
   go below zero (`ValidateNoNegativeBalances`, `:566-612`; `Account.cs:161`).
7. **Treasury inflow/outflow authority:** cash/bank changes must trace to a certified
   source (§5).
8. **Serializable posting + advisory-lock outbox:** no lost updates under concurrency
   (`FinancialPostingService.cs:83-98`; `AccountingOutboxProcessor` advisory lock).
9. **Opening balance uniqueness:** one active customer opening balance per customer
   (`IX_AccountLedgers_CustomerOpeningBalance_Once`).
10. **Expense approval:** outflow validator enforces `!RequiresApproval || IsApproved`.
11. **Tenant isolation:** every financial entity is `ITenantOwned` with NOT NULL
    `TenantId`; global query filters + `TenantSaveChangesInterceptor` block
    tenant-less writes.
12. **System accounts protected:** `IsSystem` accounts cannot be deleted/renamed
    (`Account.cs:148`).
13. **Clearing never final:** clearing accounts require settlement to reach the bank;
    direct cash-out/in is blocked (`TreasuryOperationService.cs:242-290`).
14. **Ledger mutation scope:** direct persistence of `LedgerEntry`, `JournalEntry`, and
    `Account.CurrentBalance` mutations is blocked unless the runtime is inside
    `LedgerMutationScope.Authorize()` (`ApplicationDbContext.cs:6187-6272`).
15. **CommerceHub financial transaction idempotency:** `CommerceHubFinancialLedgerService` rejects
    idempotency-key reuse with a different financial fingerprint and also de-duplicates identical
    reference/type/entity/direction/bucket/amount/currency fingerprints (`CommerceHubFinancialLedgerService.cs:773-863`).

---

## 18. Architectural Decisions (proven by implementation)

1. **Double-entry GL as sole financial record.** Every money effect is a balanced journal;
   no single-entry cash log is authoritative (legacy `TreasuryTransaction` is
   read-only/blocked).
2. **Stored denormalized balance + immutable ledger entries.** Balances update
   atomically per posting for O(1) reads; `LedgerEntry.BalanceAfter` preserves
   point-in-time history. Trade-off: balance must be kept in sync with entries (cross-checked
   by report `IsBalanced` warnings).
3. **Treasury = view over ledger, not a store.** `TreasuryAccount` maps a physical
   account to a backing `Account`; money never lives "in treasury" separately from the GL.
4. **Authority gates inside the posting engine.** Inflow/outflow validators encode
   "money cannot appear/disappear without a certified event" as code, not convention.
5. **Database-level enforcement.** SQL triggers block legacy direct writes
   (`PhaseTOJ_BlockLegacyTreasuryWrites.sql`), so even a buggy controller cannot
   bypass ledger authority.
6. **Outbox for async GL feeding.** Decouples business ops from posting; provides
   retry, aggregate-version ordering, and per-entity advisory-lock serialization.
7. **Tenant GL ledger-only reporting.** No Mongo/Redis/ES financial read models — tenant GL reports
   are live SQL over the immutable ledger. SuperAdmin CommerceHub finance reports intentionally
   read `CommerceHubFinancialTransactions` (§11.1), so "ledger-only reporting" is scoped to the
   tenant GL report suite.
8. **Chart of Accounts seeded idempotently per tenant** by `TenantAccountSeeder` +
   startup `BackgroundService`, so new system accounts (e.g. AF-14 2105/6110) appear
   for existing tenants on restart.
9. **Reversals over edits.** Corrections are offsetting reversal journals, preserving a
   full audit trail (never mutate a posted journal).
10. **Party ledger (AR/AP) as both GL control account and `AccountLedger` projection.**
    The GL owns the number; `AccountLedger`/`CustomerTransaction` own the operational
    statement UX.
11. **CommerceHub operational ledger plus GL coverage.** CommerceHub platform/vendor finance is
    first recorded as `CommerceHubFinancialTransaction`; tenant accounting coverage is produced by
    same-transaction accounting outbox rows and the background GL posting processor. This is a
    deliberate split between marketplace operational finance and tenant double-entry accounting.

---

## 19. Technical Debt (implementation-backed only)

1. **Two AR/AP authorities.** `AccountLedger`/`CustomerTransaction` and the GL control
   account can diverge if a posting path updates one and not the other. No automated
   reconciliation job was found that forces them equal (the SOA reads `AccountLedger`,
   not the GL). Risk class: silent divergence.
2. **Legacy tables retained.** `TreasuryTransactions`, `CashboxTransactions`,
   `VisaTransactions` still exist and are read for history; they are write-blocked but not
   dropped. Carries dead schema + the trigger-maintenance burden.
3. **`PostInventoryAdjustmentAsync` is a stub** that always fails
   (`CommerceAccountingService.cs:1433-1443`) — inventory valuation adjustments have no
   implemented SQL-authority workflow yet; the rejection path exists but the happy path
   does not.
4. **Mixed/credit purchase posting requires explicit cash/card split**
   (`PostPosPurchaseAsync` throws for mixed without split) — callers must pre-compute the
   split; no auto-derivation from `PaidAmount` for mixed/credit.
5. **Cash Flow report is partial.** `NonCashAdjustments`, `InvestingActivities`,
   `FinancingActivities` are hard-coded empty (`FinancialReportService.cs:450-452`) —
   only operating activities (net profit + working-capital changes) are computed. No
   depreciation accounts wired.
6. **Daily Close reads the ledger but is shaped operationally**; it is the one report
   that mixes "cash drawer" semantics with GL data, which can confuse consumers expecting
   a pure accounting view.
7. **Clearing aging is the only aging engine.** AR/AP aging is not a dedicated
   report in the read path; it would be re-derived from `AccountLedger` in the UI,
   risking inconsistent bucket definitions across clients.
8. **`TreasuryReadService.GetMovementsAsync` joins `Users` per ledger entry**
   (`FinancialReportService`/`TreasuryReadService.cs:214-217`) — N+1-style lookup per
   row; acceptable at treasury volumes but not scaled for high-frequency clearing.
9. **No automated ledger-vs-balance reconciliation job.** `IsBalanced` warnings exist
   but are passive logs; there is no scheduled self-heal/`RebuildPartyBalanceAsync`
   (which exists for party ledgers, `LedgerService.cs:105`) equivalent for GL accounts.
10. **CommerceHub finance is a second PostgreSQL financial ledger.**
    `CommerceHubFinancialTransactions` is a real runtime financial authority for
    CommerceHub/SuperAdmin platform-vendor finance, while tenant accounting authority is the GL.
    Synchronization is via outbox and reconciliation, not an atomic same-method journal post.
    Risk class: temporary or failed coverage gap between CommerceHub finance and tenant GL.
11. **SuperAdmin finance reporting bypasses tenant GL by design.** SuperAdmin cashflow/P&L reads
    `CommerceHubFinancialTransactions` directly. This is correct for platform finance, but it means
    the phrase "financial reports are ledger-only" is only true for tenant GL reports.

---

## 20. Competing Financial Representations Audit

| Financial truth | Runtime authority | Secondary representation | Synchronization mechanism | Failure mode | Risk | Recommended future action |
|---|---|---|---|---|---|---|
| Tenant GL balance | `Account.CurrentBalance` mutated only by `FinancialPostingService` inside `LedgerMutationScope` | Recomputed sums from `LedgerEntries`; `LedgerEntry.BalanceAfter` snapshots | Same posting transaction updates account and inserts ledger lines | Stored balance can diverge only through data corruption/manual DB writes/bug inside posting scope | Reports may disagree with account header; current alarms are passive | Add scheduled GL balance reconciliation/alert job; do not self-heal without audit |
| Treasury cash/bank | Backing `Account.CurrentBalance` through `TreasuryAccount.AccountId` | Legacy `TreasuryTransactions`/`CashboxTransactions`/`VisaTransactions` historical rows | New writes blocked by disabled service methods + DB triggers; read path uses GL for movements | Legacy history remains but no longer reflects new movement rows | Consumer confusion if old endpoints/tables are treated as current authority | Keep legacy endpoints clearly marked read-only; eventually migrate/drop legacy tables |
| AR/AP control balance | GL control accounts (`AccountsReceivable` 1410, `SupplierPayables` 2010) | `AccountLedger`, `CustomerTransaction`, `SupplierTransaction`, party `Balance` fields, SOA | Callers must update party ledger and enqueue/post GL for the same business event | GL can post while party ledger does not, or party ledger can update while GL does not | Silent statement vs GL control-account divergence | Add automated GL-vs-party-ledger reconciliation with blocking alerts |
| CommerceHub platform/vendor finance | `CommerceHubFinancialTransactions` | Tenant GL journals with `SourceType="CommerceHubFinancialTransaction"`; SuperAdmin aggregates/reconciliation reports | Same-transaction `AccountingOutboxMessage` then `AccountingOutboxProcessor` posts balanced GL | Outbox delay/failure leaves CommerceHub finance ahead of tenant GL | Platform finance and tenant accounting temporarily disagree | Monitor unprocessed/failed `CommerceHubLedgerPosting` outbox rows and surface coverage lag |
| SuperAdmin finance reports | `CommerceHubFinancialTransactions` + reconciliation read service | Tenant GL reports for store-level accounting | No direct read-time synchronization; GL coverage happens through outbox | SuperAdmin P&L/cashflow can include transactions not yet reflected in tenant GL | Cross-dashboard number mismatch during backlog/failure | Label report authority explicitly; expose GL coverage status alongside SuperAdmin finance numbers |

---

## 21. Implementation Follow-up Items

### 21.1 Add GL-vs-party-ledger reconciliation
- **Current implementation:** `LedgerService.RebuildPartyBalanceAsync` can rebuild party running
  balances from `AccountLedger`, but no job was found that reconciles AR/AP GL control balances
  against `AccountLedger`/`CustomerTransaction`/`SupplierTransaction` totals.
- **Why it matters:** SOA reads `AccountLedger` while accounting reports read GL control accounts;
  divergence can produce two valid-looking AR/AP numbers.
- **Recommended future improvement:** add a scheduled reconciliation that compares GL control
  balances to party ledger totals per tenant/party and emits blocking financial-health alerts.
- **Priority:** High.
- **Status:** Technical Debt.

### 21.2 Add GL account-balance integrity monitor
- **Current implementation:** `Account.CurrentBalance` is denormalized and protected by
  `GuardTreasuryCashAuthority`; reports can recompute from `LedgerEntries`, but no scheduled GL
  self-check job was found.
- **Why it matters:** A stored balance vs ledger-line divergence is a financial integrity defect;
  passive report warnings are not sufficient operational control.
- **Recommended future improvement:** scheduled read-only reconciliation of every account's
  `CurrentBalance` against `LedgerEntries`, with alerting and a manual audited repair workflow.
- **Priority:** High.
- **Status:** Technical Debt.

### 21.3 Surface CommerceHub-to-GL coverage lag
- **Current implementation:** `CommerceHubFinancialLedgerService` writes
  `CommerceHubFinancialTransactions` and enqueues `CommerceHubLedgerPosting` outbox rows;
  `CommerceHubFinanceReconciliationJob` is registered hourly and reports mismatches, but SuperAdmin
  cashflow/P&L reads `CommerceHubFinancialTransactions` directly.
- **Why it matters:** CommerceHub finance can be ahead of tenant GL until outbox processing catches
  up; this is expected async behavior but financially important.
- **Recommended future improvement:** expose unprocessed/failed `CommerceHubLedgerPosting` counts
  and oldest age beside SuperAdmin finance dashboards and tenant GL coverage checks.
- **Priority:** Medium.
- **Status:** Architecture Decision.

### 21.4 Clarify SuperAdmin finance report authority in UI/API contracts
- **Current implementation:** SuperAdmin finance cashflow/P&L are sourced from
  `CommerceHubFinancialTransactions`, not `FinancialReportService`/tenant GL.
- **Why it matters:** Users can misread SuperAdmin platform finance numbers as tenant GL reports.
- **Recommended future improvement:** label these reports as CommerceHub operational finance and
  include reconciliation/coverage status in responses.
- **Priority:** Medium.
- **Status:** Intentional Design.

---

## 22. Certification

### Implementation Confidence
**HIGH.** Every section was read directly from source: `Account`, `JournalEntry`,
`LedgerEntry`, `TreasuryAccount`, `AccountLedger`, `CustomerTransaction`,
`PartyLedgerAdjustment`, `Expense`, `ExpenseCategory`, `SystemAccountTemplate`,
`AccountCodes`; services `FinancialPostingService`, `CommerceAccountingService`,
`TreasuryOperationService`, `TreasuryTransferService`, `TreasuryReadService`,
`CashboxLedgerAccountService`, `TenantAccountSeeder`, `ChartOfAccountsStartupSeeder`,
`AccountResolver`, `FinancialReportService`, `AccountingOutboxProcessor`,
`LedgerService`, `CommerceHubFinancialLedgerService`, `CommerceHubFinanceReconciliationJob`,
`FinancialReconciliationService`, `CommerceHubFinanceReconciliationReadService`,
`SuperAdminFinanceOverviewService`; controllers `TreasuryController`, `CustomersController`,
`StatementOfAccountController`, `ReportsController`; SQL `PhaseTOJ_BlockLegacyTreasuryWrites.sql`.

### Verified Capabilities
- Double-entry immutable GL with balanced-journal enforcement.
- Treasury as ledger view; cashbox/bank/visa/clearing accounts; per-cashbox sub-accounts.
- Inflow/outflow authority gates; DB-level legacy-write blocking.
- Opening balances (customer/supplier/cash/inventory) journaled to GL.
- Payments (POS/online/COD/wallet), refunds, settlements, expenses w/ approval.
- AR/AP via GL control accounts + party ledger; statements of account; adjustments.
- Tenant GL reporting suite (Trial Balance, P&L, Balance Sheet, Cash Flow, GL, Clearing
  Aging, Account Summary, Treasury Dashboard, Daily Close) — ledger-only, live SQL.
- SuperAdmin CommerceHub finance reporting — PostgreSQL `CommerceHubFinancialTransactions`, not
  tenant GL; reconciled by CommerceHub finance reconciliation services.
- Outbox-driven async GL feeding with advisory-lock serialization + aggregate ordering.
- Multi-tenant isolation on every financial entity.

### Missing Capabilities
- No automated GL-vs-balance reconciliation/self-heal job (party-ledger rebuild exists,
  GL equivalent does not).
- No dedicated AR/AP aging report in the read path (clearing aging only).
- Cash Flow investing/financing sections unimplemented.
- Inventory valuation adjustment posting not implemented (stub rejects).
- No separate treasury approval workflow beyond role gating + expense approval.
- No GL-vs-party-ledger reconciliation job or GL `CurrentBalance` integrity monitor found.
- No explicit CommerceHub-to-tenant-GL coverage-lag metric surfaced with SuperAdmin finance reports.

### Architectural Observations
- The tenant accounting subsystem is a **textbook double-entry ledger with a treasury view
  layer**, and it enforces monetary integrity at four layers: engine validators,
  `ApplicationDbContext` mutation guards, immutable journals, and DB triggers. This is unusually
  defensive for a POS system.
- The biggest structural risk is the **dual AR/AP authority** (GL control account vs
  `AccountLedger` projection) with no forcing function to keep them equal.
- A second structural risk is **CommerceHub operational finance vs tenant GL coverage**:
  CommerceHub/SuperAdmin finance is PostgreSQL-authoritative in `CommerceHubFinancialTransactions`,
  then asynchronously covered by tenant GL postings through the accounting outbox.
- "CQRS" here means write-engine vs live-SQL-read, **not** event-sourced Mongo
  read models — finance deliberately avoids Mongo/Redis/ES projections, but does use PostgreSQL
  operational ledgers/aggregates for CommerceHub finance.

### Runtime Authority Verification
- Verified that the **only** writers to `JournalEntries`/`LedgerEntries` are
  `FinancialPostingService` (called by `CommerceAccountingService`,
  `TreasuryOperationService`, `TreasuryTransferService`, and the outbox processor).
- Verified via `PhaseTOJ_BlockLegacyTreasuryWrites.sql` that legacy treasury tables
  cannot be written directly.
- Verified `TreasuryOperationService.RecordExpenseAsync`/`CashInAsync`/`CashOutAsync`
  throw — confirming treasury cannot mint/destroy money unilaterally.
- Verified reporting services read ledger-only (their own class doc comments assert this and
  no business-table access was found in `FinancialReportService`/`TreasuryReadService`).
- Verified SuperAdmin finance reports are an exception: `SuperAdminFinanceOverviewService`
  cashflow/P&L read `CommerceHubFinancialTransactions`, and reconciliation reads CommerceHub
  finance plus business source tables.

### Technical Debt
See §19 and §21. Highest priority: force GL-control-account == `AccountLedger`
reconciliation, add a scheduled GL balance self-check to act on the existing `IsBalanced`
warnings, and surface CommerceHub-to-tenant-GL coverage lag for SuperAdmin finance.

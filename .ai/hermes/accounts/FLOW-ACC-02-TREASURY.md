# FLOW-ACC-02 — Treasury (POS Admin) Architecture

> Source of truth: implementation only. Every claim is grounded in code cited by
> `path:line`. No README/markdown spec is treated as authoritative. Where a
> narrative doc contradicts code, the code wins.
>
> Status: REVERIFIED AGAINST CODE — HIGH CONFIDENCE. All sections read directly
> from source.
>
> Companion doc: `platform/treasury-accounting-architecture.md` (broad GL/Treasury
> view). This document is the focused Treasury bounded-context reference.

---

## 1. Executive Summary

**What is Treasury?**
Treasury in Shopixy/PointOfSale is a *physical-account mapping and movement layer*
on top of the tenant double-entry General Ledger (GL). It is **NOT** a store of
record. The implemented Treasury domain consists of:

- `TreasuryAccount` — a lightweight view entity mapping a physical cashbox/bank/
  clearing account to a backing `Account` (the GL node that holds the real
  balance). `TreasuryAccount.CurrentBalance => Account?.CurrentBalance` is a pure
  pass-through (`TreasuryAccount.cs:76`).
- `TreasuryOperationService` — performs treasury-internal movements (deposit/
  withdraw/transfer) **only between already-authorized treasury accounts**; it
  deliberately hard-disables direct cash-in / cash-out / expense (`TreasuryOperationService.cs:242-290`).
- `TreasuryTransferService` — restricted transfers (cashbox↔bank, clearing→bank)
  with account-type enforcement.
- `TreasuryReadService` — all reads come from the ledger (`TreasuryReadService.cs:12-21`).
- `CashboxLedgerAccountService` — provisions dedicated GL accounts (`1010-<guid>`)
  per cashbox.

**What does Treasury own?**
Treasury owns the *mapping* of physical accounts to the ledger and the *internal
movement* of money between already-funded treasury accounts (cashbox↔bank,
cashbox→bank deposit, clearing→bank settlement). It owns the cashbox→ledger
account provisioning lifecycle.

**What does Treasury NOT own?**
- Treasury does **NOT** own financial truth. The GL (`JournalEntry`/`LedgerEntry`
  on `Account`) is the authoritative record.
- Treasury does **NOT** create or destroy money. `CashInAsync`, `CashOutAsync`,
  `RecordExpenseAsync` are all disabled and throw (`TreasuryOperationService.cs:242-290`);
  the controller exposes `/cash-in` as HTTP 410 Gone (`TreasuryController.cs:750-762`)
  and `/admin/accounting/treasury/settle` as 410 Gone (`AdminAccountingController.cs:118-127`).
- Inflow/outflow must originate from a *certified* payment, liability, settlement,
  or approved-expense workflow, verified by
  `FinancialPostingService.ValidateTreasuryInflowAuthorityAsync` /
  `ValidateTreasuryOutflowAuthorityAsync` (`FinancialPostingService.cs:614-861`).

**Who owns financial truth?** The tenant GL (`JournalEntries` + `LedgerEntries`
on `Account`). Treasury is a view + movement router.

**What is the source of truth?**
- Balances: `Account.CurrentBalance` (stored, updated atomically in the posting
  transaction).
- Treasury mapping: `TreasuryAccounts` table.
- Movements/history: `LedgerEntries`/`JournalEntries` (read-only projection via
  `TreasuryReadService`).

---

## 2. Discovery — Implemented Treasury Entities

| Entity | Path | Status | Role |
|---|---|---|---|
| `TreasuryAccount` | `PointOfSale.Core/Entities/Accounting/TreasuryAccount.cs` | IMPLEMENTED | Mapping: physical account → GL `Account`. View layer. |
| `TreasuryOperationService` | `PointOfSale.Infrastructure/Treasury/TreasuryOperationService.cs` | IMPLEMENTED | Deposit/Withdraw/Transfer (active). CashIn/CashOut/Expense (disabled). |
| `TreasuryTransferService` | `PointOfSale.Infrastructure/Accounting/TreasuryTransferService.cs` | IMPLEMENTED | Type-restricted transfer / clearing settlement / bank deposit. |
| `TreasuryReadService` | `PointOfSale.Infrastructure/Treasury/TreasuryReadService.cs` | IMPLEMENTED | All treasury reads from ledger. |
| `CashboxLedgerAccountService` | `PointOfSale.Infrastructure/Treasury/CashboxLedgerAccountService.cs` | IMPLEMENTED | Provisioning dedicated cashbox GL accounts. |
| `TreasuryController` | `PointOfSale.API/Controllers/TreasuryController.cs` | IMPLEMENTED | HTTP surface (Owner/Admin/Manager). |
| `AdminAccountingController` | `PointOfSale.API/Controllers/AdminAccountingController.cs` | IMPLEMENTED | Admin seeding + transfer/deposit (settlement disabled). |
| `Cashbox` / `CashboxTransaction` | `PointOfSale.Core/Entities/Cashbox.cs`, `CashboxTransaction.cs` | IMPLEMENTED | Operational cashbox + legacy transaction rows. |
| `TreasuryTransaction` | `PointOfSale.Core/Entities/TreasuryTransaction.cs` | LEGACY / READ-ONLY | Historical display only; new writes blocked. |
| `VisaTransaction` | `PointOfSale.Core/Entities/VisaTransaction.cs` | LEGACY / WRITE-BLOCKED | Direct inserts blocked by DB trigger. |
| Clearing accounts (`1110/1120/1130/1140`) | seeded `Account` rows flagged `IsClearing` | IMPLEMENTED | Settlement transit accounts (drain via certified settlement). |
| `PaymentMethod` | `PointOfSale.Core/Entities/Accounting/PaymentMethod.cs` | IMPLEMENTED | Routing config: `TargetAccountId`/`ClearingAccountId`/`FeesAccountId`. |

**NOT IMPLEMENTED (no code found):**
- No standalone "Treasury" aggregate with its own persistence.
- No treasury approval workflow (transfer/deposit/withdraw require only role
  authorization; only `Expense` has an approval flag, and `Expense` posting is
  itself disabled in treasury).
- No treasury-owned outbox/publisher/domain events. Treasury posts journals
  synchronously through `FinancialPostingService`; it does not emit Treasury
  events. (The GL outbox is `AccountingOutboxMessage`, owned by the accounting
  layer, not Treasury.)
- No `PhaseTOJ_BlockLegacyTreasuryWrites.sql` trigger (referenced by a prior
  narrative doc) — the only legacy write-block migration that exists blocks
  `CashboxTransactions` and `VisaTransactions` inserts
  (`Migrations/20260208180000_BlockLegacyCashboxVisaWrites.cs`), and even that is
  gated by a `LCR_ENFORCEMENT_ENABLED` setting defaulting to `false`.

---

## 3. Treasury Identity

`TreasuryAccount` (`TreasuryAccount.cs`):

| Attribute | Implementation | Notes |
|---|---|---|
| Primary Key | `Id` (inherited `BaseEntity` Guid) | |
| Tenant Ownership | `TenantId` (`ITenantOwned`) | Cross-tenant isolation enforced by query filters + `RequiredTenantId`. |
| Account Mapping | `AccountId` → `Account` (`TreasuryAccount.cs:24-25`) | The real balance lives on `Account`. |
| Treasury Type | `Type` (`TreasuryAccountType`: Cashbox=1, Bank=2, Clearing=3) (`:149-165`) | Drives movement authority. |
| Funding Type | Derived from backing `Account.AccountType == Asset` | Treasury accounts are always Asset-type GL nodes (`TreasuryReadService.cs:78-79`). |
| Currency | **NOT IMPLEMENTED** | No currency field on `TreasuryAccount` or the movement path. Single-currency assumption. |
| Active Status | `IsActive` (default `true`) (`:66`) | `false` = archived; excluded from reads and movement. |
| Default Flag | `IsDefault` (one per type per tenant) (`:61`) | First dedicated cashbox claims default; generic "Main Cashbox" demoted. |
| Soft Delete | `IsDeleted` (inherited `BaseEntity`) | Used in `IgnoreQueryFilters()` checks throughout. |
| System Flags | None on `TreasuryAccount` itself | Backing `Account.IsSystem = true` for cashbox accounts (`CashboxLedgerAccountService.cs:108-115`). |
| Name / NameAr | `Name` (required, ≤100), `NameAr` (≤100) | Bilingual display. |
| Reference / Location | `ReferenceCode` (≤50), `Location` (≤100), `DisplayOrder` | Cashbox `ReferenceCode` = cashbox GUID (`N` format). |

`CurrentBalance => Account?.CurrentBalance ?? 0` (`:76`) — Treasury never stores
its own balance.

---

## 4. Treasury Ownership

**Treasury owns:**
1. **Account mapping** — the `TreasuryAccount` rows that decide which GL `Account`
   nodes are "cashboxes", "banks", or "clearing" for a tenant
   (`SeedTreasuryAccountsAsync`, `TenantAccountSeeder.cs:235-322`).
2. **Cashbox→ledger provisioning** — `CashboxLedgerAccountService` creates the
   dedicated `1010-<guid>` GL account + its `TreasuryAccount` mapping when a
   cashbox is first resolved (`CashboxLedgerAccountService.cs:28-261`).
3. **Internal movement of already-authorized money** — Deposit/Withdraw/Transfer
   between cashbox/bank accounts, and clearing→bank settlement / cashbox→bank
   deposit. These move money between treasury accounts; they do not create it.
4. **Read surfaces** — dashboard, balances, movements, accounts dropdown, all
   computed from the ledger (`TreasuryReadService`).

**Treasury does NOT own:**
- Money creation/cancellation (inflow/outflow authority belongs to certified
  payment/liability/settlement workflows validated in `FinancialPostingService`).
- The GL itself (journal/ledger posting engine is `FinancialPostingService`).
- Reports' truth (they derive from the ledger).
- Settlement initiation (manual clearing settlement endpoint is 410 Gone; drains
  happen via certified `GatewaySettlement`/`CODSettlement` postings).

---

## 5. Treasury Lifecycle (implemented stages only)

| Stage | Implemented? | Evidence |
|---|---|---|
| **Create (mapping)** | YES | `TenantAccountSeeder.SeedTreasuryAccountsAsync` seeds Main Cashbox (1010), Primary Bank (1030), and clearing accounts (1110/1120/1130/1140) (`TenantAccountSeeder.cs:235-322`). Cashbox mapping created lazily by `CashboxLedgerAccountService`. |
| **Provision cashbox account** | YES | `EnsureCashboxAccountAsync` creates `1010-<guid>` GL account + `TreasuryAccount` (`CashboxLedgerAccountService.cs:28-261`). |
| **Activate** | PARTIAL | `TreasuryAccount.IsActive` exists; there is no explicit "activate" API. Cashbox open (`CashboxController.OpenCashbox`) sets `Cashbox.IsActive=true` but records **zero opening float** — opening non-zero cash is rejected (`CashboxController.cs:347-354`). |
| **Deposit** | YES | `TreasuryOperationService.DepositAsync` (`TreasuryOperationService.cs:57-117`). |
| **Withdraw** | YES | `TreasuryOperationService.WithdrawAsync` (`:120-178`). |
| **Transfer** | YES | `TreasuryOperationService.TransferAsync` (`:181-239`) + `TreasuryTransferService.TransferAsync` (`:40-90`). |
| **Settlement (clearing→bank)** | YES (engine), NO (manual UI) | `TreasuryTransferService.SettleClearingAsync` (`:93-145`) exists; the admin/manual endpoint is 410 Gone (`AdminAccountingController.cs:118-127`). Real drains come from certified settlement postings. |
| **Bank deposit (cashbox→bank)** | YES | `TreasuryTransferService.DepositToBankAsync` (`:148-200`) + admin endpoint `POST /admin/accounting/treasury/deposit` (`:132-152`). |
| **Close / Archive** | PARTIAL | `IsActive=false` archives a treasury account (excluded from reads/movement). No dedicated "close" API found. |
| **Delete** | NO (hard) | `TreasuryAccount` rows are never deleted (soft delete only; DB guard forbids delete). |

---

## 6. Aggregate Analysis

```
Treasury (bounded context — view + movement router)
├── TreasuryAccount            (mapping rows; 1 per physical account)
│     └── Account (GL node)    (OWNED BY ACCOUNTING — holds balance)
│           ├── JournalEntry   (OWNED BY ACCOUNTING — immutable document)
│           └── LedgerEntry    (OWNED BY ACCOUNTING — debit/credit lines)
├── Cashbox                    (operational; provisions 1010-<guid> + mapping)
└── TreasuryOperationService / TreasuryTransferService
      └── FinancialPostingService.PostJournalAsync  (OWNED BY ACCOUNTING — sole writer)
```

**Who owns what:**
- Treasury owns `TreasuryAccount` mapping rows and the cashbox provisioning
  orchestration.
- Accounting owns `Account`, `JournalEntry`, `LedgerEntry`, and the posting
  engine. Treasury only *calls* `PostJournalAsync`; it cannot write the ledger
  directly (enforced by `ApplicationDbContext.GuardTreasuryCashAuthority`
  and the disabled legacy writes).
- Ledger/Reports are downstream read projections — Treasury reads them, does not
  own them.

---

## 7. Treasury Behavior

Implementing behavior (all via balanced journals through `FinancialPostingService`):

| Operation | Source Type | Journal (Debit/Credit) | Idempotency Key | Evidence |
|---|---|---|---|---|
| Deposit | `TreasuryDeposit` | DEBIT target, CREDIT source | `TREASURY_DEPOSIT:{tenant}:{opId}` | `TreasuryOperationService.DepositAsync:57-117` |
| Withdraw | `TreasuryWithdraw` | DEBIT target, CREDIT source | `TREASURY_WITHDRAW:{tenant}:{opId}` | `:120-178` |
| Transfer | `TreasuryTransfer` | DEBIT to, CREDIT from | `TREASURY_TRANSFER:{tenant}:{opId}` | `:181-239` |
| Transfer (restricted) | `TreasuryTransfer` | DEBIT to, CREDIT from | `TRANSFER:{tenant}:{id}` | `TreasuryTransferService.TransferAsync:40-90` |
| Clearing settlement | `ClearingSettlement` | DEBIT bank, CREDIT clearing | `SETTLEMENT:{tenant}:{id}` | `:93-145` |
| Bank deposit | `BankDeposit` | DEBIT bank, CREDIT cashbox | `DEPOSIT:{tenant}:{id}` | `:148-200` |
| Expense | — | — | — | `RecordExpenseAsync` throws (disabled) `:242-256` |
| Cash In | — | — | — | `CashInAsync` throws (disabled) `:259-273`; controller 410 `TreasuryController.cs:750-762` |
| Cash Out | — | — | — | `CashOutAsync` throws (disabled) `:276-290` |

**Shared guards in `TreasuryOperationService`:**
- `ValidateAmount(amount)` — must be `> 0` (`:292-296`).
- Source must differ from target (`:85-86`, `:147-148`, `:208-209`).
- `EnsureTreasuryAccountAsync` — both endpoints must be **active same-tenant
  Cashbox/Bank** treasury accounts with active Asset backing accounts
  (`:298-325`); otherwise `TreasuryValidationException`.

**Reads (`TreasuryReadService`):**
- `GetAccountBalanceAsync`, `GetAccountBalancesAsync` — sums `Account.CurrentBalance`.
- `GetMovementsAsync` — reads `LedgerEntries` for treasury asset accounts in a
  date range, returns `TreasuryMovementDto` (`:126-223`).
- `GetDashboardAsync` — cashbox/bank balances + period P&L + pending clearing
  (`:226-352`).
- `GetTreasuryAccountsAsync` — list mappings (`:355-396`).

---

## 8. Financial Behavior

**Cash movements** — Always a GL journal; no separate cash-movement store.
`TreasuryReadService.GetMovementsAsync` reads `LedgerEntries`.

**Transfer workflow** —
1. Controller (`POST /api/treasury/operations/transfer`) → `TreasuryOperationService.TransferAsync`.
2. Amount validated `>0`, source≠target, both accounts verified as active
   same-tenant Cashbox/Bank treasury accounts.
3. Build `PostingRequest` (DEBIT to, CREDIT from) with idempotency key.
4. `FinancialPostingService.PostJournalAsync` runs inflow/outflow authority +
   `PreventNegative` checks, then writes `JournalEntry` + `LedgerEntry`s,
   updating `Account.CurrentBalance`.

**Funding validation** —
- `TreasuryOperationService.EnsureTreasuryAccountAsync` (`:298-325`) requires
  active Cashbox/Bank treasury authority.
- `TreasuryTransferService.RequireTreasuryTypeAsync` (`:202-229`) enforces
  permitted type pairs: transfer source/dest ∈ {Cashbox,Bank}; settlement source
  = Clearing, dest = Bank; deposit source = Cashbox, dest = Bank.

**Negative balance prevention** —
Enforced at the GL level by `FinancialPostingService.ValidateNoNegativeBalances`
(`FinancialPostingService.cs:566-612`): any `Account.PreventNegative` (cashbox,
bank, etc.) whose projected balance would go `< 0` throws
`InsufficientFundsException`. This is what makes a treasury withdrawal from an
empty cashbox fail.

**Settlement** —
- Implemented engine: `TreasuryTransferService.SettleClearingAsync` (clearing→bank).
- Manual initiation is **disabled** (410 Gone). In production, clearing drains
  via certified `GatewaySettlement`/`CODSettlement` postings
  (`FinancialPostingService.cs:667-677` inflow allow-list), which debit Bank
  (net), credit Clearing (gross), debit Fees.
- Clearing accounts are flagged `IsClearing` and "should trend to zero".

**Posting** — Sole writer is `FinancialPostingService.PostJournalAsync`. Treasury
never writes `JournalEntry`/`LedgerEntry`/`Account.CurrentBalance` directly.

**Clearing** — Clearing accounts are normal seeded `Account` rows with
`IsClearing=true` + `ClearingSourceType`/`ClearingSourceCode`/`ExpectedSettlementDays`.
They are treasury-mapped as `TreasuryAccountType.Clearing`.

**Ledger ownership** — Accounting owns the ledger. Treasury is a consumer.

**Financial authority** — Split:
- *Movement authority*: Treasury (which accounts may move internally).
- *Inflow/Outflow authority*: Accounting (`FinancialPostingService` validators) —
  every cashbox/bank balance change must trace to a certified source.
- *DB boundary*: legacy `CashboxTransactions`/`VisaTransactions` inserts blocked
  by trigger (when enforcement enabled); `TreasuryTransaction` is legacy read-only.

---

## 9. Constraints (implementation-backed business rules)

| # | Rule | Evidence |
|---|---|---|
| C1 | Amount must be positive | `TreasuryOperationService.ValidateAmount:292-296`; `TreasuryTransferService` `amount<=0` throws `:49-50,102-103,157-158`. |
| C2 | Source ≠ target account | `TreasuryOperationService:85-86,147-148,208-209`; `TreasuryTransferService:52-53,105-106,160-161`. |
| C3 | Both endpoints must be active same-tenant Cashbox/Bank treasury accounts with active Asset backing | `TreasuryOperationService.EnsureTreasuryAccountAsync:298-325`. |
| C4 | Transfer type restriction: Cashbox/Bank only | `TreasuryTransferService.RequireTreasuryTypeAsync:55-58,202-229`. |
| C5 | Settlement: Clearing→Bank only | `TreasuryTransferService:108-111`. |
| C6 | Bank deposit: Cashbox→Bank only | `TreasuryTransferService:163-166`. |
| C7 | `PreventNegative` accounts cannot go below zero | `FinancialPostingService.ValidateNoNegativeBalances:566-612`; throws `InsufficientFundsException`. |
| C8 | Inactive accounts reject new entries | `FinancialPostingService.cs:148-152` (`InactiveAccountException`). |
| C9 | Treasury inflow requires certified source | `ValidateTreasuryInflowAuthorityAsync:614-722`. |
| C10 | Treasury outflow requires certified source | `ValidateTreasuryOutflowAuthorityAsync:724-861`. |
| C11 | Cashboxes open with ZERO new cash | `CashboxController.OpenCashbox:347-354` (non-zero → 400 `OPENING_FLOAT_TRANSFER_REQUIRED`). |
| C12 | Manual cash-in disabled | `TreasuryOperationService.CashInAsync` throws; controller 410 `TreasuryController.cs:750-762`. |
| C13 | Manual cash-out disabled | `TreasuryOperationService.CashOutAsync` throws `:276-290`. |
| C14 | Manual expense disabled | `TreasuryOperationService.RecordExpenseAsync` throws `:242-256`. |
| C15 | Manual clearing settlement disabled | `AdminAccountingController.SettleClearing` 410 `:118-127`. |
| C16 | Cross-tenant isolation | All reads/writes filter by `TenantId` + `RequiredTenantId`; `IgnoreQueryFilters()` reads still re-check `TenantId`. |
| C17 | Idempotency | Every operation carries a unique idempotency key re-checked inside the posting transaction. |
| C18 | Legacy write-block (conditional) | `BlockLegacyCashboxVisaWrites` migration triggers block `CashboxTransactions`/`VisaTransactions` inserts **only when `LCR_ENFORCEMENT_ENABLED='true'`** (default `false`). |

---

## 10. SQL Transactions

**Transaction boundaries:**
- `CashboxLedgerAccountService.EnsureCashboxAccountAsync` opens its own
  `ReadCommitted` transaction when no ambient transaction exists, with up to 2
  retries on `DbUpdateException` (`:56-245`). Re-checks existence inside the
  transaction to handle races; detaches created entities on retry; brief delay
  (`Task.Delay(50*(attempt+1))`).
- `FinancialPostingService.PostJournalAsync` runs inside a `Serializable`
  transaction (or joins ambient), with EF execution strategy retries, and
  re-checks idempotency inside the transaction as a race guard
  (`FinancialPostingService.cs` STEP 5).
- `CashboxController.OpenCashbox` wraps open in `BeginTransactionAsync` +
  `CommitAsync` (`:356-407`).

**Locks / Concurrency:**
- Accounts loaded ordered by `Id` for deadlock prevention
  (`treasury-accounting-architecture.md` §4.1; `FinancialPostingService`).
- Cashbox account creation uses an explicit re-check-inside-tx + retry pattern
  to survive concurrent creation (`CashboxLedgerAccountService.cs:56-245`).

**Rollback:**
- `PostJournalAsync` rolls back on any exception (`:244-251`).
- `EnsureCashboxAccountAsync` rolls back its own transaction on
  `DbUpdateException` (retry) or any other exception (`:192-244`).

**Retry:** cashbox provisioning retries 2×; posting uses EF execution strategy.

**Atomicity:** balances (`Account.CurrentBalance`) are updated atomically inside
the posting transaction via `ApplyEntry`; nothing is recomputed asynchronously
for the header.

**Outbox:** Treasury does **not** own an outbox. The GL outbox
(`AccountingOutboxMessage`) is an accounting-layer concern. Treasury mutations are
synchronous journal posts.

---

## 11. Events

**Treasury events: NOT IMPLEMENTED.** No `IEventPublisher`, domain-event, or
outbox row is emitted by `TreasuryOperationService` or `TreasuryTransferService`
(verified: zero matches for `OutboxMessage`/`IEventPublisher`/`PublishAsync`/
`DomainEvent` in `PointOfSale.Infrastructure/Treasury`).

**Transfer / Settlement events: NOT IMPLEMENTED** at the Treasury layer.

**Posting events:** The GL posting path can enqueue `AccountingOutboxMessage`
rows (owned by accounting, for CommerceHub→GL sync), but this is not a Treasury
emission.

**Consumers:** None Treasury-specific. Downstream readers (dashboard, movements)
read the ledger live.

> Correction vs. prior narrative: a previously circulated doc claimed a
> `PhaseTOJ_BlockLegacyTreasuryWrites.sql` trigger set on `TreasuryTransactions`/
> `CashboxTransactions`/`VisaTransactions`. This file does **not** exist in the
> solution. The only legacy write-block migration is
> `20260208180000_BlockLegacyCashboxVisaWrites.cs` (Cashbox + Visa only), and it
> is disabled by default (`LCR_ENFORCEMENT_ENABLED=false`).

---

## 12. Runtime Architecture (implementation-backed)

```
Treasury Operation (Deposit/Withdraw/Transfer/BankDeposit)
        │
        ▼
[ Controller: role Owner/Admin/Manager + FinancialWrite rate limit ]
        │  (validates DTO: amount>0, codes present, OperationId required)
        ▼
TreasuryOperationService / TreasuryTransferService
        │  (ValidateAmount, source≠target,
        │   EnsureTreasuryAccountAsync / RequireTreasuryTypeAsync)
        ▼
Build PostingRequest (balanced 2-line journal, idempotency key, SourceType)
        │
        ▼
FinancialPostingService.PostJournalAsync
        │   ├─ Idempotency check (before + inside Serializable tx)
        │   ├─ ValidateTreasuryInflowAuthorityAsync   (C9)
        │   ├─ ValidateTreasuryOutflowAuthorityAsync  (C10)
        │   ├─ ValidateNoNegativeBalances (PreventNegative) (C7)
        │   ├─ Create JournalEntry + LedgerEntry(s)
        │   └─ account.ApplyEntry → update Account.CurrentBalance
        ▼
Commit → JournalEntries / LedgerEntries / Account (GL = source of truth)
        │
        ▼
TreasuryReadService (dashboard / balances / movements) reads GL live
```

Manual `cash-in`, `cash-out`, `expense`, and admin `settle` are rejected at the
service/controller boundary (disabled / 410 Gone).

---

## 13. Browser Test Specification

Frontend surface: `PointOfSale.Web/src/services/treasuryApi.js`
(`treasuryDeposit`, `treasuryWithdraw`, `treasuryTransfer`, `treasuryExpense`,
`treasuryCashIn`, `treasuryCashOut`, `getTreasuryDashboard`, `getTreasuryBalance`,
`getTreasuryMovements`). All hit `POST /api/treasury/operations/*` and
`GET /api/treasury/*`.

### Scenarios & Assertions

**S1 — Deposit (cashbox → bank)**
- Auth: Owner/Admin/Manager. Rate-limited (`FinancialWrite`).
- `POST /api/treasury/operations/deposit` { sourceAccountCode:"1010",
  targetAccountCode:"1030", amount:100, operationId:<guid> }.
- Assert: 200, `success=true`, `journalId`+`journalNumber` returned.
- Assert: bank balance +100, cashbox balance −100 (reconcile via
  `GET /api/treasury/balance/1030` and `/1010`).

**S2 — Withdraw (bank → cashbox)** — mirror of S1; assert reversed balances.

**S3 — Transfer between cashboxes** — `POST .../transfer` {fromAccountCode,
toAccountCode, amount}. Assert both balances move; source≠target enforced.

**S4 — Idempotency** — Replay S1 with same `operationId` twice. Assert second
call does NOT double-post (balance unchanged on second call).

**S5 — Zero / negative amount** — amount:0 or −50 → 400
(`Amount must be positive`).

**S6 — Same account transfer** — source==target → 400
(`Source and destination treasury accounts must be different`).

**S7 — Inactive / non-treasury account** — use an inactive or non-Cashbox/Bank
code → 400 `TreasuryValidationException`
(`requires active same-tenant cashbox or bank treasury account authority`).

**S8 — Insufficient funds (PreventNegative)** — withdraw more than cashbox
balance → 400 `InsufficientFundsException`.

**S9 — Role gate** — call as non Owner/Admin/Manager (e.g. Cashier) → 403.

**S10 — Cash-in disabled** — `POST /api/treasury/operations/cash-in` → 410 Gone,
`code: TREASURY_INFLOW_WORKFLOW_REQUIRED`.

**S11 — Cash-out disabled** — `POST .../cash-out` → service throws 400
(`Direct treasury cash-out is disabled`).

**S12 — Expense disabled** — `POST .../operations/expense` → 400
(`Direct treasury expense posting is disabled`).

**S13 — Admin settle disabled** — `POST /api/admin/accounting/treasury/settle`
→ 410 Gone, `code: SETTLEMENT_AUTHORITY_REQUIRED`.

**S14 — Dashboard** — `GET /api/treasury/dashboard?from=&to=` → 200 with
`cashboxBalance`, `bankBalance`, `totalCashAndBank`, `revenue`, `expenses`,
`netProfit`, `clearingAccounts`, `totalPendingClearing`.

**S15 — Movements** — `GET /api/treasury/movements/{code}?from=&to=` → list of
`TreasuryMovementDto` with `debit`, `credit`, `balanceAfter`, `sourceType`.

**S16 — Accounts dropdown** — `GET /api/treasury/accounts` → options with
`accountType`, `isClearing`, `preventNegative`.

**S17 — Cashbox opens with zero float** — `POST /api/cashbox/{id}/open` with
`openingBalance != 0` → 400 `OPENING_FLOAT_TRANSFER_REQUIRED`.

### Failure cases / business validation
- Legacy `CreateTransaction` endpoint removed — no direct legacy writes.
- All money movement must be reflected in the GL; if a treasury op succeeds, a
  balanced `JournalEntry` with the matching `SourceType` must exist
  (`TreasuryTransfer`/`TreasuryDeposit`/`TreasuryWithdraw`/`BankDeposit`/
  `ClearingSettlement`).

---

## 14. Domain Ownership Matrix

| Capability | Owner | Evidence |
|---|---|---|
| Treasury Identity (mapping) | Treasury | `TreasuryAccount`, `TenantAccountSeeder.SeedTreasuryAccountsAsync` |
| Cashbox→Ledger provisioning | Treasury | `CashboxLedgerAccountService` |
| Cash Movement (internal) | Treasury (router) → Accounting (writer) | `TreasuryOperationService` → `FinancialPostingService` |
| Transfer | Treasury (auth) + Accounting (post) | `TreasuryOperationService.TransferAsync` / `TreasuryTransferService` |
| Settlement | Accounting (certified) — Treasury engine exists, manual disabled | `TreasuryTransferService.SettleClearingAsync` + 410 admin endpoint |
| Posting | Accounting (sole) | `FinancialPostingService.PostJournalAsync` |
| Ledger | Accounting | `JournalEntry`/`LedgerEntry`/`Account` |
| Inflow/Outflow Authority | Accounting | `ValidateTreasuryInflowAuthorityAsync`/`OutflowAuthorityAsync` |
| Reports / Dashboard | Accounting (derived) | `TreasuryReadService.GetDashboardAsync` reads ledger |
| Funding Authority (source validation) | Accounting | `FinancialPostingService` validators |
| Clearing | Accounting (flag) + Treasury (mapping) | `Account.IsClearing`, `TreasuryAccountType.Clearing` |
| Cross-tenant isolation | Platform (ITenantContext) | `RequiredTenantId` everywhere |

---

## 15. Canonical Pattern

```
Treasury (mapping + internal movement)
        │  Deposit / Withdraw / Transfer / BankDeposit
        ▼
TreasuryOperationService / TreasuryTransferService
        │  (type + active + same-tenant validation)
        ▼
FinancialPostingService  ──(inflow/outflow authority + PreventNegative)──▶ Accounting GL
        │                                                              (JournalEntry/LedgerEntry/Account)
        ▼
Committed Journal  ──▶  TreasuryReadService  ──▶  Dashboard / Balances / Movements / Reports
```

Treasury is the **router**; Accounting is the **writer/authority**; the GL is the
**source of truth**; reads are **live ledger projections**.

---

## 16. Documentation Validation (misconceptions corrected)

❌ *Treasury owns financial truth.*
✅ The GL (`Account`/`JournalEntry`/`LedgerEntry`) owns financial truth. Treasury
is a mapping + movement view (`TreasuryAccount.CurrentBalance => Account.CurrentBalance`).

❌ *Treasury transfers directly update reports.*
✅ Reports derive from the ledger (`TreasuryReadService` reads `LedgerEntries`/
`Accounts` live). Treasury never writes reports.

❌ *Clearing equals settlement.*
✅ Clearing (`IsClearing` transit accounts) is money-in-transit; settlement is the
certified journal that drains it to the bank. Manual settlement is disabled;
production drains come from `GatewaySettlement`/`CODSettlement` postings.

❌ *Treasury can create or destroy money via cash-in/cash-out/expense.*
✅ Those three operations are deliberately disabled
(`TreasuryOperationService.cs:242-290`); `cash-in` returns HTTP 410, `settle`
returns 410. Only certified workflows may change cashbox/bank balances.

❌ *A `PhaseTOJ_BlockLegacyTreasuryWrites.sql` trigger blocks Treasury writes.*
✅ That file does not exist. Only `20260208180000_BlockLegacyCashboxVisaWrites.cs`
exists (Cashbox + Visa), and it is off by default
(`LCR_ENFORCEMENT_ENABLED=false`). `TreasuryTransactions` is legacy read-only by
convention, not by a trigger.

❌ *Treasury emits domain events / outbox messages.*
✅ No Treasury events/outbox exist. Posting is synchronous through
`FinancialPostingService`.

---

## 17. Final Architecture Conclusion

**Treasury Authority** — Treasury owns the *mapping* of physical cashboxes/banks/
clearing accounts to GL nodes and the *authorization* of internal money movement
(active same-tenant Cashbox/Bank, type-restricted pairs). It does **not** own
money creation, outflow, or settlement initiation.

**Financial Authority** — Resides in Accounting: `FinancialPostingService`
validates inflow/outflow against certified sources and enforces `PreventNegative`.
Treasury cannot bypass this; it can only call `PostJournalAsync`.

**Runtime Flow** — HTTP (Owner/Admin/Manager, rate-limited) →
`TreasuryOperationService`/`TreasuryTransferService` (validation) →
`PostingRequest` → `FinancialPostingService` (authority + balance) → GL commit →
live ledger reads via `TreasuryReadService`.

**Aggregate Ownership** — Treasury owns `TreasuryAccount` + cashbox provisioning
orchestration. Accounting owns `Account`/`JournalEntry`/`LedgerEntry` and the
posting engine. Reports are downstream read models.

**Transaction Model** — `Serializable` posting transaction (or joined ambient);
cashbox provisioning uses `ReadCommitted` + 2× retry with re-check-inside-tx.
Idempotency keys prevent double-posting. Rollback on any failure. No Treasury
outbox.

**Projection Strategy** — Treasury reads are **live SQL against the ledger**
(`TreasuryReadService`); no Mongo/Redis/ES read model for treasury. Dashboard,
balances, and movements are recomputed from `Accounts`/`LedgerEntries` on demand.
The only intentional non-ledger artifact is the Daily Close cash-drawer
reconciliation report (operational, not the GL truth).

**Net:** Treasury is a ledger-backed movement-and-mapping bounded context — a
router on top of the accounting GL, with cash-in/out/expense and manual
settlement deliberately disabled so that all cashbox/bank balance changes trace
to certified business events.

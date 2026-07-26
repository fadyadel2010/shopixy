# PASS P1-EE POS Financial & Treasury Authority Certification Findings

Date: 2026-06-08

## Verdict

PASS P1-EE is production-certifiable for inspected POS Financial & Treasury Authority runtime paths after hardening.

Financial and treasury authority remains PostgreSQL-owned through current tenant SQL state, active `TreasuryAccounts`, double-entry `LedgerJournalEntries`, `LedgerLines`, and `LedgerAccounts.CurrentBalance`. DTO values, route account codes, frontend balances, legacy bank/cashbox transaction rows, and displayed totals are evidence or compatibility read models only.

## Cashbox Authority Findings

Cashboxes remain tenant-bound through `Cashbox.TenantId`, tenant query filters, trusted tenant context, and cashbox ledger-account resolution. Cashbox display balances are built from `ITreasuryReadService` ledger balances, not `Cashbox.CurrentBalance` or `ExpectedBalance`.

Cashbox open, opening-float, manual cash-in, and manual cash-out paths route money mutation through `ITreasuryOperationService` and `IFinancialPostingService`. Deleted/inactive cashboxes cannot become treasury posting authority unless an active same-tenant `TreasuryAccount` linked to an active ledger account exists.

## Bank Authority Findings

Banks remain tenant-bound on inspected controller reads/writes through explicit `TenantId` predicates. Runtime hardening blocks bank creation with nonzero opening balance DTOs, disables the legacy `POST /api/banks/opening-balances` mutation path with `410 Gone`, and prevents deleting active banks or banks with financial history.

Bank money authority for new treasury mutations is the active same-tenant `TreasuryAccount` plus linked active ledger account, not `BankTransaction` rows.

## Transfer Authority Findings

Treasury transfers are ledger-posted, idempotent by operation id, balanced, and atomic. Runtime hardening now requires source and destination account codes to resolve to active same-tenant cashbox/bank `TreasuryAccounts`, and rejects same-account no-op transfer/deposit/withdraw journals.

The posting boundary rechecks idempotency inside the transaction, validates balanced lines, validates active same-tenant accounts, prevents negative balances for `PreventNegative` accounts, updates `LedgerAccounts.CurrentBalance`, creates `LedgerLines`, and commits as one SQL transaction.

## Expense Authority Findings

Expense creation/update/delete/approval is idempotency-keyed and accounting-outbox-backed. Approved expenses enqueue accounting mutations by mutation version and do not directly mutate treasury balances.

Treasury expense posting now requires `PaidFromAccountCode` to be an active same-tenant cashbox/bank treasury account and `ExpenseAccountCode` to be a ledger `Expense` account before a journal can be posted.

## Income Authority Findings

Treasury cash-in posting is idempotent and double-entry. Runtime hardening now requires the receiving target to be an active same-tenant cashbox/bank treasury account and rejects debit-normal expense/COGS accounts as cash-in credit accounts, preventing income replay from disguising an expense-account mutation as cash inflation.

## Financial Balance Findings

Authoritative balance truth is `LedgerAccounts.CurrentBalance` updated atomically with posted `LedgerLines`. Treasury reads, cashbox responses, dashboard balances, and movement histories use ledger accounts and posted journal lines. Legacy `Cashbox.CurrentBalance`, `Cashbox.ExpectedBalance`, `BankTransaction` running balances, and frontend totals are not write authority.

## Concurrency Findings

`FinancialPostingService` uses idempotency keys, a transaction-scoped idempotency recheck, serializable transactions when it owns the transaction, ordered account loading, EF `xmin` account concurrency, negative-balance projection checks, and unique `(TenantId, IdempotencyKey)` journal constraints. Concurrent replay converges to the existing journal or fails closed instead of double-posting.

## Adversarial Findings

DTO account-code tampering cannot select cross-tenant accounts because account resolution and treasury-account role checks require the trusted tenant id. Route tampering on banks/cashboxes is tenant-filtered. Replay is bounded by operation id/idempotency keys. Legacy bank opening-balance DTOs can no longer mint `BankTransaction` money outside the ledger.

## Runtime Hardening Performed

- `TreasuryOperationService` now validates active same-tenant cashbox/bank `TreasuryAccount` authority before deposit, withdraw, transfer, expense, cash-in, and cash-out postings.
- Treasury transfer/deposit/withdraw reject same-account postings.
- Treasury expense postings require a ledger `Expense` account.
- Treasury cash-in rejects debit-normal expense/COGS credit accounts.
- `BanksController` rejects bank creation with nonzero opening balances, disables legacy bank opening-balance writes, and blocks deletion of active banks or banks with financial history.

## Runtime Files Modified

- `PointOfSale.Infrastructure/Treasury/TreasuryOperationService.cs`
- `PointOfSale.API/Controllers/BanksController.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/PosFinancialTreasuryAuthorityCertificationTests.cs`

## Architecture Updates

PASS P1-EE truth, risk, hardening, and freeze entries were added to `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, and `.ai-context/CODEX_RULES.md`.

## Deferred

- CommerceHub Financial Authority
- Affiliate Settlement Authority
- Accounting Authority

## Remaining Debt

Operational treasury chaos proof, production reconciliation dashboards, alert packs, deployment proof for ledger immutability triggers, and a staged migration away from legacy `BankTransactions` statement reads remain launch-readiness debt. Bank statement compatibility reads still exist, but new opening-balance writes are disabled and new treasury mutation authority is ledger-only.

## Required Answers

- Can Tenant A access Tenant B cashboxes? No, inspected runtime uses trusted tenant context and tenant-owned SQL filters.
- Can Tenant A access Tenant B banks? No, inspected bank runtime uses explicit `TenantId` predicates.
- Can transfers execute twice? No for the same operation id; ledger idempotency returns the existing journal.
- Can transfers duplicate money? No, postings are balanced double-entry journals and treasury accounts are SQL-validated.
- Can failed transfers partially apply? No, journal lines and account balance updates commit in one SQL transaction.
- Can expenses execute twice? No for the same idempotency key/mutation version; expense and accounting paths are idempotent.
- Can income execute twice? No for the same operation id; treasury cash-in uses ledger idempotency.
- Can balances drift from transaction history? Runtime balance truth is ledger-owned; legacy bank statement rows are compatibility reads, not authority.
- Can race conditions create financial drift? Inspected ledger paths use serializable/idempotent/concurrency-protected posting controls.
- Were runtime code changes required? Yes.
- Is Financial & Treasury Authority production-certifiable? Yes, for inspected POS cashbox, bank, transfer, expense, income, and treasury-balance paths after this hardening.

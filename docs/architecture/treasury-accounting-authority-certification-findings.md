# PASS T7 Treasury Accounting Authority Certification

Date: 2026-06-12

Verdict: Treasury Accounting Authority is production-certifiable for inspected runtime paths after journal insertion, replay payload, reversal workflow, and source-less adjustment hardening.

## Accounting Findings

PostgreSQL `LedgerJournalEntries`, `LedgerLines`, tenant-owned ledger accounts, and current certified SQL payment, refund, payout, settlement, affiliate, POS, and treasury source rows own accounting truth. Accounting derives from those authorities; it does not own cash, inflow, outflow, reservation, settlement, or reconciliation truth.

`ApplicationDbContext` now rejects direct journal and ledger-line insertion outside `LedgerMutationScope`. Runtime code cannot bypass `FinancialPostingService` to place unbalanced, source-less, tenant-forged, or account-balance-detached rows into financial history.

## Journal Findings

Journal number and tenant/idempotency identities remain unique. Journal source, date, description, and financial lineage are immutable after posting. Deletion is rejected. Direct insertion is rejected outside posting authority.

Manual journals are not exposed through HTTP or application services. The dormant inventory-adjustment method previously accepted caller-supplied value without a SQL adjustment row; it now fails closed until an authoritative SQL adjustment workflow exists.

## Posting Findings

`FinancialPostingService` requires tenant identity, source identity, source type, description, idempotency key, active same-tenant accounts, at least two single-sided positive lines, and exact debit/credit balance before persistence. Cash-affecting postings retain T1-T3 SQL source and cumulative amount checks.

## Double-Entry Findings

Every certified posting validates total debits equal total credits before mutation. Database line constraints preserve single-sided positive values. Direct line insertion is blocked, so a caller cannot append report-only value detached from account balances.

## Reversal Findings

Ordinary `PostJournalAsync` calls cannot submit `SourceType = "Reversal"`. Reversals must use `ReverseJournalAsync`, which swaps original lines, uses deterministic idempotency, records bidirectional lineage, marks the original with a reason, and commits under serializable posting authority.

Original and reversing journals remain in history. Financial and treasury reads include both, so reversal offsets value without erasing or rewriting the original.

## Recovery Findings

Accounting outbox replay reloads current SQL business rows before posting. Journal idempotency is checked before and inside the transaction. T7 also binds an existing key to the original tenant, source type, source id, journal date, and ordered debit/credit payload; conflicting replay fails.

## Financial Statement Findings

Trial balance, general ledger, P&L, balance sheet, cash flow, clearing aging, and account summaries derive from tenant-scoped SQL ledger lines. Reversed originals and reversal journals are both included. Analytics and projections cannot write or override journal or statement truth.

## Cross-System Findings

CommerceHub, Affiliate, Storefront, POS, and Service Marketplace cannot insert accounting rows directly. Their certified workflows may request accounting only after current SQL source authority is loaded. Service Marketplace has no inspected financial posting workflow.

## Isolation Findings

Posting account loads require the posting tenant. Journal and line query filters remain tenant-scoped, and worker paths that bypass filters use explicit tenant predicates. Financial report APIs derive scope from trusted tenant context.

## Historical Findings

Journal and ledger-line history is append-only. Only reversal status fields may change, and only under posting authority. Reversals append compensating history rather than rewriting originals.

## Adversarial Findings

- Direct journal and ledger-line forgery is rejected.
- Unbalanced and dual-sided lines are rejected before persistence.
- Conflicting replay under a consumed idempotency key is rejected.
- Direct reversal posting is rejected.
- Caller-supplied inventory adjustment value cannot create accounting truth.
- DTO, route, projection, analytics, cache, and stale-event state cannot write ledger history.
- Tenant/account guessing fails through explicit tenant predicates.
- Accounting cannot create treasury money because T1-T3 cash source and amount controls remain enforced.

## Runtime Hardening Performed

- Required posting authority for new `JournalEntry` and `LedgerEntry` persistence.
- Required non-empty tenant and source identities.
- Bound idempotent replay to original source and ordered line payload.
- Reserved reversal creation for `ReverseJournalAsync`.
- Disabled source-less inventory adjustment accounting.

## Exact Runtime Files Modified

- `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs`
- `PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/TreasuryAccountingAuthorityCertificationTests.cs`

Focused T1-T7 and posting-engine result: 35 passed, 0 failed.

## Architecture Updates

- Added this T7 findings document.
- Updated all five `.ai-context` authority files.

## Required Answers

1. Who owns treasury accounting authority? PostgreSQL journal, ledger-line, account, and authoritative SQL source lineage under `FinancialPostingService`.
2. Can accounting create money? No on inspected runtime paths.
3. Can journals be forged? No on inspected runtime paths.
4. Can replay duplicate postings? No; exact retries converge and conflicting reuse fails.
5. Can reversals erase history? No.
6. Can analytics override accounting truth? No.
7. Can tenant leakage occur? No on inspected posting and reporting paths.
8. Were runtime code changes required? Yes.
9. Is Treasury Accounting Authority production-certifiable? Yes, for inspected runtime authority paths.

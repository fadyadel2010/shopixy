# PASS T3 Treasury Outflow Authority Certification

Date: 2026-06-12

Verdict: Treasury Outflow Authority is production-certifiable for the inspected runtime paths after central outflow-lineage, cumulative source-capacity, and manual spend hardening.

## Outflow Findings

Treasury cash reduction is owned by current PostgreSQL liability, refund, payout, approved expense, purchase-payment, and reversal truth plus balanced ledger posting against active same-tenant cashbox/bank accounts. DTOs, routes, source labels, projections, caches, analytics, recovery payloads, and external evidence cannot independently authorize a cash reduction.

`FinancialPostingService` now detects every net decrease across cashbox/bank treasury accounts before mutation. Aggregate-neutral treasury transfers do not consume outflow authority; every net reduction must match a certified SQL source.

## Liability Findings

Purchase cash payments are bounded by completed purchase-invoice paid amount. Supplier payments are bounded by the immutable same-tenant supplier-payment row. Approved POS expenses are bounded by their SQL amount and approval state. POS sales refunds are bounded by same-tenant sales-return totals. Storefront refunds are bounded by cumulative SQL `OrderRefund` rows.

CommerceHub cash outflow accepts only persisted cash-bucket debit transactions classified as refund or payout. Affiliate payout accounting is bounded by same-tenant transfer-evidence allocations.

## Duplicate Findings

Exact retries converge through tenant-scoped journal idempotency. T3 additionally sums prior treasury decreases for the same source type and source identity and rejects cumulative cash reduction above current SQL authority. Alternate idempotency keys cannot pay one liability, payout allocation, refund, or approved expense twice.

## Manual Withdrawal Findings

Direct treasury expense posting and generic cash-out are disabled at the service boundary. Operators cannot select an arbitrary debit account and create treasury spending. Existing treasury withdraw/deposit/transfer operations require both sides to be active same-tenant treasury accounts and therefore move existing cash without reducing aggregate treasury cash.

## Refund Findings

POS refund cash reductions require same-tenant `SalesReturn` authority and cannot exceed the return total. Storefront refund cash reductions require current same-tenant `OrderRefund` rows in pending/posted financial state and are cumulatively bounded. CommerceHub refund outflow requires a persisted CommerceHub cash-bucket refund debit produced by the certified M4 workflow.

## Cross-System Findings

CommerceHub, Affiliate, Storefront, POS, and Service Marketplace events cannot directly reduce treasury cash. Their certified liability/refund/payout workflows may create SQL source truth, but accounting independently reloads that truth before posting. No inspected Service Marketplace financial runtime can create treasury outflow.

## Transfer-Evidence Findings

CommerceHub transfer evidence remains bounded by the certified M5 payout/refund workflows. Affiliate transfer evidence remains HMAC-verified, globally unique, tenant/currency/amount bound, and allocated before accounting. T3 consumes the persisted allocation amount, not a DTO reference or provider payload.

## Isolation Findings

All treasury accounts and tenant-owned source rows must match the posting tenant. Supplier payments, purchases, returns, expenses, storefront refunds, and affiliate allocations use explicit tenant predicates. Foreign source IDs cannot reduce local cash.

## Historical Findings

Outflow journals and ledger lines remain append-only. Reversals require original-journal lineage and are bounded by the original cash increase being reversed. Paid liabilities and completed payouts are not rewritten or resurrected by projections, recovery jobs, or stale events.

## Adversarial Findings

- Forged source types and source labels fail closed.
- Duplicate payout, liability, refund, or expense posting under a new idempotency key is amount-bounded.
- DTO and route account tampering cannot create generic cash-out.
- Foreign-tenant liability IDs cannot reduce local cash.
- Transfer evidence cannot be reused beyond its persisted allocation.
- Recovery, analytics, cache, projection, and stale-event payloads cannot become outflow authority.

## Runtime Hardening Performed

- Added central net-treasury-decrease detection in `FinancialPostingService`.
- Added SQL authority reloads for purchases, supplier payments, sales returns, approved expenses, storefront refunds, CommerceHub refund/payout cash transactions, affiliate payout allocations, and reversals.
- Added cumulative source-capacity enforcement across prior outflow journals.
- Disabled generic direct treasury expense and cash-out service methods.

## Exact Runtime Files Modified

- `PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs`
- `PointOfSale.Infrastructure/Treasury/TreasuryOperationService.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/TreasuryOutflowAuthorityCertificationTests.cs`

Focused T1/T2/T3 result: 13 passed, 0 failed. API build completed with 0 errors; existing repository warnings remain.

## Architecture Updates

- Added this T3 findings document.
- Updated all five `.ai-context` authority files with the T3 truth and freeze.

## Required Answers

1. Who owns treasury outflow authority? Current PostgreSQL liability, refund, payout, approved expense, purchase-payment, transfer-allocation, and reversal truth plus balanced ledger posting.
2. Can outflows be forged? No on inspected runtime paths; source labels, DTOs, routes, projections, caches, analytics, and evidence without matching SQL authority fail closed.
3. Can replay reduce cash twice? No; journal idempotency and cumulative source-capacity enforcement prevent duplicate cash reduction.
4. Can withdrawals bypass liability? No net treasury reduction can use the generic expense or cash-out methods; aggregate-neutral treasury transfers do not spend cash.
5. Can tenant leakage occur? No inspected path can use foreign treasury accounts or foreign liability/refund/payout sources to reduce local cash.
6. Were runtime code changes required? Yes.
7. Is Treasury Outflow Authority production-certifiable? Yes, for the inspected runtime authority paths.

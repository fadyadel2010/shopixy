# PASS P1-EF POS Customer & Supplier Authority Certification Findings

Date: 2026-06-08

## Verdict

POS Customer & Supplier Authority is production-certifiable for the inspected runtime paths after hardening. Current tenant-owned PostgreSQL Customer, Supplier, AccountLedger, CustomerTransaction, SupplierTransaction, SupplierPayment, PartyLedgerAdjustment, SalesRequestIdempotency, and current SQL state are authority. Frontend balances, DTO balances, outstanding-balance cards, SOA totals, route ids, query ids, and cached party state are evidence only.

## Runtime Hardening Performed

- `LedgerService` now exposes `ApplyCustomerTransaction` and applies duplicate opening-balance rejection to both Customer and Supplier opening balances under the same locked SQL party row.
- `CustomersController.SaveCustomerOpeningBalances` now normalizes and hashes the request, claims durable financial idempotency, rejects conflicting duplicate entries, uses locked ledger authority, writes `CustomerTransactions` from SQL-computed balance-after, and replays stored responses instead of re-executing money movement.
- `StatementOfAccountController.CreateOpeningBalance` now routes Customer opening balances through durable idempotency and `ApplyCustomerTransaction`, matching the stronger Supplier path.
- `StatementOfAccountController.CreateAdjustment` now scopes adjustment idempotency keys by party type plus tenant for both Customers and Suppliers.
- `StatementOfAccountController.GetOutstandingBalances` now recomputes outstanding balances from `AccountLedgers` SQL aggregation instead of reading stored `Customer.Balance` or `Supplier.Balance` shadows.
- `ApplicationDbContext` and migration `20260608120000_PosCustomerSupplierAuthorityHardening` add live unique SQL boundaries for Customer opening-balance ledger and transaction rows.

## Customer Authority Findings

- Tenant isolation is enforced by tenant query filters, trusted tenant context, and `TenantSaveChangesInterceptor`; customer writes set `TenantId` from `RequiredTenantId`.
- Tenant A cannot access Tenant B customers through inspected controller paths because tenant-owned queries are filter-scoped and ledger mutations lock by current tenant.
- Deleted customers are excluded from inspected opening-balance and adjustment mutations.
- Customer balance truth is `AccountLedgers` aggregation plus SQL-current row locks. `Customer.Balance` is a compatibility shadow maintained by `LedgerService`, not display or mutation authority.

## Supplier Authority Findings

- Tenant isolation is enforced by tenant query filters, trusted tenant context, and tenant-owned supplier/payment/account-ledger rows.
- Tenant A cannot access Tenant B suppliers through inspected controller paths because tenant-owned queries are filter-scoped and supplier ledger mutations lock by current tenant.
- Deleted suppliers are excluded from inspected opening-balance, payment, and adjustment mutations.
- Supplier payment and opening-balance paths remain replay-safe through `SalesRequestIdempotency`, request hashes, outbox idempotency, and SQL uniqueness.
- Supplier has soft deletion but no separate inactive state; this remains inherited lifecycle debt from P1-EC.

## Opening Balance Findings

- Customer and Supplier opening balances are one-time mutations under locked SQL party authority.
- Supplier opening balances already had application and SQL uniqueness guards; this pass added the missing Customer equivalent.
- Replayed opening-balance requests converge through durable idempotency and stored response snapshots.
- Conflicting duplicate entries in the same batch fail closed before mutation.

## Payment Authority Findings

- Supplier payments require an `Idempotency-Key`, validate positive amount and concrete funding method, check current treasury account availability, write `SupplierPayment`, apply supplier ledger effects, write `SupplierTransaction`, and enqueue accounting outbox before commit.
- Customer payment-at-sale was certified in P1-ED and is not recertified here; no standalone customer payment endpoint was certified in this pass.
- Deferred Authority Domain: sales, purchases, and treasury funding execution remain governed by P1-ED, P1-EC, and P1-EE respectively.

## Outstanding Balance Findings

- Outstanding-balance response authority now comes from SQL `AccountLedgers` aggregation.
- Stored `Customer.Balance` and `Supplier.Balance` shadows no longer decide outstanding-balance inclusion, totals, or balance type in the inspected SOA endpoint.
- Stale stored balances can be detected by `FinancialIntegrityService` and rebuilt through `IPartyBalanceMaintenanceService`, but display truth now follows ledger aggregation.

## Statement Of Account Findings

- SOA transaction lists, summary totals, opening balance, closing balance, and balance type are derived from tenant-scoped `AccountLedgers`.
- Running balances remain stored ledger row values, with rebuild support available through `LedgerService.RebuildPartyBalanceAsync`.
- SOA totals can no longer diverge merely because party shadow balances drift; the inspected SOA/outstanding reads do not use those shadows as authority.

## Adjustment Authority Findings

- Customer and Supplier adjustments require `Idempotency-Key`, request-hash matching, valid adjustment transaction type, positive amount, active same-tenant party existence, durable `PartyLedgerAdjustment`, party ledger mutation, party transaction row, and accounting outbox.
- Adjustment replay collapses through `SalesRequestIdempotency`; database uniqueness on `PartyLedgerAdjustments(TenantId, IdempotencyKey)` provides an additional SQL boundary.
- Adjustments cannot target foreign tenants through inspected paths because party lookup and ledger mutation are tenant-scoped.

## Concurrency Findings

- Customer and Supplier ledger writes lock the current party row before balance mutation.
- Opening-balance replay is blocked by idempotency plus ledger duplicate checks; Customer now also has SQL unique indexes on live opening-balance ledger and transaction rows.
- Supplier payments and party adjustments use durable idempotency claims, blocking concurrent duplicate execution for the same key.
- Remaining debt is production-like PostgreSQL chaos evidence for simultaneous customer/supplier opening-balance, adjustment, supplier-payment, and rebuild races.

## Adversarial Findings

- Route tampering and party-id guessing fail closed through tenant-scoped party queries and locked ledger mutation.
- DTO balance tampering has no authority; mutation paths recompute balance-after from current SQL ledger state.
- SOA and outstanding-balance manipulation through stale frontend totals is blocked because reads aggregate from SQL `AccountLedgers`.
- Deleted-party usage is blocked in inspected opening-balance, payment, and adjustment paths.
- Deferred Authority Domains: Sales, Purchases, Treasury, Storefront Orders, CommerceHub Orders, and Affiliate Payouts were intentionally not recertified in this pass.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/CustomersController.cs`
- `PointOfSale.API/Controllers/StatementOfAccountController.cs`
- `PointOfSale.API/Services/ILedgerService.cs`
- `PointOfSale.API/Services/LedgerService.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Migrations/20260608120000_PosCustomerSupplierAuthorityHardening.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/PosCustomerSupplierAuthorityCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Remaining Debt

- Staging and production migration application proof for `20260608120000_PosCustomerSupplierAuthorityHardening`.
- Production reconciliation monitoring for party ledger drift, missing adjustment journals, and outstanding-balance anomalies.
- PostgreSQL-backed concurrent chaos coverage for simultaneous party opening balances, adjustments, supplier payments, and balance rebuilds.
- Supplier lacks a separate inactive/deactivated lifecycle state; deleted suppliers are blocked, but inactive supplier policy remains future lifecycle work.
- No standalone customer payment-application endpoint was certified in this pass.

## Required Answers

- Can Tenant A access Tenant B customers? No.
- Can Tenant A access Tenant B suppliers? No.
- Can opening balances execute twice? No for inspected Customer and Supplier paths after hardening.
- Can payments execute twice? No for inspected Supplier payment path; it is idempotency-bounded. Customer payments-at-sale remain governed by P1-ED.
- Can adjustments execute twice? No; inspected adjustments require durable idempotency and request-hash matching.
- Can balances drift? Inspected display and SOA authority now derive from SQL ledger aggregation; stored shadow drift cannot become outstanding-balance truth.
- Can SOA diverge from financial truth? Not through inspected SOA/outstanding paths; they derive from tenant-scoped `AccountLedgers`.
- Can race conditions create balance drift? Not in inspected opening-balance, supplier-payment, and adjustment paths after row locks, idempotency, and SQL uniqueness hardening.
- Were runtime code changes required? Yes.
- Is Customer & Supplier Authority production-certifiable? Yes, for inspected Customers, Suppliers, Opening Balances, Supplier Payments, Outstanding Balances, SOA, and Adjustments.

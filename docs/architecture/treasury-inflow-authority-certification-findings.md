# PASS T2 Treasury Inflow Authority Certification

Date: 2026-06-12

Verdict: Treasury Inflow Authority is production-certifiable for the inspected runtime paths after central posting-lineage, manual inflow, deposit replay, and treasury-role hardening.

## Collection Findings

Treasury inflow is owned by PostgreSQL payment/accounting source rows plus the balanced journal and active same-tenant cashbox/bank treasury accounts that receive the posted debit. Provider callbacks, webhook payloads, DTOs, routes, projections, caches, and analytics are evidence only.

`FinancialPostingService` now detects every net increase to cashbox/bank treasury accounts before mutation. It permits that increase only when current SQL proves a certified source: completed paid POS sale, collected/settled storefront order payment, current settlement batch, CommerceHub financial transaction, or a bounded reversal of an existing tenant journal.

Provider evidence alone cannot create cash. Unresolved provider money remains suspense/clearing evidence until payment and settlement authority creates the SQL source accepted by accounting.

## Duplicate Findings

Journal idempotency still collapses exact retries before and inside the serializable transaction. T2 additionally sums prior cash increases for the same authoritative source and rejects cumulative posting above the current SQL-authorized amount. A valid payment ID, sale ID, settlement ID, financial transaction ID, or reversal source therefore cannot be reused under a different idempotency key to inflate treasury.

Public treasury operation DTOs now require caller-stable operation IDs. Admin cashbox-to-bank deposits require a non-empty deposit ID, preserving replay-safe deposit lineage.

## Lineage Findings

Cash inflow journals are bounded by current source amount and tenant:

- POS cash collection derives from same-tenant completed `SalesInvoice` paid amount.
- Storefront collection derives from same-tenant collected/settled/refunded SQL `Order` payment truth.
- Gateway/COD bank inflow derives from the current same-tenant `PaymentSettlementBatch` identified by settlement reference.
- CommerceHub cash effects derive from persisted `CommerceHubFinancialTransaction` authority.
- Reversal inflow is bounded by the original journal's cash outflow.

Ledger source labels without matching SQL rows fail closed. Existing T1 ledger mutation, append-only history, idempotency, and tenant co-ownership controls remain authoritative.

## Manual Deposit Findings

Direct treasury cash-in is retired at both service and HTTP boundaries. Cashbox manual cash-in cannot post. Cashboxes must open with zero new cash, and the one-time cashbox opening-balance endpoint cannot create cash. Existing authoritative cash may be moved into a cashbox or bank through treasury transfers.

Manual clearing-to-bank settlement is retired because it bypassed certified payment settlement lineage. Cashbox-to-bank deposit remains a transfer of existing cash, requires exact cashbox/bank treasury roles, and cannot increase aggregate treasury cash.

## Cross-System Findings

Storefront, CommerceHub, Affiliate, Service Marketplace, and POS DTOs/events cannot directly create cash. Their certified payment or financial workflows may produce SQL source rows and accounting work, but the central posting boundary independently revalidates cash inflow authority before ledger mutation.

Service Marketplace has no inspected revenue/payment runtime capable of creating treasury inflow.

## Recovery Findings

Recovery and reconciliation payloads cannot create treasury cash from stale events or provider evidence. Any path that reaches `FinancialPostingService` must still present current SQL source lineage, tenant ownership, source amount capacity, balanced lines, and posting idempotency. Manual clearing settlement is not available as a recovery shortcut.

## Isolation Findings

All posting accounts must belong to the request tenant. Inflow source queries also require the same tenant where the source model is tenant-owned. Raw-ID treasury transfers now require active same-tenant treasury mappings and exact cashbox, bank, or clearing roles. Foreign payment rows and foreign treasury mappings cannot create local cash.

## Historical Findings

Posted source lineage remains append-only. Prior cash increases are included when enforcing source amount capacity, so stale events cannot resurrect already-consumed inflow authority. Reversals preserve original and reversing journal history and are bounded by the original journal's actual cash effect.

## Adversarial Findings

- Webhook replay cannot create cash without current payment/accounting source truth.
- A forged provider/source label is rejected.
- A legitimate source ID cannot be over-posted with a new idempotency key.
- DTO and route account tampering cannot bypass exact treasury roles.
- Manual cash-in and positive cashbox opening balances are rejected.
- Manual settlement and recovery shortcuts cannot convert arbitrary clearing evidence into bank cash.
- Tenant guessing cannot bind foreign payment or treasury lineage to local cash.

## Runtime Hardening Performed

- Added central net-cash-increase detection and SQL source validation in `FinancialPostingService`.
- Added cumulative source-amount enforcement across prior cash inflow journals.
- Retired direct treasury cash-in at service and HTTP boundaries.
- Required zero-new-cash cashbox opening and opening-balance behavior.
- Retired manual clearing settlement.
- Added exact same-tenant treasury role validation to raw-ID transfer/deposit/settlement service methods.
- Required stable operation/deposit IDs on public treasury mutation routes.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/AdminAccountingController.cs`
- `PointOfSale.API/Controllers/CashboxController.cs`
- `PointOfSale.API/Controllers/TreasuryController.cs`
- `PointOfSale.API/DTOs/TreasuryDTOs.cs`
- `PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs`
- `PointOfSale.Infrastructure/Accounting/TreasuryTransferService.cs`
- `PointOfSale.Infrastructure/Treasury/TreasuryOperationService.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/TreasuryInflowAuthorityCertificationTests.cs`
- Updated `TreasuryCashAuthorityCertificationTests`.
- Updated `PosFinancialTreasuryAuthorityCertificationTests`.
- Updated `TreasuryTransferServiceTests`.

Focused T1/T2/treasury result: 11 passed, 0 failed.

## Required Answers

1. Who owns treasury inflow authority? Current SQL payment/accounting source truth plus balanced ledger posting into active same-tenant cashbox/bank treasury accounts.
2. Can inflows be forged? No on inspected paths; source labels, DTOs, routes, callbacks, projections, and provider evidence fail without matching bounded SQL authority.
3. Can replay create inflows? No; journal idempotency and cumulative source-amount enforcement prevent duplicate cash.
4. Can manual deposits create unauthorized cash? No; direct cash-in, positive cashbox opening money, and manual clearing settlement are retired. Cashbox-to-bank deposit only moves existing cash.
5. Can tenant leakage occur? No inspected path can use foreign payment or treasury lineage to create local cash.
6. Were runtime code changes required? Yes.
7. Is Treasury Inflow Authority production-certifiable? Yes, for the inspected runtime authority paths.

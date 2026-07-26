# PASS P1-EB POS Warehouse & Inventory Authority Certification

Date: 2026-06-07

## Verdict

PASS P1-EB is production-certifiable for the inspected POS Warehouse, Inventory, Movement,
Transfer, Reservation, Adjustment, and concurrency runtime boundaries after hardening.
PostgreSQL `ProductVariantStocks`, `InventoryReservations`, `StockMovements`, Warehouses, and
durable idempotency records remain authority. Frontend, DTO, route, cache, and projection state are
not inventory authority.

Certification assumes migration `20260607210000_PosWarehouseInventoryAuthorityHardening` is
applied before the hardened application version accepts production traffic.

## Warehouse Authority Findings

- Warehouse ownership is tenant-bound in controller queries and stock mutation lineage checks.
- Stock mutation requires a current active same-tenant Warehouse, ProductVariant, and parent
  Product.
- Inactive warehouses cannot receive opening stock, adjustment, reservation, or transfer mutation.
- No warehouse delete path was found that bypasses stock authority. Soft-deleted or cross-tenant
  warehouses fail current-lineage checks.

## Inventory Authority Findings

- SQL `ProductVariantStocks` is current balance authority per tenant, variant, and warehouse.
- The unique tenant/variant/warehouse key prevents duplicate stock rows.
- SQL constraints require nonnegative physical, reserved, and damaged quantities and require
  physical quantity to be at least reserved quantity.
- Available inventory is derived from physical, reserved, and damaged SQL quantities; clients
  cannot submit an authoritative available balance.
- Opening stock now uses the shared locked stock mutation path and is rejected after any prior
  balance or movement authority exists.

## Inventory Movement Findings

- Every inspected authoritative mutation writes a movement with SQL-computed after-state.
- Direct adjustments now require a durable idempotency key and reject payload reuse.
- Opening movements have deterministic identities.
- Sales and purchase invoice inventory reverts now append compensating movements instead of
  deleting history; only their inventory boundary was reviewed.
- `StockMovement` update and delete are rejected in `ApplicationDbContext` and by a PostgreSQL
  trigger, making movement authority append-only across application and direct SQL paths.

## Transfer Authority Findings

- Transfer create, approve, reject, and complete require Owner, Admin, or Manager.
- Transfer creation rejects nonpositive quantities, same-warehouse transfers, invalid lineage, and
  inactive source or target warehouses.
- Completion locks transfer state and stock rows in deterministic order inside one transaction.
- Source decrement, target increment, movements, and final transfer state commit atomically.
- Concurrent or replayed completion cannot apply a completed transfer twice.

## Reservation Findings

- Reservation mutations lock authoritative stock and cannot reserve beyond SQL available quantity.
- Admin order creation claims durable request idempotency before reservation and commits order,
  reservations, and the idempotency binding transactionally.
- Storefront checkout uses durable checkout idempotency, serializable transactions, deterministic
  stock locking, rollback before retry, and a database active-cart order uniqueness guard.
- Reservation release and commit use cumulative applied quantities, so duplicate delivery cannot
  release or consume more than the reservation authority.

## Adjustment Findings

- Adjustment endpoints require Owner, Admin, or Manager.
- Variant, Product, Warehouse, and Tenant lineage is resolved from SQL.
- Negative-result adjustments fail before commit.
- Direct adjustment replay is now claimed by tenant, operation, request key, and request
  fingerprint; completed responses replay without a second mutation.

## Concurrency Findings

- Stock mutations use row locks and deterministic ordering for multi-row operations.
- Transfers and checkout use explicit transactions; checkout uses serializable authority commits.
- Database constraints remain the final guard against negative or over-reserved stock.
- Focused transfer concurrency and PASS P1-EB architecture freezes passed.

## Adversarial Findings

- Cross-tenant warehouse, product, variant, stock, movement, and reservation identifiers fail
  explicit SQL lineage checks.
- DTO product/variant mismatches are rejected rather than trusted.
- Route IDs do not establish ownership.
- Negative quantities, duplicate opening variants, inactive warehouses, replayed adjustment keys,
  and replayed transfer completion fail closed.
- Cashier authorization can no longer mutate structured transfer lifecycle state.

## Runtime Hardening Performed

- Added durable idempotency and fingerprint conflict detection to direct warehouse adjustments.
- Made opening stock one-time, positive-only, duplicate-free, tenant-explicit, and row-locked.
- Restricted structured transfer lifecycle mutations and validated positive quantities and active
  warehouses.
- Replaced invoice-revert movement deletion with compensating movement entries.
- Added application and PostgreSQL append-only enforcement for `StockMovements`.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/WarehousesController.cs`
- `PointOfSale.API/Controllers/StockTransfersController.cs`
- `PointOfSale.API/Controllers/SalesController.cs`
- `PointOfSale.API/Controllers/PurchasesController.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Migrations/20260607210000_PosWarehouseInventoryAuthorityHardening.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/PosWarehouseInventoryAuthorityCertificationTests.cs`
- Freezes adjustment idempotency, one-time opening stock, transfer authorization and validation,
  append-only movement/reversal behavior, and SQL balance constraints.
- Existing `StockTransfersConcurrencyHardeningTests` remain part of focused verification.

## Verification

- `dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore -nologo -v minimal -m:1`
  passed with zero errors.
- Focused PASS P1-EB and transfer concurrency run passed: 9 tests, 0 failures.
- A broader selected run exposed stale `InventoryServiceTests` lineage fixtures and SQLite
  PostgreSQL-check parsing in two transfer controller tests; those failures occur before the
  hardened runtime paths and are recorded as test-harness debt.

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Remaining Debt

- Apply and prove migration `20260607210000_PosWarehouseInventoryAuthorityHardening` in staging and
  production before deployment.
- Add PostgreSQL-backed chaos tests for simultaneous adjustment, transfer, reserve, release, and
  commit operations.
- Repair stale inventory service fixtures so they create active same-tenant Product, Variant, and
  Warehouse lineage.
- Replace or adapt SQLite transfer controller setup so PostgreSQL-specific constraints do not fail
  during `EnsureCreated`.
- Add production monitoring for rejected stock mutations, idempotency conflicts, reservation
  imbalance, and movement/balance reconciliation.

## Required Answers

- Can Tenant A access Tenant B inventory? **No**, on inspected authority paths.
- Can inventory become negative? **No**, through certified paths; runtime checks, locks, and SQL
  constraints reject it.
- Can available quantity exceed physical quantity? **No**; it is derived from constrained SQL
  quantities.
- Can transfers duplicate stock? **No**; completion is locked, state-gated, and atomic.
- Can failed transfers partially apply? **No**; all balance and movement writes share one
  transaction.
- Can reservations oversell inventory? **No**; reservation uses locked SQL availability and bounded
  cumulative settlement.
- Can adjustments bypass authority? **No**; they require privileged roles, SQL lineage, locked
  balances, and durable idempotency.
- Can race conditions create inventory drift? **No known certified path**; row locks, deterministic
  ordering, transactions, idempotency, and constraints close the inspected races.
- Can deleted/inactive warehouses retain authority? **No** for current inventory mutation paths.
- Were runtime code changes required? **Yes.**
- Is Warehouse & Inventory Authority production-certifiable? **Yes, after the required database
  migration is applied.**
